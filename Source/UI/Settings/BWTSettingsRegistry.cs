using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Foundation.GameState;
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
using Spine.UI.SettingsFramework;
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

        private static SettingsSchema<BetterWorkTabSettings> _schema;
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
                return _schema.Definitions;
            }
        }

        public static SettingsSchema<BetterWorkTabSettings> Schema
        {
            get
            {
                EnsureInitialized();
                return _schema;
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
            _hierarchy = new SettingsHierarchy(_schema.Definitions);
            _initialized = true;
            try
            {
                BWTSettingsAdaptiveSearchAliases.Initialize(_schema.Definitions);
                SettingsConsistencyValidator.ValidateAtStartup();
            }
            catch
            {
                _initialized = false;
                _schema = null;
                _hierarchy = null;
                BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
                throw;
            }
        }

        public static void Invalidate()
        {
            BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
            if (!_initialized)
            {
                return;
            }

            _initialized = false;
            _schema = null;
            _hierarchy = null;
            BetterWorkTabSettingsUI.NotifySettingsChanged();
        }

        private static void PrepareDefinition(SettingDefinition def)
        {
            ApplyScribeMetadata(def);
            Action<object> existingOnChanged = def?.OnChanged;
            if (def != null)
            {
                def.OnChanged = settingsObject =>
                {
                    existingOnChanged?.Invoke(settingsObject);
                    BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged(def.Id);
                };
                BWTWorkloadSettingsOwnershipPolicy.PrepareDefinition(def);
            }
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

        private static void RegisterHiddenPreference<TValue>(SettingsScope<BetterWorkTabSettings> scope, string id,
                                                             System.Linq.Expressions.Expression<Func<BetterWorkTabSettings, TValue>> field, SettingType type, TValue defaultValue,
                                                             Action<object> onChanged = null)
        {
            string fieldName = ((System.Linq.Expressions.MemberExpression)field.Body).Member.Name;
            SettingDefinition definition = scope.Field(id, field, type, fieldName, "Hidden compatibility preference.")
                .DefaultTo(defaultValue)
                .ShownIn(false, false)
                .Ordered(int.MaxValue);
            definition.OnChanged = onChanged;
        }

        /// <summary>
        /// True when specific jobs open as Fluffy Work Tab's right-expanding columns rather than
        /// Better Work Tab's focused view. The focused view's layout settings are inert in that mode.
        /// </summary>
        private static bool UsesExpandBesideDrilldown()
        {
            BetterWorkTabSettings.SubWorkDrilldownStyle style = BetterWorkTabMod.Settings?.subWorkDrilldownStyle ?? DefaultSettings.subWorkDrilldownStyle;
            return style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside;
        }

        private static void RegisterModCompatibilitySettings(SettingsScope<BetterWorkTabSettings> scope)
        {
            IReadOnlyList<IModSettingsContributor> contributors = BWTModSettingsApi.GetContributors();
            if (contributors == null || contributors.Count == 0)
            {
                return;
            }

            SettingsSchema<BetterWorkTabSettings> contributorSchema = new SettingsSchema<BetterWorkTabSettings>();
            SettingsScope<BetterWorkTabSettings> contributorScope = contributorSchema.Root;
            var sections = new List<BWTModSettingsSection>();
            foreach (IModSettingsContributor contributor in contributors)
            {
                if (contributor == null)
                {
                    continue;
                }

                BWTModSettingsSection section = contributor.CreateSettingsSection(contributorScope);
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

            scope.Define(ModCompatHeader, SettingType.Header, "Mod Compatibility", "Settings for loaded mod integrations.")
                .Accented(new Color(0.7f, 0.75f, 0.9f))
                .ShownIn(false, true)
                .Ordered(-40)
                .ShownWhen(settingsObject => HasVisibleModCompatibilitySection(sections, settingsObject));

            foreach (BWTModSettingsSection section in sections)
            {
                section.Header.ParentId = ModCompatHeader;
                scope.Add(section.Header);

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

                    scope.Add(child);
                }
            }
        }

        private static bool HasVisibleModCompatibilitySection(List<BWTModSettingsSection> sections, object settingsObject)
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
                var offOption = new FloatMenuOption("BWT_SubWorkTransition_Off".Translate(), () =>
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
                    [offOption] = "BWT_SubWorkTransition_Off_Description".Translate(),
                    [classicOption] = "BWT_Enum_SubWorkTransitionStyle_ClassicGlideFlash_Description".Translate(),
                    [pixelOption] = "BWT_Enum_SubWorkTransitionStyle_PixelWaveFlip_Description".Translate()
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
                return "BWT_SubWorkTransition_Off".Translate();
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
            _schema = new SettingsSchema<BetterWorkTabSettings>();
            SettingsSchema<BetterWorkTabSettings> schema = _schema;

            RegisterHiddenPreference(_schema.Root, "compat.settingsViewMode", settings => settings.settingsViewMode, SettingType.Enum, DefaultSettings.settingsViewMode);
            RegisterHiddenPreference(_schema.Root, "compat.persistColumnWidths", settings => settings.persistColumnWidths, SettingType.Bool, DefaultSettings.persistColumnWidths);
            RegisterHiddenPreference(_schema.Root, "compat.Color_IncapableBecauseOfCapacities", settings => settings.Color_IncapableBecauseOfCapacities, SettingType.Color, DefaultSettings.Color_IncapableBecauseOfCapacities);
            RegisterHiddenPreference(_schema.Root, "compat.Color_HeaderText", settings => settings.Color_HeaderText, SettingType.Color, DefaultSettings.Color_HeaderText);
            RegisterHiddenPreference(_schema.Root, "compat.Color_DividerText", settings => settings.Color_DividerText, SettingType.Color, DefaultSettings.Color_DividerText);
            RegisterHiddenPreference(_schema.Root, "compat.Color_Borders", settings => settings.Color_Borders, SettingType.Color, DefaultSettings.Color_Borders);
            RegisterHiddenPreference(_schema.Root, "compat.enableExtendedPriorities", settings => settings.enableExtendedPriorities, SettingType.Bool, DefaultSettings.enableExtendedPriorities);
            RegisterHiddenPreference(_schema.Root, "compat.delegateToExternalPriorityMods", settings => settings.delegateToExternalPriorityMods, SettingType.Bool, DefaultSettings.delegateToExternalPriorityMods);
            RegisterHiddenPreference(_schema.Root, "compat.selectedPriorityProviderId", settings => settings.selectedPriorityProviderId, SettingType.Custom, DefaultSettings.selectedPriorityProviderId);

            schema.Root.Toggle(FeaturesOverlay, settings => settings.enableSkillOverlayFeature, "Skill display", tooltip: "Show skill levels and best-pawn indicators in the Work tab. The options below control when each indicator appears.")
                .DefaultTo(DefaultSettings.enableSkillOverlayFeature)
                .SearchableBy(SkillDisplaySearchKeywords)
                .ControlsChildren()
                .Ordered(-49)
                .Accented(new Color(0.9f, 0.7f, 0.4f))
                .Emphasized(true);

            schema.Root
                .Toggle(FeaturesDragdrop, settings => settings.enableDragDropReordering, "Reorder by dragging",
                        tooltip: "Drag pawn rows, Work columns, rules, and a rule's conditions into a new order. Turning this off keeps the current order, and puts arrow buttons on rule conditions instead.")
                .DefaultTo(DefaultSettings.enableDragDropReordering)
                .SearchableBy(ReorderingSearchKeywords)
                .ControlsChildren()
                .Ordered(-48)
                .Accented(new Color(0.5f, 0.8f, 0.5f))
                .Emphasized(true);

            schema.Root.Toggle(FeaturesHighlights, settings => settings.ShowPawnAndWorktypeHighlights, "Row and column highlights", tooltip: "Highlight the pawn row and Work column related to the current selection or cursor position.")
                .DefaultTo(DefaultSettings.ShowPawnAndWorktypeHighlights)
                .ControlsChildren()
                .Ordered(-47)
                .Accented(new Color(0.4f, 0.6f, 0.9f))
                .Emphasized(true);

            schema.Root.Toggle(FeaturesDividers, settings => settings.enableDividers, "Dividers", tooltip: "Add named divider rows to organize pawns into collapsible groups.")
                .DefaultTo(DefaultSettings.enableDividers)
                .SearchableBy(new[] { "pawn groups", "organize colonists", "section", "separator", "collapse rows" })
                .ControlsChildren()
                .Ordered(-46)
                .Accented(new Color(0.8f, 0.8f, 0.6f))
                .Emphasized(true);

            schema.Root
                .Toggle(FeaturesAutoassign, settings => settings.enableAutoAssignFeature, "Automatic work assignments (rulesets)", tooltip: "Create reusable rules that assign work priorities from pawn skills, passions, capabilities, and other conditions.")
                .DefaultTo(DefaultSettings.enableAutoAssignFeature)
                .SearchableBy(new[] { "automatically assign work", "best pawn", "passions", "skills", "new colonist", "priority rules", "work manager" })
                .ControlsChildren()
                .Ordered(-45)
                .Accented(new Color(0.6f, 0.6f, 0.6f))
                .Emphasized(true);

            schema.Root.Under(FeaturesAutoassign)
                .Toggle(AutoassignWarnOnApply, settings => settings.warnOnApplyRuleset, "Warn before applying Ruleset", tooltip: "Show a confirmation warning before applying a ruleset to all colonists.")
                .DefaultTo(DefaultSettings.warnOnApplyRuleset)
                .Ordered(1);

            schema.Root.Toggle(FeaturesWorkloads, settings => settings.enableWorkloads, "Saved work-priority layouts (workloads)", tooltip: "Save the colony's current pawn work priorities as a named layout and restore it later.")
                .DefaultTo(DefaultSettings.enableWorkloads)
                .SearchableBy(new[] { "save priorities", "load priorities", "preset", "profile", "snapshot", "backup assignments", "work layout" })
                .ControlsChildren()
                .Ordered(-44)
                .Accented(new Color(0.6f, 0.6f, 0.6f))
                .Emphasized(true);

            schema.Root.Under(FeaturesWorkloads)
                .Toggle(WorkloadsWarnOnApply, settings => settings.warnOnApplyWorkload, "Warn before applying Workload", tooltip: "Show a confirmation warning before applying a workload to all colonists.")
                .DefaultTo(DefaultSettings.warnOnApplyWorkload)
                .Ordered(1);

            schema.Root.Under(FeaturesWorkloads)
                .Toggle(
                    WorkloadsPreviewRevealAnimation,
                    settings => settings.enableWorkloadPreviewRevealAnimation,
                    "Animate workload preview reveal",
                    tooltip: "Animate the workload preview controls as they appear and disappear.")
                .DefaultTo(DefaultSettings.enableWorkloadPreviewRevealAnimation)
                .Ordered(2);

            schema.Root.Under(FeaturesWorkloads)
                .Int(
                    WorkloadsPreviewRevealSpeed,
                    settings => settings.workloadPreviewRevealSpeed,
                    "Workload preview reveal speed",
                    tooltip: "Controls how quickly the workload preview controls slide into view.")
                .DefaultTo(DefaultSettings.workloadPreviewRevealSpeed)
                .Ordered(3)
                .ShownWhen(s => ((BetterWorkTabSettings)s).enableWorkloadPreviewRevealAnimation)
                .ValueRange(25f, 200f)
                .ValueLabels("Slow", "Fast")
                .FormattedAs("{0}%");

            schema.Root.Under(FeaturesWorkloads)
                .Toggle(
                    WorkloadsInspectionHighlights,
                    settings => settings.enableWorkloadInspectionHighlights,
                    "Workload inspection highlights",
                    tooltip: "Highlight changed workload rows and cells while inspecting a workload preview.")
                .DefaultTo(DefaultSettings.enableWorkloadInspectionHighlights)
                .ControlsChildren()
                .Ordered(4);

            schema.Root.Under(FeaturesWorkloads)
                .Int(
                    WorkloadsInspectionOpacity,
                    settings => settings.workloadInspectionOpacity,
                    "Workload inspection highlight opacity",
                    tooltip: "Controls the opacity of workload inspection highlights.")
                .DefaultTo(DefaultSettings.workloadInspectionOpacity)
                .Ordered(5)
                .ShownWhen(s => ((BetterWorkTabSettings)s).enableWorkloadInspectionHighlights)
                .ValueRange(0f, 100f)
                .ValueLabels("Transparent", "Solid")
                .FormattedAs("{0}%");

            schema.Root.Under(FeaturesWorkloads)
                .Toggle(AdvancedWorkloadsLegacy, settings => settings.useLegacyWorkloads, "Use legacy Workloads (1.0.5)",
                        tooltip: "Use the original Workloads behavior from Better Work Tab 1.0.5 instead of the modern Workloads implementation.",
                        onChanged: settings => BWTWorkloadSettingsOwnershipPolicy.HandleLegacyWorkloadModeChanged(settings))
                .DefaultTo(DefaultSettings.useLegacyWorkloads)
                .SearchableBy(new[] { "legacy workloads", "classic workloads", "Workloads 1.0.5", "compatibility", "workload version" })
                .Ordered(2)
                .AdvancedOnly();

            schema.Root
                .Toggle(FeaturesSubWorkJobs, settings => settings.enableSubWorkDrilldown, "Specific jobs",
                        tooltip: "Open a Work column to set priorities for its individual jobs. Use the shortcut below on a Work header or cell; use it again, or press Escape, to return.")
                .DefaultTo(DefaultSettings.enableSubWorkDrilldown)
                .SearchableBy(SpecificJobSearchKeywords)
                .ControlsChildren()
                .Ordered(-43)
                .Accented(new Color(0.55f, 0.75f, 0.9f))
                .Emphasized(true);

            schema.Root.Under(FeaturesSubWorkJobs)
                .Enum(SubWorkOpenModifier, settings => settings.subWorkDrilldownModifier, "Shortcut modifier", tooltip: "Modifier key used with the mouse button below to open or leave the specific-job view.")
                .DefaultTo(DefaultSettings.subWorkDrilldownModifier)
                .Ordered(1);

            schema.Root.Under(FeaturesSubWorkJobs)
                .Enum(SubWorkOpenButton, settings => settings.subWorkDrilldownButton, "Shortcut mouse button", tooltip: "Mouse button used with the modifier key above to open or leave the specific-job view.")
                .DefaultTo(DefaultSettings.subWorkDrilldownButton)
                .Ordered(2);

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkCrossWorkDragDrop, settings => settings.enableSubWorkCrossWorkDragDrop, "Move specific jobs between Work columns",
                        tooltip: "Drag a specific job onto another Work column to move it there. Turning this off limits dragging to the current specific-job view.")
                .DefaultTo(DefaultSettings.enableSubWorkCrossWorkDragDrop)
                .Ordered(5);

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkCompactPriorityBoxes, settings => settings.useCompactSubWorkPriorityBoxes, "Compact boxes in expanded view",
                        tooltip: "Use smaller priority boxes when specific jobs expand beside their parent Work column. Focused full-tab view always uses normal-size boxes.")
                .DefaultTo(DefaultSettings.useCompactSubWorkPriorityBoxes)
                .SearchableBy(new[] { "small cells", "narrow columns", "Fluffy layout", "compact priorities" })
                .Ordered(6)
                .AdvancedOnly();

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkGlobalVanillaPriorityBoxes, settings => settings.useVanillaSubWorkGlobalPriorityBoxes, "Full-size boxes in the shared-priority row",
                        tooltip: "Draw the priority boxes in the top shared-priority row at normal Work-cell size instead of using compact boxes.")
                .DefaultTo(DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes)
                .SearchableBy(new[] { "top row", "global priority", "shared priority", "box size" })
                .Ordered(7)
                .AdvancedOnly();

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkRestoreCursor, settings => settings.restoreCursorOnSubWorkExit, "Restore cursor from headers", tooltip: "After leaving from a specific-job header, move the cursor back to the Work header used to open it.")
                .DefaultTo(DefaultSettings.restoreCursorOnSubWorkExit)
                .Ordered(7)
                .AdvancedOnly();

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkRestoreCursorFromPawnCells, settings => settings.restoreCursorOnSubWorkPawnCellExit, "Restore cursor from pawn cells",
                        tooltip: "After leaving from a pawn priority cell, move the cursor back to the Work header used to open the specific-job view. Turning this off leaves the cursor where you clicked.")
                .DefaultTo(DefaultSettings.restoreCursorOnSubWorkPawnCellExit)
                .Ordered(8)
                .AdvancedOnly();

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkOverrideBreakAnimation, settings => settings.enableSubWorkOverrideBreakAnimation, "Override reset animation", tooltip: "Show a short break-and-fade effect when returning a pawn's specific-job priority to the shared priority.")
                .DefaultTo(DefaultSettings.enableSubWorkOverrideBreakAnimation)
                .Ordered(9)
                .AdvancedOnly();

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkTransitionAnimation, settings => settings.enableSubWorkTransitionAnimation, "Specific-job transition animation",
                        tooltip: "Animate specific-job views, including Fluffy-style expand-beside columns and header text fade when they open or collapse.", onChanged: _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.enableSubWorkTransitionAnimation)
                .Ordered(10)
                .ShownIn(false, false);

            schema.Root.Under(FeaturesSubWorkJobs)
                .Custom(SubWorkTransitionMode, (rect, rowLabel, rowTooltip, settings, disabled) => DrawSubWorkTransitionMode(rect, rowLabel, rowTooltip, settings, disabled), "Animation style",
                        tooltip: "Choose how the Work tab opens specific jobs. Off changes instantly with no transition.", onChanged: _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .Ordered(10)
                .AdvancedOnly()
                .WithCustomReset(IsSubWorkTransitionModeNonDefault, ResetSubWorkTransitionMode);

            schema.Root.Under(FeaturesSubWorkJobs)
                .Enum(SubWorkTransitionStyle, settings => settings.subWorkTransitionStyle, "Transition style", tooltip: "Classic glide is the original sub-work transition. Pixel wave reveal keeps columns in place and fades them as the grey wave passes.",
                      onChanged: _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.subWorkTransitionStyle)
                .Ordered(11)
                .ShownIn(false, false);

            schema.Root.Under(FeaturesSubWorkJobs).Float(
                SubWorkTransitionSpeed,
                settings => settings.subWorkTransitionSeconds,
                "Animation speed",
                tooltip: "Controls how quickly the Work tab opens, expands, collapses, or leaves specific jobs.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings settings)
                {
                    settings.subWorkTransitionSeconds =
                        BetterWorkTabSettings.ClampSubWorkTransitionSeconds(settings.subWorkTransitionSeconds);
                }

                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
            }
            )
                .DefaultTo(DefaultSettings.subWorkTransitionSeconds)
                .Ordered(11)
                .ShownWhen(s => (s as BetterWorkTabSettings)?.enableSubWorkTransitionAnimation ?? true)
                .AdvancedOnly()
                .ValueRange(0.2f, 0.9f)
                .ValueLabels("Fast", "Slow")
                .FormattedAs("{0:0.00}s")
        ;

            schema.Root.Under(FeaturesSubWorkJobs)
                .Enum(SubWorkDisabledParentMode, settings => settings.subWorkDisabledParentMode, "When the parent Work type is off",
                      tooltip: "Choose whether locked specific-job priorities can still run when their parent Work type is disabled. Multiplayer always uses vanilla parent-disable behavior.", onChanged: _ => WorkExecutionOrder.MarkAllPawnsWorkGiversDirty())
                .DefaultTo(DefaultSettings.subWorkDisabledParentMode)
                .SearchableBy(new[] { "locked job", "disabled work", "job still run", "parent work off", "override parent" })
                .Ordered(12);

            schema.Root.Under(FeaturesSubWorkJobs)
                .Toggle(SubWorkAutoExpandColumns, settings => settings.subWorkAutoExpandColumns, "Expand specific-job columns", tooltip: "Use empty table width for specific-job columns so long labels fit without changing the pawn-name column.",
                        onChanged: _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.subWorkAutoExpandColumns)
                .ControlsChildren()
                .Ordered(12)
                .AdvancedOnly()
                .Configure(definition =>
                           {
                               definition.Suppressions = new List<SettingSuppression> { new SettingSuppression { When = settingsObj => !((BetterWorkTabSettings)settingsObj).keepVanillaWorkTabMinimumWidth,
                                                                                                                 Reason =
                                                                                                                     _ => "Compact window width keeps focused columns at their natural widths.",
                                                                                                                 SuppressorSettingId = LayoutWorkTabMinimumWidth, LinkLabel = "Keep vanilla minimum width" },
                                                                                        new SettingSuppression { When =
                                                                                                                     _ => FluffyWorkTabGateway.CanHostFluffySubWorkColumns && UsesExpandBesideDrilldown(),
                                                                                                                 Reason =
                                                                                                                     _ => "Expand beside uses BWT's dedicated child columns.",
                                                                                                                 SuppressorSettingId = SubWorkDrilldownStyle, LinkLabel = "Specific-job view" },
                                                                                        new SettingSuppression { When =
                                                                                                                     _ => BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders,
                                                                                                                 Reason =
                                                                                                                     _ => "Angled headers already keep labels from colliding.",
                                                                                                                 SuppressorSettingId = HeadersAngled, LinkLabel = "Angled headers" } };
                           });

            schema.Root.Under(SubWorkAutoExpandColumns)
                .Toggle(SubWorkEvenlyExpandColumns, settings => settings.subWorkEvenlyExpandColumns, "Use equal expanded widths", tooltip: "Give expanded specific-job columns equal widths. Turning this off widens only columns whose labels need more room.",
                        onChanged: _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.subWorkEvenlyExpandColumns)
                .Ordered(1)
                .AdvancedOnly();

            schema.Root.Define(FeaturesUiElements, SettingType.Header, "Work Tab", tooltip: "General Work tab size, spacing, controls, and display options.").Ordered(-42).Accented(new Color(0.8f, 0.8f, 0.6f));

            schema.Root.Define(FeaturesClicks, SettingType.Header, "Mouse & shortcuts", tooltip: "Mouse and shortcut behavior for the Work tab.").Ordered(-41).Accented(new Color(0.7f, 0.75f, 0.9f));

            RegisterModCompatibilitySettings(_schema.Root);

            schema.Root.Under(FeaturesUiElements)
                .Define(PriorityHeader, SettingType.Header, "Priority range", tooltip: "Controls which mod owns the manual priority range.")
                .SearchableBy(PriorityRangeSearchKeywords)
                .Ordered(42)
                .Accented(new Color(0.8f, 0.7f, 0.45f));

            schema.Root.Under(PriorityHeader).Enum(
                PriorityModeSetting,
                settings => settings.priorityMode,
                "Priority mode",
                tooltip: "Auto keeps RimWorld's normal 1-4 range unless another compatible mod or existing higher priorities require more. Better Work Tab lets BWT manage the expanded range.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings settings)
                {
                    settings.NormalizePrioritySettings();
                }

                PriorityAuthorityBroker.InvalidateCaches();
                Patch_WorkPriority_DoCell_Unified.ClearColorCache();
            }
            )
                .DefaultTo(DefaultSettings.priorityMode)
                .SearchableBy(PriorityRangeSearchKeywords)
                .Ordered(0)
        ;

            schema.Root.Under(PriorityHeader).Int(
                UiMaxPriority,
                settings => settings.maxPriorityInt,
                "Maximum priority",
                tooltip: "Highest manual priority available when Better Work Tab manages the priority range.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings settings)
                {
                    settings.NormalizePrioritySettings();
                }

                PriorityAuthorityBroker.InvalidateCaches();
                Patch_WorkPriority_DoCell_Unified.ClearColorCache();
            }
            )
                .DefaultTo(DefaultSettings.maxPriority)
                .SearchableBy(PriorityRangeSearchKeywords)
                .Ordered(2)
                .ShownWhen(s =>
            {
                var settings = (BetterWorkTabSettings)s;
                return settings.priorityMode == PriorityMode.BetterWorkTab ||
                       settings.priorityMode == PriorityMode.Auto;
            })
                .ValueRange(BetterWorkTabSettings.MAX_PRIORITY_MINIMUM, BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT)
        ;

            schema.Root.Under(PriorityHeader).Enum(
                UiAutoDisabledPriorityMode,
                settings => settings.autoDisabledPriorityMode,
                "Priority when re-enabling work",
                tooltip: "Choose the priority assigned when you click disabled work back on in Auto priority mode.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings settings)
                {
                    settings.NormalizePrioritySettings();
                }
            }
            )
                .DefaultTo(DefaultSettings.autoDisabledPriorityMode)
                .SearchableBy(new[] { "turn work back on", "disabled cell", "re-enable", "click priority" })
                .Ordered(3)
                .ShownWhen(s => ((BetterWorkTabSettings)s).priorityMode == PriorityMode.Auto)
        ;

            schema.Root.Under(PriorityHeader)
                .Toggle(UiTimePrioritySchedules, settings => settings.enableTimePrioritySchedules, "Priorities by hour", tooltip: "Ctrl-click a work-priority cell to set different priorities by time of day.")
                .DefaultTo(DefaultSettings.enableTimePrioritySchedules)
                .SearchableBy(HourlyPrioritySearchKeywords)
                .ControlsChildren()
                .Ordered(8);

            schema.Root.Under(UiTimePrioritySchedules)
                .Toggle(UiTimePriorityHourDivider, settings => settings.showTimePriorityHourDivider, "Show time-number divider", tooltip: "Draw a thin divider line above the hour numbers in the Work tab time-priority editor.")
                .DefaultTo(DefaultSettings.showTimePriorityHourDivider)
                .Ordered(1)
                .AdvancedOnly();

            schema.Root.Under(UiTimePrioritySchedules)
                .Toggle(UiTimePriorityCopyPasteButtons, settings => settings.showTimePriorityCopyPasteButtons, "Show schedule copy/paste buttons",
                        tooltip: "Show copy and paste controls for Work tab time-priority schedules. These controls use the same copy/paste column as vanilla while a schedule row is open.")
                .DefaultTo(DefaultSettings.showTimePriorityCopyPasteButtons)
                .Ordered(2);

            schema.Root.Under(UiTimePrioritySchedules)
                .Toggle(UiTimePrioritySourceColumnHighlight, settings => settings.keepTimePrioritySourceColumnHighlighted, "Keep source column highlighted",
                        tooltip: "While a time-priority schedule is open, keep the work column it edits highlighted and prevent the schedule strip from highlighting columns behind it.")
                .DefaultTo(DefaultSettings.keepTimePrioritySourceColumnHighlighted)
                .Ordered(3)
                .AdvancedOnly();

            schema.Root.Under(UiTimePrioritySchedules)
                .Toggle(UiFluffyTimePriorityMirroring, settings => settings.enableFluffyTimePriorityMirroring, "Mirror schedules to Fluffy",
                        tooltip: "When Fluffy Work Tab is loaded, push BWT time-priority schedules into Fluffy's own per-hour priority tracker.")
                .DefaultTo(DefaultSettings.enableFluffyTimePriorityMirroring)
                .Ordered(4)
                .ShownWhen(
                    _ => FluffyWorkTabGateway.IsPresent)
                .AdvancedOnly();

            schema.Root.Under(PriorityHeader)
                .Int(UiAutoDisabledPriorityFixedValue, settings => settings.autoDisabledPriorityFixedValue, "Fixed re-enabled priority", tooltip: "Priority number used when the setting above is Fixed Priority.")
                .DefaultTo(DefaultSettings.autoDisabledPriorityFixedValue)
                .SearchableBy(new[] { "turn work back on", "disabled cell", "re-enable", "click priority" })
                .Ordered(4)
                .ShownWhen(s =>
                           {
                               var settings = (BetterWorkTabSettings)s;
                               return settings.priorityMode == PriorityMode.Auto && settings.autoDisabledPriorityMode == BetterWorkTabSettings.AutoDisabledPriorityMode.FixedPriority;
                           })
                .ValueRange(1, BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT);

            schema.Root.Under(FeaturesUiElements)
                .Int(UiPriorityColorPercentageGreen, settings => settings.priorityColorPercentage_Green, "Green priority threshold",
                     tooltip: "For extended priorities, color the highest-priority number range green. Lower numbers are acted on first; this percentage is measured from priority 1 toward the maximum.")
                .DefaultTo(DefaultSettings.priorityColorPercentage_Green)
                .Ordered(43)
                .AdvancedOnly()
                .ValueRange(1, 100);

            schema.Root.Under(FeaturesUiElements)
                .Int(UiPriorityColorPercentageYellow, settings => settings.priorityColorPercentage_Yellow, "Yellow priority threshold",
                     tooltip: "For extended priorities, color priority numbers yellow through this cumulative percentage of the range. Lower numbers are acted on first.")
                .DefaultTo(DefaultSettings.priorityColorPercentage_Yellow)
                .Ordered(44)
                .AdvancedOnly()
                .ValueRange(1, 100);

            schema.Root.Under(FeaturesUiElements)
                .Int(UiPriorityColorPercentageTan, settings => settings.priorityColorPercentage_Tan, "Tan priority threshold",
                     tooltip: "For extended priorities, color priority numbers tan through this cumulative percentage of the range. Lower numbers are acted on first; later numbers are gray.")
                .DefaultTo(DefaultSettings.priorityColorPercentage_Tan)
                .Ordered(45)
                .AdvancedOnly()
                .ValueRange(1, 100);

            // Multiplayer settings are retained for possible future implementation; they are not currently wired to runtime behavior.
            schema.Root.Toggle(FeaturesMultiplayer, settings => settings.enableMultiplayerSync, "Multiplayer Sync", tooltip: "Enable multiplayer synchronization options.")
                .DefaultTo(DefaultSettings.enableMultiplayerSync)
                .ControlsChildren()
                .Ordered(-40)
                .ShownWhen(
                    _ => MP.enabled && MP.IsInMultiplayer)
                .ShownIn(false, false);

            // Highlights
            schema.Root.Under(FeaturesHighlights)
                .Toggle(HighlightsHover, settings => settings.ShowCursorPawnAndWorktypeHighlight, "Highlight on Hover", tooltip: "Tint the row and column under your cursor.")
                .DefaultTo(DefaultSettings.ShowCursorPawnAndWorktypeHighlight)
                .ControlsChildren()
                .Ordered(0);

            schema.Root.Under(HighlightsHover).Button(
                "highlights.masterColor",
                "Set Master Highlight Color",
                tooltip: "Select a color to apply to ALL highlight settings (Hover, Selected, etc).",
                onChanged: settingsObj =>
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
                         s.Color_CustomSimilarWorktypeHighlight = picked;
                         s.Write();
                         Messages.Message("BWT_Settings_MasterHighlightColor_Applied".Translate(), MessageTypeDefOf.PositiveEvent, false);
                     }));
                }
            }
            )
                .Ordered(0)
        ;

            schema.Root.Under(HighlightsHover)
                .Colour(HighlightsHoverColor, settings => settings.Color_CursorHighlight, "Cell hover: row and column color", tooltip: "Tints both the pawn row and Work column crossing under the cursor.")
                .DefaultTo(DefaultSettings.Color_CursorHighlight)
                .Ordered(1)
                .AdvancedOnly();

            schema.Root.Under(HighlightsHover)
                .Colour(HighlightsRowHoverColor, settings => settings.Color_RowHoverHighlight, "Cell hover: pawn-row color", tooltip: "Tints the horizontal pawn row crossing under the cursor.")
                .DefaultTo(DefaultSettings.Color_RowHoverHighlight)
                .Ordered(2)
                .AdvancedOnly();

            schema.Root.Under(HighlightsHover)
                .Colour(HighlightsColumnHoverColor, settings => settings.Color_ColumnHoverHighlight, "Cell hover: Work-column color", tooltip: "Tints the vertical Work column crossing under the cursor.")
                .DefaultTo(DefaultSettings.Color_ColumnHoverHighlight)
                .Ordered(3)
                .AdvancedOnly();

            schema.Root.Under(HighlightsHover).Button(
                HighlightsResetRowHoverColor,
                "Reset Row Hover Color",
                tooltip: "Reset row hover highlight to the general hover color.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings settings)
                {
                    settings.Color_RowHoverHighlight = settings.Color_MouseHoverHighlight;
                    settings.Write();
                }
            }
            )
                .Ordered(4)
                .AdvancedOnly()
        ;

            schema.Root.Under(HighlightsHover).Button(
                HighlightsResetColumnHoverColor,
                "Reset Column Hover Color",
                tooltip: "Reset column hover highlight to the general hover color.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings settings)
                {
                    settings.Color_ColumnHoverHighlight = settings.Color_MouseHoverHighlight;
                    settings.Write();
                }
            }
            )
                .Ordered(5)
                .AdvancedOnly()
        ;

            schema.Root.Under(FeaturesHighlights)
                .Toggle(HighlightsSelected, settings => settings.DoSelectedPawnHighlight, "Highlight Selected Pawn", tooltip: "Always highlight the currently selected pawn's row.")
                .DefaultTo(DefaultSettings.DoSelectedPawnHighlight)
                .Ordered(2);

            schema.Root.Under(HighlightsSelected)
                .Colour(HighlightsSelectedColor, settings => settings.Color_SelectedPawnHighlight, "Selected pawn-row color", tooltip: "Tints the full Work-tab row for a pawn selected on the map or colonist bar.")
                .DefaultTo(DefaultSettings.Color_SelectedPawnHighlight)
                .Ordered(2_1)
                .AdvancedOnly();

            schema.Root.Under(HighlightsSelected)
                .Float(HighlightsSelectedOpacity, settings => settings.SelectedPawnHighlightOpacity, "Selected Pawn Highlight Opacity", tooltip: "Opacity for selected pawn highlight.")
                .DefaultTo(DefaultSettings.SelectedPawnHighlightOpacity)
                .Ordered(2_2)
                .AdvancedOnly()
                .ValueRange(0f, 1f);

            schema.Root.Under(FeaturesHighlights)
                .Toggle(HighlightsFloatMenu, settings => settings.ShowFloatMenuPawnAndWorktypeHighlight, "Highlight context-menu target", tooltip: "When a context menu opens the Work tab, highlight the related pawn and Work column.")
                .DefaultTo(DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight)
                .Ordered(3);

            schema.Root.Under(HighlightsFloatMenu)
                .Colour(HighlightsFloatMenuColor, settings => settings.Color_FloatMenuHighlight, "Context target: row and column color",
                        tooltip: "Tints the pawn row and Work column opened by Go to Work or Manage Work options, including those beside Do Once. The Do Once action itself stays on the map and has no Work-tab highlight.")
                .DefaultTo(DefaultSettings.Color_FloatMenuHighlight)
                .Ordered(3_1)
                .AdvancedOnly();

            schema.Root.Under(HighlightsHover)
                .Toggle(HighlightsOutlineMode, settings => settings.useOutlineHighlights, "Use Outline Highlights", tooltip: "Draw highlights as outlines instead of solid boxes.")
                .DefaultTo(DefaultSettings.useOutlineHighlights)
                .Ordered(4);

            schema.Root.Under(FeaturesOverlay)
                .Int(HighlightsBestPawnBackground, settings => settings.bestPawnHighlightThickness, "Best-pawn outline thickness", tooltip: "Adjust the thickness of the green outline for the best pawn in a work type.")
                .DefaultTo(DefaultSettings.bestPawnHighlightThickness)
                .Ordered(42)
                .ShownWhen(s => ((BetterWorkTabSettings)s).ShowUIMode_ShowPawnForSkillSquare != BetterWorkTabSettings.ShowUIMode.Never)
                .AdvancedOnly()
                .ValueRange(1f, 4f);

            schema.Root.Under(HighlightsHover)
                .Toggle(HighlightsSimilar, settings => settings.ShowSimilarWorktypeHighlight, "Related Work columns", tooltip: "Dimly highlight other Work columns that use the same skills.")
                .DefaultTo(DefaultSettings.ShowSimilarWorktypeHighlight)
                .Ordered(5);

            schema.Root.Under(HighlightsSimilar)
                .Colour(HighlightsSimilarColor, settings => settings.Color_CustomSimilarWorktypeHighlight, "Related Work-column color", tooltip: "Tints other Work columns that use skills related to the column under the cursor.")
                .DefaultTo(DefaultSettings.Color_CustomSimilarWorktypeHighlight)
                .Ordered(5_1)
                .AdvancedOnly();

            schema.Root.Under(HighlightsSimilar)
                .Float(HighlightsSimilarOpacity, settings => settings.SimilarWorktypeHighlightOpacity, "Related Work opacity", tooltip: "Opacity used to highlight related Work columns.")
                .DefaultTo(DefaultSettings.SimilarWorktypeHighlightOpacity)
                .Ordered(5_2)
                .AdvancedOnly()
                .ValueRange(0f, 1f);

            // Layout
            schema.Root.Under(FeaturesDragdrop)
                .Toggle(LayoutCtrlDrag, settings => settings.requireCtrlForDrag, "Require Ctrl for Dragging", tooltip: "Hold Ctrl to drag rows/columns. Prevents accidental reordering.")
                .DefaultTo(DefaultSettings.requireCtrlForDrag)
                .Ordered(95);

            schema.Root.Under(FeaturesDragdrop)
                .Toggle(DragdropEnableGrouping, settings => settings.enableColumnGrouping, "Group columns with Shift-click", tooltip: "Shift-click Work headers to select and drag multiple columns together.")
                .DefaultTo(DefaultSettings.enableColumnGrouping)
                .Ordered(96)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDragdrop).Toggle(LayoutDragRows, settings => settings.rowDraggingEnabled, "Drag pawn rows", tooltip: "Drag pawn rows to change their order.").DefaultTo(DefaultSettings.rowDraggingEnabled).Ordered(1011);

            schema.Root.Under(FeaturesDragdrop)
                .Toggle(LayoutDragColumns, settings => settings.columnDraggingEnabled, "Drag Work columns", tooltip: "Drag Work columns to change their order.")
                .DefaultTo(DefaultSettings.columnDraggingEnabled)
                .Ordered(1012);

            schema.Root.Under(FeaturesDragdrop)
                .Int(LayoutDragThreshold, settings => settings.dragThreshold, "Drag Threshold (px)", tooltip: "Minimum mouse movement before a drag begins.")
                .DefaultTo(DefaultSettings.dragThreshold)
                .Ordered(1013)
                .AdvancedOnly()
                .ValueRange(1f, 20f);

            schema.Root.Under(FeaturesDragdrop)
                .Int(LayoutDragColumnLineInset, settings => settings.columnInsertionLineInset, "Column insertion-line height", tooltip: "How far the insertion line extends upward from the bottom of the Work header while dragging a column.")
                .DefaultTo(DefaultSettings.columnInsertionLineInset)
                .SearchableBy(new[] { "drag line", "drop position", "insertion marker" })
                .Ordered(1014)
                .AdvancedOnly()
                .ValueRange(0f, 64f);

            schema.Root.Under(FeaturesAutoassign)
                .Float(LayoutDragHoverDelay, settings => settings.dragHoverDelay, "Rule Builder: Drag Hover Delay (s)", tooltip: "Time in seconds to hover before switching categories while dragging rules.")
                .DefaultTo(DefaultSettings.dragHoverDelay)
                .Ordered(430)
                .AdvancedOnly()
                .ValueRange(0.2f, 2.0f);

            schema.Root.Under(FeaturesClicks)
                .Toggle(LayoutClickClose, settings => settings.disableLeftClickClose, "Keep Work tab open after selecting a pawn", tooltip: "Selecting a pawn keeps the Work tab open. While enabled, clicks outside the tab also leave it open.")
                .DefaultTo(DefaultSettings.disableLeftClickClose)
                .SearchableBy(new[] { "stay open", "don't close", "selecting colonist", "click outside", "map click" })
                .Ordered(1020);

            schema.Root.Under(FeaturesClicks)
                .Toggle(LayoutCloseOnMapClick, settings => settings.closeOnMapClick, "Close on map click", tooltip: "Close the Work tab when clicking on the map.")
                .DefaultTo(DefaultSettings.closeOnMapClick)
                .SearchableBy(new[] { "stay open", "don't close", "click outside", "map click" })
                .Ordered(1021)
                .Configure(definition =>
                           {
                               definition.Suppressions = new List<SettingSuppression> { new SettingSuppression { When = settingsObj => ((BetterWorkTabSettings)settingsObj).disableLeftClickClose,
                                                                                                                 Reason =
                                                                                                                     _ => "Keeping the Work tab open overrides this option.",
                                                                                                                 SuppressorSettingId = LayoutClickClose, LinkLabel = "Keep Work tab open" } };
                           });

            schema.Root.Under(FeaturesClicks)
                .Toggle(LayoutContextMenu, settings => settings.enableContextMenuOnRightClick, "Right-click context menu", tooltip: "Enable context menu on right-clicking pawn names.")
                .DefaultTo(DefaultSettings.enableContextMenuOnRightClick)
                .Ordered(1022);

            schema.Root.Under(FeaturesUiElements)
                .Toggle(LayoutWorkTabMinimumWidth, settings => settings.keepVanillaWorkTabMinimumWidth, "Keep vanilla minimum width",
                        tooltip: "Keep the Work tab at least as wide as RimWorld's normal Work tab to reduce distracting motion. Wider content may still expand the tab to the right. Turn this off to let compact layouts shrink the window.",
                        onChanged: _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged())
                .DefaultTo(DefaultSettings.keepVanillaWorkTabMinimumWidth)
                .SearchableBy(WorkTabLayoutSearchKeywords)
                .Ordered(90)
                .AdvancedOnly();

            schema.Root.Under(FeaturesUiElements)
                .Toggle(LayoutPawnCount, settings => settings.showPawnCountAtBottom, "Show Colonist Count", tooltip: "Display colonist count in the bottom-left corner.")
                .DefaultTo(DefaultSettings.showPawnCountAtBottom)
                .Ordered(103);

            schema.Root.Under(FeaturesUiElements)
                .Toggle(LayoutBedCount, settings => settings.showBedCountAtBottom, "Show Bed Count", tooltip: "Display bed count (red if insufficient).")
                .DefaultTo(DefaultSettings.showBedCountAtBottom)
                .Ordered(104);

            schema.Root.Under(FeaturesUiElements)
                .Toggle(UiPriorityLegend, settings => settings.showPriorityLegend, "Show Priority Legend", tooltip: "Display priority direction text above work columns.")
                .DefaultTo(DefaultSettings.showPriorityLegend)
                .Ordered(1041);

            schema.Root.Under(FeaturesUiElements)
                .Toggle(UiDragInstructions, settings => settings.showDragInstructions, "Show Footer Control Hints", tooltip: "Always show the Shift skill-view hint and show other control hints only when hovering a target that supports them.")
                .DefaultTo(DefaultSettings.showDragInstructions)
                .Ordered(1042);

            schema.Root.Under(FeaturesUiElements)
                .Toggle(UiContextSettingsHint, settings => settings.showContextSettingsHint, "Show Alt-Click Settings Hint",
                        tooltip: "Show the top-right hint that Alt-clicking the Work tab opens related settings. This turns off automatically after the first successful Alt-click.")
                .DefaultTo(DefaultSettings.showContextSettingsHint)
                .Ordered(1043)
                .AdvancedOnly();

            schema.Root.Under(FeaturesUiElements)
                .Toggle(UiGeneralTutorial, settings => settings.showGeneralTutorial, "Show Better Work Tab Tutorial",
                        tooltip: "Show or resume the interactive Better Work Tab tutorial. Turning this off pauses the tutorial without clearing completed lessons or the current lesson.")
                .DefaultTo(DefaultSettings.showGeneralTutorial)
                .Ordered(10434);

            schema.Root.Under(FeaturesUiElements)
                .Toggle(UiManualPriorities, settings => settings.showManualPrioritiesCheckbox, "Show Manual Priorities Checkbox",
                        tooltip: "Show the Manual Priorities checkbox in the header. Disabling hides the checkbox and prevents switching between checkmarks and manual priority numbers.")
                .DefaultTo(DefaultSettings.showManualPrioritiesCheckbox)
                .Ordered(1044);

            schema.Root.Under(FeaturesUiElements)
                .Toggle("ui.autoEnableManualPriorities", settings => settings.autoEnableManualPriorities, "Turn on Manual priorities automatically", tooltip: "Automatically check the Manual Priorities checkbox when opening the Work tab.")
                .DefaultTo(DefaultSettings.autoEnableManualPriorities)
                .Ordered(1045);

            schema.Root.Under(FeaturesDividers)
                .Float(LayoutDividerHeight, settings => settings.dividerHeight, "Divider Default Height", tooltip: "Default height of divider rows in pixels.")
                .DefaultTo(DefaultSettings.dividerHeight)
                .Ordered(105)
                .AdvancedOnly()
                .ValueRange(10f, 50f)
                .ValueLabels("Thin", "Thick");

            schema.Root.Under(FeaturesDividers)
                .Float(LayoutDividerAlpha, settings => settings.dividerMinAlpha, "Divider Minimum Opacity", tooltip: "Minimum background opacity for dividers.")
                .DefaultTo(DefaultSettings.dividerMinAlpha)
                .Ordered(106)
                .AdvancedOnly()
                .ValueRange(0f, 1f);

            schema.Root.Under(FeaturesDividers).Toggle(DividersShow, settings => settings.showDividerRows, "Show Dividers", tooltip: "Toggle visibility of divider rows.").DefaultTo(DefaultSettings.showDividerRows).Ordered(107);

            schema.Root.Under(FeaturesDividers)
                .Toggle(DividersCustomColors, settings => settings.allowCustomDividerColors, "Custom divider colors", tooltip: "Choose a different color for each divider.")
                .DefaultTo(DefaultSettings.allowCustomDividerColors)
                .Ordered(108)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDividers)
                .Toggle("dividers.highlight", settings => settings.highlightDividersOnHover, "Highlight on Hover", tooltip: "Highlight dividers when hovering over them using the hover highlight color.")
                .DefaultTo(DefaultSettings.highlightDividersOnHover)
                .Ordered(109)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDividers).Toggle(DividersLabels, settings => settings.showDividerLabels, "Show Divider Labels", tooltip: "Show divider labels by default.").DefaultTo(DefaultSettings.showDividerLabels).Ordered(111);

            schema.Root.Under(FeaturesDividers)
                .Toggle(DividersCollapse, settings => settings.allowDividerCollapse, "Collapsible dividers", tooltip: "Let dividers collapse or expand their pawn groups.")
                .DefaultTo(DefaultSettings.allowDividerCollapse)
                .Ordered(112);

            schema.Root.Under(FeaturesDividers)
                .Toggle(DividersAnimations, settings => settings.enableDividerAnimations, "Animate Divider Changes", tooltip: "Smoothly grows and collapses divider sections and newly inserted dividers. Disable this if another mod causes table resize flicker.",
                        onChanged: _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged())
                .DefaultTo(DefaultSettings.enableDividerAnimations)
                .Ordered(113)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDividers).Button(
                "dividers.resetHeight",
                "Reset all divider heights",
                tooltip: "Restore every divider to the default height.",
                onChanged: settingsObj =>
            {
                IWorkTabDividerState dividers =
                    WorkTabGameRoots.For(Current.Game)?.State.Dividers;
                if (settingsObj is BetterWorkTabSettings settings && dividers != null)
                {
                    if (dividers.ActiveDividers != null)
                    {
                        foreach (var div in dividers.ActiveDividers)
                        {
                            div.Height = settings.dividerHeight;
                        }
                        MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                        Messages.Message("BWT_Settings_DividerHeight_Reset".Translate(), MessageTypeDefOf.PositiveEvent, false);
                    }
                }
            }
            )
                .Ordered(114)
                .AdvancedOnly()
        ;

            schema.Root.Under(FeaturesDragdrop).Button(LayoutResetColumns, "Reset Columns to Vanilla", tooltip: "Restore all work columns to their default order.", onChanged: _ => WorkColumnOrderManager.ResetToVanilla()).Ordered(1015);

            schema.Root.Under(FeaturesDragdrop)
                .Toggle(ColumnsShowMovedIndicator, settings => settings.showColumnMovedMarker, "Show Moved Indicator", tooltip: "Show indicator on manually moved columns.")
                .DefaultTo(DefaultSettings.showColumnMovedMarker)
                .Ordered(115)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDragdrop)
                .Toggle(ColumnsShowBaselineLine, settings => settings.showColumnBaselineLine, "Show Baseline Line While Dragging", tooltip: "Show a line at the column's vanilla position while dragging moved columns.")
                .DefaultTo(DefaultSettings.showColumnBaselineLine)
                .Ordered(116)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDragdrop)
                .Toggle("columns.showMovedColorTint", settings => settings.showMovedColumnColorTint, "Color tint for reordered columns", tooltip: "Apply a color tint to the text of manually reordered columns.")
                .DefaultTo(DefaultSettings.showMovedColumnColorTint)
                .Ordered(117)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDragdrop)
                .Colour("columns.movedMarkerColor", settings => settings.movedMarkerColor, "Moved-column marker color", tooltip: "Colors the star and header tint that identify a Work column moved from its default position.")
                .DefaultTo(DefaultSettings.Color_MovedMarkerColor)
                .Ordered(118)
                .AdvancedOnly();

            schema.Root.Under(FeaturesDragdrop).Button(
                ColumnsResetWidths,
                "Reset Column Widths",
                tooltip: "Clear saved column widths.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings s)
                {
                    s.storedColumnWidths.Clear();
                    s.Write();
                }
            }
            )
                .Ordered(1016)
                .AdvancedOnly()
        ;

            // Skill colors
            schema.Root.Under(FeaturesOverlay).Define(OverlayHeader, SettingType.Header, "Skill Colors", tooltip: "Skill overlay behaviors when using Shift and hover.").Ordered(104).Accented(new Color(0.8f, 0.8f, 0.6f)).AdvancedOnly();

            var settings = BetterWorkTabMod.Settings;
            if (settings != null)
            {
                // Dynamic settings generation: The DropdownListAdder lets users add hidden work types,
                // and for each hidden type, we create a removable button tag below.
                schema.Root.Under(FeaturesUiElements)
                    .Field(HideWorktypes, settings => settings.hiddenWorktypes, SettingType.DropdownListAdder, "Hidden Work Types",
                           tooltip: "Select work types to hide from the work tab.")
                    .SearchableBy(new[] { "hide column", "remove work column", "unwanted job", "show work type", "unhide" })
                    .Ordered(105)
                    .OptionsFrom(() => DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(wt => !settings.hiddenWorktypes.Contains(wt.defName)).Select(w => w.labelShort.CapitalizeFirst()).OrderBy(l => l),
                                 (option) =>
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
                                 });

                foreach (var hiddenDefName in settings.hiddenWorktypes)
                {
                    var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(hiddenDefName);
                    if (wt == null)
                        continue;

                    string localHiddenDefName = hiddenDefName;
                    schema.Root.Under(HideWorktypes).Button(
                        "hide.wt." + hiddenDefName,
                        "  - " + wt.labelShort.CapitalizeFirst(),
                        tooltip: "Click to unhide this work type.",
                        onChanged: (s) =>
                    {
                        var settingsObj = (BetterWorkTabSettings)s;
                        settingsObj.hiddenWorktypes.Remove(localHiddenDefName);
                        settingsObj.Write();
                        _initialized = false;
                        BetterWorkTabSettingsUI.NotifySettingsChanged();
                        EnsureInitialized();
                        MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                    }
                    )
                        .Ordered(106)
                        .Configure(definition =>
                        {
                            definition.LabelKey = string.Empty;
                            definition.TooltipKey = "BWT_Settings_hide.workType_Tooltip";
                        })
                ;
                }
            }

            schema.Root.Under(FeaturesOverlay)
                .Enum(OverlayNumbersMode, settings => settings.ShowUIMode_ShowSmallSkillNumbers, "Tiny Skill Numbers", tooltip: "When to show the small skill numbers in cells.")
                .DefaultTo(DefaultSettings.ShowUIMode_ShowSmallSkillNumbers)
                .Ordered(0);

            schema.Root.Under(FeaturesOverlay)
                .Enum(OverlayBestPawnMode, settings => settings.ShowUIMode_ShowPawnForSkillSquare, "Best-pawn indicator", tooltip: "When to highlight the pawn with highest skill.",
                      onChanged: settingsObj => ((BetterWorkTabSettings)settingsObj).ShowUIMode_ShowPawnForSkillSquare = BetterWorkTabSettings.ShowUIMode.Shifted)
                .DefaultTo(DefaultSettings.ShowUIMode_ShowPawnForSkillSquare)
                .Ordered(1);

            schema.Root.Under(FeaturesOverlay)
                .Toggle(OverlayHoverCellOverlay, settings => settings.showHoverCellOverlay, "Change cell display on hover",
                        tooltip: "When enabled, hovering can emphasize either skill or priority in one cell or the whole column, according to the options below.")
                .DefaultTo(DefaultSettings.showHoverCellOverlay)
                .SearchableBy(new[] { "mouse over", "skill on hover", "priority on hover", "big skill number", "column hover" })
                .Ordered(2);

            schema.Root.Under(OverlayHoverCellOverlay)
                .Enum(OverlayHoverMode, settings => settings.skillViewHoverMode, "Hover Behavior (Priority/Skill)", tooltip: "Choose hover visuals: Skill focused (big skill number + small priority) or Priority focused (vanilla box + tiny skill).")
                .DefaultTo(DefaultSettings.skillViewHoverMode)
                .Ordered(3);

            schema.Root.Under(OverlayHoverCellOverlay)
                .Enum(OverlayHoverScope, settings => settings.hoverEffectScope, "Hover Effect Scope", tooltip: "Controls whether hover overlays are shown only on the hovered cell or across the whole column.")
                .DefaultTo(DefaultSettings.hoverEffectScope)
                .Ordered(4)
                .AdvancedOnly();

            schema.Root.Under(OverlayHeader)
                .Colour(ColorsSkillVeryLow, settings => settings.Color_VeryLowSkill, "Shift-overlay skill number: levels 0-3",
                        tooltip: "Text color of skill-level numbers from 0 through 3 shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                        onChanged: _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache())
                .DefaultTo(DefaultSettings.Color_VeryLowSkill)
                .SearchableBy(new[] { "skill text", "shift numbers", "aptitude", "skill level color" })
                .Ordered(301)
                .AdvancedOnly();

            schema.Root.Under(OverlayHeader)
                .Colour(ColorsSkillLow, settings => settings.Color_LowSkill, "Shift-overlay skill number: levels 4-9",
                        tooltip: "Text color of skill-level numbers from 4 through 9 shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                        onChanged: _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache())
                .DefaultTo(DefaultSettings.Color_LowSkill)
                .SearchableBy(new[] { "skill text", "shift numbers", "aptitude", "skill level color" })
                .Ordered(302)
                .AdvancedOnly();

            schema.Root.Under(OverlayHeader)
                .Colour(ColorsSkillGood, settings => settings.Color_GoodLowSkill, "Shift-overlay skill number: levels 10-15",
                        tooltip: "Text color of skill-level numbers from 10 through 15 shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                        onChanged: _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache())
                .DefaultTo(DefaultSettings.Color_GoodLowSkill)
                .SearchableBy(new[] { "skill text", "shift numbers", "aptitude", "skill level color" })
                .Ordered(303)
                .AdvancedOnly();

            schema.Root.Under(OverlayHeader)
                .Colour(ColorsSkillExcellent, settings => settings.Color_ExcellentSkill, "Shift-overlay skill number: levels 16+",
                        tooltip: "Text color of skill-level numbers at 16 or higher shown while holding Shift. This does not change the cell background; RimWorld shades every cell by skill aptitude, including unassigned cells.",
                        onChanged: _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache())
                .DefaultTo(DefaultSettings.Color_ExcellentSkill)
                .SearchableBy(new[] { "skill text", "shift numbers", "aptitude", "skill level color" })
                .Ordered(304)
                .AdvancedOnly();

            schema.Root.Under(OverlayHeader)
                .Colour(ColorsBestPawnOutline, settings => settings.Color_BestPawnForSkillSquare, "Best-pawn cell indicator color", tooltip: "Colors the outline or background around the highest-skilled eligible pawn's Work cell.")
                .DefaultTo(DefaultSettings.Color_BestPawnForSkillSquare)
                .Ordered(305)
                .AdvancedOnly();

            // Advanced
            schema.Root.Define(AdvancedHeader, SettingType.Header, "Maintenance", tooltip: "Advanced toggles and maintenance/reset options.").Ordered(400).Accented(new Color(0.6f, 0.6f, 0.6f));

            schema.Root.Under(FeaturesUiElements)
                .NumericInt(LayoutWorkTabMaxVisiblePawns, settings => settings.workTabMaxVisiblePawns, "Visible pawn rows",
                            tooltip: "Caps the Work tab height by how many normal pawn rows are visible before scrolling. -1 keeps RimWorld's default full-screen-height behavior.", onChanged: _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged())
                .DefaultTo(DefaultSettings.workTabMaxVisiblePawns)
                .SearchableBy(WorkTabLayoutSearchKeywords)
                .Ordered(91)
                .AdvancedOnly()
                .ValueRange(-1f, 200f);

            schema.Root.Under(FeaturesUiElements)
                .Float(LayoutWorkTabTopSpace, settings => settings.workTabTopSpace, "Space above Work headers",
                       tooltip: "Controls the empty vertical space above the work headers, between the priority direction hint and the top of the header labels. 40px matches RimWorld's default.",
                       onChanged: _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged())
                .DefaultTo(DefaultSettings.workTabTopSpace)
                .SearchableBy(WorkTabLayoutSearchKeywords)
                .Ordered(92)
                .AdvancedOnly()
                .ValueRange(0f, 80f)
                .ValueLabels("Tight", "Tall");

            schema.Root.Under(AdvancedHeader)
                .Toggle(AdvancedHideSettingResetIcons, settings => settings.hideSettingResetIcons, "Hide setting reset icons", tooltip: "Hide the per-setting reset buttons shown beside settings that differ from their defaults.")
                .DefaultTo(DefaultSettings.hideSettingResetIcons)
                .Ordered(403)
                .AdvancedOnly();

            schema.Root.Under(AdvancedHeader)
                .Colour(AdvancedSettingFocusHighlightColor, settings => settings.Color_SettingFocusHighlight, "Setting focus highlight", tooltip: "Color used to pulse a setting row after Alt-clicking the Work tab or double-clicking a search result.")
                .DefaultTo(DefaultSettings.Color_SettingFocusHighlight)
                .Ordered(404)
                .AdvancedOnly();

            schema.Root.Under(AdvancedHeader)
                .Custom(
                    AdvancedSearchAliases,
                    (rect, label, tooltip, settings, disabled) =>
                        BWTSettingsAdaptiveSearchAliases.DrawAliasStatus(
                            rect,
                            label,
                            tooltip,
                            settings,
                            disabled),
                    "Learned settings search",
                    tooltip: "BWT can remember a local search correction after three deliberate confirmations. The alias file stays on this computer and can be reset here.")
                .SearchableBy(new[]
                {
                    "learned search",
                    "search correction",
                    "adaptive search",
                    "search aliases",
                    "reset search"
                })
                .Ordered(405)
                .AdvancedOnly()
                .Configure(definition =>
                {
                    definition.CustomHasNonDefaultValue = _ =>
                        BWTSettingsAdaptiveSearchAliases.ActiveAliasCount > 0 ||
                        BWTSettingsAdaptiveSearchAliases.PendingAliasCount > 0;
                    definition.CustomReset = _ => BWTSettingsAdaptiveSearchAliases.Reset();
                });

            schema.Root.Under(AdvancedHeader)
                .Enum(AdvancedWorkGridRenderer, settings => settings.workGridRendererMode, "Work grid renderer",
                      tooltip: "Auto uses BWT's optimized renderer when available and falls back safely. Vanilla always uses the game's native Work grid renderer. Both paths preserve vanilla visuals and interactions.")
                .DefaultTo(DefaultSettings.workGridRendererMode)
                .SearchableBy(new[] { "lag", "FPS", "stutter", "slow work tab", "performance", "rendering", "compatibility", "vanilla grid", "optimized grid" })
                .Ordered(405)
                .AdvancedOnly();

            // Auto-assign settings
            schema.Root.Under(FeaturesAutoassign)
                .Enum(AutoassignViewMode, settings => settings.rulesetViewMode, "Ruleset editor", tooltip: "Choose the visual builder, the classic list, or make both editor choices available.")
                .DefaultTo(DefaultSettings.rulesetViewMode)
                .SearchableBy(new[] { "visual builder", "classic list", "regular", "raw", "rule interface" })
                .Ordered(405);

            schema.Root.Under(FeaturesAutoassign)
                .Toggle(RuleBuilder2Use, settings => settings.useRuleBuilder2, "Use Rule Builder 2.0", tooltip: "Open the card-based Rule Builder 2.0 by default while preserving the classic builder as a fallback.")
                .DefaultTo(DefaultSettings.useRuleBuilder2)
                .Ordered(406);

            // No "show help" toggle here on purpose. Better Work Tab's help is
            // now RimWorld concepts, and RimWorld already has the switch for
            // that -- the learning helper. A second toggle that could disagree
            // with it would only be a way to get the two out of step.
            schema.Root.Under(RuleBuilder2Use)
                .Button(RuleBuilder2TutorialReset, "Show Better Work Tab help again", tooltip: "Puts Better Work Tab's entries back in the learning helper, even if you have already read them.", onChanged: _ => Features.Tutorial.BWTConcepts.ReplayAll())
                .Ordered(408)
                .AdvancedOnly();

            schema.Root.Under(RuleBuilder2Use)
                .Toggle(RuleBuilder2Highlights, settings => settings.ruleBuilder2ShowWorkTabHighlights, "Rule Builder Work tab highlights", tooltip: "Highlight the Work tab target while editing or previewing a Rule Builder 2.0 card.")
                .DefaultTo(DefaultSettings.ruleBuilder2ShowWorkTabHighlights)
                .Ordered(409)
                .AdvancedOnly();

            schema.Root.Under(RuleBuilder2Use)
                .Toggle(RuleBuilder2Animations, settings => settings.ruleBuilder2EnableAnimations, "Rule Builder animations", tooltip: "Animate Rule Builder 2.0 cards, previews, and tutorial focus movement.")
                .DefaultTo(DefaultSettings.ruleBuilder2EnableAnimations)
                .Ordered(410)
                .AdvancedOnly();

            schema.Root.Under(RuleBuilder2Use)
                .Toggle(RuleBuilder2AdvancedConditions, settings => settings.ruleBuilder2ShowAdvancedConditions, "Show advanced conditions", tooltip: "Show advanced Rule Builder 2.0 condition cards such as capacities and assignment state.")
                .DefaultTo(DefaultSettings.ruleBuilder2ShowAdvancedConditions)
                .Ordered(412)
                .AdvancedOnly();

            schema.Root.Under(RuleBuilder2Use)
                .Toggle(RuleBuilder2MatchedPanel, settings => settings.ruleBuilder2ShowMatchedPanel, "Priority-box match panel", tooltip: "Show matched conditions when clicking a Work tab priority box while Rule Builder 2.0 is open.")
                .DefaultTo(DefaultSettings.ruleBuilder2ShowMatchedPanel)
                .Ordered(413)
                .AdvancedOnly();

            schema.Root.Under(FeaturesAutoassign)
                .Toggle(AdvancedAlwaysShowConditionEditors, settings => settings.alwaysShowConditionEditors, "Always show condition editors", tooltip: "Condition rows are always editable without an initial click.")
                .DefaultTo(DefaultSettings.alwaysShowConditionEditors)
                .Ordered(420)
                .AdvancedOnly();

            schema.Root.Under(FeaturesClicks)
                .Toggle(AdvancedScrollWheelPriority, settings => settings.enableScrollWheelPriority, "Scroll Wheel Priority",
                        tooltip: "Change priorities by hovering a work priority cell and scrolling. Applies to normal work cells, sub-work cells, and time-priority cells.")
                .DefaultTo(DefaultSettings.enableScrollWheelPriority)
                .Ordered(25);

            // Master toggle for debug logging. When false, no BWT debug messages (except errors) will fire.
            schema.Root.Under(AdvancedHeader)
                .Toggle(AdvancedDebugLogging, settings => settings.enableDebugLogging, "Enable Debug Logging", tooltip: "Output detailed debug messages to the log.")
                .DefaultTo(DefaultSettings.enableDebugLogging)
                .ControlsChildren()
                .Ordered(500)
                .ShownIn(false, false);

            if (settings != null)
            {
                // Dynamic list of debug features. Uses DropdownListAdder to let users pick specific sub-systems to log.
                schema.Root.Under(AdvancedDebugLogging)
                    .DropdownListAdder("debug.features", "Debug Features",
                                       () => Enum.GetValues(typeof(DebugFeature)).Cast<DebugFeature>().Where(f => !settings.debugFeatureToggles.ContainsKey(f) || !settings.debugFeatureToggles[f]).Select(f => f.ToString()).OrderBy(l => l),
                                       (option) =>
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
                                       tooltip: "Select which debug features to enable logging for.")
                    .Ordered(501)
                    .ShownIn(false, false);

                // Display each currently enabled debug feature as a removable button tag.
                var enabledFeatures = settings.debugFeatureToggles.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
                foreach (var feature in enabledFeatures)
                {
                    var localFeature = feature;
                    schema.Root.Under("debug.features").Button(
                        "debug.feature." + feature.ToString(),
                        "  - " + feature.ToString(),
                        tooltip: "Click to disable logging for this feature.",
                        onChanged: (s) =>
                    {
                        var settingsObj = (BetterWorkTabSettings)s;
                        settingsObj.debugFeatureToggles[localFeature] = false;
                        settingsObj.Write();
                        _initialized = false;
                        BetterWorkTabSettingsUI.NotifySettingsChanged();
                        EnsureInitialized();
                    }
                    )
                        .Ordered(502)
                        .ShownIn(false, false)
                ;
                }
            }

            // Controls whether BWT tracks performance metrics (visible via debug commands).
            schema.Root.Under(AdvancedHeader)
                .Toggle(AdvancedProfiler, settings => settings.enableProfiler, "Enable Profiler", tooltip: "Enable in-game profiler (1 to report, Shift+1 to clear).")
                .DefaultTo(DefaultSettings.enableProfiler)
                .Ordered(503)
                .ShownIn(false, false);

            // Multiplayer sync
            schema.Root.Under(FeaturesMultiplayer)
                .Toggle(MpSyncColumnOrder, settings => settings.mpSyncColumnOrder, "Sync Column Order", tooltip: "Synchronize column order across multiplayer.")
                .DefaultTo(DefaultSettings.mpSyncColumnOrder)
                .Ordered(411)
                .ShownIn(false, false);

            schema.Root.Under(FeaturesMultiplayer).Toggle(MpSyncWorkloads, settings => settings.mpSyncWorkloads, "Sync Workloads", tooltip: "Synchronize workload save/load.").DefaultTo(DefaultSettings.mpSyncWorkloads).Ordered(412).ShownIn(false, false);

            schema.Root.Under(FeaturesMultiplayer).Toggle(MpSyncRulesets, settings => settings.mpSyncRulesets, "Sync Rulesets", tooltip: "Synchronize ruleset applications.").DefaultTo(DefaultSettings.mpSyncRulesets).Ordered(413).ShownIn(false, false);

            schema.Root.Under(FeaturesMultiplayer).Enum(MpConflictMode, settings => settings.mpConflictMode, "Conflict Mode", tooltip: "How to resolve multiplayer conflicts.").DefaultTo(DefaultSettings.mpConflictMode).Ordered(414).ShownIn(false, false);

            schema.Root.Under(FeaturesMultiplayer)
                .Toggle(MpShowOtherPlayersHover, settings => settings.mpShowOtherPlayersHover, "Show other players' hovered cell", tooltip: "Render hover indicators shared by other players.")
                .DefaultTo(DefaultSettings.mpShowOtherPlayersHover)
                .Ordered(415)
                .ShownWhen(
                    _ => MP.enabled && MP.IsInMultiplayer)
                .ShownIn(false, false);

            schema.Root.Under(FeaturesMultiplayer)
                .Toggle(MpAllowPresenceBroadcast, settings => settings.mpAllowPresenceBroadcast, "Broadcast my hovered cell", tooltip: "Share the hovered cell you are looking at with your peers.")
                .DefaultTo(DefaultSettings.mpAllowPresenceBroadcast)
                .Ordered(416)
                .ShownWhen(
                    _ => MP.enabled && MP.IsInMultiplayer)
                .ShownIn(false, false);

            schema.Root.Under(FeaturesMultiplayer)
                .Toggle(MpAllowOthersToRequestLayout, settings => settings.mpAllowOthersToRequestLayout, "Allow layout requests", tooltip: "Permit other players to request snapshots of your layout.")
                .DefaultTo(DefaultSettings.mpAllowOthersToRequestLayout)
                .Ordered(417)
                .ShownWhen(
                    _ => MP.enabled && MP.IsInMultiplayer)
                .ShownIn(false, false);

            schema.Root.Under(FeaturesMultiplayer)
                .Toggle(MpShowLinkedIndicator, settings => settings.mpShowLinkedIndicator, "Show linked indicator", tooltip: "Display a linked/peered indicator when viewing another player's layout.")
                .DefaultTo(DefaultSettings.mpShowLinkedIndicator)
                .Ordered(418)
                .ShownWhen(
                    _ => MP.enabled && MP.IsInMultiplayer)
                .ShownIn(false, false);

            schema.Root.Define(HeadersHeader, SettingType.Header, "Headers", tooltip: "Work-column label style, angle, color, and alignment.").SearchableBy(WorkHeaderSearchKeywords).Ordered(350).Accented(new Color(0.7f, 0.7f, 0.9f));

            schema.Root.Under(HeadersHeader).Toggle(
                HeadersCustomWorkLabels,
                settings => settings.enableCustomWorkLabels,
                "Custom Work names",
                tooltip: "Allow renamed Work columns and specific jobs to appear in the Work tab. Off keeps saved names but shows default names.",
                onChanged: _ =>
            {
                WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.HeaderText | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry | WorkGrid.Contracts.WorkTabDirtyFlags.RenderResources);
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }
            )
                .DefaultTo(DefaultSettings.enableCustomWorkLabels)
                .Ordered(505)
        ;

            schema.Root.Under(HeadersHeader)
                .Toggle(HeadersAngled, settings => settings.enableAngledHeaders, "Angled Work headers", tooltip: "Draw Work names at an angle to fit more columns. Turning this off uses vanilla-style headers and disables the moved-column marker.",
                        onChanged: s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.enableAngledHeaders)
                .SearchableBy(WorkHeaderSearchKeywords)
                .ControlsChildren()
                .Ordered(506)
                .Configure(definition =>
                           { definition.Suppressions = new List<SettingSuppression> { FluffyWorkTabGateway.CreateWorkTabOwnedByFluffySuppression("Fluffy Work Tab is drawing the Work tab headers.") }; });

            schema.Root.Under(HeadersAngled)
                .Toggle(DragdropRemoveHeaderUnderline, settings => settings.removeHeaderUnderline, "Hide header underline", tooltip: "Remove the line beneath Work header labels.")
                .DefaultTo(DefaultSettings.removeHeaderUnderline)
                .Ordered(5061);

            schema.Root.Under(HeadersAngled).Int(
                HeadersAngleRotation,
                settings => settings.angledHeaderRotation,
                "Angle rotation",
                tooltip: "Rotate the angled headers (-90 to 90 degrees). Snaps to 5-degree increments.",
                onChanged: s =>
            {
                var bSettings = (BetterWorkTabSettings)s;
                bSettings.angledHeaderRotation = Mathf.RoundToInt(bSettings.angledHeaderRotation / 5f) * 5;
                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
            }
            )
                .DefaultTo(DefaultSettings.angledHeaderRotation)
                .SearchableBy(WorkHeaderSearchKeywords)
                .Ordered(507)
                .ValueRange(-90f, 90f)
        ;

            schema.Root.Under(HeadersAngled)
                .Toggle("headers.useVerticalStackingForCJK", settings => settings.useVerticalStackingForCJK, "Vertical stacking for CJK",
                        tooltip: "Draw East Asian characters (Korean, Chinese, Japanese) vertically when angled headers are enabled. This is much more legible than rotated text.", onChanged: s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.useVerticalStackingForCJK)
                .Ordered(5072);

            schema.Root.Under("headers.useVerticalStackingForCJK")
                .Float("headers.cjkVerticalKerning", settings => settings.cjkVerticalKerning, "CJK vertical kerning",
                       tooltip: "Adjust the vertical spacing between characters in Asian vertical stacking. Lower values mean tighter spacing.", onChanged: s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.cjkVerticalKerning)
                .Ordered(5073)
                .AdvancedOnly()
                .ValueRange(0.5f, 1.5f);

            schema.Root.Under(HeadersAngled)
                .Colour("headers.angledColor", settings => settings.angledHeaderColor, "Angled Work-header text color", tooltip: "Colors Work names drawn in angled column headers.", onChanged: s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.Color_AngledHeaderText)
                .Ordered(5071)
                .AdvancedOnly();

            schema.Root.Under(HeadersAngled)
                .Colour(HeadersUnderlineColor, settings => settings.headerUnderlineColor, "Work-header underline color", tooltip: "Colors the line beneath angled Work names and the stem used by vanilla-style Work headers.",
                        onChanged: s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.Color_HeaderUnderline)
                .Ordered(50715)
                .AdvancedOnly();

            schema.Root.Under(HeadersAngled)
                .NumericInt("headers.horizontalOffset", settings => settings.angledHeaderHorizontalOffset, "Horizontal offset",
                            tooltip: "Adjust the horizontal position of angled headers. 0 is centered; 10 is the default. At -90°, the offset is automatically set to 0 for alignment.", onChanged: s => HeaderDrawingCoordinator.NotifyAngledHeadersChanged())
                .DefaultTo(DefaultSettings.angledHeaderHorizontalOffset)
                .SearchableBy(WorkHeaderSearchKeywords)
                .Ordered(508)
                .AdvancedOnly()
                .ValueRange(-100f, 100f);

            schema.Root.Button(
                AdvancedRestoreDefaults,
                "Restore default settings",
                tooltip: "Reset all settings to default values.",
                onChanged: settingsObj =>
            {
                if (settingsObj is BetterWorkTabSettings settings)
                {
                    if (BWTWorkloadSettingsOwnershipPolicy.IsBulkSettingsOperationBlocked(out string blockReason))
                    {
                        Messages.Message(blockReason, MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("BWT_Settings_RestoreDefaults_Confirm".Translate(), () =>
                    {
                        if (BWTWorkloadSettingsOwnershipPolicy.IsBulkSettingsOperationBlocked(out string confirmationBlockReason))
                        {
                            Messages.Message(confirmationBlockReason, MessageTypeDefOf.RejectInput, false);
                            return;
                        }

                        settings.RestoreDefaults();
                        WorkColumnOrderManager.ResetToVanilla();
                        settings.Write();
                        Messages.Message("BWT_Settings_RestoreDefaults_Done".Translate(), MessageTypeDefOf.PositiveEvent, false);
                    }, true, "BWT_Settings_RestoreDefaults_Title".Translate()));
                }
            }
            )
                .Ordered(420)
                .AdvancedOnly()
        ;

            foreach (SettingDefinition definition in schema.Definitions)
            {
                PrepareDefinition(definition);
            }
        }
    }
    }
