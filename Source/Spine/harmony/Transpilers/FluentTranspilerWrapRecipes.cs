using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ModAPI.Harmony
{
    /// <summary>
    /// High-level recipes for numeric wraparound logic such as increment/decrement UI cycling.
    /// </summary>
    public static class FluentTranspilerWrapRecipes
    {
        public static FluentCallResultSelection ForCallResult(this FluentTranspiler transpiler, MethodInfo method)
        {
            return new FluentCallResultSelection(transpiler, method);
        }

        public static FluentWrapBoundsReplacementResult ReplaceWrappedRangeUpperBoundWithCallOrCompatibleProvider(
            this FluentTranspiler transpiler,
            MethodInfo valueProvider,
            int lowerBound,
            int upperBound,
            int step,
            MethodInfo replacementMethod,
            Func<MethodInfo, bool> compatibleProviderPredicate,
            string editLabel = null,
            SearchMode mode = SearchMode.Start)
        {
            return transpiler.ForCallResult(valueProvider)
                             .AsWrappedRange(lowerBound, upperBound, step)
                             .ReplaceUpperBoundWithCallOrCompatibleProvider(
                                 replacementMethod,
                                 compatibleProviderPredicate,
                                 editLabel,
                                 mode);
        }
    }

    public sealed class FluentCallResultSelection
    {
        private readonly FluentTranspiler _transpiler;
        private readonly MethodInfo _method;

        internal FluentCallResultSelection(FluentTranspiler transpiler, MethodInfo method)
        {
            _transpiler = transpiler;
            _method = method;
        }

        public FluentWrappedRangeSelection AsWrappedRange(int lowerBound, int upperBound, int step = 1)
        {
            return new FluentWrappedRangeSelection(_transpiler, _method, lowerBound, upperBound, step);
        }
    }

    public sealed class FluentWrappedRangeSelection
    {
        private const int UnderflowUpperBoundPatternOffset = 7;
        private const int OverflowUpperBoundPatternOffset = 5;

        private readonly FluentTranspiler _transpiler;
        private readonly MethodInfo _valueProvider;
        private readonly int _lowerBound;
        private readonly int _upperBound;
        private readonly int _step;

        internal FluentWrappedRangeSelection(
            FluentTranspiler transpiler,
            MethodInfo valueProvider,
            int lowerBound,
            int upperBound,
            int step)
        {
            _transpiler = transpiler;
            _valueProvider = valueProvider;
            _lowerBound = lowerBound;
            _upperBound = upperBound;
            _step = step;
        }

        public FluentWrapBoundsReplacementResult ReplaceUpperBoundWithCall(MethodInfo replacementMethod, SearchMode mode = SearchMode.Start)
        {
            if (!IsValid())
            {
                return FluentWrapBoundsReplacementResult.NoMatch;
            }

            FluentReplacementResult underflowResult = _transpiler.TryReplaceSequenceOffsetWithCall(
                BuildUnderflowPattern(),
                UnderflowUpperBoundPatternOffset,
                replacementMethod,
                mode)
                ? FluentReplacementResult.PatternReplaced
                : FluentReplacementResult.NoMatch;

            FluentReplacementResult overflowResult = _transpiler.TryReplaceSequenceOffsetWithCall(
                BuildOverflowPattern(),
                OverflowUpperBoundPatternOffset,
                replacementMethod,
                mode)
                ? FluentReplacementResult.PatternReplaced
                : FluentReplacementResult.NoMatch;

            return new FluentWrapBoundsReplacementResult(underflowResult, overflowResult);
        }

        public FluentWrapBoundsReplacementResult ReplaceUpperBoundWithCallOrCompatibleProvider(
            MethodInfo replacementMethod,
            Func<MethodInfo, bool> compatibleProviderPredicate,
            string editLabel = null,
            SearchMode mode = SearchMode.Start)
        {
            return ReplaceUpperBoundWithCallOrFallbackCall(
                replacementMethod,
                compatibleProviderPredicate,
                editLabel,
                mode);
        }

        public FluentWrapBoundsReplacementResult ReplaceUpperBoundWithCallOrFallbackCall(
            MethodInfo replacementMethod,
            Func<MethodInfo, bool> fallbackMethodPredicate,
            string editLabel = null,
            SearchMode mode = SearchMode.Start)
        {
            if (!IsValid())
            {
                return FluentWrapBoundsReplacementResult.NoMatch;
            }

            FluentReplacementResult underflowResult = _transpiler.ReplaceSequenceOffsetWithCallOrFallbackCall(
                BuildUnderflowPattern(),
                UnderflowUpperBoundPatternOffset,
                replacementMethod,
                fallbackMethodPredicate,
                editLabel,
                mode);

            if (underflowResult == FluentReplacementResult.FallbackCallReplaced ||
                underflowResult == FluentReplacementResult.ReplacementAlreadyPresent)
            {
                return new FluentWrapBoundsReplacementResult(underflowResult, FluentReplacementResult.NoMatch);
            }

            FluentReplacementResult overflowResult = _transpiler.ReplaceSequenceOffsetWithCallOrFallbackCall(
                BuildOverflowPattern(),
                OverflowUpperBoundPatternOffset,
                replacementMethod,
                fallbackMethodPredicate,
                editLabel,
                mode);

            return new FluentWrapBoundsReplacementResult(underflowResult, overflowResult);
        }

        private bool IsValid()
        {
            if (_transpiler == null)
            {
                return false;
            }

            if (_valueProvider == null)
            {
                _transpiler.AddWarning("ForCallResult received a null method.");
                return false;
            }

            if (_lowerBound > _upperBound)
            {
                _transpiler.AddWarning($"AsWrappedRange lower bound {_lowerBound} is greater than upper bound {_upperBound}.");
                return false;
            }

            if (_step <= 0)
            {
                _transpiler.AddWarning($"AsWrappedRange step {_step} must be greater than zero.");
                return false;
            }

            return true;
        }

        private Func<CodeInstruction, bool>[] BuildUnderflowPattern()
        {
            return new Func<CodeInstruction, bool>[]
            {
                instruction => instruction != null && instruction.Calls(_valueProvider),
                instruction => instruction != null && instruction.IsLdcI4(_step),
                instruction => instruction != null && instruction.opcode == OpCodes.Sub,
                instruction => instruction.IsStoreLocal(),
                instruction => instruction.IsLoadLocal(),
                instruction => instruction != null && instruction.IsLdcI4(_lowerBound),
                instruction => instruction.IsBranch(OpCodes.Bge, OpCodes.Bge_S),
                instruction => instruction != null && instruction.IsLdcI4(_upperBound)
            };
        }

        private Func<CodeInstruction, bool>[] BuildOverflowPattern()
        {
            return new Func<CodeInstruction, bool>[]
            {
                instruction => instruction != null && instruction.Calls(_valueProvider),
                instruction => instruction != null && instruction.IsLdcI4(_step),
                instruction => instruction != null && instruction.opcode == OpCodes.Add,
                instruction => instruction.IsStoreLocal(),
                instruction => instruction.IsLoadLocal(),
                instruction => instruction != null && instruction.IsLdcI4(_upperBound),
                instruction => instruction.IsBranch(OpCodes.Ble, OpCodes.Ble_S),
                instruction => instruction != null && instruction.IsLdcI4(_lowerBound)
            };
        }
    }

    public sealed class FluentWrapBoundsReplacementResult
    {
        public static readonly FluentWrapBoundsReplacementResult NoMatch =
            new FluentWrapBoundsReplacementResult(FluentReplacementResult.NoMatch, FluentReplacementResult.NoMatch);

        public FluentWrapBoundsReplacementResult(FluentReplacementResult underflowResult, FluentReplacementResult overflowResult)
        {
            UnderflowResult = underflowResult;
            OverflowResult = overflowResult;
        }

        public FluentReplacementResult UnderflowResult { get; }

        public FluentReplacementResult OverflowResult { get; }

        public bool Succeeded
        {
            get
            {
                return UnderflowResult == FluentReplacementResult.FallbackCallReplaced ||
                       UnderflowResult == FluentReplacementResult.ReplacementAlreadyPresent ||
                       (UnderflowResult == FluentReplacementResult.PatternReplaced &&
                        OverflowResult == FluentReplacementResult.PatternReplaced);
            }
        }

        public bool ReplacedFallbackCall
        {
            get
            {
                return UnderflowResult == FluentReplacementResult.FallbackCallReplaced ||
                       OverflowResult == FluentReplacementResult.FallbackCallReplaced;
            }
        }

        public override string ToString()
        {
            return $"underflow={UnderflowResult}, overflow={OverflowResult}";
        }
    }
}
