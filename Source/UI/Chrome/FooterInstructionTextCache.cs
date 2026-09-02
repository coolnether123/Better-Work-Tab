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
        private CacheKey _cacheKey;
        private string _cachedInstructionText;
        private bool _hasCachedInstruction;

        internal string GetInstructionText(
            bool hasOverlayInstruction,
            bool overlayShifted,
            FooterPointerKind pointerKind,
            bool subWorkActive,
            string gesture,
            float textWidth)
        {
            float truncateWidth = Mathf.Max(1f, textWidth);
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            CacheKey cacheKey = new CacheKey(
                language,
                WorkTabPresentationRevision.Current,
                hasOverlayInstruction,
                hasOverlayInstruction && overlayShifted,
                pointerKind,
                pointerKind == FooterPointerKind.GestureAction && subWorkActive,
                pointerKind == FooterPointerKind.GestureAction ? gesture : null,
                truncateWidth);
            if (_hasCachedInstruction && _cacheKey.Equals(cacheKey))
            {
                return _cachedInstructionText;
            }

            string overlayText = hasOverlayInstruction
                ? overlayShifted
                    ? "BWT_Footer_ReleaseShiftForPriorities".Translate()
                    : "BWT_Footer_HoldShiftForSkills".Translate()
                : null;
            string pointerText = null;
            switch (pointerKind)
            {
                case FooterPointerKind.CtrlClickSchedule:
                    pointerText = "BWT_Footer_CtrlClickSchedule".Translate();
                    break;
                case FooterPointerKind.GestureAction:
                    string actionText = subWorkActive
                        ? "BWT_Footer_BackToWorkTypes".Translate()
                        : "BWT_Footer_OpenSpecificJobs".Translate();
                    pointerText = "BWT_Footer_GestureAction".Translate(
                        gesture.CapitalizeFirst(),
                        actionText);
                    break;
            }

            string composedText = overlayText == null
                ? pointerText
                : pointerText == null
                    ? overlayText
                    : overlayText + " | " + pointerText;
            _cacheKey = cacheKey;
            _cachedInstructionText = composedText == null
                ? null
                : composedText.Truncate(truncateWidth);
            _hasCachedInstruction = true;
            return _cachedInstructionText;
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            private readonly string _language;
            private readonly long _presentationRevision;
            private readonly bool _hasOverlayInstruction;
            private readonly bool _overlayShifted;
            private readonly FooterPointerKind _pointerKind;
            private readonly bool _subWorkActive;
            private readonly string _gesture;
            private readonly float _truncateWidth;

            internal CacheKey(
                string language,
                long presentationRevision,
                bool hasOverlayInstruction,
                bool overlayShifted,
                FooterPointerKind pointerKind,
                bool subWorkActive,
                string gesture,
                float truncateWidth)
            {
                _language = language;
                _presentationRevision = presentationRevision;
                _hasOverlayInstruction = hasOverlayInstruction;
                _overlayShifted = overlayShifted;
                _pointerKind = pointerKind;
                _subWorkActive = subWorkActive;
                _gesture = gesture;
                _truncateWidth = truncateWidth;
            }

            public bool Equals(CacheKey other)
            {
                return String.Equals(_language, other._language, StringComparison.Ordinal) &&
                       _presentationRevision == other._presentationRevision &&
                       _hasOverlayInstruction == other._hasOverlayInstruction &&
                       _overlayShifted == other._overlayShifted &&
                       _pointerKind == other._pointerKind &&
                       _subWorkActive == other._subWorkActive &&
                       String.Equals(_gesture, other._gesture, StringComparison.Ordinal) &&
                       _truncateWidth == other._truncateWidth;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey && Equals((CacheKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _language?.GetHashCode() ?? 0;
                    hash = (hash * 397) ^ _presentationRevision.GetHashCode();
                    hash = (hash * 397) ^ (_hasOverlayInstruction ? 1 : 0);
                    hash = (hash * 397) ^ (_overlayShifted ? 1 : 0);
                    hash = (hash * 397) ^ (int)_pointerKind;
                    hash = (hash * 397) ^ (_subWorkActive ? 1 : 0);
                    hash = (hash * 397) ^ (_gesture?.GetHashCode() ?? 0);
                    return (hash * 397) ^ _truncateWidth.GetHashCode();
                }
            }
        }
    }
}
