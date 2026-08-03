using System;
using System.Collections.Generic;
using Better_Work_Tab.UI;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;
using Verse.Sound;

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
        internal BWTTutorialSelectorAction(string lessonId)
        {
            LessonId = lessonId;
        }

        internal string LessonId { get; }
        internal bool HasLesson => !string.IsNullOrEmpty(LessonId);
    }

    /// <summary>
    /// Anchor selection for the tutorial. Clicking a gold anchor opens a compact
    /// list attached to it; the docked strip owns the instruction text and the
    /// leave/pause actions, so nothing here travels to the far side of the tab.
    /// </summary>
    internal sealed class BWTTutorialSelector
    {
        private const float PopupWidth = 250f;
        private const float PopupPadding = 7f;
        private const float TitleHeight = 22f;
        private const float OptionHeight = 26f;
        private const float OptionGap = 3f;
        private const float AnchorGap = 8f;
        private const float MarkerWidth = 16f;

        // Give players enough time to travel from a narrow/angled Work-tab
        // target into the attached list without losing its context.
        private readonly TutorialHoverGraceState hover = new TutorialHoverGraceState(0.75d);
        private TutorialHubAnchor pinnedAnchor = TutorialHubAnchor.None;
        private BWTTutorialAnchor pinnedGeometry;
        private Rect lastPopupRect;

        /// <summary>The anchor a click has pinned, or None. Observed by the quicktest driver.</summary>
        internal TutorialHubAnchor PinnedAnchor => pinnedAnchor;

        internal void Reset()
        {
            ClearPinnedSelection();
        }

        internal void ClearPinnedSelection()
        {
            hover.Clear();
            pinnedAnchor = TutorialHubAnchor.None;
            pinnedGeometry = default(BWTTutorialAnchor);
            lastPopupRect = Rect.zero;
        }

        internal bool TryHandleSelectionInput(
            IList<BWTTutorialAnchor> anchors,
            Rect workTabBounds,
            Event evt)
        {
            if (anchors == null || evt == null || evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            TutorialHubAnchor anchor = GetAnchorAt(anchors, evt.mousePosition);
            if (anchor == TutorialHubAnchor.None)
            {
                if (!workTabBounds.Contains(evt.mousePosition))
                {
                    ClearPinnedSelection();
                }
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
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            Event evt,
            out BWTTutorialSelectorAction action)
        {
            action = default(BWTTutorialSelectorAction);
            if (evt == null || anchors == null || anchors.Count == 0 || hubs == null)
            {
                return false;
            }

            TutorialHubAnchor active = ResolveActiveAnchor(GetAnchorAt(anchors, evt.mousePosition));
            if (active == TutorialHubAnchor.None ||
                !hubs.TryGetValue(active, out BWTTutorialHubDefinition hub) ||
                hub.Options.Count == 0)
            {
                return false;
            }

            PopupLayout layout = BuildLayout(workBounds, FindAnchorRect(anchors, active), hub);
            if (!layout.PopupRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                for (int i = 0; i < hub.Options.Count && i < layout.OptionRects.Count; i++)
                {
                    if (layout.OptionRects[i].Contains(evt.mousePosition))
                    {
                        action = new BWTTutorialSelectorAction(hub.Options[i].LessonId);
                        break;
                    }
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
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            Vector2 pointer)
        {
            // The list drawn last frame owns the pointer outright. Re-deriving it
            // from the active anchor misses the case where the pointer has left
            // the anchor to travel into the list, which let the grid underneath
            // keep drawing hover highlights and tooltips through the popup.
            if (lastPopupRect.Contains(pointer))
            {
                return true;
            }

            if (anchors == null || anchors.Count == 0 || hubs == null)
            {
                return false;
            }

            TutorialHubAnchor active = ResolveActiveAnchor(GetAnchorAt(anchors, pointer));
            return hubs.TryGetValue(active, out BWTTutorialHubDefinition hub) &&
                   hub.Options.Count > 0 &&
                   BuildLayout(workBounds, FindAnchorRect(anchors, active), hub)
                       .PopupRect.Contains(pointer);
        }

        internal void Draw(
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            ICollection<string> completedLessonIds,
            string recommendedLabel)
        {
            if (anchors == null || anchors.Count == 0)
            {
                return;
            }

            Vector2 pointer = Event.current?.mousePosition ?? new Vector2(-1f, -1f);
            TutorialHubAnchor pointerAnchor = GetAnchorAt(anchors, pointer);
            TutorialHubAnchor hoverAnchor = hover.Update(
                Time.realtimeSinceStartup,
                pointerAnchor,
                lastPopupRect.Contains(pointer),
                false);
            TutorialHubAnchor active = pinnedAnchor != TutorialHubAnchor.None ? pinnedAnchor : hoverAnchor;
            DrawAnchorOutlines(anchors, active, pointerAnchor, completedLessonIds, hubs);

            if (!hubs.TryGetValue(active, out BWTTutorialHubDefinition hub) || hub.Options.Count == 0)
            {
                lastPopupRect = Rect.zero;
                return;
            }

            PopupLayout layout = BuildLayout(workBounds, FindAnchorRect(anchors, active), hub);
            lastPopupRect = layout.PopupRect;
            DrawPopup(hub, layout, completedLessonIds, recommendedLabel, pointer);
        }

        private void DrawPopup(
            BWTTutorialHubDefinition hub,
            PopupLayout layout,
            ICollection<string> completed,
            string recommendedLabel,
            Vector2 pointer)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            // Same tutor chrome as the strip, with RimWorld's own drop shadow, so
            // the list reads as one surface with the band rather than a separate
            // widget kit that happens to be open at the same time.
            Widgets.DrawShadowAround(layout.PopupRect);
            Widgets.DrawWindowBackgroundTutor(layout.PopupRect);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(layout.TitleRect, hub.Title.Truncate(layout.TitleRect.width));
            GUI.color = oldColor;
            for (int i = 0; i < hub.Options.Count && i < layout.OptionRects.Count; i++)
            {
                BWTTutorialOptionDefinition option = hub.Options[i];
                Rect rect = layout.OptionRects[i];
                bool isComplete = TutorialProgressTransitions.IsCompleted(completed, option.LessonId);
                bool isHovered = rect.Contains(pointer);

                // RimWorld's own hover wash and mouseover tick, so the list feels
                // like every other selectable row in the game.
                Widgets.DrawHighlightIfMouseover(rect);
                MouseoverSounds.DoRegion(rect);

                // Completion reads as a check plus a quieter label, so a glance at
                // the list shows what has already been looked at.
                Rect markerRect = new Rect(rect.x + 4f, rect.y, MarkerWidth, rect.height);
                if (isComplete)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(markerRect, "✓");
                }

                Rect labelRect = new Rect(
                    markerRect.xMax + 4f,
                    rect.y,
                    Mathf.Max(1f, rect.width - MarkerWidth - (option.Recommended ? 26f : 10f)),
                    rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = isComplete
                    ? new Color(1f, 1f, 1f, 0.5f)
                    : isHovered ? Widgets.MouseoverOptionColor : Widgets.NormalOptionColor;
                Widgets.Label(labelRect, option.Label.Truncate(labelRect.width));

                // Recommended is a mark, not words appended to the label. The old
                // inline separator wrapped and left every row a different height.
                if (option.Recommended && !isComplete)
                {
                    Rect starRect = new Rect(rect.xMax - 22f, rect.y, 18f, rect.height);
                    GUI.color = BWTUiPalette.TutorAccent;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(starRect, "★");
                    TooltipHandler.TipRegion(starRect, recommendedLabel);
                }

                if (!string.IsNullOrEmpty(option.Body))
                {
                    TooltipHandler.TipRegion(rect, option.Body);
                }
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private void DrawAnchorOutlines(
            IList<BWTTutorialAnchor> anchors,
            TutorialHubAnchor active,
            TutorialHubAnchor pointerAnchor,
            ICollection<string> completed,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4.2f);
            for (int i = 0; i < anchors.Count; i++)
            {
                BWTTutorialAnchor anchor = anchors[i];
                bool selected = anchor.Kind == active && active != TutorialHubAnchor.None &&
                    (!pinnedGeometry.IsValid || ApproximatelySame(anchor.Rect, pinnedGeometry.Rect));
                bool emphasized = selected || anchor.Kind == pointerAnchor;

                // An anchor whose lessons are all done stops pulsing for attention.
                bool exhausted = AllOptionsComplete(anchor.Kind, hubs, completed);
                float alpha = exhausted
                    ? (emphasized ? 0.7f : 0.3f)
                    : emphasized ? 1f : Mathf.Lerp(0.46f, 0.78f, pulse);
                BWTTutorialAnchorRenderer.DrawOutline(
                    anchor,
                    BWTTutorialAnchorRenderer.TutorialGold(alpha),
                    emphasized ? 3f : 2f);
            }
        }

        private static bool AllOptionsComplete(
            TutorialHubAnchor kind,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            ICollection<string> completed)
        {
            if (hubs == null || !hubs.TryGetValue(kind, out BWTTutorialHubDefinition hub) ||
                hub.Options.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < hub.Options.Count; i++)
            {
                if (!TutorialProgressTransitions.IsCompleted(completed, hub.Options[i].LessonId))
                {
                    return false;
                }
            }

            return true;
        }

        private static PopupLayout BuildLayout(
            Rect workBounds,
            Rect anchorRect,
            BWTTutorialHubDefinition hub)
        {
            float width = Mathf.Min(PopupWidth, Mathf.Max(140f, workBounds.width - 16f));
            float height = PopupPadding * 2f + TitleHeight +
                hub.Options.Count * OptionHeight +
                Mathf.Max(0, hub.Options.Count - 1) * OptionGap;

            // Prefer below the anchor, flip above when the tab runs out, then
            // clamp. The list stays attached to what it describes either way.
            float x = Mathf.Clamp(
                anchorRect.center.x - width * 0.5f,
                workBounds.xMin + 4f,
                Mathf.Max(workBounds.xMin + 4f, workBounds.xMax - width - 4f));
            float below = anchorRect.yMax + AnchorGap;
            float y = below + height <= workBounds.yMax - 4f
                ? below
                : Mathf.Max(workBounds.yMin + 4f, anchorRect.yMin - height - AnchorGap);

            Rect popup = new Rect(x, y, width, height);
            Rect inner = popup.ContractedBy(PopupPadding);
            Rect title = new Rect(inner.x, inner.y, inner.width, TitleHeight);

            var optionRects = new List<Rect>(hub.Options.Count);
            float rowY = title.yMax;
            for (int i = 0; i < hub.Options.Count; i++)
            {
                optionRects.Add(new Rect(inner.x, rowY, inner.width, OptionHeight));
                rowY += OptionHeight + OptionGap;
            }

            return new PopupLayout(popup, title, optionRects);
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

        private Rect FindAnchorRect(IList<BWTTutorialAnchor> anchors, TutorialHubAnchor kind)
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

        private readonly struct PopupLayout
        {
            internal PopupLayout(Rect popupRect, Rect titleRect, IList<Rect> optionRects)
            {
                PopupRect = popupRect;
                TitleRect = titleRect;
                OptionRects = optionRects;
            }

            internal Rect PopupRect { get; }
            internal Rect TitleRect { get; }
            internal IList<Rect> OptionRects { get; }
        }
    }
}
