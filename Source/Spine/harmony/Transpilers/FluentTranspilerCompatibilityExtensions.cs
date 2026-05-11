using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ModAPI.Harmony
{
    public enum FluentReplacementResult
    {
        NoMatch,
        PatternReplaced,
        FallbackCallReplaced,
        ReplacementAlreadyPresent
    }

    /// <summary>
    /// Compatibility-oriented helpers for patches that need to handle either an expected
    /// base IL shape or a method body already rewritten by another transpiler.
    /// </summary>
    public static class FluentTranspilerCompatibilityExtensions
    {
        /// <summary>
        /// Replaces the instruction at an absolute index while preserving labels and exception blocks.
        /// </summary>
        public static FluentTranspiler ReplaceAt(this FluentTranspiler transpiler, int absoluteIndex, CodeInstruction instruction)
        {
            if (transpiler == null)
            {
                return null;
            }

            if (instruction == null)
            {
                transpiler.AddWarning("ReplaceAt received a null replacement instruction.");
                return transpiler;
            }

            int count = transpiler.Instructions().Count();
            if (absoluteIndex < 0 || absoluteIndex >= count)
            {
                transpiler.AddSoftFailure($"ReplaceAt: Position {absoluteIndex} out of range.");
                return transpiler;
            }

            return transpiler.MoveTo(absoluteIndex)
                             .ReplaceSequence(1, instruction);
        }

        /// <summary>Replaces the instruction at an absolute index with a static method call.</summary>
        public static FluentTranspiler ReplaceAtWithCall(this FluentTranspiler transpiler, int absoluteIndex, MethodInfo method)
        {
            return transpiler.ReplaceAt(absoluteIndex, new CodeInstruction(OpCodes.Call, method));
        }

        /// <summary>
        /// Replaces every instruction matching a predicate with a replacement instruction.
        /// The instruction count is preserved, making this suitable for compatibility rewrites.
        /// </summary>
        public static int ReplaceMatchingInstructions(
            this FluentTranspiler transpiler,
            Func<CodeInstruction, bool> predicate,
            Func<CodeInstruction, CodeInstruction> replacementFactory,
            string editLabel = null)
        {
            if (transpiler == null)
            {
                return 0;
            }

            if (predicate == null)
            {
                transpiler.AddWarning("ReplaceMatchingInstructions received a null predicate.");
                return 0;
            }

            if (replacementFactory == null)
            {
                transpiler.AddWarning("ReplaceMatchingInstructions received a null replacement factory.");
                return 0;
            }

            var instructions = transpiler.Instructions().ToList();
            int replaced = 0;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (!predicate(instructions[i]))
                {
                    continue;
                }

                CodeInstruction replacement = replacementFactory(instructions[i]);
                if (replacement == null)
                {
                    transpiler.AddWarning($"ReplaceMatchingInstructions factory returned null at index {i}.");
                    continue;
                }

                transpiler.ReplaceAt(i, replacement);
                replaced++;
            }

            if (replaced > 0 && !string.IsNullOrEmpty(editLabel))
            {
                transpiler.AddNote($"{editLabel}: replaced {replaced} instruction(s).");
            }

            return replaced;
        }

        /// <summary>Replaces every call instruction whose method matches the provided predicate.</summary>
        public static int ReplaceMatchingCalls(
            this FluentTranspiler transpiler,
            Func<MethodInfo, bool> methodPredicate,
            MethodInfo replacementMethod,
            string editLabel = null)
        {
            if (transpiler == null)
            {
                return 0;
            }

            if (methodPredicate == null)
            {
                transpiler.AddWarning("ReplaceMatchingCalls received a null method predicate.");
                return 0;
            }

            if (!IsValidStaticReplacementMethod(transpiler, replacementMethod, nameof(ReplaceMatchingCalls)))
            {
                return 0;
            }

            return transpiler.ReplaceMatchingInstructions(
                instruction => IsCallToMethod(instruction, methodPredicate),
                _ => new CodeInstruction(OpCodes.Call, replacementMethod),
                editLabel);
        }

        /// <summary>Checks whether the instruction stream contains a call matching the method predicate.</summary>
        public static bool HasMatchingCall(this FluentTranspiler transpiler, Func<MethodInfo, bool> methodPredicate)
        {
            if (transpiler == null)
            {
                return false;
            }

            if (methodPredicate == null)
            {
                transpiler.AddWarning("HasMatchingCall received a null method predicate.");
                return false;
            }

            foreach (CodeInstruction instruction in transpiler.Instructions())
            {
                if (IsCallToMethod(instruction, methodPredicate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Replaces a matched predicate sequence at an offset with a call. Returns false without
        /// diagnostics when the sequence is absent, allowing callers to try compatibility fallbacks.
        /// </summary>
        public static bool TryReplaceSequenceOffsetWithCall(
            this FluentTranspiler transpiler,
            Func<CodeInstruction, bool>[] pattern,
            int replacementOffset,
            MethodInfo replacementMethod,
            SearchMode mode = SearchMode.Start)
        {
            if (transpiler == null)
            {
                return false;
            }

            if (!IsValidStaticReplacementMethod(transpiler, replacementMethod, nameof(TryReplaceSequenceOffsetWithCall)))
            {
                return false;
            }

            if (pattern == null || pattern.Length == 0)
            {
                transpiler.AddWarning("TryReplaceSequenceOffsetWithCall received an empty predicate sequence.");
                return false;
            }

            if (replacementOffset < 0 || replacementOffset >= pattern.Length)
            {
                transpiler.AddWarning($"TryReplaceSequenceOffsetWithCall offset {replacementOffset} is outside pattern length {pattern.Length}.");
                return false;
            }

            if (!transpiler.TryFindSequence(mode, pattern))
            {
                return false;
            }

            transpiler.Advance(replacementOffset)
                      .ReplaceWithCall(replacementMethod);
            return true;
        }

        /// <summary>
        /// Replaces an expected sequence offset with a call. If the sequence is absent, replaces
        /// compatible calls identified by <paramref name="fallbackMethodPredicate"/> instead.
        /// </summary>
        public static FluentReplacementResult ReplaceSequenceOffsetWithCallOrFallbackCall(
            this FluentTranspiler transpiler,
            Func<CodeInstruction, bool>[] pattern,
            int replacementOffset,
            MethodInfo replacementMethod,
            Func<MethodInfo, bool> fallbackMethodPredicate,
            string editLabel = null,
            SearchMode mode = SearchMode.Start)
        {
            if (transpiler == null)
            {
                return FluentReplacementResult.NoMatch;
            }

            if (transpiler.TryReplaceSequenceOffsetWithCall(pattern, replacementOffset, replacementMethod, mode))
            {
                return FluentReplacementResult.PatternReplaced;
            }

            int fallbackReplacements = transpiler.ReplaceMatchingCalls(
                fallbackMethodPredicate,
                replacementMethod,
                editLabel);

            if (fallbackReplacements > 0)
            {
                return FluentReplacementResult.FallbackCallReplaced;
            }

            if (replacementMethod != null && transpiler.HasMatchingCall(method => method == replacementMethod))
            {
                return FluentReplacementResult.ReplacementAlreadyPresent;
            }

            return FluentReplacementResult.NoMatch;
        }

        private static bool IsCallToMethod(CodeInstruction instruction, Func<MethodInfo, bool> methodPredicate)
        {
            if (instruction == null ||
                (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) ||
                !(instruction.operand is MethodInfo method))
            {
                return false;
            }

            return methodPredicate(method);
        }

        private static bool IsValidStaticReplacementMethod(FluentTranspiler transpiler, MethodInfo method, string caller)
        {
            if (method == null)
            {
                transpiler.AddWarning($"{caller} received a null replacement method.");
                return false;
            }

            if (!method.IsStatic)
            {
                transpiler.AddWarning($"{caller} replacement method {method.DeclaringType?.Name}.{method.Name} must be static.");
                return false;
            }

            return true;
        }
    }
}
