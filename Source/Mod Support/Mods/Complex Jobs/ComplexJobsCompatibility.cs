using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using RimWorld;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;
using static Better_Work_Tab.UI.Settings.SettingIDs;

namespace Better_Work_Tab.ModSupport.Mods.ComplexJobs
{
    /// <summary>
    /// Presents one compatibility-facing mode selector while keeping the existing BWT settings
    /// as the only persisted source of truth.
    /// </summary>
    internal static class ComplexJobsCompatibility
    {
        private const string PackageId = "FrozenSnowFox.ComplexJobs";
        private static readonly IModSettingsContributor SettingsContributor =
            new ComplexJobsSettingsContributor();

        private enum SubWorkMode
        {
            ComplexJobsOnly,
            BetterWorkTabFocus,
            FluffyStyleExpansion
        }

        internal static bool IsActive =>
            ModsConfig.IsActive(PackageId);

        internal static void RegisterSettings()
        {
            BWTModSettingsApi.RegisterContributor(SettingsContributor);
        }

        private static SubWorkMode GetMode(BetterWorkTabSettings settings)
        {
            if (settings == null || !settings.enableSubWorkDrilldown)
            {
                return SubWorkMode.ComplexJobsOnly;
            }

            return settings.enableFluffyStyleFeatures &&
                   settings.subWorkDrilldownStyle ==
                       BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside
                ? SubWorkMode.FluffyStyleExpansion
                : SubWorkMode.BetterWorkTabFocus;
        }

        private static bool ApplyMode(BetterWorkTabSettings settings, SubWorkMode mode)
        {
            if (settings == null)
            {
                return false;
            }

            bool enableSubWorkDrilldown = mode != SubWorkMode.ComplexJobsOnly;
            bool enableFluffyStyleFeatures = mode == SubWorkMode.FluffyStyleExpansion;
            BetterWorkTabSettings.SubWorkDrilldownStyle subWorkDrilldownStyle =
                mode == SubWorkMode.FluffyStyleExpansion
                    ? BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside
                    : BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView;
            bool changed = settings.enableSubWorkDrilldown != enableSubWorkDrilldown ||
                           settings.enableFluffyStyleFeatures != enableFluffyStyleFeatures ||
                           settings.subWorkDrilldownStyle != subWorkDrilldownStyle;
            if (!changed)
            {
                return false;
            }

            SubWorkDrilldownState.ExitImmediate();
            SubWorkDrilldownState.CollapseAllExpandBesideImmediate();

            settings.enableSubWorkDrilldown = enableSubWorkDrilldown;
            settings.enableFluffyStyleFeatures = enableFluffyStyleFeatures;
            settings.subWorkDrilldownStyle = subWorkDrilldownStyle;

            settings.Write();
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.Columns |
                WorkTabDirtyFlags.HeaderGeometry |
                WorkTabDirtyFlags.SettingsThemeLanguageScale);
            return true;
        }

        private static bool DrawMode(
            Rect rect,
            string label,
            string tooltip,
            object settingsObject,
            bool disabled)
        {
            if (!(settingsObject is BetterWorkTabSettings settings) || !IsActive)
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

            SubWorkMode current = GetMode(settings);
            if (Widgets.ButtonText(buttonRect, GetModeLabel(current)))
            {
                var options = new List<FloatMenuOption>();
                var descriptions = new Dictionary<FloatMenuOption, string>();
                FloatMenuOption selected = null;
                foreach (SubWorkMode mode in System.Enum.GetValues(typeof(SubWorkMode)))
                {
                    SubWorkMode capturedMode = mode;
                    var option = new FloatMenuOption(
                        GetModeLabel(capturedMode),
                        () =>
                        {
                            if (ApplyMode(settings, capturedMode))
                            {
                                BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
                            }
                        });
                    options.Add(option);
                    descriptions[option] = GetModeDescription(capturedMode);
                    if (capturedMode == current)
                    {
                        selected = option;
                    }
                }

                Find.WindowStack.Add(
                    new DescribedFloatMenu(options, selected, label, tooltip, descriptions));
            }

            GUI.enabled = previousEnabled;
            GUI.color = previousColor;
            if (!string.IsNullOrEmpty(tooltip) && !DescribedFloatMenu.AnyOpen)
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
            }

            return false;
        }

        private static string GetModeLabel(SubWorkMode mode)
        {
            switch (mode)
            {
                case SubWorkMode.ComplexJobsOnly:
                    return "Complex Jobs only";
                case SubWorkMode.FluffyStyleExpansion:
                    return "Fluffy-style expansion";
                default:
                    return "BWT Focus View";
            }
        }

        private static string GetModeDescription(SubWorkMode mode)
        {
            switch (mode)
            {
                case SubWorkMode.ComplexJobsOnly:
                    return "Use Complex Jobs' Work columns without BWT specific-job drilldowns or Fluffy-style expanded columns.";
                case SubWorkMode.FluffyStyleExpansion:
                    return "Keep Complex Jobs' columns and let BWT expand individual jobs beside a selected parent column.";
                default:
                    return "Keep Complex Jobs' columns and let BWT open a selected column's individual jobs in a focused full-tab view.";
            }
        }

        private static bool HasNonDefaultMode(object settingsObject)
        {
            return settingsObject is BetterWorkTabSettings settings &&
                   GetMode(settings) != SubWorkMode.BetterWorkTabFocus;
        }

        private static void ResetMode(object settingsObject)
        {
            ApplyMode(settingsObject as BetterWorkTabSettings, SubWorkMode.BetterWorkTabFocus);
        }

        private sealed class ComplexJobsSettingsContributor : IModSettingsContributor
        {
            public BWTModSettingsSection CreateSettingsSection(SettingsScope<BetterWorkTabSettings> scope)
            {
                return new BWTModSettingsSection { Header = scope
                                                                .Define(CompatComplexJobsHeader, SettingType.Header, "[FSF] Complex Jobs",
                                                                        tooltip: "Choose whether Complex Jobs works alone or with one of BWT's specific-job presentations.")
                                                                .SearchableBy(new[] { "Complex Jobs", "FSF", "sub-work", "specific jobs" })
                                                                .Ordered(16)
                                                                .Accented(new Color(0.65f, 0.78f, 0.9f))
                                                                .ShownWhen(
                                                                    _ => IsActive),
                                                   Children = new List<SettingDefinition> {
                                                       scope
                                                           .Custom(CompatComplexJobsSubWorkMode,
                                                                   (rect, rowLabel, rowTooltip, settings, disabled) => DrawMode(rect, rowLabel, rowTooltip, settings, disabled),
                                                                   "Specific-job columns",
                                                                   tooltip: "Use Complex Jobs alone, add BWT's focused view, or add BWT's Fluffy-style right-expanding columns.")
                                                           .SearchableBy(new[] { "Complex Jobs", "BWT", "Fluffy", "sub-work", "drilldown", "split work types", "extra job columns",
                                                                                 "many work columns", "use BWT drilldown" })
                                                           .Ordered(1)
                                                           .WithCustomReset(HasNonDefaultMode, ResetMode)
                                                   } };
            }
        }
    }
}
