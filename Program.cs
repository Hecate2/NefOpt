using Neo.IO;
using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using DevHawk.DumpNef;
using static DevHawk.DumpNef.Extensions;
using static NefOpt.OpCodeTypes;
using System.Security.Cryptography;
using System.Reflection;

public static class Program
{
    public static int[] OperandSizePrefixTable = new int[256];
    public static int[] OperandSizeTable = new int[256];
    static Program()
    {
        foreach (FieldInfo field in typeof(OpCode).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            OperandSizeAttribute? attribute = field.GetCustomAttribute<OperandSizeAttribute>();
            if (attribute == null) continue;
            int index = (int)(OpCode)field.GetValue(null)!;
            OperandSizePrefixTable[index] = attribute.SizePrefix;
            OperandSizeTable[index] = attribute.Size;
        }
    }

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
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(pathAndFileName);
            string nefPath = Path.Combine(directory.FullName, fileNameWithoutExtension + ".nef");
            string manifestPath = Path.Combine(directory.FullName, fileNameWithoutExtension + ".manifest.json");
            string debugInfoPath = Path.Combine(directory.FullName, fileNameWithoutExtension + ".nefdbgnfo");
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
            string padding = script.GetInstructionAddressPadding();

            //foreach ((int address, Instruction instruction) in addressAndInstructions)
            //    Console.WriteLine(WriteInstruction(address, instruction, padding, methodTokens));

            Dictionary<int, bool> coveredMap = FindCoveredInstructions(nef, manifest, debugInfo);
            //int prevUncoveredAddr = -100;
            //int prevCoveredAddr = -100;
            //bool prevCovered = true;
            //foreach ((int addr, bool covered) in coveredMap)
            //{
            //    if (addr < script.Length && !covered && prevCovered)
            //        Console.WriteLine($"{WriteInstruction(addr, script.GetInstruction(addr), padding, methodTokens)}: {covered}");
            //    if (addr < script.Length && covered && !prevCovered)
            //        Console.WriteLine($"{WriteInstruction(addr, script.GetInstruction(addr), padding, methodTokens)}: {covered}");

            //    prevCovered = covered;
            //    if (covered)
            //        prevCoveredAddr = addr;
            //    else
            //        prevUncoveredAddr = addr;
            //}
            (nef, manifest, string dumpnef) = RemoveUncoveredInstructions(coveredMap, nef, manifest);
            File.WriteAllBytes(Path.Combine(directory.FullName, fileNameWithoutExtension + ".optimized.nef"), nef.ToArray());
            File.WriteAllBytes(Path.Combine(directory.FullName, fileNameWithoutExtension + ".optimized.manifest.json"), manifest.ToJson().ToByteArray(true));
            File.WriteAllText(Path.Combine(directory.FullName, fileNameWithoutExtension + ".optimized.nef.txt"), dumpnef);
        }
    }

    public static (NefFile, ContractManifest, string) RemoveUncoveredInstructions(
        Dictionary<int, bool> coveredMap, NefFile nef, ContractManifest oldManifest)
    {
        Script oldScript = nef.Script;
        List<(int, Instruction)> oldAddressAndInstructionsList = oldScript.EnumerateInstructions().ToList();
        Dictionary<int, Instruction> oldAddressToInstruction = new();
        foreach ((int a, Instruction i) in oldAddressAndInstructionsList)
            oldAddressToInstruction.Add(a, i);
        Dictionary<Instruction, int> simplifiedInstructionsToAddress = new();
        Dictionary<Instruction, Instruction> jumpInstructionSourceToTargets = new();
        Dictionary<Instruction, (Instruction, Instruction)> tryInstructionSourceToTargets = new();
        int currentAddress = 0;
        foreach ((int a, Instruction i) in oldAddressAndInstructionsList)
        {
            if (coveredMap[a])
            {
                simplifiedInstructionsToAddress.Add(i, currentAddress);
                currentAddress += i.Size;
            }
            else
                continue;
            if (i.OpCode == OpCode.JMP || conditionalJump.Contains(i.OpCode) || i.OpCode == OpCode.CALL || i.OpCode == OpCode.ENDTRY)
                jumpInstructionSourceToTargets.Add(i, oldAddressToInstruction[a+i.TokenI8]);
            if (i.OpCode == OpCode.PUSHA || i.OpCode == OpCode.JMP_L || conditionalJump_L.Contains(i.OpCode) || i.OpCode == OpCode.CALL_L || i.OpCode == OpCode.ENDTRY_L)
                jumpInstructionSourceToTargets.Add(i, oldAddressToInstruction[a+i.TokenI32]);
            if (i.OpCode == OpCode.TRY)
                tryInstructionSourceToTargets.Add(i, (oldAddressToInstruction[a+i.TokenI8], oldAddressToInstruction[a+i.TokenI8_1]));
            if (i.OpCode == OpCode.TRY_L)
                tryInstructionSourceToTargets.Add(i, (oldAddressToInstruction[a+i.TokenI32], oldAddressToInstruction[a+i.TokenI32_1]));
        }
        List<byte> simplifiedScript = new();
        foreach ((Instruction i, int a) in simplifiedInstructionsToAddress)
        {
            simplifiedScript.Add((byte)i.OpCode);
            int operandSizeLength = OperandSizePrefixTable[(int)i.OpCode];
            simplifiedScript = simplifiedScript.Concat(BitConverter.GetBytes(i.Operand.Length)[0..operandSizeLength]).ToList();
            if (jumpInstructionSourceToTargets.ContainsKey(i))
            {
                Instruction dst = jumpInstructionSourceToTargets[i];
                int delta = simplifiedInstructionsToAddress[dst] - a;
                if (i.OpCode == OpCode.JMP || conditionalJump.Contains(i.OpCode) || i.OpCode == OpCode.CALL || i.OpCode == OpCode.ENDTRY)
                    simplifiedScript.Add(BitConverter.GetBytes(delta)[0]);
                if (i.OpCode == OpCode.PUSHA || i.OpCode == OpCode.JMP_L || conditionalJump_L.Contains(i.OpCode) || i.OpCode == OpCode.CALL_L || i.OpCode == OpCode.ENDTRY_L)
                    simplifiedScript = simplifiedScript.Concat(BitConverter.GetBytes(delta)).ToList();
                continue;
            }
            if (tryInstructionSourceToTargets.ContainsKey(i))
            {
                (Instruction dst1, Instruction dst2) = tryInstructionSourceToTargets[i];
                (int delta1, int delta2) = (simplifiedInstructionsToAddress[dst1] - a, simplifiedInstructionsToAddress[dst2] - a);
                if (i.OpCode == OpCode.TRY)
                {
                    simplifiedScript.Add(BitConverter.GetBytes(delta1)[0]);
                    simplifiedScript.Add(BitConverter.GetBytes(delta2)[0]);
                }
                if (i.OpCode == OpCode.TRY_L)
                {
                    simplifiedScript = simplifiedScript.Concat(BitConverter.GetBytes(delta1)).ToList();
                    simplifiedScript = simplifiedScript.Concat(BitConverter.GetBytes(delta2)).ToList();
                }
                continue;
            }
            if (i.Operand.Length != 0)
                simplifiedScript = simplifiedScript.Concat(i.Operand.ToArray()).ToList();
        }
        foreach (ContractMethodDescriptor method in oldManifest.Abi.Methods)
            method.Offset = simplifiedInstructionsToAddress[oldAddressToInstruction[method.Offset]];
        Script newScript = new Script(simplifiedScript.ToArray());
        nef.Script = newScript;
        nef.Compiler = System.AppDomain.CurrentDomain.FriendlyName;
        nef.CheckSum = NefFile.ComputeChecksum(nef);

        string dumpnef = "";
        foreach ((int a, Instruction i) in newScript.EnumerateInstructions(print: true).ToList())
            if (a < newScript.Length)
                dumpnef += WriteInstruction(a, newScript.GetInstruction(a), newScript.GetInstructionAddressPadding(), nef.Tokens) + "\n";
        return (nef, oldManifest, dumpnef);
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
        CONDITIONAL_JUMP,
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
            {
                StackType stackType;
                do
                    (addr, _, stackType) = stack.Pop();
                while (stackType != StackType.CONDITIONAL_JUMP && stack.Count > 0);
                continue;
            }
            if (callWithJump.Contains(instruction.OpCode))
            {
                stack.Push((addr+instruction.Size, 0, StackType.CALL));
                if (instruction.OpCode == OpCode.CALL)
                    addr += instruction.TokenI8;
                if (instruction.OpCode == OpCode.CALL_L)
                    addr += instruction.TokenI32;
                if (instruction.OpCode == OpCode.CALLA)
                    throw new NotImplementedException("CALLA is dynamic, not supported");
                    // addr += instruction.Size;
                continue;
            }
            if (instruction.OpCode == OpCode.RET)
            {
                StackType stackType;
                do
                    (addr, _, stackType) = stack.Pop();
                while (stackType != StackType.CALL && stackType != StackType.CONDITIONAL_JUMP && stack.Count > 0);
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
                    while (stackType != StackType.TRY && stackType != StackType.CONDITIONAL_JUMP && stack.Count > 0);
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
                    if (stackType == StackType.CONDITIONAL_JUMP)
                        addr = catchAddr;
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
                int jumpOffset = instruction.TokenI8;
                stack.Push((addr + instruction.Size, -1, StackType.CONDITIONAL_JUMP));
                addr += jumpOffset;
                continue;
            }
            if (conditionalJump_L.Contains(instruction.OpCode))
            {
                int jumpOffset = instruction.TokenI32;
                stack.Push((addr + instruction.Size, -1, StackType.CONDITIONAL_JUMP));
                addr += jumpOffset;
                continue;
            }

            addr += instruction.Size;
        }
    }
}
