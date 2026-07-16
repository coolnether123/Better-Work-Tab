using System;
using System.Collections.Generic;
using Better_Work_Tab;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Spine.UI.Tutorial
{
    public sealed class TutorialOverlayController
    {
        private readonly TutorialOverlayStyle style;
        private TutorialOverlayButton pressedButton = TutorialOverlayButton.None;
        private bool hasAnimatedLayout;
        private float layoutAnimationStartedAt;
        private TutorialOverlayLayout animationStartLayout;
        private TutorialOverlayLayout animationTargetLayout;
        private TutorialOverlayLayout animatedLayout;

        public TutorialOverlayController(TutorialOverlayStyle style = null)
        {
            this.style = style ?? new TutorialOverlayStyle();
        }

        public bool TryHandleInput(
            Rect bounds,
            TutorialOverlayContent content,
            List<Rect> focusRects,
            Event evt,
            Action onPrimary,
            Action onSecondary,
            Action onTertiary,
            Action onDismiss)
        {
            if (evt == null)
            {
                return false;
            }

            TutorialOverlayLayout visualLayout = GetAnimatedLayout(bounds, focusRects, content);
            Rect cardRect = visualLayout.CardRect;

            if (evt.type == EventType.KeyDown &&
                (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                HandleAcceptKey(content, onPrimary);
                evt.Use();
                return true;
            }

            if (!TryGetButtonAt(cardRect, content, evt.mousePosition, out TutorialOverlayButton hoveredButton) &&
                !cardRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                pressedButton = hoveredButton;
                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                TutorialOverlayButton releasedButton = pressedButton;
                pressedButton = TutorialOverlayButton.None;

                if (releasedButton != TutorialOverlayButton.None && releasedButton == hoveredButton)
                {
                    if (releasedButton == TutorialOverlayButton.Dismiss)
                    {
                        onDismiss?.Invoke();
                    }
                    else if (releasedButton == TutorialOverlayButton.Secondary)
                    {
                        onSecondary?.Invoke();
                    }
                    else if (releasedButton == TutorialOverlayButton.Tertiary)
                    {
                        onTertiary?.Invoke();
                    }
                    else if (releasedButton == TutorialOverlayButton.Primary)
                    {
                        onPrimary?.Invoke();
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

        public bool ContainsPointer(
            Rect bounds,
            TutorialOverlayContent content,
            List<Rect> focusRects,
            Vector2 pointer)
        {
            return GetAnimatedLayout(bounds, focusRects, content).CardRect.Contains(pointer);
        }

        public bool TryHandleAcceptKey(TutorialOverlayContent content, Action onPrimary)
        {
            HandleAcceptKey(content, onPrimary);
            Event.current?.Use();
            return true;
        }

        public void Draw(
            Rect bounds,
            TutorialOverlayContent content,
            List<Rect> focusRects,
            List<TutorialOverlayShortcutHint> shortcutHints,
            Action onPrimary,
            Action onSecondary,
            Action onTertiary,
            Action onDismiss)
        {
            TutorialOverlayLayout visualLayout = GetAnimatedLayout(bounds, focusRects, content);

            DrawSpotlight(bounds, visualLayout.FocusBounds, visualLayout.FocusRects);
            DrawShortcutHints(bounds, visualLayout.FocusRects, shortcutHints);
            if (visualLayout.FocusRects.Count > 0)
            {
                DrawConnector(visualLayout.CardRect, visualLayout.FocusBounds);
            }

            DrawCard(visualLayout.CardRect, content, onPrimary, onSecondary, onTertiary, onDismiss);
        }

        public void ResetAnimation()
        {
            hasAnimatedLayout = false;
            pressedButton = TutorialOverlayButton.None;
        }

        private void HandleAcceptKey(TutorialOverlayContent content, Action onPrimary)
        {
            if (content.HasPrimaryButton)
            {
                onPrimary?.Invoke();
            }
            else
            {
                UISoundCompat.TickLow.PlayOneShotOnCamera();
            }
        }

        private TutorialOverlayLayout GetAnimatedLayout(
            Rect bounds,
            List<Rect> focusRects,
            TutorialOverlayContent content)
        {
            TutorialOverlayLayout target = BuildTargetLayout(bounds, focusRects, content);
            if (!hasAnimatedLayout)
            {
                animationStartLayout = target;
                animationTargetLayout = target;
                animatedLayout = target;
                layoutAnimationStartedAt = Time.realtimeSinceStartup;
                hasAnimatedLayout = true;
                return animatedLayout;
            }

            if (!LayoutsApproximatelyEqual(animationTargetLayout, target))
            {
                animationStartLayout = AlignLayoutForAnimation(animatedLayout, target);
                animationTargetLayout = target;
                layoutAnimationStartedAt = Time.realtimeSinceStartup;
            }

            float progress = style.LayoutAnimationSeconds <= 0f
                ? 1f
                : Mathf.Clamp01((Time.realtimeSinceStartup - layoutAnimationStartedAt) / style.LayoutAnimationSeconds);
            progress = SmoothStep01(progress);
            animatedLayout = LerpLayout(animationStartLayout, animationTargetLayout, progress);
            return animatedLayout;
        }

        private TutorialOverlayLayout BuildTargetLayout(
            Rect bounds,
            List<Rect> focusRects,
            TutorialOverlayContent content)
        {
            var safeFocusRects = focusRects ?? new List<Rect>();
            Rect focusBounds = UnionFocusRects(safeFocusRects, bounds);
            Rect cardRect = GetCardRect(bounds, focusBounds, safeFocusRects, content);
            return new TutorialOverlayLayout(cardRect, focusBounds, safeFocusRects);
        }

        private TutorialOverlayLayout AlignLayoutForAnimation(TutorialOverlayLayout current, TutorialOverlayLayout target)
        {
            var focusRects = new List<Rect>(target.FocusRects.Count);
            for (int i = 0; i < target.FocusRects.Count; i++)
            {
                focusRects.Add(i < current.FocusRects.Count ? current.FocusRects[i] : current.FocusBounds);
            }

            return new TutorialOverlayLayout(current.CardRect, current.FocusBounds, focusRects);
        }

        private static TutorialOverlayLayout LerpLayout(TutorialOverlayLayout start, TutorialOverlayLayout target, float t)
        {
            var focusRects = new List<Rect>(target.FocusRects.Count);
            for (int i = 0; i < target.FocusRects.Count; i++)
            {
                Rect startRect = i < start.FocusRects.Count ? start.FocusRects[i] : start.FocusBounds;
                focusRects.Add(LerpRect(startRect, target.FocusRects[i], t));
            }

            return new TutorialOverlayLayout(
                LerpRect(start.CardRect, target.CardRect, t),
                LerpRect(start.FocusBounds, target.FocusBounds, t),
                focusRects);
        }

        private static Rect LerpRect(Rect start, Rect end, float t)
        {
            return new Rect(
                Mathf.Lerp(start.x, end.x, t),
                Mathf.Lerp(start.y, end.y, t),
                Mathf.Lerp(start.width, end.width, t),
                Mathf.Lerp(start.height, end.height, t));
        }

        private static bool LayoutsApproximatelyEqual(TutorialOverlayLayout a, TutorialOverlayLayout b)
        {
            if (!RectApproximatelyEqual(a.CardRect, b.CardRect) ||
                !RectApproximatelyEqual(a.FocusBounds, b.FocusBounds) ||
                a.FocusRects.Count != b.FocusRects.Count)
            {
                return false;
            }

            for (int i = 0; i < a.FocusRects.Count; i++)
            {
                if (!RectApproximatelyEqual(a.FocusRects[i], b.FocusRects[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RectApproximatelyEqual(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f &&
                   Mathf.Abs(a.y - b.y) < 0.5f &&
                   Mathf.Abs(a.width - b.width) < 0.5f &&
                   Mathf.Abs(a.height - b.height) < 0.5f;
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private Rect GetCardRect(
            Rect boundsSource,
            Rect focusBounds,
            List<Rect> focusRects,
            TutorialOverlayContent content)
        {
            Vector2 size = GetCardSize(boundsSource, content);
            Rect bounds = boundsSource.ContractedBy(style.WindowMargin);
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return new Rect(boundsSource.x, boundsSource.y, size.x, size.y);
            }

            if (focusRects == null || focusRects.Count == 0)
            {
                return ClampCardToBounds(
                    new Rect(
                        bounds.xMin + 24f,
                        bounds.yMin + Mathf.Min(42f, Mathf.Max(12f, bounds.height * 0.16f)),
                        size.x,
                        size.y),
                    bounds);
            }

            var candidates = new List<Rect>
            {
                ClampCardToBounds(new Rect(focusBounds.center.x - size.x / 2f, focusBounds.yMin - size.y - style.CardGap, size.x, size.y), bounds),
                ClampCardToBounds(new Rect(focusBounds.center.x - size.x / 2f, focusBounds.yMax + style.CardGap, size.x, size.y), bounds),
                ClampCardToBounds(new Rect(focusBounds.xMin - size.x - style.CardGap, focusBounds.center.y - size.y / 2f, size.x, size.y), bounds),
                ClampCardToBounds(new Rect(focusBounds.xMax + style.CardGap, focusBounds.center.y - size.y / 2f, size.x, size.y), bounds),
                new Rect(bounds.xMin, bounds.yMin, size.x, size.y),
                new Rect(bounds.xMax - size.x, bounds.yMin, size.x, size.y),
                new Rect(bounds.xMin, bounds.yMax - size.y, size.x, size.y),
                new Rect(bounds.xMax - size.x, bounds.yMax - size.y, size.x, size.y)
            };

            Rect best = candidates[0];
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                Rect candidate = ClampCardToBounds(candidates[i], bounds);
                float score = ScoreCardPlacement(candidate, focusBounds, focusRects);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private Vector2 GetCardSize(Rect bounds, TutorialOverlayContent content)
        {
            float availableWidth = Mathf.Max(220f, bounds.width - style.WindowMargin * 2f);
            float width = Mathf.Min(style.CardWidth, availableWidth);
            float innerWidth = Mathf.Max(1f, width - style.CardPadding * 2f);
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float bodyHeight = Text.CalcHeight(content.Body, innerWidth);
            Text.Font = oldFont;

            TutorialOverlayButtonLayout buttons = CreateButtonLayout(innerWidth, content);
            float desiredHeight = style.CardPadding * 2f + 40f + Mathf.Max(64f, bodyHeight) + 12f + buttons.Height;
            float maximumHeight = Mathf.Max(180f, Mathf.Min(420f, bounds.height - style.WindowMargin * 2f));
            float minimumHeight = Mathf.Min(210f, maximumHeight);
            float height = Mathf.Clamp(desiredHeight, minimumHeight, maximumHeight);
            return new Vector2(width, height);
        }

        private static bool IsPointerEvent(EventType type)
        {
            return type == EventType.MouseDown ||
                   type == EventType.MouseUp ||
                   type == EventType.MouseMove ||
                   type == EventType.MouseDrag ||
                   type == EventType.ScrollWheel;
        }

        private static Rect ClampCardToBounds(Rect rect, Rect bounds)
        {
            float x = Mathf.Clamp(rect.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - rect.width));
            float y = Mathf.Clamp(rect.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - rect.height));
            return new Rect(x, y, rect.width, rect.height);
        }

        private static float ScoreCardPlacement(Rect cardRect, Rect focusBounds, List<Rect> focusRects)
        {
            float overlapArea = IntersectionArea(cardRect, focusBounds);
            if (focusRects != null)
            {
                for (int i = 0; i < focusRects.Count; i++)
                {
                    overlapArea += IntersectionArea(cardRect, focusRects[i]) * 2f;
                }
            }

            float distance = Vector2.Distance(cardRect.center, focusBounds.center);
            float sidePreference = cardRect.yMax <= focusBounds.yMin || cardRect.yMin >= focusBounds.yMax ? 10000f : 0f;
            return sidePreference + distance - overlapArea * 100f;
        }

        private static float IntersectionArea(Rect a, Rect b)
        {
            float width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            return width * height;
        }

        private void DrawSpotlight(Rect bounds, Rect focusBounds, List<Rect> focusRects)
        {
            if (focusRects == null || focusRects.Count == 0)
            {
                DrawDimRect(bounds);
                return;
            }

            Rect focus = ClampRectToBounds(focusBounds.ExpandedBy(8f), bounds);
            DrawDimRect(new Rect(bounds.xMin, bounds.yMin, bounds.width, Mathf.Max(0f, focus.yMin - bounds.yMin)));
            DrawDimRect(new Rect(bounds.xMin, focus.yMax, bounds.width, Mathf.Max(0f, bounds.yMax - focus.yMax)));
            DrawDimRect(new Rect(bounds.xMin, focus.yMin, Mathf.Max(0f, focus.xMin - bounds.xMin), focus.height));
            DrawDimRect(new Rect(focus.xMax, focus.yMin, Mathf.Max(0f, bounds.xMax - focus.xMax), focus.height));

            Color oldColor = GUI.color;
            for (int i = 0; i < focusRects.Count; i++)
            {
                Rect rect = ClampRectToBounds(focusRects[i], bounds);
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, style.FocusFillColor);
                GUI.color = style.FocusColor;
                Widgets.DrawBox(rect, 2);
            }

            GUI.color = oldColor;
        }

        private void DrawShortcutHints(
            Rect bounds,
            List<Rect> focusRects,
            List<TutorialOverlayShortcutHint> shortcutHints)
        {
            if (focusRects == null || shortcutHints == null)
            {
                return;
            }

            for (int i = 0; i < shortcutHints.Count; i++)
            {
                TutorialOverlayShortcutHint hint = shortcutHints[i];
                if (string.IsNullOrEmpty(hint.Text))
                {
                    continue;
                }

                if (hint.FocusIndex >= 0)
                {
                    if (hint.FocusIndex < focusRects.Count)
                    {
                        DrawShortcutHint(hint.Text, ClampRectToBounds(focusRects[hint.FocusIndex], bounds), bounds);
                    }
                    continue;
                }

                for (int focusIndex = 0; focusIndex < focusRects.Count; focusIndex++)
                {
                    DrawShortcutHint(hint.Text, ClampRectToBounds(focusRects[focusIndex], bounds), bounds);
                }
            }
        }

        private void DrawShortcutHint(string text, Rect focusRect, Rect bounds)
        {
            if (focusRect.width <= 1f || focusRect.height <= 1f)
            {
                return;
            }

            const float badgeHeight = 24f;
            const float padding = 8f;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 size = Text.CalcSize(text);
            Text.Font = oldFont;

            float width = Mathf.Max(92f, size.x + padding * 2f);
            Rect badgeRect = new Rect(
                focusRect.center.x - width / 2f,
                focusRect.yMin - badgeHeight - 6f,
                width,
                badgeHeight);

            if (badgeRect.yMin < bounds.yMin + 4f)
            {
                badgeRect.y = focusRect.yMax + 6f;
            }

            badgeRect = ClampCardToBounds(badgeRect, bounds.ContractedBy(4f));

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            oldFont = Text.Font;

            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(badgeRect, new Color(0.05f, 0.05f, 0.05f, 0.95f));
            GUI.color = style.FocusColor;
            Widgets.DrawBox(badgeRect, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(badgeRect, text);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private void DrawDimRect(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, style.DimColor);
        }

        private static Rect ClampRectToBounds(Rect rect, Rect bounds)
        {
            float xMin = Mathf.Clamp(rect.xMin, bounds.xMin, bounds.xMax);
            float yMin = Mathf.Clamp(rect.yMin, bounds.yMin, bounds.yMax);
            float xMax = Mathf.Clamp(rect.xMax, bounds.xMin, bounds.xMax);
            float yMax = Mathf.Clamp(rect.yMax, bounds.yMin, bounds.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void DrawConnector(Rect cardRect, Rect focusBounds)
        {
            Vector2 start = ClosestPointOnRect(cardRect, focusBounds.center);
            Vector2 end = ClosestPointOnRect(focusBounds, cardRect.center);
            Widgets.DrawLine(start, end, style.FocusColor, 2f);
        }

        private static Vector2 ClosestPointOnRect(Rect rect, Vector2 target)
        {
            return new Vector2(
                Mathf.Clamp(target.x, rect.xMin, rect.xMax),
                Mathf.Clamp(target.y, rect.yMin, rect.yMax));
        }

        private void DrawCard(
            Rect rect,
            TutorialOverlayContent content,
            Action onPrimary,
            Action onSecondary,
            Action onTertiary,
            Action onDismiss)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, style.CardColor);
            GUI.color = style.BorderColor;
            Widgets.DrawBox(rect, 1);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 3f), style.AccentColor);

            Rect inner = rect.ContractedBy(style.CardPadding);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = style.TitleColor;
            Widgets.Label(new Rect(inner.x, inner.y + 2f, inner.width, 30f), content.Title);

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.9f, 0.91f, 0.9f, 1f);
            float bodyY = inner.y + 40f;
            TutorialOverlayButtonLayout buttons = CreateButtonLayout(inner.width, content);
            float buttonTop = inner.yMax - buttons.Height;
            float bodyHeight = Mathf.Max(0f, buttonTop - bodyY - 12f);
            Widgets.Label(new Rect(inner.x, bodyY, inner.width, bodyHeight), content.Body);

            GUI.color = Color.white;
            if (content.HasSecondaryButton)
            {
                Rect settingsRect = OffsetButtonRect(buttons.Secondary, inner, buttonTop);
                if (Better_Work_Tab.WidgetsCompat.ButtonText(settingsRect, content.SecondaryButton))
                {
                    onSecondary?.Invoke();
                }
            }

            if (content.HasTertiaryButton)
            {
                Rect tertiaryRect = OffsetButtonRect(buttons.Tertiary, inner, buttonTop);
                if (Better_Work_Tab.WidgetsCompat.ButtonText(tertiaryRect, content.TertiaryButton))
                {
                    onTertiary?.Invoke();
                }
            }

            Rect dismissRect = OffsetButtonRect(buttons.Dismiss, inner, buttonTop);
            if (Better_Work_Tab.WidgetsCompat.ButtonText(dismissRect, content.DismissButton))
            {
                onDismiss?.Invoke();
            }

            if (content.HasPrimaryButton)
            {
                Rect nextRect = OffsetButtonRect(buttons.Primary, inner, buttonTop);
                if (Better_Work_Tab.WidgetsCompat.ButtonText(nextRect, content.PrimaryButton))
                {
                    onPrimary?.Invoke();
                }
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private TutorialOverlayButtonLayout CreateButtonLayout(
            float width,
            TutorialOverlayContent content)
        {
            const float buttonHeight = 36f;
            const float gap = 8f;
            float y = 0f;
            Rect secondary = default(Rect);
            Rect tertiary = default(Rect);

            if (content.HasSecondaryButton && content.HasTertiaryButton)
            {
                float secondaryWidth = MeasureButtonWidth(content.SecondaryButton, 120f);
                float tertiaryWidth = MeasureButtonWidth(content.TertiaryButton, 120f);
                if (secondaryWidth + gap + tertiaryWidth <= width)
                {
                    secondary = new Rect(0f, y, secondaryWidth, buttonHeight);
                    tertiary = new Rect(width - tertiaryWidth, y, tertiaryWidth, buttonHeight);
                    y += buttonHeight;
                }
                else
                {
                    secondary = new Rect(0f, y, width, buttonHeight);
                    y += buttonHeight + gap;
                    tertiary = new Rect(0f, y, width, buttonHeight);
                    y += buttonHeight;
                }
            }
            else if (content.HasSecondaryButton)
            {
                secondary = new Rect(0f, y, width, buttonHeight);
                y += buttonHeight;
            }

            if (content.HasSecondaryButton)
            {
                y += gap;
            }

            float dismissWidth = MeasureButtonWidth(content.DismissButton, 130f);
            Rect dismiss;
            Rect primary = default(Rect);
            if (content.HasPrimaryButton)
            {
                float primaryWidth = MeasureButtonWidth(content.PrimaryButton, 100f);
                if (dismissWidth + gap + primaryWidth <= width)
                {
                    dismiss = new Rect(0f, y, dismissWidth, buttonHeight);
                    primary = new Rect(width - primaryWidth, y, primaryWidth, buttonHeight);
                    y += buttonHeight;
                }
                else
                {
                    dismiss = new Rect(0f, y, width, buttonHeight);
                    y += buttonHeight + gap;
                    primary = new Rect(0f, y, width, buttonHeight);
                    y += buttonHeight;
                }
            }
            else
            {
                dismiss = new Rect(0f, y, width, buttonHeight);
                y += buttonHeight;
            }

            return new TutorialOverlayButtonLayout(dismiss, primary, secondary, tertiary, y);
        }

        private static float MeasureButtonWidth(string label, float minimumWidth)
        {
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float measuredWidth = Text.CalcSize(label ?? string.Empty).x;
            Text.Font = oldFont;
            return Mathf.Max(minimumWidth, measuredWidth + 28f);
        }

        private static Rect OffsetButtonRect(Rect relativeRect, Rect inner, float buttonTop)
        {
            return new Rect(
                inner.x + relativeRect.x,
                buttonTop + relativeRect.y,
                relativeRect.width,
                relativeRect.height);
        }

        private bool TryGetButtonAt(
            Rect cardRect,
            TutorialOverlayContent content,
            Vector2 mousePosition,
            out TutorialOverlayButton button)
        {
            Rect inner = cardRect.ContractedBy(style.CardPadding);
            TutorialOverlayButtonLayout buttons = CreateButtonLayout(inner.width, content);
            float buttonTop = inner.yMax - buttons.Height;

            if (OffsetButtonRect(buttons.Dismiss, inner, buttonTop).Contains(mousePosition))
            {
                button = TutorialOverlayButton.Dismiss;
                return true;
            }

            if (content.HasPrimaryButton &&
                OffsetButtonRect(buttons.Primary, inner, buttonTop).Contains(mousePosition))
            {
                button = TutorialOverlayButton.Primary;
                return true;
            }

            if (content.HasSecondaryButton &&
                OffsetButtonRect(buttons.Secondary, inner, buttonTop).Contains(mousePosition))
            {
                button = TutorialOverlayButton.Secondary;
                return true;
            }

            if (content.HasTertiaryButton &&
                OffsetButtonRect(buttons.Tertiary, inner, buttonTop).Contains(mousePosition))
            {
                button = TutorialOverlayButton.Tertiary;
                return true;
            }

            button = TutorialOverlayButton.None;
            return false;
        }

        private static Rect UnionFocusRects(List<Rect> rects, Rect fallback)
        {
            if (rects == null || rects.Count == 0)
            {
                return fallback;
            }

            Rect union = rects[0];
            for (int i = 1; i < rects.Count; i++)
            {
                union = Union(union, rects[i]);
            }

            return union;
        }

        private static Rect Union(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private enum TutorialOverlayButton
        {
            None,
            Dismiss,
            Secondary,
            Tertiary,
            Primary
        }

        private readonly struct TutorialOverlayButtonLayout
        {
            public TutorialOverlayButtonLayout(
                Rect dismiss,
                Rect primary,
                Rect secondary,
                Rect tertiary,
                float height)
            {
                Dismiss = dismiss;
                Primary = primary;
                Secondary = secondary;
                Tertiary = tertiary;
                Height = height;
            }

            public Rect Dismiss { get; }
            public Rect Primary { get; }
            public Rect Secondary { get; }
            public Rect Tertiary { get; }
            public float Height { get; }
        }
    }
}
