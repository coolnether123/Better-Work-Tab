using System;
using System.Collections.Generic;
using System.Reflection;
using Spine.UI.SettingsFramework;
using Verse;
using static Better_Work_Tab.UI.Settings.SettingIDs;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Better Work Tab-specific filter catalog for the shared Spine settings drawer.
    /// </summary>
    public static class BWTSettingsFilters
    {
        private const string SystemsCategory = "systems";
        private const string VersionsCategory = "versions";
        private const string StatesCategory = "states";
        private const string PresetsCategory = "presets";
        public static IReadOnlyList<SettingsFilterDefinition> Create()
        {
            return new List<SettingsFilterDefinition>
            {
                BuildVersionFilter("version.2.0", "BWT v2.0", IsV20Setting),
                BuildVersionFilter("version.1.1", "BWT v1.1", IsV11Setting),
                BuildVersionFilter("version.1.0.5", "BWT v1.0.5", IsV105Setting),
                BuildVersionFilter("version.1.0", "BWT v1.0", IsV10Setting),
                BuildSystemFilter("system.subwork", "BWT_Settings_Filter_SpecificJobs".Translate(), FeaturesSubWorkJobs),
                BuildSystemFilter("system.headers", "BWT_Settings_Filter_Headers".Translate(), HeadersHeader),
                BuildSystemFilter("system.dividers", "BWT_Settings_Filter_Dividers".Translate(), FeaturesDividers),
                BuildSystemFilter("system.dragdrop", "BWT_Settings_Filter_DragDrop".Translate(), FeaturesDragdrop),
                BuildSystemFilter("system.priorities", "BWT_Settings_Filter_Priorities".Translate(), PriorityHeader),
                BuildSystemFilter("system.timePriority", "BWT_Settings_Filter_HourlyPriorities".Translate(), UiTimePrioritySchedules),
                BuildSystemFilter("system.fluffyStyle", "BWT_Settings_Filter_FluffyStyle".Translate(), CompatFluffyWorkTabHeader),
                BuildSystemFilter("system.workloads", "BWT_Settings_Filter_Workloads".Translate(), FeaturesWorkloads),
                BuildSystemFilter("system.rules", "BWT_Settings_Filter_RuleBuilder".Translate(), FeaturesAutoassign),
                BuildSystemFilter("system.highlights", "BWT_Settings_Filter_Highlights".Translate(), FeaturesHighlights),
                BuildSystemFilter("system.ui", "BWT_Settings_Filter_Display".Translate(), FeaturesUiElements),
                BuildSystemFilter("system.clicks", "BWT_Settings_Filter_Controls".Translate(), FeaturesClicks),
                new SettingsFilterDefinition
                {
                    Id = "worktab.style",
                    Label = "BWT_Settings_Filter_WorkTabStyle".Translate(),
                    Category = PresetsCategory,
                    CategoryLabel = "BWT_Settings_Filter_Presets".Translate(),
                    Tooltip = "BWT_Settings_Filter_WorkTabStyleTooltip".Translate(),
                    Predicate = (def, _) => IsWorkTabStyleSetting(def),
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "vanilla.plus",
                    Label = "BWT_Settings_Filter_VanillaPlus".Translate(),
                    Category = PresetsCategory,
                    CategoryLabel = "BWT_Settings_Filter_Presets".Translate(),
                    Tooltip = "BWT_Settings_Filter_VanillaPlusTooltip".Translate(),
                    Predicate = (def, _) => IsVanillaPlusSetting(def),
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.enabled",
                    Label = "BWT_Settings_Filter_Enabled".Translate(),
                    Category = StatesCategory,
                    CategoryLabel = "BWT_Settings_Filter_States".Translate(),
                    Tooltip = "BWT_Settings_Filter_EnabledTooltip".Translate(),
                    Predicate = (def, settings) => TryReadBool(def, settings, out bool value) && value,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.disabled",
                    Label = "BWT_Settings_Filter_Disabled".Translate(),
                    Category = StatesCategory,
                    CategoryLabel = "BWT_Settings_Filter_States".Translate(),
                    Tooltip = "BWT_Settings_Filter_DisabledTooltip".Translate(),
                    Predicate = (def, settings) => TryReadBool(def, settings, out bool value) && !value,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.changed",
                    Label = "BWT_Settings_Filter_Changed".Translate(),
                    Category = StatesCategory,
                    CategoryLabel = "BWT_Settings_Filter_States".Translate(),
                    Tooltip = "BWT_Settings_Filter_ChangedTooltip".Translate(),
                    Predicate = IsChangedFromDefault,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.notViewed",
                    Label = "BWT_Settings_Filter_NotViewed".Translate(),
                    Category = StatesCategory,
                    CategoryLabel = "BWT_Settings_Filter_States".Translate(),
                    Tooltip = "BWT_Settings_Filter_NotViewedTooltip".Translate(),
                    Predicate = IsNotYetViewed,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "system.animations",
                    Label = "BWT_Settings_Filter_Animations".Translate(),
                    Category = SystemsCategory,
                    CategoryLabel = "BWT_Settings_Filter_Systems".Translate(),
                    Tooltip = "BWT_Settings_Filter_AnimationsTooltip".Translate(),
                    Predicate = (def, _) => ContainsAny(def, "animation", "animate", "cursor"),
                    IncludeChildrenOfMatches = true
                }
            };
        }

        private static SettingsFilterDefinition BuildSystemFilter(string id, string label, string rootId)
        {
            return new SettingsFilterDefinition
            {
                Id = id,
                Label = label,
                Category = SystemsCategory,
                CategoryLabel = "BWT_Settings_Filter_Systems".Translate(),
                Tooltip = "BWT_Settings_Filter_SystemTooltip".Translate(label),
                Predicate = (def, _) => string.Equals(def.Id, rootId, StringComparison.OrdinalIgnoreCase),
                IncludeChildrenOfMatches = true
            };
        }

        private static SettingsFilterDefinition BuildVersionFilter(
            string id,
            string label,
            Func<SettingDefinition, bool> predicate)
        {
            return new SettingsFilterDefinition
            {
                Id = id,
                Label = label,
                Category = VersionsCategory,
                CategoryLabel = "BWT_Settings_Filter_Versions".Translate(),
                Tooltip = "BWT_Settings_Filter_VersionTooltip".Translate(),
                Predicate = (def, _) => predicate(def),
                IncludeChildrenOfMatches = true
            };
        }

        private static bool IsV11Setting(SettingDefinition def)
        {
            return HasAnyPrefix(def,
                    "subWorkJobs.",
                    "ui.timePrioritySchedules",
                    "layout.workTab",
                    "ui.timePriority",
                    "ui.chronosPointer",
                    "advanced.settingFocusHighlightColor",
                    "advanced.scrollWheelPriority",
                    "dividers.animations") ||
                ContainsAny(def, "animation", "animate", "cursor") ||
                string.Equals(def.Id, FeaturesSubWorkJobs, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Id, FeaturesClicks, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsV20Setting(SettingDefinition def)
        {
            return HasAnyId(def,
                    UiGeneralTutorial,
                    UiTimePrioritySchedules,
                    UiTimePriorityCopyPasteButtons,
                    UiTimePriorityHourDivider,
                    UiChronosPointerTimePriority,
                    UiTimePrioritySourceColumnHighlight,
                    UiFluffyTimePriorityMirroring,
                    CompatFluffyWorkTabHeader,
                    FluffyStyleFeatures,
                    FluffyStyleTopButtons,
                    FluffyStyleStandaloneTopButtons,
                    UiMaxPriority,
                    FeaturesSubWorkJobs) ||
                HasAnyPrefix(def,
                    "subWorkJobs.",
                    "ui.timePriority",
                    "ui.chronosPointer",
                    "fluffyStyle.",
                    "ui.maxPriority",
                    "priority.");
        }

        private static bool IsV105Setting(SettingDefinition def)
        {
            // V1.0.5 was the last public 1.0.x release in this repo. There is no v1.0.0 tag,
            // so this tracks settings touched from the practical public baseline V1.0.1
            // through V1.0.5, plus the v1.0.4 -> V1.0.5 tooltip touch on the baseline line.
            return HasAnyId(def,
                    ColumnsShowBaselineLine,
                    "columns.showMovedColorTint",
                    "columns.movedMarkerColor",
                    HeadersHeader,
                    HeadersAngled,
                    DragdropRemoveHeaderUnderline,
                    HeadersAngleRotation,
                    HeadersUseVerticalStackingForCJK,
                    "headers.cjkVerticalKerning",
                    "headers.angledColor",
                    HeadersUnderlineColor,
                    "headers.horizontalOffset");
        }

        /// <summary>
        /// Whether a setting was part of the public 1.0.x surface.
        ///
        /// Internal rather than private because it is not only a search filter:
        /// <see cref="SettingsConsistencyValidator"/> reads it too. Note what
        /// that validator does and does not promise — it checks only that such
        /// a setting stays reachable in *some* view. Keeping one in Simple is a
        /// judgement made at each registration, not something enforced here;
        /// this predicate is a broad prefix match and matches far more settings
        /// than 1.0.5 actually shipped.
        /// </summary>
        internal static bool IsV10Setting(SettingDefinition def)
        {
            return HasAnyPrefix(def,
                    "overlay.",
                    "highlights.",
                    "headers.",
                    "dragdrop.",
                    "columns.",
                    "workloads.",
                    "autoassign.",
                    "ui.maxPriority",
                    "ui.priority",
                    "layout.divider") ||
                string.Equals(def.Id, FeaturesOverlay, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Id, FeaturesHighlights, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Id, FeaturesDragdrop, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Id, FeaturesWorkloads, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWorkTabStyleSetting(SettingDefinition def)
        {
            return HasAnyId(def,
                    FeaturesOverlay,
                    FeaturesDragdrop,
                    FeaturesDividers,
                    FeaturesWorkloads,
                    FeaturesSubWorkJobs,
                    CompatFluffyWorkTabHeader,
                    FluffyStyleFeatures,
                    FluffyStyleTopButtons,
                    FluffyStyleStandaloneTopButtons,
                    HeadersHeader,
                    HeadersAngled,
                    PriorityHeader,
                    LayoutCtrlDrag,
                    LayoutDragRows,
                    LayoutDragColumns,
                    LayoutDragThreshold,
                    LayoutDragColumnLineInset,
                    LayoutResetColumns,
                    LayoutWorkTabMaxVisiblePawns,
                    LayoutWorkTabTopSpace,
                    UiManualPriorities,
                    UiPriorityLegend,
                    AdvancedScrollWheelPriority,
                    WorkloadsWarnOnApply) ||
                HasAnyPrefix(def,
                    "headers.",
                    "priority.",
                    "ui.maxPriority",
                    "ui.priorityColor",
                    "ui.timePriority",
                    "fluffyStyle.",
                    "subWorkJobs.",
                    "dividers.",
                    "layout.divider",
                    "dragdrop.",
                    "layout.drag",
                    "columns.",
                    "overlay.",
                    "colors.",
                    "workloads.",
                    "layout.workTab");
        }

        private static bool IsVanillaPlusSetting(SettingDefinition def)
        {
            return HasAnyId(def,
                    FeaturesUiElements,
                    FeaturesClicks,
                    FeaturesOverlay,
                    FeaturesHighlights,
                    FeaturesDragdrop,
                    FeaturesDividers,
                    HeadersHeader,
                    HeadersAngled,
                    PriorityHeader,
                    PriorityModeSetting,
                    UiMaxPriority,
                    UiManualPriorities,
                    UiPriorityLegend,
                    UiDragInstructions,
                    UiContextSettingsHint,
                    UiPriorityColorPercentageGreen,
                    UiPriorityColorPercentageYellow,
                    UiPriorityColorPercentageTan,
                    "ui.autoEnableManualPriorities",
                    LayoutPawnCount,
                    LayoutBedCount,
                    LayoutContextMenu,
                    LayoutClickClose,
                    LayoutCloseOnMapClick,
                    LayoutCtrlDrag,
                    LayoutDragRows,
                    LayoutDragColumns,
                    LayoutDragThreshold,
                    LayoutDragColumnLineInset,
                    LayoutResetColumns,
                    LayoutWorkTabMaxVisiblePawns,
                    LayoutWorkTabTopSpace,
                    AdvancedScrollWheelPriority) ||
                HasAnyPrefix(def,
                    "headers.",
                    "dragdrop.",
                    "layout.drag",
                    "columns.",
                    "overlay.",
                    "colors.",
                    "highlights.",
                    "dividers.",
                    "layout.divider");
        }

        private static bool HasAnyId(SettingDefinition def, params string[] ids)
        {
            if (def == null || string.IsNullOrEmpty(def.Id))
            {
                return false;
            }

            foreach (string id in ids)
            {
                if (string.Equals(def.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyPrefix(SettingDefinition def, params string[] prefixes)
        {
            if (def == null || string.IsNullOrEmpty(def.Id))
            {
                return false;
            }

            foreach (string prefix in prefixes)
            {
                if (def.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(SettingDefinition def, params string[] needles)
        {
            string text = $"{def?.Id} {def?.Label} {def?.Tooltip}".ToLowerInvariant();
            foreach (string needle in needles)
            {
                if (text.Contains(needle.ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadBool(SettingDefinition def, object settingsObject, out bool value)
        {
            value = false;
            if (def == null || settingsObject == null || string.IsNullOrEmpty(def.FieldName))
            {
                return false;
            }

            FieldInfo field = settingsObject.GetType().GetField(
                def.FieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                return false;
            }

            value = (bool)field.GetValue(settingsObject);
            return true;
        }

        private static bool IsChangedFromDefault(SettingDefinition def, object settingsObject)
        {
            if (def == null || settingsObject == null || string.IsNullOrEmpty(def.FieldName) || def.DefaultValue == null)
            {
                return def?.Type == SettingType.Custom &&
                       (def.CustomHasNonDefaultValue?.Invoke(settingsObject) ?? false);
            }

            FieldInfo field = settingsObject.GetType().GetField(
                def.FieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return false;
            }

            object current = field.GetValue(settingsObject);
            return !Equals(current, def.DefaultValue);
        }

        private static bool IsNotYetViewed(SettingDefinition def, object settingsObject)
        {
            if (def == null || string.IsNullOrEmpty(def.Id))
            {
                return false;
            }

            if (def.Type == SettingType.Header || def.Type == SettingType.Spacer)
            {
                return false;
            }

            return !(settingsObject is BetterWorkTabSettings settings) ||
                !settings.HasViewedSetting(def.Id);
        }
    }
}
