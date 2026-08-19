using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.Caching;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.ModSupport.Mods.WorkManager;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using Spine.Profiling;
using Spine.UI.WidgetExtensions;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Chrome
{
    /// <summary>
    /// Owns the Work tab's non-grid controls. The window keeps the explicit
    /// frame sequence; this class owns the state, geometry, and IMGUI details
    /// for each chrome phase.
    /// </summary>
    internal sealed class WorkTabChrome
    {
        private readonly SubWorkInteractionController _subWorkInteractionController;

        private static string _cachedUiTextLanguage;
        private static int _cachedUiTextMaxPriority = -1;
        private static string _manualPrioritiesText;
        private static string _priorityHelpText;
        private static string _higherPriorityText;
        private static string _lowerPriorityText;

        internal WorkTabChrome(SubWorkInteractionController subWorkInteractionController)
        {
            _subWorkInteractionController = subWorkInteractionController ??
                throw new ArgumentNullException(nameof(subWorkInteractionController));
        }

        internal void DrawTopControls(IWorkTabLayoutController layout, Rect inRect)
        {
            if (SpineTiming.Enabled)
            {
                if (SleekWorkTabGateway.BetterWorkTabHostsSleek)
                {
                    SpineTiming.Time("WorkTab.DrawMixedSleekToolbar", () => SleekWorkTabGateway.DrawMixedToolbarExtras(inRect));
                }
                else
                {
                    SpineTiming.Time("WorkTab.DrawExternalWorkTabSwitch", () => FluffyWorkTabGateway.DrawWorkTabSwitchButton(inRect));
                    SpineTiming.Time("WorkTab.DrawFluffyStyleTopButtons", () => HeaderButtons.DrawTopRightFluffyStyle(layout, inRect));
                    DrawPriorityControls(inRect);
                }

                if (!SleekWorkTabGateway.BetterWorkTabHostsSleek)
                {
                    SpineTiming.Time("WorkTab.DrawContextSettingsHint", () => DrawContextSettingsHint(inRect));
                }

                return;
            }

            if (SleekWorkTabGateway.BetterWorkTabHostsSleek)
            {
                SleekWorkTabGateway.DrawMixedToolbarExtras(inRect);
            }
            else
            {
                FluffyWorkTabGateway.DrawWorkTabSwitchButton(inRect);
                HeaderButtons.DrawTopRightFluffyStyle(layout, inRect);
                DrawPriorityControls(inRect);
            }

            if (!SleekWorkTabGateway.BetterWorkTabHostsSleek)
            {
                DrawContextSettingsHint(inRect);
            }
        }

        internal void DrawPriorityControls(Rect rect)
        {
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawManualPrioritiesCheckbox", DrawManualPrioritiesCheckbox);
                SpineTiming.Time("WorkTab.DrawPriorityLegend", () => DrawPriorityLegend(rect));
                return;
            }

            DrawManualPrioritiesCheckbox();
            DrawPriorityLegend(rect);
        }

        internal void DrawBottomControls(IWorkTabLayoutController layout, Rect inRect)
        {
            WorkManagerCompatibility.DrawControls(inRect);
            WorkTabColorPreviewRenderer.Draw(layout, inRect);

            bool mouseInside = !BWTWorkTabTutorial.OwnsCurrentPointer && Mouse.IsOver(inRect);
            Rect infoRect = WorkTabChromeGeometry.GetInfoIconRect(inRect);
            DrawBottomRightButtons(layout, inRect, infoRect);
            if (mouseInside)
            {
                DrawInfoButton(infoRect);
            }
        }

        internal void DrawSubWorkExitButton(Rect inRect)
        {
            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked ||
                !SubWorkDrilldownState.IsActive)
            {
                return;
            }

            const float buttonSize = 24f;
            float topRightReservedWidth = HeaderButtons.GetTopRightReservedWidth();
            Rect exitRect = new Rect(
                inRect.xMax - buttonSize - WorkTabChromeGeometry.RightEdgeMargin - topRightReservedWidth,
                inRect.y + 8f,
                buttonSize,
                buttonSize);

            if (Widgets.ButtonImage(exitRect, TexButton.CloseXSmall, Color.white, GenUI.MouseoverColor))
            {
                _subWorkInteractionController.TryExitSubWorkMode(restoreMousePosition: false);
            }

            TooltipHandler.TipRegion(
                exitRect,
                "Back to work types. " + SubWorkDrilldownInput.GestureLabel() +
                " or press Escape to return.");
        }

        internal void DrawBottomCounters(Rect inRect, PawnTable table)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }
            bool showPawns = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.LayoutPawnCount,
                settings.showPawnCountAtBottom);
            bool showBeds = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.LayoutBedCount,
                settings.showBedCountAtBottom);
            if (!showPawns && !showBeds)
            {
                return;
            }

            int pawnCount = showPawns ? table?.cachedPawns?.Count ?? 0 : 0;

            // Use cached bed count instead of calculating every frame.
            int bedCount = 0;
            if (showBeds)
            {
                Map map = Find.CurrentMap;
                // Cached lookup: invalidated via Harmony patches and time-based expiry.
                bedCount = BedCountCache.GetBedCount(map);
            }

            var rect = new Rect(inRect.x + 6f, inRect.yMax - 45f, inRect.width * 0.5f, 20f);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;

            // Draw colonist count in gray.
            if (showPawns)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(rect, $"Colonists: {pawnCount}");
            }

            // Draw bed count in red if insufficient, otherwise gray.
            if (showBeds)
            {
                string bedLabel = showPawns ? $" | Beds: {bedCount}" : $"Beds: {bedCount}";
                float colonistWidth = showPawns ? Text.CalcSize($"Colonists: {pawnCount}").x : 0f;
                Rect bedRect = new Rect(rect.x + colonistWidth, rect.y, rect.width - colonistWidth, rect.height);

                // Red if fewer beds than pawns, gray otherwise.
                if (bedCount < pawnCount)
                {
                    GUI.color = new Color(0.8f, 0.1f, 0.1f);
                }
                else
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.7f);
                }

                Widgets.Label(bedRect, bedLabel);
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        internal void DrawContextSettingsHint(Rect inRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.UiContextSettingsHint,
                    settings?.showContextSettingsHint ?? true))
            {
                return;
            }

            Rect hintRect = WorkTabChromeGeometry.GetContextSettingsHintRect(inRect);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(1f, 1f, 1f, 0.42f);
            Widgets.Label(hintRect, "Alt + click anywhere for settings");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawManualPrioritiesCheckbox()
        {
            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.UiManualPriorities,
                    settings?.showManualPrioritiesCheckbox ?? true))
            {
                return;
            }

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Rect rect = new Rect(5f, 5f, 140f, 30f);
            int maxPriority = WorkPrioritySystem.GetMaxPriority();
            EnsureUiTextCache(maxPriority);
            bool wasEnabled = WorkTabEffectiveStateRuntime.IsPreviewActive
                ? WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                    Find.PlaySettings?.useWorkPriorities ?? true)
                : Current.Game.playSettings.useWorkPriorities;
            bool requestedEnabled = wasEnabled;
            Widgets.CheckboxLabeled(rect, _manualPrioritiesText, ref requestedEnabled);
            if (wasEnabled != requestedEnabled &&
                !WorkTabEffectiveStateRuntime.TrySetManualMode(requestedEnabled))
            {
                // A preview mutation that the active provider cannot own is
                // rejected without ever touching PlaySettings.
                requestedEnabled = wasEnabled;
            }

            bool isEnabled = WorkTabEffectiveStateRuntime.IsPreviewActive
                ? requestedEnabled
                : Current.Game.playSettings.useWorkPriorities;
            if (isEnabled)
            {
                using (new TextBlock(new Color(1f, 1f, 1f, 0.5f)))
                {
                    float helpWidth = maxPriority > 4 ? 220f : rect.width;
                    Widgets.Label(new Rect(rect.x, rect.yMax - 6f, helpWidth, 60f), _priorityHelpText);
                }
            }
            else
            {
                UIHighlighter.HighlightOpportunity(rect, "ManualPriorities-Off");
            }
        }

        private void DrawPriorityLegend(Rect rect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.UiPriorityLegend,
                    settings?.showPriorityLegend ?? true))
            {
                return;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            EnsureUiTextCache(WorkPrioritySystem.GetMaxPriority());
            Rect contextHintRect = BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.UiContextSettingsHint,
                    settings?.showContextSettingsHint ?? true)
                ? WorkTabChromeGeometry.GetContextSettingsHintRect(rect)
                : Rect.zero;
            if (contextHintRect.width > 0f)
            {
                float legendLeft = rect.x + 370f;
                float legendRight = contextHintRect.xMin - 8f;
                float laneWidth = Mathf.Min(160f, Mathf.Max(0f, (legendRight - legendLeft) / 2f));
                if (laneWidth >= 70f)
                {
                    Widgets.Label(new Rect(legendLeft, rect.y + 5f, laneWidth, 30f), _higherPriorityText);
                    Widgets.Label(new Rect(legendLeft + laneWidth, rect.y + 5f, laneWidth, 30f), _lowerPriorityText);
                }
            }
            else
            {
                Widgets.Label(new Rect(370f, rect.y + 5f, 160f, 30f), _higherPriorityText);
                Widgets.Label(new Rect(630f, rect.y + 5f, 160f, 30f), _lowerPriorityText);
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void EnsureUiTextCache(int maxPriority)
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            if (_cachedUiTextLanguage == language && _cachedUiTextMaxPriority == maxPriority)
            {
                return;
            }

            _cachedUiTextLanguage = language;
            _cachedUiTextMaxPriority = maxPriority;
            _manualPrioritiesText = "ManualPriorities".Translate();
            _priorityHelpText = maxPriority > 4
                ? "BWT_PriorityOneDoneFirstExtended".Translate(maxPriority)
                : "PriorityOneDoneFirst".Translate();
            _higherPriorityText = "<= " + "HigherPriority".Translate();
            _lowerPriorityText = "LowerPriority".Translate() + " =>";
        }

        private void DrawBottomRightButtons(
            IWorkTabLayoutController layout,
            Rect inRect,
            Rect gearRect)
        {
            HeaderButtons.DrawBottomRightGrouped(inRect, gearRect);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.72f);
            Text.Anchor = TextAnchor.LowerLeft;

            var settings = BetterWorkTabMod.Settings;
            if (BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.UiDragInstructions,
                    settings?.showDragInstructions ?? DefaultSettings.showDragInstructions))
            {
                var instructions = new List<string>();
                if (BWTWorkTabEffectiveSettings.GetBool(
                        SettingIDs.FeaturesOverlay,
                        settings?.enableSkillOverlayFeature ?? DefaultSettings.enableSkillOverlayFeature))
                {
                    instructions.Add(
                        ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted
                            ? "BWT_Footer_ReleaseShiftForPriorities".Translate()
                            : "BWT_Footer_HoldShiftForSkills".Translate());
                }

                bool pointerAvailable = layout != null &&
                                        Event.current != null &&
                                        Mouse.IsOver(inRect) &&
                                        !BWTWorkTabTutorial.OwnsCurrentPointer &&
                                        !RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab &&
                                        !FluffyTimeScheduleAssigner.IsOpen &&
                                        !(PawnOrganizerSystem.Instance?.IsDragging ?? false);
                if (pointerAvailable)
                {
                    Vector2 mousePosition = Event.current.mousePosition;
                    if (TimePriorityScheduleEditor.HasToggleTargetAt(layout, mousePosition))
                    {
                        instructions.Add("BWT_Footer_CtrlClickSchedule".Translate());
                    }
                    else if (SubWorkDrilldownInput.IsEnabled &&
                             !WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked)
                    {
                        bool hasDrilldownAction;
                        string action;
                        if (SubWorkDrilldownState.IsActive)
                        {
                            hasDrilldownAction = _subWorkInteractionController.TryGetSubWorkExitTarget(
                                layout,
                                mousePosition,
                                out _,
                                out _);
                            action = "BWT_Footer_BackToWorkTypes".Translate();
                        }
                        else
                        {
                            hasDrilldownAction = _subWorkInteractionController.TryGetSubWorkOpenTarget(
                                layout,
                                mousePosition,
                                out _,
                                out _,
                                out _,
                                out _);
                            action = "BWT_Footer_OpenSpecificJobs".Translate();
                        }

                        if (hasDrilldownAction)
                        {
                            instructions.Add(
                                "BWT_Footer_GestureAction".Translate(
                                    SubWorkDrilldownInput.GestureLabel().CapitalizeFirst(),
                                    action));
                        }
                    }
                }

                if (instructions.Count > 0)
                {
                    HeaderButtons.BottomButtonRects buttonRects = HeaderButtons.GetBottomButtonRects(inRect, gearRect);
                    float textRight = buttonRects.LeftEdge - 8f;

                    Rect textRect = new Rect(
                        inRect.x + 6f,
                        inRect.y,
                        Mathf.Max(0f, textRight - inRect.x - 6f),
                        inRect.height);
                    Widgets.Label(
                        textRect,
                        string.Join(" | ", instructions).Truncate(Mathf.Max(1f, textRect.width)));
                }
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawInfoButton(Rect gearRect)
        {
            if (Widgets.ButtonImage(gearRect, TexButton.Info))
            {
                BetterWorkTabSettingsWindowService.Open();
            }
        }
    }
}
