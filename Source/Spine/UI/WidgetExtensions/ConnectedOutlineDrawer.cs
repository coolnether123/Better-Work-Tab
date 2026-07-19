using UnityEngine;
using Verse;

namespace Spine.UI.WidgetExtensions
{
    /// <summary>
    /// Draws thick closed outlines as one miter-joined mesh so adjacent segments
    /// cannot expose gaps at their shared corners.
    /// </summary>
    internal static class ConnectedOutlineDrawer
    {
        private static Material outlineMaterial;

        internal static void DrawClosed(
            Vector2[] points,
            Color outlineColor,
            float width,
            Color? fillColor = null)
        {
            if (Event.current.type != EventType.Repaint ||
                points == null ||
                points.Length < 3 ||
                width <= 0f)
            {
                return;
            }

            Material material = OutlineMaterial;
            if (material == null || !material.SetPass(0))
            {
                return;
            }

            float halfWidth = width * 0.5f;
            int count = points.Length;
            var leftOffsets = new Vector2[count];
            var rightOffsets = new Vector2[count];
            CalculateMiteredOffsets(points, halfWidth, leftOffsets, rightOffsets);

            GL.PushMatrix();
            try
            {
                GL.MultMatrix(GUI.matrix);
                GL.Begin(GL.TRIANGLES);

                if (fillColor.HasValue)
                {
                    GL.Color(fillColor.Value);
                    Vector2 origin = points[0];
                    for (int i = 1; i < count - 1; i++)
                    {
                        GL.Vertex3(origin.x, origin.y, 0f);
                        GL.Vertex3(points[i].x, points[i].y, 0f);
                        GL.Vertex3(points[i + 1].x, points[i + 1].y, 0f);
                    }
                }

                GL.Color(outlineColor);
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    GL.Vertex3(leftOffsets[i].x, leftOffsets[i].y, 0f);
                    GL.Vertex3(leftOffsets[next].x, leftOffsets[next].y, 0f);
                    GL.Vertex3(rightOffsets[next].x, rightOffsets[next].y, 0f);

                    GL.Vertex3(leftOffsets[i].x, leftOffsets[i].y, 0f);
                    GL.Vertex3(rightOffsets[next].x, rightOffsets[next].y, 0f);
                    GL.Vertex3(rightOffsets[i].x, rightOffsets[i].y, 0f);
                }

                GL.End();
            }
            finally
            {
                GL.PopMatrix();
            }
        }

        private static void CalculateMiteredOffsets(
            Vector2[] points,
            float halfWidth,
            Vector2[] leftOffsets,
            Vector2[] rightOffsets)
        {
            int count = points.Length;
            for (int i = 0; i < count; i++)
            {
                Vector2 current = points[i];
                Vector2 previous = points[(i - 1 + count) % count];
                Vector2 next = points[(i + 1) % count];
                Vector2 incoming = current - previous;
                Vector2 outgoing = next - current;

                if (incoming.sqrMagnitude <= 0.001f || outgoing.sqrMagnitude <= 0.001f)
                {
                    Vector2 fallback = outgoing.sqrMagnitude > 0.001f ? outgoing : incoming;
                    Vector2 normal = fallback.sqrMagnitude > 0.001f
                        ? new Vector2(-fallback.y, fallback.x).normalized
                        : Vector2.up;
                    leftOffsets[i] = current + (normal * halfWidth);
                    rightOffsets[i] = current - (normal * halfWidth);
                    continue;
                }

                incoming.Normalize();
                outgoing.Normalize();
                Vector2 incomingNormal = new Vector2(-incoming.y, incoming.x);
                Vector2 outgoingNormal = new Vector2(-outgoing.y, outgoing.x);
                Vector2 miter = incomingNormal + outgoingNormal;
                if (miter.sqrMagnitude <= 0.001f)
                {
                    miter = outgoingNormal;
                }
                else
                {
                    miter.Normalize();
                }

                float denominator = Vector2.Dot(miter, outgoingNormal);
                float miterLength = Mathf.Abs(denominator) > 0.15f
                    ? halfWidth / denominator
                    : halfWidth;
                miterLength = Mathf.Clamp(miterLength, -halfWidth * 4f, halfWidth * 4f);
                leftOffsets[i] = current + (miter * miterLength);
                rightOffsets[i] = current - (miter * miterLength);
            }
        }

        private static Material OutlineMaterial
        {
            get
            {
                if (outlineMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored") ?? ShaderDatabase.Transparent;
                    outlineMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    outlineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    outlineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    outlineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    outlineMaterial.SetInt("_ZWrite", 0);
                }

                return outlineMaterial;
            }
        }
    }
}
