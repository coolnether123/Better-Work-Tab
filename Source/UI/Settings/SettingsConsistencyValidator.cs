using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if HAS_LUDEON_TK
using LudeonTK;
#endif
using Spine.UI.SettingsFramework;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    public static class SettingsConsistencyValidator
    {
        private static bool _validatedAtStartup;

        private static readonly HashSet<string> StateFields = new HashSet<string>
        {
            nameof(BetterWorkTabSettings.SavedRulesets),
            nameof(BetterWorkTabSettings.SavedRuleBuilder2Rulesets),
            nameof(BetterWorkTabSettings.CurrentRuleBuilder2Ruleset),
            nameof(BetterWorkTabSettings.CurrentRuleset),
            nameof(BetterWorkTabSettings.currentRulesetName),
            nameof(BetterWorkTabSettings.currentRuleBuilder2RulesetStableId),
            nameof(BetterWorkTabSettings.defaultAutoAssignRuleset),
            nameof(BetterWorkTabSettings.workColumnOrderDefNames),
            nameof(BetterWorkTabSettings.storedColumnWidths),
            nameof(BetterWorkTabSettings.playerDraggedColumns),
            nameof(BetterWorkTabSettings.viewedSettingIds),
            nameof(BetterWorkTabSettings.debugFeatureToggles),
            nameof(BetterWorkTabSettings.LegacyWorkGiverReassignments),
            nameof(BetterWorkTabSettings.tutorialWelcomeCompleted),
            nameof(BetterWorkTabSettings.tutorialFlowVersion),
            nameof(BetterWorkTabSettings.activeTutorialLessonId),
            nameof(BetterWorkTabSettings.tutorialLessonPhase),
            nameof(BetterWorkTabSettings.completedTutorialLessonIds),
            nameof(BetterWorkTabSettings.ruleBuilder2TutorialStep),
            nameof(BetterWorkTabSettings.bwtPlayerIdentifier),
            nameof(BetterWorkTabSettings.debugPrintLayout),
            nameof(BetterWorkTabSettings.workTabMaxHeight),
            nameof(BetterWorkTabSettings.firstTimeSetupDone),
            nameof(BetterWorkTabSettings.subWorkCtrlClickNoticeDismissed)
        };

        private static readonly HashSet<string> UnregisteredPreferenceFields = new HashSet<string>
        {
            nameof(BetterWorkTabSettings.hiddenWorktypes)
        };

        public static void ValidateAtStartup()
        {
            if (_validatedAtStartup || !Prefs.DevMode)
            {
                return;
            }

            _validatedAtStartup = true;
            Validate(BWTSettingsRegistry.Definitions);
        }

#if HAS_LUDEON_TK
        [DebugAction("Better Work Tab", "Validate settings registry", actionType = DebugActionType.Action)]
#endif
        public static void ValidateFromDebugAction()
        {
            BWTSettingsRegistry.EnsureInitialized();
            Validate(BWTSettingsRegistry.Definitions);
        }

        public static void Validate(IEnumerable<SettingDefinition> definitions)
        {
            List<SettingDefinition> defs = definitions?.Where(def => def != null).ToList() ?? new List<SettingDefinition>();
            Type settingsType = typeof(BetterWorkTabSettings);
            BetterWorkTabSettings freshSettings = new BetterWorkTabSettings();
            var ids = new HashSet<string>();
            var scribeKeys = new HashSet<string>();
            var registeredFields = new HashSet<string>();

            foreach (SettingDefinition def in defs)
            {
                if (!string.IsNullOrEmpty(def.Id) && !ids.Add(def.Id))
                {
                    Warn("Duplicate setting id: " + def.Id);
                }

                string scribeKey = SettingsScribe.EffectiveScribeKey(def);
                if (!string.IsNullOrEmpty(scribeKey) && !scribeKeys.Add(scribeKey))
                {
                    Warn("Duplicate scribe key: " + scribeKey);
                }

                if (string.IsNullOrEmpty(def.FieldName))
                {
                    continue;
                }

                registeredFields.Add(def.FieldName);
                FieldInfo field = settingsType.GetField(def.FieldName);
                if (field == null)
                {
                    Warn("FieldName does not resolve: " + def.FieldName);
                    continue;
                }

                if (def.DefaultValue != null && !field.FieldType.IsInstanceOfType(def.DefaultValue))
                {
                    Warn("DefaultValue type mismatch for " + def.FieldName);
                    continue;
                }

                object freshValue = field.GetValue(freshSettings);
                if (def.DefaultValue != null && !ValuesEqual(def.DefaultValue, freshValue))
                {
                    Warn("DefaultValue drift for " + def.FieldName);
                }
            }

            foreach (FieldInfo field in settingsType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.IsLiteral ||
                    registeredFields.Contains(field.Name) ||
                    StateFields.Contains(field.Name) ||
                    UnregisteredPreferenceFields.Contains(field.Name))
                {
                    continue;
                }

                Warn("Public settings field is not classified: " + field.Name);
            }
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (Equals(left, right))
            {
                return true;
            }

            if (left is IEnumerable leftItems && right is IEnumerable rightItems && !(left is string) && !(right is string))
            {
                return leftItems.Cast<object>().SequenceEqual(rightItems.Cast<object>());
            }

            return false;
        }

        private static void Warn(string message)
        {
            Log.Warning("[Better Work Tab] Settings validation: " + message);
        }
    }
}
