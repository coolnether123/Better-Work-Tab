using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ModAPI.Harmony
{
    /// <summary>
    /// High-level recipes for common argument validation shapes.
    /// </summary>
    public static class FluentTranspilerRangeCheckRecipes
    {
        public static FluentArgumentSelection ForArgument(this FluentTranspiler transpiler, int argumentIndex)
        {
            return new FluentArgumentSelection(transpiler, argumentIndex);
        }

        public static FluentReplacementResult ReplaceArgumentRangeUpperBoundWithCall(
            this FluentTranspiler transpiler,
            int argumentIndex,
            int lowerBound,
            int upperBound,
            MethodInfo replacementMethod,
            SearchMode mode = SearchMode.Start)
        {
            return transpiler.ForArgument(argumentIndex)
                             .InRangeCheck(lowerBound, upperBound)
                             .ReplaceUpperBoundWithCall(replacementMethod, mode);
        }

        public static FluentReplacementResult ReplaceArgumentRangeUpperBoundWithCallOrCompatibleProvider(
            this FluentTranspiler transpiler,
            int argumentIndex,
            int lowerBound,
            int upperBound,
            MethodInfo replacementMethod,
            Func<MethodInfo, bool> compatibleProviderPredicate,
            string editLabel = null,
            SearchMode mode = SearchMode.Start)
        {
            return transpiler.ForArgument(argumentIndex)
                             .InRangeCheck(lowerBound, upperBound)
                             .ReplaceUpperBoundWithCallOrCompatibleProvider(
                                 replacementMethod,
                                 compatibleProviderPredicate,
                                 editLabel,
                                 mode);
        }
    }

    public sealed class FluentArgumentSelection
    {
        private readonly FluentTranspiler _transpiler;
        private readonly int _argumentIndex;

        internal FluentArgumentSelection(FluentTranspiler transpiler, int argumentIndex)
        {
            _transpiler = transpiler;
            _argumentIndex = argumentIndex;
        }

        public FluentRangeCheckSelection InRangeCheck(int lowerBound, int upperBound)
        {
            return new FluentRangeCheckSelection(_transpiler, _argumentIndex, lowerBound, upperBound);
        }
    }

    public sealed class FluentRangeCheckSelection
    {
        private const int UpperBoundPatternOffset = 4;

        private readonly FluentTranspiler _transpiler;
        private readonly int _argumentIndex;
        private readonly int _lowerBound;
        private readonly int _upperBound;

        internal FluentRangeCheckSelection(
            FluentTranspiler transpiler,
            int argumentIndex,
            int lowerBound,
            int upperBound)
        {
            _transpiler = transpiler;
            _argumentIndex = argumentIndex;
            _lowerBound = lowerBound;
            _upperBound = upperBound;
        }

        public FluentReplacementResult ReplaceUpperBoundWithCall(MethodInfo replacementMethod, SearchMode mode = SearchMode.Start)
        {
            if (!IsValid())
            {
                return FluentReplacementResult.NoMatch;
            }

            return _transpiler.TryReplaceSequenceOffsetWithCall(
                BuildUpperBoundPattern(),
                UpperBoundPatternOffset,
                replacementMethod,
                mode)
                ? FluentReplacementResult.PatternReplaced
                : FluentReplacementResult.NoMatch;
        }

        public FluentReplacementResult ReplaceUpperBoundWithCallOrCompatibleProvider(
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

        public FluentReplacementResult ReplaceUpperBoundWithCallOrFallbackCall(
            MethodInfo replacementMethod,
            Func<MethodInfo, bool> fallbackMethodPredicate,
            string editLabel = null,
            SearchMode mode = SearchMode.Start)
        {
            if (!IsValid())
            {
                return FluentReplacementResult.NoMatch;
            }

            return _transpiler.ReplaceSequenceOffsetWithCallOrFallbackCall(
                BuildUpperBoundPattern(),
                UpperBoundPatternOffset,
                replacementMethod,
                fallbackMethodPredicate,
                editLabel,
                mode);
        }

        private bool IsValid()
        {
            if (_transpiler == null)
            {
                return false;
            }

            if (_argumentIndex < 0)
            {
                _transpiler.AddWarning($"ForArgument received invalid argument index {_argumentIndex}.");
                return false;
            }

            if (_lowerBound > _upperBound)
            {
                _transpiler.AddWarning($"InRangeCheck lower bound {_lowerBound} is greater than upper bound {_upperBound}.");
                return false;
            }

            return true;
        }

        private Func<CodeInstruction, bool>[] BuildUpperBoundPattern()
        {
            return new Func<CodeInstruction, bool>[]
            {
                instruction => instruction.IsLoadArgument(_argumentIndex),
                instruction => instruction != null && instruction.IsLdcI4(_lowerBound),
                instruction => instruction.IsBranch(OpCodes.Blt, OpCodes.Blt_S),
                instruction => instruction.IsLoadArgument(_argumentIndex),
                instruction => instruction != null && instruction.IsLdcI4(_upperBound),
                instruction => instruction.IsBranch(OpCodes.Ble, OpCodes.Ble_S)
            };
        }
    }
}
