using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Spine.Harmony.Transpilers.VNext
{
    internal sealed class IlVerificationReport
    {
        private readonly List<PatchDiagnostic> _diagnostics = new List<PatchDiagnostic>();
        internal bool IsValid { get { return !HasErrors; } }
        internal IReadOnlyList<PatchDiagnostic> Diagnostics { get { return _diagnostics.AsReadOnly(); } }
        internal bool HasErrors { get; private set; }
        internal void Add(PatchDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            _diagnostics.Add(diagnostic);
            HasErrors |= diagnostic.Severity == PatchDiagnosticSeverity.Error;
        }
        internal void Error(PatchDiagnosticCode code, int? start, int? end, string detail)
        {
            Add(new PatchDiagnostic(code, PatchDiagnosticSeverity.Error, null, null, null, start, end, detail));
        }
        internal void Fault(int index, Exception exception)
        {
            Add(new PatchDiagnostic(PatchDiagnosticCode.InternalException, PatchDiagnosticSeverity.Error,
                null, null, null, index, index + 1, "The instruction operand could not be inspected.",
                exception == null ? null : exception.GetType().FullName,
                exception == null ? null : exception.Message));
        }
        internal bool HasErrorAt(int index)
        {
            return _diagnostics.Any(d => d.Severity == PatchDiagnosticSeverity.Error && d.StartIndex == index);
        }
    }

    internal sealed class IlControlFlowGraph
    {
        internal IlControlFlowGraph(int count)
        {
            Successors = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
            Reachable = new bool[count];
            HandlerEntries = new Dictionary<int, IlStackState>();
            Labels = new Dictionary<Label, int>();
        }
        internal List<int>[] Successors { get; private set; }
        internal bool[] Reachable { get; private set; }
        internal Dictionary<int, IlStackState> HandlerEntries { get; private set; }
        internal Dictionary<Label, int> Labels { get; private set; }
    }

    internal sealed class IlExceptionClause
    {
        internal IlExceptionClause(ExceptionBlockType kind, int start)
        { Kind = kind; Start = start; EndExclusive = Int32.MaxValue; }
        internal ExceptionBlockType Kind { get; private set; }
        internal int Start { get; private set; }
        internal int EndExclusive { get; set; }
    }

    internal sealed class IlExceptionContext
    {
        internal IlExceptionContext(IEnumerable<IlExceptionClause> clauses)
        { Clauses = clauses == null ? new IlExceptionClause[0] : clauses.ToArray(); }
        internal IlExceptionClause[] Clauses { get; private set; }
        internal bool IsHandlerKind(ExceptionBlockType kind, int index)
        {
            return Clauses.Length != 0 && Clauses[Clauses.Length - 1].Kind == kind &&
                Clauses[Clauses.Length - 1].Start <= index &&
                index < Clauses[Clauses.Length - 1].EndExclusive;
        }
        internal bool SameAs(IlExceptionContext other)
        {
            if (other == null || Clauses.Length != other.Clauses.Length) return false;
            for (int i = 0; i < Clauses.Length; i++)
                if (!Object.ReferenceEquals(Clauses[i], other.Clauses[i])) return false;
            return true;
        }
        internal bool CanLeaveTo(IlExceptionContext target)
        {
            if (target == null || target.Clauses.Length == 0) return true;
            if (target.Clauses[target.Clauses.Length - 1].Kind != ExceptionBlockType.BeginExceptionBlock)
                return false;
            if (target.Clauses.Length >= Clauses.Length) return false;
            for (int i = 0; i < target.Clauses.Length; i++)
                if (!Object.ReferenceEquals(Clauses[i], target.Clauses[i])) return false;
            return true;
        }
    }

        internal static class IlVerifier
    {
        internal static IlVerificationReport Verify(IlInstructionSnapshot snapshot, IList<CodeInstruction> candidate)
        {
            return Verify(snapshot, candidate, null);
        }

        internal static IlVerificationReport Verify(
            IlInstructionSnapshot snapshot, IList<CodeInstruction> candidate, IList<IlResolvedEdit> edits)
        {
            var report = new IlVerificationReport();
            if (snapshot == null)
            {
                report.Error(PatchDiagnosticCode.NullInstructionStream, null, null, "No snapshot was supplied.");
                return report;
            }
            if (snapshot.NullInput)
                report.Error(PatchDiagnosticCode.NullInstructionStream, null, null, "The instruction stream was null.");
            if (candidate == null || candidate.Count == 0)
            {
                report.Error(PatchDiagnosticCode.StructuralValidationFailed, null, null,
                    "The instruction stream is empty.");
                return report;
            }
            for (int i = 0; i < candidate.Count; i++)
                if (candidate[i] == null)
                    report.Error(PatchDiagnosticCode.NullInstruction, i, i + 1,
                        "A null instruction is not valid IL.");
            if (report.HasErrors) return report;
            if (snapshot.Method.OriginalMethod != null && snapshot.Method.OriginalMethod.ContainsGenericParameters)
            {
                report.Error(PatchDiagnosticCode.UnsupportedMethodContext, null, null,
                    "Open generic original methods are outside the verifier safety boundary.");
                return report;
            }

            List<CodeInstruction> original = snapshot.CloneInstructions();
            CompareMetadata(original, candidate, edits, report);
            if (report.HasErrors) return report;
            ValidatePrefixes(candidate, original, edits, report);
            if (report.HasErrors) return report;
            IlControlFlowGraph graph = BuildGraph(candidate, report);
            if (report.HasErrors) return report;
            ValidateShortBranches(candidate, graph, report);
            IlExceptionContext[] regions = BuildRegions(candidate, report);
            MarkReachable(graph);
            ValidateRegionEdges(candidate, graph, regions, report);
            if (report.HasErrors) return report;
            for (int i = 0; i < candidate.Count; i++)
                if (graph.Reachable[i] && graph.Successors[i].Count == 0 && !IsTerminal(candidate[i].opcode))
                    report.Error(PatchDiagnosticCode.StructuralValidationFailed, i, i + 1,
                        "Reachable control flow falls through the end of the method.");
            for (int i = 0; i < candidate.Count; i++)
                if (!IlStackAnalyzer.IsSupported(candidate[i]))
                    report.Error(PatchDiagnosticCode.UnsupportedOpcode, i, i + 1,
                        "The verifier does not recognize opcode " + candidate[i].opcode + ".");
            if (!report.HasErrors) IlStackAnalyzer.Analyze(snapshot, candidate, graph, regions, report);
            return report;
        }

        internal static bool AreCallShapesCompatible(
            MethodBase source, OpCode sourceOpcode, MethodBase replacement, out string detail)
        {
            return TryValidate(() => CallShapeError(source, sourceOpcode, replacement),
                "Call signatures could not be inspected: ", out detail);
        }
        internal static bool CanReplaceInt32ConstantWithCall(MethodBase method, out string detail)
        {
            return TryValidate(() => ConstantCallError(method),
                "The constant replacement signature could not be inspected: ", out detail);
        }
        private static bool TryValidate(Func<string> validation, string exceptionPrefix, out string detail)
        {
            try
            {
                detail = validation();
                return detail == null;
            }
            catch (Exception exception)
            {
                detail = exceptionPrefix + exception.Message;
                return false;
            }
        }
        private static string CallShapeError(MethodBase source, OpCode sourceOpcode, MethodBase replacement)
        {
            if (source == null || replacement == null) return "Both source and replacement methods are required.";
            if (source.ContainsGenericParameters || replacement.ContainsGenericParameters)
                return "Open generic methods are not supported by call replacement.";
            if (sourceOpcode == OpCodes.Callvirt && source.IsStatic)
                return "callvirt cannot target a static method operand.";
            List<Type> inputs = Inputs(source, !source.IsStatic);
            List<Type> replacements = Inputs(replacement, !replacement.IsStatic);
            if (inputs.Count != replacements.Count)
                return "The replacement consumes a different number of stack values.";
            for (int i = 0; i < inputs.Count; i++)
                if (!TypesCompatible(inputs[i], replacements[i]))
                    return "The replacement parameter " + i + " is not stack-compatible.";
            return TypesCompatible(ReturnType(replacement), ReturnType(source))
                ? null : "The replacement return type is not stack-compatible.";
        }
        private static string ConstantCallError(MethodBase method)
        {
            if (method == null || !method.IsStatic || method.ContainsGenericParameters ||
                method.GetParameters().Length != 0)
                return "A constant replacement must be a parameterless static method.";
            return IsInt32Like(ReturnType(method))
                ? null : "A constant replacement must return an integral value.";
        }
        internal static Type ReturnType(MethodBase method) =>
            method is MethodInfo info ? info.ReturnType : typeof(void);
        private static IlControlFlowGraph BuildGraph(IList<CodeInstruction> code, IlVerificationReport report)
        {
            var graph = new IlControlFlowGraph(code.Count);
            var labels = graph.Labels;
            for (int i = 0; i < code.Count; i++)
                if (code[i].labels != null)
                    foreach (Label label in code[i].labels)
                        if (labels.ContainsKey(label))
                            report.Error(PatchDiagnosticCode.InvalidBranchTarget, i, i + 1,
                                "A label is attached twice.");
                        else labels.Add(label, i);
            for (int i = 0; i < code.Count; i++)
            {
                AddSuccessors(code, i, labels, graph, report);
                AddExceptionEntry(code[i], graph, i);
            }
            return graph;
        }

        private static void AddSuccessors(
            IList<CodeInstruction> code, int index, IDictionary<Label, int> labels,
            IlControlFlowGraph graph, IlVerificationReport report)
        {
            CodeInstruction instruction = code[index];
            if (instruction.opcode == OpCodes.Switch)
            {
                var targets = instruction.operand as IEnumerable<Label>;
                if (targets == null)
                    report.Error(PatchDiagnosticCode.InvalidSwitchTarget, index, index + 1,
                        "Switch has no label table.");
                else
                    try
                    {
                        foreach (Label target in targets)
                            AddTarget(target, index, labels, graph, report, PatchDiagnosticCode.InvalidSwitchTarget);
                    }
                    catch (Exception exception) { report.Fault(index, exception); }
                AddSuccessor(code.Count, graph, index, index + 1);
                return;
            }

            FlowControl flow = instruction.opcode.FlowControl;
            if (flow == FlowControl.Branch || flow == FlowControl.Cond_Branch)
            {
                if (!(instruction.operand is Label))
                {
                    report.Error(PatchDiagnosticCode.InvalidBranchTarget, index, index + 1,
                        "Branch has no label operand.");
                    return;
                }
                AddTarget((Label)instruction.operand, index, labels, graph, report,
                    PatchDiagnosticCode.InvalidBranchTarget);
                if (flow == FlowControl.Cond_Branch && index + 1 < code.Count)
                    AddSuccessor(code.Count, graph, index, index + 1);
            }
            else if (!IsTerminal(instruction.opcode)) AddSuccessor(code.Count, graph, index, index + 1);
        }

        private static void AddExceptionEntry(CodeInstruction instruction, IlControlFlowGraph graph, int index)
        {
            if (instruction.blocks == null) return;
            foreach (ExceptionBlock block in instruction.blocks)
            {
                bool catchEntry = block.blockType == ExceptionBlockType.BeginCatchBlock;
                bool filterEntry = block.blockType == ExceptionBlockType.BeginExceptFilterBlock;
                bool emptyEntry = block.blockType == ExceptionBlockType.BeginFinallyBlock ||
                    block.blockType == ExceptionBlockType.BeginFaultBlock;
                if (!catchEntry && !filterEntry && !emptyEntry) continue;
                var entry = new IlStackState();
                if (catchEntry || filterEntry)
                    entry.Values.Add(IlStackType.FromClr(catchEntry
                        ? block.catchType ?? typeof(Exception) : typeof(Exception)));
                graph.HandlerEntries[index] = entry;
            }
        }

        private static void AddTarget(
            Label target, int index, IDictionary<Label, int> labels, IlControlFlowGraph graph,
            IlVerificationReport report, PatchDiagnosticCode code)
        {
            int targetIndex;
            if (!labels.TryGetValue(target, out targetIndex))
            {
                report.Error(code, index, index + 1,
                    "A branch target label is not attached to an instruction.");
                return;
            }
            AddSuccessor(graph.Successors.Length, graph, index, targetIndex);
        }

        private static void AddSuccessor(int count, IlControlFlowGraph graph, int source, int target)
        {
            if (target >= 0 && target < count && !graph.Successors[source].Contains(target))
                graph.Successors[source].Add(target);
        }

        private static void MarkReachable(IlControlFlowGraph graph)
        {
            var worklist = new Queue<int>(graph.HandlerEntries.Keys);
            foreach (int entry in graph.HandlerEntries.Keys) graph.Reachable[entry] = true;
            if (graph.Reachable.Length != 0 && !graph.Reachable[0])
            {
                graph.Reachable[0] = true;
                worklist.Enqueue(0);
            }
            while (worklist.Count != 0)
            {
                int index = worklist.Dequeue();
                foreach (int successor in graph.Successors[index])
                    if (!graph.Reachable[successor])
                    {
                        graph.Reachable[successor] = true;
                        worklist.Enqueue(successor);
                    }
            }
        }

        private static void ValidatePrefixes(
            IList<CodeInstruction> code, IList<CodeInstruction> original,
            IList<IlResolvedEdit> edits, IlVerificationReport report)
        {
            for (int i = 0; i < code.Count; i++)
            {
                if (!IsPrefix(code[i].opcode)) continue;
                int start = i;
                while (i + 1 < code.Count && IsPrefix(code[i + 1].opcode)) i++;
                int terminal = i + 1;
                if (terminal >= code.Count)
                {
                    PrefixError(report, start, "A prefix group has no terminal instruction.");
                    continue;
                }
                for (int index = start; index < terminal; index++)
                {
                    OpCode prefix = code[index].opcode;
                    if (index > start && HasMetadata(code[index]))
                        PrefixError(report, index, "A prefix group has an interior label boundary.");
                    for (int prior = start; prior < index; prior++)
                        if (code[prior].opcode == prefix)
                            PrefixError(report, index, "A prefix group contains a duplicate prefix.");
                    MethodBase method = code[terminal].operand as MethodBase;
                    bool follower = prefix == OpCodes.Constrained
                        ? ValidConstrainedType(code[index].operand as Type) &&
                            method != null && !method.IsStatic && code[terminal].opcode == OpCodes.Callvirt
                        : prefix == OpCodes.Tailcall
                            ? IsAny(code[terminal].opcode, OpCodes.Call, OpCodes.Callvirt, OpCodes.Calli)
                            : prefix == OpCodes.Readonly
                                ? code[terminal].opcode == OpCodes.Ldelema : IsMemoryOpcode(code[terminal].opcode);
                    PrefixError(report, index, follower ? null :
                        "The prefix has an incompatible terminal instruction.");
                    if (follower && prefix == OpCodes.Readonly)
                        report.Error(PatchDiagnosticCode.UnsupportedMethodContext, index, index + 1,
                            "readonly. ldelema is outside the verifier safety boundary.");
                    if (prefix == OpCodes.Unaligned && !ValidUnalignedOperand(code[index].operand))
                        PrefixError(report, index, "unaligned. requires an alignment of 1, 2, or 4.");
                }
            }
            if (edits == null) return;
            foreach (IlResolvedEdit edit in edits)
            {
                if (edit == null || edit.Operation.Kind == IlEditKind.InsertGuardBeforeCall ||
                    edit.Start < 0 || edit.Start >= original.Count) continue;
                int groupStart = edit.Start;
                bool touchesGroup = IsPrefix(original[groupStart].opcode);
                while (groupStart > 0 && IsPrefix(original[groupStart - 1].opcode))
                {
                    groupStart--;
                    touchesGroup = true;
                }
                if (!touchesGroup) continue;
                int terminal = groupStart;
                while (terminal < original.Count && IsPrefix(original[terminal].opcode)) terminal++;
                if (edit.Start != terminal || edit.EndExclusive != terminal + 1)
                    report.Error(PatchDiagnosticCode.InvalidPrefix, edit.Start, edit.EndExclusive,
                        "An edit must replace a prefix group terminal without splitting the group.");
            }
        }
        private static void PrefixError(IlVerificationReport report, int index, string detail)
        { if (detail != null) report.Error(PatchDiagnosticCode.InvalidPrefix, index, index + 1, detail); }
        internal static bool HasMetadata(CodeInstruction instruction) =>
            instruction.labels != null && instruction.labels.Count != 0 ||
            instruction.blocks != null && instruction.blocks.Count != 0;
        private static bool IsMemoryOpcode(OpCode opcode)
        {
            string name = opcode.Name;
            return name == "ldfld" || name == "stfld" || name == "ldsfld" || name == "stsfld" ||
                name == "ldflda" || name == "ldsflda" || name == "ldobj" || name == "stobj" ||
                name != null && (name.StartsWith("ldind.", StringComparison.Ordinal) ||
                    name.StartsWith("stind.", StringComparison.Ordinal));
        }
        private static bool ValidUnalignedOperand(object operand)
        {
            try
            {
                int value = Convert.ToInt32(operand);
                return value == 1 || value == 2 || value == 4;
            }
            catch { return false; }
        }
        private static bool ValidConstrainedType(Type type)
        {
            return type != null && type != typeof(void) && !type.IsByRef &&
                !type.ContainsGenericParameters;
        }
        private static IlExceptionContext[] BuildRegions(
            IList<CodeInstruction> code, IlVerificationReport report)
        {
            var regions = new IlExceptionContext[code.Count];
            var active = new List<IlExceptionClause>();
            for (int index = 0; index < code.Count; index++)
            {
                IList<ExceptionBlock> blocks = code[index].blocks;
                IlExceptionClause terminalEnd = null;
                foreach (ExceptionBlock block in blocks ?? Enumerable.Empty<ExceptionBlock>())
                {
                    switch (block.blockType)
                    {
                        case ExceptionBlockType.BeginExceptionBlock:
                            active.Add(new IlExceptionClause(block.blockType, index)); break;
                        case ExceptionBlockType.BeginCatchBlock:
                        case ExceptionBlockType.BeginExceptFilterBlock:
                        case ExceptionBlockType.BeginFinallyBlock:
                        case ExceptionBlockType.BeginFaultBlock:
                            ChangeRegion(active, block.blockType, report, index); break;
                        case ExceptionBlockType.EndExceptionBlock:
                            if (IsTerminal(code[index].opcode))
                            {
                                if (active.Count == 0)
                                    EndRegion(active, report, index, index + 1);
                                else
                                {
                                    terminalEnd = active[active.Count - 1];
                                    terminalEnd.EndExclusive = index + 1;
                                }
                            }
                            else EndRegion(active, report, index, index);
                            break;
                    }
                }
                regions[index] = new IlExceptionContext(active);
                if (terminalEnd != null) active.Remove(terminalEnd);
            }
            if (active.Count != 0)
                report.Error(PatchDiagnosticCode.InvalidExceptionRegion, null, null,
                    "An exception region was not closed.");
            return regions;
        }
        private static void EndRegion(
            IList<IlExceptionClause> active, IlVerificationReport report, int markerIndex, int endExclusive)
        {
            if (active.Count == 0)
                report.Error(PatchDiagnosticCode.InvalidExceptionRegion, markerIndex, markerIndex + 1,
                    "An exception region ended without an active region.");
            else
            {
                active[active.Count - 1].EndExclusive = endExclusive;
                active.RemoveAt(active.Count - 1);
            }
        }
        private static void ChangeRegion(
            IList<IlExceptionClause> active, ExceptionBlockType kind,
            IlVerificationReport report, int index)
        {
            if (active.Count == 0)
            {
                report.Error(PatchDiagnosticCode.InvalidExceptionRegion, index, index + 1,
                    "An exception handler began without a protected region.");
                return;
            }
            IlExceptionClause current = active[active.Count - 1];
            current.EndExclusive = index;
            active[active.Count - 1] = new IlExceptionClause(kind, index);
        }
        private static void ValidateRegionEdges(
            IList<CodeInstruction> code, IlControlFlowGraph graph, IlExceptionContext[] regions,
            IlVerificationReport report)
        {
            for (int index = 0; index < code.Count; index++)
            {
                IlExceptionContext source = regions[index];
                string name = code[index].opcode.Name;
                string contextError = name == "rethrow" &&
                    !source.IsHandlerKind(ExceptionBlockType.BeginCatchBlock, index)
                    ? "rethrow is only valid inside a catch handler."
                    : name == "endfinally" &&
                        !source.IsHandlerKind(ExceptionBlockType.BeginFinallyBlock, index) &&
                        !source.IsHandlerKind(ExceptionBlockType.BeginFaultBlock, index)
                            ? "endfinally is only valid inside a finally or fault handler."
                            : name == "endfilter" &&
                                !source.IsHandlerKind(ExceptionBlockType.BeginExceptFilterBlock, index)
                                ? "endfilter is only valid inside a filter." : null;
                if (contextError != null)
                    report.Error(PatchDiagnosticCode.InvalidExceptionRegion, index, index + 1,
                        contextError);
                foreach (int target in graph.Successors[index])
                {
                    if (!graph.Reachable[index] && IsImplicitFallthrough(code, graph, index, target) &&
                        code[target].blocks != null && code[target].blocks.Any(block =>
                            IsHandlerStart(block.blockType))) continue;
                    if (code[target].blocks != null && code[target].blocks.Any(block =>
                        block.blockType == ExceptionBlockType.BeginExceptionBlock) &&
                        IsImplicitFallthrough(code, graph, index, target))
                        continue;
                    if (name.StartsWith("leave", StringComparison.Ordinal)
                        ? !source.CanLeaveTo(regions[target]) : !source.SameAs(regions[target]))
                        report.Error(PatchDiagnosticCode.InvalidExceptionRegion, index, index + 1,
                            "Control flow illegally crosses a protected-region boundary.");
                }
            }
        }
        private static bool IsImplicitFallthrough(
            IList<CodeInstruction> code, IlControlFlowGraph graph, int source, int target)
        {
            if (target != source + 1 || code[source].opcode.FlowControl == FlowControl.Branch) return false;
            if (code[source].opcode.FlowControl == FlowControl.Next) return true;
            if (code[source].operand is Label)
                return !graph.Labels.TryGetValue((Label)code[source].operand, out var branch) || branch != target;
            var labels = code[source].operand as IEnumerable<Label>;
            return labels == null || !labels.Any(value =>
                graph.Labels.TryGetValue(value, out var index) && index == target);
        }
        private static void ValidateShortBranches(
            IList<CodeInstruction> code, IlControlFlowGraph graph, IlVerificationReport report)
        {
            int[] offsets = new int[code.Count];
            for (int index = 1; index < code.Count; index++)
                offsets[index] = offsets[index - 1] + InstructionSize(code[index - 1]);
            for (int index = 0; index < code.Count; index++)
            {
                if (code[index].opcode.OperandType != OperandType.ShortInlineBrTarget ||
                    !(code[index].operand is Label)) continue;
                int target;
                if (!graph.Labels.TryGetValue((Label)code[index].operand, out target)) continue;
                int displacement = offsets[target] - offsets[index] - InstructionSize(code[index]);
                if (displacement < SByte.MinValue || displacement > SByte.MaxValue)
                    report.Error(PatchDiagnosticCode.InvalidBranchTarget, index, index + 1,
                        "A short branch displacement is outside the signed-byte range.");
            }
        }

        private static int InstructionSize(CodeInstruction instruction)
        {
            int operandSize = 0;
            switch (instruction.opcode.OperandType)
            {
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: operandSize = 1; break;
                case OperandType.ShortInlineR: operandSize = 4; break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType: operandSize = 4; break;
                case OperandType.InlineVar: operandSize = 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: operandSize = 8; break;
                case OperandType.InlineSwitch:
                    try { operandSize = 4 + ((IEnumerable<Label>)instruction.operand).Count() * 4; }
                    catch { operandSize = 4; }
                    break;
            }
            return instruction.opcode.Size + operandSize;
        }

        private static List<Tuple<int, ExceptionBlock>> ExceptionTopology(IList<CodeInstruction> code)
        {
            var result = new List<Tuple<int, ExceptionBlock>>();
            int depth = 0;
            for (int i = 0; i < code.Count; i++)
                foreach (ExceptionBlock block in code[i].blocks ?? Enumerable.Empty<ExceptionBlock>())
                {
                    if (block.blockType == ExceptionBlockType.BeginExceptionBlock)
                        depth++;
                    else if (block.blockType == ExceptionBlockType.EndExceptionBlock)
                    {
                        if (depth == 0) return null;
                        depth--;
                    }
                    else if (depth == 0 && IsHandlerStart(block.blockType)) return null;
                    result.Add(Tuple.Create(i, block));
                }
            return depth == 0 ? result : null;
        }
        internal static bool IsHandlerStart(ExceptionBlockType type) =>
            type == ExceptionBlockType.BeginCatchBlock || type == ExceptionBlockType.BeginExceptFilterBlock ||
            type == ExceptionBlockType.BeginFinallyBlock || type == ExceptionBlockType.BeginFaultBlock;
        internal static bool IsExceptionEntry(ExceptionBlock block) =>
        block.blockType == ExceptionBlockType.BeginExceptionBlock || IsHandlerStart(block.blockType);
        private static void CompareMetadata(
            IList<CodeInstruction> original, IList<CodeInstruction> candidate,
            IList<IlResolvedEdit> edits, IlVerificationReport report)
        {
            List<Tuple<int, ExceptionBlock>> expected = ExceptionTopology(original);
            List<Tuple<int, ExceptionBlock>> actual = ExceptionTopology(candidate);
            if (expected == null || actual == null || expected.Count != actual.Count)
                report.Error(PatchDiagnosticCode.InvalidExceptionRegion, null, null,
                    "Exception-block topology is not balanced.");
            else
                for (int i = 0; i < expected.Count; i++)
                    if (MapMetadataIndex(expected[i].Item1, edits, true) != actual[i].Item1 ||
                        expected[i].Item2.blockType != actual[i].Item2.blockType ||
                        expected[i].Item2.catchType != actual[i].Item2.catchType)
                    {
                        report.Error(PatchDiagnosticCode.MetadataNotPreserved,
                            expected[i].Item1, expected[i].Item1 + 1,
                            "Exception-block position, nesting, or handler pairing changed.");
                        break;
            }
            Dictionary<Label, int> originalLabels = LabelCounts(original);
            Dictionary<Label, int> candidateLabels = LabelCounts(candidate);
            foreach (KeyValuePair<Label, int> pair in originalLabels)
            {
                int count;
                if (!candidateLabels.TryGetValue(pair.Key, out count) || count != pair.Value)
                {
                    report.Error(PatchDiagnosticCode.MetadataNotPreserved, null, null,
                        "An original label was lost or duplicated.");
                    break;
                }
            }
            for (int i = 0; i < original.Count; i++)
            {
                if (original[i].labels == null || original[i].labels.Count == 0) continue;
                int mapped = MapMetadataIndex(i, edits, false);
                if (mapped < 0 || mapped >= candidate.Count ||
                    !candidate[mapped].labels.Intersect(original[i].labels).Any())
                {
                    report.Error(PatchDiagnosticCode.MetadataNotPreserved, i, i + 1,
                        "A label moved to a different instruction boundary.");
                    break;
                }
            }
        }
        private static int MapMetadataIndex(int originalIndex, IList<IlResolvedEdit> edits, bool blocks)
        {
            if (edits == null) return originalIndex;
            int delta = 0;
            foreach (IlResolvedEdit edit in edits.OrderBy(e => e.Start))
            {
                if (originalIndex < edit.Start) break;
                if (edit.Start == edit.EndExclusive)
                {
                    if (edit.IsGuardEntry(originalIndex, blocks) ||
                        edit.Operation.Kind != IlEditKind.InsertGuardBeforeCall && originalIndex == edit.Start)
                        return edit.Start + delta;
                    delta += edit.Replacement.Count;
                }
                else if (originalIndex < edit.EndExclusive)
                    return originalIndex == edit.Start && edit.Replacement.Count != 0
                        ? edit.Start + delta : -1;
                else delta += edit.Replacement.Count - (edit.EndExclusive - edit.Start);
            }
            return originalIndex + delta;
        }
        private static Dictionary<Label, int> LabelCounts(IList<CodeInstruction> code)
        {
            var result = new Dictionary<Label, int>();
            foreach (CodeInstruction instruction in code)
                if (instruction.labels != null)
                    foreach (Label label in instruction.labels)
                        result[label] = result.ContainsKey(label) ? result[label] + 1 : 1;
            return result;
        }
        private static List<Type> Inputs(MethodBase method, bool receiver)
        {
            var result = new List<Type>();
            if (receiver && method.DeclaringType != null) result.Add(method.DeclaringType);
            foreach (ParameterInfo parameter in method.GetParameters())
                result.Add(parameter.ParameterType);
            return result;
        }
        private static bool TypesCompatible(Type actual, Type expected)
        {
            return actual == expected || actual != null && expected != null && actual.IsByRef == expected.IsByRef &&
                (!actual.IsByRef ? expected.IsAssignableFrom(actual) :
                    actual.GetElementType() == expected.GetElementType());
        }
        private static bool IsInt32Like(Type type)
        {
            if (type == null) return false;
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            return type == typeof(int) || type == typeof(uint) || type == typeof(short) || type == typeof(ushort) ||
                type == typeof(byte) || type == typeof(sbyte);
        }
        private static bool IsAny(OpCode value, params OpCode[] values) => Array.IndexOf(values, value) >= 0;
        internal static bool IsPrefix(OpCode opcode)
        { return IsAny(opcode, OpCodes.Constrained, OpCodes.Readonly, OpCodes.Tailcall,
                OpCodes.Unaligned, OpCodes.Volatile);
        }
        private static bool IsTerminal(OpCode opcode)
        { return IsAny(opcode, OpCodes.Ret, OpCodes.Throw, OpCodes.Rethrow, OpCodes.Endfinally,
                OpCodes.Endfilter, OpCodes.Br, OpCodes.Br_S, OpCodes.Leave, OpCodes.Leave_S);
        }
    }
}
