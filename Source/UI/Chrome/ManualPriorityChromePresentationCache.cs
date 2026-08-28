using System;
using Better_Work_Tab.Foundation.GameState;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Chrome
{
    /// <summary>
    /// Caches the stable manual-priority label content and its translated
    /// geometry. The checkbox, label, help text, input, and inspection visuals
    /// all remain on the live IMGUI presentation pass.
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

        /// <summary>
        /// Compatibility seam for the shared retained-resource teardown
        /// sequence. Manual chrome no longer owns a GPU-backed surface.
        /// </summary>
        internal void ReleaseRetainedResources()
        {
        }

        /// <summary>
        /// Compatibility seam for the Work-tab reopen sequence. There is no
        /// failed surface latch after manual chrome returned to live drawing.
        /// </summary>
        internal void ResetFailureLatchesForReopen()
        {
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
