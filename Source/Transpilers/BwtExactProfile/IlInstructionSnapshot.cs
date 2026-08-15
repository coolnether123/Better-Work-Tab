using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Better_Work_Tab.Transpilers.BwtExactProfile
{
    internal sealed class IlLocalSnapshot
    {
        internal IlLocalSnapshot(int index, Type localType, bool isPinned)
        {
            Index = index;
            LocalType = localType;
            IsPinned = isPinned;
        }
        internal int Index { get; private set; }
        internal Type LocalType { get; private set; }
        internal bool IsPinned { get; private set; }
    }

    internal sealed class IlMethodSnapshot
    {
        internal IlMethodSnapshot(MethodBase method)
        {
            OriginalMethod = method;
            Identity = IdentityOf(method);
            var locals = new List<IlLocalSnapshot>();
            try
            {
                var body = method == null ? null : method.GetMethodBody();
                if (body != null)
                    foreach (var local in body.LocalVariables)
                        locals.Add(new IlLocalSnapshot(local.LocalIndex, local.LocalType, local.IsPinned));
            }
            catch (Exception)
            {
                // A diagnostic snapshot remains useful when method-body metadata is unavailable.
            }
            Locals = new ReadOnlyCollection<IlLocalSnapshot>(locals);
        }
        internal MethodBase OriginalMethod { get; private set; }
        internal string Identity { get; private set; }
        internal IReadOnlyList<IlLocalSnapshot> Locals { get; private set; }
        internal static string Trim(string value, int maximum)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            return value.Length <= maximum
                ? value
                : value.Substring(0, Math.Max(0, maximum - 3)) + "...";
        }
        private static string IdentityOf(MethodBase method)
        {
            if (method == null) return "<unknown-method>";
            var declaring = method.DeclaringType == null
                ? "<global>"
                : method.DeclaringType.FullName ?? method.DeclaringType.Name;
            var parameters = new List<string>();
            try
            {
                foreach (var parameter in method.GetParameters())
                    parameters.Add(parameter.ParameterType.FullName ?? parameter.ParameterType.Name);
            }
            catch (Exception)
            {
                parameters.Add("?");
            }
            return declaring + "." + method.Name + "(" + String.Join(",", parameters.ToArray()) + ")";
        }
    }

    internal sealed class IlInstructionSnapshot
    {
        private readonly List<CodeInstruction> _instructions;
        private IlInstructionSnapshot(List<CodeInstruction> instructions, MethodBase originalMethod, bool nullInput)
        {
            _instructions = instructions ?? new List<CodeInstruction>();
            Method = new IlMethodSnapshot(originalMethod);
            NullInput = nullInput;
            foreach (var instruction in _instructions)
            {
                if (instruction == null)
                {
                    HasNullInstruction = true;
                    break;
                }
            }
        }
        internal static IlInstructionSnapshot Capture(
            IEnumerable<CodeInstruction> instructions, MethodBase originalMethod = null)
        {
            if (instructions == null)
                return new IlInstructionSnapshot(new List<CodeInstruction>(), originalMethod, true);
            var complete = new List<CodeInstruction>();
            foreach (var instruction in instructions)
                complete.Add(CloneInstruction(instruction));
            return new IlInstructionSnapshot(complete, originalMethod, false);
        }
        internal int Count { get { return _instructions.Count; } }
        internal bool NullInput { get; private set; }
        internal bool HasNullInstruction { get; private set; }
        internal IlMethodSnapshot Method { get; private set; }
        internal List<CodeInstruction> CloneInstructions() { return CloneList(_instructions); }
        internal static List<CodeInstruction> CloneList(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            if (instructions == null) return result;
            foreach (var instruction in instructions) result.Add(CloneInstruction(instruction));
            return result;
        }
        internal static CodeInstruction CloneInstruction(CodeInstruction instruction)
        {
            if (instruction == null) return null;
            var clone = new CodeInstruction(instruction.opcode, CloneOperand(instruction.operand));
            clone.labels = instruction.labels == null
                ? new List<Label>()
                : new List<Label>(instruction.labels);
            clone.blocks = instruction.blocks == null
                ? new List<ExceptionBlock>()
                : new List<ExceptionBlock>(instruction.blocks);
            return clone;
        }
        private static object CloneOperand(object operand)
        {
            var labels = operand as Label[];
            return labels == null ? operand : (Label[])labels.Clone();
        }
    }
}
