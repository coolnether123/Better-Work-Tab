using System;
using Better_Work_Tab.Features.Caching;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Foundation.GameState;
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
using Better_Work_Tab.UI.WorkGrid.Contracts;
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
        private readonly Func<WorkTabApplication> _application;
        private readonly ManualPriorityChromePresentationCache _manualPriorityPresentationCache =
            new ManualPriorityChromePresentationCache();
        private readonly FooterInstructionTextCache _footerInstructionTextCache =
            new FooterInstructionTextCache();

        private static string _cachedUiTextLanguage;
        private static long _cachedUiTextPresentationRevision = long.MinValue;
        private static int _cachedUiTextMaxPriority = -1;
        private static string _manualPrioritiesText;
        private static string _priorityHelpText;
        private static string _higherPriorityText;
        private static string _lowerPriorityText;

        private static string _cachedChromeTextLanguage;
        private static long _cachedChromeTextPresentationRevision = long.MinValue;
        private static string _contextSettingsHintText;
        private static string _manualPriorityPreviewWarningText;
        private static string _cachedSubWorkExitGesture;
        private static string _subWorkExitTooltip;
        private static bool _subWorkExitTooltipValid;

        private static string _cachedCounterLanguage;
        private static long _cachedCounterPresentationRevision = long.MinValue;
        private static float _cachedCounterUiScale;
        private static bool _cachedCounterShowPawns;
        private static bool _cachedCounterShowBeds;
        private static int _cachedCounterPawnCount;
        private static int _cachedCounterBedCount;
        private static string _cachedColonistLabel;
        private static float _cachedColonistWidth;
        private static string _cachedBedLabel;
        private static string _cachedJoinedBedLabel;

        internal WorkTabChrome(
            SubWorkInteractionController subWorkInteractionController,
            Func<WorkTabApplication> application)
        {
            _subWorkInteractionController = subWorkInteractionController ??
                throw new ArgumentNullException(nameof(subWorkInteractionController));
            _application = application ?? throw new ArgumentNullException(nameof(application));
        }

        internal void ReleaseRetainedResources()
        {
            _manualPriorityPresentationCache.ReleaseRetainedResources();
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

        internal void DrawBottomControls(in WorkTabView view)
        {
            IWorkTabLayoutController layout = view.Layout;
            Rect inRect = view.WindowRect;
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.DrawChrome.WorkManager",
                    () => WorkManagerCompatibility.DrawControls(inRect));
                SpineTiming.Time(
                    "WorkTab.DrawChrome.ColorPreview",
                    () => WorkTabColorPreviewRenderer.Draw(layout, inRect));
            }
            else
            {
                WorkManagerCompatibility.DrawControls(inRect);
                WorkTabColorPreviewRenderer.Draw(layout, inRect);
            }

            bool mouseInside = !BWTWorkTabTutorial.OwnsCurrentPointer && Mouse.IsOver(inRect);
            Rect infoRect = WorkTabChromeGeometry.GetInfoIconRect(inRect);
            if (SpineTiming.Enabled)
            {
                WorkTabView profiledView = view;
                SpineTiming.Time(
                    "WorkTab.DrawChrome.BottomRightButtons",
                    () => DrawBottomRightButtons(in profiledView, infoRect));
            }
            else
            {
                DrawBottomRightButtons(in view, infoRect);
            }
            if (mouseInside)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time(
                        "WorkTab.DrawChrome.InfoButton",
                        () => DrawInfoButton(infoRect));
                }
                else
                {
                    DrawInfoButton(infoRect);
                }
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

            EnsureChromeTextCache();
            string gesture = SubWorkDrilldownInput.GestureLabel();
            if (!_subWorkExitTooltipValid ||
                !String.Equals(_cachedSubWorkExitGesture, gesture, StringComparison.Ordinal))
            {
                _cachedSubWorkExitGesture = gesture;
                _subWorkExitTooltip = "BWT_Chrome_BackToWorkTypesTooltip".Translate(gesture);
                _subWorkExitTooltipValid = true;
            }

            TooltipHandler.TipRegion(
                exitRect,
                _subWorkExitTooltip);
        }

        internal void DrawBottomCounters(Rect inRect, PawnTable table)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }
            bool showPawns = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.LayoutPawnCount);
            bool showBeds = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.LayoutBedCount);
            if (!showPawns && !showBeds)
            {
                return;
            }

            int pawnCount = showPawns ? table?.cachedPawns?.Count ?? 0 : 0;

            int bedCount = 0;
            if (showBeds)
            {
                Map map = Find.CurrentMap;
                bedCount = BedCountCache.GetBedCount(map);
            }

            var rect = new Rect(inRect.x + 6f, inRect.yMax - 45f, inRect.width * 0.5f, 20f);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;
            EnsureCounterTextCache(
                showPawns,
                showBeds,
                pawnCount,
                bedCount);

            if (showPawns)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(rect, _cachedColonistLabel);
            }

            if (showBeds)
            {
                string bedLabel = showPawns ? _cachedJoinedBedLabel : _cachedBedLabel;
                float colonistWidth = showPawns ? _cachedColonistWidth : 0f;
                Rect bedRect = new Rect(rect.x + colonistWidth, rect.y, rect.width - colonistWidth, rect.height);

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
            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.UiContextSettingsHint))
            {
                return;
            }

            Rect hintRect = WorkTabChromeGeometry.GetContextSettingsHintRect(inRect);
            EnsureChromeTextCache();
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(1f, 1f, 1f, 0.42f);
            Widgets.Label(hintRect, _contextSettingsHintText);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawManualPrioritiesCheckbox()
        {
            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.UiManualPriorities))
            {
                return;
            }

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Rect rect = WorkTabChromeGeometry.GetManualPrioritiesCheckboxRect();
            int maxPriority = WorkPrioritySystem.GetMaxPriority();
            EnsureUiTextCache(maxPriority);
            bool wasEnabled = WorkTabEffectiveStateRuntime.IsPreviewActive
                ? ParentPriorityRead.GetObservedManualModeForDisplay(
                    true)
                : ParentPriorityRead.GetLiveManualMode(true);
            bool requestedEnabled = wasEnabled;
            HandleManualPrioritiesCheckboxInput(rect, ref requestedEnabled);
            if (wasEnabled != requestedEnabled &&
                !WorkTabEffectiveStateRuntime.TrySetManualMode(
                    requestedEnabled,
                    _application()))
            {
                // A preview mutation that the active provider cannot own is
                // rejected without ever touching PlaySettings.
                requestedEnabled = wasEnabled;
            }

            bool retainedPresentation = DrawManualPrioritiesPresentation(
                rect,
                maxPriority,
                requestedEnabled);
            DrawManualModeInspectionIndicator(rect);

            bool isEnabled = requestedEnabled;
            if (isEnabled)
            {
                if (!retainedPresentation)
                {
                    DrawManualPrioritiesHelp(rect, maxPriority);
                }
            }
            else
            {
                UIHighlighter.HighlightOpportunity(rect, "ManualPriorities-Off");
            }
        }

        private static void DrawManualModeInspectionIndicator(Rect checkboxRect)
        {
            IWorkGridPreviewPort preview = WorkTabEffectiveStateScope.CurrentPreview;
            if (preview == null ||
                !preview.IsActive ||
                !preview.IsInspectionActive ||
                !preview.InspectionHighlightsEnabled ||
                !preview.HasManualModeInspectionChange)
            {
                return;
            }

            float normalizedOpacity = preview.InspectionOpacity;
            GUI.color = new Color(0.95f, 0.70f, 0.25f, 0.9f * normalizedOpacity);
            Widgets.DrawBox(checkboxRect.ExpandedBy(2f), 2);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(
                checkboxRect,
                _manualPriorityPreviewWarningText);
        }

        private static void HandleManualPrioritiesCheckboxInput(
            Rect rect,
            ref bool requestedEnabled)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.ToggleInvisibleDraggable(
                rect,
                ref requestedEnabled,
                true,
                false);
            Text.Anchor = previousAnchor;
        }

        private bool DrawManualPrioritiesPresentation(
            Rect rect,
            int maxPriority,
            bool enabled)
        {
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                if (_manualPriorityPresentationCache.TryDrawRetained(
                    rect,
                    maxPriority,
                    enabled,
                    _manualPrioritiesText,
                    _priorityHelpText))
                {
                    return true;
                }
            }

            DrawManualPrioritiesCheckboxDirect(rect, enabled);
            return false;
        }

        private void DrawManualPrioritiesCheckboxDirect(Rect rect, bool enabled)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = _manualPriorityPresentationCache.GetCheckboxLabelRect(
                rect,
                _manualPrioritiesText);
            Widgets.Label(labelRect, _manualPriorityPresentationCache.CheckboxContent);
            Widgets.CheckboxDraw(
                rect.x + rect.width - 24f,
                rect.y + (rect.height - 24f) / 2f,
                enabled,
                false,
                24f,
                null,
                null);
            Text.Anchor = previousAnchor;
        }

        private void DrawManualPrioritiesHelp(Rect rect, int maxPriority)
        {
            using (new TextBlock(new Color(1f, 1f, 1f, 0.5f)))
            {
                float helpWidth = maxPriority > 4 ? 220f : rect.width;
                Widgets.Label(
                    new Rect(rect.x, rect.yMax - 6f, helpWidth, 60f),
                    _priorityHelpText);
            }
        }

        private void DrawPriorityLegend(Rect rect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.UiPriorityLegend))
            {
                return;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            EnsureUiTextCache(WorkPrioritySystem.GetMaxPriority());
            Rect contextHintRect = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.UiContextSettingsHint)
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

        private void EnsureUiTextCache(int maxPriority)
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            EnsureChromeTextCache();
            if (_cachedUiTextLanguage == language &&
                _cachedUiTextPresentationRevision == presentationRevision &&
                _cachedUiTextMaxPriority == maxPriority)
            {
                return;
            }

            _cachedUiTextLanguage = language;
            _cachedUiTextPresentationRevision = presentationRevision;
            _cachedUiTextMaxPriority = maxPriority;
            _manualPrioritiesText = "ManualPriorities".Translate();
            _priorityHelpText = maxPriority > 4
                ? "BWT_PriorityOneDoneFirstExtended".Translate(maxPriority)
                : "PriorityOneDoneFirst".Translate();
            _higherPriorityText = "<= " + "HigherPriority".Translate();
            _lowerPriorityText = "LowerPriority".Translate() + " =>";
        }

        private void EnsureChromeTextCache()
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            if (_cachedChromeTextLanguage == language &&
                _cachedChromeTextPresentationRevision == presentationRevision)
            {
                return;
            }

            _cachedChromeTextLanguage = language;
            _cachedChromeTextPresentationRevision = presentationRevision;
            _contextSettingsHintText = "BWT_Chrome_AltClickSettings".Translate();
            _manualPriorityPreviewWarningText =
                "BWT_Chrome_ManualPriorityPreviewWarning".Translate();
            _subWorkExitTooltip = null;
            _cachedSubWorkExitGesture = null;
            _subWorkExitTooltipValid = false;
        }

        private static void EnsureCounterTextCache(
            bool showPawns,
            bool showBeds,
            int pawnCount,
            int bedCount)
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            float uiScale = Prefs.UIScale;
            if (_cachedCounterLanguage == language &&
                _cachedCounterPresentationRevision == presentationRevision &&
                _cachedCounterUiScale == uiScale &&
                _cachedCounterShowPawns == showPawns &&
                _cachedCounterShowBeds == showBeds &&
                _cachedCounterPawnCount == pawnCount &&
                _cachedCounterBedCount == bedCount)
            {
                return;
            }

            _cachedCounterLanguage = language;
            _cachedCounterPresentationRevision = presentationRevision;
            _cachedCounterUiScale = uiScale;
            _cachedCounterShowPawns = showPawns;
            _cachedCounterShowBeds = showBeds;
            _cachedCounterPawnCount = pawnCount;
            _cachedCounterBedCount = bedCount;
            _cachedColonistLabel = showPawns
                ? "BWT_Chrome_Colonists".Translate(pawnCount)
                : null;
            _cachedColonistWidth = showPawns && showBeds
                ? Text.CalcSize(_cachedColonistLabel).x
                : 0f;
            _cachedBedLabel = showBeds
                ? "BWT_Chrome_Beds".Translate(bedCount)
                : null;
            _cachedJoinedBedLabel = showPawns && showBeds
                ? " | " + _cachedBedLabel
                : _cachedBedLabel;
        }

        private void DrawBottomRightButtons(
            in WorkTabView view,
            Rect gearRect)
        {
            IWorkTabLayoutController layout = view.Layout;
            Rect inRect = view.WindowRect;
            HeaderButtons.DrawBottomRightGrouped(inRect, gearRect);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.72f);
            Text.Anchor = TextAnchor.LowerLeft;

            var settings = BetterWorkTabMod.Settings;
            if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.UiDragInstructions))
            {
                bool hasOverlayInstruction = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesOverlay);
                bool overlayShifted = hasOverlayInstruction &&
                    ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;
                FooterPointerKind pointerKind = FooterPointerKind.None;
                bool subWorkActive = false;
                string gesture = null;

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
                        pointerKind = FooterPointerKind.CtrlClickSchedule;
                    }
                    else if (SubWorkDrilldownInput.IsEnabled &&
                             !WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked)
                    {
                        bool hasDrilldownAction;
                        subWorkActive = SubWorkDrilldownState.IsActive;
                        if (subWorkActive)
                        {
                            hasDrilldownAction = _subWorkInteractionController.TryGetSubWorkExitTarget(
                                in view,
                                mousePosition,
                                out _,
                                out _);
                        }
                        else
                        {
                            hasDrilldownAction = _subWorkInteractionController.TryGetSubWorkOpenTarget(
                                in view,
                                mousePosition,
                                out _,
                                out _,
                                out _,
                                out _);
                        }

                        if (hasDrilldownAction)
                        {
                            pointerKind = FooterPointerKind.GestureAction;
                            gesture = SubWorkDrilldownInput.GestureLabel();
                        }
                    }
                }

                if (hasOverlayInstruction || pointerKind != FooterPointerKind.None)
                {
                    HeaderButtons.BottomButtonRects buttonRects = HeaderButtons.GetBottomButtonRects(inRect, gearRect);
                    float textRight = buttonRects.LeftEdge - 8f;

                    Rect textRect = new Rect(
                        inRect.x + 6f,
                        inRect.y,
                        Mathf.Max(0f, textRight - inRect.x - 6f),
                        inRect.height);
                    string instructionText = _footerInstructionTextCache.GetInstructionText(
                        hasOverlayInstruction,
                        overlayShifted,
                        pointerKind,
                        subWorkActive,
                        gesture,
                        textRect.width);
                    Widgets.Label(textRect, instructionText);
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
