using System;
using System.Reflection;
using HarmonyLib;

namespace ModAPI.Harmony
{
    /// <summary>
    /// High-level recipes for common call-site rewrites.
    /// </summary>
    public static class FluentTranspilerCallRecipes
    {
        public static FluentCallReplacementSelection ReplaceCalls(this FluentTranspiler transpiler, MethodInfo sourceMethod)
        {
            return new FluentCallReplacementSelection(transpiler, sourceMethod);
        }
    }

    public sealed class FluentCallReplacementSelection
    {
        private readonly FluentTranspiler _transpiler;
        private readonly MethodInfo _sourceMethod;

        internal FluentCallReplacementSelection(FluentTranspiler transpiler, MethodInfo sourceMethod)
        {
            _transpiler = transpiler;
            _sourceMethod = sourceMethod;
        }

        public FluentReplacementResult WithCall(MethodInfo replacementMethod, string editLabel = null)
        {
            if (_transpiler == null)
            {
                return FluentReplacementResult.NoMatch;
            }

            if (_sourceMethod == null)
            {
                _transpiler.AddWarning("ReplaceCalls received a null source method.");
                return FluentReplacementResult.NoMatch;
            }

            int replaced = _transpiler.ReplaceMatchingCalls(
                method => method == _sourceMethod,
                replacementMethod,
                editLabel ?? $"Replace calls to {FluentTranspilerFormatting.FormatMethod(_sourceMethod)}");

            if (replaced > 0)
            {
                return FluentReplacementResult.PatternReplaced;
            }

            if (replacementMethod != null && _transpiler.HasMatchingCall(method => method == replacementMethod))
            {
                return FluentReplacementResult.ReplacementAlreadyPresent;
            }

            _transpiler.AddSoftFailure($"ReplaceCalls found no calls to {FluentTranspilerFormatting.FormatMethod(_sourceMethod)}.");
            return FluentReplacementResult.NoMatch;
        }
    }
}
