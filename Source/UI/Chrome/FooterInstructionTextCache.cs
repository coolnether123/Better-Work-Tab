using System;
using Better_Work_Tab.Foundation.GameState;
using Spine.UI.WidgetExtensions;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Chrome
{
    internal enum FooterPointerKind
    {
        None,
        CtrlClickSchedule,
        GestureAction
    }

    /// <summary>
    /// Caches translated footer instruction composition and truncation.
    /// Pointer and gesture discovery remain in <see cref="WorkTabChrome"/>;
    /// this type only formats the stable text after those live decisions.
    /// </summary>
    internal sealed class FooterInstructionTextCache
    {
        private string _language;
        private long _presentationRevision = long.MinValue;
        private bool _overlayTextValid;
        private bool _overlayShifted;
        private string _overlayText;
        private bool _ctrlClickTextValid;
        private string _ctrlClickText;
        private bool _actionTextValid;
        private bool _actionActive;
        private string _actionText;
        private bool _gestureTextValid;
        private string _gestureInput;
        private string _gestureText;
        private bool _gestureActionTextValid;
        private string _gestureActionInput;
        private string _gestureActionLabelInput;
        private string _gestureActionText;
        private string _overlayInput;
        private string _pointerInput;
        private string _joinedText;
        private bool _compositionValid;
        private float _truncateWidth;
        private string _truncatedText;
        private bool _truncateValid;

        internal string GetInstructionText(
            bool hasOverlayInstruction,
            bool overlayShifted,
            FooterPointerKind pointerKind,
            bool subWorkActive,
            string gesture,
            float textWidth)
        {
            EnsureCacheKey();
            string overlayText = ResolveOverlayText(
                hasOverlayInstruction,
                overlayShifted);
            string pointerText = ResolvePointerText(
                pointerKind,
                subWorkActive,
                gesture);
            string composedText = ComposeInstructionText(overlayText, pointerText);
            return TruncateInstructionText(composedText, textWidth);
        }

        private string ResolveOverlayText(bool hasOverlayInstruction, bool overlayShifted)
        {
            if (!hasOverlayInstruction)
            {
                return null;
            }

            if (!_overlayTextValid || _overlayShifted != overlayShifted)
            {
                _overlayShifted = overlayShifted;
                _overlayText = overlayShifted
                    ? "BWT_Footer_ReleaseShiftForPriorities".Translate()
                    : "BWT_Footer_HoldShiftForSkills".Translate();
                _overlayTextValid = true;
            }

            return _overlayText;
        }

        private string ResolvePointerText(
            FooterPointerKind pointerKind,
            bool subWorkActive,
            string gesture)
        {
            switch (pointerKind)
            {
                case FooterPointerKind.CtrlClickSchedule:
                    if (!_ctrlClickTextValid)
                    {
                        _ctrlClickText = "BWT_Footer_CtrlClickSchedule".Translate();
                        _ctrlClickTextValid = true;
                    }

                    return _ctrlClickText;
                case FooterPointerKind.GestureAction:
                    string actionText = EnsureActionText(subWorkActive);
                    string gestureText = EnsureGestureText(gesture);
                    if (!_gestureActionTextValid ||
                        !String.Equals(
                            _gestureActionInput,
                            gestureText,
                            StringComparison.Ordinal) ||
                        !String.Equals(
                            _gestureActionLabelInput,
                            actionText,
                            StringComparison.Ordinal))
                    {
                        _gestureActionInput = gestureText;
                        _gestureActionLabelInput = actionText;
                        _gestureActionText = "BWT_Footer_GestureAction".Translate(
                            gestureText,
                            actionText);
                        _gestureActionTextValid = true;
                    }

                    return _gestureActionText;
            }

            return null;
        }

        private string ComposeInstructionText(string overlayText, string pointerText)
        {
            if (!_compositionValid ||
                !String.Equals(_overlayInput, overlayText, StringComparison.Ordinal) ||
                !String.Equals(_pointerInput, pointerText, StringComparison.Ordinal))
            {
                _overlayInput = overlayText;
                _pointerInput = pointerText;
                _joinedText = overlayText == null
                    ? pointerText
                    : pointerText == null
                        ? overlayText
                        : overlayText + " | " + pointerText;
                _compositionValid = true;
                _truncateValid = false;
            }

            return _joinedText;
        }

        private string TruncateInstructionText(string composedText, float textWidth)
        {
            if (composedText == null)
            {
                return null;
            }

            float truncateWidth = Mathf.Max(1f, textWidth);
            if (!_truncateValid || _truncateWidth != truncateWidth)
            {
                _truncateWidth = truncateWidth;
                _truncatedText = composedText.Truncate(truncateWidth);
                _truncateValid = true;
            }

            return _truncatedText;
        }

        private string EnsureActionText(bool subWorkActive)
        {
            if (!_actionTextValid || _actionActive != subWorkActive)
            {
                _actionActive = subWorkActive;
                _actionText = subWorkActive
                    ? "BWT_Footer_BackToWorkTypes".Translate()
                    : "BWT_Footer_OpenSpecificJobs".Translate();
                _actionTextValid = true;
            }

            return _actionText;
        }

        private string EnsureGestureText(string gesture)
        {
            if (!_gestureTextValid ||
                !String.Equals(_gestureInput, gesture, StringComparison.Ordinal))
            {
                _gestureInput = gesture;
                _gestureText = gesture.CapitalizeFirst();
                _gestureTextValid = true;
            }

            return _gestureText;
        }

        private void EnsureCacheKey()
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            long presentationRevision = WorkTabPresentationRevision.Current;
            if (String.Equals(_language, language, StringComparison.Ordinal) &&
                _presentationRevision == presentationRevision)
            {
                return;
            }

            _language = language;
            _presentationRevision = presentationRevision;
            _overlayTextValid = false;
            _ctrlClickTextValid = false;
            _actionTextValid = false;
            _gestureTextValid = false;
            _gestureActionTextValid = false;
            _compositionValid = false;
            _truncateValid = false;
            _overlayInput = null;
            _pointerInput = null;
            _joinedText = null;
            _gestureActionInput = null;
            _gestureActionLabelInput = null;
        }
    }
}
