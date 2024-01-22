using Neo.IO;
using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using DevHawk.DumpNef;
using static DevHawk.DumpNef.Extensions;
using static NefOpt.OpCodeTypes;
using Akka.Actor;

public static class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine($"{System.AppDomain.CurrentDomain.FriendlyName} PATH_TO_YOUR_NEF_FILE");
            return;
        }
        foreach (string pathAndFileName in args)
        {
            string absolutePath = Path.GetFullPath(pathAndFileName);
            DirectoryInfo? directory = Directory.GetParent(absolutePath);
            if (directory == null)
                throw new ArgumentException($"Invalid directory {directory}");
            string name = Path.GetFileNameWithoutExtension(pathAndFileName);
            string nefPath = Path.Combine(directory.FullName, name + ".nef");
            string manifestPath = Path.Combine(directory.FullName, name + ".manifest.json");
            string debugInfoPath = Path.Combine(directory.FullName, name + ".nefdbgnfo");
            if (!File.Exists(nefPath))
                throw new ArgumentException($".nef file {nefPath} not found");
            if (!File.Exists(manifestPath))
                throw new ArgumentException($"manifest {manifestPath} not found");
            if (!File.Exists(debugInfoPath))
                throw new ArgumentException($"debugInfo {debugInfoPath} not found");
            var reader = new MemoryReader(File.ReadAllBytes(nefPath));
            NefFile nef = reader.ReadSerializable<NefFile>();
            ContractManifest manifest = ContractManifest.Parse(File.ReadAllBytes(manifestPath));
            JToken debugInfo = JObject.Parse(Unzip(File.ReadAllBytes(debugInfoPath)))!;
            Script script = nef.Script;
            MethodToken[] methodTokens = nef.Tokens;
            List<(int, Instruction)> addressAndInstructions = script.EnumerateInstructions().ToList();
            string padding = script.GetInstructionAddressPadding();

            //foreach ((int address, Instruction instruction) in addressAndInstructions)
            //    Console.WriteLine(WriteInstruction(address, instruction, padding, methodTokens));

            Dictionary<int, bool> coveredMap = FindCoveredInstructions(nef, manifest, debugInfo);
            int prevUncoveredAddr = -100;
            int prevCoveredAddr = -100;
            bool prevCovered = true;
            foreach((int addr, bool covered) in coveredMap)
            {
                if (addr < script.Length && !covered && prevCovered)
                    Console.WriteLine($"{WriteInstruction(addr, script.GetInstruction(addr), padding, methodTokens)}: {covered}");
                if (addr < script.Length && covered && !prevCovered)
                    Console.WriteLine($"{WriteInstruction(addr, script.GetInstruction(addr), padding, methodTokens)}: {covered}");

                prevCovered = covered;
                if (covered)
                    prevCoveredAddr = addr;
                else
                    prevUncoveredAddr = addr;
            }
        }
    }

    public static Dictionary<int, bool>
        FindCoveredInstructions(NefFile nef, ContractManifest manifest, JToken debugInfo)
    {
        Script script = nef.Script;
        Dictionary<int, bool> coveredMap = new();
        foreach ((int addr, Instruction inst) in script.EnumerateInstructions())
            coveredMap.Add(addr, false);

        Dictionary<int, string> publicMethodStartingAddressToName = new();
        foreach (ContractMethodDescriptor method in manifest.Abi.Methods)
            publicMethodStartingAddressToName.Add(method.Offset, method.Name);

        foreach (ContractMethodDescriptor method in manifest.Abi.Methods)
            CoverInstruction(method.Offset, script, coveredMap);
        // start from _deploy method
        foreach (JToken? method in (JArray)debugInfo["methods"]!)
        {
            string name = method!["name"]!.AsString();  // NFTLoan.NFTLoan,RegisterRental
            name = name.Substring(name.LastIndexOf(',') + 1);  // RegisterRental
            name = char.ToLower(name[0]) + name.Substring(1);  // registerRental
            if (name == "_deploy")
            {
                int startAddr = int.Parse(method!["range"]!.AsString().Split("-")[0]);
                CoverInstruction(startAddr, script, coveredMap);
            }
        }
        return coveredMap;
    }

    public enum StackType
    {
        CALL,
        TRY,
        CATCH,
        FINALLY,
    }

    public static void CoverInstruction(int addr, Script script, Dictionary<int, bool> coveredMap)
    {
        Stack<(int returnAddr, int finallyAddr, StackType stackType)> stack = new();
        stack.Push((-1, -1, StackType.CALL));
        while (stack.Count > 0)
        {
            if (!coveredMap.ContainsKey(addr))
                throw new BadScriptException($"wrong address {addr}");
            if (coveredMap[addr])
            {// We have visited the code. Skip it.
                (addr, _, _) = stack.Pop();
                continue;
            }
            Instruction instruction = script.GetInstruction(addr);
            coveredMap[addr] = true;

            if (instruction.OpCode == OpCode.ABORT || instruction.OpCode == OpCode.ABORTMSG)
                return;
            if (callWithJump.Contains(instruction.OpCode))
            {
                stack.Push((addr+instruction.Size, 0, StackType.CALL));
                if (instruction.OpCode == OpCode.CALL)
                    addr += instruction.TokenI8;
                if (instruction.OpCode == OpCode.CALL_L)
                    addr += instruction.TokenI32;
                if (instruction.OpCode == OpCode.CALLA)
                    addr += instruction.Size;// throw new NotImplementedException("CALLA is dynamic and is not supported");
                continue;
            }
            if (instruction.OpCode == OpCode.RET)
            {
                StackType stackType;
                do
                    (addr, _, stackType) = stack.Pop();
                while (stackType != StackType.CALL && stack.Count > 0);
                continue;
            }
            if (tryThrowFinally.Contains(instruction.OpCode))
            {
                if (instruction.OpCode == OpCode.TRY)
                    stack.Push((
                        instruction.TokenI8 == 0 ? -1 : addr + instruction.TokenI8,
                        instruction.TokenI8_1 == 0 ? -1 : addr + instruction.TokenI8_1,
                        StackType.TRY));
                if (instruction.OpCode == OpCode.TRY_L)
                    stack.Push((
                        instruction.TokenI32 == 0 ? -1 : addr + instruction.TokenI32,
                        instruction.TokenI32_1 == 0 ? -1 : addr + instruction.TokenI32_1,
                        StackType.TRY));
                if (instruction.OpCode == OpCode.THROW)
                {
                    StackType stackType;
                    int catchAddr; int finallyAddr;
                    do
                        (catchAddr, finallyAddr, stackType) = stack.Pop();
                    while (stackType != StackType.TRY && stack.Count > 0);
                    if (stackType == StackType.TRY)
                    {
                        if (catchAddr != -1)
                        {
                            addr = catchAddr;
                            stack.Push((-1, finallyAddr, StackType.CATCH));
                        }
                        else if (finallyAddr != -1)
                        {
                            addr = finallyAddr;
                            stack.Push((-1, -1, StackType.FINALLY));
                        }
                    }
                    continue;
                }
                if (instruction.OpCode == OpCode.ENDTRY)
                {
                    (_, int finallyAddr, StackType stackType) = stack.Pop();
                    if (stackType != StackType.TRY) throw new BadScriptException("No try stack on ENDTRY");
                    int endPointer = addr + instruction.TokenI8;
                    if (finallyAddr != -1)
                    {
                        stack.Push((-1, endPointer, StackType.FINALLY));
                        addr = finallyAddr;
                        continue;
                    }
                    else
                        addr = endPointer;
                    continue;
                }
                if (instruction.OpCode == OpCode.ENDTRY_L)
                {
                    (_, int finallyAddr, StackType stackType) = stack.Pop();
                    if (stackType != StackType.TRY) throw new BadScriptException("No try stack on ENDTRY");
                    int endPointer = addr + instruction.TokenI32;
                    if (finallyAddr != -1)
                    {
                        stack.Push((-1, endPointer, StackType.FINALLY));
                        addr = finallyAddr;
                    }
                    else
                        addr = endPointer;
                    continue;
                }
                if (instruction.OpCode == OpCode.ENDFINALLY)
                {
                    (_, addr, StackType stackType) = stack.Pop();
                    if (stackType != StackType.FINALLY)
                        throw new BadScriptException("No finally stack on ENDFINALLY");
                    continue;
                }
            }
            if (unconditionalJump.Contains(instruction.OpCode))
            {
                if (instruction.OpCode == OpCode.JMP)
                    addr += instruction.TokenI8;
                if (instruction.OpCode == OpCode.JMP_L)
                    addr += instruction.TokenI32;
                continue;
            }
            if (conditionalJump.Contains(instruction.OpCode))
            {
                int jumpTarget = instruction.TokenI8;
                CoverInstruction(addr+jumpTarget, script, coveredMap);
            }
            if (conditionalJump_L.Contains(instruction.OpCode))
            {
                int jumpTarget = instruction.TokenI32;
                CoverInstruction(addr+jumpTarget, script, coveredMap);
            }

            addr += instruction.Size;
        }
    }
}
