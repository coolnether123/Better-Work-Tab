using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// Draws visual-only input demonstrations over live tutorial anchors. The
    /// real Work-tab handlers remain the sole owners of input and lesson state.
    /// </summary>
    internal static class BWTTutorialGestureDemo
    {
        private const float PointerSize = 32f;
        private const float ArrowLength = 54f;
        private const float ClickCycleSeconds = 3.7f;
        private const float DragCycleSeconds = 4.4f;
        private static Texture2D pointerTexture;
        private static float animationStartedAt = -1f;
        private static string animationIdentity = string.Empty;

        private enum GestureKind
        {
            None,
            DragRight,
            CtrlClick,
            ShiftClick
        }

        internal static void Reset()
        {
            animationStartedAt = -1f;
            animationIdentity = string.Empty;
        }

        internal static void Draw(
            string lessonId,
            int phase,
            BWTTutorialAnchor anchor,
            IWorkTabLayoutController layout,
            string giveItATryLabel,
            string ctrlLabel,
            string shiftLabel)
        {
            GestureKind kind = ResolveKind(lessonId, phase);
            if (kind == GestureKind.None || !anchor.IsValid)
            {
                return;
            }

            string identity = lessonId + ":" + phase;
            if (!string.Equals(animationIdentity, identity, System.StringComparison.Ordinal))
            {
                animationIdentity = identity;
                animationStartedAt = Time.realtimeSinceStartup;
            }

            if (animationStartedAt < 0f)
            {
                animationStartedAt = Time.realtimeSinceStartup;
            }

            float cycleSeconds = kind == GestureKind.DragRight
                ? DragCycleSeconds
                : ClickCycleSeconds;
            float elapsed = Mathf.Repeat(Time.realtimeSinceStartup - animationStartedAt, cycleSeconds);
            if (kind == GestureKind.DragRight)
            {
                DrawDragDemo(anchor, layout, elapsed, giveItATryLabel);
                return;
            }

            DrawModifiedClickDemo(
                anchor,
                elapsed,
                kind == GestureKind.CtrlClick ? ctrlLabel : shiftLabel,
                giveItATryLabel);
        }

        private static GestureKind ResolveKind(string lessonId, int phase)
        {
            if (lessonId == BWTGeneralTutorial.HeaderReorderLesson)
            {
                return GestureKind.DragRight;
            }

            if (lessonId == BWTGeneralTutorial.HeaderSubWorkLesson ||
                (lessonId == BWTGeneralTutorial.PriorityScheduleLesson && phase == 0))
            {
                return GestureKind.CtrlClick;
            }

            return lessonId == BWTGeneralTutorial.HeaderGroupLesson
                ? GestureKind.ShiftClick
                : GestureKind.None;
        }

        private static void DrawDragDemo(
            BWTTutorialAnchor anchor,
            IWorkTabLayoutController layout,
            float elapsed,
            string giveItATryLabel)
        {
            const float approachEnd = 0.65f;
            const float pressEnd = 1.05f;
            const float dragEnd = 2.15f;
            const float releaseEnd = 2.55f;
            const float tryEnd = 3.95f;

            float dragDistance = ResolveRightwardDragDistance(anchor, layout);
            Vector2 clickPoint = anchor.Rect.center;
            Vector2 pointer;
            float moveOffset;
            if (elapsed < approachEnd)
            {
                float t = Smooth(elapsed / approachEnd);
                pointer = Vector2.Lerp(clickPoint + new Vector2(-24f, -18f), clickPoint, t);
                moveOffset = 0f;
            }
            else if (elapsed < pressEnd)
            {
                pointer = clickPoint;
                moveOffset = 0f;
                DrawRipple(clickPoint, (elapsed - approachEnd) / (pressEnd - approachEnd));
            }
            else if (elapsed < dragEnd)
            {
                float t = Smooth((elapsed - pressEnd) / (dragEnd - pressEnd));
                moveOffset = dragDistance * t;
                pointer = clickPoint + Vector2.right * moveOffset;
                BWTTutorialAnchorRenderer.DrawOutline(
                    anchor.OffsetBy(Vector2.right * moveOffset),
                    BWTTutorialAnchorRenderer.TutorialGold(Mathf.Lerp(0.35f, 0.82f, t)),
                    2f);
            }
            else
            {
                moveOffset = dragDistance;
                pointer = clickPoint + Vector2.right * dragDistance;
                if (elapsed < releaseEnd)
                {
                    DrawRipple(pointer, (elapsed - dragEnd) / (releaseEnd - dragEnd));
                }
            }

            if (elapsed < releaseEnd)
            {
                DrawMovingArrow(anchor, moveOffset);
                DrawPointer(pointer, 1f);
            }
            else if (elapsed < tryEnd)
            {
                DrawTryPrompt(anchor, giveItATryLabel, 1f);
            }
            else
            {
                float fade = 1f - Mathf.Clamp01((elapsed - tryEnd) / (DragCycleSeconds - tryEnd));
                DrawTryPrompt(anchor, giveItATryLabel, fade);
            }
        }

        private static void DrawModifiedClickDemo(
            BWTTutorialAnchor anchor,
            float elapsed,
            string modifierLabel,
            string giveItATryLabel)
        {
            const float approachEnd = 0.7f;
            const float pressEnd = 1.15f;
            const float releaseEnd = 1.5f;
            const float tryEnd = 3.25f;

            Vector2 clickPoint = anchor.Rect.center;
            if (elapsed < approachEnd)
            {
                float t = Smooth(elapsed / approachEnd);
                Vector2 pointer = Vector2.Lerp(clickPoint + new Vector2(-26f, -20f), clickPoint, t);
                DrawModifierBadge(pointer, modifierLabel, Mathf.Clamp01(t * 1.5f));
                DrawPointer(pointer, Mathf.Clamp01(t * 1.5f));
                return;
            }

            if (elapsed < pressEnd)
            {
                DrawModifierBadge(clickPoint, modifierLabel, 1f);
                DrawRipple(clickPoint, (elapsed - approachEnd) / (pressEnd - approachEnd));
                DrawPointer(clickPoint, 1f);
                return;
            }

            if (elapsed < releaseEnd)
            {
                float t = (elapsed - pressEnd) / (releaseEnd - pressEnd);
                DrawModifierBadge(clickPoint, modifierLabel, 1f - t * 0.35f);
                DrawRipple(clickPoint, t);
                DrawPointer(clickPoint, 1f - t * 0.35f);
                return;
            }

            float alpha = elapsed < tryEnd
                ? 1f
                : 1f - Mathf.Clamp01((elapsed - tryEnd) / (ClickCycleSeconds - tryEnd));
            DrawTryPrompt(anchor, giveItATryLabel, alpha);
        }

        private static float ResolveRightwardDragDistance(
            BWTTutorialAnchor anchor,
            IWorkTabLayoutController layout)
        {
            float sourceX = anchor.Rect.center.x;
            float nearestRight = float.MaxValue;
            if (layout?.Columns != null)
            {
                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    WorkTabLayoutColumn column = layout.Columns[i];
                    if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                        column.HeaderRect.center.x <= sourceX + 4f)
                    {
                        continue;
                    }

                    nearestRight = Mathf.Min(nearestRight, column.HeaderRect.center.x);
                }
            }

            return nearestRight < float.MaxValue
                ? Mathf.Clamp(nearestRight - sourceX + 18f, 72f, 120f)
                : 84f;
        }

        private static void DrawMovingArrow(BWTTutorialAnchor anchor, float moveOffset)
        {
            // Keep the moving arrow in the clear band between RimWorld's
            // priority-direction hint and the angled Work headers.
            float y = Mathf.Max(10f, anchor.Rect.yMin - 8f);
            Vector2 start = new Vector2(anchor.Rect.center.x - ArrowLength * 0.5f + moveOffset, y);
            Vector2 end = start + Vector2.right * ArrowLength;
            Color color = BWTTutorialAnchorRenderer.TutorialGold(0.95f);
            Widgets.DrawLine(start, end, color, 3f);
            Widgets.DrawLine(end, end + new Vector2(-10f, -7f), color, 3f);
            Widgets.DrawLine(end, end + new Vector2(-10f, 7f), color, 3f);
        }

        private static void DrawPointer(Vector2 hotspot, float alpha)
        {
            if (pointerTexture == null)
            {
                pointerTexture = ContentFinder<Texture2D>.Get("UI/Cursors/CursorCustom", false);
            }

            if (pointerTexture == null)
            {
                return;
            }

            // RimWorld's CustomCursor uses a (3, 3) hotspot. Mirroring that
            // offset keeps the animated click point identical to the real cursor.
            Rect pointerRect = new Rect(hotspot.x - 3f, hotspot.y - 3f, PointerSize, PointerSize);
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            GUI.DrawTexture(pointerRect, pointerTexture);
            GUI.color = oldColor;
        }

        private static void DrawRipple(Vector2 center, float progress)
        {
            float t = Mathf.Clamp01(progress);
            DrawCircle(center, Mathf.Lerp(5f, 20f, t),
                BWTTutorialAnchorRenderer.TutorialGold(1f - t), 2f);
            if (t > 0.18f)
            {
                float second = Mathf.Clamp01((t - 0.18f) / 0.82f);
                DrawCircle(center, Mathf.Lerp(4f, 14f, second),
                    Color.white.WithAlpha((1f - second) * 0.8f), 1.5f);
            }
        }

        private static void DrawCircle(Vector2 center, float radius, Color color, float thickness)
        {
            const int segments = 20;
            Vector2 previous = center + Vector2.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                Vector2 current = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Widgets.DrawLine(previous, current, color, thickness);
                previous = current;
            }
        }

        private static void DrawModifierBadge(Vector2 hotspot, string label, float alpha)
        {
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            Rect badge = new Rect(hotspot.x + 30f, hotspot.y + 29f, 50f, 22f);
            Widgets.DrawBoxSolid(badge, new Color(0.05f, 0.06f, 0.07f, 0.9f * alpha));
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            GUI.color = BWTTutorialAnchorRenderer.TutorialGold(alpha);
            Widgets.DrawBox(badge, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(badge, label);
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private static void DrawTryPrompt(BWTTutorialAnchor anchor, string label, float alpha)
        {
            if (string.IsNullOrEmpty(label) || alpha <= 0f)
            {
                return;
            }

            Rect prompt = new Rect(anchor.Rect.center.x - 65f, anchor.Rect.yMax + 8f, 130f, 27f);
            Widgets.DrawBoxSolid(prompt, new Color(0.04f, 0.05f, 0.06f, 0.88f * alpha));
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            GUI.color = BWTTutorialAnchorRenderer.TutorialGold(alpha);
            Widgets.DrawBox(prompt, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(prompt, label);
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private static float Smooth(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
