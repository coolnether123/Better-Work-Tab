using System.Collections.Generic;
using RimWorld;
using Spine.UI.Tutorial;
using Spine.UI.WidgetExtensions;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal readonly struct BWTTutorialAnchor
    {
        internal BWTTutorialAnchor(
            TutorialHubAnchor kind,
            Rect rect,
            Pawn pawn = null,
            WorkTypeDef workType = null,
            WorkGiverDef workGiver = null,
            IList<Vector2> outlinePoints = null)
        {
            Kind = kind;
            Rect = rect;
            Pawn = pawn;
            WorkType = workType;
            WorkGiver = workGiver;
            OutlinePoints = outlinePoints;
        }

        internal TutorialHubAnchor Kind { get; }
        internal Rect Rect { get; }
        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal WorkGiverDef WorkGiver { get; }
        internal IList<Vector2> OutlinePoints { get; }
        internal bool HasCustomOutline => OutlinePoints != null && OutlinePoints.Count >= 3;
        internal bool IsValid => Kind != TutorialHubAnchor.None && Rect.width > 0f && Rect.height > 0f;

        internal bool Contains(Vector2 point)
        {
            if (!HasCustomOutline)
            {
                return Rect.Contains(point);
            }

            bool inside = false;
            int previous = OutlinePoints.Count - 1;
            for (int i = 0; i < OutlinePoints.Count; i++)
            {
                Vector2 currentPoint = OutlinePoints[i];
                Vector2 previousPoint = OutlinePoints[previous];
                bool crosses = (currentPoint.y > point.y) != (previousPoint.y > point.y);
                if (crosses)
                {
                    float intersectX = (previousPoint.x - currentPoint.x) *
                        (point.y - currentPoint.y) /
                        (previousPoint.y - currentPoint.y) + currentPoint.x;
                    if (point.x < intersectX)
                    {
                        inside = !inside;
                    }
                }

                previous = i;
            }

            return inside;
        }

        internal BWTTutorialAnchor OffsetBy(Vector2 offset)
        {
            IList<Vector2> shiftedOutline = null;
            if (HasCustomOutline)
            {
                var points = new Vector2[OutlinePoints.Count];
                for (int i = 0; i < OutlinePoints.Count; i++)
                {
                    points[i] = OutlinePoints[i] + offset;
                }
                shiftedOutline = points;
            }

            return new BWTTutorialAnchor(
                Kind,
                new Rect(Rect.position + offset, Rect.size),
                Pawn,
                WorkType,
                WorkGiver,
                shiftedOutline);
        }
    }

    /// <summary>Draws rectangular and rotated tutorial anchors through one shared path.</summary>
    internal static class BWTTutorialAnchorRenderer
    {
        internal static Color TutorialGold(float alpha)
        {
            return new Color(1f, 0.78f, 0.22f, Mathf.Clamp01(alpha));
        }

        internal static void DrawFill(BWTTutorialAnchor anchor, Color color)
        {
            if (!anchor.IsValid)
            {
                return;
            }

            Widgets.DrawBoxSolid(anchor.Rect, color);
        }

        internal static void DrawOutline(BWTTutorialAnchor anchor, Color color, float thickness)
        {
            Vector2[] path = GetOutlinePath(anchor);
            if (path == null)
            {
                return;
            }

            // One miter-joined mesh for every anchor shape. Per-segment DrawLine
            // calls doubled their pixels wherever two segments met, which read as
            // thickened corners on angled headers and as a heavier box elsewhere.
            ConnectedOutlineDrawer.DrawClosed(path, color, thickness);
        }

        /// <summary>Returns the closed path an anchor outlines, or null when it has none.</summary>
        internal static Vector2[] GetOutlinePath(BWTTutorialAnchor anchor)
        {
            if (!anchor.IsValid)
            {
                return null;
            }

            if (anchor.HasCustomOutline)
            {
                var custom = new Vector2[anchor.OutlinePoints.Count];
                for (int i = 0; i < anchor.OutlinePoints.Count; i++)
                {
                    custom[i] = anchor.OutlinePoints[i];
                }

                return custom;
            }

            Rect rect = anchor.Rect;
            return new[]
            {
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax)
            };
        }
    }
}
