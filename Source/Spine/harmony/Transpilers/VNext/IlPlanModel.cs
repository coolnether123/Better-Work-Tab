using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Spine.Harmony.Transpilers.VNext
{
    /// <summary>Controls how many matching instruction sites a recipe may edit.</summary>
    public enum MatchMode
    {
        ExactlyOne,
        FirstMatch
    }

    /// <summary>Controls whether a missing requested edit is an error or a clean no-op.</summary>
    public enum PatchRequirement
    {
        Required,
        Optional
    }

    internal enum IlComparison
    {
        GreaterOrEqual,
        LessThan,
        LessThanOrEqual
    }

    internal sealed class IlPredicate
    {
        private readonly Func<CodeInstruction, bool> _matches;
        private IlPredicate(Func<CodeInstruction, bool> matches) { _matches = matches; }
        internal static IlPredicate Opcode(OpCode value)
        { return new IlPredicate(instruction => instruction.opcode == value); }
        internal static IlPredicate OperandEquals(object value)
        { return new IlPredicate(instruction => Object.Equals(instruction.operand, value)); }
        internal static IlPredicate Int32Constant(int value)
        { return new IlPredicate(instruction => ConstantValue(instruction) == value); }
        internal static IlPredicate Argument(int index)
        { return new IlPredicate(instruction => IsArgumentLoad(instruction, index)); }
        internal static IlPredicate ComparisonBranch(IlComparison comparison)
        {
            return new IlPredicate(instruction => IsComparisonBranch(instruction, comparison));
        }
        internal static IlPredicate LocalLoad()
        { return new IlPredicate(instruction => IsLocal(instruction, "ldloc")); }
        internal static IlPredicate LocalStore()
        { return new IlPredicate(instruction => IsLocal(instruction, "stloc")); }
        internal static IlPredicate Call(MethodBase method, bool includeCallvirt = true)
        {
            if (method == null) throw new ArgumentNullException("method");
            return new IlPredicate(instruction => IsCall(instruction, method, includeCallvirt));
        }
        internal static IlPredicate Field(FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException("field");
            return new IlPredicate(instruction => IsField(instruction, field));
        }
        internal static IlPredicate AllOf(params IlPredicate[] values) { return Composite(values, true); }
        internal static IlPredicate AnyOf(params IlPredicate[] values)
        { return Composite(values, false); }
        internal static IlPredicate Not(IlPredicate value)
        {
            if (value == null) throw new ArgumentNullException("value");
            return new IlPredicate(instruction => !value.Matches(instruction));
        }
        internal bool Matches(CodeInstruction instruction)
        { return instruction != null && _matches(instruction); }
        private static IlPredicate Composite(IEnumerable<IlPredicate> values, bool all)
        {
            if (values == null) throw new ArgumentNullException("values");
            var list = new List<IlPredicate>(values);
            if (list.Count == 0 || list.Exists(value => value == null))
                throw new ArgumentException("Non-null children are required.", "values");
            return new IlPredicate(instruction => all
                ? list.TrueForAll(child => child.Matches(instruction))
                : list.Exists(child => child.Matches(instruction)));
        }
        private static bool IsLocal(CodeInstruction instruction, string prefix)
        {
            var name = instruction.opcode.Name;
            return name != null && (name == prefix ||
                name.StartsWith(prefix + ".", StringComparison.Ordinal));
        }
        private static bool IsArgumentLoad(CodeInstruction instruction, int index)
        {
            if (instruction == null) return false;
            switch (index)
            {
                case 0: return instruction.opcode == OpCodes.Ldarg_0;
                case 1: return instruction.opcode == OpCodes.Ldarg_1;
                case 2: return instruction.opcode == OpCodes.Ldarg_2 ||
                    IsShortArgument(instruction, 2);
                case 3: return instruction.opcode == OpCodes.Ldarg_3;
                default: return instruction.opcode == OpCodes.Ldarg &&
                    ArgumentIndex(instruction) == index || IsShortArgument(instruction, index);
            }
        }
        private static bool IsShortArgument(CodeInstruction instruction, int index)
        {
            if (instruction.opcode != OpCodes.Ldarg_S) return false;
            try { return Convert.ToInt32(instruction.operand) == index; }
            catch (Exception) { return false; }
        }
        private static int ArgumentIndex(CodeInstruction instruction)
        {
            try { return Convert.ToInt32(instruction.operand); }
            catch (Exception) { return -1; }
        }
        private static bool IsComparisonBranch(CodeInstruction instruction, IlComparison comparison)
        {
            if (instruction == null) return false;
            switch (comparison)
            {
                case IlComparison.GreaterOrEqual:
                    return instruction.opcode == OpCodes.Bge || instruction.opcode == OpCodes.Bge_S;
                case IlComparison.LessThan:
                    return instruction.opcode == OpCodes.Blt || instruction.opcode == OpCodes.Blt_S;
                case IlComparison.LessThanOrEqual:
                    return instruction.opcode == OpCodes.Ble || instruction.opcode == OpCodes.Ble_S;
                default: return false;
            }
        }
        internal static bool IsCall(
            CodeInstruction instruction, MethodBase method, bool includeCallvirt = true)
        {
            return Object.Equals(instruction.operand, method) &&
                (instruction.opcode == OpCodes.Call ||
                    includeCallvirt && instruction.opcode == OpCodes.Callvirt);
        }
        private static bool IsField(CodeInstruction instruction, FieldInfo field)
        {
            return Object.Equals(instruction.operand, field) &&
                (instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldsfld ||
                    instruction.opcode == OpCodes.Ldflda || instruction.opcode == OpCodes.Ldsflda ||
                    instruction.opcode == OpCodes.Stfld || instruction.opcode == OpCodes.Stsfld);
        }
        private static int? ConstantValue(CodeInstruction instruction)
        {
            var opcode = instruction.opcode;
            if (opcode == OpCodes.Ldc_I4_M1) return -1;
            if (opcode == OpCodes.Ldc_I4_0) return 0;
            if (opcode == OpCodes.Ldc_I4_1) return 1;
            if (opcode == OpCodes.Ldc_I4_2) return 2;
            if (opcode == OpCodes.Ldc_I4_3) return 3;
            if (opcode == OpCodes.Ldc_I4_4) return 4;
            if (opcode == OpCodes.Ldc_I4_5) return 5;
            if (opcode == OpCodes.Ldc_I4_6) return 6;
            if (opcode == OpCodes.Ldc_I4_7) return 7;
            if (opcode == OpCodes.Ldc_I4_8) return 8;
            try
            {
                if (opcode == OpCodes.Ldc_I4_S) return Convert.ToSByte(instruction.operand);
                if (opcode == OpCodes.Ldc_I4) return Convert.ToInt32(instruction.operand);
            }
            catch (Exception)
            {
            }
            return null;
        }
    }

    internal sealed class IlAnchor
    {
        private readonly ReadOnlyCollection<IlPredicate> _predicates;
        private readonly Func<IList<CodeInstruction>, int, bool> _sequenceMatcher;
        internal IlAnchor(string id, IEnumerable<IlPredicate> predicates, MatchMode cardinality)
            : this(id, predicates, cardinality, null) { }
        private IlAnchor(
            string id, IEnumerable<IlPredicate> predicates, MatchMode cardinality,
            Func<IList<CodeInstruction>, int, bool> matcher)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentException("An anchor ID is required.", "id");
            if (predicates == null) throw new ArgumentNullException("predicates");
            var list = new List<IlPredicate>(predicates);
            if (list.Count == 0 || list.Exists(value => value == null))
                throw new ArgumentException("An anchor needs predicates.", "predicates");
            ValidateMatchMode(cardinality);
            Id = id.Trim();
            Cardinality = cardinality;
            _predicates = new ReadOnlyCollection<IlPredicate>(list);
            _sequenceMatcher = matcher;
        }
        internal string Id { get; private set; }
        internal MatchMode Cardinality { get; private set; }
        internal IReadOnlyList<IlPredicate> Predicates { get { return _predicates; } }
        internal static IlAnchor Sequence(string id, MatchMode cardinality, params IlPredicate[] predicates)
        { return new IlAnchor(id, predicates, cardinality); }
        internal IlAnchor WithSequenceMatcher(Func<IList<CodeInstruction>, int, bool> matcher)
        {
            if (matcher == null) throw new ArgumentNullException("matcher");
            return new IlAnchor(Id, _predicates, Cardinality, matcher);
        }
        internal IlAnchor WithLocalRelationship(int storeOffset, int loadOffset)
        {
            if (storeOffset < 0 || loadOffset < 0 || storeOffset >= _predicates.Count ||
                loadOffset >= _predicates.Count)
                throw new ArgumentOutOfRangeException("storeOffset");
            return WithSequenceMatcher((instructions, start) =>
                SameLocal(instructions[start + storeOffset], instructions[start + loadOffset]));
        }
        internal List<IlMatch> Resolve(
            IList<CodeInstruction> instructions, bool enforceMatcher = true)
        { return ResolveCore(instructions, enforceMatcher); }
        private List<IlMatch> ResolveCore(IList<CodeInstruction> instructions, bool enforceMatcher)
        {
            var result = new List<IlMatch>();
            if (instructions == null || _predicates.Count > instructions.Count) return result;
            for (var start = 0; start <= instructions.Count - _predicates.Count; start++)
            {
                var matched = true;
                for (var offset = 0; offset < _predicates.Count; offset++)
                {
                    if (!_predicates[offset].Matches(instructions[start + offset]))
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched && (!enforceMatcher || _sequenceMatcher == null ||
                    _sequenceMatcher(instructions, start)))
                    result.Add(new IlMatch(start, start + _predicates.Count));
            }
            return result;
        }
        internal static void ValidateMatchMode(MatchMode cardinality)
        {
            if (cardinality != MatchMode.ExactlyOne && cardinality != MatchMode.FirstMatch)
                throw new ArgumentOutOfRangeException("cardinality");
        }

        private static bool SameLocal(CodeInstruction store, CodeInstruction load)
        {
            LocalBuilder storeBuilder = store.operand as LocalBuilder;
            LocalBuilder loadBuilder = load.operand as LocalBuilder;
            if (storeBuilder != null || loadBuilder != null)
                return storeBuilder != null && loadBuilder != null &&
                    Object.ReferenceEquals(storeBuilder, loadBuilder);
            int storeIndex;
            int loadIndex;
            return TryLocalIndex(store, out storeIndex) && TryLocalIndex(load, out loadIndex) &&
                storeIndex == loadIndex;
        }

        private static bool TryLocalIndex(CodeInstruction instruction, out int index)
        {
            index = -1;
            string name = instruction == null || instruction.opcode == null
                ? null : instruction.opcode.Name;
            if (name == "stloc.0" || name == "ldloc.0") { index = 0; return true; }
            if (name == "stloc.1" || name == "ldloc.1") { index = 1; return true; }
            if (name == "stloc.2" || name == "ldloc.2") { index = 2; return true; }
            if (name == "stloc.3" || name == "ldloc.3") { index = 3; return true; }
            if (name != null && (name == "stloc" || name == "ldloc" ||
                name == "stloc.s" || name == "ldloc.s"))
            {
                try { index = Convert.ToInt32(instruction.operand); return true; }
                catch (Exception) { return false; }
            }
            return false;
        }
    }

    internal enum IlEditKind
    {
        ReplaceCall,
        ReplaceInstructionWithCall,
        InsertGuardBeforeCall
    }

    internal sealed class IlEditDescription
    {
        private readonly List<CodeInstruction> _replacement;
        private IlEditDescription(
            string operationId, string anchorId, IlEditKind kind,
            IEnumerable<CodeInstruction> replacement, MethodBase replacementMethod,
            PatchRequirement requirement, int offset = -1, IlPredicate expected = null,
            MethodBase guardSource = null, Label fallback = default(Label),
            Label skip = default(Label), IlPredicate guardPrefix = null)
        {
            if (String.IsNullOrWhiteSpace(operationId) || String.IsNullOrWhiteSpace(anchorId))
                throw new ArgumentException("Operation and anchor IDs are required.");
            ValidateRequirement(requirement);
            OperationId = operationId.Trim();
            AnchorId = anchorId.Trim();
            Kind = kind;
            ReplacementMethod = replacementMethod;
            Requirement = requirement;
            MatchOffset = offset;
            ExpectedInstruction = expected;
            GuardSourceMethod = guardSource;
            GuardFallbackLabel = fallback;
            GuardSkipLabel = skip;
            GuardInsertionPrefix = guardPrefix;
            _replacement = IlInstructionSnapshot.CloneList(replacement);
        }
        internal string OperationId { get; private set; }
        internal string AnchorId { get; private set; }
        internal IlEditKind Kind { get; private set; }
        internal MethodBase ReplacementMethod { get; private set; }
        internal PatchRequirement Requirement { get; private set; }
        internal bool Required { get { return Requirement == PatchRequirement.Required; } }
        internal int MatchOffset { get; private set; }
        internal IlPredicate ExpectedInstruction { get; private set; }
        internal MethodBase GuardSourceMethod { get; private set; }
        internal Label GuardFallbackLabel { get; private set; }
        internal Label GuardSkipLabel { get; private set; }
        internal IlPredicate GuardInsertionPrefix { get; private set; }
        internal static IlEditDescription ReplaceCall(
            string operationId, string anchorId, MethodBase method,
            PatchRequirement requirement = PatchRequirement.Required)
        {
            RequireMethod(method, "method");
            return new IlEditDescription(
                operationId, anchorId, IlEditKind.ReplaceCall, null, method, requirement);
        }
        internal static IlEditDescription ReplaceInstructionWithCall(
            string operationId, string anchorId, int offset, IlPredicate expected,
            MethodInfo method, PatchRequirement requirement = PatchRequirement.Required)
        {
            if (offset < 0 || expected == null)
                throw new ArgumentException("A valid offset and expected instruction are required.");
            RequireMethod(method, "method");
            return new IlEditDescription(
                operationId, anchorId, IlEditKind.ReplaceInstructionWithCall,
                null, method, requirement, offset, expected);
        }
        internal static IlEditDescription InsertGuardBeforeCall(
            string operationId, string anchorId, int offset, MethodInfo source,
            IEnumerable<CodeInstruction> guard, Label fallback, Label skip,
            PatchRequirement requirement = PatchRequirement.Required, IlPredicate prefix = null)
        {
            if (offset < 0 || source == null || guard == null)
                throw new ArgumentException("A valid guard definition is required.");
            return new IlEditDescription(
                operationId, anchorId, IlEditKind.InsertGuardBeforeCall,
                guard, null, requirement, offset, null, source, fallback, skip, prefix);
        }
        internal List<CodeInstruction> CloneReplacement()
        { return IlInstructionSnapshot.CloneList(_replacement); }
        private static void RequireMethod(MethodBase method, string name)
        { if (method == null) throw new ArgumentNullException(name); }
        internal static void ValidateRequirement(PatchRequirement requirement)
        {
            if (requirement != PatchRequirement.Required && requirement != PatchRequirement.Optional)
                throw new ArgumentOutOfRangeException("requirement");
        }
    }

    internal enum IlMarkerStatus
    {
        None,
        Applied,
        Ambiguous,
        Unproven
    }

    internal sealed class IlIdempotencyMarker
    {
        private readonly ReadOnlyCollection<IlAnchor> _anchors;
        internal IlIdempotencyMarker(IEnumerable<IlAnchor> markers)
        {
            if (markers == null) throw new ArgumentNullException("markers");
            var list = new List<IlAnchor>(markers);
            if (list.Count == 0 || list.Exists(marker => marker == null))
                throw new ArgumentException("At least one marker is required.", "markers");
            _anchors = new ReadOnlyCollection<IlAnchor>(list);
        }
        internal IlMarkerStatus Inspect(IList<CodeInstruction> instructions, IlTranspilerPlan plan)
        {
            if (instructions == null || plan == null) return IlMarkerStatus.Unproven;
            var provenOperations = new HashSet<IlEditDescription>();
            foreach (var marker in _anchors)
            {
                var shape = marker.Resolve(instructions, false);
                if (shape.Count > 1) return IlMarkerStatus.Ambiguous;
                if (shape.Count == 0) return IlMarkerStatus.None;
                var exact = marker.Resolve(instructions);
                if (exact.Count != 1) return IlMarkerStatus.Unproven;
                var operation = ProvesTopology(marker, exact[0], instructions, plan);
                if (operation == null || !provenOperations.Add(operation))
                    return IlMarkerStatus.Unproven;
            }
            foreach (var operation in plan.Operations)
            {
                if (!provenOperations.Contains(operation) &&
                    (operation.Required || plan.AnchorFor(operation.AnchorId).Resolve(instructions).Count != 0))
                    return IlMarkerStatus.Unproven;
            }
            return IlMarkerStatus.Applied;
        }
        private static IlEditDescription ProvesTopology(
            IlAnchor marker, IlMatch match, IList<CodeInstruction> code, IlTranspilerPlan plan)
        {
            IlEditDescription proven = null;
            foreach (var operation in plan.Operations)
            {
                if (!(operation.Kind == IlEditKind.InsertGuardBeforeCall
                    ? ProvesGuard(marker, match, code, plan, operation)
                    : ProvesReplacement(marker, match, code, plan, operation))) continue;
                if (proven != null) return null;
                proven = operation;
            }
            return proven;
        }
        private static bool ProvesGuard(
            IlAnchor marker, IlMatch match, IList<CodeInstruction> code,
            IlTranspilerPlan plan, IlEditDescription operation)
        {
            var first = -1;
            var last = -1;
            for (var i = match.Start; i < match.EndExclusive; i++)
                if (code[i].opcode.FlowControl == FlowControl.Branch ||
                    code[i].opcode.FlowControl == FlowControl.Cond_Branch)
                {
                    if (first < 0) first = i;
                    last = i;
                }
            if (first < 0 || last <= first || !(code[first].operand is Label) ||
                !(code[last].operand is Label)) return false;
            var fallback = (Label)code[first].operand;
            var skip = (Label)code[last].operand;
            var fallbackIndex = match.EndExclusive;
            if (fallbackIndex >= code.Count || UniqueLabelTarget(code, fallback) != fallbackIndex)
                return false;
            var sourceIndex = fallbackIndex;
            if (operation.GuardInsertionPrefix != null)
            {
                if (sourceIndex >= code.Count ||
                    !operation.GuardInsertionPrefix.Matches(code[sourceIndex])) return false;
                sourceIndex++;
            }
            if (sourceIndex >= code.Count || !IsCall(code[sourceIndex], operation.GuardSourceMethod) ||
                UniqueLabelTarget(code, skip) != sourceIndex + 1) return false;
            var sourceAnchor = plan.AnchorFor(operation.AnchorId);
            foreach (var source in sourceAnchor.Resolve(code))
            {
                if (source.Start < match.Start) return false;
                if (source.Start == sourceIndex) return true;
            }
            return false;
        }
        private static bool ProvesReplacement(
            IlAnchor marker, IlMatch match, IList<CodeInstruction> code,
            IlTranspilerPlan plan, IlEditDescription operation)
        {
            var source = plan.AnchorFor(operation.AnchorId);
            if (source.Resolve(code).Count != 0) return false;
            var offset = operation.Kind == IlEditKind.ReplaceCall ? 0 : operation.MatchOffset;
            var index = match.Start + offset;
            return offset >= 0 && offset < marker.Predicates.Count && index >= 0 &&
                index < code.Count && IsCall(code[index], operation.ReplacementMethod);
        }
        private static int UniqueLabelTarget(IList<CodeInstruction> code, Label label)
        {
            var target = -1;
            for (var i = 0; i < code.Count; i++)
            {
                if (code[i].labels == null || !code[i].labels.Contains(label)) continue;
                if (target >= 0) return -1;
                target = i;
            }
            return target;
        }
        private static bool IsCall(CodeInstruction instruction, MethodBase method)
        {
            return IlPredicate.IsCall(instruction, method);
        }
    }

    internal sealed class IlTranspilerPlan
    {
        private readonly ReadOnlyCollection<IlAnchor> _anchors;
        private readonly ReadOnlyCollection<IlEditDescription> _operations;
        internal IlTranspilerPlan(
            string id, IEnumerable<IlAnchor> anchors, IEnumerable<IlEditDescription> operations,
            IlIdempotencyMarker marker = null)
        {
            if (String.IsNullOrWhiteSpace(id) || anchors == null || operations == null)
                throw new ArgumentException("A plan ID, anchors, and operations are required.");
            var anchorList = new List<IlAnchor>(anchors);
            var operationList = new List<IlEditDescription>(operations);
            if (anchorList.Exists(anchor => anchor == null) ||
                operationList.Exists(operation => operation == null))
                throw new ArgumentException("Plans cannot contain null members.");
            var anchorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anchor in anchorList)
                if (!anchorIds.Add(anchor.Id))
                    throw new ArgumentException("Anchor IDs must be unique.", "anchors");
            var operationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in operationList)
                if (!operationIds.Add(operation.OperationId) || !anchorIds.Contains(operation.AnchorId))
                    throw new ArgumentException(
                        "Operation IDs and anchor references must be valid.", "operations");
            Id = id.Trim();
            _anchors = new ReadOnlyCollection<IlAnchor>(anchorList);
            _operations = new ReadOnlyCollection<IlEditDescription>(operationList);
            IdempotencyMarker = marker;
        }
        internal string Id { get; private set; }
        internal IReadOnlyList<IlAnchor> Anchors { get { return _anchors; } }
        internal IReadOnlyList<IlEditDescription> Operations { get { return _operations; } }
        internal IlIdempotencyMarker IdempotencyMarker { get; private set; }
        internal IlAnchor AnchorFor(string id)
        {
            foreach (var anchor in _anchors)
                if (String.Equals(anchor.Id, id, StringComparison.Ordinal)) return anchor;
            return null;
        }
    }

    internal sealed class IlMatch
    {
        internal IlMatch(int start, int endExclusive)
        {
            Start = start;
            EndExclusive = endExclusive;
        }
        internal int Start { get; private set; }
        internal int EndExclusive { get; private set; }
    }
}
