using System.Reflection.Emit;
using HarmonyLib;

namespace ModAPI.Harmony
{
    internal static class HarmonyInstructionCompat
    {
        public static bool TryGetLocalIndex(CodeInstruction instruction, out int index)
        {
            index = -1;
            if (instruction == null) return false;

            var opcode = instruction.opcode;
            if (opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Stloc_0) { index = 0; return true; }
            if (opcode == OpCodes.Ldloc_1 || opcode == OpCodes.Stloc_1) { index = 1; return true; }
            if (opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Stloc_2) { index = 2; return true; }
            if (opcode == OpCodes.Ldloc_3 || opcode == OpCodes.Stloc_3) { index = 3; return true; }

            if (opcode != OpCodes.Ldloc
                && opcode != OpCodes.Ldloc_S
                && opcode != OpCodes.Ldloca
                && opcode != OpCodes.Ldloca_S
                && opcode != OpCodes.Stloc
                && opcode != OpCodes.Stloc_S)
            {
                return false;
            }

            return TryReadIndexOperand(instruction.operand, out index);
        }

        public static bool TryGetArgumentIndex(CodeInstruction instruction, out int index)
        {
            index = -1;
            if (instruction == null) return false;

            var opcode = instruction.opcode;
            if (opcode == OpCodes.Ldarg_0) { index = 0; return true; }
            if (opcode == OpCodes.Ldarg_1) { index = 1; return true; }
            if (opcode == OpCodes.Ldarg_2) { index = 2; return true; }
            if (opcode == OpCodes.Ldarg_3) { index = 3; return true; }

            if (opcode != OpCodes.Ldarg
                && opcode != OpCodes.Ldarg_S
                && opcode != OpCodes.Ldarga
                && opcode != OpCodes.Ldarga_S
                && opcode != OpCodes.Starg
                && opcode != OpCodes.Starg_S)
            {
                return false;
            }

            return TryReadIndexOperand(instruction.operand, out index);
        }

        private static bool TryReadIndexOperand(object operand, out int index)
        {
            index = -1;

            if (operand is LocalBuilder localBuilder)
            {
                index = localBuilder.LocalIndex;
                return true;
            }

            if (operand is int intIndex) { index = intIndex; return true; }
            if (operand is byte byteIndex) { index = byteIndex; return true; }
            if (operand is sbyte signedByteIndex) { index = signedByteIndex; return true; }
            if (operand is short shortIndex) { index = shortIndex; return true; }
            if (operand is ushort unsignedShortIndex) { index = unsignedShortIndex; return true; }

            return false;
        }
    }
}
