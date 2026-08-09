using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Feedback
{
    /// <summary>
    /// Everything a bug report needs to be actionable without a conversation.
    ///
    /// The old report deliberately omitted the mod list, on the theory that it
    /// was private. For a beta of a Work tab it is the single most useful thing
    /// a tester can send: almost every report that cannot be reproduced comes
    /// down to another mod adding columns, work types, or its own tab. The list
    /// is now included, and the portal says so plainly instead of promising the
    /// opposite.
    /// </summary>
    internal static class BWTBetaEnvironmentReport
    {
        internal const int SettingsDiffCap = 40;

        internal sealed class Line
        {
            internal string Label;
            internal string Value;

            internal Line(string label, string value)
            {
                Label = label;
                Value = value;
            }
        }

        /// <summary>The short block: what the build is and what it is running in.</summary>
        internal static List<Line> Summary()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            WorkGridRendererDiagnosticSnapshot renderer = WorkGridRendererDiagnostics.Current;
            var lines = new List<Line>
            {
                new Line("BWT_Beta_Env_Build".Translate(), BWTBuildInfo.Build + " (" + ShortCommit() + ")"),
                new Line("BWT_Beta_Env_RimWorld".Translate(), VersionControl.CurrentVersionStringWithRev),
                new Line("BWT_Beta_Env_Language".Translate(), LanguageDatabase.activeLanguage?.LegacyFolderName ?? "unknown"),
                new Line("BWT_Beta_Env_Display".Translate(), Display()),
                new Line("BWT_Beta_Env_Mods".Translate(), ModCount()),
                new Line("BWT_Beta_Env_WorkTab".Translate(), WorkTabOwner()),
                new Line("BWT_Beta_Env_Colony".Translate(), Colony()),
                new Line("BWT_Beta_Env_Priorities".Translate(), Priorities(settings)),
                new Line("BWT_Beta_Env_SpecificJobs".Translate(), SpecificJobs(settings)),
                new Line("BWT_Beta_Env_Renderer".Translate(), Renderer(renderer)),
                new Line("BWT_Beta_Env_Compat".Translate(), CompatibilityModules()),
                new Line("BWT_Beta_Env_Changed".Translate(), ChangedSettingsSummary(settings))
            };

            if (MultiplayerBridge.Active)
            {
                lines.Add(new Line("BWT_Beta_Env_Multiplayer".Translate(),
                    MultiplayerBridge.Host ? "hosting" : "client"));
            }

            return lines;
        }

        /// <summary>Active mods, in load order, as "Name (packageId)".</summary>
        internal static List<string> ActiveMods()
        {
            try
            {
                return LoadedModManager.RunningModsListForReading
                    .Select(mod => mod.Name + " (" + mod.PackageIdPlayerFacing + ")")
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Every setting the tester has moved off its default, discovered by
        /// comparing against a freshly constructed settings object rather than a
        /// hand-maintained list — a hand-maintained list is the kind that goes
        /// stale the first time somebody adds a setting.
        /// </summary>
        internal static List<string> ChangedSettings(BetterWorkTabSettings settings)
        {
            var changed = new List<string>();
            if (settings == null)
            {
                return changed;
            }

            try
            {
                var defaults = new BetterWorkTabSettings();
                foreach (FieldInfo field in typeof(BetterWorkTabSettings)
                             .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             .OrderBy(field => field.Name, StringComparer.Ordinal))
                {
                    if (!IsComparable(field) || IsBookkeeping(field.Name))
                    {
                        continue;
                    }

                    object mine = field.GetValue(settings);
                    object theirs = field.GetValue(defaults);
                    if (Equals(mine, theirs))
                    {
                        continue;
                    }

                    changed.Add(field.Name + "=" + Describe(mine));
                }
            }
            catch (Exception exception)
            {
                changed.Add("(settings diff unavailable: " + exception.GetType().Name + ")");
            }

            return changed;
        }

        private static bool IsComparable(FieldInfo field)
        {
            if (field.IsStatic || field.IsLiteral)
            {
                return false;
            }

            Type type = field.FieldType;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Color);
        }

        /// <summary>
        /// Progress counters and feedback text are not settings the tester chose;
        /// listing them would bury the handful of lines that explain a bug.
        /// </summary>
        private static bool IsBookkeeping(string name)
        {
            return name.StartsWith("tutorial", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("beta", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("active", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("last", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("seen", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("has", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("Completed", StringComparison.Ordinal) ||
                   name.EndsWith("Version", StringComparison.Ordinal) ||
                   name.EndsWith("Step", StringComparison.Ordinal);
        }

        private static string Describe(object value)
        {
            if (value is float number)
            {
                return number.ToString("0.###");
            }

            if (value is Color color)
            {
                return ColorUtility.ToHtmlStringRGBA(color);
            }

            string text = Convert.ToString(value) ?? string.Empty;
            return text.Length <= 40 ? text : text.Substring(0, 39) + "…";
        }

        private static string ShortCommit()
        {
            string commit = BWTBuildInfo.SourceCommit ?? string.Empty;
            return commit.Length > 10 ? commit.Substring(0, 10) : commit;
        }

        private static string Display()
        {
            return Screen.width + "x" + Screen.height + " @ " + Prefs.UIScale.ToString("0.##") + "x UI scale";
        }

        private static string ModCount()
        {
            int count = ActiveMods().Count;
            return "BWT_Beta_Env_ModsValue".Translate(count);
        }

        private static string WorkTabOwner()
        {
            if (!FluffyWorkTabGateway.AnyExternalWorkTabPresent)
            {
                return "BWT_Beta_Env_WorkTabBWT".Translate();
            }

            if (FluffyWorkTabGateway.SleekWorkPrioritiesOwnsWorkTab)
            {
                return "Sleek Work Priorities";
            }

            if (FluffyWorkTabGateway.BetterWorkTabHostsSleek)
            {
                return "Better Work Tab + Sleek Work Priorities";
            }

            return FluffyWorkTabGateway.FluffyOwnsWorkTab
                ? "BWT_Beta_Env_WorkTabFluffyOwns".Translate()
                : "BWT_Beta_Env_WorkTabBWTWithFluffy".Translate();
        }

        private static string Colony()
        {
            int colonists = Find.CurrentMap?.mapPawns?.FreeColonistsCount ?? 0;
            int columns = PawnOrganizerSystem.Instance?.Layout?.Columns?.Count ?? 0;
            return "BWT_Beta_Env_ColonyValue".Translate(colonists, columns);
        }

        private static string Priorities(BetterWorkTabSettings settings)
        {
            string mode = Find.PlaySettings != null && !Find.PlaySettings.useWorkPriorities
                ? "BWT_Beta_Env_PrioritiesSimple".Translate().ToString()
                : "BWT_Beta_Env_PrioritiesManual".Translate(settings?.maxPriorityInt ?? DefaultSettings.maxPriority).ToString();
            return mode;
        }

        private static string SpecificJobs(BetterWorkTabSettings settings)
        {
            string style = (settings?.subWorkDrilldownStyle ?? DefaultSettings.subWorkDrilldownStyle).ToString();
            return SubWorkDrilldownState.IsExpandBesideActive
                ? style + ", " + "BWT_Beta_Env_SpecificJobsOpen".Translate()
                : style;
        }

        private static string Renderer(WorkGridRendererDiagnosticSnapshot renderer)
        {
            string fallback = renderer.FallbackReasons.Count == 0
                ? "no fallback"
                : string.Join("; ", renderer.FallbackReasons.Select(reason => reason.Code).ToArray());
            return renderer.ActiveRendererId + " (" + renderer.SelectionMode + ", " + fallback + ")";
        }

        private static string CompatibilityModules()
        {
            IReadOnlyList<string> names = ModSupportManager.GetActiveModuleNames();
            return names == null || names.Count == 0 ? "none" : string.Join(", ", names.ToArray());
        }

        private static string ChangedSettingsSummary(BetterWorkTabSettings settings)
        {
            int count = ChangedSettings(settings).Count;
            return "BWT_Beta_Env_ChangedValue".Translate(count);
        }
    }
}
