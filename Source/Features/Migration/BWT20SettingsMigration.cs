using System;
using System.Collections.Generic;
using System.Xml;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Migration
{
    /// <summary>
    /// Migrates a persisted 1.x settings document without opting the player into
    /// any top-level 2.0 feature. Child preferences retain their normal defaults,
    /// and values explicitly present in the document always win.
    /// </summary>
    internal static class BWT20SettingsMigration
    {
        private static int startupSettingsLoadDepth;
        private static bool migratedDuringStartupLoad;

        internal static void BeginStartupSettingsLoad()
        {
            if (startupSettingsLoadDepth == 0)
            {
                migratedDuringStartupLoad = false;
            }

            startupSettingsLoadDepth++;
        }

        internal static bool EndStartupSettingsLoad()
        {
            if (startupSettingsLoadDepth > 0)
            {
                startupSettingsLoadDepth--;
            }

            return startupSettingsLoadDepth == 0 && migratedDuringStartupLoad;
        }

        internal static HashSet<string> CapturePersistedKeys()
        {
            if (startupSettingsLoadDepth == 0 ||
                Scribe.mode != LoadSaveMode.LoadingVars ||
                Scribe.loader?.curXmlParent == null)
            {
                return null;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode child in Scribe.loader.curXmlParent.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    keys.Add(child.Name);
                }
            }

            return keys;
        }

        internal static bool ApplyIfNeeded(
            BetterWorkTabSettings settings,
            HashSet<string> persistedKeys)
        {
            if (settings == null ||
                Scribe.mode != LoadSaveMode.LoadingVars ||
                !BWT20UpgradePolicy.NeedsMigration(
                    startupSettingsLoadDepth > 0,
                    settings.settingsSchemaVersion))
            {
                return false;
            }

            // Keep only the new top-level feature gates off for a public 1.0.5
            // migration. Child preferences must retain their normal defaults so
            // enabling a feature later does not require repairing every option.
            // Explicit prerelease 2.0 values still win.
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableSubWorkDrilldown), value => settings.enableSubWorkDrilldown = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableFluffyStyleFeatures), value => settings.enableFluffyStyleFeatures = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.useRuleBuilder2), value => settings.useRuleBuilder2 = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableTimePrioritySchedules), value => settings.enableTimePrioritySchedules = value);

            // These preferences existed in 1.0.5, but their 2.0 defaults changed.
            // Restore the old absent-key defaults while retaining any saved custom color.
            SetWhenAbsent(
                persistedKeys,
                nameof(settings.Color_FloatMenuHighlight),
                () => settings.Color_FloatMenuHighlight =
                    new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f));
            SetWhenAbsent(
                persistedKeys,
                nameof(settings.Color_CustomMouseHighlight),
                () => settings.Color_CustomMouseHighlight =
                    new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f));

            if (!BWT20UpgradePolicy.WasPersisted(persistedKeys, nameof(settings.workGridRendererMode)))
            {
                settings.workGridRendererMode = WorkGridRendererMode.Optimized;
            }

            // The launch dialog decides whether to open tutorial choices. The
            // tutorial then offers the public 2.0 and full-course tracks itself.
            settings.showGeneralTutorial = false;
            settings.tutorialWelcomeCompleted = true;
            settings.activeTutorialLessonId = string.Empty;
            settings.tutorialLessonPhase = 0;
            settings.tutorialFlowVersion = BWTGeneralTutorial.CurrentFlowVersion;
            settings.v2UpgradePromptPending = true;
            settings.settingsSchemaVersion = BWT20UpgradePolicy.CurrentSettingsSchemaVersion;
            migratedDuringStartupLoad = true;
            return true;
        }

        /// <summary>
        /// Opts a public-1.0.5 migration into the supported 2.0 feature set when
        /// the player explicitly chooses a tutorial course. Child preferences
        /// already hold their normal defaults and are not rewritten here.
        /// </summary>
        internal static void EnablePublic20TutorialFeatures(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.enableSubWorkDrilldown = DefaultSettings.enableSubWorkDrilldown;
            settings.enableFluffyStyleFeatures = DefaultSettings.enableFluffyStyleFeatures;
            settings.useRuleBuilder2 = DefaultSettings.useRuleBuilder2;
            settings.enableTimePrioritySchedules = DefaultSettings.enableTimePrioritySchedules;
            settings.SetPriorityMode(PriorityMode.BetterWorkTab);
        }

        private static void SetFalseWhenAbsent(
            HashSet<string> persistedKeys,
            string key,
            Action<bool> setter)
        {
            if (!BWT20UpgradePolicy.WasPersisted(persistedKeys, key))
            {
                setter(false);
            }
        }

        private static void SetWhenAbsent(
            HashSet<string> persistedKeys,
            string key,
            Action setter)
        {
            if (!BWT20UpgradePolicy.WasPersisted(persistedKeys, key))
            {
                setter();
            }
        }
    }
}
