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
    /// any 2.0 feature. Values explicitly present in the document always win.
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

            // Every field below was introduced after the public 1.0.5 schema.
            // Only absent fields receive the compatibility value so an explicit
            // value from a prerelease 2.0 config is never overwritten.
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableSubWorkDrilldown), value => settings.enableSubWorkDrilldown = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableFluffyStyleFeatures), value => settings.enableFluffyStyleFeatures = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showFluffyStyleTopButtons), value => settings.showFluffyStyleTopButtons = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showStandaloneFluffyStyleTopButtons), value => settings.showStandaloneFluffyStyleTopButtons = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableFluffyScheduleAssigner), value => settings.enableFluffyScheduleAssigner = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showSubWorkHeaderBadge), value => settings.showSubWorkHeaderBadge = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableSubWorkCrossWorkDragDrop), value => settings.enableSubWorkCrossWorkDragDrop = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableCustomWorkLabels), value => settings.enableCustomWorkLabels = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.useVanillaSubWorkGlobalPriorityBoxes), value => settings.useVanillaSubWorkGlobalPriorityBoxes = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.useCompactSubWorkPriorityBoxes), value => settings.useCompactSubWorkPriorityBoxes = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.restoreCursorOnSubWorkExit), value => settings.restoreCursorOnSubWorkExit = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.restoreCursorOnSubWorkPawnCellExit), value => settings.restoreCursorOnSubWorkPawnCellExit = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableSubWorkOverrideBreakAnimation), value => settings.enableSubWorkOverrideBreakAnimation = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableSubWorkTransitionAnimation), value => settings.enableSubWorkTransitionAnimation = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.subWorkAutoExpandColumns), value => settings.subWorkAutoExpandColumns = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.subWorkEvenlyExpandColumns), value => settings.subWorkEvenlyExpandColumns = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showContextSettingsHint), value => settings.showContextSettingsHint = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showGeneralTutorial), value => settings.showGeneralTutorial = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.useRuleBuilder2), value => settings.useRuleBuilder2 = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showRuleBuilder2Tutorial), value => settings.showRuleBuilder2Tutorial = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.ruleBuilder2ShowWorkTabHighlights), value => settings.ruleBuilder2ShowWorkTabHighlights = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.ruleBuilder2EnableAnimations), value => settings.ruleBuilder2EnableAnimations = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.ruleBuilder2UseDraftSuggestions), value => settings.ruleBuilder2UseDraftSuggestions = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.ruleBuilder2ShowAdvancedConditions), value => settings.ruleBuilder2ShowAdvancedConditions = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.ruleBuilder2ShowMatchedPanel), value => settings.ruleBuilder2ShowMatchedPanel = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableTimePrioritySchedules), value => settings.enableTimePrioritySchedules = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showTimePriorityCopyPasteButtons), value => settings.showTimePriorityCopyPasteButtons = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableChronosPointerTimePriorityIntegration), value => settings.enableChronosPointerTimePriorityIntegration = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.showTimePriorityHourDivider), value => settings.showTimePriorityHourDivider = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.keepTimePrioritySourceColumnHighlighted), value => settings.keepTimePrioritySourceColumnHighlighted = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableFluffyTimePriorityMirroring), value => settings.enableFluffyTimePriorityMirroring = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.chronosPointerTimePriorityIncidentOverlay), value => settings.chronosPointerTimePriorityIncidentOverlay = value);
            SetFalseWhenAbsent(persistedKeys, nameof(settings.enableDividerAnimations), value => settings.enableDividerAnimations = value);
            SetFalseWhenAbsent(persistedKeys, "showFluffyWorkTabColumns", value => settings.showExternalWorkTabColumns = value);

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

            // The launch dialog owns the tutorial choice for an upgrader. Mark the
            // generic tutorial welcome resolved so it cannot race this prompt.
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
