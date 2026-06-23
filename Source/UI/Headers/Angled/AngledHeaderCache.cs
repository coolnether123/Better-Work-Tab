using UnityEngine;
using RimWorld;
using Verse;
using System.Collections.Generic;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGiverReassignments;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Caches the geometry and layout of angled headers to avoid expensive calculations every frame.
    /// Invalidation is driven by frame count and signature changes.
    /// </summary>
    public static class AngledHeaderCache
    {
        private static int _lastFrame = -1;
        private static readonly Dictionary<WorkTypeDef, CachedHeaderData> _cache = new Dictionary<WorkTypeDef, CachedHeaderData>();

        /// <summary>
        /// Contains all geometric data needed to render and detect mouse-over for an angled header.
        /// </summary>
        public struct CachedHeaderData
        {
            public AngledLabelDrawer.AngledLabelLayout Layout;
            public Rect Bounds;
            public Vector2[] Quad;
            public int ParamSignature; // Hash of cosine, sine, and offset used to detect parameter changes
        }

        /// <summary>
        /// Manually clears the entire layout cache.
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        public static bool TryGetBounds(WorkTypeDef workType, out Rect bounds)
        {
            if (workType != null && _cache.TryGetValue(workType, out var cached))
            {
                bounds = cached.Bounds;
                return true;
            }

            bounds = Rect.zero;
            return false;
        }

        /// <summary>
        /// Clears the cache if it hasn't been cleared this frame.
        /// </summary>
        private static void EnsureFrameCache()
        {
            if (Time.frameCount != _lastFrame)
            {
                _cache.Clear();
                _lastFrame = Time.frameCount;
            }
        }

        /// <summary>
        /// Computes a signature for the given parameters to detect changes that should invalidate the cache.
        /// </summary>
        private static int ComputeParamSignature(float cos, float sin, float horizontalOffset, float drawWidthOverride)
        {
            // Simple hash combine
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + cos.GetHashCode();
                hash = hash * 23 + sin.GetHashCode();
                hash = hash * 23 + horizontalOffset.GetHashCode();
                hash = hash * 23 + drawWidthOverride.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Attempts to retrieve a cached layout for an angled header, or calculates it if missing.
        /// </summary>
        /// <returns>True if the layout is valid and available.</returns>
        public static bool TryGetLayout(Rect rect, WorkTypeDef workType, float cos, float sin, float stemGap, float horizontalOffset, out CachedHeaderData cached, float drawWidthOverride = -1f)
        {
            EnsureFrameCache();

            int currentSig = ComputeParamSignature(cos, sin, horizontalOffset, drawWidthOverride);

            if (_cache.TryGetValue(workType, out cached))
            {
                if (cached.ParamSignature == currentSig)
                {
                    return true;
                }
                _cache.Remove(workType);
            }

            // Calculation
            bool isMoved = MainTabWindow_BetterWork.ShouldShowColumnMarker(workType);
            string label = HeaderUtility.GetHeaderText(workType, isMoved);
            
            bool isCJKVertical = HeaderUtility.ShouldUseCJKVerticalLabel(label);

            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            Vector2 size = Text.CalcSize(label);

            if (isCJKVertical)
            {
                // In vertical stacking, the 'width' becomes the character width, 
                // and the 'height' becomes the cumulative stack of characters.
                float charH = Text.LineHeight * BetterWorkTabMod.Settings.cjkVerticalKerning;
                size = new Vector2(size.y, label.Length * charH);
            }
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            // Layout Anchor Logic:
            // For standard angled headers, we use vertical centering relative to the header area.
            // For CJK Vertical headers, we push the text down to the bottom (anchored near the pawn rows) 
            // for maximum space efficiency and a more traditional vertical label aesthetic.
            float drawWidth = isCJKVertical
                ? size.x
                : drawWidthOverride > 0f
                    ? drawWidthOverride
                    : rect.height;
            Rect drawRect;
            if (isCJKVertical)
            {
                float yPos = rect.yMax - size.y - AngledLabelDrawer.STEM_BOTTOM_GAP;
                drawRect = new Rect(rect.center.x - drawWidth / 2f + horizontalOffset, yPos, drawWidth, size.y);
            }
            else
            {
                drawRect = new Rect(0f, 0f, drawWidth, size.y) { center = rect.center };
                drawRect.x += horizontalOffset;

                if (SubWorkDrilldownState.IsActive)
                {
                    drawRect = AnchorSubWorkUnderlineToPriorityRow(drawRect, rect, cos, sin, stemGap, horizontalOffset);
                }
            }
            Vector2 pivot = drawRect.center;

            // Bounds and Quad: 
            // Build the quad centered on the pivot for accurate collision detection.
            float effectiveCos = isCJKVertical ? 1f : cos;
            float effectiveSin = isCJKVertical ? 0f : sin;
            Vector2[] quad = CalculateRotatedQuad(pivot, drawWidth, size.y, effectiveCos, effectiveSin);

            cached = new CachedHeaderData
            {
                Layout = isCJKVertical
                    ? new AngledLabelDrawer.AngledLabelLayout(label, size, pivot, isMoved, isCJKVertical, drawRect)
                    : SubWorkDrilldownState.IsActive
                        ? new AngledLabelDrawer.AngledLabelLayout(label, size, pivot, isMoved, isCJKVertical, drawRect)
                        : new AngledLabelDrawer.AngledLabelLayout(label, size, pivot, isMoved, isCJKVertical),
                Quad = quad,
                Bounds = CalculateBounds(quad),
                ParamSignature = currentSig
            };

            _cache[workType] = cached;
            return true;
        }

        private static Rect AnchorSubWorkUnderlineToPriorityRow(Rect drawRect, Rect headerRect, float cos, float sin, float stemGap, float horizontalOffset)
        {
            Vector2 currentLocalUnderlineStart = new Vector2(-drawRect.width / 2f, drawRect.height / 2f);
            Vector2 currentRotatedLocal = RotatePoint(currentLocalUnderlineStart, cos, sin);

            float baselineWidth = SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(null, drawRect.width);
            Vector2 baselineRotatedLocal = RotatePoint(new Vector2(-baselineWidth / 2f, drawRect.height / 2f), cos, sin);

            float globalBoxTop = headerRect.yMax +
                ((SubWorkDrilldownState.GlobalRowVisibleHeight - SubWorkDrilldownState.GlobalPriorityBoxSize) / 2f);
            Vector2 targetUnderlineStart = new Vector2(
                headerRect.center.x + horizontalOffset + baselineRotatedLocal.x,
                globalBoxTop - stemGap);

            Vector2 pivot = targetUnderlineStart - currentRotatedLocal;
            return new Rect(
                pivot.x - drawRect.width / 2f,
                pivot.y - drawRect.height / 2f,
                drawRect.width,
                drawRect.height);
        }

        private static Vector2[] CalculateRotatedQuad(Vector2 pivot, float labelWidth, float textHeight, float cos, float sin)
        {
            // Expansion for highlight logic (±2f)
            float halfW = (labelWidth / 2f) + 2f;
            float halfH = (textHeight / 2f) + 2f;

            // Local coordinates relative to center pivot (0,0)
            Vector2 p1 = new Vector2(-halfW, -halfH);
            Vector2 p2 = new Vector2(halfW, -halfH);
            Vector2 p3 = new Vector2(halfW, halfH);
            Vector2 p4 = new Vector2(-halfW, halfH);

            return new Vector2[]
            {
                RotatePoint(p1, cos, sin) + pivot,
                RotatePoint(p2, cos, sin) + pivot,
                RotatePoint(p3, cos, sin) + pivot,
                RotatePoint(p4, cos, sin) + pivot
            };
        }

        private static Rect CalculateBounds(Vector2[] quad)
        {
            if (quad == null || quad.Length == 0)
            {
                return Rect.zero;
            }

            float minX = quad[0].x;
            float maxX = quad[0].x;
            float minY = quad[0].y;
            float maxY = quad[0].y;
            for (int i = 1; i < quad.Length; i++)
            {
                minX = Mathf.Min(minX, quad[i].x);
                maxX = Mathf.Max(maxX, quad[i].x);
                minY = Mathf.Min(minY, quad[i].y);
                maxY = Mathf.Max(maxY, quad[i].y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static Vector2 RotatePoint(Vector2 p, float cos, float sin)
        {
            return new Vector2(
                (p.x * cos) - (p.y * sin),
                (p.x * sin) + (p.y * cos)
            );
        }

        /// <summary>
        /// Uses ray-casting algorithm to test if a point is inside a convex polygon (the rotated header quad).
        /// A ray is cast horizontally from the point; crossing count determines inclusion.
        /// </summary>
        public static bool IsMouseOver(Vector2[] quad, Vector2 mousePos)
        {
            if (quad == null || quad.Length < 4) return false;
            
            // Ray-casting: count intersections with polygon edges
            bool insidePolygon = false;
            int prevVertex = quad.Length - 1;
            
            for (int i = 0; i < quad.Length; i++)
            {
                Vector2 current = quad[i];
                Vector2 previous = quad[prevVertex];
                
                // Check if ray crosses this edge
                if (RayIntersectsEdge(previous, current, mousePos))
                {
                    insidePolygon = !insidePolygon;
                }
                
                prevVertex = i;
            }
            
            return insidePolygon;
        }

        private static bool RayIntersectsEdge(Vector2 v1, Vector2 v2, Vector2 point)
        {
            // Horizontal ray from point; does it cross edge v1->v2?
            // (v1.y < point.y && v2.y >= point.y) check if the y-range of the edge contains the point's y
            if ((v1.y < point.y && v2.y >= point.y) || (v2.y < point.y && v1.y >= point.y))
            {
                // Calculate x-coordinate of intersection between horizontal ray and edge.
                // Linear interpolation: x = x1 + (y_target - y1) / (y2 - y1) * (x2 - x1)
                float xIntersection = v1.x + (point.y - v1.y) / (v2.y - v1.y) * (v2.x - v1.x);
                return xIntersection < point.x;
            }
            return false;
        }
    }
}
