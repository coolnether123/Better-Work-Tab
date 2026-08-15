using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Better_Work_Tab.Transpilers.BwtExactProfile
{
    internal enum IlStackKind
    {
        Unknown,
        Int32,
        Int64,
        Float,
        NativeInt,
        Reference,
        Value,
        ManagedPointer
    }

    internal sealed class IlStackType
    {
        private static readonly string[] KindNames =
        {
            "unknown", "int32", "int64", "float", "native-int", "reference", "value-type", "managed-pointer"
        };
        private readonly bool _isNull;

        private IlStackType(IlStackKind kind, Type clrType, Type elementType, bool isNull = false)
        {
            Kind = kind;
            ClrType = clrType;
            ElementType = elementType;
            _isNull = isNull;
        }

        internal IlStackKind Kind { get; private set; }
        internal Type ClrType { get; private set; }
        internal Type ElementType { get; private set; }

        internal static IlStackType Unknown() => new IlStackType(IlStackKind.Unknown, null, null);
        internal static IlStackType NullReference() => new IlStackType(IlStackKind.Reference, null, null, true);
        internal static IlStackType FromClr(Type type)
        {
            if (type == null) return Unknown();
            if (type.IsByRef)
                return new IlStackType(IlStackKind.ManagedPointer, type, type.GetElementType());
            if (type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr))
                return new IlStackType(IlStackKind.NativeInt, type, null);
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);

            TypeCode code = Type.GetTypeCode(type);
            if (code == TypeCode.Single || code == TypeCode.Double)
                return new IlStackType(IlStackKind.Float, type, null);
            if (code == TypeCode.Int64 || code == TypeCode.UInt64)
                return new IlStackType(IlStackKind.Int64, type, null);
            if (code >= TypeCode.Boolean && code <= TypeCode.UInt32)
                return new IlStackType(IlStackKind.Int32, type, null);
            return new IlStackType(type.IsValueType ? IlStackKind.Value : IlStackKind.Reference, type, null);
        }

        internal bool CompatibleWith(IlStackType expected)
        {
            if (expected == null || Kind == IlStackKind.Unknown || expected.Kind == IlStackKind.Unknown)
                return false;
            if (Kind == IlStackKind.ManagedPointer || expected.Kind == IlStackKind.ManagedPointer)
                return Kind == expected.Kind && ElementType == expected.ElementType;
            if (Kind != expected.Kind) return false;
            if (Kind == IlStackKind.Reference)
                return _isNull || !expected._isNull && ClrType != null && expected.ClrType != null &&
                    expected.ClrType.IsAssignableFrom(ClrType);
            return Kind != IlStackKind.Value || ClrType == expected.ClrType;
        }

        internal bool IsKnownNonNullReferenceAssignableTo(Type expected)
        {
            if (expected == null || Kind != IlStackKind.Reference || _isNull || ClrType == null)
                return false;
            try
            {
                return !ClrType.IsValueType && expected.IsAssignableFrom(ClrType);
            }
            catch (Exception) { return false; }
        }

        internal bool TryMerge(IlStackType other, out IlStackType merged)
        {
            merged = null;
            if (other == null || Kind == IlStackKind.Unknown || other.Kind == IlStackKind.Unknown ||
                Kind != other.Kind)
                return false;
            if (Kind == IlStackKind.Value && ClrType != other.ClrType ||
                Kind == IlStackKind.ManagedPointer && ElementType != other.ElementType)
                return false;
            if (Kind != IlStackKind.Reference)
            {
                merged = this;
                return true;
            }
            if (_isNull) merged = other;
            else if (other._isNull) merged = this;
            else if (ClrType == other.ClrType) merged = this;
            else if (ClrType != null && ClrType.IsAssignableFrom(other.ClrType)) merged = this;
            else if (other.ClrType != null && other.ClrType.IsAssignableFrom(ClrType)) merged = other;
            else return false;
            return true;
        }

        internal string Describe() => KindNames[(int)Kind];
        internal IlStackType AddressType()
        {
            Type type = ClrType != null && !ClrType.IsByRef ? ClrType : ElementType;
            if (type == null) return Unknown();
            try { return FromClr(type.MakeByRefType()); }
            catch (Exception) { return Unknown(); }
        }
        }
        internal sealed class IlStackState
    {
        internal IlStackState() { Values = new List<IlStackType>(); }
        internal IlStackState(IEnumerable<IlStackType> values) { Values = new List<IlStackType>(values); }
        internal List<IlStackType> Values { get; private set; }
        }
        internal static class IlStackAnalyzer
    {
        private enum OpcodeFamily
        {
            Unsupported,
            Passive,
            Constant,
            Slot,
            Stack,
            Conversion,
            Initialization,
            Field,
            Call,
            FunctionPointer,
            Flow,
            Binary,
            Unary,
            IsInst,
            Terminal
        }
        private enum BinaryRule
        {
            Numeric,
            Integer,
            Shift
        }
        private static readonly HashSet<string> PassiveOps = Ops(
            "nop", "break", "constrained.", "readonly.", "tail.", "unaligned.", "volatile.");
        private static readonly HashSet<string> FieldOps = Ops(
            "ldsfld", "ldsflda", "ldfld", "ldflda", "stfld", "stsfld");
        private static readonly HashSet<string> CallOps = Ops("call", "callvirt", "newobj");
        private static readonly HashSet<string> FunctionPointerOps = Ops("ldftn", "ldvirtftn");
        private static readonly HashSet<string> TruthyOps = Ops("brtrue", "brtrue.s", "brfalse", "brfalse.s");
        private static readonly HashSet<string> UnconditionalOps = Ops("br", "br.s", "leave", "leave.s");
        private static readonly HashSet<string> ComparisonValueOps = Ops(
            "ceq", "cgt", "cgt.un", "clt", "clt.un");
        private static readonly HashSet<string> EqualityOps = Ops(
            "ceq", "beq", "beq.s", "bne.un", "bne.un.s");
        private static readonly HashSet<string> NumericOps = Ops("add", "sub", "mul", "div", "rem");
        private static readonly HashSet<string> IntegerOps = Ops(
            "add.ovf", "add.ovf.un", "sub.ovf", "sub.ovf.un", "mul.ovf", "mul.ovf.un",
            "div.un", "rem.un", "and", "or", "xor");
        private static readonly HashSet<string> ShiftOps = Ops("shl", "shr", "shr.un");
        private static readonly HashSet<string> TerminalOps = Ops(
            "throw", "rethrow", "endfinally", "endfilter", "ret");
        internal static void Analyze(
            IlInstructionSnapshot snapshot, IList<CodeInstruction> instructions,
            IlControlFlowGraph graph, IlExceptionContext[] regions,
            IlVerificationReport report)
        {
            var states = new Dictionary<int, IlStackState>();
            var worklist = new Queue<int>();
            Enqueue(0, new IlStackState(), states, worklist, report, -1);
            foreach (KeyValuePair<int, IlStackState> entry in graph.HandlerEntries)
                Enqueue(entry.Key, entry.Value, states, worklist, report, entry.Key);

            while (worklist.Count != 0)
            {
                int index = worklist.Dequeue();
                var state = new IlStackState(states[index].Values);
                Execute(snapshot, instructions, index, state, report, regions[index]);
                if (report.HasErrorAt(index)) continue;
                foreach (int successor in graph.Successors[index])
                    Enqueue(successor, state, states, worklist, report, index);
            }
        }
        internal static bool IsSupported(CodeInstruction instruction)
        {
            if (instruction == null || String.IsNullOrEmpty(instruction.opcode.Name)) return false;
            BinaryRule ignored;
            return Family(instruction.opcode.Name, out ignored) != OpcodeFamily.Unsupported;
        }
        private static void Enqueue(
            int index, IlStackState incoming, IDictionary<int, IlStackState> states,
            Queue<int> worklist, IlVerificationReport report, int source)
        {
            IlStackState existing;
            if (!states.TryGetValue(index, out existing))
            {
                states[index] = new IlStackState(incoming.Values);
                worklist.Enqueue(index);
                return;
            }
            if (existing.Values.Count != incoming.Values.Count)
            {
                Error(report, PatchDiagnosticCode.StackHeightMergeMismatch, index,
                    "Control-flow paths merge with different evaluation-stack heights (" +
                    existing.Values.Count + " versus " + incoming.Values.Count + ", from " + source + ").");
                return;
            }

            bool changed = false;
            for (int i = 0; i < existing.Values.Count; i++)
            {
                IlStackType merged;
                if (!existing.Values[i].TryMerge(incoming.Values[i], out merged))
                {
                    Error(report, PatchDiagnosticCode.StackTypeMergeMismatch, index,
                        "Control-flow paths merge incompatible stack values.");
                    return;
                }
                changed |= !SameType(existing.Values[i], merged);
                existing.Values[i] = merged;
            }
            if (changed) worklist.Enqueue(index);
        }
        private static bool SameType(IlStackType left, IlStackType right) =>
            left.Kind == right.Kind && left.ClrType == right.ClrType && left.ElementType == right.ElementType;
        private static void Execute(
            IlInstructionSnapshot snapshot, IList<CodeInstruction> instructions, int index,
            IlStackState state, IlVerificationReport report, IlExceptionContext context)
        {
            CodeInstruction instruction = instructions[index];
            string name = instruction.opcode.Name;
            BinaryRule rule;
            switch (Family(name, out rule))
            {
                case OpcodeFamily.Passive: return;
                case OpcodeFamily.Constant: ExecuteConstant(instruction, state, report, index); return;
                case OpcodeFamily.Slot: ExecuteSlot(snapshot, instruction, state, report, index); return;
                case OpcodeFamily.Stack: ExecuteStack(name, state, report, index); return;
                case OpcodeFamily.Conversion: ExecuteConversion(name, state, report, index); return;
                case OpcodeFamily.Initialization: ExecuteInitialization(instruction, state, report, index); return;
                case OpcodeFamily.Field: ExecuteField(instruction, state, report, index); return;
                case OpcodeFamily.Call: ExecuteCall(instruction, instructions, state, report, index); return;
                case OpcodeFamily.FunctionPointer:
                    ExecuteFunctionPointer(instruction, state, report, index);
                    return;
                case OpcodeFamily.Flow: ExecuteFlow(name, state, report, index); return;
                case OpcodeFamily.Binary: ExecuteBinary(name, rule, state, report, index); return;
                case OpcodeFamily.Unary: ExecuteUnary(name, state, report, index); return;
                case OpcodeFamily.IsInst: ExecuteIsInst(instruction, state, report, index); return;
                case OpcodeFamily.Terminal:
                    ExecuteTerminal(snapshot, name, state, report, index, context);
                    return;
                default:
                    Error(report, PatchDiagnosticCode.UnsupportedOpcode, index,
                        "The verifier does not recognize opcode " + instruction.opcode + ".");
                    return;
            }
        }
        private static void ExecuteConstant(
            CodeInstruction instruction, IlStackState state, IlVerificationReport report, int index)
        {
            string name = instruction.opcode.Name;
            if (name == "ldnull")
            {
                state.Values.Add(IlStackType.NullReference());
                return;
            }
            if (name == "ldstr")
            {
                if (instruction.operand is string) state.Values.Add(IlStackType.FromClr(typeof(string)));
                else Error(report, PatchDiagnosticCode.InvalidOperand, index, "ldstr requires a string operand.");
                return;
            }
            IlStackType type;
            if (TryLiteral(instruction, out type)) state.Values.Add(type);
            else Error(report, PatchDiagnosticCode.InvalidOperand, index,
                name + " has an invalid literal operand.");
        }
        private static void ExecuteSlot(
            IlInstructionSnapshot snapshot, CodeInstruction instruction,
            IlStackState state, IlVerificationReport report, int index)
        {
            string name = instruction.opcode.Name;
            IlStackType type = IsArgument(name)
                ? ArgumentType(snapshot, instruction)
                : LocalType(snapshot, instruction);
            if (name.StartsWith("st", StringComparison.Ordinal))
                Require(state, type, report, index, "slot store");
            else Push(type, state, report, index, "slot load");
        }
        private static void ExecuteStack(
            string name, IlStackState state, IlVerificationReport report, int index)
        {
            if (name == "pop")
            {
                Pop(state, report, index, "pop");
                return;
            }
            IlStackType value = Top(state, report, index);
            if (value != null) state.Values.Add(value);
        }
        private static void ExecuteField(
            CodeInstruction instruction, IlStackState state, IlVerificationReport report, int index)
        {
            string name = instruction.opcode.Name;
            FieldInfo field = instruction.operand as FieldInfo;
            bool isStatic = name.StartsWith("lds", StringComparison.Ordinal) || name == "stsfld";
            if (field == null || field.IsStatic != isStatic)
            {
                Error(report, PatchDiagnosticCode.InvalidOperand, index, isStatic
                    ? "A static field opcode requires a static field."
                    : "An instance field opcode requires an instance field.");
                return;
            }

            bool store = name == "stfld" || name == "stsfld";
            if (store) Require(state, IlStackType.FromClr(field.FieldType), report, index, "field value");
            if (!isStatic) RequireFieldReceiver(state, field.DeclaringType, report, index);
            if (store) return;

            IlStackType value = IlStackType.FromClr(field.FieldType);
            Push(name.EndsWith("a", StringComparison.Ordinal) ? value.AddressType() : value,
                state, report, index, "field load");
        }
        private static void ExecuteCall(
            CodeInstruction instruction, IList<CodeInstruction> instructions,
            IlStackState state, IlVerificationReport report, int index)
        {
            MethodBase method = instruction.operand as MethodBase;
            string name = instruction.opcode.Name;
            bool constructor = name == "newobj";
            string error = method == null ? "Call has no MethodBase operand."
                : method.ContainsGenericParameters ? "Open generic call operands are unsupported."
                : constructor && !(method is ConstructorInfo) ? "newobj requires a constructor operand."
                : name == "callvirt" && method.IsStatic ? "callvirt cannot target a static method operand."
                : null;
            if (error != null)
            {
                Error(report, PatchDiagnosticCode.UnsupportedSignature, index, error);
                return;
            }

            ParameterInfo[] parameters;
            try { parameters = method.GetParameters(); }
            catch (Exception exception) { report.Fault(index, exception); return; }
            for (int i = parameters.Length - 1; i >= 0; i--)
                Require(state, IlStackType.FromClr(parameters[i].ParameterType), report, index, "call argument");
            if (!constructor && !method.IsStatic)
            {
                Type constrainedType = ConstrainedType(instructions, index);
                Require(state, constrainedType == null ? ReceiverType(method.DeclaringType) :
                    ManagedPointerType(constrainedType), report, index, "call receiver");
            }

            Type returnType = IlVerifier.ReturnType(method);
            if (constructor)
                Push(IlStackType.FromClr(method.DeclaringType), state, report, index, "constructor result");
            else if (returnType != typeof(void))
                Push(IlStackType.FromClr(returnType), state, report, index, "call result");
        }

        private static void ExecuteConversion(
            string name, IlStackState state, IlVerificationReport report, int index)
        {
            IlStackType value = Pop(state, report, index, name);
            if (value == null) return;
            bool valid = Numeric(value) && (!name.EndsWith(".un", StringComparison.Ordinal) || Integral(value));
            if (!valid)
            {
                ReportInvalid(report, index, name, "a numeric value", value, null);
                return;
            }
            state.Values.Add(ConversionResult(name));
        }

        private static void ExecuteInitialization(
            CodeInstruction instruction, IlStackState state,
            IlVerificationReport report, int index)
        {
            IlStackType address = Pop(state, report, index, "initobj");
            Type type = instruction.operand as Type;
            if (type == null || type == typeof(void) || type.IsByRef || type.ContainsGenericParameters)
            {
                Error(report, PatchDiagnosticCode.InvalidOperand, index,
                    "initobj requires a closed non-byref type operand.");
                return;
            }
            if (address != null)
                RequireCompatible(address, ManagedPointerType(type), report, index, "initobj address");
        }
        private static void ExecuteFunctionPointer(
            CodeInstruction instruction, IlStackState state, IlVerificationReport report, int index)
        {
            MethodBase method = instruction.operand as MethodBase;
            string name = instruction.opcode.Name;
            string error = method == null || method.ContainsGenericParameters
                ? "Function pointers require a closed method operand."
                : name == "ldvirtftn" && method.IsStatic ? "ldvirtftn cannot target a static method." : null;
            if (error != null)
            {
                Error(report, PatchDiagnosticCode.UnsupportedSignature, index, error);
                return;
            }
            if (name == "ldvirtftn")
                Require(state, ReceiverType(method.DeclaringType), report, index, "function receiver");
            Push(IlStackType.FromClr(typeof(IntPtr)), state, report, index, "function pointer");
        }
        private static void ExecuteFlow(
            string name, IlStackState state, IlVerificationReport report, int index)
        {
            if (name == "switch")
            {
                IlStackType value = Pop(state, report, index, "switch value");
                if (value != null && !Int32OrNative(value))
                    ReportInvalid(report, index, name, "an int32 or native-int value", value, null);
                return;
            }
            if (TruthyOps.Contains(name))
            {
                IlStackType value = Pop(state, report, index, name);
                if (value != null && !Truthy(value))
                    ReportInvalid(report, index, name,
                        "an integer, reference, or managed-pointer value", value, null);
                return;
            }
            bool pushesResult = ComparisonValueOps.Contains(name);
            if (pushesResult || IsComparisonBranch(name))
            {
                ExecuteComparison(name, state, report, index, pushesResult);
                return;
            }
            if (name == "leave" || name == "leave.s") state.Values.Clear();
        }
        private static void ExecuteBinary(
            string name, BinaryRule rule, IlStackState state, IlVerificationReport report, int index)
        {
            IlStackType right = Pop(state, report, index, name);
            IlStackType left = Pop(state, report, index, name);
            if (left == null || right == null) return;

            bool valid = rule == BinaryRule.Numeric
                ? SameNumeric(left, right) || PointerArithmetic(name, left, right)
                : rule == BinaryRule.Shift
                    ? Integral(left) && Int32OrNative(right)
                : Integral(left) && Integral(right) && SameNumeric(left, right);
            string requirement = rule == BinaryRule.Numeric
                ? "two compatible numeric values or valid managed-pointer arithmetic operands"
                : rule == BinaryRule.Shift
                    ? "an integral value and an int32 or native-int shift count"
                    : "two compatible integral values";
            if (!valid)
            {
                ReportInvalid(report, index, name, requirement, left, right);
                return;
            }
            IlStackType result = rule == BinaryRule.Shift ? left
                : name == "sub" && SamePointer(left, right) ? IlStackType.FromClr(typeof(IntPtr))
                : left.Kind == IlStackKind.ManagedPointer || right.Kind == IlStackKind.ManagedPointer
                    ? left.Kind == IlStackKind.ManagedPointer ? left : right
                : left.Kind == IlStackKind.NativeInt || right.Kind == IlStackKind.NativeInt
                    ? IlStackType.FromClr(typeof(IntPtr)) : left;
            state.Values.Add(result);
        }
        private static void ExecuteUnary(
            string name, IlStackState state, IlVerificationReport report, int index)
        {
            IlStackType value = Top(state, report, index);
            if (value == null || name == "neg" ? Numeric(value) : Integral(value)) return;
            ReportInvalid(report, index, name,
                name == "neg" ? "an integer or floating-point value" : "an integer value", value, null);
        }
        private static void ExecuteIsInst(
            CodeInstruction instruction, IlStackState state, IlVerificationReport report, int index)
        {
            IlStackType value = Pop(state, report, index, "object conversion");
            Type target = instruction.operand as Type;
            if (value != null && value.Kind != IlStackKind.Reference)
                Error(report, PatchDiagnosticCode.StackTypeMismatch, index,
                    "isinst requires a reference operand.");
            if (target == null || target.IsValueType || target.ContainsGenericParameters)
                Error(report, PatchDiagnosticCode.InvalidOperand, index,
                    "isinst requires a closed reference type operand.");
            else Push(IlStackType.FromClr(target), state, report, index, "isinst result");
        }
        private static void ExecuteTerminal(
            IlInstructionSnapshot snapshot, string name, IlStackState state,
            IlVerificationReport report, int index, IlExceptionContext context)
        {
            ExceptionBlockType legal = ExceptionBlockType.EndExceptionBlock;
            if (name == "throw")
            {
                IlStackType value = Pop(state, report, index, "throw");
                // A null reference is deliberately rejected: this model cannot prove
                // that it is an exception object, so throw validation fails closed.
                if (value != null && !value.IsKnownNonNullReferenceAssignableTo(typeof(Exception)))
                    ReportInvalid(report, index, name,
                        "a known non-null reference assignable to System.Exception", value, null);
                if (value == null) return;
            }
            else if (name == "rethrow") legal = ExceptionBlockType.BeginCatchBlock;
            else if (name == "endfinally") legal = ExceptionBlockType.BeginFinallyBlock;
            else if (name == "endfilter")
            {
                Require(state, IlStackType.FromClr(typeof(int)), report, index, name);
                legal = ExceptionBlockType.BeginExceptFilterBlock;
            }
            else
            {
                VerifyReturn(snapshot, state, report, index);
                return;
            }
            if (state.Values.Count != 0)
                Error(report, PatchDiagnosticCode.StackTypeMismatch, index,
                    name + " requires an empty evaluation stack.");
            bool wrongContext = legal == ExceptionBlockType.BeginFinallyBlock
                ? !context.IsHandlerKind(ExceptionBlockType.BeginFinallyBlock, index) &&
                    !context.IsHandlerKind(ExceptionBlockType.BeginFaultBlock, index)
                : legal != ExceptionBlockType.EndExceptionBlock && !context.IsHandlerKind(legal, index);
            if (wrongContext)
                Error(report, PatchDiagnosticCode.InvalidExceptionRegion, index,
                    name + " requires its legal exception-handler context.");
        }
        private static void ExecuteComparison(
            string name, IlStackState state, IlVerificationReport report, int index, bool pushesResult)
        {
            IlStackType right = Pop(state, report, index, name);
            IlStackType left = Pop(state, report, index, name);
            if (left == null || right == null) return;
            bool equality = EqualityOps.Contains(name);
            bool valid = equality
                ? SameNumeric(left, right) ||
                    left.Kind == IlStackKind.Reference && right.Kind == IlStackKind.Reference ||
                    SamePointer(left, right)
                : SameNumeric(left, right) ||
                    name.EndsWith(".un", StringComparison.Ordinal) && SamePointer(left, right);
            if (!valid)
            {
                ReportInvalid(report, index, name,
                    equality ? "two comparable values" : "two numeric values", left, right);
                return;
            }
            if (pushesResult) state.Values.Add(IlStackType.FromClr(typeof(int)));
        }
        private static void VerifyReturn(
            IlInstructionSnapshot snapshot, IlStackState state, IlVerificationReport report, int index)
        {
            Type returnType = IlVerifier.ReturnType(snapshot.Method.OriginalMethod);
            if (returnType == typeof(void))
            {
                if (state.Values.Count != 0)
                    Error(report, PatchDiagnosticCode.InvalidReturn, index, "Void return has stack values.");
                return;
            }
            if (state.Values.Count != 1)
            {
                Error(report, PatchDiagnosticCode.InvalidReturn, index, "Return stack shape is invalid.");
                return;
            }
            IlStackType actual = state.Values[0];
            if (actual.Kind == IlStackKind.Unknown || !actual.CompatibleWith(IlStackType.FromClr(returnType)))
                Error(report, actual.Kind == IlStackKind.Unknown
                    ? PatchDiagnosticCode.UnknownStackType : PatchDiagnosticCode.InvalidReturn,
                    index, "Return value has an incompatible stack category.");
            state.Values.Clear();
        }
        private static OpcodeFamily Family(string name, out BinaryRule rule)
        {
            rule = default(BinaryRule);
            if (PassiveOps.Contains(name)) return OpcodeFamily.Passive;
            if (name.StartsWith("ldc.", StringComparison.Ordinal) || name == "ldnull" || name == "ldstr")
                return OpcodeFamily.Constant;
            if (IsArgument(name) || name.StartsWith("ldloc", StringComparison.Ordinal) ||
                name.StartsWith("stloc", StringComparison.Ordinal)) return OpcodeFamily.Slot;
            if (name == "dup" || name == "pop") return OpcodeFamily.Stack;
            if (IsConversion(name)) return OpcodeFamily.Conversion;
            if (name == "initobj") return OpcodeFamily.Initialization;
            if (FieldOps.Contains(name)) return OpcodeFamily.Field;
            if (CallOps.Contains(name)) return OpcodeFamily.Call;
            if (FunctionPointerOps.Contains(name)) return OpcodeFamily.FunctionPointer;
            if (name == "switch" || TruthyOps.Contains(name) || UnconditionalOps.Contains(name) ||
                ComparisonValueOps.Contains(name) || IsComparisonBranch(name))
                return OpcodeFamily.Flow;
            if (TryBinaryRule(name, out rule)) return OpcodeFamily.Binary;
            if (name == "neg" || name == "not") return OpcodeFamily.Unary;
            if (name == "isinst") return OpcodeFamily.IsInst;
            if (TerminalOps.Contains(name)) return OpcodeFamily.Terminal;
            return OpcodeFamily.Unsupported;
        }
        private static bool TryLiteral(CodeInstruction instruction, out IlStackType type)
        {
            type = null;
            string name = instruction.opcode.Name;
            Type clrType = null;
            if (name == "ldc.i4" && instruction.operand is int ||
                name == "ldc.i4.s" && IsShortInteger(instruction.operand))
                clrType = typeof(int);
            else if (name.StartsWith("ldc.i4.", StringComparison.Ordinal) && instruction.operand == null)
            {
                string suffix = name.Substring("ldc.i4.".Length);
                int value;
                if (suffix == "m1" || Int32.TryParse(suffix, out value) && value >= 0 && value <= 8)
                    clrType = typeof(int);
            }
            else if (name == "ldc.i8" && instruction.operand is long)
                clrType = typeof(long);
            else if (name == "ldc.r4" && instruction.operand is float)
                clrType = typeof(float);
            else if (name == "ldc.r8" && instruction.operand is double)
                clrType = typeof(double);
            if (clrType != null) type = IlStackType.FromClr(clrType);
            return type != null;
        }
        private static bool IsShortInteger(object value) =>
            value is sbyte || value is byte || value is int &&
            (int)value >= SByte.MinValue && (int)value <= SByte.MaxValue;
        private static IlStackType ArgumentType(
            IlInstructionSnapshot snapshot, CodeInstruction instruction)
        {
            int index = SlotIndex(instruction);
            MethodBase method = snapshot.Method.OriginalMethod;
            if (index < 0 || method == null) return IlStackType.Unknown();
            if (!method.IsStatic)
            {
                if (index == 0) return ReceiverType(method.DeclaringType);
                index--;
            }
            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (index >= parameters.Length) return IlStackType.Unknown();
                return SlotType(parameters[index].ParameterType, instruction);
            }
            catch (Exception) { return IlStackType.Unknown(); }
        }
        private static IlStackType LocalType(
            IlInstructionSnapshot snapshot, CodeInstruction instruction)
        {
            LocalBuilder liveLocal = instruction.operand as LocalBuilder;
            if (liveLocal != null)
                return SlotType(liveLocal.LocalType, instruction);
            int index = SlotIndex(instruction);
            foreach (IlLocalSnapshot local in snapshot.Method.Locals)
                if (local.Index == index) return SlotType(local.LocalType, instruction);
            return IlStackType.Unknown();
        }
        private static IlStackType SlotType(Type type, CodeInstruction instruction)
        {
            IlStackType value = IlStackType.FromClr(type);
            string name = instruction.opcode.Name;
            return name.StartsWith("ldarga", StringComparison.Ordinal) ||
                name.StartsWith("ldloca", StringComparison.Ordinal) ? value.AddressType() : value;
        }
        private static int SlotIndex(CodeInstruction instruction)
        {
            string name = instruction.opcode.Name;
            for (int index = 0; index <= 3; index++)
                if (name.EndsWith("." + index, StringComparison.Ordinal)) return index;
            try
            {
                LocalBuilder local = instruction.operand as LocalBuilder;
                return local == null ? Convert.ToInt32(instruction.operand) : local.LocalIndex;
            }
            catch (Exception) { return -1; }
        }
        private static IlStackType ReceiverType(Type declaringType) =>
            declaringType == null ? IlStackType.Unknown() : IlStackType.FromClr(
                declaringType.IsValueType ? declaringType.MakeByRefType() : declaringType);
        private static IlStackType ManagedPointerType(Type type)
        {
            try { return IlStackType.FromClr(type.MakeByRefType()); }
            catch (Exception) { return IlStackType.Unknown(); }
        }
        private static Type ConstrainedType(IList<CodeInstruction> instructions, int index)
        {
            for (int prior = index - 1; prior >= 0 && IlVerifier.IsPrefix(instructions[prior].opcode); prior--)
                if (instructions[prior].opcode == OpCodes.Constrained)
                    return instructions[prior].operand as Type;
            return null;
        }
        private static void Require(
            IlStackState state, IlStackType expected, IlVerificationReport report, int index, string context)
        {
            IlStackType actual = Pop(state, report, index, context);
            if (actual == null) return;
            if (expected == null || expected.Kind == IlStackKind.Unknown)
                Error(report, PatchDiagnosticCode.UnknownStackType, index,
                    context + " has an unknown destination type.");
            else if (!actual.CompatibleWith(expected))
                Error(report, actual.Kind == IlStackKind.Unknown
                    ? PatchDiagnosticCode.UnknownStackType : PatchDiagnosticCode.StackTypeMismatch,
                    index, context + " expected " + expected.Describe() +
                        " but found " + actual.Describe() + ".");
        }
        private static void RequireFieldReceiver(
            IlStackState state, Type declaringType, IlVerificationReport report, int index)
        {
            IlStackType actual = Pop(state, report, index, "field receiver");
            if (actual == null) return;
            IlStackType expected = IlStackType.FromClr(declaringType);
            bool compatible = declaringType != null &&
                (actual.CompatibleWith(expected) ||
                    actual.Kind == IlStackKind.ManagedPointer && actual.ElementType == declaringType);
            if (!compatible)
                Error(report, actual.Kind == IlStackKind.Unknown
                    ? PatchDiagnosticCode.UnknownStackType : PatchDiagnosticCode.StackTypeMismatch,
                    index, "field receiver expected " + expected.Describe() +
                        " or its address but found " + actual.Describe() + ".");
        }
        private static IlStackType Top(IlStackState state, IlVerificationReport report, int index)
        {
            if (state.Values.Count != 0) return state.Values[state.Values.Count - 1];
            Error(report, PatchDiagnosticCode.StackUnderflow, index, "The evaluation stack is empty.");
            return null;
        }
        private static IlStackType Pop(
            IlStackState state, IlVerificationReport report, int index, string context)
        {
            if (state.Values.Count == 0)
            {
                Error(report, PatchDiagnosticCode.StackUnderflow, index,
                    context + " requires a stack value.");
                return null;
            }
            int top = state.Values.Count - 1;
            IlStackType value = state.Values[top];
            state.Values.RemoveAt(top);
            return value;
        }
        private static void Push(
            IlStackType type, IlStackState state, IlVerificationReport report, int index, string context)
        {
            if (type == null || type.Kind == IlStackKind.Unknown)
                Error(report, PatchDiagnosticCode.UnknownStackType, index,
                    context + " has an unknown stack category.");
            else state.Values.Add(type);
        }

        private static void RequireCompatible(
            IlStackType actual, IlStackType expected, IlVerificationReport report,
            int index, string context)
        {
            if (expected == null || expected.Kind == IlStackKind.Unknown)
                Error(report, PatchDiagnosticCode.UnknownStackType, index,
                    context + " has an unknown destination type.");
            else if (!actual.CompatibleWith(expected))
                Error(report, actual.Kind == IlStackKind.Unknown
                    ? PatchDiagnosticCode.UnknownStackType : PatchDiagnosticCode.StackTypeMismatch,
                    index, context + " expected " + expected.Describe() +
                        " but found " + actual.Describe() + ".");
        }

        private static void Error(
            IlVerificationReport report, PatchDiagnosticCode code, int index, string detail) =>
            report.Error(code, index, index + 1, detail);
        private static void ReportInvalid(
            IlVerificationReport report, int index, string name, string requirement,
            IlStackType left, IlStackType right)
        {
            bool unknown = left != null && left.Kind == IlStackKind.Unknown ||
                right != null && right.Kind == IlStackKind.Unknown;
            Error(report, unknown ? PatchDiagnosticCode.UnknownStackType : PatchDiagnosticCode.StackTypeMismatch,
                index, name + " requires " + requirement + ", but found " + Describe(left, right) + ".");
        }
        private static string Describe(IlStackType left, IlStackType right) =>
            (left == null ? "<missing>" : left.Describe()) +
            (right == null ? String.Empty : " and " + right.Describe());
        private static bool SameNumeric(IlStackType left, IlStackType right) =>
            Numeric(left) && Numeric(right) &&
            (left.Kind == right.Kind || Int32OrNative(left) && Int32OrNative(right));
        private static bool Numeric(IlStackType value) =>
            Integral(value) || value != null && value.Kind == IlStackKind.Float;
        private static bool Integral(IlStackType value) =>
            value != null && (value.Kind == IlStackKind.Int32 || value.Kind == IlStackKind.Int64 ||
                value.Kind == IlStackKind.NativeInt);
        private static bool Int32OrNative(IlStackType value) =>
            value != null && (value.Kind == IlStackKind.Int32 || value.Kind == IlStackKind.NativeInt);
        private static bool Truthy(IlStackType value) =>
            Int32OrNative(value) || value != null &&
            (value.Kind == IlStackKind.Reference || value.Kind == IlStackKind.ManagedPointer);
        private static bool PointerArithmetic(string name, IlStackType left, IlStackType right) =>
            name == "sub" && SamePointer(left, right) ||
            left.Kind == IlStackKind.ManagedPointer && Int32OrNative(right) ||
            name == "add" && Int32OrNative(left) && right.Kind == IlStackKind.ManagedPointer;
        private static bool SamePointer(IlStackType left, IlStackType right) =>
            left != null && right != null && left.Kind == IlStackKind.ManagedPointer &&
            right.Kind == IlStackKind.ManagedPointer && left.ElementType == right.ElementType;
        private static bool TryBinaryRule(string name, out BinaryRule rule)
        {
            if (NumericOps.Contains(name))
            { rule = BinaryRule.Numeric; return true; }
            if (IntegerOps.Contains(name))
            { rule = BinaryRule.Integer; return true; }
            if (ShiftOps.Contains(name))
            { rule = BinaryRule.Shift; return true; }
            rule = default(BinaryRule);
            return false;
        }
        private static bool IsConversion(string name) => name.StartsWith("conv.", StringComparison.Ordinal);
        private static IlStackType ConversionResult(string name)
        {
            string target = name.Substring("conv.".Length);
            if (target.StartsWith("ovf.", StringComparison.Ordinal)) target = target.Substring(4);
            if (target.EndsWith(".un", StringComparison.Ordinal)) target = target.Substring(0, target.Length - 3);
            return target == "r" || target == "r4" || target == "r8"
                ? IlStackType.FromClr(typeof(double))
                : target == "i8" || target == "u8"
                    ? IlStackType.FromClr(typeof(long))
                    : target == "i" || target == "u"
                        ? IlStackType.FromClr(typeof(IntPtr)) : IlStackType.FromClr(typeof(int));
        }
        private static bool IsComparisonBranch(string name) =>
            name.StartsWith("beq", StringComparison.Ordinal) ||
            name.StartsWith("bne.un", StringComparison.Ordinal) ||
            name.StartsWith("bge", StringComparison.Ordinal) ||
            name.StartsWith("bgt", StringComparison.Ordinal) ||
            name.StartsWith("ble", StringComparison.Ordinal) ||
            name.StartsWith("blt", StringComparison.Ordinal);
        private static bool IsArgument(string name) =>
            name.StartsWith("ldarg", StringComparison.Ordinal) ||
            name.StartsWith("starg", StringComparison.Ordinal);

        private static HashSet<string> Ops(params string[] names) => new HashSet<string>(names);
    }
}
