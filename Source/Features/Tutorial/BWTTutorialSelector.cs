using System;
using System.Collections.Generic;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal sealed class BWTTutorialHubDefinition
    {
        internal BWTTutorialHubDefinition(
            TutorialHubAnchor anchor,
            string title,
            string body,
            IList<BWTTutorialOptionDefinition> options)
        {
            Anchor = anchor;
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            Options = options ?? new BWTTutorialOptionDefinition[0];
        }

        internal TutorialHubAnchor Anchor { get; }
        internal string Title { get; }
        internal string Body { get; }
        internal IList<BWTTutorialOptionDefinition> Options { get; }
    }

    internal sealed class BWTTutorialOptionDefinition
    {
        internal BWTTutorialOptionDefinition(
            string lessonId,
            string label,
            string heading,
            string body,
            bool recommended = false)
        {
            LessonId = lessonId ?? string.Empty;
            Label = label ?? string.Empty;
            Heading = heading ?? string.Empty;
            Body = body ?? string.Empty;
            Recommended = recommended;
        }

        internal string LessonId { get; }
        internal string Label { get; }
        internal string Heading { get; }
        internal string Body { get; }
        internal bool Recommended { get; }
    }

    internal readonly struct BWTTutorialSelectorAction
    {
        internal BWTTutorialSelectorAction(string lessonId, bool exit, bool leave = false)
        {
            LessonId = lessonId;
            Exit = exit;
            Leave = leave;
        }

        internal string LessonId { get; }
        internal bool Exit { get; }
        internal bool Leave { get; }
        internal bool HasLesson => !string.IsNullOrEmpty(LessonId);
    }

    /// <summary>
    /// Shared anchor/options/context renderer. Hub content is data, so
    /// pawn, work-header, and priority-cell tutorials use one interaction model.
    /// </summary>
    internal sealed class BWTTutorialSelector
    {
        private const float PanelGap = 12f;
        private const float CardPadding = 14f;
        private const float ContextPadding = 13f;
        private const float ScrollbarWidth = 16f;
        private const float OptionWidth = 220f;
        private const float ContextWidth = 280f;
        private const float FooterHeight = 40f;
        private const float MinimumPanelWidth = 420f;
        private const float PreferredPanelWidth = OptionWidth + ContextWidth + PanelGap + CardPadding * 2f;

        // Give players enough time to travel from a narrow/angled Work-tab
        // target into the detached tutorial card without losing its context.
        private readonly TutorialHoverGraceState hover = new TutorialHoverGraceState(0.75d);
        private TutorialHubAnchor pinnedAnchor = TutorialHubAnchor.None;
        private BWTTutorialAnchor pinnedGeometry;
        private string hoveredLessonId;
        private float hoveredLessonLastConnectedAt = -1f;
        private Rect lastOptionsRect;
        private Rect lastContextRect;
        private Vector2 optionScrollPosition;
        private Vector2 contextScrollPosition;
        private TutorialHubAnchor optionScrollAnchor = TutorialHubAnchor.None;
        private float lastContextContentHeight;
        private string contextContentKey;

        internal void Reset()
        {
            hover.Clear();
            pinnedAnchor = TutorialHubAnchor.None;
            pinnedGeometry = default(BWTTutorialAnchor);
            hoveredLessonId = null;
            hoveredLessonLastConnectedAt = -1f;
            lastOptionsRect = Rect.zero;
            lastContextRect = Rect.zero;
            optionScrollPosition = Vector2.zero;
            contextScrollPosition = Vector2.zero;
            optionScrollAnchor = TutorialHubAnchor.None;
            lastContextContentHeight = 0f;
            contextContentKey = null;
        }

        internal void ClearPinnedSelection()
        {
            hover.Clear();
            pinnedAnchor = TutorialHubAnchor.None;
            pinnedGeometry = default(BWTTutorialAnchor);
            hoveredLessonId = null;
            hoveredLessonLastConnectedAt = -1f;
            lastOptionsRect = Rect.zero;
            lastContextRect = Rect.zero;
            contextContentKey = null;
        }

        internal bool TryHandleAnchorInput(IList<BWTTutorialAnchor> anchors, Event evt)
        {
            if (anchors == null || evt == null || evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            TutorialHubAnchor anchor = GetAnchorAt(anchors, evt.mousePosition);
            if (anchor == TutorialHubAnchor.None)
            {
                return false;
            }

            pinnedAnchor = anchor;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Kind == anchor && anchors[i].Contains(evt.mousePosition))
                {
                    pinnedGeometry = anchors[i];
                    break;
                }
            }
            hover.Pin(anchor, Time.realtimeSinceStartup);
            evt.Use();
            return true;
        }

        internal bool TryHandleInput(
            Rect bounds,
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            ICollection<string> completedLessonIds,
            Event evt,
            string recommendedLabel,
            string exitLabel,
            string leaveLabel,
            out BWTTutorialSelectorAction action)
        {
            action = default(BWTTutorialSelectorAction);
            if (evt == null || anchors == null || anchors.Count == 0)
            {
                return false;
            }

            TutorialHubAnchor anchorAtPointer = GetAnchorAt(anchors, evt.mousePosition);
            TutorialHubAnchor active = ResolveActiveAnchor(anchorAtPointer);

            if (active == TutorialHubAnchor.None || !hubs.TryGetValue(active, out BWTTutorialHubDefinition hub))
            {
                return false;
            }

            SelectorLayout layout = BuildLayout(
                bounds,
                workBounds,
                FindAnchorRect(anchors, active),
                hub,
                recommendedLabel);
            if (!layout.PanelRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (evt.type == EventType.ScrollWheel && layout.OptionsRect.Contains(evt.mousePosition))
            {
                optionScrollPosition.y = TutorialOverflowScrollPolicy.ApplyWheel(
                    optionScrollPosition.y,
                    evt.delta.y,
                    layout.OptionsRect.height,
                    layout.OptionViewRect.height);
                evt.Use();
                return true;
            }

            if (evt.type == EventType.ScrollWheel && layout.ContextRect.Contains(evt.mousePosition))
            {
                contextScrollPosition.y = TutorialOverflowScrollPolicy.ApplyWheel(
                    contextScrollPosition.y,
                    evt.delta.y,
                    Mathf.Max(0f, layout.ContextRect.height - ContextPadding * 2f),
                    lastContextContentHeight);
                evt.Use();
                return true;
            }

            bool overOptionsScrollbar =
                layout.OptionViewRect.height > layout.OptionsRect.height + 0.5f &&
                new Rect(
                    layout.OptionsRect.xMax - ScrollbarWidth,
                    layout.OptionsRect.y,
                    ScrollbarWidth,
                    layout.OptionsRect.height).Contains(evt.mousePosition);
            Rect contextInner = layout.ContextRect.ContractedBy(ContextPadding);
            bool overContextScrollbar =
                lastContextContentHeight > contextInner.height + 0.5f &&
                new Rect(
                    contextInner.xMax - ScrollbarWidth,
                    contextInner.y,
                    ScrollbarWidth,
                    contextInner.height).Contains(evt.mousePosition);
            if ((evt.type == EventType.MouseDown && (overOptionsScrollbar || overContextScrollbar)) ||
                ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseUp) && GUIUtility.hotControl != 0))
            {
                // BeginScrollView owns scrollbar dragging. The Work-grid router
                // has already honored tutorial pointer ownership beneath this window.
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (layout.ExitRect.Contains(evt.mousePosition))
                {
                    action = new BWTTutorialSelectorAction(null, exit: true);
                    evt.Use();
                    return true;
                }
                if (layout.LeaveRect.Contains(evt.mousePosition))
                {
                    action = new BWTTutorialSelectorAction(null, exit: false, leave: true);
                    evt.Use();
                    return true;
                }

                for (int i = 0; i < hub.Options.Count && i < layout.OptionRects.Count; i++)
                {
                    Rect optionRect = GetScreenOptionRect(layout, layout.OptionRects[i], optionScrollPosition.y);
                    if (!layout.OptionsRect.Overlaps(optionRect) || !optionRect.Contains(evt.mousePosition))
                    {
                        continue;
                    }

                    if (TutorialProgressTransitions.CanStartLesson(optionHovered: true, optionClicked: true))
                    {
                        action = new BWTTutorialSelectorAction(hub.Options[i].LessonId, exit: false);
                    }
                    evt.Use();
                    return true;
                }

                evt.Use();
                return true;
            }

            if (IsPointerEvent(evt.type))
            {
                evt.Use();
                return true;
            }

            return false;
        }

        internal bool ContainsPointer(
            Rect bounds,
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            Vector2 pointer,
            string recommendedLabel)
        {
            if (anchors == null || anchors.Count == 0 || hubs == null)
            {
                return false;
            }

            TutorialHubAnchor active = ResolveActiveAnchor(GetAnchorAt(anchors, pointer));
            return hubs.TryGetValue(active, out BWTTutorialHubDefinition hub) &&
                   BuildLayout(
                       bounds,
                       workBounds,
                       FindAnchorRect(anchors, active),
                       hub,
                       recommendedLabel).PanelRect.Contains(pointer);
        }

        internal void Draw(
            Rect bounds,
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            ICollection<string> completedLessonIds,
            string defaultHeading,
            string recommendedLabel,
            string exitLabel,
            string leaveLabel)
        {
            if (anchors == null || anchors.Count == 0)
            {
                return;
            }

            Vector2 pointer = Event.current?.mousePosition ?? new Vector2(-1f, -1f);
            TutorialHubAnchor pointerAnchor = GetAnchorAt(anchors, pointer);
            bool overOptions = lastOptionsRect.Contains(pointer);
            bool overContext = lastContextRect.Contains(pointer);
            TutorialHubAnchor hoverAnchor = hover.Update(
                Time.realtimeSinceStartup,
                pointerAnchor,
                overOptions,
                overContext);
            TutorialHubAnchor active = pinnedAnchor != TutorialHubAnchor.None ? pinnedAnchor : hoverAnchor;
            if (active != TutorialHubAnchor.None)
            {
                // Shade the complete Work-tab surface. The inferred content
                // bounds omit toolbars and footer rows, which left visibly
                // different strips around the tutorial overlay.
                DrawFocusDim(bounds, FindAnchorRect(anchors, active));
            }
            DrawAnchorOutlines(anchors, active, pointerAnchor);
            if (!hubs.TryGetValue(active, out BWTTutorialHubDefinition hub))
            {
                lastOptionsRect = Rect.zero;
                lastContextRect = Rect.zero;
                hoveredLessonId = null;
                optionScrollAnchor = TutorialHubAnchor.None;
                return;
            }

            SelectorLayout layout = BuildLayout(
                bounds,
                workBounds,
                FindAnchorRect(anchors, active),
                hub,
                recommendedLabel);
            lastOptionsRect = layout.OptionsRect;
            lastContextRect = layout.ContextRect;
            if (optionScrollAnchor != active)
            {
                optionScrollAnchor = active;
                optionScrollPosition = Vector2.zero;
            }
            optionScrollPosition.y = TutorialOverflowScrollPolicy.ClampOffset(
                optionScrollPosition.y,
                layout.OptionsRect.height,
                layout.OptionViewRect.height);
            string currentHoveredLesson = GetHoveredLesson(
                hub,
                layout,
                optionScrollPosition.y,
                pointer);
            if (!string.IsNullOrEmpty(currentHoveredLesson))
            {
                hoveredLessonId = currentHoveredLesson;
                hoveredLessonLastConnectedAt = Time.realtimeSinceStartup;
            }
            else if (!layout.ContextRect.Contains(pointer) &&
                     Time.realtimeSinceStartup - hoveredLessonLastConnectedAt > 0.22f)
            {
                hoveredLessonId = null;
            }
            BWTTutorialOptionDefinition hoveredOption = FindOption(hub, hoveredLessonId);

            string nextContextKey = hub.Anchor + ":" + (hoveredOption?.LessonId ?? string.Empty);
            if (!string.Equals(contextContentKey, nextContextKey, StringComparison.Ordinal))
            {
                contextContentKey = nextContextKey;
                contextScrollPosition = Vector2.zero;
            }

            DrawPanel(
                hub,
                hoveredOption,
                layout,
                completedLessonIds,
                defaultHeading,
                recommendedLabel,
                exitLabel,
                leaveLabel);
        }

        private void DrawAnchorOutlines(
            IList<BWTTutorialAnchor> anchors,
            TutorialHubAnchor active,
            TutorialHubAnchor pointerAnchor)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4.2f);
            for (int i = 0; i < anchors.Count; i++)
            {
                BWTTutorialAnchor anchor = anchors[i];
                bool selected = anchor.Kind == active && active != TutorialHubAnchor.None &&
                    (!pinnedGeometry.IsValid || ApproximatelySame(anchor.Rect, pinnedGeometry.Rect));
                bool emphasized = selected || anchor.Kind == pointerAnchor;
                float alpha = emphasized ? 1f : Mathf.Lerp(0.46f, 0.78f, pulse);
                if (selected && anchor.Kind != TutorialHubAnchor.WorkHeader)
                {
                    BWTTutorialAnchorRenderer.DrawOutline(anchor, Color.white, 5f);
                }
                BWTTutorialAnchorRenderer.DrawOutline(
                    anchor,
                    new Color(1f, 0.78f, 0.22f, alpha),
                    emphasized ? 3f : 2f);
            }

            if (pinnedGeometry.IsValid && !ContainsRect(anchors, pinnedGeometry.Rect))
            {
                if (pinnedGeometry.Kind != TutorialHubAnchor.WorkHeader)
                {
                    BWTTutorialAnchorRenderer.DrawOutline(pinnedGeometry, Color.white, 5f);
                }
                BWTTutorialAnchorRenderer.DrawOutline(pinnedGeometry, new Color(1f, 0.78f, 0.22f, 1f), 3f);
            }
        }

        private void DrawPanel(
            BWTTutorialHubDefinition hub,
            BWTTutorialOptionDefinition hovered,
            SelectorLayout layout,
            ICollection<string> completed,
            string defaultHeading,
            string recommendedLabel,
            string exitLabel,
            string leaveLabel)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            Widgets.DrawBoxSolid(layout.PanelRect, new Color(0.055f, 0.062f, 0.07f, 0.98f));
            GUI.color = new Color(0.55f, 0.47f, 0.28f, 0.95f);
            Widgets.DrawBox(layout.PanelRect, 1);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(1f, 0.85f, 0.36f, 1f);
            Widgets.Label(layout.TitleRect, hub.Title);

            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            Widgets.BeginScrollView(layout.OptionsRect, ref optionScrollPosition, layout.OptionViewRect);
            for (int i = 0; i < hub.Options.Count && i < layout.OptionRects.Count; i++)
            {
                BWTTutorialOptionDefinition option = hub.Options[i];
                Rect rect = layout.OptionRects[i];
                bool isHovered = string.Equals(option.LessonId, hovered?.LessonId, StringComparison.Ordinal);
                bool isComplete = TutorialProgressTransitions.IsCompleted(completed, option.LessonId);
                Widgets.DrawBoxSolid(rect, isHovered
                    ? new Color(0.23f, 0.20f, 0.11f, 0.98f)
                    : new Color(0.095f, 0.105f, 0.115f, 0.98f));
                GUI.color = isHovered
                    ? new Color(1f, 0.82f, 0.28f, 1f)
                    : new Color(0.42f, 0.43f, 0.42f, 1f);
                Widgets.DrawBox(rect, isHovered ? 2 : 1);
                GUI.color = Color.white;
                string prefix = isComplete ? "✓  " : string.Empty;
                Rect labelRect = new Rect(rect.x + 9f, rect.y + 4f, rect.width - 18f, rect.height - 8f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, prefix + GetOptionDisplayLabel(option, recommendedLabel));
            }
            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;

            Widgets.DrawBoxSolid(layout.ContextRect, new Color(0.075f, 0.082f, 0.09f, 0.98f));
            GUI.color = new Color(0.32f, 0.34f, 0.35f, 1f);
            Widgets.DrawBox(layout.ContextRect, 1);
            Rect contextInner = layout.ContextRect.ContractedBy(ContextPadding);
            Text.Font = GameFont.Small;
            string heading = hovered?.Heading ?? defaultHeading;
            string body = hovered?.Body ?? hub.Body;
            float contentWidth = Mathf.Max(80f, contextInner.width);
            float headingHeight = Mathf.Max(28f, Text.CalcHeight(heading, contentWidth));
            float bodyHeight = Mathf.Max(28f, Text.CalcHeight(body, contentWidth));
            lastContextContentHeight = headingHeight + 10f + bodyHeight;
            if (lastContextContentHeight > contextInner.height + 0.5f)
            {
                contentWidth = Mathf.Max(80f, contextInner.width - ScrollbarWidth);
                headingHeight = Mathf.Max(28f, Text.CalcHeight(heading, contentWidth));
                bodyHeight = Mathf.Max(28f, Text.CalcHeight(body, contentWidth));
                lastContextContentHeight = headingHeight + 10f + bodyHeight;
            }
            contextScrollPosition.y = TutorialOverflowScrollPolicy.ClampOffset(
                contextScrollPosition.y,
                contextInner.height,
                lastContextContentHeight);
            Rect contextView = new Rect(
                0f,
                0f,
                contentWidth,
                Mathf.Max(contextInner.height, lastContextContentHeight));
            Widgets.BeginScrollView(contextInner, ref contextScrollPosition, contextView);
            GUI.color = new Color(1f, 0.86f, 0.42f, 1f);
            Widgets.Label(new Rect(0f, 0f, contentWidth, headingHeight), heading);
            GUI.color = new Color(0.9f, 0.91f, 0.9f, 1f);
            Widgets.Label(new Rect(
                0f,
                headingHeight + 10f,
                contentWidth,
                bodyHeight), body);
            Widgets.EndScrollView();

            bool exitHovered = layout.ExitRect.Contains(Event.current?.mousePosition ?? Vector2.zero);
            Widgets.DrawBoxSolid(layout.ExitRect, exitHovered
                ? new Color(0.20f, 0.18f, 0.11f, 1f)
                : new Color(0.10f, 0.11f, 0.12f, 1f));
            GUI.color = exitHovered ? new Color(1f, 0.83f, 0.32f, 1f) : Color.white;
            Widgets.DrawBox(layout.ExitRect, 1);
            TextAnchor exitAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(layout.ExitRect, exitLabel);
            Text.Anchor = exitAnchor;

            bool leaveHovered = layout.LeaveRect.Contains(Event.current?.mousePosition ?? Vector2.zero);
            Widgets.DrawBoxSolid(layout.LeaveRect, leaveHovered
                ? new Color(0.20f, 0.18f, 0.11f, 1f)
                : new Color(0.10f, 0.11f, 0.12f, 1f));
            GUI.color = leaveHovered ? new Color(1f, 0.83f, 0.32f, 1f) : Color.white;
            Widgets.DrawBox(layout.LeaveRect, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(layout.LeaveRect, leaveLabel);
            Text.Anchor = exitAnchor;

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private static SelectorLayout BuildLayout(
            Rect bounds,
            Rect workBounds,
            Rect anchorRect,
            BWTTutorialHubDefinition hub,
            string recommendedLabel)
        {
            float availableRight = bounds.xMax - workBounds.xMax - PanelGap;
            bool useRight = availableRight >= MinimumPanelWidth;
            float width = useRight
                ? Mathf.Min(PreferredPanelWidth, availableRight)
                : Mathf.Min(Mathf.Max(MinimumPanelWidth, bounds.width * 0.76f), bounds.width - 20f);
            float height = Mathf.Min(440f, bounds.height - 20f);
            float x = useRight
                ? workBounds.xMax + PanelGap
                : Mathf.Clamp(workBounds.center.x - width / 2f, bounds.xMin + 10f, bounds.xMax - width - 10f);
            Vector2 anchorCenter = anchorRect.width > 0f && anchorRect.height > 0f
                ? anchorRect.center
                : workBounds.center;
            float y = useRight
                ? Mathf.Clamp(anchorCenter.y - height / 2f, bounds.yMin + 10f, bounds.yMax - height - 10f)
                : Mathf.Clamp(
                    anchorRect.yMin - height - PanelGap,
                    bounds.yMin + 10f,
                    bounds.yMax - height - 10f);
            Rect panel = new Rect(x, y, width, height);

            Rect inner = panel.ContractedBy(CardPadding);
            Rect title = new Rect(inner.x, inner.y, inner.width, 30f);
            float bodyTop = title.yMax + 8f;
            float bodyBottom = inner.yMax - FooterHeight - 8f;
            float bodyHeight = Mathf.Max(90f, bodyBottom - bodyTop);
            bool stacked = inner.width < OptionWidth + ContextWidth + PanelGap;
            float optionsWidth = stacked ? inner.width * 0.43f : OptionWidth;
            Rect options = new Rect(inner.x, bodyTop, optionsWidth, bodyHeight);
            Rect context = new Rect(options.xMax + PanelGap, bodyTop, inner.xMax - options.xMax - PanelGap, bodyHeight);
            if (context.width < 150f)
            {
                context.x = inner.x + optionsWidth + 6f;
                context.width = Mathf.Max(140f, inner.xMax - context.x);
            }

            float gap = 7f;
            var optionRects = new List<Rect>(hub.Options.Count);
            float optionContentWidth = options.width;
            float contentHeight = 0f;
            bool oldWordWrap = Text.WordWrap;
            GameFont oldFont = Text.Font;
            Text.WordWrap = true;
            Text.Font = GameFont.Small;
            for (int pass = 0; pass < 2; pass++)
            {
                optionRects.Clear();
                float yOffset = 0f;
                for (int i = 0; i < hub.Options.Count; i++)
                {
                    string label = GetOptionDisplayLabel(hub.Options[i], recommendedLabel);
                    float labelHeight = Text.CalcHeight(label, Mathf.Max(40f, optionContentWidth - 18f));
                    float optionHeight = Mathf.Max(40f, labelHeight + 14f);
                    optionRects.Add(new Rect(0f, yOffset, optionContentWidth, optionHeight));
                    yOffset += optionHeight + gap;
                }

                contentHeight = Mathf.Max(0f, yOffset - gap);
                if (pass == 0 && contentHeight > options.height + 0.5f)
                {
                    optionContentWidth = Mathf.Max(40f, options.width - 16f);
                    continue;
                }

                break;
            }
            Text.WordWrap = oldWordWrap;
            Text.Font = oldFont;

            float footerButtonWidth = Mathf.Min(170f, (inner.width - 8f) / 2f);
            Rect exit = new Rect(inner.x, inner.yMax - 34f, footerButtonWidth, 34f);
            Rect leave = new Rect(exit.xMax + 8f, exit.y, footerButtonWidth, 34f);
            Rect optionView = new Rect(0f, 0f, optionContentWidth, Mathf.Max(options.height, contentHeight));
            return new SelectorLayout(panel, title, options, optionView, context, exit, leave, optionRects);
        }

        private static TutorialHubAnchor GetAnchorAt(IList<BWTTutorialAnchor> anchors, Vector2 point)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Contains(point))
                {
                    return anchors[i].Kind;
                }
            }

            return TutorialHubAnchor.None;
        }

        private static void DrawFocusDim(Rect workBounds, Rect focusRect)
        {
            if (focusRect.width <= 0f || focusRect.height <= 0f)
            {
                return;
            }

            Rect focus = focusRect.ExpandedBy(6f);
            Color dim = new Color(0f, 0f, 0f, 0.24f);
            Widgets.DrawBoxSolid(new Rect(workBounds.xMin, workBounds.yMin, workBounds.width,
                Mathf.Max(0f, focus.yMin - workBounds.yMin)), dim);
            Widgets.DrawBoxSolid(new Rect(workBounds.xMin, focus.yMax, workBounds.width,
                Mathf.Max(0f, workBounds.yMax - focus.yMax)), dim);
            Widgets.DrawBoxSolid(new Rect(workBounds.xMin, focus.yMin,
                Mathf.Max(0f, focus.xMin - workBounds.xMin), focus.height), dim);
            Widgets.DrawBoxSolid(new Rect(focus.xMax, focus.yMin,
                Mathf.Max(0f, workBounds.xMax - focus.xMax), focus.height), dim);
        }

        private Rect FindAnchorRect(
            IList<BWTTutorialAnchor> anchors,
            TutorialHubAnchor kind)
        {
            if (pinnedGeometry.IsValid && pinnedGeometry.Kind == kind)
            {
                return pinnedGeometry.Rect;
            }
            if (anchors != null)
            {
                for (int i = 0; i < anchors.Count; i++)
                {
                    if (anchors[i].Kind == kind)
                    {
                        return anchors[i].Rect;
                    }
                }
            }

            return Rect.zero;
        }

        private static bool ContainsRect(IList<BWTTutorialAnchor> anchors, Rect rect)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (ApproximatelySame(anchors[i].Rect, rect))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ApproximatelySame(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f &&
                   Mathf.Abs(a.y - b.y) < 0.5f &&
                   Mathf.Abs(a.width - b.width) < 0.5f &&
                   Mathf.Abs(a.height - b.height) < 0.5f;
        }

        private TutorialHubAnchor ResolveActiveAnchor(TutorialHubAnchor anchorAtPointer)
        {
            if (pinnedAnchor != TutorialHubAnchor.None)
            {
                return pinnedAnchor;
            }

            return anchorAtPointer != TutorialHubAnchor.None
                ? anchorAtPointer
                : hover.ActiveAnchor;
        }

        private static bool IsPointerEvent(EventType type)
        {
            return type == EventType.MouseDown ||
                   type == EventType.MouseUp ||
                   type == EventType.MouseMove ||
                   type == EventType.MouseDrag ||
                   type == EventType.ScrollWheel;
        }

        private static string GetHoveredLesson(
            BWTTutorialHubDefinition hub,
            SelectorLayout layout,
            float scrollY,
            Vector2 pointer)
        {
            if (!layout.OptionsRect.Contains(pointer))
            {
                return null;
            }

            for (int i = 0; i < hub.Options.Count && i < layout.OptionRects.Count; i++)
            {
                Rect optionRect = GetScreenOptionRect(layout, layout.OptionRects[i], scrollY);
                if (layout.OptionsRect.Overlaps(optionRect) && optionRect.Contains(pointer))
                {
                    return hub.Options[i].LessonId;
                }
            }

            return null;
        }

        private static BWTTutorialOptionDefinition FindOption(BWTTutorialHubDefinition hub, string lessonId)
        {
            if (string.IsNullOrEmpty(lessonId))
            {
                return null;
            }

            for (int i = 0; i < hub.Options.Count; i++)
            {
                if (string.Equals(hub.Options[i].LessonId, lessonId, StringComparison.Ordinal))
                {
                    return hub.Options[i];
                }
            }

            return null;
        }

        private static Rect GetScreenOptionRect(SelectorLayout layout, Rect contentRect, float scrollY)
        {
            return new Rect(
                layout.OptionsRect.x + contentRect.x,
                layout.OptionsRect.y + contentRect.y - scrollY,
                contentRect.width,
                contentRect.height);
        }

        private static string GetOptionDisplayLabel(BWTTutorialOptionDefinition option, string recommendedLabel)
        {
            return option.Recommended
                ? option.Label + "  · " + recommendedLabel
                : option.Label;
        }

        private readonly struct SelectorLayout
        {
            internal SelectorLayout(
                Rect panelRect,
                Rect titleRect,
                Rect optionsRect,
                Rect optionViewRect,
                Rect contextRect,
                Rect exitRect,
                Rect leaveRect,
                IList<Rect> optionRects)
            {
                PanelRect = panelRect;
                TitleRect = titleRect;
                OptionsRect = optionsRect;
                OptionViewRect = optionViewRect;
                ContextRect = contextRect;
                ExitRect = exitRect;
                LeaveRect = leaveRect;
                OptionRects = optionRects;
            }

            internal Rect PanelRect { get; }
            internal Rect TitleRect { get; }
            internal Rect OptionsRect { get; }
            internal Rect OptionViewRect { get; }
            internal Rect ContextRect { get; }
            internal Rect ExitRect { get; }
            internal Rect LeaveRect { get; }
            internal IList<Rect> OptionRects { get; }
        }
    }
}
