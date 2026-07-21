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
        /// the player explicitly chooses a tutorial course. Migration itself
        /// remains conservative until that choice is made.
        /// </summary>
        internal static void EnablePublic20TutorialFeatures(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.enableSubWorkDrilldown = DefaultSettings.enableSubWorkDrilldown;
            settings.enableFluffyStyleFeatures = DefaultSettings.enableFluffyStyleFeatures;
            settings.showFluffyStyleTopButtons = DefaultSettings.showFluffyStyleTopButtons;
            settings.showStandaloneFluffyStyleTopButtons = DefaultSettings.showStandaloneFluffyStyleTopButtons;
            settings.enableFluffyScheduleAssigner = DefaultSettings.enableFluffyScheduleAssigner;
            settings.showSubWorkHeaderBadge = DefaultSettings.showSubWorkHeaderBadge;
            settings.enableSubWorkCrossWorkDragDrop = DefaultSettings.enableSubWorkCrossWorkDragDrop;
            settings.enableCustomWorkLabels = DefaultSettings.enableCustomWorkLabels;
            settings.useVanillaSubWorkGlobalPriorityBoxes = DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes;
            settings.useCompactSubWorkPriorityBoxes = DefaultSettings.useCompactSubWorkPriorityBoxes;
            settings.restoreCursorOnSubWorkExit = DefaultSettings.restoreCursorOnSubWorkExit;
            settings.restoreCursorOnSubWorkPawnCellExit = DefaultSettings.restoreCursorOnSubWorkPawnCellExit;
            settings.enableSubWorkOverrideBreakAnimation = DefaultSettings.enableSubWorkOverrideBreakAnimation;
            settings.enableSubWorkTransitionAnimation = DefaultSettings.enableSubWorkTransitionAnimation;
            settings.subWorkAutoExpandColumns = DefaultSettings.subWorkAutoExpandColumns;
            settings.subWorkEvenlyExpandColumns = DefaultSettings.subWorkEvenlyExpandColumns;
            settings.showContextSettingsHint = DefaultSettings.showContextSettingsHint;
            settings.useRuleBuilder2 = DefaultSettings.useRuleBuilder2;
            settings.showRuleBuilder2Tutorial = DefaultSettings.showRuleBuilder2Tutorial;
            settings.ruleBuilder2ShowWorkTabHighlights = DefaultSettings.ruleBuilder2ShowWorkTabHighlights;
            settings.ruleBuilder2EnableAnimations = DefaultSettings.ruleBuilder2EnableAnimations;
            settings.ruleBuilder2UseDraftSuggestions = DefaultSettings.ruleBuilder2UseDraftSuggestions;
            settings.ruleBuilder2ShowAdvancedConditions = DefaultSettings.ruleBuilder2ShowAdvancedConditions;
            settings.ruleBuilder2ShowMatchedPanel = DefaultSettings.ruleBuilder2ShowMatchedPanel;
            settings.enableTimePrioritySchedules = DefaultSettings.enableTimePrioritySchedules;
            settings.showTimePriorityCopyPasteButtons = DefaultSettings.showTimePriorityCopyPasteButtons;
            settings.enableChronosPointerTimePriorityIntegration = DefaultSettings.enableChronosPointerTimePriorityIntegration;
            settings.showTimePriorityHourDivider = DefaultSettings.showTimePriorityHourDivider;
            settings.keepTimePrioritySourceColumnHighlighted = DefaultSettings.keepTimePrioritySourceColumnHighlighted;
            settings.enableFluffyTimePriorityMirroring = DefaultSettings.enableFluffyTimePriorityMirroring;
            settings.chronosPointerTimePriorityIncidentOverlay = DefaultSettings.chronosPointerTimePriorityIncidentOverlay;
            settings.enableDividerAnimations = DefaultSettings.enableDividerAnimations;
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
