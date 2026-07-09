using Better_Work_Tab;
using Better_Work_Tab.UI.Settings;
using LudeonTK;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Spine.UI.SettingsFramework
{
    public static class SettingsScribe
    {
        private static readonly MethodInfo ScribeValuesLookMethod = typeof(Scribe_Values)
            .GetMethods()
            .First(method =>
            {
                if (method.Name != "Look" || !method.IsGenericMethodDefinition)
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length >= 3 &&
                    parameters[0].ParameterType.IsByRef &&
                    parameters[1].ParameterType == typeof(string);
            });

        public static void ScribeAll(object settings, IEnumerable<SettingDefinition> definitions)
        {
            if (settings == null || definitions == null)
            {
                return;
            }

            Type settingsType = settings.GetType();
            foreach (SettingDefinition def in definitions)
            {
                if (def == null ||
                    def.Classification == SettingClassification.State ||
                    def.DisableAutoScribe ||
                    string.IsNullOrEmpty(def.FieldName))
                {
                    continue;
                }

                FieldInfo field = settingsType.GetField(def.FieldName);
                if (field == null || typeof(IEnumerable).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(string))
                {
                    continue;
                }

                object defaultValue = def.ScribeDefaultOverride ?? def.DefaultValue;
                if (defaultValue == null)
                {
                    continue;
                }

                string scribeKey = string.IsNullOrEmpty(def.ScribeKey) ? def.FieldName : def.ScribeKey;
                object[] args = CreateScribeArgs(field.GetValue(settings), scribeKey, defaultValue);
                ScribeValuesLookMethod.MakeGenericMethod(field.FieldType).Invoke(null, args);
                field.SetValue(settings, args[0]);
            }
        }

        public static void ApplyPreferenceDefaults(object settings, IEnumerable<SettingDefinition> definitions)
        {
            if (settings == null || definitions == null)
            {
                return;
            }

            Type settingsType = settings.GetType();
            foreach (SettingDefinition def in definitions)
            {
                if (def == null ||
                    def.Classification == SettingClassification.State ||
                    def.DefaultValue == null ||
                    string.IsNullOrEmpty(def.FieldName))
                {
                    continue;
                }

                FieldInfo field = settingsType.GetField(def.FieldName);
                if (field == null || !field.FieldType.IsInstanceOfType(def.DefaultValue))
                {
                    continue;
                }

                field.SetValue(settings, def.DefaultValue);
            }
        }

        public static string EffectiveScribeKey(SettingDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.FieldName))
            {
                return null;
            }

            return string.IsNullOrEmpty(def.ScribeKey) ? def.FieldName : def.ScribeKey;
        }

        private static object[] CreateScribeArgs(object value, string scribeKey, object defaultValue)
        {
            ParameterInfo[] parameters = ScribeValuesLookMethod.GetParameters();
            object[] args = new object[parameters.Length];
            args[0] = value;
            args[1] = scribeKey;
            args[2] = defaultValue;

            for (int i = 3; i < args.Length; i++)
            {
                args[i] = parameters[i].DefaultValue == DBNull.Value ? Type.Missing : parameters[i].DefaultValue;
            }

            return args;
        }
    }

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
            nameof(BetterWorkTabSettings.generalTutorialStep),
            nameof(BetterWorkTabSettings.betaTutorialStep),
            nameof(BetterWorkTabSettings.ruleBuilder2TutorialStep),
            nameof(BetterWorkTabSettings.bwtPlayerIdentifier),
            nameof(BetterWorkTabSettings.debugPrintLayout),
            nameof(BetterWorkTabSettings.workTabMaxHeight),
            nameof(BetterWorkTabSettings.firstTimeSetupDone),
            nameof(BetterWorkTabSettings.subWorkCtrlClickNoticeDismissed)
        };

        // Preferences scribed manually in ExposeData (collections the auto-scribe skips).
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

        [DebugAction("Better Work Tab", "Validate settings registry", false, false, false, false, false, 0, false, actionType = DebugActionType.Action)]
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
