using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.ComplexJobs;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Multiplayer.API;
using RimWorld;
using Spine.UI.ColourPicker;
using Better_Work_Tab.UI.SettingsFramework;
using UnityEngine;
using Verse;
using static Better_Work_Tab.UI.Settings.SettingIDs;

namespace Better_Work_Tab.UI.Settings
{
    // HOW TO ADD A SETTING
    // 1. Add the canonical default to DefaultSettings.
    // 2. Add the BetterWorkTabSettings instance field initialized from DefaultSettings.
    // 3. Add one SettingDefinition here: Id from SettingIDs, FieldName, DefaultValue =
    //    DefaultSettings.x, widget Type, parent/sort metadata, search keywords, and translation keys
    //    in Languages/English/Keyed/BWT_Settings.xml.
    // 4. Nothing else is needed for normal preferences. Registry scribing, reset, and the
    //    dev-mode validator pick it up automatically and warn about drift.

    /// <summary>
    /// Registers Better Work Tab settings and builds the hierarchy used by the UI.
    /// </summary>
    public static class BWTSettingsRegistry
    {
        private static readonly string[] SpecificJobSearchKeywords =
        {
            "individual jobs", "detailed jobs", "sub-jobs", "work givers",
            "break down work type", "expand work column", "drill down",
            "separate cooking jobs", "separate crafting jobs"
        };

        private static readonly string[] PriorityRangeSearchKeywords =
        {
            "more priorities", "priorities above 4", "extended priorities",
            "priority 5", "priority 9", "max priority", "priority range", "priority colors"
        };

        private static readonly string[] HourlyPrioritySearchKeywords =
        {
            "hourly work", "day shift", "night shift", "schedule by time",
            "different priority at night", "per-hour priorities", "timetable"
        };

        private static readonly string[] SkillDisplaySearchKeywords =
        {
            "show skills in work tab", "skill levels", "best worker", "best colonist",
            "most skilled pawn", "shift overlay", "aptitude"
        };

        private static readonly string[] WorkTabLayoutSearchKeywords =
        {
            "tab size", "window size", "resize", "too wide", "too tall", "compact",
            "shrink", "more pawn rows", "scrolling", "spacing above headers"
        };

        private static readonly string[] WorkHeaderSearchKeywords =
        {
            "column labels", "work labels", "rotated text", "header angle",
            "overlapping text", "cramped headers", "header alignment", "vertical labels"
        };

        private static readonly string[] ReorderingSearchKeywords =
        {
            "sort pawns", "organize colonists", "pawn order", "work column order",
            "move row", "move column", "rearrange jobs"
        };

        private static List<SettingDefinition> _settings;
        private static SettingsHierarchy _hierarchy;
        private static bool _initialized;

        static BWTSettingsRegistry()
        {
            BWTModSettingsApi.ContributorsChanged += Invalidate;
        }

        /// <summary>
        /// Gets the built hierarchy for all settings.
        /// </summary>
        public static SettingsHierarchy Hierarchy
        {
            get
            {
                EnsureInitialized();
                return _hierarchy;
            }
        }

        /// <summary>
        /// Gets the raw definitions list.
        /// </summary>
        public static IReadOnlyList<SettingDefinition> Definitions
        {
            get
            {
                EnsureInitialized();
                return _settings;
            }
        }

        /// <summary>
        /// Ensures registration happens exactly once.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            ModSupportManager.EnsureInitialized();
            ComplexJobsCompatibility.RegisterSettings();
            FluffyWorkTabGateway.RegisterSettings();
            ChronosPointerSupport.RegisterSettings();
            RegisterAllSettings();
            _hierarchy = new SettingsHierarchy(_settings);
            _initialized = true;
            SettingsConsistencyValidator.ValidateAtStartup();
        }

        public static void Invalidate()
        {
            if (!_initialized)
            {
                return;
            }

            _initialized = false;
            _settings = null;
            _hierarchy = null;
            BetterWorkTabSettingsUI.NotifySettingsChanged();
        }

        private static void Register(SettingDefinition def)
        {
            ApplyScribeMetadata(def);
            Action<object> existingOnChanged = def?.OnChanged;
            if (def != null)
            {
                def.OnChanged = settingsObject =>
                {
                    existingOnChanged?.Invoke(settingsObject);
                    WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                        WorkTabDirtyFlags.SettingsThemeLanguageScale);
                };
            }
            _settings.Add(def);
        }

        private static void ApplyScribeMetadata(SettingDefinition def)
        {
            if (def == null)
            {
                return;
            }

            switch (def.FieldName)
            {
                case nameof(BetterWorkTabSettings.showExternalWorkTabColumns):
                    def.ScribeKey = "showFluffyWorkTabColumns";
                    break;
                case nameof(BetterWorkTabSettings.enableColumnGrouping):
                    def.ScribeDefaultOverride = false; // BWT 1.0.5 absent-key default.
                    break;
                case nameof(BetterWorkTabSettings.enableScrollWheelPriority):
                    def.ScribeDefaultOverride = false; // BWT 1.0.5 absent-key default.
                    break;
                case nameof(BetterWorkTabSettings.priorityMode):
                    def.DisableAutoScribe = true; // Legacy absent-key default is inferred from older priority fields.
                    break;
                case nameof(BetterWorkTabSettings.enableExtendedPriorities):
                case nameof(BetterWorkTabSettings.delegateToExternalPriorityMods):
                case nameof(BetterWorkTabSettings.selectedPriorityProviderId):
                    def.DisableAutoScribe = true; // Manually scribed before priorityMode for legacy inference.
                    break;
            }
        }

        private static void RegisterHiddenPreference(
            string id,
            string fieldName,
            SettingType type,
            object defaultValue,
            Type enumType = null)
        {
            Register(new SettingDefinition
            {
                Id = id,
                FieldName = fieldName,
                Label = fieldName,
                Tooltip = "Hidden compatibility preference.",
                Type = type,
                EnumType = enumType,
                DefaultValue = defaultValue,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = int.MaxValue
            });
        }

        /// <summary>
        /// True when specific jobs open as Fluffy Work Tab's right-expanding columns rather than
        /// Better Work Tab's focused view. The focused view's layout settings are inert in that mode.
        /// </summary>
        private static bool UsesExpandBesideDrilldown()
        {
            BetterWorkTabSettings.SubWorkDrilldownStyle style =
                BetterWorkTabMod.Settings?.subWorkDrilldownStyle ?? DefaultSettings.subWorkDrilldownStyle;
            return style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside;
        }

        private static void RegisterModCompatibilitySettings()
        {
            IReadOnlyList<IModSettingsContributor> contributors = BWTModSettingsApi.GetContributors();
            if (contributors == null || contributors.Count == 0)
            {
                return;
            }

            var sections = new List<BWTModSettingsSection>();
            foreach (IModSettingsContributor contributor in contributors)
            {
                if (contributor == null)
                {
                    continue;
                }

                BWTModSettingsSection section = contributor.CreateSettingsSection();
                if (section?.Header == null || string.IsNullOrEmpty(section.Header.Id))
                {
                    continue;
                }

                sections.Add(section);
            }

            if (sections.Count == 0)
            {
                return;
            }

            sections = sections
                .OrderBy(section => section.Header.Label ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(section => section.Header.Id, StringComparer.Ordinal)
                .ToList();

            Register(new SettingDefinition
            {
                Id = ModCompatHeader,
                Label = "Mod Compatibility",
                Tooltip = "Settings for loaded mod integrations.",
                Type = SettingType.Header,
                HeaderColor = new Color(0.7f, 0.75f, 0.9f),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = -40,
                VisibleWhen = settingsObject => HasVisibleModCompatibilitySection(sections, settingsObject)
            });

            foreach (BWTModSettingsSection section in sections)
            {
                section.Header.ParentId = ModCompatHeader;
                Register(section.Header);

                if (section.Children == null)
                {
                    continue;
                }

                foreach (SettingDefinition child in section.Children)
                {
                    if (child == null || string.IsNullOrEmpty(child.Id))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(child.ParentId))
                    {
                        child.ParentId = section.Header.Id;
                    }

                    Register(child);
                }
            }
        }

        private static bool HasVisibleModCompatibilitySection(
            List<BWTModSettingsSection> sections,
            object settingsObject)
        {
            foreach (BWTModSettingsSection section in sections)
            {
                SettingDefinition header = section?.Header;
                if (header == null)
                {
                    continue;
                }

                if (header.VisibleWhen == null || header.VisibleWhen(settingsObject))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DrawSubWorkTransitionMode(
            Rect rect,
            string label,
            string tooltip,
            object settingsObject,
            bool disabled)
        {
            if (!(settingsObject is BetterWorkTabSettings settings))
            {
                return false;
            }

            Rect labelRect = rect.LeftPart(0.5f);
            Rect buttonRect = rect.RightPart(0.48f);
            Widgets.Label(labelRect, label);

            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            if (Widgets.ButtonText(buttonRect, GetSubWorkTransitionModeLabel(settings)))
            {
                var offOption = new FloatMenuOption("Off (instant)", () =>
                    {
                        settings.enableSubWorkTransitionAnimation = false;
                        HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                        settings.Write();
                    });
                var classicOption = new FloatMenuOption(GetSubWorkTransitionStyleLabel(BetterWorkTabSettings.SubWorkTransitionStyle.ClassicGlideFlash), () =>
                    {
                        settings.enableSubWorkTransitionAnimation = true;
                        settings.subWorkTransitionStyle = BetterWorkTabSettings.SubWorkTransitionStyle.ClassicGlideFlash;
                        HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                        settings.Write();
                    });
                var pixelOption = new FloatMenuOption(GetSubWorkTransitionStyleLabel(BetterWorkTabSettings.SubWorkTransitionStyle.PixelWaveFlip), () =>
                    {
                        settings.enableSubWorkTransitionAnimation = true;
                        settings.subWorkTransitionStyle = BetterWorkTabSettings.SubWorkTransitionStyle.PixelWaveFlip;
                        HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                        settings.Write();
                    });
                var options = new List<FloatMenuOption>
                {
                    offOption,
                    classicOption,
                    pixelOption
                };

                FloatMenuOption selectedOption = !settings.enableSubWorkTransitionAnimation
                    ? offOption
                    : settings.subWorkTransitionStyle == BetterWorkTabSettings.SubWorkTransitionStyle.PixelWaveFlip
                        ? pixelOption
                        : classicOption;
                var optionDescriptions = new Dictionary<FloatMenuOption, string>
                {
                    [offOption] = "Turns the specific-job transition animation off. Columns change immediately when entering or leaving the specific-job view.",
                    [classicOption] = "Uses BWT's original transition: columns glide into position with a brief flash while entering or leaving the specific-job view.",
                    [pixelOption] = "Keeps the columns in place while a grey pixel wave passes across them, progressively revealing or hiding the specific-job view."
                };
                Find.WindowStack.Add(new DescribedFloatMenu(options, selectedOption, label, tooltip, optionDescriptions));
            }

            GUI.enabled = previousEnabled;
            GUI.color = previousColor;

            if (!string.IsNullOrEmpty(tooltip) && !DescribedFloatMenu.AnyOpen)
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
            }

            return false;
        }

        private static string GetSubWorkTransitionModeLabel(BetterWorkTabSettings settings)
        {
            if (settings == null || !settings.enableSubWorkTransitionAnimation)
            {
                return "Off (instant)";
            }

            return GetSubWorkTransitionStyleLabel(settings.subWorkTransitionStyle);
        }

        private static string GetSubWorkTransitionStyleLabel(BetterWorkTabSettings.SubWorkTransitionStyle style)
        {
            string key = $"BWT_Enum_SubWorkTransitionStyle_{style}";
            return key.CanTranslate() ? key.Translate() : style.ToString();
        }

        private static bool IsSubWorkTransitionModeNonDefault(object settingsObject)
        {
            return settingsObject is BetterWorkTabSettings settings &&
                (settings.enableSubWorkTransitionAnimation != DefaultSettings.enableSubWorkTransitionAnimation ||
                 settings.subWorkTransitionStyle != DefaultSettings.subWorkTransitionStyle);
        }

        private static void ResetSubWorkTransitionMode(object settingsObject)
        {
            if (!(settingsObject is BetterWorkTabSettings settings))
            {
                return;
            }

            settings.enableSubWorkTransitionAnimation = DefaultSettings.enableSubWorkTransitionAnimation;
            settings.subWorkTransitionStyle = DefaultSettings.subWorkTransitionStyle;
        }

        /// <summary>
        /// Adds all setting definitions with hierarchy relationships.
        /// </summary>
        private static void RegisterAllSettings()
        {
            _settings = new List<SettingDefinition>();

            RegisterHiddenPreference("compat.workTabMaxHeight", nameof(BetterWorkTabSettings.workTabMaxHeight), SettingType.Float, DefaultSettings.workTabMaxHeight);
            RegisterHiddenPreference("compat.settingsViewMode", nameof(BetterWorkTabSettings.settingsViewMode), SettingType.Enum, DefaultSettings.settingsViewMode, typeof(BetterWorkTabSettings.SettingsViewMode));
            RegisterHiddenPreference("compat.enableColumnOrderSaving", nameof(BetterWorkTabSettings.enableColumnOrderSaving), SettingType.Bool, DefaultSettings.enableColumnOrderSaving);
            RegisterHiddenPreference("compat.enableRowColumnHighlights", nameof(BetterWorkTabSettings.enableRowColumnHighlights), SettingType.Bool, DefaultSettings.enableRowColumnHighlights);
            RegisterHiddenPreference("compat.enableUIElements", nameof(BetterWorkTabSettings.enableUIElements), SettingType.Bool, DefaultSettings.enableUIElements);
            RegisterHiddenPreference("compat.hideWorkloadButton", nameof(BetterWorkTabSettings.hideWorkloadButton), SettingType.Bool, DefaultSettings.hideWorkloadButton);
            RegisterHiddenPreference("compat.persistColumnOrder", nameof(BetterWorkTabSettings.persistColumnOrder), SettingType.Bool, DefaultSettings.persistColumnOrder);
            RegisterHiddenPreference("compat.persistColumnWidths", nameof(BetterWorkTabSettings.persistColumnWidths), SettingType.Bool, DefaultSettings.persistColumnWidths);
            RegisterHiddenPreference("compat.showOnlyLineDragIndicatorRows", nameof(BetterWorkTabSettings.showOnlyLineDragIndicatorRows), SettingType.Bool, DefaultSettings.showOnlyLineDragIndicatorRows);
            RegisterHiddenPreference("compat.showOnlyLineDragIndicatorColumns", nameof(BetterWorkTabSettings.showOnlyLineDragIndicatorColumns), SettingType.Bool, DefaultSettings.showOnlyLineDragIndicatorColumns);
            RegisterHiddenPreference("compat.showGhostDragIndicator", nameof(BetterWorkTabSettings.showGhostDragIndicator), SettingType.Bool, DefaultSettings.showGhostDragIndicator);
            RegisterHiddenPreference("compat.showInsertionLineIndicator", nameof(BetterWorkTabSettings.showInsertionLineIndicator), SettingType.Bool, DefaultSettings.showInsertionLineIndicator);
            RegisterHiddenPreference("compat.useColumnHoverOverride", nameof(BetterWorkTabSettings.useColumnHoverOverride), SettingType.Bool, DefaultSettings.useColumnHoverOverride);
            RegisterHiddenPreference("compat.UseCustomMouseHoverHighlight", nameof(BetterWorkTabSettings.UseCustomMouseHoverHighlight), SettingType.Bool, DefaultSettings.UseCustomMouseHoverHighlight);
            RegisterHiddenPreference("compat.useRowHoverOverride", nameof(BetterWorkTabSettings.useRowHoverOverride), SettingType.Bool, DefaultSettings.useRowHoverOverride);
            RegisterHiddenPreference("compat.Color_CustomMouseHighlight", nameof(BetterWorkTabSettings.Color_CustomMouseHighlight), SettingType.Color, DefaultSettings.Color_CustomMouseHighlight);
            RegisterHiddenPreference("compat.Color_IncapableBecauseOfCapacities", nameof(BetterWorkTabSettings.Color_IncapableBecauseOfCapacities), SettingType.Color, DefaultSettings.Color_IncapableBecauseOfCapacities);
            RegisterHiddenPreference("compat.Color_HeaderText", nameof(BetterWorkTabSettings.Color_HeaderText), SettingType.Color, DefaultSettings.Color_HeaderText);
            RegisterHiddenPreference("compat.Color_DividerText", nameof(BetterWorkTabSettings.Color_DividerText), SettingType.Color, DefaultSettings.Color_DividerText);
            RegisterHiddenPreference("compat.Color_Borders", nameof(BetterWorkTabSettings.Color_Borders), SettingType.Color, DefaultSettings.Color_Borders);
            RegisterHiddenPreference("compat.enableWorkloadSaving", nameof(BetterWorkTabSettings.enableWorkloadSaving), SettingType.Bool, DefaultSettings.enableWorkloadSaving);
            RegisterHiddenPreference("compat.enableWorkloadLoading", nameof(BetterWorkTabSettings.enableWorkloadLoading), SettingType.Bool, DefaultSettings.enableWorkloadLoading);
            RegisterHiddenPreference("compat.showWorkloadButtonFooter", nameof(BetterWorkTabSettings.showWorkloadButtonFooter), SettingType.Bool, DefaultSettings.showWorkloadButtonFooter);
            RegisterHiddenPreference("compat.enableExtendedPriorities", nameof(BetterWorkTabSettings.enableExtendedPriorities), SettingType.Bool, DefaultSettings.enableExtendedPriorities);
            RegisterHiddenPreference("compat.delegateToExternalPriorityMods", nameof(BetterWorkTabSettings.delegateToExternalPriorityMods), SettingType.Bool, DefaultSettings.delegateToExternalPriorityMods);
            RegisterHiddenPreference("compat.selectedPriorityProviderId", nameof(BetterWorkTabSettings.selectedPriorityProviderId), SettingType.Custom, DefaultSettings.selectedPriorityProviderId);

            Register(new SettingDefinition
            {
                Id = FeaturesOverlay,
                FieldName = "enableSkillOverlayFeature",
                Label = "Skill display",
                Tooltip = "Show skill levels and best-pawn indicators in the Work tab. The options below control when each indicator appears.",
                SearchKeywords = SkillDisplaySearchKeywords,
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSkillOverlayFeature,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -49,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.9f, 0.7f, 0.4f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesDragdrop,
                FieldName = "enableDragDropReordering",
                Label = "Reorder by dragging",
                Tooltip = "Drag pawn rows, Work columns, rules, and a rule's conditions into a new order. Turning this off keeps the current order, and puts arrow buttons on rule conditions instead.",
                SearchKeywords = ReorderingSearchKeywords,
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableDragDropReordering,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -48,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.5f, 0.8f, 0.5f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesHighlights,
                FieldName = "ShowPawnAndWorktypeHighlights",
                Label = "Row and column highlights",
                Tooltip = "Highlight the pawn row and Work column related to the current selection or cursor position.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowPawnAndWorktypeHighlights,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -47,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.4f, 0.6f, 0.9f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesDividers,
                FieldName = "enableDividers",
                Label = "Dividers",
                Tooltip = "Add named divider rows to organize pawns into collapsible groups.",
                SearchKeywords = new[] { "pawn groups", "organize colonists", "section", "separator", "collapse rows" },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableDividers,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -46,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.8f, 0.8f, 0.6f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesAutoassign,
                FieldName = "enableAutoAssignFeature",
                Label = "Automatic work assignments (rulesets)",
                Tooltip = "Create reusable rules that assign work priorities from pawn skills, passions, capabilities, and other conditions.",
                SearchKeywords = new[]
                {
                    "automatically assign work", "best pawn", "passions", "skills",
                    "new colonist", "priority rules", "work manager"
                },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableAutoAssignFeature,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -45,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.6f, 0.6f, 0.6f)
            });

            Register(new SettingDefinition
            {
                Id = AutoassignWarnOnApply,
                ParentId = FeaturesAutoassign,
                FieldName = "warnOnApplyRuleset",
                Label = "Warn before applying Ruleset",
                Tooltip = "Show a confirmation warning before applying a ruleset to all colonists.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.warnOnApplyRuleset,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = FeaturesWorkloads,
                FieldName = "enableWorkloads",
                Label = "Saved work-priority layouts (workloads)",
                Tooltip = "Save the colony's current pawn work priorities as a named layout and restore it later.",
                SearchKeywords = new[]
                {
                    "save priorities", "load priorities", "preset", "profile",
                    "snapshot", "backup assignments", "work layout"
                },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableWorkloads,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -44,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.6f, 0.6f, 0.6f)
            });

            Register(new SettingDefinition
            {
                Id = WorkloadsWarnOnApply,
                ParentId = FeaturesWorkloads,
                FieldName = "warnOnApplyWorkload",
                Label = "Warn before applying Workload",
                Tooltip = "Show a confirmation warning before applying a workload to all colonists.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.warnOnApplyWorkload,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = FeaturesSubWorkJobs,
                FieldName = "enableSubWorkDrilldown",
                Label = "Sub-work jobs",
                Tooltip = "Open a Work column to set priorities for its individual jobs. Use the shortcut below on a Work header or cell; use it again, or press Escape, to return.",
                SearchKeywords = SpecificJobSearchKeywords,
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSubWorkDrilldown,
                ControlsChildVisibility = true,
                OnChanged = _ => WorkGiverReassignmentManager.OnRuntimeSettingChanged(),
                ShowInSimpleView = true,
                SortOrder = -43,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.55f, 0.75f, 0.9f)
            });

            Register(new SettingDefinition
            {
                Id = SubWorkOpenModifier,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkDrilldownModifier",
                Label = "Shortcut modifier",
                Tooltip = "Modifier key used with the mouse button below to open or leave the specific-job view.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SubWorkDrilldownModifier),
                DefaultValue = DefaultSettings.subWorkDrilldownModifier,
                ShowInSimpleView = false,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = SubWorkOpenButton,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkDrilldownButton",
                Label = "Shortcut mouse button",
                Tooltip = "Mouse button used with the modifier key above to open or leave the specific-job view.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SubWorkDrilldownButton),
                DefaultValue = DefaultSettings.subWorkDrilldownButton,
                ShowInSimpleView = false,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = SubWorkCrossWorkDragDrop,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "enableSubWorkCrossWorkDragDrop",
                Label = "Move sub-work jobs between Work columns",
                Tooltip = "Drag a specific job onto another Work column to move it there. Turning this off limits dragging to the current specific-job view.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSubWorkCrossWorkDragDrop,
                ShowInSimpleView = false,
                SortOrder = 5
            });

            Register(new SettingDefinition
            {
                Id = SubWorkCompactPriorityBoxes,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "useCompactSubWorkPriorityBoxes",
                Label = "Compact boxes in expanded view",
                Tooltip = "Use smaller priority boxes when specific jobs expand beside their parent Work column. Focused full-tab view always uses normal-size boxes.",
                SearchKeywords = new[] { "small cells", "narrow columns", "Fluffy layout", "compact priorities" },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.useCompactSubWorkPriorityBoxes,
                ShowInSimpleView = false,
                SortOrder = 6
            });

            Register(new SettingDefinition
            {
                Id = SubWorkGlobalVanillaPriorityBoxes,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "useVanillaSubWorkGlobalPriorityBoxes",
                Label = "Full-size boxes in the shared-priority row",
                Tooltip = "Draw the priority boxes in the top shared-priority row at normal Work-cell size instead of using compact boxes.",
                SearchKeywords = new[] { "top row", "global priority", "shared priority", "box size" },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes,
                ShowInSimpleView = false,
                SortOrder = 7
            });

            Register(new SettingDefinition
            {
                Id = SubWorkRestoreCursor,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "restoreCursorOnSubWorkExit",
                Label = "Restore cursor from headers",
                Tooltip = "After leaving from a specific-job header, move the cursor back to the Work header used to open it.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.restoreCursorOnSubWorkExit,
                ShowInSimpleView = false,
                SortOrder = 7
            });

            Register(new SettingDefinition
            {
                Id = SubWorkRestoreCursorFromPawnCells,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "restoreCursorOnSubWorkPawnCellExit",
                Label = "Restore cursor from pawn cells",
                Tooltip = "After leaving from a pawn priority cell, move the cursor back to the Work header used to open the specific-job view. Turning this off leaves the cursor where you clicked.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.restoreCursorOnSubWorkPawnCellExit,
                ShowInSimpleView = false,
                SortOrder = 8
            });

            Register(new SettingDefinition
            {
                Id = SubWorkOverrideBreakAnimation,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "enableSubWorkOverrideBreakAnimation",
                Label = "Override reset animation",
                Tooltip = "Show a short break-and-fade effect when returning a pawn's specific-job priority to the shared priority.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSubWorkOverrideBreakAnimation,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 9
            });

            Register(new SettingDefinition
            {
                Id = SubWorkTransitionAnimation,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "enableSubWorkTransitionAnimation",
                Label = "Sub-work transition animation",
                Tooltip = "Animate specific-job views, including Fluffy-style expand-beside columns and header text fade when they open or collapse.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSubWorkTransitionAnimation,
                OnChanged = _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 10
            });

            Register(new SettingDefinition
            {
                Id = SubWorkTransitionMode,
                ParentId = FeaturesSubWorkJobs,
                Label = "Animation style",
                Tooltip = "Choose how the Work tab opens specific jobs. Off changes instantly with no transition.",
                Type = SettingType.Custom,
                CustomDrawer = DrawSubWorkTransitionMode,
                CustomHasNonDefaultValue = IsSubWorkTransitionModeNonDefault,
                CustomReset = ResetSubWorkTransitionMode,
                OnChanged = _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 10
            });

            Register(new SettingDefinition
            {
                Id = SubWorkTransitionStyle,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkTransitionStyle",
                Label = "Transition style",
                Tooltip = "Classic glide is the original sub-work transition. Pixel wave reveal keeps columns in place and fades them as the grey wave passes.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SubWorkTransitionStyle),
                DefaultValue = DefaultSettings.subWorkTransitionStyle,
                OnChanged = _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 11
            });

            Register(new SettingDefinition
            {
                Id = SubWorkTransitionSpeed,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkTransitionSeconds",
                Label = "Animation speed",
                Tooltip = "Controls how quickly the Work tab opens, expands, collapses, or leaves specific jobs.",
                Type = SettingType.Float,
                MinValue = 0.2f,
                MaxValue = 0.9f,
                MinLabel = "Fast",
                MaxLabel = "Slow",
                ValueFormat = "{0:0.00}s",
                DefaultValue = DefaultSettings.subWorkTransitionSeconds,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.subWorkTransitionSeconds =
                            BetterWorkTabSettings.ClampSubWorkTransitionSeconds(settings.subWorkTransitionSeconds);
                    }

                    HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                },
                VisibleWhen = s => (s as BetterWorkTabSettings)?.enableSubWorkTransitionAnimation ?? true,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 11
            });

            Register(new SettingDefinition
            {
                Id = SubWorkDisabledParentMode,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkDisabledParentMode",
                Label = "When the parent Work type is off",
                Tooltip = "Choose whether locked specific-job priorities can still run when their parent Work type is disabled. Multiplayer always uses vanilla parent-disable behavior.",
                SearchKeywords = new[] { "locked job", "disabled work", "job still run", "parent work off", "override parent" },
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SubWorkDisabledParentMode),
                DefaultValue = DefaultSettings.subWorkDisabledParentMode,
                OnChanged = _ => WorkExecutionOrder.MarkAllPawnsWorkGiversDirty(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 12
            });

            Register(new SettingDefinition
            {
                Id = SubWorkAutoExpandColumns,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkAutoExpandColumns",
                Label = "Expand sub-work columns",
                Tooltip = "Use empty table width for specific-job columns so long labels fit without changing the pawn-name column.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.subWorkAutoExpandColumns,
                ControlsChildVisibility = true,
                // Width relief only runs for the focused specific-job view, and only when angled
                // headers are not already keeping labels from colliding.
                Suppressions = new List<SettingSuppression>
                {
                    new SettingSuppression
                    {
                        When = settingsObj => !((BetterWorkTabSettings)settingsObj).keepVanillaWorkTabMinimumWidth,
                        Reason = _ => "Compact window width keeps focused columns at their natural widths.",
                        SuppressorSettingId = LayoutWorkTabMinimumWidth,
                        LinkLabel = "Keep vanilla minimum width"
                    },
                    new SettingSuppression
                    {
                        When = _ => FluffyWorkTabGateway.CanHostFluffySubWorkColumns && UsesExpandBesideDrilldown(),
                        Reason = _ => "Expand beside uses BWT's dedicated child columns.",
                        SuppressorSettingId = SubWorkDrilldownStyle,
                        LinkLabel = "Sub-work view"
                    },
                    new SettingSuppression
                    {
                        When = _ => BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders,
                        Reason = _ => "Angled headers already keep labels from colliding.",
                        SuppressorSettingId = HeadersAngled,
                        LinkLabel = "Angled headers"
                    }
                },
                OnChanged = _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 12
            });

            Register(new SettingDefinition
            {
                Id = SubWorkEvenlyExpandColumns,
                ParentId = SubWorkAutoExpandColumns,
                FieldName = "subWorkEvenlyExpandColumns",
                Label = "Use equal expanded widths",
                Tooltip = "Give expanded specific-job columns equal widths. Turning this off widens only columns whose labels need more room.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.subWorkEvenlyExpandColumns,
                OnChanged = _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = FeaturesUiElements,
                Label = "Work Tab",
                Type = SettingType.Header,
                Tooltip = "General Work tab size, spacing, controls, and display options.",
                HeaderColor = new Color(0.8f, 0.8f, 0.6f),
                ShowInSimpleView = true,
                SortOrder = -42
            });

            Register(new SettingDefinition
            {
                Id = FeaturesClicks,
                Label = "Mouse & shortcuts",
                Type = SettingType.Header,
                Tooltip = "Mouse and shortcut behavior for the Work tab.",
                HeaderColor = new Color(0.7f, 0.75f, 0.9f),
                ShowInSimpleView = true,
                SortOrder = -41
            });

            RegisterModCompatibilitySettings();

            Register(new SettingDefinition
            {
                Id = PriorityHeader,
                ParentId = FeaturesUiElements,
                Label = "Priority range",
                Tooltip = "Controls which mod owns the manual priority range.",
                SearchKeywords = PriorityRangeSearchKeywords,
                Type = SettingType.Header,
                HeaderColor = new Color(0.8f, 0.7f, 0.45f),
                ShowInSimpleView = true,
                SortOrder = 42,
            });

            Register(new SettingDefinition
            {
                Id = PriorityModeSetting,
                ParentId = PriorityHeader,
                FieldName = "priorityMode",
                Label = "Priority mode",
                Tooltip = "Auto keeps RimWorld's normal 1-4 range unless another compatible mod or existing higher priorities require more. Better Work Tab lets BWT manage the expanded range.",
                SearchKeywords = PriorityRangeSearchKeywords,
                Type = SettingType.Enum,
                EnumType = typeof(PriorityMode),
                DefaultValue = DefaultSettings.priorityMode,
                ShowInSimpleView = true,
                SortOrder = 0,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }

                    PriorityAuthorityBroker.InvalidateCaches();
                    Patch_WorkPriority_DoCell_Unified.ClearColorCache();
                }
            });

            Register(new SettingDefinition
            {
                Id = UiAutoMaxPriority,
                ParentId = PriorityHeader,
                FieldName = "autoMaxPriorityInt",
                Label = "Auto max priority",
                Tooltip = "Legacy compatibility value. Auto mode now uses the BWT max priority setting as its ceiling.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.autoMaxPriority,
                MinValue = PriorityConstants.VanillaMax,
                MaxValue = BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT,
                VisibleWhen = s => false,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 1,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }

                    PriorityAuthorityBroker.InvalidateCaches();
                }
            });

            Register(new SettingDefinition
            {
                Id = UiMaxPriority,
                ParentId = PriorityHeader,
                FieldName = "maxPriorityInt",
                Label = "Maximum priority",
                Tooltip = "Highest manual priority available when Better Work Tab manages the priority range.",
                SearchKeywords = PriorityRangeSearchKeywords,
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.maxPriority,
                MinValue = BetterWorkTabSettings.MAX_PRIORITY_MINIMUM,
                MaxValue = BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT,
                VisibleWhen = s =>
                {
                    var settings = (BetterWorkTabSettings)s;
                    return settings.priorityMode == PriorityMode.BetterWorkTab ||
                           settings.priorityMode == PriorityMode.Auto;
                },
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 2,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }

                    PriorityAuthorityBroker.InvalidateCaches();
                    Patch_WorkPriority_DoCell_Unified.ClearColorCache();
                }
            });

            Register(new SettingDefinition
            {
                Id = UiAutoDisabledPriorityMode,
                ParentId = PriorityHeader,
                FieldName = "autoDisabledPriorityMode",
                Label = "Priority when re-enabling work",
                Tooltip = "Choose the priority assigned when you click disabled work back on in Auto priority mode.",
                SearchKeywords = new[] { "turn work back on", "disabled cell", "re-enable", "click priority" },
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.AutoDisabledPriorityMode),
                DefaultValue = DefaultSettings.autoDisabledPriorityMode,
                VisibleWhen = s => ((BetterWorkTabSettings)s).priorityMode == PriorityMode.Auto,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 3,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = UiTimePrioritySchedules,
                ParentId = PriorityHeader,
                FieldName = "enableTimePrioritySchedules",
                Label = "Priorities by hour",
                Tooltip = "Ctrl-click a work-priority cell to set different priorities by time of day.",
                SearchKeywords = HourlyPrioritySearchKeywords,
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableTimePrioritySchedules,
                ControlsChildVisibility = true,
                OnChanged = _ =>
                {
                    TimePriorityService.OnRuntimeSettingChanged();
                    if (!TimePriorityService.IsRuntimeEnabled)
                    {
                        TimePriorityScheduleEditor.ResetForWindowClose();
                    }
                },
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 8
            });

            Register(new SettingDefinition
            {
                Id = UiTimePriorityHourDivider,
                ParentId = UiTimePrioritySchedules,
                FieldName = "showTimePriorityHourDivider",
                Label = "Show time-number divider",
                Tooltip = "Draw a thin divider line above the hour numbers in the Work tab time-priority editor.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showTimePriorityHourDivider,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = UiTimePriorityCopyPasteButtons,
                ParentId = UiTimePrioritySchedules,
                FieldName = "showTimePriorityCopyPasteButtons",
                Label = "Show schedule copy/paste buttons",
                Tooltip = "Show copy and paste controls for Work tab time-priority schedules. These controls use the same copy/paste column as vanilla while a schedule row is open.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showTimePriorityCopyPasteButtons,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = UiTimePrioritySourceColumnHighlight,
                ParentId = UiTimePrioritySchedules,
                FieldName = "keepTimePrioritySourceColumnHighlighted",
                Label = "Keep source column highlighted",
                Tooltip = "While a time-priority schedule is open, keep the work column it edits highlighted and prevent the schedule strip from highlighting columns behind it.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.keepTimePrioritySourceColumnHighlighted,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = UiFluffyTimePriorityMirroring,
                ParentId = UiTimePrioritySchedules,
                FieldName = "enableFluffyTimePriorityMirroring",
                Label = "Mirror schedules to Fluffy",
                Tooltip = "When Fluffy Work Tab is loaded, push BWT time-priority schedules into Fluffy's own per-hour priority tracker.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableFluffyTimePriorityMirroring,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                VisibleWhen = _ => FluffyWorkTabGateway.IsPresent,
                SortOrder = 4
            });

            Register(new SettingDefinition
            {
                Id = UiAutoDisabledPriorityFixedValue,
                ParentId = PriorityHeader,
                FieldName = "autoDisabledPriorityFixedValue",
                Label = "Fixed re-enabled priority",
                Tooltip = "Priority number used when the setting above is Fixed Priority.",
                SearchKeywords = new[] { "turn work back on", "disabled cell", "re-enable", "click priority" },
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.autoDisabledPriorityFixedValue,
                MinValue = 1,
                MaxValue = BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT,
                VisibleWhen = s =>
                {
                    var settings = (BetterWorkTabSettings)s;
                    return settings.priorityMode == PriorityMode.Auto &&
                           settings.autoDisabledPriorityMode == BetterWorkTabSettings.AutoDisabledPriorityMode.FixedPriority;
                },
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 4,
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityColorPercentageGreen,
                ParentId = FeaturesUiElements,
                FieldName = "priorityColorPercentage_Green",
                Label = "Green priority threshold",
                Tooltip = "For extended priorities, color the highest-priority number range green. Lower numbers are acted on first; this percentage is measured from priority 1 toward the maximum.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.priorityColorPercentage_Green,
                MinValue = 1,
                MaxValue = 100,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 43,
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityColorPercentageYellow,
                ParentId = FeaturesUiElements,
                FieldName = "priorityColorPercentage_Yellow",
                Label = "Yellow priority threshold",
                Tooltip = "For extended priorities, color priority numbers yellow through this cumulative percentage of the range. Lower numbers are acted on first.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.priorityColorPercentage_Yellow,
                MinValue = 1,
                MaxValue = 100,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 44,
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityColorPercentageTan,
                ParentId = FeaturesUiElements,
                FieldName = "priorityColorPercentage_Tan",
                Label = "Tan priority threshold",
                Tooltip = "For extended priorities, color priority numbers tan through this cumulative percentage of the range. Lower numbers are acted on first; later numbers are gray.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.priorityColorPercentage_Tan,
                MinValue = 1,
                MaxValue = 100,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 45,
            });

            Register(new SettingDefinition
            {
                Id = FeaturesPerformance,
                FieldName = "enablePerformanceOptimizations",
                Label = "Performance",
                Tooltip = "Disabling may reduce performance on large colonies",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enablePerformanceOptimizations,
                ControlsChildVisibility = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                VisibleWhen = _ => false,
                SortOrder = -41,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.6f, 0.8f, 0.8f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesMultiplayer,
                FieldName = "enableMultiplayerSync",
                Label = "Multiplayer Sync",
                Tooltip = "Enable multiplayer synchronization options.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableMultiplayerSync,
                ControlsChildVisibility = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
                SortOrder = -40
            });

            // Highlights
            Register(new SettingDefinition
            {
                Id = HighlightsHover,
                ParentId = FeaturesHighlights,
                FieldName = "ShowCursorPawnAndWorktypeHighlight",
                Label = "Highlight on Hover",
                Tooltip = "Tint the row and column under your cursor.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowCursorPawnAndWorktypeHighlight,
                ShowInSimpleView = true,
                ControlsChildVisibility = true,
                SortOrder = 0
            });

            Register(new SettingDefinition
            {
                Id = "highlights.masterColor",
                ParentId = HighlightsHover,
                Label = "Set Master Highlight Color",
                Tooltip = "Select a color to apply to ALL highlight settings (Hover, Selected, etc).",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 0, 
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings s)
                    {
                         Find.WindowStack.Add(new Dialog_ColourPicker(s.Color_CursorHighlight, (picked, closing) =>
                         {
                             s.Color_CursorHighlight = picked;
                             s.Color_RowHoverHighlight = picked;
                             s.Color_ColumnHoverHighlight = picked;
                             s.Color_SelectedPawnHighlight = picked;
                             s.Color_FloatMenuHighlight = picked;
                             s.Color_CustomMouseHighlight = picked;
                             s.Color_CustomSimilarWorktypeHighlight = picked;
                             s.Write();
                             Messages.Message("Master color applied to all highlight settings.", MessageTypeDefOf.PositiveEvent, false);
                         }));
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = HighlightsHoverColor,
                ParentId = HighlightsHover,
                FieldName = "Color_CursorHighlight",
                Label = "Cell hover: row and column color",
                Tooltip = "Tints both the pawn row and Work column crossing under the cursor.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_CursorHighlight,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsRowHoverColor,
                ParentId = HighlightsHover,
                FieldName = "Color_RowHoverHighlight",
                Label = "Cell hover: pawn-row color",
                Tooltip = "Tints the horizontal pawn row crossing under the cursor.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_RowHoverHighlight,
                ShowInSimpleView = false,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = HighlightsColumnHoverColor,
                ParentId = HighlightsHover,
                FieldName = "Color_ColumnHoverHighlight",
                Label = "Cell hover: Work-column color",
                Tooltip = "Tints the vertical Work column crossing under the cursor.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_ColumnHoverHighlight,
                ShowInSimpleView = false,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = HighlightsResetRowHoverColor,
                ParentId = HighlightsHover,
                Label = "Reset Row Hover Color",
                Tooltip = "Reset row hover highlight to the general hover color.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 4,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.useRowHoverOverride = false;
                        settings.Color_RowHoverHighlight = settings.Color_MouseHoverHighlight;
                        settings.Write();
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = HighlightsResetColumnHoverColor,
                ParentId = HighlightsHover,
                Label = "Reset Column Hover Color",
                Tooltip = "Reset column hover highlight to the general hover color.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 5,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.useColumnHoverOverride = false;
                        settings.Color_ColumnHoverHighlight = settings.Color_MouseHoverHighlight;
                        settings.Write();
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSelected,
                ParentId = FeaturesHighlights,
                FieldName = "DoSelectedPawnHighlight",
                Label = "Highlight Selected Pawn",
                Tooltip = "Always highlight the currently selected pawn's row.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.DoSelectedPawnHighlight,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSelectedColor,
                ParentId = HighlightsSelected,
                FieldName = "Color_SelectedPawnHighlight",
                Label = "Selected pawn-row color",
                Tooltip = "Tints the full Work-tab row for a pawn selected on the map or colonist bar.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_SelectedPawnHighlight,
                ShowInSimpleView = false,
                SortOrder = 2_1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSelectedOpacity,
                ParentId = HighlightsSelected,
                FieldName = "SelectedPawnHighlightOpacity",
                Label = "Selected Pawn Highlight Opacity",
                Tooltip = "Opacity for selected pawn highlight.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.SelectedPawnHighlightOpacity,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = false,
                SortOrder = 2_2
            });

            Register(new SettingDefinition
            {
                Id = HighlightsFloatMenu,
                ParentId = FeaturesHighlights,
                FieldName = "ShowFloatMenuPawnAndWorktypeHighlight",
                Label = "Highlight context-menu target",
                Tooltip = "When a context menu opens the Work tab, highlight the related pawn and Work column.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = HighlightsFloatMenuColor,
                ParentId = HighlightsFloatMenu,
                FieldName = "Color_FloatMenuHighlight",
                Label = "Context target: row and column color",
                Tooltip = "Tints the pawn row and Work column opened by Go to Work or Manage Work options, including those beside Do Once. The Do Once action itself stays on the map and has no Work-tab highlight.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_FloatMenuHighlight,
                ShowInSimpleView = false,
                SortOrder = 3_1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsOutlineMode,
                ParentId = HighlightsHover,
                FieldName = "useOutlineHighlights",
                Label = "Use Outline Highlights",
                Tooltip = "Draw highlights as outlines instead of solid boxes.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.useOutlineHighlights,
                ShowInSimpleView = true,
                SortOrder = 4 // Placed after colors in HighlightHover
            });

            Register(new SettingDefinition
            {
                Id = HighlightsDisableBestPawn,
                ParentId = FeaturesOverlay,
                FieldName = "disableBestPawnHighlight",
                Label = "Hide best-pawn indicator",
                Tooltip = "Hide the green marker that identifies the highest-skilled eligible pawn for each Work type.",
                SearchKeywords = new[] { "best worker", "most skilled", "green box", "green outline" },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.disableBestPawnHighlight,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 41
            });

            Register(new SettingDefinition
            {
                Id = HighlightsBestPawnBackground,
                ParentId = FeaturesOverlay,
                FieldName = "bestPawnHighlightThickness",
                Label = "Best-pawn outline thickness",
                Tooltip = "Adjust the thickness of the green outline for the best pawn in a work type.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.bestPawnHighlightThickness,
                MinValue = 1f,
                MaxValue = 4f,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 42,
                VisibleWhen = s => !((BetterWorkTabSettings)s).disableBestPawnHighlight
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSimilar,
                ParentId = HighlightsHover,
                FieldName = "ShowSimilarWorktypeHighlight",
                Label = "Related Work columns",
                Tooltip = "Dimly highlight other Work columns that use the same skills.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowSimilarWorktypeHighlight,
                ShowInSimpleView = false,
                SortOrder = 5
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSimilarColor,
                ParentId = HighlightsSimilar,
                FieldName = "Color_CustomSimilarWorktypeHighlight",
                Label = "Related Work-column color",
                Tooltip = "Tints other Work columns that use skills related to the column under the cursor.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_CustomSimilarWorktypeHighlight,
                ShowInSimpleView = false,
                SortOrder = 5_1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSimilarOpacity,
                ParentId = HighlightsSimilar,
                FieldName = "SimilarWorktypeHighlightOpacity",
                Label = "Related Work opacity",
                Tooltip = "Opacity used to highlight related Work columns.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.SimilarWorktypeHighlightOpacity,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = false,
                SortOrder = 5_2
            });

            // Layout
            Register(new SettingDefinition
            {
                Id = LayoutCtrlDrag,
                FieldName = "requireCtrlForDrag",
                Label = "Require Ctrl for Dragging",
                Tooltip = "Hold Ctrl to drag rows/columns. Prevents accidental reordering.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.requireCtrlForDrag,
                ShowInSimpleView = false,
                SortOrder = 95,
                ParentId = FeaturesDragdrop
            });

            Register(new SettingDefinition
            {
                Id = DragdropEnableGrouping,
                ParentId = FeaturesDragdrop,
                FieldName = "enableColumnGrouping",
                Label = "Group columns with Shift-click",
                Tooltip = "Shift-click Work headers to select and drag multiple columns together.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableColumnGrouping,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 96
            });


            Register(new SettingDefinition
            {
                Id = LayoutDragRows,
                ParentId = FeaturesDragdrop,
                FieldName = "rowDraggingEnabled",
                Label = "Drag pawn rows",
                Tooltip = "Drag pawn rows to change their order.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.rowDraggingEnabled,
                ShowInSimpleView = false,
                SortOrder = 1011
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragColumns,
                ParentId = FeaturesDragdrop,
                FieldName = "columnDraggingEnabled",
                Label = "Drag Work columns",
                Tooltip = "Drag Work columns to change their order.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.columnDraggingEnabled,
                ShowInSimpleView = false,
                SortOrder = 1012
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragThreshold,
                ParentId = FeaturesDragdrop,
                FieldName = "dragThreshold",
                Label = "Drag Threshold (px)",
                Tooltip = "Minimum mouse movement before a drag begins.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.dragThreshold,
                MinValue = 1f,
                MaxValue = 20f,
                ShowInSimpleView = false,
                SortOrder = 1013
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragColumnLineInset,
                ParentId = FeaturesDragdrop,
                FieldName = "columnInsertionLineInset",
                Label = "Column insertion-line height",
                Tooltip = "How far the insertion line extends upward from the bottom of the Work header while dragging a column.",
                SearchKeywords = new[] { "drag line", "drop position", "insertion marker" },
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.columnInsertionLineInset,
                MinValue = 0f,
                MaxValue = 64f,
                ShowInSimpleView = false,
                SortOrder = 1014
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragHoverDelay,
                ParentId = FeaturesAutoassign,
                FieldName = "dragHoverDelay",
                Label = "Rule Builder: Drag Hover Delay (s)",
                Tooltip = "Time in seconds to hover before switching categories while dragging rules.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dragHoverDelay,
                MinValue = 0.2f,
                MaxValue = 2.0f,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 430
            });

            Register(new SettingDefinition
            {
                Id = LayoutClickClose,
                ParentId = AdvancedHeader,
                FieldName = "disableLeftClickClose",
                Label = "Keep Work tab open after selecting a pawn",
                Tooltip = "Selecting a pawn keeps the Work tab open. While enabled, clicks outside the tab also leave it open.",
                SearchKeywords = new[] { "stay open", "don't close", "selecting colonist", "click outside", "map click" },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.disableLeftClickClose,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 405
            });

            Register(new SettingDefinition
            {
                Id = LayoutCloseOnMapClick,
                FieldName = "closeOnMapClick",
                Label = "Close on map click",
                Tooltip = "Close the Work tab when clicking on the map.",
                SearchKeywords = new[] { "stay open", "don't close", "click outside", "map click" },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.closeOnMapClick,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 1021,
                ParentId = FeaturesClicks,
                Suppressions = new List<SettingSuppression>
                {
                    new SettingSuppression
                    {
                        When = settingsObj => ((BetterWorkTabSettings)settingsObj).disableLeftClickClose,
                        Reason = _ => "Keeping the Work tab open overrides this option.",
                        SuppressorSettingId = LayoutClickClose,
                        LinkLabel = "Keep Work tab open"
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = LayoutContextMenu,
                FieldName = "enableContextMenuOnRightClick",
                Label = "Right-click context menu",
                Tooltip = "Enable context menu on right-clicking pawn names.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableContextMenuOnRightClick,
                ShowInSimpleView = true,
                SortOrder = 1022,
                ParentId = FeaturesClicks
            });

            Register(new SettingDefinition
            {
                Id = LayoutWorkTabMinimumWidth,
                ParentId = FeaturesUiElements,
                FieldName = nameof(BetterWorkTabSettings.keepVanillaWorkTabMinimumWidth),
                Label = "Keep vanilla minimum width",
                Tooltip = "Keep the Work tab at least as wide as RimWorld's normal Work tab to reduce distracting motion. Wider content may still expand the tab to the right. Turn this off to let compact layouts shrink the window.",
                SearchKeywords = WorkTabLayoutSearchKeywords,
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.keepVanillaWorkTabMinimumWidth,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 90,
                OnChanged = _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged()
            });

            Register(new SettingDefinition
            {
                Id = LayoutPawnCount,
                FieldName = "showPawnCountAtBottom",
                Label = "Show Colonist Count",
                Tooltip = "Display colonist count in the bottom-left corner.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showPawnCountAtBottom,
                ShowInSimpleView = false,
                SortOrder = 103,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = LayoutBedCount,
                FieldName = "showBedCountAtBottom",
                Label = "Show Bed Count",
                Tooltip = "Display bed count (red if insufficient).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showBedCountAtBottom,
                ShowInSimpleView = false,
                SortOrder = 104,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityLegend,
                FieldName = "showPriorityLegend",
                Label = "Show Priority Legend",
                Tooltip = "Display priority direction text above work columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showPriorityLegend,
                ShowInSimpleView = false,
                SortOrder = 1041,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiDragInstructions,
                FieldName = "showDragInstructions",
                Label = "Show Footer Control Hints",
                Tooltip = "Always show the Shift skill-view hint and show other control hints only when hovering a target that supports them.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showDragInstructions,
                ShowInSimpleView = true,
                SortOrder = 1042,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiContextSettingsHint,
                FieldName = "showContextSettingsHint",
                Label = "Show Alt-Click Settings Hint",
                Tooltip = "Show the top-right hint that Alt-clicking the Work tab opens related settings. This turns off automatically after the first successful Alt-click.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showContextSettingsHint,
                ShowInSimpleView = false,
                SortOrder = 1043,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiGeneralTutorial,
                FieldName = "showGeneralTutorial",
                Label = "Show Better Work Tab Tutorial",
                Tooltip = "Show or resume the interactive Better Work Tab tutorial. Turning this off pauses the tutorial without clearing completed lessons or the current lesson.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showGeneralTutorial,
                ShowInSimpleView = true,
                SortOrder = 10434,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiManualPriorities,
                FieldName = "showManualPrioritiesCheckbox",
                Label = "Show Manual Priorities Checkbox",
                Tooltip = "Show the Manual Priorities checkbox in the header. Disabling hides the checkbox and prevents switching between checkmarks and manual priority numbers.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showManualPrioritiesCheckbox,
                ShowInSimpleView = true,
                SortOrder = 1044,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = "ui.autoEnableManualPriorities",
                FieldName = "autoEnableManualPriorities",
                Label = "Turn on Manual priorities automatically",
                Tooltip = "Automatically check the Manual Priorities checkbox when opening the Work tab.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.autoEnableManualPriorities,
                ShowInSimpleView = false,
                SortOrder = 1045,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = LayoutDividerHeight,
                FieldName = "dividerHeight",
                Label = "Divider Default Height",
                Tooltip = "Default height of divider rows in pixels.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dividerHeight,
                MinValue = 10f,
                MaxValue = 50f,
                MinLabel = "Thin",
                MaxLabel = "Thick",
                ShowInSimpleView = true,
                SortOrder = 105,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = LayoutDividerAlpha,
                FieldName = "dividerMinAlpha",
                Label = "Divider Minimum Opacity",
                Tooltip = "Minimum background opacity for dividers.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dividerMinAlpha,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = false,
                SortOrder = 106,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = DividersShow,
                FieldName = "showDividers",
                Label = "Show Dividers",
                Tooltip = "Toggle visibility of divider rows.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showDividers,
                ShowInSimpleView = true,
                SortOrder = 107,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = DividersCustomColors,
                FieldName = "allowCustomDividerColors",
                Label = "Custom divider colors",
                Tooltip = "Choose a different color for each divider.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.allowCustomDividerColors,
                ShowInSimpleView = false,
                SortOrder = 108,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = "dividers.highlight",
                ParentId = FeaturesDividers,
                FieldName = "highlightDividersOnHover",
                Label = "Highlight on Hover",
                Tooltip = "Highlight dividers when hovering over them using the hover highlight color.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.highlightDividersOnHover,
                ShowInSimpleView = false,
                SortOrder = 109
            });

            Register(new SettingDefinition
            {
                Id = DividersLabels,
                FieldName = "showDividerLabels",
                Label = "Show Divider Labels",
                Tooltip = "Show divider labels by default.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showDividerLabels,
                ShowInSimpleView = false,
                SortOrder = 111,
                ParentId = FeaturesDividers
            });


            Register(new SettingDefinition
            {
                Id = DividersCollapse,
                FieldName = "allowDividerCollapse",
                Label = "Collapsible dividers",
                Tooltip = "Let dividers collapse or expand their pawn groups.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.allowDividerCollapse,
                ShowInSimpleView = false,
                SortOrder = 112,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = DividersAnimations,
                FieldName = "enableDividerAnimations",
                Label = "Animate Divider Changes",
                Tooltip = "Smoothly grows and collapses divider sections and newly inserted dividers. Disable this if another mod causes table resize flicker.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableDividerAnimations,
                OnChanged = _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 113,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = "dividers.resetHeight",
                Label = "Reset all divider heights",
                Tooltip = "Restore every divider to the default height.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 114,
                ParentId = FeaturesDividers,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings && Current.Game?.GetComponent<GameComponent_BWTWorldSettings>() is GameComponent_BWTWorldSettings worldSettings)
                    {
                        if (worldSettings.ActiveDividers != null)
                        {
                            foreach (var div in worldSettings.ActiveDividers)
                            {
                                div.Height = settings.dividerHeight;
                            }
                            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                            Messages.Message("Dividers reset to default height.", MessageTypeDefOf.PositiveEvent, false);
                        }
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = LayoutResetColumns,
                Label = "Reset Columns to Vanilla",
                Tooltip = "Restore all work columns to their default order.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 1015,
                ParentId = FeaturesDragdrop,
                OnChanged = _ => WorkColumnOrderManager.ResetToVanilla()
            });

            Register(new SettingDefinition
            {
                Id = ColumnsShowMovedIndicator,
                ParentId = FeaturesDragdrop,
                FieldName = "showColumnMovedMarker",
                Label = "Show Moved Indicator",
                Tooltip = "Show indicator on manually moved columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showColumnMovedMarker,
                ShowInSimpleView = true,
                SortOrder = 115
            });

            Register(new SettingDefinition
            {
                Id = ColumnsShowBaselineLine,
                ParentId = FeaturesDragdrop,
                FieldName = "showColumnBaselineLine",
                Label = "Show Baseline Line While Dragging",
                Tooltip = "Show a line at the column's vanilla position while dragging moved columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showColumnBaselineLine,
                ShowInSimpleView = true,
                SortOrder = 116
            });

            Register(new SettingDefinition
            {
                Id = "columns.showMovedColorTint",
                ParentId = FeaturesDragdrop,
                FieldName = "showMovedColumnColorTint",
                Label = "Color tint for reordered columns",
                Tooltip = "Apply a color tint to the text of manually reordered columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showMovedColumnColorTint,
                ShowInSimpleView = true,
                SortOrder = 117
            });

            Register(new SettingDefinition
            {
                Id = "columns.movedMarkerColor",
                ParentId = FeaturesDragdrop,
                FieldName = "movedMarkerColor",
                Label = "Moved-column marker color",
                Tooltip = "Colors the star and header tint that identify a Work column moved from its default position.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_MovedMarkerColor,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 118
            });

            Register(new SettingDefinition
            {
                Id = ColumnsResetWidths,
                ParentId = FeaturesDragdrop,
                Label = "Reset Column Widths",
                Tooltip = "Clear saved column widths.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 1016,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings s)
                    {
                        s.storedColumnWidths.Clear();
                        s.Write();
                    }
                }
            });

            // Skill colors
            Register(new SettingDefinition
            {
                Id = OverlayHeader,
                Label = "Skill Colors",
                Type = SettingType.Header,
                Tooltip = "Skill overlay behaviors when using Shift and hover.",
                HeaderColor = new Color(0.8f, 0.8f, 0.6f),
                ShowInSimpleView = false,
                SortOrder = 104,
                ParentId = FeaturesOverlay
            });

            var settings = BetterWorkTabMod.Settings;
            if (settings != null)
            {
                // Dynamic settings generation: The DropdownListAdder lets users add hidden work types,
                // and for each hidden type, we create a removable button tag below.
                Register(new SettingDefinition
                {
                    Id = HideWorktypes,
                    ParentId = FeaturesUiElements,
                    FieldName = "hiddenWorktypes",
                    Label = "Hidden Work Types",
                    Tooltip = "Select work types to hide from the work tab. (Beta Testing Phase. Please reach out to discord with ideas for improving)",
                    SearchKeywords = new[] { "hide column", "remove work column", "unwanted job", "show work type", "unhide" },
                    Type = SettingType.DropdownListAdder,
                    DropdownOptionsProvider = () => DefDatabase<WorkTypeDef>.AllDefsListForReading
                        .Where(wt => !settings.hiddenWorktypes.Contains(wt.defName))
                        .Select(w => w.labelShort.CapitalizeFirst())
                        .OrderBy(l => l),
                    OnOptionAdded = (option) =>
                    {
                        var wt = DefDatabase<WorkTypeDef>.AllDefsListForReading.FirstOrDefault(w => w.labelShort.CapitalizeFirst() == option);
                        if (wt != null && !settings.hiddenWorktypes.Contains(wt.defName))
                        {
                            settings.hiddenWorktypes.Add(wt.defName);
                            settings.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                        }
                    },
                    ShowInSimpleView = false,
                    ShowInAdvancedView = true,
                    SortOrder = 105
                });

                foreach (var hiddenDefName in settings.hiddenWorktypes)
                {
                    var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(hiddenDefName);
                    if (wt == null) continue;

                    string localHiddenDefName = hiddenDefName;
                    Register(new SettingDefinition
                    {
                        Id = "hide.wt." + hiddenDefName,
                        ParentId = HideWorktypes,
                        Label = "  - " + wt.labelShort.CapitalizeFirst(),
                        LabelKey = string.Empty,
                        Tooltip = "Click to unhide this work type.",
                        TooltipKey = "BWT_Settings_hide.workType_Tooltip",
                        Type = SettingType.Button,
                        OnChanged = (s) =>
                        {
                            var settingsObj = (BetterWorkTabSettings)s;
                            settingsObj.hiddenWorktypes.Remove(localHiddenDefName);
                            settingsObj.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                        },
                        ShowInSimpleView = false,
                        ShowInAdvancedView = true,
                        SortOrder = 106
                    });
                }
            }

            Register(new SettingDefinition
            {
                Id = OverlayNumbersMode,
                ParentId = FeaturesOverlay,
                FieldName = "ShowUIMode_ShowSmallSkillNumbers",
                Label = "Tiny Skill Numbers",
                Tooltip = "When to show the small skill numbers in cells.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.ShowUIMode),
                DefaultValue = DefaultSettings.ShowUIMode_ShowSmallSkillNumbers,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 0
            });

            Register(new SettingDefinition
            {
                Id = OverlayBestPawnMode,
                ParentId = FeaturesOverlay,
                FieldName = "ShowUIMode_ShowPawnForSkillSquare",
                Label = "Best-pawn indicator",
                Tooltip = "When to highlight the pawn with highest skill.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.ShowUIMode),
                DefaultValue = DefaultSettings.ShowUIMode_ShowPawnForSkillSquare,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = OverlayHoverCellOverlay,
                ParentId = FeaturesOverlay,
                FieldName = "showHoverCellOverlay",
                Label = "Change cell display on hover",
                Tooltip = "When enabled, hovering can emphasize either skill or priority in one cell or the whole column, according to the options below.",
                SearchKeywords = new[] { "mouse over", "skill on hover", "priority on hover", "big skill number", "column hover" },
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showHoverCellOverlay,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = OverlayHoverMode,
                ParentId = OverlayHoverCellOverlay,
                FieldName = "skillViewHoverMode",
                Label = "Hover Behavior (Priority/Skill)",
                Tooltip = "Choose hover visuals: Skill focused (big skill number + small priority) or Priority focused (vanilla box + tiny skill).",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SkillViewHoverMode),
                DefaultValue = DefaultSettings.skillViewHoverMode,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = OverlayHoverScope,
                ParentId = OverlayHoverCellOverlay,
                FieldName = "hoverEffectScope",
                Label = "Hover Effect Scope",
                Tooltip = "Controls whether hover overlays are shown only on the hovered cell or across the whole column.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.HoverEffectScope),
                DefaultValue = DefaultSettings.hoverEffectScope,
                ShowInSimpleView = false,
                SortOrder = 4
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillVeryLow,
                ParentId = OverlayHeader,
                FieldName = "Color_VeryLowSkill",
                Label = "Shift-overlay skill number: levels 0-3",
                Tooltip = "Text color of skill-level numbers from 0 through 3 shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                SearchKeywords = new[] { "skill text", "shift numbers", "aptitude", "skill level color" },
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_VeryLowSkill,
                ShowInSimpleView = false,
                SortOrder = 301,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillLow,
                ParentId = OverlayHeader,
                FieldName = "Color_LowSkill",
                Label = "Shift-overlay skill number: levels 4-9",
                Tooltip = "Text color of skill-level numbers from 4 through 9 shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                SearchKeywords = new[] { "skill text", "shift numbers", "aptitude", "skill level color" },
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_LowSkill,
                ShowInSimpleView = false,
                SortOrder = 302,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillGood,
                ParentId = OverlayHeader,
                FieldName = "Color_GoodLowSkill",
                Label = "Shift-overlay skill number: levels 10-15",
                Tooltip = "Text color of skill-level numbers from 10 through 15 shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                SearchKeywords = new[] { "skill text", "shift numbers", "aptitude", "skill level color" },
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_GoodLowSkill,
                ShowInSimpleView = false,
                SortOrder = 303,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillExcellent,
                ParentId = OverlayHeader,
                FieldName = "Color_ExcellentSkill",
                Label = "Shift-overlay skill number: levels 16+",
                Tooltip = "Text color of skill-level numbers at 16 or higher shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                SearchKeywords = new[] { "skill text", "shift numbers", "aptitude", "skill level color" },
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_ExcellentSkill,
                ShowInSimpleView = false,
                SortOrder = 304,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsBestPawnOutline,
                ParentId = OverlayHeader,
                FieldName = "Color_BestPawnForSkillSquare",
                Label = "Best-pawn cell indicator color",
                Tooltip = "Colors the outline or background around the highest-skilled eligible pawn's Work cell.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_BestPawnForSkillSquare,
                ShowInSimpleView = false,
                SortOrder = 305
            });

            // Advanced
            Register(new SettingDefinition
            {
                Id = AdvancedHeader,
                Label = "Maintenance",
                Type = SettingType.Header,
                Tooltip = "Advanced toggles and maintenance/reset options.",
                HeaderColor = new Color(0.6f, 0.6f, 0.6f),
                ShowInSimpleView = false,
                SortOrder = 400
            });

            Register(new SettingDefinition
            {
                Id = LayoutWorkTabMaxHeight,
                ParentId = FeaturesUiElements,
                FieldName = nameof(BetterWorkTabSettings.workTabMaxVisiblePawns),
                Label = "Visible pawn rows",
                Tooltip = "Caps the Work tab height by how many normal pawn rows are visible before scrolling. -1 keeps RimWorld's default full-screen-height behavior.",
                SearchKeywords = WorkTabLayoutSearchKeywords,
                Type = SettingType.NumericInt,
                DefaultValue = DefaultSettings.workTabMaxVisiblePawns,
                MinValue = -1f,
                MaxValue = 200f,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 91,
                OnChanged = _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged()
            });

            Register(new SettingDefinition
            {
                Id = LayoutWorkTabTopSpace,
                ParentId = FeaturesUiElements,
                FieldName = "workTabTopSpace",
                Label = "Space above Work headers",
                Tooltip = "Controls the empty vertical space above the work headers, between the priority direction hint and the top of the header labels. 40px matches RimWorld's default.",
                SearchKeywords = WorkTabLayoutSearchKeywords,
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.workTabTopSpace,
                MinValue = 0f,
                MaxValue = 80f,
                MinLabel = "Tight",
                MaxLabel = "Tall",
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 92,
                OnChanged = _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged()
            });

            Register(new SettingDefinition
            {
                Id = AdvancedHideSettingResetIcons,
                ParentId = AdvancedHeader,
                FieldName = nameof(BetterWorkTabSettings.hideSettingResetIcons),
                Label = "Hide setting reset icons",
                Tooltip = "Hide the per-setting reset buttons shown beside settings that differ from their defaults.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.hideSettingResetIcons,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 403
            });

            Register(new SettingDefinition
            {
                Id = AdvancedSettingFocusHighlightColor,
                ParentId = AdvancedHeader,
                FieldName = nameof(BetterWorkTabSettings.Color_SettingFocusHighlight),
                Label = "Setting focus highlight",
                Tooltip = "Color used to pulse a setting row after Alt-clicking the Work tab or double-clicking a search result.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_SettingFocusHighlight,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 404
            });

            Register(new SettingDefinition
            {
                Id = AdvancedWorkGridRenderer,
                ParentId = AdvancedHeader,
                FieldName = nameof(BetterWorkTabSettings.workGridRendererMode),
                Label = "Work grid renderer",
                Tooltip = "Auto uses BWT's optimized renderer when available and falls back safely. Vanilla always uses the game's native Work grid renderer. Both paths preserve vanilla visuals and interactions.",
                SearchKeywords = new[]
                {
                    "lag", "FPS", "stutter", "slow work tab", "performance",
                    "rendering", "compatibility", "vanilla grid", "optimized grid"
                },
                Type = SettingType.Enum,
                EnumType = typeof(WorkGridRendererMode),
                DefaultValue = DefaultSettings.workGridRendererMode,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 405
            });

            // Auto-assign settings
            Register(new SettingDefinition
            {
                Id = AutoassignViewMode,
                ParentId = FeaturesAutoassign,
                FieldName = "rulesetViewMode",
                Label = "Ruleset editor",
                Tooltip = "Choose the visual builder, the classic list, or make both editor choices available.",
                SearchKeywords = new[] { "visual builder", "classic list", "regular", "raw", "rule interface" },
                Type = SettingType.Enum,
                DefaultValue = DefaultSettings.rulesetViewMode,
                EnumType = typeof(BetterWorkTabSettings.RulesetViewMode),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 405
            });

            Register(new SettingDefinition
            {
                Id = RuleBuilder2Use,
                ParentId = FeaturesAutoassign,
                FieldName = nameof(BetterWorkTabSettings.useRuleBuilder2),
                Label = "Use Rule Builder 2.0",
                Tooltip = "Open the card-based Rule Builder 2.0 by default while preserving the classic builder as a fallback.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.useRuleBuilder2,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 406
            });

            // No "show help" toggle here on purpose. Better Work Tab's help is
            // now RimWorld concepts, and RimWorld already has the switch for
            // that -- the learning helper. A second toggle that could disagree
            // with it would only be a way to get the two out of step.
            Register(new SettingDefinition
            {
                Id = RuleBuilder2TutorialReset,
                ParentId = RuleBuilder2Use,
                Label = "Show Better Work Tab help again",
                Tooltip = "Puts Better Work Tab's entries back in the learning helper, even if you have already read them.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 408,
                OnChanged = _ => Features.Tutorial.BWTConcepts.ReplayAll()
            });

            Register(new SettingDefinition
            {
                Id = RuleBuilder2Highlights,
                ParentId = RuleBuilder2Use,
                FieldName = nameof(BetterWorkTabSettings.ruleBuilder2ShowWorkTabHighlights),
                Label = "Rule Builder Work tab highlights",
                Tooltip = "Highlight the Work tab target while editing or previewing a Rule Builder 2.0 card.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ruleBuilder2ShowWorkTabHighlights,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 409
            });

            Register(new SettingDefinition
            {
                Id = RuleBuilder2Animations,
                ParentId = RuleBuilder2Use,
                FieldName = nameof(BetterWorkTabSettings.ruleBuilder2EnableAnimations),
                Label = "Rule Builder animations",
                Tooltip = "Animate Rule Builder 2.0 cards, previews, and tutorial focus movement.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ruleBuilder2EnableAnimations,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 410
            });

            Register(new SettingDefinition
            {
                Id = RuleBuilder2DraftSuggestions,
                ParentId = RuleBuilder2Use,
                FieldName = nameof(BetterWorkTabSettings.ruleBuilder2UseDraftSuggestions),
                Label = "Generated draft suggestions",
                Tooltip = "Reserved for a future Rule Builder draft generator.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ruleBuilder2UseDraftSuggestions,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 411
            });

            Register(new SettingDefinition
            {
                Id = RuleBuilder2AdvancedConditions,
                ParentId = RuleBuilder2Use,
                FieldName = nameof(BetterWorkTabSettings.ruleBuilder2ShowAdvancedConditions),
                Label = "Show advanced conditions",
                Tooltip = "Show advanced Rule Builder 2.0 condition cards such as capacities and assignment state.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ruleBuilder2ShowAdvancedConditions,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 412
            });

            Register(new SettingDefinition
            {
                Id = RuleBuilder2MatchedPanel,
                ParentId = RuleBuilder2Use,
                FieldName = nameof(BetterWorkTabSettings.ruleBuilder2ShowMatchedPanel),
                Label = "Priority-box match panel",
                Tooltip = "Show matched conditions when clicking a Work tab priority box while Rule Builder 2.0 is open.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ruleBuilder2ShowMatchedPanel,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 413
            });

            Register(new SettingDefinition
            {
                Id = AutoassignConfirm,
                ParentId = FeaturesAutoassign,
                FieldName = "showAutoAssignConfirmation",
                Label = "Confirm on Apply",
                Tooltip = "Show confirmation before applying a ruleset.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showAutoAssignConfirmation,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 401
            });

            Register(new SettingDefinition
            {
                Id = AutoassignResetBefore,
                ParentId = FeaturesAutoassign,
                FieldName = "resetWorkBeforeAutoAssign",
                Label = "Reset Before Apply",
                Tooltip = "Clear assignments before applying a ruleset.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.resetWorkBeforeAutoAssign,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 402
            });

            Register(new SettingDefinition
            {
                Id = AutoassignVisual,
                ParentId = FeaturesAutoassign,
                FieldName = "showAutoAssignVisualFeedback",
                Label = "Visual Feedback",
                Tooltip = "Highlight affected pawns/work types after applying.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showAutoAssignVisualFeedback,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 403
            });

            Register(new SettingDefinition
            {
                Id = WorkloadsPersistDividers,
                ParentId = FeaturesWorkloads,
                FieldName = "persistDividersInWorkloads",
                Label = "Include Dividers",
                Tooltip = "Include divider positions when saving workloads.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.persistDividersInWorkloads,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 4025
            });

            Register(new SettingDefinition
            {
                Id = AdvancedHideAutoAssignBtn,
                ParentId = FeaturesAutoassign,
                FieldName = "hideAutoAssignButton",
                Label = "Hide ruleset button",
                Tooltip = "Hide the ruleset button from the Work tab footer.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.hideAutoAssignButton,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 404
            });
            Register(new SettingDefinition
            {
                Id = AdvancedAlwaysShowConditionEditors,
                ParentId = FeaturesAutoassign,
                FieldName = nameof(BetterWorkTabSettings.alwaysShowConditionEditors),
                Label = "Always show condition editors",
                Tooltip = "Condition rows are always editable without an initial click.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.alwaysShowConditionEditors,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 420 // After persistDividersInWorkloads (4025), before perf toggles
            });

            // Workloads are above; Performance toggles
            Register(new SettingDefinition
            {
                Id = PerfCacheBedCounts,
                ParentId = FeaturesPerformance,
                FieldName = "cacheBedCounts",
                Label = "Cache Bed Counts",
                Tooltip = "Cache bed counts to reduce stutter.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.cacheBedCounts,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 405
            });

            Register(new SettingDefinition
            {
                Id = PerfCacheSkillLevels,
                ParentId = FeaturesPerformance,
                FieldName = "cacheSkillLevels",
                Label = "Cache Skill Levels",
                Tooltip = "Cache skill calculations to reduce redundant work.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.cacheSkillLevels,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 406
            });

            Register(new SettingDefinition
            {
                Id = PerfCacheRowDescriptors,
                ParentId = FeaturesPerformance,
                FieldName = "cacheRowDescriptors",
                Label = "Cache Row Descriptors",
                Tooltip = "Cache row descriptor calculations for layout.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.cacheRowDescriptors,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 407
            });

            Register(new SettingDefinition
            {
                Id = PerfCacheIncapability,
                ParentId = FeaturesPerformance,
                FieldName = "cacheIncapabilityChecks",
                Label = "Cache Incapability Checks",
                Tooltip = "Cache incapability checks for work types.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.cacheIncapabilityChecks,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 408
            });

            Register(new SettingDefinition
            {
                Id = PerfUseElementPooling,
                ParentId = FeaturesPerformance,
                FieldName = "useElementPooling",
                Label = "Use Element Pooling",
                Tooltip = "Reuse UI elements instead of allocating each frame.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.useElementPooling,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 409
            });

            Register(new SettingDefinition
            {
                Id = PerfViewportCulling,
                ParentId = FeaturesPerformance,
                FieldName = "viewportCulling",
                Label = "Viewport Culling",
                Tooltip = "Only render visible rows in the scroll area.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.viewportCulling,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 410
            });

            Register(new SettingDefinition
            {
                Id = AdvancedScrollWheelPriority,
                ParentId = FeaturesClicks,
                FieldName = "enableScrollWheelPriority",
                Label = "Scroll Wheel Priority",
                Tooltip = "Change priorities by hovering a work priority cell and scrolling. Applies to normal work cells, sub-work cells, and time-priority cells.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableScrollWheelPriority,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 25
            });

            // Master toggle for debug logging. When false, no BWT debug messages (except errors) will fire.
            Register(new SettingDefinition
            {
                Id = AdvancedDebugLogging,
                ParentId = AdvancedHeader,
                FieldName = "enableDebugLogging",
                Label = "Enable Debug Logging",
                Tooltip = "Output detailed debug messages to the log.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableDebugLogging,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                ControlsChildVisibility = true,
                SortOrder = 500
            });

            if (settings != null)
            {
                // Dynamic list of debug features. Uses DropdownListAdder to let users pick specific sub-systems to log.
                Register(new SettingDefinition
                {
                    Id = "debug.features",
                    ParentId = AdvancedDebugLogging,
                    Label = "Debug Features",
                    Tooltip = "Select which debug features to enable logging for.",
                    Type = SettingType.DropdownListAdder,
                    DropdownOptionsProvider = () => Enum.GetValues(typeof(DebugFeature))
                        .Cast<DebugFeature>()
                        .Where(f => !settings.debugFeatureToggles.ContainsKey(f) || !settings.debugFeatureToggles[f])
                        .Select(f => f.ToString())
                        .OrderBy(l => l),
                    OnOptionAdded = (option) =>
                    {
                        if (Enum.TryParse<DebugFeature>(option, out var feature))
                        {
                            settings.debugFeatureToggles[feature] = true;
                            settings.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                        }
                    },
                    ShowInSimpleView = false,
                    ShowInAdvancedView = false,
                    SortOrder = 501
                });

                // Display each currently enabled debug feature as a removable button tag.
                var enabledFeatures = settings.debugFeatureToggles.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
                foreach (var feature in enabledFeatures)
                {
                    var localFeature = feature;
                    Register(new SettingDefinition
                    {
                        Id = "debug.feature." + feature.ToString(),
                        ParentId = "debug.features",
                        Label = "  - " + feature.ToString(),
                        Tooltip = "Click to disable logging for this feature.",
                        Type = SettingType.Button,
                        OnChanged = (s) =>
                        {
                            var settingsObj = (BetterWorkTabSettings)s;
                            settingsObj.debugFeatureToggles[localFeature] = false;
                            settingsObj.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                        },
                        ShowInSimpleView = false,
                        ShowInAdvancedView = false,
                        SortOrder = 502
                    });
                }
            }

            // Controls whether BWT tracks performance metrics (visible via debug commands).
            Register(new SettingDefinition
            {
                Id = AdvancedProfiler,
                ParentId = AdvancedHeader,
                FieldName = "enableProfiler",
                Label = "Enable Profiler",
                Tooltip = "Enable in-game profiler (1 to report, Shift+1 to clear).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableProfiler,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 503
            });

            // If enabled, debug messages are mirrored to a dedicated .txt file in the mod folder.
            Register(new SettingDefinition
            {
                Id = AdvancedLogToFile,
                ParentId = AdvancedHeader,
                FieldName = "logDebugToFile",
                Label = "Log to File",
                Tooltip = "Write debug logs to file in addition to console.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.logDebugToFile,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 504
            });

            // Multiplayer sync
            Register(new SettingDefinition
            {
                Id = MpSyncColumnOrder,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpSyncColumnOrder",
                Label = "Sync Column Order",
                Tooltip = "Synchronize column order across multiplayer.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpSyncColumnOrder,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 411
            });

            Register(new SettingDefinition
            {
                Id = MpSyncWorkloads,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpSyncWorkloads",
                Label = "Sync Workloads",
                Tooltip = "Synchronize workload save/load.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpSyncWorkloads,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 412
            });

            Register(new SettingDefinition
            {
                Id = MpSyncRulesets,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpSyncRulesets",
                Label = "Sync Rulesets",
                Tooltip = "Synchronize ruleset applications.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpSyncRulesets,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 413
            });

            Register(new SettingDefinition
            {
                Id = MpConflictMode,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpConflictMode",
                Label = "Conflict Mode",
                Tooltip = "How to resolve multiplayer conflicts.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.MpConflictMode),
                DefaultValue = DefaultSettings.mpConflictMode,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 414
            });

            Register(new SettingDefinition
            {
                Id = MpShowOtherPlayersHover,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpShowOtherPlayersHover),
                Label = "Show other players' hovered cell",
                Tooltip = "Render hover indicators shared by other players.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpShowOtherPlayersHover,
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 415
            });

            Register(new SettingDefinition
            {
                Id = MpAllowPresenceBroadcast,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpAllowPresenceBroadcast),
                Label = "Broadcast my hovered cell",
                Tooltip = "Share the hovered cell you are looking at with your peers.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpAllowPresenceBroadcast,
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 416
            });

            Register(new SettingDefinition
            {
                Id = MpAllowOthersToRequestLayout,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpAllowOthersToRequestLayout),
                Label = "Allow layout requests",
                Tooltip = "Permit other players to request snapshots of your layout.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpAllowOthersToRequestLayout,
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 417
            });

            Register(new SettingDefinition
            {
                Id = MpShowLinkedIndicator,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpShowLinkedIndicator),
                Label = "Show linked indicator",
                Tooltip = "Display a linked/peered indicator when viewing another player's layout.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpShowLinkedIndicator,
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 418
            });

            Register(new SettingDefinition
            {
                Id = HeadersHeader,
                Label = "Headers",
                Type = SettingType.Header,
                Tooltip = "Work-column label style, angle, color, and alignment.",
                SearchKeywords = WorkHeaderSearchKeywords,
                HeaderColor = new Color(0.7f, 0.7f, 0.9f),
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 350
            });

            Register(new SettingDefinition
            {
                Id = HeadersCustomWorkLabels,
                ParentId = HeadersHeader,
                FieldName = "enableCustomWorkLabels",
                Label = "Custom Work names",
                Tooltip = "Allow renamed Work columns and specific jobs to appear in the Work tab. Off keeps saved names but shows default names.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableCustomWorkLabels,
                OnChanged = _ =>
                {
                    WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.HeaderText | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry | WorkGrid.Contracts.WorkTabDirtyFlags.RenderResources);
                    MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                },
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 505
            });

            Register(new SettingDefinition
            {
                Id = HeadersAngled,
                ParentId = HeadersHeader,
                FieldName = "enableAngledHeaders",
                Label = "Angled Work headers",
                Tooltip = "Draw Work names at an angle to fit more columns. Turning this off uses vanilla-style headers and disables the moved-column marker.",
                SearchKeywords = WorkHeaderSearchKeywords,
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableAngledHeaders,
                ControlsChildVisibility = true,
                Suppressions = new List<SettingSuppression>
                {
                    FluffyWorkTabGateway.CreateWorkTabOwnedByFluffySuppression(
                        "Fluffy Work Tab is drawing the Work tab headers.")
                },
                OnChanged = s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 506
            });

            Register(new SettingDefinition
            {
                Id = DragdropRemoveHeaderUnderline,
                ParentId = HeadersAngled,
                FieldName = "removeHeaderUnderline",
                Label = "Hide header underline",
                Tooltip = "Remove the line beneath Work header labels.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.removeHeaderUnderline,
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 5061 // Immediately after HeadersAngled
            });

            Register(new SettingDefinition
            {
                Id = HeadersAngleRotation,
                ParentId = HeadersAngled,
                FieldName = "angledHeaderRotation",
                Label = "Angle rotation",
                Tooltip = "Rotate the angled headers (-90 to 90 degrees). Snaps to 5-degree increments.",
                SearchKeywords = WorkHeaderSearchKeywords,
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.angledHeaderRotation,
                MinValue = -90f,
                MaxValue = 90f,
                OnChanged = s => 
                {
                    var bSettings = (BetterWorkTabSettings)s;
                    bSettings.angledHeaderRotation = Mathf.RoundToInt(bSettings.angledHeaderRotation / 5f) * 5;
                    HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                },
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 507
            });

            Register(new SettingDefinition
            {
                Id = "headers.useVerticalStackingForCJK",
                ParentId = HeadersAngled,
                FieldName = "useVerticalStackingForCJK",
                Label = "Vertical stacking for CJK",
                Tooltip = "Draw East Asian characters (Korean, Chinese, Japanese) vertically when angled headers are enabled. This is much more legible than rotated text.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.useVerticalStackingForCJK,
                OnChanged = s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 5072
            });

            Register(new SettingDefinition
            {
                Id = "headers.cjkVerticalKerning",
                ParentId = "headers.useVerticalStackingForCJK", // Nest under the toggle
                FieldName = "cjkVerticalKerning",
                Label = "CJK vertical kerning",
                Tooltip = "Adjust the vertical spacing between characters in Asian vertical stacking. Lower values mean tighter spacing.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.cjkVerticalKerning,
                MinValue = 0.5f,
                MaxValue = 1.5f,
                OnChanged = s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 5073
            });

            Register(new SettingDefinition
            {
                Id = "headers.angledColor",
                ParentId = HeadersAngled,
                FieldName = "angledHeaderColor",
                Label = "Angled Work-header text color",
                Tooltip = "Colors Work names drawn in angled column headers.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_AngledHeaderText,
                OnChanged = s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 5071
            });

            Register(new SettingDefinition
            {
                Id = HeadersUnderlineColor,
                ParentId = HeadersAngled,
                FieldName = nameof(BetterWorkTabSettings.headerUnderlineColor),
                Label = "Work-header underline color",
                Tooltip = "Colors the line beneath angled Work names and the stem used by vanilla-style Work headers.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_HeaderUnderline,
                OnChanged = s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 50715
            });

            Register(new SettingDefinition
            {
                Id = "headers.horizontalOffset",
                ParentId = HeadersAngled,
                FieldName = "angledHeaderHorizontalOffset",
                Label = "Horizontal offset",
                Tooltip = "Adjust the horizontal position of angled headers. 0 is centered; 10 is the default. At -90°, the offset is automatically set to 0 for alignment.",
                SearchKeywords = WorkHeaderSearchKeywords,
                Type = SettingType.NumericInt,
                DefaultValue = DefaultSettings.angledHeaderHorizontalOffset,
                MinValue = -100f,
                MaxValue = 100f,
                OnChanged = s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 508
            });

            Register(new SettingDefinition
            {
                Id = AdvancedRestoreDefaults,
                Label = "Restore default settings",
                Tooltip = "Reset all settings to default values.",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 420,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Restore every Better Work Tab setting to its default? Your current settings will be lost.", () =>
                        {
                            settings.RestoreDefaults();
                            WorkColumnOrderManager.ResetToVanilla();
                            settings.Write();
                            Messages.Message("Factory defaults restored.", MessageTypeDefOf.PositiveEvent, false);
                        }, true, "Confirm Restore"));
                    }
                }
            });

            ApplyDeterministicRootSectionOrdering();
        }

        /// <summary>
        /// Gives root sections a stable alphabetical order without touching child sort orders.
        /// Child order is deliberately owned by each section because coupled controls often need
        /// a functional sequence rather than alphabetical labels.
        /// </summary>
        private static void ApplyDeterministicRootSectionOrdering()
        {
            List<SettingDefinition> roots = _settings
                .Where(def => def != null && string.IsNullOrEmpty(def.ParentId))
                .OrderBy(def => def.Label ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(def => def.Id, StringComparer.Ordinal)
                .ToList();

            for (int index = 0; index < roots.Count; index++)
            {
                roots[index].SortOrder = index;
            }
        }
    }
}
