using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Draws the transient blank-column flash and pixel wave used by sub-work transitions.
    /// </summary>
    internal static class SubWorkTransitionOverlay
    {
        internal static void DrawBlankTransitionFlash(
            IWorkTabLayoutController layout,
            WorkTabLayoutColumn column,
            Rect headerRect,
            float totalHeight)
        {
            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked)
            {
                return;
            }

            float alpha = SubWorkDrilldownState.GetBlankColumnFlashAlpha(column.Column);
            if (alpha <= 0.001f)
            {
                return;
            }

            Rect rect = new Rect(
                headerRect.x,
                layout.TableOrigin.y,
                column.Width,
                layout.HeaderHeight + totalHeight);
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, alpha));
            Widgets.DrawBoxSolid(
                new Rect(rect.center.x - 0.5f, rect.yMin, 1f, rect.height),
                new Color(1f, 1f, 1f, alpha * 0.65f));
        }

        internal static void DrawTransitionPixelWave(
            IWorkTabLayoutController layout,
            float pinnedRowsHeight)
        {
            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked ||
                layout?.Columns == null ||
                !SubWorkDrilldownState.TryGetTransitionWave(out float pivotSlot, out float phase) ||
                !TryGetSubWorkWaveGeometry(layout, pivotSlot, out Rect workBounds, out float pivotX))
            {
                return;
            }

            Rect waveRect = new Rect(
                workBounds.xMin,
                layout.TableOrigin.y,
                workBounds.width,
                layout.HeaderHeight + pinnedRowsHeight + layout.ContentHeight);
            if (waveRect.width <= 1f || waveRect.height <= 1f)
            {
                return;
            }

            const float leadingWidth = 10f;
            const float trailWidth = 64f;
            const float leadingAlpha = 0.24f;
            const float trailAlpha = 0.09f;

            float maxDistance = Mathf.Max(pivotX - waveRect.xMin, waveRect.xMax - pivotX);
            float waveCenter = Mathf.Clamp01(phase) * (maxDistance + trailWidth + leadingWidth);
            float overrun = Mathf.Max(0f, waveCenter - maxDistance);
            float fadeOut = 1f - SmoothStep01(overrun / trailWidth);
            if (fadeOut <= 0.001f)
            {
                return;
            }

            Color oldColor = GUI.color;
            try
            {
                float startX = Mathf.Floor(waveRect.xMin);
                float endX = Mathf.Ceil(waveRect.xMax);
                const float stripWidth = 4f;
                for (float x = startX; x < endX; x += stripWidth)
                {
                    float width = Mathf.Min(stripWidth, endX - x);
                    float sampleX = x + width / 2f;
                    float distance = Mathf.Abs(sampleX - pivotX);
                    float leading = SmoothStep01(1f - Mathf.Abs(distance - waveCenter) / leadingWidth);

                    float behind = waveCenter - distance;
                    float trail = behind > 0f
                        ? Mathf.Pow(Mathf.Clamp01(1f - behind / trailWidth), 1.6f)
                        : 0f;

                    float alpha = fadeOut * ((leading * leadingAlpha) + ((1f - leading) * trail * trailAlpha));
                    if (alpha <= 0.002f)
                    {
                        continue;
                    }

                    float grey = Mathf.Lerp(0.52f, 0.82f, leading);
                    Widgets.DrawBoxSolid(
                        new Rect(x, waveRect.yMin, width, waveRect.height),
                        new Color(grey, grey, grey, alpha));
                }
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static bool TryGetSubWorkWaveGeometry(
            IWorkTabLayoutController layout,
            float pivotSlot,
            out Rect workBounds,
            out float pivotX)
        {
            workBounds = Rect.zero;
            pivotX = 0f;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float nearestDistance = float.MaxValue;
            bool foundPivot = false;

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                int slot = SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column);
                if (slot < 0)
                {
                    continue;
                }

                Rect headerRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                minX = Mathf.Min(minX, headerRect.xMin);
                maxX = Mathf.Max(maxX, headerRect.xMax);

                float slotDistance = Mathf.Abs(pivotSlot - slot);
                if (slotDistance < nearestDistance)
                {
                    nearestDistance = slotDistance;
                    float local = Mathf.Clamp01(pivotSlot - slot + 0.5f);
                    pivotX = Mathf.Lerp(headerRect.xMin, headerRect.xMax, local);
                    foundPivot = true;
                }
            }

            if (!foundPivot || minX >= maxX)
            {
                return false;
            }

            workBounds = new Rect(minX, layout.TableOrigin.y, maxX - minX, 1f);
            return true;
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
