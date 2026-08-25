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

        private static readonly GUIContent _manualPrioritiesContent = new GUIContent();
        private static string _cachedManualCheckboxText;
        private static float _cachedManualCheckboxUiScale;
        private static Rect _cachedManualCheckboxSourceRect;
        private static Rect _cachedManualCheckboxLabelRect;
        private static bool _manualCheckboxPresentationValid;
        private static RenderTexture _manualPrioritiesSurfaceEnabled;
        private static RenderTexture _manualPrioritiesSurfaceDisabled;
        private static bool _manualPrioritiesSurfaceEnabledValid;
        private static bool _manualPrioritiesSurfaceDisabledValid;
        private static bool _manualPrioritiesSurfaceEnabledFailed;
        private static bool _manualPrioritiesSurfaceDisabledFailed;
        private static string _manualSurfaceLanguage;
        private static long _manualSurfacePresentationRevision = long.MinValue;
        private static string _manualSurfaceText;
        private static string _manualSurfaceHelpText;
        private static int _manualSurfaceMaxPriority = -1;
        private static float _manualSurfaceUiScale;
        private static float _manualSurfacePixelScale;
        private static Rect _manualSurfaceCheckboxRect;
        private static Rect _manualSurfaceContextRect;
        private static int _manualSurfaceFontId;
        private static int _manualSurfaceFontSize;
        private static int _manualSurfaceFontStyle;
        private static bool _manualSurfaceWordWrap;
        private static bool _manualSurfaceKeyValid;

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

        private enum FooterPointerKind
        {
            None,
            CtrlClickSchedule,
            GestureAction
        }

        private static string _cachedFooterLanguage;
        private static long _cachedFooterPresentationRevision = long.MinValue;
        private static bool _cachedFooterOverlayTextValid;
        private static bool _cachedFooterOverlayShifted;
        private static string _cachedFooterOverlayText;
        private static bool _cachedFooterCtrlClickTextValid;
        private static string _cachedFooterCtrlClickText;
        private static bool _cachedFooterActionTextValid;
        private static bool _cachedFooterActionActive;
        private static string _cachedFooterActionText;
        private static bool _cachedFooterGestureTextValid;
        private static string _cachedFooterGestureInput;
        private static string _cachedFooterGestureText;
        private static bool _cachedFooterGestureActionTextValid;
        private static string _cachedFooterGestureActionInput;
        private static string _cachedFooterGestureActionLabelInput;
        private static string _cachedFooterGestureActionText;
        private static string _cachedFooterOverlayInput;
        private static string _cachedFooterPointerInput;
        private static string _cachedFooterJoinedText;
        private static bool _cachedFooterCompositionValid;
        private static float _cachedFooterTruncateWidth;
        private static string _cachedFooterTruncatedText;
        private static bool _cachedFooterTruncateValid;

        internal WorkTabChrome(
            SubWorkInteractionController subWorkInteractionController,
            Func<WorkTabApplication> application)
        {
            _subWorkInteractionController = subWorkInteractionController ??
                throw new ArgumentNullException(nameof(subWorkInteractionController));
            _application = application ?? throw new ArgumentNullException(nameof(application));
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
            EnsureCounterTextCache(
                showPawns,
                showBeds,
                pawnCount,
                bedCount);

            // Draw colonist count in gray.
            if (showPawns)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(rect, _cachedColonistLabel);
            }

            // Draw bed count in red if insufficient, otherwise gray.
            if (showBeds)
            {
                string bedLabel = showPawns ? _cachedJoinedBedLabel : _cachedBedLabel;
                float colonistWidth = showPawns ? _cachedColonistWidth : 0f;
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

        private static bool DrawManualPrioritiesPresentation(
            Rect rect,
            int maxPriority,
            bool enabled)
        {
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                Rect contextRect = WorkTabChromeGeometry.GetManualPrioritiesContextRect();
                EnsureManualSurfaceKey(rect, contextRect, maxPriority);
                RenderTexture surface = GetManualPrioritiesSurface(enabled);
                if (surface != null && surface.IsCreated())
                {
                    GUI.DrawTextureWithTexCoords(
                        contextRect,
                        surface,
                        new Rect(0f, 0f, 1f, 1f),
                        true);
                    return true;
                }
            }

            DrawManualPrioritiesCheckboxDirect(rect, enabled);
            return false;
        }

        private static void DrawManualPrioritiesCheckboxDirect(Rect rect, bool enabled)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = EnsureManualCheckboxPresentation(rect);
            Widgets.Label(labelRect, _manualPrioritiesContent);
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

        private static void DrawManualPrioritiesHelp(Rect rect, int maxPriority)
        {
            using (new TextBlock(new Color(1f, 1f, 1f, 0.5f)))
            {
                float helpWidth = maxPriority > 4 ? 220f : rect.width;
                Widgets.Label(
                    new Rect(rect.x, rect.yMax - 6f, helpWidth, 60f),
                    _priorityHelpText);
            }
        }

        private static void EnsureManualSurfaceKey(
            Rect checkboxRect,
            Rect contextRect,
            int maxPriority)
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            float uiScale = Prefs.UIScale;
            float pixelScale = Verse.UI.screenWidth > 0
                ? Mathf.Max(1f, (float)Screen.width / Verse.UI.screenWidth)
                : 1f;
            GUIStyle fontStyle = Text.CurFontStyle;
            int fontId = fontStyle?.font != null ? fontStyle.font.GetInstanceID() : 0;
            int fontSize = fontStyle?.fontSize ?? 0;
            int fontStyleValue = (int)(fontStyle?.fontStyle ?? FontStyle.Normal);
            bool wordWrap = Text.WordWrap;
            bool changed = !_manualSurfaceKeyValid ||
                _manualSurfaceLanguage != language ||
                _manualSurfacePresentationRevision != presentationRevision ||
                !String.Equals(_manualSurfaceText, _manualPrioritiesText, StringComparison.Ordinal) ||
                !String.Equals(_manualSurfaceHelpText, _priorityHelpText, StringComparison.Ordinal) ||
                _manualSurfaceMaxPriority != maxPriority ||
                _manualSurfaceUiScale != uiScale ||
                _manualSurfacePixelScale != pixelScale ||
                _manualSurfaceFontId != fontId ||
                _manualSurfaceFontSize != fontSize ||
                _manualSurfaceFontStyle != fontStyleValue ||
                _manualSurfaceWordWrap != wordWrap ||
                !SameRect(_manualSurfaceCheckboxRect, checkboxRect) ||
                !SameRect(_manualSurfaceContextRect, contextRect);
            if (!changed)
            {
                return;
            }

            ReleaseManualPrioritiesSurfaces();
            _manualSurfaceLanguage = language;
            _manualSurfacePresentationRevision = presentationRevision;
            _manualSurfaceText = _manualPrioritiesText;
            _manualSurfaceHelpText = _priorityHelpText;
            _manualSurfaceMaxPriority = maxPriority;
            _manualSurfaceUiScale = uiScale;
            _manualSurfacePixelScale = pixelScale;
            _manualSurfaceCheckboxRect = checkboxRect;
            _manualSurfaceContextRect = contextRect;
            _manualSurfaceFontId = fontId;
            _manualSurfaceFontSize = fontSize;
            _manualSurfaceFontStyle = fontStyleValue;
            _manualSurfaceWordWrap = wordWrap;
            _manualSurfaceKeyValid = true;
            _manualPrioritiesSurfaceEnabledFailed = false;
            _manualPrioritiesSurfaceDisabledFailed = false;
        }

        private static RenderTexture GetManualPrioritiesSurface(bool enabled)
        {
            RenderTexture surface = enabled
                ? _manualPrioritiesSurfaceEnabled
                : _manualPrioritiesSurfaceDisabled;
            bool failed = enabled
                ? _manualPrioritiesSurfaceEnabledFailed
                : _manualPrioritiesSurfaceDisabledFailed;
            if (failed)
            {
                return null;
            }

            int pixelWidth = Mathf.Max(
                1,
                Mathf.CeilToInt(_manualSurfaceContextRect.width * _manualSurfacePixelScale));
            int pixelHeight = Mathf.Max(
                1,
                Mathf.CeilToInt(_manualSurfaceContextRect.height * _manualSurfacePixelScale));
            if (surface == null ||
                !surface.IsCreated() ||
                surface.width != pixelWidth ||
                surface.height != pixelHeight)
            {
                ReleaseManualPrioritiesSurface(enabled);
                surface = CreateManualPrioritiesSurface(pixelWidth, pixelHeight);
                if (enabled)
                {
                    _manualPrioritiesSurfaceEnabled = surface;
                }
                else
                {
                    _manualPrioritiesSurfaceDisabled = surface;
                }
            }

            if (surface == null)
            {
                if (enabled)
                {
                    _manualPrioritiesSurfaceEnabledFailed = true;
                }
                else
                {
                    _manualPrioritiesSurfaceDisabledFailed = true;
                }

                return null;
            }

            bool valid = enabled
                ? _manualPrioritiesSurfaceEnabledValid
                : _manualPrioritiesSurfaceDisabledValid;
            if (valid)
            {
                return surface;
            }

            if (!BuildManualPrioritiesSurface(surface, enabled))
            {
                ReleaseManualPrioritiesSurface(enabled);
                if (enabled)
                {
                    _manualPrioritiesSurfaceEnabledFailed = true;
                }
                else
                {
                    _manualPrioritiesSurfaceDisabledFailed = true;
                }

                return null;
            }

            if (enabled)
            {
                _manualPrioritiesSurfaceEnabledValid = true;
            }
            else
            {
                _manualPrioritiesSurfaceDisabledValid = true;
            }

            return surface;
        }

        private static RenderTexture CreateManualPrioritiesSurface(int width, int height)
        {
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            {
                return null;
            }

            var surface = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "BWT manual priorities chrome",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!surface.Create())
            {
                UnityEngine.Object.Destroy(surface);
                return null;
            }

            return surface;
        }

        private static bool BuildManualPrioritiesSurface(
            RenderTexture surface,
            bool enabled)
        {
            RenderTexture previousTarget = RenderTexture.active;
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;
            try
            {
                RenderTexture.active = surface;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(
                        0f,
                        _manualSurfaceContextRect.width,
                        _manualSurfaceContextRect.height,
                        0f);
                    GL.Clear(true, true, Color.clear);
                    GUI.BeginGroup(new Rect(
                        0f,
                        0f,
                        _manualSurfaceContextRect.width,
                        _manualSurfaceContextRect.height));
                    try
                    {
                        Text.Font = GameFont.Small;
                        Text.Anchor = TextAnchor.UpperLeft;
                        Text.WordWrap = previousWrap;
                        GUI.color = Color.white;
                        Rect localLabelRect = EnsureManualCheckboxPresentation(
                            _manualSurfaceCheckboxRect);
                        localLabelRect.x -= _manualSurfaceContextRect.x;
                        localLabelRect.y -= _manualSurfaceContextRect.y;
                        Widgets.Label(localLabelRect, _manualPrioritiesContent);
                        float localCheckboxX =
                            _manualSurfaceCheckboxRect.x +
                            _manualSurfaceCheckboxRect.width - 24f -
                            _manualSurfaceContextRect.x;
                        float localCheckboxY =
                            _manualSurfaceCheckboxRect.y +
                            (_manualSurfaceCheckboxRect.height - 24f) / 2f -
                            _manualSurfaceContextRect.y;
                        Widgets.CheckboxDraw(
                            localCheckboxX,
                            localCheckboxY,
                            enabled,
                            false,
                            24f,
                            null,
                            null);
                        if (enabled)
                        {
                            using (new TextBlock(new Color(1f, 1f, 1f, 0.5f)))
                            {
                                float helpWidth = _manualSurfaceMaxPriority > 4
                                    ? 220f
                                    : _manualSurfaceCheckboxRect.width;
                                Rect helpRect = new Rect(
                                    _manualSurfaceCheckboxRect.x - _manualSurfaceContextRect.x,
                                    _manualSurfaceCheckboxRect.y +
                                        _manualSurfaceCheckboxRect.height - 6f -
                                        _manualSurfaceContextRect.y,
                                    helpWidth,
                                    60f);
                                Widgets.Label(helpRect, _priorityHelpText);
                            }
                        }
                    }
                    finally
                    {
                        GUI.EndGroup();
                    }
                }
                finally
                {
                    GL.PopMatrix();
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWrap;
                RenderTexture.active = previousTarget;
            }
        }

        private static void ReleaseManualPrioritiesSurface(bool enabled)
        {
            RenderTexture surface = enabled
                ? _manualPrioritiesSurfaceEnabled
                : _manualPrioritiesSurfaceDisabled;
            if (enabled)
            {
                _manualPrioritiesSurfaceEnabledValid = false;
            }
            else
            {
                _manualPrioritiesSurfaceDisabledValid = false;
            }
            if (surface == null)
            {
                return;
            }

            surface.Release();
            UnityEngine.Object.Destroy(surface);
            if (enabled)
            {
                _manualPrioritiesSurfaceEnabled = null;
            }
            else
            {
                _manualPrioritiesSurfaceDisabled = null;
            }
        }

        private static void ReleaseManualPrioritiesSurfaces()
        {
            ReleaseManualPrioritiesSurface(true);
            ReleaseManualPrioritiesSurface(false);
        }

        internal static void ReleaseRetainedResources()
        {
            ReleaseManualPrioritiesSurfaces();
            _manualSurfaceKeyValid = false;
            _manualPrioritiesSurfaceEnabledFailed = false;
            _manualPrioritiesSurfaceDisabledFailed = false;
        }

        private static bool SameRect(Rect left, Rect right)
        {
            return left.x == right.x &&
                   left.y == right.y &&
                   left.width == right.width &&
                   left.height == right.height;
        }

        private static Rect EnsureManualCheckboxPresentation(Rect rect)
        {
            float uiScale = Prefs.UIScale;
            if (!_manualCheckboxPresentationValid ||
                !String.Equals(
                    _cachedManualCheckboxText,
                    _manualPrioritiesText,
                    StringComparison.Ordinal) ||
                _cachedManualCheckboxUiScale != uiScale ||
                _cachedManualCheckboxSourceRect.x != rect.x ||
                _cachedManualCheckboxSourceRect.y != rect.y ||
                _cachedManualCheckboxSourceRect.width != rect.width ||
                _cachedManualCheckboxSourceRect.height != rect.height)
            {
                Rect labelRect = rect;
                labelRect.xMax -= 24f;
                if (uiScale > 1f)
                {
                    float halfScale = uiScale / 2f;
                    if (Math.Abs(halfScale - Math.Floor(halfScale)) > float.Epsilon)
                    {
                        labelRect = LudeonTK.UIScaling.AdjustRectToUIScaling(labelRect);
                    }
                }

                _cachedManualCheckboxText = _manualPrioritiesText;
                _manualPrioritiesContent.text = _manualPrioritiesText;
                _cachedManualCheckboxUiScale = uiScale;
                _cachedManualCheckboxSourceRect = rect;
                _cachedManualCheckboxLabelRect = labelRect;
                _manualCheckboxPresentationValid = true;
            }

            return _cachedManualCheckboxLabelRect;
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

        private static void EnsureUiTextCache(int maxPriority)
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

        private static void EnsureChromeTextCache()
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
            _manualCheckboxPresentationValid = false;
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
                    string instructionText = GetFooterInstructionText(
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

        private static string GetFooterInstructionText(
            bool hasOverlayInstruction,
            bool overlayShifted,
            FooterPointerKind pointerKind,
            bool subWorkActive,
            string gesture,
            float textWidth)
        {
            EnsureFooterTextCache();

            string overlayText = null;
            if (hasOverlayInstruction)
            {
                if (!_cachedFooterOverlayTextValid ||
                    _cachedFooterOverlayShifted != overlayShifted)
                {
                    _cachedFooterOverlayShifted = overlayShifted;
                    _cachedFooterOverlayText = overlayShifted
                        ? "BWT_Footer_ReleaseShiftForPriorities".Translate()
                        : "BWT_Footer_HoldShiftForSkills".Translate();
                    _cachedFooterOverlayTextValid = true;
                }

                overlayText = _cachedFooterOverlayText;
            }

            string pointerText = null;
            switch (pointerKind)
            {
                case FooterPointerKind.CtrlClickSchedule:
                    if (!_cachedFooterCtrlClickTextValid)
                    {
                        _cachedFooterCtrlClickText = "BWT_Footer_CtrlClickSchedule".Translate();
                        _cachedFooterCtrlClickTextValid = true;
                    }

                    pointerText = _cachedFooterCtrlClickText;
                    break;
                case FooterPointerKind.GestureAction:
                    string actionText = EnsureFooterActionText(subWorkActive);
                    string gestureText = EnsureFooterGestureText(gesture);
                    if (!_cachedFooterGestureActionTextValid ||
                        !String.Equals(
                            _cachedFooterGestureActionInput,
                            gestureText,
                            StringComparison.Ordinal) ||
                        !String.Equals(
                            _cachedFooterGestureActionLabelInput,
                            actionText,
                            StringComparison.Ordinal))
                    {
                        _cachedFooterGestureActionInput = gestureText;
                        _cachedFooterGestureActionLabelInput = actionText;
                        _cachedFooterGestureActionText = "BWT_Footer_GestureAction".Translate(
                            gestureText,
                            actionText);
                        _cachedFooterGestureActionTextValid = true;
                    }

                    pointerText = _cachedFooterGestureActionText;
                    break;
            }

            if (!_cachedFooterCompositionValid ||
                !String.Equals(
                    _cachedFooterOverlayInput,
                    overlayText,
                    StringComparison.Ordinal) ||
                !String.Equals(
                    _cachedFooterPointerInput,
                    pointerText,
                    StringComparison.Ordinal))
            {
                _cachedFooterOverlayInput = overlayText;
                _cachedFooterPointerInput = pointerText;
                _cachedFooterJoinedText = overlayText == null
                    ? pointerText
                    : pointerText == null
                        ? overlayText
                        : overlayText + " | " + pointerText;
                _cachedFooterCompositionValid = true;
                _cachedFooterTruncateValid = false;
            }

            if (_cachedFooterJoinedText == null)
            {
                return null;
            }

            float truncateWidth = Mathf.Max(1f, textWidth);
            if (!_cachedFooterTruncateValid || _cachedFooterTruncateWidth != truncateWidth)
            {
                _cachedFooterTruncateWidth = truncateWidth;
                _cachedFooterTruncatedText = _cachedFooterJoinedText.Truncate(truncateWidth);
                _cachedFooterTruncateValid = true;
            }

            return _cachedFooterTruncatedText;
        }

        private static string EnsureFooterActionText(bool subWorkActive)
        {
            if (!_cachedFooterActionTextValid || _cachedFooterActionActive != subWorkActive)
            {
                _cachedFooterActionActive = subWorkActive;
                _cachedFooterActionText = subWorkActive
                    ? "BWT_Footer_BackToWorkTypes".Translate()
                    : "BWT_Footer_OpenSpecificJobs".Translate();
                _cachedFooterActionTextValid = true;
            }

            return _cachedFooterActionText;
        }

        private static string EnsureFooterGestureText(string gesture)
        {
            if (!_cachedFooterGestureTextValid ||
                !String.Equals(_cachedFooterGestureInput, gesture, StringComparison.Ordinal))
            {
                _cachedFooterGestureInput = gesture;
                _cachedFooterGestureText = gesture.CapitalizeFirst();
                _cachedFooterGestureTextValid = true;
            }

            return _cachedFooterGestureText;
        }

        private static void EnsureFooterTextCache()
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            if (_cachedFooterLanguage == language &&
                _cachedFooterPresentationRevision == presentationRevision)
            {
                return;
            }

            _cachedFooterLanguage = language;
            _cachedFooterPresentationRevision = presentationRevision;
            _cachedFooterOverlayTextValid = false;
            _cachedFooterCtrlClickTextValid = false;
            _cachedFooterActionTextValid = false;
            _cachedFooterGestureTextValid = false;
            _cachedFooterGestureActionTextValid = false;
            _cachedFooterCompositionValid = false;
            _cachedFooterTruncateValid = false;
            _cachedFooterOverlayInput = null;
            _cachedFooterPointerInput = null;
            _cachedFooterJoinedText = null;
            _cachedFooterGestureActionInput = null;
            _cachedFooterGestureActionLabelInput = null;
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
