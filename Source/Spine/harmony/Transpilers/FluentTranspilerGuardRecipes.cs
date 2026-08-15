using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Spine.Harmony
{
    /// <summary>
    /// High-level recipes for guarding existing call sites with branch-safe control flow.
    /// </summary>
    public static class FluentTranspilerGuardRecipes
    {
        public static FluentCallGuardSelection BeforeCall(
            this FluentTranspiler transpiler,
            MethodInfo targetCall,
            SearchMode mode = SearchMode.Start)
        {
            return new FluentCallGuardSelection(transpiler, targetCall, mode);
        }
    }

    public sealed class FluentCallGuardSelection
    {
        private readonly FluentTranspiler _transpiler;
        private readonly MethodInfo _targetCall;
        private readonly SearchMode _mode;
        private int _includedPreviousInstructionCount;

        internal FluentCallGuardSelection(FluentTranspiler transpiler, MethodInfo targetCall, SearchMode mode)
        {
            _transpiler = transpiler;
            _targetCall = targetCall;
            _mode = mode;
        }

        public FluentCallGuardSelection IncludingPreviousInstruction()
        {
            _includedPreviousInstructionCount = 1;
            return this;
        }

        public FluentReplacementResult SkipOriginalWhen(Action<FluentGuardBuilder> configureGuard, string editLabel = null)
        {
            if (_transpiler == null)
            {
                return FluentReplacementResult.NoMatch;
            }

            if (_targetCall == null)
            {
                _transpiler.AddWarning("BeforeCall received a null target call.");
                return FluentReplacementResult.NoMatch;
            }

            if (configureGuard == null)
            {
                _transpiler.AddWarning("SkipOriginalWhen received a null guard configuration.");
                return FluentReplacementResult.NoMatch;
            }

            var instructions = _transpiler.Instructions().ToList();
            int callIndex = FindSingleCallIndex(instructions);
            if (callIndex == -2)
            {
                return FluentReplacementResult.AmbiguousMatch;
            }

            if (callIndex < 0)
            {
                _transpiler.AddSoftFailure($"BeforeCall found no calls to {FluentTranspilerFormatting.FormatMethod(_targetCall)}.");
                return FluentReplacementResult.NoMatch;
            }

            int replaceStart = callIndex - _includedPreviousInstructionCount;
            if (replaceStart < 0)
            {
                _transpiler.AddWarning($"BeforeCall cannot include {_includedPreviousInstructionCount} instruction(s) before call at index {callIndex}.");
                return FluentReplacementResult.NoMatch;
            }

            _transpiler.DefineLabel(out Label runOriginalLabel)
                       .DefineLabel(out Label skipOriginalLabel);

            var builder = new FluentGuardBuilder(_transpiler, runOriginalLabel, skipOriginalLabel);
            configureGuard(builder);

            List<CodeInstruction> guardInstructions = builder.Build();
            if (guardInstructions.Count == 0)
            {
                _transpiler.AddWarning("SkipOriginalWhen guard produced no instructions.");
                return FluentReplacementResult.NoMatch;
            }

            int afterOriginalIndex = callIndex + 1;
            bool hasInstructionAfterOriginal = afterOriginalIndex < instructions.Count;
            if (hasInstructionAfterOriginal)
            {
                instructions[afterOriginalIndex].labels.Add(skipOriginalLabel);
            }

            var replacement = new List<CodeInstruction>(guardInstructions.Count + _includedPreviousInstructionCount + 2);
            replacement.AddRange(guardInstructions);

            for (int i = replaceStart; i <= callIndex; i++)
            {
                replacement.Add(CloneWithoutAnchors(instructions[i]));
            }

            replacement[guardInstructions.Count].labels.Add(runOriginalLabel);

            if (!hasInstructionAfterOriginal)
            {
                var skipAnchor = new CodeInstruction(OpCodes.Nop);
                skipAnchor.labels.Add(skipOriginalLabel);
                replacement.Add(skipAnchor);
            }

            CodeInstruction originalEntry = instructions[replaceStart];
            _transpiler.MoveTo(replaceStart)
                       .ReplaceSequence(callIndex - replaceStart + 1, replacement.ToArray());

            if (ReferenceEquals(_transpiler.Instructions().ElementAt(replaceStart), originalEntry))
            {
                return FluentReplacementResult.UnsafeMatch;
            }

            return FluentReplacementResult.PatternReplaced;
        }

        private int FindSingleCallIndex(IList<CodeInstruction> instructions)
        {
            int startIndex = 0;
            if (_mode == SearchMode.Current)
            {
                startIndex = Math.Max(0, _transpiler.CurrentIndex);
            }
            else if (_mode == SearchMode.Next)
            {
                startIndex = Math.Max(0, _transpiler.CurrentIndex + 1);
            }

            int matchIndex = -1;
            for (int i = startIndex; i < instructions.Count; i++)
            {
                if (instructions[i] != null && instructions[i].Calls(_targetCall))
                {
                    if (matchIndex >= 0)
                    {
                        _transpiler.AddWarning(
                            $"BeforeCall found multiple calls to {FluentTranspilerFormatting.FormatMethod(_targetCall)}.");
                        return -2;
                    }

                    matchIndex = i;
                }
            }

            return matchIndex;
        }

        private static CodeInstruction CloneWithoutAnchors(CodeInstruction instruction)
        {
            var clone = new CodeInstruction(instruction);
            clone.labels.Clear();
            clone.blocks.Clear();
            return clone;
        }

    }

    public sealed class FluentGuardBuilder
    {
        private readonly FluentTranspiler _transpiler;
        private readonly Label _runOriginalLabel;
        private readonly Label _skipOriginalLabel;
        private readonly List<CodeInstruction> _instructions = new List<CodeInstruction>();
        private bool _requiresTerminalSkip;
        private bool _isValid = true;

        internal FluentGuardBuilder(FluentTranspiler transpiler, Label runOriginalLabel, Label skipOriginalLabel)
        {
            _transpiler = transpiler;
            _runOriginalLabel = runOriginalLabel;
            _skipOriginalLabel = skipOriginalLabel;
        }

        public FluentGuardBuilder RequireStaticFieldNotNull(FieldInfo field)
        {
            if (!ValidateField(field, nameof(RequireStaticFieldNotNull)))
            {
                return this;
            }

            if (!field.IsStatic)
            {
                _transpiler.AddWarning($"{nameof(RequireStaticFieldNotNull)} expected static field {FluentTranspilerFormatting.FormatField(field)}.");
                _isValid = false;
                return this;
            }

            _instructions.Add(new CodeInstruction(OpCodes.Ldsfld, field));
            _instructions.Add(new CodeInstruction(OpCodes.Brfalse, _runOriginalLabel));
            _requiresTerminalSkip = true;
            return this;
        }

        public FluentGuardBuilder RequireStaticFieldInstanceFieldTrue(FieldInfo ownerField, FieldInfo boolField)
        {
            if (!ValidateField(ownerField, nameof(RequireStaticFieldInstanceFieldTrue)) ||
                !ValidateField(boolField, nameof(RequireStaticFieldInstanceFieldTrue)))
            {
                return this;
            }

            if (!ownerField.IsStatic)
            {
                _transpiler.AddWarning($"{nameof(RequireStaticFieldInstanceFieldTrue)} expected static owner field {FluentTranspilerFormatting.FormatField(ownerField)}.");
                _isValid = false;
                return this;
            }

            if (boolField.IsStatic || !object.Equals(boolField.FieldType, typeof(bool)))
            {
                _transpiler.AddWarning($"{nameof(RequireStaticFieldInstanceFieldTrue)} expected instance bool field {FluentTranspilerFormatting.FormatField(boolField)}.");
                _isValid = false;
                return this;
            }

            _instructions.Add(new CodeInstruction(OpCodes.Ldsfld, ownerField));
            _instructions.Add(new CodeInstruction(OpCodes.Ldfld, boolField));
            _instructions.Add(new CodeInstruction(OpCodes.Brfalse, _runOriginalLabel));
            _requiresTerminalSkip = true;
            return this;
        }

        public FluentGuardBuilder RequireCallTrue(MethodInfo method)
        {
            if (method == null)
            {
                _transpiler.AddWarning($"{nameof(RequireCallTrue)} received a null method.");
                _isValid = false;
                return this;
            }

            if (!method.IsStatic || !object.Equals(method.ReturnType, typeof(bool)) || method.GetParameters().Length != 0)
            {
                _transpiler.AddWarning($"{nameof(RequireCallTrue)} expected a parameterless static bool method {FluentTranspilerFormatting.FormatMethod(method)}.");
                _isValid = false;
                return this;
            }

            _instructions.Add(new CodeInstruction(OpCodes.Call, method));
            _instructions.Add(new CodeInstruction(OpCodes.Brfalse, _runOriginalLabel));
            _requiresTerminalSkip = true;
            return this;
        }

        public FluentGuardBuilder SkipIfThisIs(Type type)
        {
            if (type == null)
            {
                _transpiler.AddWarning($"{nameof(SkipIfThisIs)} received a null type.");
                _isValid = false;
                return this;
            }

            _instructions.Add(new CodeInstruction(OpCodes.Ldarg_0));
            _instructions.Add(new CodeInstruction(OpCodes.Isinst, type));
            _instructions.Add(new CodeInstruction(OpCodes.Brfalse, _runOriginalLabel));
            _requiresTerminalSkip = true;
            return this;
        }

        internal List<CodeInstruction> Build()
        {
            if (!_isValid)
            {
                return new List<CodeInstruction>();
            }

            var result = _instructions
                .Select(instruction => new CodeInstruction(instruction))
                .ToList();
            if (_requiresTerminalSkip)
            {
                result.Add(new CodeInstruction(
                    OpCodes.Br,
                    _skipOriginalLabel));
            }
            return result;
        }

        private bool ValidateField(FieldInfo field, string caller)
        {
            if (field != null)
            {
                return true;
            }

            _transpiler.AddWarning($"{caller} received a null field.");
            _isValid = false;
            return false;
        }

    }
}
