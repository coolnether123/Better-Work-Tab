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
            string body)
        {
            LessonId = lessonId ?? string.Empty;
            Label = label ?? string.Empty;
            Heading = heading ?? string.Empty;
            Body = body ?? string.Empty;
        }

        internal string LessonId { get; }
        internal string Label { get; }
        internal string Heading { get; }
        internal string Body { get; }
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
        private const float MinimumTitleHeight = 22f;
        private const float MinimumOptionHeight = 26f;
        private const float OptionGap = 3f;
        private const float AnchorGap = 8f;
        private const float MarkerWidth = 16f;

        // Give players enough time to travel from a narrow/angled Work-tab
        // target into the attached list without losing its context.
        private readonly TutorialHoverGraceState hover = new TutorialHoverGraceState(0.75d);
        private TutorialHubAnchor pinnedAnchor = TutorialHubAnchor.None;
        private BWTTutorialAnchor pinnedGeometry;
        private Rect lastPopupRect;

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
            Event evt)
        {
            if (evt == null || evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            if (!TrySelectAnchorAt(anchors, evt.mousePosition))
            {
                return false;
            }

            evt.Use();
            return true;
        }

        /// <summary>
        /// Selects the anchor under a point. Split out from the event overload so
        /// callers that already have a local point use the same hit testing.
        /// </summary>
        internal bool TrySelectAnchorAt(
            IList<BWTTutorialAnchor> anchors,
            Vector2 point)
        {
            if (anchors == null)
            {
                return false;
            }

            TutorialHubAnchor anchor = GetAnchorAt(anchors, point);
            if (anchor == TutorialHubAnchor.None)
            {
                // Anything that is not an anchor dismisses the open list. Callers
                // reach this only after the band and the list itself have declined
                // the click, so the player has clicked away from the popup.
                //
                // This used to dismiss only when the click landed outside the Work
                // tab, which is the one place nobody clicks: once a list was open
                // it stayed open, the other two anchors sat underneath it, and
                // "click a highlighted name, header, or priority" stopped being
                // true for two of the three.
                ClearPinnedSelection();
                return false;
            }

            pinnedAnchor = anchor;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Kind == anchor && anchors[i].Contains(point))
                {
                    pinnedGeometry = anchors[i];
                    break;
                }
            }
            hover.Pin(anchor, Time.realtimeSinceStartup);
            return true;
        }

        /// <summary>
        /// Resolves a click inside the attached list. Returns true when the point
        /// lands on the popup at all — the list owns those clicks whether or not
        /// one hit a row — and sets <paramref name="action"/> only when a lesson
        /// row was hit.
        /// </summary>
        internal bool TryHandlePopupClick(
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            Vector2 point,
            out BWTTutorialSelectorAction action)
        {
            action = default(BWTTutorialSelectorAction);
            if (!TryResolvePopupAt(workBounds, anchors, hubs, point, out BWTTutorialHubDefinition hub, out PopupLayout layout))
            {
                return false;
            }

            for (int i = 0; i < hub.Options.Count && i < layout.OptionRects.Count; i++)
            {
                if (layout.OptionRects[i].Contains(point))
                {
                    action = new BWTTutorialSelectorAction(hub.Options[i].LessonId);
                    break;
                }
            }

            return true;
        }

        private bool TryResolvePopupAt(
            Rect workBounds,
            IList<BWTTutorialAnchor> anchors,
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs,
            Vector2 point,
            out BWTTutorialHubDefinition hub,
            out PopupLayout layout)
        {
            hub = default(BWTTutorialHubDefinition);
            layout = default(PopupLayout);
            if (anchors == null || anchors.Count == 0 || hubs == null)
            {
                return false;
            }

            TutorialHubAnchor active = ResolveActiveAnchor(GetAnchorAt(anchors, point));
            if (active == TutorialHubAnchor.None ||
                !hubs.TryGetValue(active, out hub) ||
                hub.Options.Count == 0)
            {
                return false;
            }

            layout = BuildLayout(workBounds, FindAnchorRect(anchors, active), hub);
            return layout.PopupRect.Contains(point);
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
            BWTTutorialProgressSnapshot progress)
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
            DrawAnchorOutlines(anchors, active, pointerAnchor, progress, hubs);

            if (!hubs.TryGetValue(active, out BWTTutorialHubDefinition hub) || hub.Options.Count == 0)
            {
                lastPopupRect = Rect.zero;
                return;
            }

            PopupLayout layout = BuildLayout(workBounds, FindAnchorRect(anchors, active), hub);
            lastPopupRect = layout.PopupRect;
            DrawPopup(hub, layout, progress, pointer);
        }

        private void DrawPopup(
            BWTTutorialHubDefinition hub,
            PopupLayout layout,
            BWTTutorialProgressSnapshot progress,
            Vector2 pointer)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = true;

            // Same tutor chrome as the strip, with RimWorld's own drop shadow, so
            // the list reads as one surface with the band rather than a separate
            // widget kit that happens to be open at the same time.
            Widgets.DrawShadowAround(layout.PopupRect);
            Widgets.DrawWindowBackgroundTutor(layout.PopupRect);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(layout.TitleRect, hub.Title);
            GUI.color = oldColor;
            for (int i = 0; i < hub.Options.Count && i < layout.OptionRects.Count; i++)
            {
                BWTTutorialOptionDefinition option = hub.Options[i];
                Rect rect = layout.OptionRects[i];
                bool isComplete = progress.IsSettled(option.LessonId);
                bool alreadyUsed = progress.IsAlreadyUsed(option.LessonId);
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
                    // A check either way -- both mean there is nothing here the
                    // player needs. The gold one says the colony already showed
                    // this feature in use rather than that the tour taught it,
                    // and the tooltip says so in words.
                    GUI.color = alreadyUsed
                        ? BWTTutorialAnchorRenderer.TutorialGold(0.85f)
                        : new Color(1f, 1f, 1f, 0.6f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(markerRect, "✓");
                }

                Rect labelRect = new Rect(
                    markerRect.xMax + 4f,
                    rect.y,
                    Mathf.Max(1f, rect.width - MarkerWidth - 10f),
                    rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = isComplete
                    ? new Color(1f, 1f, 1f, 0.5f)
                    : isHovered ? Widgets.MouseoverOptionColor : Widgets.NormalOptionColor;
                Widgets.Label(labelRect, option.Label);

                string tip = option.Body ?? string.Empty;
                if (alreadyUsed)
                {
                    tip = (tip.Length > 0 ? tip + "\n\n" : string.Empty) +
                          "BWT_Tutorial_AlreadyUsing".Translate();
                }

                if (tip.Length > 0)
                {
                    TooltipHandler.TipRegion(rect, tip);
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
            BWTTutorialProgressSnapshot progress,
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
                bool exhausted = AllOptionsComplete(anchor.Kind, hubs, progress);
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
            BWTTutorialProgressSnapshot progress)
        {
            if (hubs == null || !hubs.TryGetValue(kind, out BWTTutorialHubDefinition hub) ||
                hub.Options.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < hub.Options.Count; i++)
            {
                if (!progress.IsSettled(hub.Options[i].LessonId))
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
            float innerWidth = Mathf.Max(1f, width - PopupPadding * 2f);
            float labelWidth = Mathf.Max(1f, innerWidth - MarkerWidth - 10f);
            float titleHeight = MeasureTextHeight(hub.Title, innerWidth, MinimumTitleHeight);
            var optionHeights = new List<float>(hub.Options.Count);
            float optionsHeight = 0f;
            for (int i = 0; i < hub.Options.Count; i++)
            {
                float optionHeight = MeasureTextHeight(
                    hub.Options[i].Label,
                    labelWidth,
                    MinimumOptionHeight);
                optionHeights.Add(optionHeight);
                optionsHeight += optionHeight;
            }

            float height = PopupPadding * 2f + titleHeight + optionsHeight +
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
            Rect title = new Rect(inner.x, inner.y, inner.width, titleHeight);

            var optionRects = new List<Rect>(hub.Options.Count);
            float rowY = title.yMax;
            for (int i = 0; i < hub.Options.Count; i++)
            {
                optionRects.Add(new Rect(inner.x, rowY, inner.width, optionHeights[i]));
                rowY += optionHeights[i] + OptionGap;
            }

            return new PopupLayout(popup, title, optionRects);
        }

        private static float MeasureTextHeight(string text, float width, float minimumHeight)
        {
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float height = Mathf.Ceil(Text.CalcHeight(text ?? string.Empty, width));
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
            return Mathf.Max(minimumHeight, height);
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

        /// <summary>
        /// Whether an event carries a pointer position the list could own. Lives
        /// here because the list is what claims the pointer.
        /// </summary>
        internal static bool IsPointerEvent(EventType type)
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
