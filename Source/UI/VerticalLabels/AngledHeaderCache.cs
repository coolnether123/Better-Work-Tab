using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Caches angled header layout (text, quad, bounds) and tooltips so we only rebuild when inputs change.
    /// </summary>
    internal static class AngledHeaderCache
    {
        internal sealed class CachedHeaderData
        {
            public AngledLabelDrawer.AngledLabelLayout Layout;
            public Vector2[] Quad;
            public Rect Bounds;
            public Rect SourceRect;
            public float UiScale;
            public bool ShowMarker;
            public string Text;
        }

        private static readonly Dictionary<string, CachedHeaderData> LayoutCache =
            new Dictionary<string, CachedHeaderData>(32);

        private static readonly Dictionary<string, string> TooltipCache =
            new Dictionary<string, string>(32);

        private static readonly Dictionary<string, (Vector2 size, int lastFrame)> TextSizeCache =
            new Dictionary<string, (Vector2, int)>(64);

        internal static bool TryGetLayout(
            Rect headerRect,
            WorkTypeDef workType,
            float rotCos,
            float rotSin,
            float stemBottomGap,
            out CachedHeaderData cached)
        {
            cached = null;
            if (workType == null)
            {
                return false;
            }

            string baseText = workType.labelShort;
            if (string.IsNullOrEmpty(baseText))
                baseText = workType.label;
            if (string.IsNullOrEmpty(baseText))
                baseText = workType.defName;

            string text = baseText.NullOrEmpty() ? "Work" : baseText.CapitalizeFirst();

            bool shouldShowMarker = MainTabWindow_BetterWork.ShouldShowColumnMarker(workType);
            string displayText = shouldShowMarker ? text + "*" : text;

            string key = workType.defName ?? displayText;
            float uiScale = Prefs.UIScale;
            int currentFrame = Time.frameCount;

            if (LayoutCache.TryGetValue(key, out var existing))
            {
                bool sameRect = RectApproximatelyEqual(headerRect, existing.SourceRect);
                bool sameMarker = existing.ShowMarker == shouldShowMarker;
                bool sameText = existing.Text == displayText;
                bool sameUi = Mathf.Approximately(existing.UiScale, uiScale);

                if (sameRect && sameMarker && sameText && sameUi)
                {
                    cached = existing;
                    TouchTextSize(displayText, uiScale, currentFrame);
                    return true;
                }
            }

            Vector2 textSize = GetTextSize(displayText, uiScale, currentFrame);

            var layout = new AngledLabelDrawer.AngledLabelLayout(displayText, textSize, shouldShowMarker);
            var quad = BuildHighlightQuad(layout, headerRect, rotCos, rotSin);
            Rect bounds = GetAabb(quad);

            var updated = new CachedHeaderData
            {
                Layout = layout,
                Quad = quad,
                Bounds = bounds,
                SourceRect = headerRect,
                UiScale = uiScale,
                ShowMarker = shouldShowMarker,
                Text = displayText
            };

            LayoutCache[key] = updated;
            cached = updated;
            return true;
        }

        internal static string GetTooltip(PawnColumnWorker_WorkPriority worker)
        {
            var workType = worker?.def?.workType;
            if (workType == null)
            {
                return string.Empty;
            }

            string key = workType.defName ?? workType.labelShort ?? "Work";
            if (TooltipCache.TryGetValue(key, out var cached) && !cached.NullOrEmpty())
            {
                return cached;
            }

            string gerund = workType.gerundLabel ?? workType.labelShort ?? workType.defName ?? "Work";
            string desc = workType.description ?? string.Empty;

            TaggedString tip = gerund.CapitalizeFirst().Colorize(ColoredText.TipSectionTitleColor)
                                + "\n\n" + desc
                                + "\n\n" + BuildSpecificWorkListString(workType) + "\n";

            if (worker.def.sortable)
            {
                tip += "\n" + "ClickToSortByThisColumn".Translate().Colorize(ColoredText.SubtleGrayColor);
            }

            if (!SteamDeck.IsSteamDeckInNonKeyboardMode)
            {
                if (Find.PlaySettings.useWorkPriorities)
                {
                    tip += "\n" + "WorkPriorityShiftClickTip".Translate().Colorize(ColoredText.SubtleGrayColor);
                }
                else
                {
                    tip += "\n" + "WorkPriorityShiftClickEnableDisableTip".Translate().Colorize(ColoredText.SubtleGrayColor);
                }
            }

            string resolved = tip.Resolve();
            TooltipCache[key] = resolved;
            return resolved;
        }

        internal static bool IsMouseOver(Vector2[] quad, Vector2 mousePos)
        {
            return PointInConvexQuad(mousePos, quad);
        }

        private static string BuildSpecificWorkListString(WorkTypeDef def)
        {
            if (def?.workGiversByPriority == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < def.workGiversByPriority.Count; i++)
            {
                var giver = def.workGiversByPriority[i];
                if (giver == null) continue;

                sb.Append(" - " + giver.LabelCap);
                if (giver.emergency)
                {
                    sb.Append(" (" + "EmergencyWorkMarker".Translate() + ")");
                }
                if (i < def.workGiversByPriority.Count - 1)
                {
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        private static Vector2[] BuildHighlightQuad(AngledLabelDrawer.AngledLabelLayout layout, Rect headerRect, float rotCos, float rotSin)
        {
            float w = layout.Size.x;
            float h = layout.Size.y;

            // Local points relative to the pivot (0,0)
            Vector2 bl = new Vector2(0f, 0f);
            Vector2 br = new Vector2(w, 0f);
            Vector2 tr = new Vector2(w, -h);
            Vector2 tl = new Vector2(0f, -h);

            // Compute the same pivot as the drawer: bottom-left of header + design offsets
            Vector2 basePivot = new Vector2(headerRect.xMin, headerRect.yMax) + new Vector2(2f, -AngledLabelDrawer.STEM_BOTTOM_GAP);
            
            // Snap to physical pixels the same way
            float s = Prefs.UIScale;
            Vector2 snappedPivot = new Vector2(
                Mathf.Round(basePivot.x * s) / s,
                Mathf.Round(basePivot.y * s) / s
            );

            Vector2 Rotate(Vector2 local)
            {
                // standard 2D rotation: 
                // x' = x*cos - y*sin
                // y' = x*sin + y*cos
                float rx = local.x * rotCos - local.y * rotSin;
                float ry = local.x * rotSin + local.y * rotCos;
                return new Vector2(rx, ry) + snappedPivot;
            }

            return new[] { Rotate(bl), Rotate(br), Rotate(tr), Rotate(tl) };
        }

        private static Rect GetAabb(Vector2[] quad)
        {
            if (quad == null || quad.Length != 4)
            {
                return Rect.zero;
            }

            float minX = quad[0].x;
            float maxX = quad[0].x;
            float minY = quad[0].y;
            float maxY = quad[0].y;

            for (int i = 1; i < quad.Length; i++)
            {
                var p = quad[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static bool RectApproximatelyEqual(Rect a, Rect b)
        {
            return Mathf.Approximately(a.x, b.x)
                   && Mathf.Approximately(a.y, b.y)
                   && Mathf.Approximately(a.width, b.width)
                   && Mathf.Approximately(a.height, b.height);
        }

        private static bool PointInConvexQuad(Vector2 point, Vector2[] quad)
        {
            if (quad == null || quad.Length != 4)
            {
                return false;
            }

            bool? sign = null;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = quad[i];
                Vector2 b = quad[(i + 1) % 4];
                float cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

                if (Mathf.Approximately(cross, 0f))
                {
                    continue;
                }

                bool currentSign = cross > 0f;
                if (sign == null)
                {
                    sign = currentSign;
                }
                else if (sign.Value != currentSign)
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector2 GetTextSize(string displayText, float uiScale, int currentFrame)
        {
            string key = $"{displayText}@{uiScale:F3}";
            if (TextSizeCache.TryGetValue(key, out var cached))
            {
                TextSizeCache[key] = (cached.size, currentFrame);
                return cached.size;
            }

            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 textSize = Text.CalcSize(displayText);
            Text.Font = oldFont;

            if (TextSizeCache.Count >= 100)
            {
                // Remove oldest entry
                string oldestKey = null;
                int oldestFrame = int.MaxValue;
                foreach (var kvp in TextSizeCache)
                {
                    if (kvp.Value.lastFrame < oldestFrame)
                    {
                        oldestFrame = kvp.Value.lastFrame;
                        oldestKey = kvp.Key;
                    }
                }
                if (oldestKey != null)
                {
                    TextSizeCache.Remove(oldestKey);
                }
            }

            TextSizeCache[key] = (textSize, currentFrame);
            return textSize;
        }

        private static void TouchTextSize(string displayText, float uiScale, int currentFrame)
        {
            string key = $"{displayText}@{uiScale:F3}";
            if (TextSizeCache.TryGetValue(key, out var cached))
            {
                TextSizeCache[key] = (cached.size, currentFrame);
            }
        }
    }
}
