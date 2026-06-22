using UnityEngine;
using RimWorld;
using Verse;
using System.Collections.Generic;
using Better_Work_Tab.Features.WorkGiverReassignments;

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
        private static int ComputeParamSignature(float cos, float sin, float horizontalOffset)
        {
            // Simple hash combine
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + cos.GetHashCode();
                hash = hash * 23 + sin.GetHashCode();
                hash = hash * 23 + horizontalOffset.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Attempts to retrieve a cached layout for an angled header, or calculates it if missing.
        /// </summary>
        /// <returns>True if the layout is valid and available.</returns>
        public static bool TryGetLayout(Rect rect, WorkTypeDef workType, float cos, float sin, float stemGap, float horizontalOffset, out CachedHeaderData cached)
        {
            EnsureFrameCache();

            int currentSig = ComputeParamSignature(cos, sin, horizontalOffset);

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
            Text.Font = GameFont.Small;
            Vector2 size = Text.CalcSize(label);
            
            if (isCJKVertical)
            {
                // In vertical stacking, the 'width' becomes the character width, 
                // and the 'height' becomes the cumulative stack of characters.
                float charH = Text.LineHeight * BetterWorkTabMod.Settings.cjkVerticalKerning;
                size = new Vector2(size.y, label.Length * charH); 
            }
            Text.Font = oldFont;

            // Layout Anchor Logic:
            // For standard angled headers, we use vertical centering relative to the header area.
            // For CJK Vertical headers, we push the text down to the bottom (anchored near the pawn rows) 
            // for maximum space efficiency and a more traditional vertical label aesthetic.
            bool useBottomAnchoredLabel = SubWorkDrilldownState.IsActive && !isCJKVertical;
            float drawWidth = isCJKVertical || useBottomAnchoredLabel ? size.x : rect.height;
            Rect drawRect;
            if (isCJKVertical)
            {
                float yPos = rect.yMax - size.y - AngledLabelDrawer.STEM_BOTTOM_GAP;
                drawRect = new Rect(rect.center.x - drawWidth / 2f + horizontalOffset, yPos, drawWidth, size.y);
            }
            else if (useBottomAnchoredLabel)
            {
                Vector2 anchor = new Vector2(rect.center.x + horizontalOffset, rect.yMax - AngledLabelDrawer.STEM_BOTTOM_GAP);
                Vector2 localUnderlineStart = new Vector2(-drawWidth / 2f, size.y / 2f);
                Vector2 rotatedUnderlineStart = RotatePoint(localUnderlineStart, cos, sin);
                drawRect = new Rect(0f, 0f, drawWidth, size.y)
                {
                    center = anchor - rotatedUnderlineStart
                };
            }
            else
            {
                drawRect = new Rect(0f, 0f, drawWidth, size.y) { center = rect.center };
                drawRect.x += horizontalOffset;
            }
            Vector2 pivot = drawRect.center;

            // Bounds and Quad: 
            // Build the quad centered on the pivot for accurate collision detection.
            float effectiveCos = isCJKVertical ? 1f : cos;
            float effectiveSin = isCJKVertical ? 0f : sin;
            Vector2[] quad = CalculateRotatedQuad(pivot, drawWidth, size.y, effectiveCos, effectiveSin);

            cached = new CachedHeaderData
            {
                Layout = (useBottomAnchoredLabel || isCJKVertical)
                    ? new AngledLabelDrawer.AngledLabelLayout(label, size, pivot, isMoved, isCJKVertical, drawRect)
                    : new AngledLabelDrawer.AngledLabelLayout(label, size, pivot, isMoved, isCJKVertical),
                Quad = quad,
                Bounds = rect, // Approximate screen bounds for early clipping
                ParamSignature = currentSig
            };

            _cache[workType] = cached;
            return true;
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
