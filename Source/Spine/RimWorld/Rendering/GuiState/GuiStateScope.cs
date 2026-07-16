using System;
using UnityEngine;
using Verse;

namespace Spine.RimWorld.Rendering.GuiState
{
    /// <summary>
    /// Saves and restores mutable IMGUI and Verse text state around a rendering layer.
    /// </summary>
    public struct GuiStateScope : IDisposable
    {
        private readonly Color _color;
        private readonly Color _backgroundColor;
        private readonly Color _contentColor;
        private readonly bool _enabled;
        private readonly Matrix4x4 _matrix;
        private readonly GameFont _font;
        private readonly TextAnchor _anchor;
        private readonly bool _wordWrap;
        private bool _active;

        private GuiStateScope(bool active)
        {
            _color = GUI.color;
            _backgroundColor = GUI.backgroundColor;
            _contentColor = GUI.contentColor;
            _enabled = GUI.enabled;
            _matrix = GUI.matrix;
            _font = Text.Font;
            _anchor = Text.Anchor;
            _wordWrap = Text.WordWrap;
            _active = active;
        }

        public static GuiStateScope Capture()
        {
            return new GuiStateScope(true);
        }

        public void Dispose()
        {
            if (!_active)
            {
                return;
            }

            GUI.color = _color;
            GUI.backgroundColor = _backgroundColor;
            GUI.contentColor = _contentColor;
            GUI.enabled = _enabled;
            GUI.matrix = _matrix;
            Text.Font = _font;
            Text.Anchor = _anchor;
            Text.WordWrap = _wordWrap;
            _active = false;
        }
    }
}
