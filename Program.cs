using Neo.IO;
using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using static Neo.Optimizer.DumpNef;
using static Neo.Optimizer.Reachability;

namespace Neo.Optimizer
{
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
                JObject debugInfo = (JObject)JObject.Parse(Unzip(File.ReadAllBytes(debugInfoPath)))!;

                //foreach ((int address, Instruction instruction) in addressAndInstructions)
                //    Console.WriteLine(WriteInstruction(address, instruction, padding, methodTokens));

                //Script script = nef.Script;
                //string padding = script.GetInstructionAddressPadding();
                //MethodToken[] methodTokens = nef.Tokens.ToArray();
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
                (nef, manifest, debugInfo) = RemoveUncoveredInstructions(nef, manifest, debugInfo);
                string dumpnef = GenerateDumpNef(nef, debugInfo);
                File.WriteAllBytes(Path.Combine(directory.FullName, fileNameWithoutExtension + ".optimized.nef"), nef.ToArray());
                File.WriteAllBytes(Path.Combine(directory.FullName, fileNameWithoutExtension + ".optimized.manifest.json"), manifest.ToJson().ToByteArray(true));
                File.WriteAllText(Path.Combine(directory.FullName, fileNameWithoutExtension + ".optimized.nef.txt"), dumpnef);
                File.WriteAllBytes(Path.Combine(directory.FullName, fileNameWithoutExtension + ".optimized.nefdbgnfo"), Zip(debugInfo.ToByteArray(true), fileNameWithoutExtension + ".optimized.debug.json"));
            }
        }
    }
}
