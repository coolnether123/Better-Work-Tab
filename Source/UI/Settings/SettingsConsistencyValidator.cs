using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Better_Work_Tab.UI.SettingsFramework;
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
            nameof(BetterWorkTabSettings.bwtPlayerIdentifier),
            nameof(BetterWorkTabSettings.debugPrintLayout),
            nameof(BetterWorkTabSettings.workTabMaxHeight),
            nameof(BetterWorkTabSettings.firstTimeSetupDone),
            nameof(BetterWorkTabSettings.subWorkCtrlClickNoticeDismissed),
            nameof(BetterWorkTabSettings.settingsSchemaVersion),
            nameof(BetterWorkTabSettings.v2UpgradePromptPending),
            nameof(BetterWorkTabSettings.fluffyWorkTabActivePromptVersion),
            nameof(BetterWorkTabSettings.workTabOwnerSelectionMade),
            nameof(BetterWorkTabSettings.sleekWorkTabChoicePromptDismissed),
            nameof(BetterWorkTabSettings.sleekWorkTabUseMixedByDefault),

            // Beta feedback bookkeeping: accumulated Work-tab time and whether
            // the nudge has been answered. Neither is a preference, so neither
            // belongs in the settings window.
            nameof(BetterWorkTabSettings.betaFeedbackWorkTabSeconds),
            nameof(BetterWorkTabSettings.betaFeedbackPromptAnswered)
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
            LogDiagnostics(ValidateRegistry());
            if (LanguageDatabase.activeLanguage == null)
            {
                LongEventHandler.ExecuteWhenFinished(
                    () => LogDiagnostics(ValidateTranslationCoverage(BWTSettingsRegistry.Definitions)));
            }
        }

        public static IReadOnlyList<string> ValidateRegistry()
        {
            BWTSettingsRegistry.EnsureInitialized();
            IReadOnlyList<SettingDefinition> definitions = BWTSettingsRegistry.Definitions;
            List<string> diagnostics = new List<string>(CollectDiagnostics(definitions));
            if (LanguageDatabase.activeLanguage != null)
            {
                diagnostics.AddRange(ValidateTranslationCoverage(definitions));
            }

            return diagnostics.AsReadOnly();
        }

        public static void Validate(IEnumerable<SettingDefinition> definitions)
        {
            LogDiagnostics(CollectDiagnostics(definitions));
            if (LanguageDatabase.activeLanguage != null)
            {
                LogDiagnostics(ValidateTranslationCoverage(definitions));
            }
        }

        internal static IReadOnlyList<string> CollectDiagnostics(IEnumerable<SettingDefinition> definitions)
        {
            List<SettingDefinition> defs = definitions?.Where(def => def != null).ToList() ?? new List<SettingDefinition>();
            Type settingsType = typeof(BetterWorkTabSettings);
            BetterWorkTabSettings freshSettings = new BetterWorkTabSettings();
            var diagnostics = new List<string>();
            var ids = new HashSet<string>();
            var scribeKeys = new HashSet<string>();
            var registeredFields = new HashSet<string>();

            foreach (SettingDefinition def in defs)
            {
                if (!string.IsNullOrEmpty(def.Id) && !ids.Add(def.Id))
                {
                    diagnostics.Add("Duplicate setting id: " + def.Id);
                }

                // A setting that shipped in public 1.0.x must stay reachable.
                //
                // The stronger rule — that all of them stay in the simple view —
                // was tempting and is not viable: ninety-four registered settings
                // have 1.0.x fields, so honouring it would rebuild the crowded
                // list that trimming was meant to fix. What an upgrading player
                // actually cannot survive is a setting they rely on vanishing
                // from the UI altogether, so that is what is asserted. Moving one
                // into Advanced is a judgement call; removing it is a bug.
                if (BWTSettingsFilters.IsV10Setting(def) &&
                    !def.ShowInSimpleView &&
                    !def.ShowInAdvancedView)
                {
                    diagnostics.Add("Public 1.0 setting is unreachable in both views: " + def.Id);
                }

                string scribeKey = SettingsScribe.EffectiveScribeKey(def);
                if (!string.IsNullOrEmpty(scribeKey) && !scribeKeys.Add(scribeKey))
                {
                    diagnostics.Add("Duplicate scribe key: " + scribeKey);
                }

                if (string.IsNullOrEmpty(def.FieldName))
                {
                    continue;
                }

                registeredFields.Add(def.FieldName);
                FieldInfo field = settingsType.GetField(def.FieldName);
                if (field == null)
                {
                    diagnostics.Add("FieldName does not resolve: " + def.FieldName);
                    continue;
                }

                if (def.DefaultValue != null && !field.FieldType.IsInstanceOfType(def.DefaultValue))
                {
                    diagnostics.Add("DefaultValue type mismatch for " + def.FieldName);
                    continue;
                }

                object freshValue = field.GetValue(freshSettings);
                if (def.DefaultValue != null && !ValuesEqual(def.DefaultValue, freshValue))
                {
                    diagnostics.Add("DefaultValue drift for " + def.FieldName);
                }
            }

            foreach (FieldInfo field in settingsType
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                if (field.IsLiteral ||
                    registeredFields.Contains(field.Name) ||
                    StateFields.Contains(field.Name) ||
                    UnregisteredPreferenceFields.Contains(field.Name))
                {
                    continue;
                }

                diagnostics.Add("Public settings field is not classified: " + field.Name);
            }

            return diagnostics.AsReadOnly();
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

        internal static IReadOnlyList<string> ValidateTranslationCoverage(
            IEnumerable<SettingDefinition> definitions)
        {
            var diagnostics = new List<string>();
            foreach (SettingDefinition definition in definitions ?? Enumerable.Empty<SettingDefinition>())
            {
                AddTranslationDiagnostics(definition, diagnostics);
            }

            return diagnostics.AsReadOnly();
        }

        private static void AddTranslationDiagnostics(
            SettingDefinition def,
            List<string> diagnostics)
        {
            if (def == null || (!def.ShowInSimpleView && !def.ShowInAdvancedView))
            {
                return;
            }

            AddTranslationDiagnostic(
                BWTSettingsTranslation.GetLabelKey(def),
                def.Id,
                "label",
                diagnostics);
            AddTranslationDiagnostic(
                BWTSettingsTranslation.GetTooltipKey(def),
                def.Id,
                "tooltip",
                diagnostics);
        }

        private static void AddTranslationDiagnostic(
            string key,
            string settingId,
            string role,
            List<string> diagnostics)
        {
            if (!string.IsNullOrEmpty(key) && !key.CanTranslate())
            {
                diagnostics.Add($"Missing {role} translation for {settingId}: {key}");
            }
        }

        private static void LogDiagnostics(IEnumerable<string> diagnostics)
        {
            foreach (string diagnostic in diagnostics ?? Enumerable.Empty<string>())
            {
                Log.Warning("[Better Work Tab] Settings validation: " + diagnostic);
            }
        }
    }
}
