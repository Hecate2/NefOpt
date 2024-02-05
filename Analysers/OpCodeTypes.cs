﻿using Neo.VM;
using static Neo.VM.OpCode;

namespace Neo.Optimizer
{
    public static class OpCodeTypes
    {
        #region push
        public static HashSet<OpCode> pushInt = new HashSet<OpCode>
        {
            PUSHINT8,
            PUSHINT16,
            PUSHINT32,
            PUSHINT64,
            PUSHINT128,
            PUSHINT256,
        };
        public static HashSet<OpCode> pushBool = new HashSet<OpCode>
        {
            PUSHT, PUSHF,
        };
        public static HashSet<OpCode> pushData = new HashSet<OpCode>
        {
            PUSHDATA1,
            PUSHDATA2,
            PUSHDATA4,
        };
        public static HashSet<OpCode> pushConst = new HashSet<OpCode>
        {
            PUSHM1,
            PUSH0,
            PUSH1,
            PUSH2,
            PUSH3,
            PUSH4,
            PUSH5,
            PUSH6,
            PUSH7,
            PUSH8,
            PUSH9,
            PUSH10,
            PUSH11,
            PUSH12,
            PUSH13,
            PUSH14,
            PUSH15,
            PUSH16,
        };
        public static HashSet<OpCode> pushStackOps = new HashSet<OpCode>
        {
            DEPTH,
            DUP,
            OVER,
        };
        public static HashSet<OpCode> pushNewCompoundType = new HashSet<OpCode>
        {
            NEWARRAY0,
            NEWSTRUCT0,
            NEWMAP,
        };
        public static HashSet<OpCode> allPushes = new();
        #endregion

        #region jump
        // BE AWARE that PUSHA is also related to addresses
        public static HashSet<OpCode> tryThrowFinally = new HashSet<OpCode>
        {
            TRY,
            TRY_L,
            THROW,
            ENDTRY,
            ENDTRY_L,
            ENDFINALLY,
        };
        public static HashSet<OpCode> unconditionalJump = new HashSet<OpCode>
        {
            JMP,
            JMP_L,
        };
        public static HashSet<OpCode> callWithJump = new HashSet<OpCode>
        {
            CALL,
            CALL_L,
            CALLA,
        };
        public static HashSet<OpCode> conditionalJump = new HashSet<OpCode>
        {
            JMPIF,
            JMPIFNOT,
            JMPEQ,
            JMPNE,
            JMPGT,
            JMPGE,
            JMPLT,
            JMPLE,
        };
        public static HashSet<OpCode> conditionalJump_L = new HashSet<OpCode>
        {
            JMPIF_L,
            JMPIFNOT_L,
            JMPEQ_L,
            JMPNE_L,
            JMPGT_L,
            JMPGE_L,
            JMPLT_L,
            JMPLE_L,
        };
        public static HashSet<OpCode> exceptions = new HashSet<OpCode>
        {
            THROW,
            ABORT,
        };
        public static HashSet<OpCode> allEndsOfBasicBlock = new();
        #endregion

        static OpCodeTypes()
        {
            foreach (OpCode op in pushInt) allPushes.Add(op);
            foreach (OpCode op in pushBool) allPushes.Add(op);
            allPushes.Add(PUSHA);
            allPushes.Add(PUSHNULL);
            foreach (OpCode op in pushData) allPushes.Add(op);
            foreach (OpCode op in pushConst) allPushes.Add(op);
            foreach (OpCode op in pushStackOps) allPushes.Add(op);
            foreach (OpCode op in pushNewCompoundType) allPushes.Add(op);

            foreach (OpCode op in tryThrowFinally) allEndsOfBasicBlock.Add(op);
            foreach (OpCode op in unconditionalJump) allEndsOfBasicBlock.Add(op);
            foreach (OpCode op in conditionalJump_L) allEndsOfBasicBlock.Add(op);
            foreach (OpCode op in exceptions) allEndsOfBasicBlock.Add(op);
            allEndsOfBasicBlock.Add(RET);
        }
    }
}
