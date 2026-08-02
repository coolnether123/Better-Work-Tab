using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.DragDrop;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// Draws visual-only input demonstrations over live tutorial anchors. The
    /// real Work-tab handlers remain the sole owners of input and lesson state.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BWTTutorialGestureDemo
    {
        private const float PointerSize = 32f;
        private const float ArrowLength = 54f;
        private const float ClickCycleSeconds = 3.7f;
        private const float DragCycleSeconds = 4.4f;
        private static Texture2D pointerTexture;
        private static float animationStartedAt = -1f;
        private static string animationIdentity = string.Empty;

        internal enum GestureKind
        {
            None,
            DragRight,
            LeftClick,
            RightClick,
            CtrlClick,
            ShiftClick,
            HoldShift,
            TextEntry
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
            IWorkTabLayoutController layout)
        {
            GestureKind kind = ResolveKind(lessonId, phase);
            if (kind == GestureKind.None || !anchor.IsValid)
            {
                return;
            }

            string identity = "work:" + lessonId + ":" + phase;
            float elapsed = GetElapsed(identity, kind);
            if (kind == GestureKind.DragRight)
            {
                DrawDragDemo(anchor, layout, elapsed, T("BWT_Tutorial_Gesture_DragRight"));
                return;
            }

            DrawClickDemo(anchor.Rect, elapsed, kind, GetGestureLabel(kind));
        }

        /// <summary>
        /// Draws the same visual-only gesture language inside routed windows,
        /// float menus, and dialogs. The owning UI still handles all input.
        /// </summary>
        internal static void DrawExternal(
            string identity,
            Rect target,
            GestureKind kind,
            string prompt = null)
        {
            if (kind == GestureKind.None || target.width <= 0f || target.height <= 0f)
            {
                return;
            }

            float elapsed = GetElapsed("external:" + identity, kind);
            DrawClickDemo(
                target,
                elapsed,
                kind,
                string.IsNullOrEmpty(prompt) ? GetGestureLabel(kind) : prompt);
        }

        internal static void DrawCompletionTransfer(
            string lessonId,
            BWTTutorialAnchor source,
            Rect explanationRect)
        {
            if (!source.IsValid || explanationRect.width <= 0f || explanationRect.height <= 0f)
            {
                return;
            }

            const int particleCount = 32;
            const float breakSeconds = 0.55f;
            const float flightSeconds = 1.55f;
            float elapsed = GetElapsedSinceStart("completion-transfer:" + lessonId);
            float breakProgress = Mathf.Clamp01(elapsed / breakSeconds);
            Vector2 sourceCenter = source.Rect.center;
            Vector2 destination = new Vector2(
                explanationRect.xMin + 10f,
                explanationRect.center.y);
            if (elapsed < breakSeconds)
            {
                BWTTutorialAnchorRenderer.DrawOutline(
                    source,
                    BWTTutorialAnchorRenderer.TutorialGold(1f - breakProgress * 0.78f),
                    Mathf.Lerp(3f, 1f, breakProgress));
            }

            for (int i = 0; i < particleCount; i++)
            {
                float angle = Mathf.PI * 2f * i / particleCount;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 start = sourceCenter + new Vector2(
                    direction.x * source.Rect.width * 0.5f,
                    direction.y * source.Rect.height * 0.5f);
                float delay = i * 0.018f;
                float travel = Mathf.Clamp01((elapsed - breakSeconds * 0.45f - delay) / flightSeconds);
                if (travel <= 0f || travel >= 1f)
                {
                    continue;
                }

                float eased = Smooth(travel);
                Vector2 control = Vector2.Lerp(start, destination, 0.5f) +
                                  new Vector2(0f, -32f - (i % 5) * 5f);
                float inverse = 1f - eased;
                Vector2 point = inverse * inverse * start +
                                2f * inverse * eased * control +
                                eased * eased * destination;
                float size = Mathf.Lerp(9f, 4f, eased);
                float alpha = Mathf.Lerp(0.95f, 0.5f, eased);
                Vector2 trailPoint = Vector2.Lerp(point, start, 0.06f);
                float trailSize = size * 0.62f;
                Widgets.DrawBoxSolid(
                    new Rect(
                        trailPoint.x - trailSize * 0.5f,
                        trailPoint.y - trailSize * 0.5f,
                        trailSize,
                        trailSize),
                    BWTTutorialAnchorRenderer.TutorialGold(alpha * 0.42f));
                Widgets.DrawBoxSolid(
                    new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size),
                    BWTTutorialAnchorRenderer.TutorialGold(alpha));
            }

            float arrival = Mathf.Clamp01((elapsed - 0.88f) / 0.6f);
            if (arrival <= 0f)
            {
                return;
            }

            float pulse = 0.72f + 0.28f * Mathf.Sin(Time.realtimeSinceStartup * 3.5f);
            float highlightAlpha = Mathf.Lerp(0f, pulse, arrival);
            Widgets.DrawBoxSolid(
                explanationRect,
                BWTTutorialAnchorRenderer.TutorialGold(0.055f * arrival));
            Color oldColor = GUI.color;
            GUI.color = BWTTutorialAnchorRenderer.TutorialGold(highlightAlpha);
            Widgets.DrawBox(explanationRect.ExpandedBy(4f), 2);
            GUI.color = oldColor;
            if (arrival < 1f)
            {
                DrawRipple(destination, arrival);
            }
        }

        private static GestureKind ResolveKind(string lessonId, int phase)
        {
            if (lessonId == BWTGeneralTutorial.HeaderReorderLesson)
            {
                return GestureKind.DragRight;
            }

            if (lessonId == BWTGeneralTutorial.PrioritySkillLesson)
            {
                return phase == 0 ? GestureKind.HoldShift : GestureKind.None;
            }

            if (lessonId == BWTGeneralTutorial.PriorityScheduleLesson)
            {
                return phase == 1 ? GestureKind.LeftClick : GestureKind.CtrlClick;
            }

            if (lessonId == BWTGeneralTutorial.PawnMenuLesson ||
                ((lessonId == BWTGeneralTutorial.PawnDividerLesson ||
                  lessonId == BWTGeneralTutorial.PawnAppearanceLesson) && phase == 0))
            {
                return GestureKind.RightClick;
            }

            if (lessonId == BWTGeneralTutorial.HeaderSubWorkLesson)
            {
                return GestureKind.CtrlClick;
            }

            return lessonId == BWTGeneralTutorial.HeaderGroupLesson
                ? GestureKind.ShiftClick
                : GestureKind.None;
        }

        private static float GetElapsed(string identity, GestureKind kind)
        {
            if (kind == GestureKind.HoldShift)
            {
                return GetElapsedSinceStart(identity);
            }

            float cycleSeconds = kind == GestureKind.DragRight
                ? DragCycleSeconds
                : ClickCycleSeconds;
            return Mathf.Repeat(GetElapsedSinceStart(identity), cycleSeconds);
        }

        private static float GetElapsedSinceStart(string identity)
        {
            if (!string.Equals(animationIdentity, identity, System.StringComparison.Ordinal))
            {
                animationIdentity = identity;
                animationStartedAt = Time.realtimeSinceStartup;
            }

            if (animationStartedAt < 0f)
            {
                animationStartedAt = Time.realtimeSinceStartup;
            }

            return Time.realtimeSinceStartup - animationStartedAt;
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
                DrawAnimatedInsertionGuide(layout, pointer.x, anchor.Rect.xMin);
            }
            else
            {
                moveOffset = dragDistance;
                pointer = clickPoint + Vector2.right * dragDistance;
                if (elapsed < releaseEnd)
                {
                    DrawAnimatedInsertionGuide(layout, pointer.x, anchor.Rect.xMin);
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

        private static void DrawClickDemo(
            Rect target,
            float elapsed,
            GestureKind kind,
            string giveItATryLabel)
        {
            const float approachEnd = 0.7f;
            const float pressEnd = 1.15f;
            const float releaseEnd = 1.5f;
            const float tryEnd = 3.25f;

            Vector2 clickPoint = target.center;
            if (kind == GestureKind.HoldShift)
            {
                float approach = Mathf.Clamp01(elapsed / approachEnd);
                Vector2 pointer = Vector2.Lerp(
                    clickPoint + new Vector2(-26f, -20f),
                    clickPoint,
                    Smooth(approach));
                DrawModifierBadge(pointer, GetGestureLabel(kind), Mathf.Clamp01(approach * 1.5f));
                DrawPointer(pointer, Mathf.Clamp01(approach * 1.5f));
                return;
            }

            bool drawRipple = kind != GestureKind.HoldShift;
            if (elapsed < approachEnd)
            {
                float t = Smooth(elapsed / approachEnd);
                Vector2 pointer = Vector2.Lerp(clickPoint + new Vector2(-26f, -20f), clickPoint, t);
                DrawModifierBadge(pointer, GetApproachLabel(kind), Mathf.Clamp01(t * 1.5f));
                DrawPointer(pointer, Mathf.Clamp01(t * 1.5f));
                return;
            }

            if (elapsed < pressEnd)
            {
                DrawModifierBadge(clickPoint, GetGestureLabel(kind), 1f);
                if (drawRipple)
                {
                    DrawRipple(clickPoint, (elapsed - approachEnd) / (pressEnd - approachEnd));
                }
                DrawPointer(clickPoint, 1f);
                return;
            }

            if (elapsed < releaseEnd)
            {
                float t = (elapsed - pressEnd) / (releaseEnd - pressEnd);
                DrawModifierBadge(clickPoint, GetReleaseLabel(kind), 1f - t * 0.35f);
                if (drawRipple)
                {
                    DrawRipple(clickPoint, t);
                }
                DrawPointer(clickPoint, 1f - t * 0.35f);
                return;
            }

            float alpha = elapsed < tryEnd
                ? 1f
                : 1f - Mathf.Clamp01((elapsed - tryEnd) / (ClickCycleSeconds - tryEnd));
            DrawTryPrompt(target, giveItATryLabel, alpha);
        }

        private static string GetApproachLabel(GestureKind kind)
        {
            switch (kind)
            {
                case GestureKind.CtrlClick:
                    return T("BWT_Tutorial_Gesture_HoldCtrl");
                case GestureKind.ShiftClick:
                case GestureKind.HoldShift:
                    return T("BWT_Tutorial_Gesture_HoldShift");
                default:
                    return GetGestureLabel(kind);
            }
        }

        private static string GetReleaseLabel(GestureKind kind)
        {
            switch (kind)
            {
                case GestureKind.CtrlClick:
                    return T("BWT_Tutorial_Gesture_ReleaseCtrl");
                case GestureKind.ShiftClick:
                case GestureKind.HoldShift:
                    return T("BWT_Tutorial_Gesture_ReleaseShift");
                default:
                    return GetGestureLabel(kind);
            }
        }

        private static string GetGestureLabel(GestureKind kind)
        {
            switch (kind)
            {
                case GestureKind.LeftClick:
                    return T("BWT_Tutorial_Gesture_LeftClick");
                case GestureKind.RightClick:
                    return T("BWT_Tutorial_Gesture_RightClick");
                case GestureKind.CtrlClick:
                    return T("BWT_Tutorial_Gesture_CtrlClick");
                case GestureKind.ShiftClick:
                    return T("BWT_Tutorial_Gesture_ShiftClick");
                case GestureKind.HoldShift:
                    return T("BWT_Tutorial_Gesture_HoldShift");
                case GestureKind.TextEntry:
                    return T("BWT_Tutorial_Gesture_Type");
                default:
                    return string.Empty;
            }
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

        private static void DrawAnimatedInsertionGuide(
            IWorkTabLayoutController layout,
            float pointerX,
            float fallbackX)
        {
            if (layout?.Columns == null)
            {
                return;
            }

            int insertionIndex = 0;
            WorkTabLayoutColumn first = default(WorkTabLayoutColumn);
            WorkTabLayoutColumn previous = default(WorkTabLayoutColumn);
            bool hasTarget = false;
            bool foundSlot = false;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                if (!hasTarget)
                {
                    first = column;
                    hasTarget = true;
                }

                if (pointerX < column.HeaderRect.center.x)
                {
                    fallbackX = insertionIndex <= 0
                        ? first.HeaderRect.xMin
                        : column.HeaderRect.xMin;
                    foundSlot = true;
                    break;
                }

                previous = column;
                insertionIndex++;
            }

            if (!hasTarget)
            {
                return;
            }

            float lineX = foundSlot ? fallbackX : previous.HeaderRect.xMax;
            int insetSetting = BetterWorkTabMod.Settings?.columnInsertionLineInset ??
                DefaultSettings.columnInsertionLineInset;
            int inset = Mathf.Clamp(insetSetting, 0, Mathf.RoundToInt(layout.HeaderHeight));
            Rect lineRect = ColumnDragHandler.GetColumnGuideRect(layout, lineX, inset);
            Widgets.DrawBoxSolid(lineRect, Color.white);
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

            GameFont measuredFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float width = Mathf.Clamp(Text.CalcSize(label).x + 16f, 50f, 104f);
            Text.Font = measuredFont;
            Rect badge = new Rect(hotspot.x + 30f, hotspot.y + 29f, width, 22f);
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
            DrawTryPrompt(anchor.Rect, label, alpha);
        }

        private static void DrawTryPrompt(Rect target, string label, float alpha)
        {
            if (string.IsNullOrEmpty(label) || alpha <= 0f)
            {
                return;
            }

            Rect prompt = new Rect(target.center.x - 65f, target.yMax + 8f, 130f, 27f);
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

        private static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }
    }
}
