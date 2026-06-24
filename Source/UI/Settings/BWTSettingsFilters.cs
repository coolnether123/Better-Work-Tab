using System;
using System.Collections.Generic;
using System.Reflection;
using Spine.UI.SettingsFramework;
using static Better_Work_Tab.UI.Settings.SettingIDs;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Better Work Tab-specific filter catalog for the shared Spine settings drawer.
    /// </summary>
    public static class BWTSettingsFilters
    {
        public static IReadOnlyList<SettingsFilterDefinition> Create()
        {
            return new List<SettingsFilterDefinition>
            {
                BuildSystemFilter("system.subwork", "System: Sub-work Jobs", FeaturesSubWorkJobs),
                BuildSystemFilter("system.dividers", "System: Dividers", FeaturesDividers),
                BuildSystemFilter("system.dragdrop", "System: Drag & Drop", FeaturesDragdrop),
                BuildSystemFilter("system.priorities", "System: Priorities", PriorityHeader),
                BuildSystemFilter("system.workloads", "System: Workloads", FeaturesWorkloads),
                BuildSystemFilter("system.rules", "System: Rulesets", FeaturesAutoassign),
                BuildSystemFilter("system.highlights", "System: Highlights", FeaturesHighlights),
                BuildSystemFilter("system.ui", "System: UI Display", FeaturesUiElements),
                BuildSystemFilter("system.clicks", "System: Clicks & Shortcuts", FeaturesClicks),
                BuildVersionFilter("version.1.1", "BWT Version: v1.1", IsV11Setting),
                BuildVersionFilter("version.1.0.5", "BWT Version: v1.0.5", IsV105Setting),
                BuildVersionFilter("version.1.0", "BWT Version: v1.0", IsV10Setting),
                new SettingsFilterDefinition
                {
                    Id = "fluffy.like",
                    Label = "Fluffy-like Settings",
                    Tooltip = "Settings that map to common Work Tab/Fluffy-style behavior: compact headers, priorities, dividers, dragging, overlays, and time planning.",
                    Predicate = (def, _) => HasAnyPrefix(def,
                        "headers.",
                        "priority.",
                        "ui.maxPriority",
                        "ui.auto",
                        "subWorkJobs.",
                        "dividers.",
                        "dragdrop.",
                        "columns.",
                        "overlay.",
                        "ui.timePriority",
                        "ui.chronosPointer",
                        "layout.workTab",
                        "advanced.scrollWheelPriority"),
                    IncludeChildrenOfMatches = true
                },
                new SettingsFilterDefinition
                {
                    Id = "state.enabled",
                    Label = "Enabled Settings",
                    Tooltip = "Only boolean settings that are currently enabled.",
                    Predicate = (def, settings) => TryReadBool(def, settings, out bool value) && value,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.disabled",
                    Label = "Disabled Settings",
                    Tooltip = "Only boolean settings that are currently disabled.",
                    Predicate = (def, settings) => TryReadBool(def, settings, out bool value) && !value,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "state.changed",
                    Label = "Changed From Default",
                    Tooltip = "Only settings whose current value differs from the registered default.",
                    Predicate = IsChangedFromDefault,
                    IncludeChildrenOfMatches = false
                },
                new SettingsFilterDefinition
                {
                    Id = "system.animations",
                    Label = "Animations",
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
                Tooltip = $"Show settings under {label.Replace("System: ", string.Empty)}.",
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
                Tooltip = "Show settings added or materially changed in this BWT version.",
                Predicate = (def, _) => predicate(def),
                IncludeChildrenOfMatches = true
            };
        }

        private static bool IsV11Setting(SettingDefinition def)
        {
            return HasAnyPrefix(def,
                    "subWorkJobs.",
                    "ui.timePriorityPlannerPrototype",
                    "layout.workTab",
                    "ui.timePriority",
                    "ui.chronosPointer",
                    "advanced.scrollWheelPriority",
                    "dividers.animations") ||
                string.Equals(def.Id, FeaturesSubWorkJobs, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Id, FeaturesClicks, StringComparison.OrdinalIgnoreCase);
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
                return false;
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
    }
}
