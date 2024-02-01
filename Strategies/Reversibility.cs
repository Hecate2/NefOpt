using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Neo.Optimizer.JumpTarget;
using static Neo.Optimizer.OpCodeTypes;
using static Neo.Optimizer.Optimizer;
using static Neo.VM.OpCode;

namespace Neo.Optimizer
{
    public static class Reversibility
    {
        public static HashSet<OpCode[]> reversibleOpCodes = new();

        static Reversibility()
        {
            reversibleOpCodes.Add(new OpCode[] { PACK, UNPACK });
            reversibleOpCodes.Add(new OpCode[] { UNPACK, PACK });
            foreach (OpCode op in OpCodeTypes.push)
                reversibleOpCodes.Add(new OpCode[] { op, DROP });
        }

        [Strategy(Priority = 0)]
        public static (NefFile, ContractManifest, JToken) RemoveReversibleInstructions(NefFile nef, ContractManifest manifest, JToken debugInfo)
        {
            Script script = nef.Script;
            LinkedList<Instruction> instructions = new();
            foreach ((_, Instruction i) in script.EnumerateInstructions())
                instructions.AddLast(i);

            LinkedListNode<Instruction>? node = instructions.First;
            while (node != null)
            {
                // Delete pattern in reversibleOpCodes
                // Possibly Aho-Corasick automaton can be used. But we use brute force for now.
                foreach (OpCode[] opcodeSeries in reversibleOpCodes)
                {
                    //bool equal = false;
                    //LinkedListNode<Instruction>? currentNode = node;
                    //foreach (OpCode i in opcodeSeries)
                    //{
                    //    if (currentNode.Value.OpCode == i)
                    //        currentNode = currentNode.Next;
                    //    if (currentNode == null)
                    //        break;
                    //}
                    //equal = true;
                    //if (currentNode == null)
                    //{
                    //    equal = true;
                    //    break;
                    //}
                }
                node = node.Next;
            }
            return (nef, manifest, debugInfo);
        }
    }
}
