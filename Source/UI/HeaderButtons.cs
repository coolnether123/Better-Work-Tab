using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI
{
    // Single-purpose file: draw grouped header buttons anchored to the
    // bottom-right (immediately left of the small info/gear button).
    public static class HeaderButtons
    {
        private const float SelectorHeight = BWTBottomBarSelector.Height;
        internal const float SelectorMenuWidth = BWTBottomBarSelector.MenuWidth;
        internal const float GroupGap = 6f;
        private const float InterControlGap = 6f;
        private const float FluffyTopButtonSize = 30f;
        private const float FluffyTopButtonGap = 4f;
        internal const float PreferredSelectorMainWidth = 150f;

        // Strict Sleek renders its PawnTable against a different bottom-room
        // contract than BWT's host. The table needs only this small upward
        // shift to clear the footer controls; BWT's own host already inherits
        // vanilla's bottom room and must not stack the full bar height again.
        internal const float StrictSleekBottomTableShift = 12f;

        public struct BottomButtonRects
        {
            public Rect RulesetMain;
            public Rect RulesetMenu;
            public Rect OptionalMain;
            public Rect OptionalMenu;
            public Rect OptionalSaveAs;
            public Rect OptionalUpdate;
            public Rect OptionalCancel;
            public Rect OptionalApply;
            public bool HasRuleset;
            public bool HasOptional;
            public bool HasOptionalMenu;
            public bool HasOptionalPreview;
            public bool HasOptionalSaveAs;
            public bool HasOptionalUpdate;
            public bool CompactOptionalMain;

            /// <summary>
            /// The left edge reserved for the row. During a reveal this remains
            /// at the final layout edge so adjacent footer text stays stable.
            /// </summary>
            public float LeftEdge;

            public bool ContainsRuleset(Vector2 position)
            {
                return HasRuleset && (RulesetMain.Contains(position) || RulesetMenu.Contains(position));
            }

            public bool ContainsOptional(Vector2 position)
            {
                return HasOptional &&
                       (OptionalMain.Contains(position) ||
                        (HasOptionalMenu && OptionalMenu.Contains(position)));
            }

            public bool ContainsOptionalPreviewActions(Vector2 position)
            {
                return HasOptionalPreview &&
                       ((HasOptionalSaveAs && OptionalSaveAs.Contains(position)) ||
                        (HasOptionalUpdate && OptionalUpdate.Contains(position)) ||
                        OptionalCancel.Contains(position) ||
                        OptionalApply.Contains(position));
            }

            public bool ContainsOptionalFooter(Vector2 position)
            {
                return ContainsOptional(position) || ContainsOptionalPreviewActions(position);
            }
        }

        private struct TopButtonRects
        {
            public Rect Priority;
            public Rect Scheduler;
            public Rect Expand;
        }

        /// <summary>
        /// The single description of the bottom-right row. Both the painter and
        /// every hit test read it, so a control cannot be drawn in one place and
        /// clicked in another.
        /// </summary>

        public static BottomButtonRects GetBottomButtonRects(Rect inRect, Rect gearRect)
        {
            BottomButtonRects rects = new BottomButtonRects();

            float rowLeft = Mathf.Min(inRect.xMin, inRect.xMax);
            float rowRight = Mathf.Max(inRect.xMin, inRect.xMax);
            float rowHeight = Mathf.Min(SelectorHeight, Mathf.Max(0f, inRect.height));
            if (rowHeight <= 0f || rowRight <= rowLeft)
            {
                rects.LeftEdge = rowLeft;
                return rects;
            }

            float y = Mathf.Clamp(
                gearRect.yMax - rowHeight,
                inRect.yMin,
                Mathf.Max(inRect.yMin, inRect.yMax - rowHeight));
            float xRight = Mathf.Clamp(
                gearRect.x - InterControlGap,
                rowLeft,
                rowRight);

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            bool allowRuleset = settings?.enableAutoAssignFeature ?? true;
            float rulesetWidth = allowRuleset
                ? MeasureSelectorWidth(RuleBuilderGateway.CurrentRulesetLabel())
                : 0f;
            HeaderFooterLayoutContext context = new HeaderFooterLayoutContext(
                rowLeft,
                xRight,
                y,
                rowHeight,
                allowRuleset,
                rulesetWidth);
            IHeaderFooterFeature feature = HeaderFooterFeatureRegistry.Current;
            if (feature != null && feature.TryLayout(context, ref rects))
            {
                return rects;
            }

            LayoutRulesetOnly(context, ref rects);
            return rects;
        }

        private static void LayoutRulesetOnly(
            HeaderFooterLayoutContext context,
            ref BottomButtonRects rects)
        {
            float available = Mathf.Max(0f, context.RightEdge - context.LeftEdge);
            if (available <= 0f)
            {
                rects.LeftEdge = context.LeftEdge;
                return;
            }

            float rulesetWidth = Mathf.Min(
                context.RulesetWidth,
                available - SelectorMenuWidth);
            bool showRuleset = context.AllowRuleset && rulesetWidth > 0f;
            float x = context.RightEdge;
            if (showRuleset)
            {
                rects.RulesetMenu = TakeFromRight(
                    ref x,
                    context.LeftEdge,
                    SelectorMenuWidth,
                    context.Y,
                    context.Height);
                rects.RulesetMain = TakeFromRight(
                    ref x,
                    context.LeftEdge,
                    rulesetWidth,
                    context.Y,
                    context.Height);
                rects.HasRuleset = rects.RulesetMain.width > 0f &&
                    rects.RulesetMenu.width > 0f;
                x -= GroupGap;
            }

            rects.LeftEdge = Mathf.Max(context.LeftEdge, x);
        }

        internal static float MeasureSelectorWidth(string value)
        {
            return Mathf.Max(
                BWTBottomBarSelector.MeasureWidth(value),
                PreferredSelectorMainWidth);
        }

        internal static Rect TakeFromRight(
            ref float rightEdge,
            float leftEdge,
            float width,
            float y,
            float height)
        {
            rightEdge = Mathf.Max(leftEdge, rightEdge);
            float safeWidth = Mathf.Min(
                Mathf.Max(0f, width),
                rightEdge - leftEdge);
            Rect rect = new Rect(rightEdge - safeWidth, y, safeWidth, height);
            rightEdge = rect.xMin;
            return rect;
        }

        public static bool TryHandleTopRightFluffyStyleInput(
            IWorkTabLayoutController layout,
            Rect inRect,
            Event evt)
        {
            if (!ShouldShowTopRightFluffyStyle() ||
                evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0)
            {
                return false;
            }

            TopButtonRects rects = GetTopButtonRects(inRect);
            if (rects.Priority.Contains(evt.mousePosition))
            {
                bool prioritiesEnabled = ParentPriorityRead.GetObservedManualModeForDisplay(
                    true);
                ToggleManualPriorities(!prioritiesEnabled);
                evt.Use();
                return true;
            }

            if (rects.Scheduler.Contains(evt.mousePosition))
            {
                if (!ToggleScheduler(layout))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }

                evt.Use();
                return true;
            }

            if (rects.Expand.Contains(evt.mousePosition))
            {
                ToggleAllVisibleSubWork(layout);
                evt.Use();
                return true;
            }

            return false;
        }

        public static void DrawTopRightFluffyStyle(IWorkTabLayoutController layout, Rect inRect)
        {
            if (!ShouldShowTopRightFluffyStyle())
            {
                return;
            }

            TopButtonRects rects = GetTopButtonRects(inRect);
            bool prioritiesEnabled = ParentPriorityRead.GetObservedManualModeForDisplay(
                true);
            if (DrawFluffyTopButton(
                    rects.Priority,
                    prioritiesEnabled ? FluffyWorkTabIcon.PrioritiesDetailed : FluffyWorkTabIcon.PrioritiesSimple,
                    prioritiesEnabled
                        ? "BWT_Header_ManualPriorities".Translate().ToString()
                        : "BWT_Header_SimplePriorities".Translate().ToString(),
                    prioritiesEnabled ? "1" : "Y"))
            {
                ToggleManualPriorities(!prioritiesEnabled);
            }

            bool plannerVisible = FluffyTimeScheduleAssigner.IsOpen || TimePriorityScheduleEditor.IsVisible;
            if (DrawFluffyTopButton(
                    rects.Scheduler,
                    plannerVisible ? FluffyWorkTabIcon.PrioritiesTimed : FluffyWorkTabIcon.PrioritiesWholeDay,
                    plannerVisible
                        ? "BWT_Header_CloseHourlyPriorities".Translate().ToString()
                        : "BWT_Header_OpenHourlyPriorities".Translate().ToString(),
                    plannerVisible ? "T" : "D"))
            {
                if (!ToggleScheduler(layout))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
            }

            bool anyExpanded = SubWorkDrilldownState.IsExpandBesideActive;
            if (DrawFluffyTopButton(
                    rects.Expand,
                    anyExpanded ? FluffyWorkTabIcon.Collapse : FluffyWorkTabIcon.Expand,
                    anyExpanded
                        ? "BWT_Header_CollapseSpecificJobs".Translate().ToString()
                        : "BWT_Header_ExpandSpecificJobs".Translate().ToString(),
                    anyExpanded ? "-" : "+"))
            {
                ToggleAllVisibleSubWork(layout);
            }
        }

        private static bool ToggleScheduler(IWorkTabLayoutController layout)
        {
            return FluffyTimeScheduleAssigner.IsEnabled
                ? FluffyTimeScheduleAssigner.Toggle()
                : TimePriorityScheduleEditor.ToggleFirstVisiblePrioritySchedule(layout);
        }

        public static float GetTopRightReservedWidth()
        {
            return ShouldShowTopRightFluffyStyle()
                ? (FluffyTopButtonSize * 3f) + (FluffyTopButtonGap * 2f) + InterControlGap
                : 0f;
        }

        private static bool ShouldShowTopRightFluffyStyle()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (!FluffyWorkTabGateway.FluffyStyleFeaturesEnabled ||
                !BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FluffyStyleFeatures) ||
                !FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab)
            {
                return false;
            }

            return FluffyWorkTabGateway.IsPresent
                ? BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FluffyStyleTopButtons)
                : BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FluffyStyleStandaloneTopButtons);
        }

        private static TopButtonRects GetTopButtonRects(Rect inRect)
        {
            Rect priority = new Rect(
                inRect.xMax - FluffyTopButtonSize,
                inRect.yMin,
                FluffyTopButtonSize,
                FluffyTopButtonSize);

            Rect scheduler = priority;
            scheduler.x -= FluffyTopButtonSize + FluffyTopButtonGap;

            Rect expand = scheduler;
            expand.x -= FluffyTopButtonSize + FluffyTopButtonGap;

            return new TopButtonRects
            {
                Priority = priority,
                Scheduler = scheduler,
                Expand = expand
            };
        }

        // Public entry point called by the window.
        public static void DrawBottomRightGrouped(Rect inRect, Rect gearRect)
        {
            BottomButtonRects rects = GetBottomButtonRects(inRect, gearRect);
            if (rects.HasRuleset)
            {
                DrawAutoAssignGroup(rects);
            }

            HeaderFooterFeatureRegistry.Current?.Draw(rects);
        }

        /// <summary>
        /// Paints the optional footer popover after the tutorial overlay. The
        /// registered feature remains the owner of its own popover state.
        /// </summary>
        internal static void DrawOptionalFooterPopoverOnTop(Rect inRect, Rect gearRect)
        {
            IHeaderFooterFeature feature = HeaderFooterFeatureRegistry.Current;
            if (feature == null || !feature.IsPopoverOpen)
            {
                return;
            }

            feature.DrawPopoverOnTop(
                inRect,
                gearRect,
                GetBottomButtonRects(inRect, gearRect));
        }

        /// <summary>
        /// Routes input for the registered optional footer feature. When no
        /// feature is registered, the host remains a complete ruleset-only row.
        /// </summary>
        public static bool TryHandleOptionalFooterInput(
            Rect inRect,
            Rect gearRect,
            Event evt)
        {
            IHeaderFooterFeature feature = HeaderFooterFeatureRegistry.Current;
            if (feature == null || evt == null)
            {
                return false;
            }

            return feature.TryHandleInput(
                inRect,
                gearRect,
                evt,
                GetBottomButtonRects(inRect, gearRect));
        }

        public static void ResetOptionalFooterState()
        {
            HeaderFooterFeatureRegistry.Current?.Reset();
        }

        private static void DrawAutoAssignGroup(BottomButtonRects rects)
        {
            string name = RuleBuilderGateway.CurrentRulesetLabel();
            bool hasRuleset = RuleBuilderGateway.HasCurrentRuleset();

            string tooltip = hasRuleset
                ? "BWT_BottomBar_RulesetTooltip".Translate(name)
                : "BWT_BottomBar_RulesetTooltipEmpty".Translate();
            bool clicked = BWTBottomBarSelector.DrawMain(
                rects.RulesetMain,
                name,
                hasRuleset,
                tooltip);
            if (clicked)
            {
                if (hasRuleset)
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    RuleBuilderGateway.ApplyCurrentRuleset();
                }
                else
                {
                    // Applying nothing looked like a broken button. With no
                    // ruleset to apply, the useful thing is the list.
                    Find.WindowStack.Add(new FloatMenu(RuleBuilderGateway.BuildRulesetMenuOptions()));
                }
            }

            if (BWTBottomBarSelector.DrawMenu(rects.RulesetMenu, "BWT_BottomBar_RulesetMenuTooltip".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(RuleBuilderGateway.BuildRulesetMenuOptions()));
            }
        }

        private static bool DrawFluffyTopButton(
            Rect rect,
            FluffyWorkTabIcon icon,
            string tooltip,
            string fallbackLabel)
        {
            bool clicked;
            if (FluffyWorkTabGateway.TryGetIcon(icon, out Texture2D texture))
            {
                clicked = Widgets.ButtonImage(rect, texture, Color.white, GenUI.MouseoverColor);
            }
            else
            {
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                clicked = Widgets.ButtonText(rect, fallbackLabel);
                Text.Anchor = previousAnchor;
            }

            TooltipHandler.TipRegion(rect, tooltip);
            MouseoverSounds.DoRegion(rect);
            return clicked;
        }

        private static bool ToggleManualPriorities(bool enabled)
        {
            bool current = ParentPriorityRead.GetObservedManualModeForDisplay(
                true);
            if (current == enabled)
            {
                return true;
            }

            if (!WorkTabEffectiveStateRuntime.TrySetManualMode(enabled))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            return true;
        }

        private static void ToggleAllVisibleSubWork(IWorkTabLayoutController layout)
        {
            if (SubWorkDrilldownState.IsExpandBesideActive)
            {
                SubWorkDrilldownState.CollapseAllExpandBeside();
                WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.Columns | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                return;
            }

            if (layout?.Columns == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            bool expandedAny = false;
            HashSet<string> seenWorkTypes = new HashSet<string>();
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTypeDef workType = layout.Columns[i].Column?.workType;
                string defName = workType?.defName;
                if (defName.NullOrEmpty() ||
                    !seenWorkTypes.Add(defName) ||
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType).Count == 0)
                {
                    continue;
                }

                SubWorkDrilldownState.ToggleExpandBeside(workType);
                expandedAny = true;
            }

            if (!expandedAny)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.Columns | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

    }
}
