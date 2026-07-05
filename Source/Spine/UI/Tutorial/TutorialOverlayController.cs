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

            TutorialOverlayLayout visualLayout = GetAnimatedLayout(bounds, focusRects, content.Body);
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

            return evt.type == EventType.MouseDrag || evt.type == EventType.ScrollWheel;
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
            TutorialOverlayLayout visualLayout = GetAnimatedLayout(bounds, focusRects, content.Body);

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
            string body)
        {
            TutorialOverlayLayout target = BuildTargetLayout(bounds, focusRects, body);
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

        private TutorialOverlayLayout BuildTargetLayout(Rect bounds, List<Rect> focusRects, string body)
        {
            var safeFocusRects = focusRects ?? new List<Rect>();
            Rect focusBounds = UnionFocusRects(safeFocusRects, bounds);
            Rect cardRect = GetCardRect(bounds, focusBounds, safeFocusRects, body);
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

        private Rect GetCardRect(Rect boundsSource, Rect focusBounds, List<Rect> focusRects, string body)
        {
            Vector2 size = GetCardSize(boundsSource, body);
            Rect bounds = boundsSource.ContractedBy(style.WindowMargin);
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return new Rect(boundsSource.x, boundsSource.y, size.x, size.y);
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

        private Vector2 GetCardSize(Rect bounds, string body)
        {
            float width = Mathf.Min(style.CardWidth, Mathf.Max(320f, bounds.width - 32f));
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float bodyHeight = Text.CalcHeight(body ?? string.Empty, width - style.CardPadding * 2f);
            Text.Font = oldFont;

            float height = Mathf.Clamp(160f + bodyHeight, 220f, 360f);
            return new Vector2(width, height);
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
            Rect focus = ClampRectToBounds(focusBounds.ExpandedBy(8f), bounds);
            DrawDimRect(new Rect(bounds.xMin, bounds.yMin, bounds.width, Mathf.Max(0f, focus.yMin - bounds.yMin)));
            DrawDimRect(new Rect(bounds.xMin, focus.yMax, bounds.width, Mathf.Max(0f, bounds.yMax - focus.yMax)));
            DrawDimRect(new Rect(bounds.xMin, focus.yMin, Mathf.Max(0f, focus.xMin - bounds.xMin), focus.height));
            DrawDimRect(new Rect(focus.xMax, focus.yMin, Mathf.Max(0f, bounds.xMax - focus.xMax), focus.height));

            Color oldColor = GUI.color;
            for (int i = 0; i < focusRects.Count; i++)
            {
                Rect rect = ClampRectToBounds(focusRects[i], bounds);
                Widgets.DrawBoxSolid(rect, style.FocusFillColor);
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

            Widgets.DrawBoxSolid(badgeRect, new Color(0.05f, 0.05f, 0.05f, 0.95f));
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

            Widgets.DrawBoxSolid(rect, style.DimColor);
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

            Widgets.DrawBoxSolid(rect, style.CardColor);
            GUI.color = style.BorderColor;
            Widgets.DrawBox(rect, 1);

            Rect inner = rect.ContractedBy(style.CardPadding);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = style.TitleColor;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f), content.Title);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            float bodyY = inner.y + 34f;
            float bodyHeight = Mathf.Max(64f, inner.height - (content.HasSecondaryButton ? 128f : 92f));
            Widgets.Label(new Rect(inner.x, bodyY, inner.width, bodyHeight), content.Body);

            GUI.color = Color.white;
            if (content.HasSecondaryButton)
            {
                Rect settingsRect = GetSecondaryButtonRect(rect, content);
                if (Widgets.ButtonText(settingsRect, content.SecondaryButton))
                {
                    onSecondary?.Invoke();
                }
            }

            if (content.HasTertiaryButton)
            {
                Rect tertiaryRect = GetTertiaryButtonRect(rect);
                if (Widgets.ButtonText(tertiaryRect, content.TertiaryButton))
                {
                    onTertiary?.Invoke();
                }
            }

            Rect dismissRect = GetDismissButtonRect(rect);
            if (Widgets.ButtonText(dismissRect, content.DismissButton))
            {
                onDismiss?.Invoke();
            }

            if (content.HasPrimaryButton)
            {
                Rect nextRect = GetPrimaryButtonRect(rect);
                if (Widgets.ButtonText(nextRect, content.PrimaryButton))
                {
                    onPrimary?.Invoke();
                }
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private Rect GetDismissButtonRect(Rect cardRect)
        {
            Rect inner = cardRect.ContractedBy(style.CardPadding);
            float reservedPrimaryWidth = 160f;
            float width = Mathf.Min(198f, Mathf.Max(130f, inner.width - reservedPrimaryWidth));
            return new Rect(inner.x, inner.yMax - 34f, width, 32f);
        }

        private Rect GetPrimaryButtonRect(Rect cardRect)
        {
            Rect inner = cardRect.ContractedBy(style.CardPadding);
            return new Rect(inner.xMax - 150f, inner.yMax - 34f, 150f, 32f);
        }

        private Rect GetSecondaryButtonRect(Rect cardRect, TutorialOverlayContent content)
        {
            Rect inner = cardRect.ContractedBy(style.CardPadding);
            if (!content.HasTertiaryButton)
            {
                return new Rect(inner.x, inner.yMax - 70f, inner.width, 28f);
            }

            float gap = 8f;
            float width = (inner.width - gap) / 2f;
            return new Rect(inner.x, inner.yMax - 70f, width, 28f);
        }

        private Rect GetTertiaryButtonRect(Rect cardRect)
        {
            Rect inner = cardRect.ContractedBy(style.CardPadding);
            float gap = 8f;
            float width = (inner.width - gap) / 2f;
            return new Rect(inner.x + width + gap, inner.yMax - 70f, width, 28f);
        }

        private bool TryGetButtonAt(
            Rect cardRect,
            TutorialOverlayContent content,
            Vector2 mousePosition,
            out TutorialOverlayButton button)
        {
            if (GetDismissButtonRect(cardRect).Contains(mousePosition))
            {
                button = TutorialOverlayButton.Dismiss;
                return true;
            }

            if (content.HasPrimaryButton &&
                GetPrimaryButtonRect(cardRect).Contains(mousePosition))
            {
                button = TutorialOverlayButton.Primary;
                return true;
            }

            if (content.HasSecondaryButton &&
                GetSecondaryButtonRect(cardRect, content).Contains(mousePosition))
            {
                button = TutorialOverlayButton.Secondary;
                return true;
            }

            if (content.HasTertiaryButton &&
                GetTertiaryButtonRect(cardRect).Contains(mousePosition))
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
    }
}
