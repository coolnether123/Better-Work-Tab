using System;
using Better_Work_Tab.Foundation.GameState;
using Spine.UI.WidgetExtensions;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Chrome
{
    /// <summary>
    /// Retains the stable manual-priority checkbox presentation.
    ///
    /// The owning chrome keeps input, hover, inspection, and the direct draw
    /// fallback live. This cache only owns the stable label geometry and the
    /// two bounded enabled/disabled presentation surfaces.
    /// </summary>
    internal sealed class ManualPriorityChromePresentationCache
    {
        private readonly GUIContent _checkboxContent = new GUIContent();

        private string _checkboxText;
        private string _checkboxLanguage;
        private long _checkboxPresentationRevision = long.MinValue;
        private float _checkboxUiScale;
        private Rect _checkboxSourceRect;
        private Rect _checkboxLabelRect;
        private bool _checkboxPresentationValid;

        private RenderTexture _enabledSurface;
        private RenderTexture _disabledSurface;
        private bool _enabledSurfaceValid;
        private bool _disabledSurfaceValid;
        private bool _enabledSurfaceFailed;
        private bool _disabledSurfaceFailed;

        private string _surfaceLanguage;
        private long _surfacePresentationRevision = long.MinValue;
        private string _surfaceText;
        private string _surfaceHelpText;
        private int _surfaceMaxPriority = -1;
        private float _surfaceUiScale;
        private float _surfacePixelScale;
        private Rect _surfaceCheckboxRect;
        private Rect _surfaceContextRect;
        private int _surfaceFontId;
        private int _surfaceFontSize;
        private int _surfaceFontStyle;
        private bool _surfaceWordWrap;
        private bool _surfaceKeyValid;

        internal GUIContent CheckboxContent => _checkboxContent;

        internal Rect GetCheckboxLabelRect(Rect rect, string checkboxText)
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            float uiScale = Prefs.UIScale;
            if (!_checkboxPresentationValid ||
                !String.Equals(_checkboxText, checkboxText, StringComparison.Ordinal) ||
                !String.Equals(_checkboxLanguage, language, StringComparison.Ordinal) ||
                _checkboxPresentationRevision != presentationRevision ||
                _checkboxUiScale != uiScale ||
                !SameRect(_checkboxSourceRect, rect))
            {
                Rect labelRect = rect;
                labelRect.xMax -= 24f;
                if (uiScale > 1f)
                {
                    float halfScale = uiScale / 2f;
                    if (Math.Abs(halfScale - Math.Floor(halfScale)) > float.Epsilon)
                    {
                        labelRect = LudeonTK.UIScaling.AdjustRectToUIScaling(labelRect);
                    }
                }

                _checkboxText = checkboxText;
                _checkboxContent.text = checkboxText;
                _checkboxLanguage = language;
                _checkboxPresentationRevision = presentationRevision;
                _checkboxUiScale = uiScale;
                _checkboxSourceRect = rect;
                _checkboxLabelRect = labelRect;
                _checkboxPresentationValid = true;
            }

            return _checkboxLabelRect;
        }

        internal bool TryDrawRetained(
            Rect checkboxRect,
            int maxPriority,
            bool enabled,
            string checkboxText,
            string helpText)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return false;
            }

            Rect contextRect = WorkTabChromeGeometry.GetManualPrioritiesContextRect();
            EnsureSurfaceKey(
                checkboxRect,
                contextRect,
                maxPriority,
                checkboxText,
                helpText);
            RenderTexture surface = GetSurface(enabled);
            if (surface == null || !surface.IsCreated())
            {
                return false;
            }

            GUI.DrawTextureWithTexCoords(
                contextRect,
                surface,
                new Rect(0f, 0f, 1f, 1f),
                true);
            return true;
        }

        internal void ReleaseRetainedResources()
        {
            try
            {
                ReleaseSurfaces();
            }
            finally
            {
                _surfaceKeyValid = false;
                _enabledSurfaceFailed = false;
                _disabledSurfaceFailed = false;
            }
        }

        internal void ResetFailureLatchesForReopen()
        {
            _enabledSurfaceFailed = false;
            _disabledSurfaceFailed = false;
        }

        private void EnsureSurfaceKey(
            Rect checkboxRect,
            Rect contextRect,
            int maxPriority,
            string checkboxText,
            string helpText)
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            float uiScale = Prefs.UIScale;
            float pixelScale = Verse.UI.screenWidth > 0
                ? Mathf.Max(1f, (float)Screen.width / Verse.UI.screenWidth)
                : 1f;
            GUIStyle fontStyle = Text.CurFontStyle;
            int fontId = fontStyle?.font != null ? fontStyle.font.GetInstanceID() : 0;
            int fontSize = fontStyle?.fontSize ?? 0;
            int fontStyleValue = (int)(fontStyle?.fontStyle ?? FontStyle.Normal);
            bool wordWrap = Text.WordWrap;
            bool changed = !_surfaceKeyValid ||
                !String.Equals(_surfaceLanguage, language, StringComparison.Ordinal) ||
                _surfacePresentationRevision != presentationRevision ||
                !String.Equals(_surfaceText, checkboxText, StringComparison.Ordinal) ||
                !String.Equals(_surfaceHelpText, helpText, StringComparison.Ordinal) ||
                _surfaceMaxPriority != maxPriority ||
                _surfaceUiScale != uiScale ||
                _surfacePixelScale != pixelScale ||
                _surfaceFontId != fontId ||
                _surfaceFontSize != fontSize ||
                _surfaceFontStyle != fontStyleValue ||
                _surfaceWordWrap != wordWrap ||
                !SameRect(_surfaceCheckboxRect, checkboxRect) ||
                !SameRect(_surfaceContextRect, contextRect);
            if (!changed)
            {
                return;
            }

            ReleaseSurfaces();
            _surfaceLanguage = language;
            _surfacePresentationRevision = presentationRevision;
            _surfaceText = checkboxText;
            _surfaceHelpText = helpText;
            _surfaceMaxPriority = maxPriority;
            _surfaceUiScale = uiScale;
            _surfacePixelScale = pixelScale;
            _surfaceCheckboxRect = checkboxRect;
            _surfaceContextRect = contextRect;
            _surfaceFontId = fontId;
            _surfaceFontSize = fontSize;
            _surfaceFontStyle = fontStyleValue;
            _surfaceWordWrap = wordWrap;
            _surfaceKeyValid = true;
            _enabledSurfaceFailed = false;
            _disabledSurfaceFailed = false;
        }

        private RenderTexture GetSurface(bool enabled)
        {
            RenderTexture surface = enabled ? _enabledSurface : _disabledSurface;
            bool failed = enabled ? _enabledSurfaceFailed : _disabledSurfaceFailed;
            if (failed)
            {
                return null;
            }

            int pixelWidth = Mathf.Max(
                1,
                Mathf.CeilToInt(_surfaceContextRect.width * _surfacePixelScale));
            int pixelHeight = Mathf.Max(
                1,
                Mathf.CeilToInt(_surfaceContextRect.height * _surfacePixelScale));
            if (surface == null ||
                !surface.IsCreated() ||
                surface.width != pixelWidth ||
                surface.height != pixelHeight)
            {
                ReleaseSurface(enabled);
                surface = CreateSurface(pixelWidth, pixelHeight);
                if (enabled)
                {
                    _enabledSurface = surface;
                }
                else
                {
                    _disabledSurface = surface;
                }
            }

            if (surface == null)
            {
                SetSurfaceFailed(enabled);
                return null;
            }

            bool valid = enabled ? _enabledSurfaceValid : _disabledSurfaceValid;
            if (valid)
            {
                return surface;
            }

            if (!BuildSurface(surface, enabled))
            {
                ReleaseSurface(enabled);
                SetSurfaceFailed(enabled);
                return null;
            }

            if (enabled)
            {
                _enabledSurfaceValid = true;
            }
            else
            {
                _disabledSurfaceValid = true;
            }

            return surface;
        }

        private void SetSurfaceFailed(bool enabled)
        {
            if (enabled)
            {
                _enabledSurfaceFailed = true;
            }
            else
            {
                _disabledSurfaceFailed = true;
            }
        }

        private static RenderTexture CreateSurface(int width, int height)
        {
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            {
                return null;
            }

            var surface = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "BWT manual priorities chrome",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!surface.Create())
            {
                UnityEngine.Object.Destroy(surface);
                return null;
            }

            return surface;
        }

        private bool BuildSurface(RenderTexture surface, bool enabled)
        {
            // Retained composition is a state transaction: every Unity and
            // IMGUI value captured here is restored before direct fallback.
            RenderTexture previousTarget = RenderTexture.active;
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;
            try
            {
                RenderTexture.active = surface;
                GL.PushMatrix();
                try
                {
                    PrepareSurfaceTarget();
                    DrawSurfaceContents(enabled, previousWrap);
                }
                finally
                {
                    GL.PopMatrix();
                }

                return true;
            }
            catch (Exception)
            {
                // A failed retained composition must return control to the
                // caller so it can use the live direct presentation.
                return false;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWrap;
                RenderTexture.active = previousTarget;
            }
        }

        private void PrepareSurfaceTarget()
        {
            GL.LoadPixelMatrix(
                0f,
                _surfaceContextRect.width,
                _surfaceContextRect.height,
                0f);
            GL.Clear(true, true, Color.clear);
        }

        private void DrawSurfaceContents(bool enabled, bool wordWrap)
        {
            GUI.BeginGroup(new Rect(
                0f,
                0f,
                _surfaceContextRect.width,
                _surfaceContextRect.height));
            try
            {
                DrawSurfaceControls(enabled, wordWrap);
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        private void DrawSurfaceControls(bool enabled, bool wordWrap)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = wordWrap;
            GUI.color = Color.white;
            DrawSurfaceCheckbox(enabled);
            if (enabled)
            {
                DrawSurfaceHelp();
            }
        }

        private void DrawSurfaceCheckbox(bool enabled)
        {
            Rect localLabelRect = GetCheckboxLabelRect(
                _surfaceCheckboxRect,
                _surfaceText);
            localLabelRect.x -= _surfaceContextRect.x;
            localLabelRect.y -= _surfaceContextRect.y;
            Widgets.Label(localLabelRect, _checkboxContent);
            float localCheckboxX =
                _surfaceCheckboxRect.x +
                _surfaceCheckboxRect.width - 24f -
                _surfaceContextRect.x;
            float localCheckboxY =
                _surfaceCheckboxRect.y +
                (_surfaceCheckboxRect.height - 24f) / 2f -
                _surfaceContextRect.y;
            Widgets.CheckboxDraw(
                localCheckboxX,
                localCheckboxY,
                enabled,
                false,
                24f,
                null,
                null);
        }

        private void DrawSurfaceHelp()
        {
            using (new TextBlock(new Color(1f, 1f, 1f, 0.5f)))
            {
                float helpWidth = _surfaceMaxPriority > 4
                    ? 220f
                    : _surfaceCheckboxRect.width;
                Rect helpRect = new Rect(
                    _surfaceCheckboxRect.x - _surfaceContextRect.x,
                    _surfaceCheckboxRect.y +
                        _surfaceCheckboxRect.height - 6f -
                        _surfaceContextRect.y,
                    helpWidth,
                    60f);
                Widgets.Label(helpRect, _surfaceHelpText);
            }
        }

        private void ReleaseSurface(bool enabled)
        {
            RenderTexture surface = enabled ? _enabledSurface : _disabledSurface;
            if (enabled)
            {
                _enabledSurfaceValid = false;
                _enabledSurface = null;
            }
            else
            {
                _disabledSurfaceValid = false;
                _disabledSurface = null;
            }
            if (surface == null)
            {
                return;
            }

            try
            {
                surface.Release();
            }
            finally
            {
                UnityEngine.Object.Destroy(surface);
            }
        }

        private void ReleaseSurfaces()
        {
            try
            {
                ReleaseSurface(true);
            }
            finally
            {
                ReleaseSurface(false);
            }
        }

        private static bool SameRect(Rect left, Rect right)
        {
            return left.x == right.x &&
                   left.y == right.y &&
                   left.width == right.width &&
                   left.height == right.height;
        }
    }
}
