using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.UI.SettingsFramework;
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
        private static readonly HashSet<string> ControlSettingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ControlsPageHeader,
            ControlsFluffyHeader,
            FeaturesClicks,
            ControlsPriorityCells,
            ControlsShiftOverlay,
            ControlsHeaderActions,
            ControlsHistory,
            ControlsFluffyExpand,
            ControlsFluffyBatch,
            ControlsFluffyPawnRows,
            AdvancedScrollWheelPriority,
            SubWorkOpenModifier,
            SubWorkOpenButton,
            LayoutCtrlDrag,
            DragdropEnableGrouping,
            LayoutContextMenu,
            LayoutClickClose,
            LayoutCloseOnMapClick
        };

        private static readonly HashSet<string> ControlAncestorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FeaturesSubWorkJobs,
            FeaturesDragdrop,
            FeaturesUiElements,
            ModCompatHeader,
            CompatFluffyWorkTabHeader
        };

        public static bool IsControlSetting(SettingDefinition definition)
        {
            return definition != null && ControlSettingIds.Contains(definition.Id);
        }

        public static bool IsControlsPageDefinition(SettingDefinition definition)
        {
            return definition != null &&
                   (ControlSettingIds.Contains(definition.Id) || ControlAncestorIds.Contains(definition.Id));
        }

        public static IReadOnlyList<SettingsFilterDefinition> Create()
        {
            return new List<SettingsFilterDefinition>
            {
                BuildVersionFilter("version.2.0", "BWT v2.0", IsV20Setting),
                BuildVersionFilter("version.1.1", "BWT v1.1", IsV11Setting),
                BuildVersionFilter("version.1.0.5", "BWT v1.0.5", IsV105Setting),
                BuildVersionFilter("version.1.0", "BWT v1.0", IsV10Setting),
                BuildSystemFilter("system.subwork", "Sub-work Jobs", FeaturesSubWorkJobs),
                BuildSystemFilter("system.headers", "Headers", HeadersHeader),
                BuildSystemFilter("system.dividers", "Dividers", FeaturesDividers),
                BuildSystemFilter("system.dragdrop", "Drag & Drop", FeaturesDragdrop),
                BuildSystemFilter("system.priorities", "Priorities", PriorityHeader),
                BuildSystemFilter("system.timePriority", "Time Priority Schedule", UiTimePrioritySchedules),
                BuildSystemFilter("system.fluffyStyle", "Fluffy-style Work Tab", CompatFluffyWorkTabHeader),
                BuildSystemFilter("system.workloads", "Workloads", FeaturesWorkloads),
                BuildSystemFilter("system.rules", "Rulesets", FeaturesAutoassign),
                BuildSystemFilter("system.highlights", "Highlights", FeaturesHighlights),
                BuildSystemFilter("system.ui", "UI Display", FeaturesUiElements),
                BuildSystemFilter("system.clicks", "Clicks & Shortcuts", FeaturesClicks),
                new SettingsFilterDefinition
                {
                    Id = "worktab.style",
                    Label = "Work Tab-style",
                    Category = PresetsCategory,
                    CategoryLabel = "Presets",
                    Tooltip = "Settings that map to common expanded Work tab behavior: compact headers, priorities, sub-work priorities, dividers, dragging, skill overlays, workload presets, and time planning.",
                    Predicate = (def, _) => IsWorkTabStyleSetting(def),
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "vanilla.plus",
                    Label = "Vanilla+",
                    Category = PresetsCategory,
                    CategoryLabel = "Presets",
                    Tooltip = "Low-disruption settings that keep the Work tab close to vanilla while adding polish: headers, drag/reorder, highlights, overlays, counts, priority display, and layout spacing.",
                    Predicate = (def, _) => IsVanillaPlusSetting(def),
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.enabled",
                    Label = "Enabled Settings",
                    Category = StatesCategory,
                    CategoryLabel = "States",
                    Tooltip = "Only boolean settings that are currently enabled.",
                    Predicate = (def, settings) => TryReadBool(def, settings, out bool value) && value,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.disabled",
                    Label = "Disabled Settings",
                    Category = StatesCategory,
                    CategoryLabel = "States",
                    Tooltip = "Only boolean settings that are currently disabled.",
                    Predicate = (def, settings) => TryReadBool(def, settings, out bool value) && !value,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.changed",
                    Label = "Changed From Default",
                    Category = StatesCategory,
                    CategoryLabel = "States",
                    Tooltip = "Only settings whose current value differs from the registered default.",
                    Predicate = IsChangedFromDefault,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.notViewed",
                    Label = "Not Yet Viewed",
                    Category = StatesCategory,
                    CategoryLabel = "States",
                    Tooltip = "Settings whose tooltip has not been opened yet.",
                    Predicate = IsNotYetViewed,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "system.animations",
                    Label = "Animations",
                    Category = SystemsCategory,
                    CategoryLabel = "Systems",
                    Tooltip = "Animation and cursor movement settings.",
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
                CategoryLabel = "Systems",
                Tooltip = $"Show settings under {label}.",
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
                CategoryLabel = "Versions",
                Tooltip = "Show settings added or materially changed in this BWT version.",
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
                    UiAutoMaxPriority,
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

        private static bool IsV10Setting(SettingDefinition def)
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
                    LayoutWorkTabMaxHeight,
                    LayoutWorkTabTopSpace,
                    UiManualPriorities,
                    UiPriorityLegend,
                    AdvancedScrollWheelPriority,
                    WorkloadsWarnOnApply,
                    WorkloadsPersistDividers) ||
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
                    LayoutWorkTabMaxHeight,
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
