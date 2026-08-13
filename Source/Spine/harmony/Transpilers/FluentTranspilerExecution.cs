using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Spine.Harmony.Infrastructure;

namespace Spine.Harmony
{
    /// <summary>
    /// Generic execution helpers for running fluent transpilers with caller-defined fallback policy.
    /// </summary>
    public static class FluentTranspilerExecution
    {
        /// <summary>
        /// Executes one required fluent recipe and returns the untouched input when
        /// the recipe cannot be applied safely. Failure is reported once per patch.
        /// </summary>
        public static IEnumerable<CodeInstruction> ExecuteRequiredOrOriginal(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original,
            ILGenerator generator,
            string patchName,
            Func<FluentTranspiler, FluentReplacementResult> transform)
        {
            return ExecuteOrOriginal(
                instructions,
                original,
                generator,
                transpiler =>
                {
                    FluentReplacementResult result = transform != null
                        ? transform(transpiler)
                        : FluentReplacementResult.Failed;
                    if (!result.Succeeded())
                    {
                        throw new InvalidOperationException(
                            (patchName ?? "Required transpiler recipe") +
                            " was not applied: " + result + ".");
                    }
                },
                (codes, method, exception) =>
                {
                    string methodName = method?.DeclaringType != null
                        ? method.DeclaringType.FullName + "." + method.Name
                        : method?.Name ?? "<unknown method>";
                    string name = patchName ?? "Required transpiler recipe";
                    string message = name + " skipped for " + methodName + ": " +
                        exception.GetType().Name + ": " + exception.Message +
                        " Leaving the original IL unchanged.";

                    try
                    {
                        MMLog.WarnOnce(name + ":" + methodName, message);
                    }
                    catch
                    {
                        // Diagnostics must not turn a safe fallback into a patch failure.
                    }

                    return codes;
                });
        }

        public static IEnumerable<CodeInstruction> ExecuteOrOriginal(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original,
            ILGenerator generator,
            Action<FluentTranspiler> transformer,
            Func<List<CodeInstruction>, MethodBase, Exception, IEnumerable<CodeInstruction>> onFailure)
        {
            return ExecuteOrOriginal(
                instructions,
                original,
                generator,
                TranspilerSafetyPolicy.DefaultExecuteProfile,
                transformer,
                onFailure);
        }

        public static IEnumerable<CodeInstruction> ExecuteOrOriginal(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original,
            ILGenerator generator,
            FluentTranspiler.BuildProfile profile,
            Action<FluentTranspiler> transformer,
            Func<List<CodeInstruction>, MethodBase, Exception, IEnumerable<CodeInstruction>> onFailure)
        {
            var originalInstructions = CloneInstructions(instructions);

            try
            {
                return FluentTranspiler.Execute(
                    CloneInstructions(originalInstructions),
                    original,
                    generator,
                    profile,
                    transformer);
            }
            catch (Exception ex)
            {
                return onFailure != null
                    ? onFailure(originalInstructions, original, ex)
                    : originalInstructions;
            }
        }

        private static List<CodeInstruction> CloneInstructions(IEnumerable<CodeInstruction> instructions)
        {
            return instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
        }
    }
}
