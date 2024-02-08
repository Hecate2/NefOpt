using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using static Neo.Optimizer.JumpTargetAnalyser;
using static Neo.Optimizer.OpCodeTypes;

namespace Neo.Optimizer
{
    public class BasicBlock
    {
        public LinkedList<Instruction> instructions;
        public Instruction firstInstruction => instructions.First();
        public Instruction lastInstruction => instructions.Last();
        public HashSet<BasicBlock> fromBlocks = new();  // who reach me by jumping
        public BasicBlock? prevBlock { get; protected internal set; } = null;  // who reaches me by jumping
        public BasicBlock? nextBlock { get; protected internal set; } = null;  // the block I reach without jumping
        public BasicBlock? jumpTargetBlock { get; protected internal set; } = null;  // the block I reach by jumping
        public BasicBlock? tryBlock { get; protected internal set; } = null;
        public BasicBlock? catchBlock { get; protected internal set; } = null;
        public BasicBlock? finallyBlock { get; protected internal set; } = null;
        public BasicBlock? endTryBlock { get; protected internal set; } = null;
        public readonly bool isTry;
        public readonly bool isCatch;
        public readonly bool isFinally;
        public BasicBlock(IEnumerable<Instruction> instructions, bool isTry=false, bool isCatch=false, bool isFinally=false)
        {
            this.instructions = new(instructions);
            if (new bool[] { isTry, isCatch, isFinally }.Count(i => i == true) > 1)
                throw new ArgumentException("A block cannot be multiple types of try, catch and finally");
            this.isTry = isTry;
            this.isCatch = isCatch;
            this.isFinally = isFinally;
        }
    }

    public static class BasicBlocks
    {
        public static Dictionary<int, BasicBlock> addrToBasicBlocks = new();
    }

    public class BasicBlockAnalyser
    {
        public readonly NefFile nef;
        public readonly Script script;
        public readonly ContractManifest manifest;
        public readonly JToken debugInfo;
        public Dictionary<int, BasicBlock> addressToBasicBlock { get; protected internal set; } = new();
        protected Dictionary<int, bool> coveredMap = new();
        public BasicBlockAnalyser(NefFile nef, ContractManifest manifest, JToken debugInfo)
        {
            this.nef = nef;
            this.manifest = manifest;
            this.debugInfo = debugInfo;
            this.script = nef.Script;
            foreach ((int a, _) in script.EnumerateInstructions().ToList())
                coveredMap.Add(a, false);
            Analyse(script);
        }
        protected void Analyse(Script script)
        {
            
        }

        protected BasicBlock HandleJumpToBasicBlock(BasicBlock src, int dstAddr, bool isTry = false, bool isCatch = false, bool isFinally = false, int finallyAddr = -1, int endTryAddr = -1)
        {
            BasicBlock dst = coveredMap[dstAddr] ?
                this.addressToBasicBlock[dstAddr] :
                AnalyseBasicBlockFromAddress(dstAddr,isTry, isCatch, isFinally, finallyAddr, endTryAddr);
            if (src.jumpTargetBlock != null) throw new BadScriptException("Multiple jump target");
            src.jumpTargetBlock = dst;
            dst.fromBlocks.Add(src);
            return dst;
        }

        protected BasicBlock HandleNextToBasicBlock(BasicBlock src, int dstAddr,
            bool isTry = false, bool isCatch = false, bool isFinally = false,
            int finallyAddr = -1, int endTryAddr = -1)
        {
            BasicBlock dst = coveredMap[dstAddr] ?
                this.addressToBasicBlock[dstAddr] :
                AnalyseBasicBlockFromAddress(dstAddr,
                isTry, isCatch, isFinally, finallyAddr, endTryAddr);
            if (src.nextBlock != null) throw new BadScriptException("Multiple next");
            src.nextBlock = dst;
            if (dst.prevBlock != null) throw new BadScriptException("Multiple prev");
            dst.prevBlock = src;
            return dst;
        }

        protected BasicBlock HandleNextToTryBlock(BasicBlock src, int dstAddr, int finallyAddr = -1)
        {
            OpCode lastOpCode = src.lastInstruction.OpCode;
            if (lastOpCode != OpCode.TRY && lastOpCode != OpCode.TRY_L)
                throw new BadScriptException("No TRY instruction.");
            BasicBlock dst = coveredMap[dstAddr] ?
                this.addressToBasicBlock[dstAddr] :
                AnalyseBasicBlockFromAddress(dstAddr, isTry: true, finallyAddr: finallyAddr);
            if (src.nextBlock != null) throw new BadScriptException("Multiple next");
            src.nextBlock = dst;
            if (dst.prevBlock != null) throw new BadScriptException("Multiple prev");
            dst.prevBlock = src;
            return dst;
        }

        protected BasicBlock HandleJumpToFinallyBlock(BasicBlock src, int dstAddr, int endTryAddr)
        {
            if (!src.isTry && !src.isCatch)
                throw new BadScriptException("Cannot enter finally without try and catch");
            OpCode lastOpCode = src.lastInstruction.OpCode;
            if (lastOpCode != OpCode.ENDTRY && lastOpCode != OpCode.ENDTRY_L)
                throw new BadScriptException("No ENDTRY instruction");
            BasicBlock dst = coveredMap[dstAddr] ?
                this.addressToBasicBlock[dstAddr] :
                AnalyseBasicBlockFromAddress(dstAddr, isFinally: true, endTryAddr: endTryAddr);
            src.finallyBlock = dst;
            return dst;
        }

        protected BasicBlock HandleJumpToEndTryBlock(BasicBlock src, int dstAddr)
        {
            if (!src.isTry && !src.isCatch)
                throw new BadScriptException("Cannot enter finally without try and catch");
            OpCode lastOpCode = src.lastInstruction.OpCode;
            if (lastOpCode != OpCode.ENDTRY && lastOpCode != OpCode.ENDTRY_L)
                throw new BadScriptException("No ENDTRY instruction");
            BasicBlock dst = coveredMap[dstAddr] ?
                this.addressToBasicBlock[dstAddr] :
                AnalyseBasicBlockFromAddress(dstAddr);
            src.endTryBlock = dst;
            dst.fromBlocks.Add(src);
            return dst;
        }

        public (BasicBlock thisBlock, int lastAddress) CoverInstructions(int address, bool isTry=false, bool isCatch=false, bool isFinally=false)
        {
            LinkedList<Instruction> instructions = new();
            Instruction currentInstruction;
            while (true)
            {
                if (coveredMap[address])
                    throw new AccessViolationException("Instruction covered in basic block");
                coveredMap[address] = true;
                if (address < script.Length)
                    currentInstruction = script.GetInstruction(address);
                else
                    currentInstruction = Instruction.RET;
                instructions.AddLast(currentInstruction);
                if (allEndsOfBasicBlock.Contains(currentInstruction.OpCode))
                    break;
                address += currentInstruction.Size;
            }
            return (new BasicBlock(instructions, isTry, isCatch, isFinally), address);
        }

        public BasicBlock AnalyseBasicBlockFromAddress(int address,
            bool isTry=false, bool isCatch = false, bool isFinally = false,
            int finallyAddr = -1, int endTryAddr = -1)
        {
            int startAddress = address;
            (BasicBlock thisBlock, int lastAddress) = CoverInstructions(address, isTry, isCatch, isFinally);
            Instruction lastInstruction = thisBlock.lastInstruction;
            this.addressToBasicBlock.Add(startAddress, thisBlock);
            if (lastInstruction.OpCode == OpCode.RET
             || lastInstruction.OpCode == OpCode.ABORT
             || lastInstruction.OpCode == OpCode.THROW)
                return thisBlock;
            if (SingleJumpInOperand(lastInstruction))
            {
                int jumpTarget = ComputeJumpTarget(lastAddress, lastInstruction);
                if (lastInstruction.OpCode == OpCode.ENDTRY || lastInstruction.OpCode == OpCode.ENDTRY_L)
                {
                    if (!isTry && !isCatch)
                        throw new BadScriptException("No try on ENDTRY");
                    if (finallyAddr > 0)
                        HandleJumpToFinallyBlock(thisBlock, finallyAddr, endTryAddr: jumpTarget);
                    else
                        HandleJumpToEndTryBlock(thisBlock, endTryAddr);
                }
                else
                {
                    // handle jump target
                    if (callWithJump.Contains(lastInstruction.OpCode))
                        HandleJumpToBasicBlock(thisBlock, jumpTarget);  // do not pass try state
                    else
                        HandleJumpToBasicBlock(thisBlock, jumpTarget,
                            isTry, isCatch, isFinally, finallyAddr, endTryAddr);
                    // handle no jump target
                    if (conditionalJump.Contains(lastInstruction.OpCode) || conditionalJump_L.Contains(lastInstruction.OpCode))
                        HandleNextToBasicBlock(thisBlock, lastAddress + lastInstruction.Size,
                            isTry, isCatch, isFinally, finallyAddr, endTryAddr);
                }
            }
            else if (lastInstruction.OpCode == OpCode.ENDFINALLY)
            {
                if (!isFinally)
                    throw new BadScriptException("No finally on ENDFINALLY");
                if (endTryAddr < 0)
                    throw new BadScriptException($"Invalid endTryAddr={endTryAddr}");
                HandleJumpToEndTryBlock(thisBlock, endTryAddr);
            }
            else if (DoubleJumpInOperand(lastInstruction))
            {
                (int catchTarget, int finallyTarget) = ComputeTryTarget(lastAddress, lastInstruction);
                BasicBlock tryBlock = HandleNextToTryBlock(thisBlock, lastAddress + lastInstruction.Size, finallyTarget);
                if (catchTarget != -1)
                {
                    BasicBlock catchBlock = AnalyseBasicBlockFromAddress(catchTarget, isCatch: true, finallyAddr: finallyTarget);
                    tryBlock.catchBlock = catchBlock;
                    catchBlock.tryBlock = tryBlock;
                }
                // DO NOT evaluate finally block, because we do not know the ENDTRY address
            }
            else
                throw new BadScriptException($"Unknown OpCode {lastInstruction.OpCode}");
            return thisBlock;
        }
    }
}
