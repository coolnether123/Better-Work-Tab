using System;
using UnityEngine;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Keeps a retained texture presentation independent from the caller's
    /// current IMGUI tint. Unity multiplies GUI.color into texture pixels, so
    /// a stale alpha here would make cached text dimmer than direct vanilla
    /// drawing. The scope restores the caller's color after the draw.
    /// </summary>
    internal static class RetainedSurfacePresentation
    {
        internal static NeutralTextureTintScope EnterNeutralTextureTint()
        {
            return NeutralTextureTintScope.Enter();
        }

        internal readonly struct NeutralTextureTintScope : IDisposable
        {
            private readonly Color _previousColor;

            private NeutralTextureTintScope(Color previousColor)
            {
                _previousColor = previousColor;
            }

            internal static NeutralTextureTintScope Enter()
            {
                Color previousColor = GUI.color;
                GUI.color = Color.white;
                return new NeutralTextureTintScope(previousColor);
            }

            public void Dispose()
            {
                GUI.color = _previousColor;
            }
        }
    }
}
