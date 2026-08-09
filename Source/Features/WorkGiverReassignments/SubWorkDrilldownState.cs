using System;
using System.Collections.Generic;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI.Input;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Local view state for showing one work type's WorkGivers in the main Work tab grid.
    /// The real PawnColumnDefs stay in place; visible work columns become stable slots.
    /// </summary>
    internal static class SubWorkDrilldownState
    {
        internal const float GlobalRowHeight = 30f;
        internal const float GlobalPriorityBoxSize = 25f;
        private const float WaveFeatherSlots = 1.05f;

        private static readonly List<WorkGiver> ActiveWorkGiversBuffer = new List<WorkGiver>();
        private static readonly Dictionary<WorkGiverDef, int> ActiveWorkGiverSlots = new Dictionary<WorkGiverDef, int>();
        private static readonly Dictionary<PawnColumnDef, int> VisibleColumnSlots = new Dictionary<PawnColumnDef, int>();
        private static readonly Dictionary<WorkTypeDef, int> VisibleWorkTypeSlots = new Dictionary<WorkTypeDef, int>();
        private static readonly HashSet<WorkGiverDef> MovedFromBaseline = new HashSet<WorkGiverDef>();
        private static readonly Dictionary<string, ExpandBesideEntry> ExpandBesideEntries =
            new Dictionary<string, ExpandBesideEntry>(StringComparer.Ordinal);
        private static WorkTypeDef _drawingSubWorkParent;
        private static WorkGiverDef _drawingSubWorkGiver;
        private static int _drawingSubWorkSlot = -1;
        private static WorkTypeDef _activeWorkType;
        private static string _cachedWorkTypeDefName;
        private static int _cachedSyncVersion = -1;
        private static int _cachedSlotSignature = int.MinValue;
        private static float _enteredAt;
        private static float _exitingAt;
        private static float _baseHeaderDrawWidth;
        private static int _entryWorkColumnSlot = -1;
        private static int _exitWorkColumnSlot = -1;
        private static float _exitWaveSlotPosition = -1f;
        private static bool _isExiting;
        private static bool _layoutRefreshPending;
        private static Vector2? _returnMousePosition;
        private static Vector2? _returnMouseLocalPosition;
        private static Vector2? _entryNativeCursorPosition;
        private static bool _cursorMovedSinceEnter;

        private const float CursorMoveSuppressThreshold = 12f;
        private const float CursorMoveSuppressGraceSeconds = 0.15f;

        internal static bool IsActive => _activeWorkType != null;

        internal static bool IsExiting => _isExiting;

        internal static bool IsExpandBesideActive => ExpandBesideEntries.Count > 0;

        internal static bool HasAnyDrilldown => IsActive || IsExpandBesideActive;

        internal static bool IsDrawingExpandBesideChild => _drawingSubWorkGiver != null && _drawingSubWorkParent != null;

        internal static WorkTypeDef ActiveWorkType => _activeWorkType;

        // Test-only read seam for the transition invariant. Production rendering still
        // obtains the pivot through GetTransitionPivotSlot and cannot override this value.
        internal static int TransitionSourceWorkColumnSlot => _entryWorkColumnSlot;

        internal static int FocusedHeaderAffordanceSlot
        {
            get
            {
                if (!IsActive)
                {
                    return -1;
                }

                RefreshIfNeeded();
                if (ActiveWorkGiversBuffer.Count == 0)
                {
                    return -1;
                }

                return Mathf.Clamp(_entryWorkColumnSlot, 0, ActiveWorkGiversBuffer.Count - 1);
            }
        }

        internal static IEnumerable<WorkTypeDef> ExpandBesideWorkTypes
        {
            get
            {
                foreach (ExpandBesideEntry entry in ExpandBesideEntries.Values)
                {
                    if (entry?.WorkType != null)
                    {
                        yield return entry.WorkType;
                    }
                }
            }
        }

        internal static int CurrentDrawingHeaderSignature
        {
            get
            {
                if (_drawingSubWorkGiver == null || _drawingSubWorkParent == null)
                {
                    return 0;
                }

                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + _drawingSubWorkParent.shortHash;
                    hash = hash * 31 + _drawingSubWorkGiver.shortHash;
                    hash = hash * 31 + _drawingSubWorkSlot;
                    return hash;
                }
            }
        }

        internal static float BaseHeaderDrawWidth => _baseHeaderDrawWidth;

        internal static int LayoutSignature
        {
            get
            {
                EnsureSlotCache();
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_activeWorkType?.shortHash ?? 0);
                    hash = hash * 31 + WorkGiverReassignmentManager.CurrentSyncVersion;
                    hash = hash * 31 + _cachedSlotSignature;
                    hash = hash * 31 + _entryWorkColumnSlot;
                    hash = hash * 31 + _exitWorkColumnSlot;
                    hash = hash * 31 + Mathf.RoundToInt(_exitWaveSlotPosition * 100f);
                    hash = hash * 31 + (_isExiting ? 1 : 0);
                    hash = hash * 31 + ComputeExpandBesideSignature();
                    return hash;
                }
            }
        }

        internal static int MeasurementSignature
        {
            get
            {
                EnsureSlotCache();
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_activeWorkType?.shortHash ?? 0);
                    hash = hash * 31 + WorkGiverReassignmentManager.CurrentSyncVersion;
                    hash = hash * 31 + _cachedSlotSignature;
                    hash = hash * 31 + _entryWorkColumnSlot;
                    hash = hash * 31 + _exitWorkColumnSlot;
                    hash = hash * 31 + ComputeExpandBesideSignature(includeProgress: false);
                    return hash;
                }
            }
        }

        internal static IReadOnlyList<WorkGiver> ActiveWorkGivers
        {
            get
            {
                RefreshIfNeeded();
                return ActiveWorkGiversBuffer;
            }
        }

        internal static float TransitionAlpha
        {
            get
            {
                if (!IsActive)
                {
                    return 0f;
                }

                if (!UseTransitionAnimation)
                {
                    return 1f;
                }

                if (_isExiting)
                {
                    return 1f - Mathf.Clamp01((Time.realtimeSinceStartup - _exitingAt) / TransitionSeconds);
                }

                return Mathf.Clamp01((Time.realtimeSinceStartup - _enteredAt) / TransitionSeconds);
            }
        }

        internal static float TransitionSeconds =>
            BetterWorkTabSettings.ClampSubWorkTransitionSeconds(
                BetterWorkTabMod.Settings?.subWorkTransitionSeconds ??
                DefaultSettings.subWorkTransitionSeconds);

        // Expand-beside changes are lighter than the full focused-view transition and should
        // feel closer to Fluffy's quick column reveal. Keep the shared speed setting as the
        // user's control while shortening only this presentation.
        private static float ExpandBesideTransitionSeconds => Mathf.Max(0.12f, TransitionSeconds * 0.6f);

        internal static bool UseTransitionAnimation =>
            BetterWorkTabMod.Settings?.enableSubWorkTransitionAnimation ??
            DefaultSettings.enableSubWorkTransitionAnimation;

        internal static BetterWorkTabSettings.SubWorkTransitionStyle TransitionStyle =>
            BetterWorkTabMod.Settings?.subWorkTransitionStyle ??
            DefaultSettings.subWorkTransitionStyle;

        internal static bool UseClassicTransition =>
            TransitionStyle == BetterWorkTabSettings.SubWorkTransitionStyle.ClassicGlideFlash;

        internal static bool UsePixelWaveTransition =>
            TransitionStyle == BetterWorkTabSettings.SubWorkTransitionStyle.PixelWaveFlip;

        internal static bool IsTransitioning => IsActive && UseTransitionAnimation && (_isExiting || TransitionAlpha < 0.999f);

        internal static bool IsExpandBesideTransitioning
        {
            get
            {
                if (!IsExpandBesideActive || !UseTransitionAnimation)
                {
                    return false;
                }

                foreach (ExpandBesideEntry entry in ExpandBesideEntries.Values)
                {
                    if (entry.IsCollapsing || entry.VisualProgress < 0.999f)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal static float ExpandBesideHeaderExpansionProgress
        {
            get
            {
                if (!IsExpandBesideActive)
                {
                    return 0f;
                }

                float progress = 0f;
                foreach (ExpandBesideEntry entry in ExpandBesideEntries.Values)
                {
                    progress = Mathf.Max(progress, entry.VisualProgress);
                }

                return Mathf.Clamp01(progress);
            }
        }

        internal static float GetExpandBesideHeaderAlpha(WorkTypeDef workType)
        {
            if (workType?.defName == null ||
                !ExpandBesideEntries.TryGetValue(workType.defName, out var entry))
            {
                return 1f;
            }

            return Mathf.Clamp01(entry.VisualProgress);
        }

        internal static int TransitionLayoutFrame
        {
            get
            {
                if (!IsTransitioning)
                {
                    return 0;
                }

                return Mathf.Clamp(Mathf.RoundToInt(ModeVisualProgress * 60f), 0, 60);
            }
        }

        internal static float TransitionEase
        {
            get
            {
                float t = Mathf.Clamp01(TransitionAlpha);
                return t * t * t * (t * (t * 6f - 15f) + 10f);
            }
        }

        internal static float ModeVisualProgress
        {
            get
            {
                if (!IsActive)
                {
                    return 0f;
                }

                if (!UseTransitionAnimation || !IsTransitioning)
                {
                    return 1f;
                }

                return TransitionEase;
            }
        }

        internal static float ParentWorkContentAlpha => IsTransitioning ? 1f - ModeVisualProgress : 0f;

        internal static float SubWorkContentAlpha => ModeVisualProgress;

        internal static float SubWorkContentScale
        {
            get
            {
                if (!IsTransitioning || !UseClassicTransition)
                {
                    return 1f;
                }

                return Mathf.Lerp(0.78f, 1f, Mathf.Sin(SubWorkContentAlpha * Mathf.PI * 0.5f));
            }
        }

        internal static float HeaderFlipScale
        {
            get
            {
                if (!IsTransitioning || !UseClassicTransition)
                {
                    return 1f;
                }

                float visibleProgress = HeaderVisibleProgress;
                return Mathf.Lerp(0.62f, 1f, Mathf.Sin(visibleProgress * Mathf.PI * 0.5f));
            }
        }

        internal static float HeaderFlipAlpha => IsTransitioning && UseClassicTransition ? HeaderVisibleProgress : 1f;

        private static float HeaderVisibleProgress => ModeVisualProgress;

        internal static float GlobalRowVisibleHeight
        {
            get
            {
                if (!HasAnyDrilldown)
                {
                    return 0f;
                }

                return GlobalRowHeight;
            }
        }

        internal static float GlobalRowReservedHeight => HasAnyDrilldown ? GlobalRowHeight : 0f;

        internal static float GlobalRowVisualAlpha
        {
            get
            {
                if (IsExpandBesideActive)
                {
                    return UseTransitionAnimation
                        ? ExpandBesideHeaderExpansionProgress
                        : 1f;
                }

                if (!IsActive)
                {
                    return 0f;
                }

                if (!UseTransitionAnimation || !IsTransitioning)
                {
                    return 1f;
                }

                return Mathf.Clamp01(ModeVisualProgress);
            }
        }

        internal static float HeaderAnchorVisualOffsetY
        {
            get
            {
                if (!IsActive || !UseTransitionAnimation || !IsTransitioning)
                {
                    return 0f;
                }

                float hiddenHeight = Mathf.Max(0f, GlobalRowReservedHeight - GlobalRowVisibleHeight);
                return (_isExiting ? 1f : -1f) * hiddenHeight * 0.5f;
            }
        }

        internal static bool TryGetHeaderTransitionOffset(PawnColumnDef column, float columnWidth, out float offsetX)
        {
            offsetX = 0f;
            float pivotSlot = GetTransitionPivotSlot();
            if (!UseClassicTransition || !IsTransitioning || column == null || pivotSlot < 0)
            {
                return false;
            }

            int slot = GetVisibleWorkColumnSlot(column);
            if (slot < 0 || slot == pivotSlot)
            {
                return false;
            }

            float progress = HeaderVisibleProgress;
            offsetX = (pivotSlot - slot) * Mathf.Max(1f, columnWidth) * (1f - progress);
            return Mathf.Abs(offsetX) > 0.01f;
        }

        internal static bool TryGetHeaderTransitionVisuals(
            PawnColumnDef column,
            out float flipScale,
            out float subWorkAlpha,
            out float parentAlpha)
        {
            flipScale = 1f;
            subWorkAlpha = 1f;
            parentAlpha = 0f;

            if (!UsePixelWaveTransition || !IsActive || column == null)
            {
                return false;
            }

            int slot = GetVisibleWorkColumnSlot(column);
            if (slot < 0)
            {
                return false;
            }

            if (!UseTransitionAnimation || !IsTransitioning)
            {
                return true;
            }

            float passProgress = GetWavePassProgressForSlot(slot);
            float easedPass = SmoothStep01(passProgress);
            subWorkAlpha = _isExiting ? 1f - easedPass : easedPass;
            parentAlpha = 1f - subWorkAlpha;
            flipScale = 1f;
            return true;
        }

        internal static bool TryGetSubWorkContentTransitionVisuals(
            WorkGiver workGiver,
            out float alpha,
            out float scale)
        {
            alpha = 1f;
            scale = 1f;

            if (!IsActive || workGiver?.def == null)
            {
                return false;
            }

            int slot = GetActiveWorkGiverSlot(workGiver.def);
            if (slot < 0)
            {
                return false;
            }

            if (!UseTransitionAnimation || !IsTransitioning)
            {
                return true;
            }

            if (UseClassicTransition)
            {
                alpha = SubWorkContentAlpha;
                scale = SubWorkContentScale;
                return true;
            }

            float passProgress = GetWavePassProgressForSlot(slot);
            float easedPass = SmoothStep01(passProgress);
            alpha = _isExiting ? 1f - easedPass : easedPass;
            scale = 1f;
            return true;
        }

        internal static bool TryGetTransitionWave(out float pivotSlot, out float phase)
        {
            pivotSlot = -1f;
            phase = 0f;

            if (!UsePixelWaveTransition || !IsTransitioning)
            {
                return false;
            }

            pivotSlot = GetTransitionPivotSlot();
            if (pivotSlot < 0)
            {
                return false;
            }

            EnsureSlotCache();
            if (VisibleWorkTypeSlots.Count <= 0)
            {
                return false;
            }

            phase = GetTransitionPhase();
            return true;
        }

        internal static float GetBlankColumnFlashAlpha(PawnColumnDef column)
        {
            if (!UseClassicTransition || !IsTransitioning || column == null)
            {
                return 0f;
            }

            int slot = GetVisibleWorkColumnSlot(column);
            if (slot < 0)
            {
                return 0f;
            }

            float pivotSlot = GetTransitionPivotSlot();
            if (pivotSlot < 0)
            {
                return 0f;
            }

            float distance = Mathf.Abs(slot - pivotSlot);
            float phase = GetTransitionPhase();
            float waveCenter = Mathf.Lerp(
                -WaveFeatherSlots,
                GetMaxWaveDistanceFromPivot(pivotSlot) + WaveFeatherSlots,
                phase);
            float wave = 1f - Mathf.Abs(distance - waveCenter) / 1.25f;
            wave = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(wave));
            float fadeOut = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((phase - 0.48f) / 0.34f));

            return 0.18f * wave * fadeOut;
        }

        private static float GetWavePassProgressForSlot(float slot)
        {
            if (!TryGetTransitionWave(out float pivotSlot, out float phase))
            {
                return 1f;
            }

            float distance = Mathf.Abs(slot - pivotSlot);
            float maxDistance = GetMaxWaveDistanceFromPivot(pivotSlot);
            float waveCenter = Mathf.Lerp(-WaveFeatherSlots, maxDistance + WaveFeatherSlots, phase);
            return Mathf.Clamp01((waveCenter - distance + WaveFeatherSlots) / WaveFeatherSlots);
        }

        private static float GetTransitionPhase()
        {
            float progress = UsePixelWaveTransition
                ? Mathf.Clamp01(TransitionAlpha)
                : TransitionEase;
            return _isExiting ? 1f - progress : progress;
        }

        private static float GetMaxWaveDistanceFromPivot(float pivotSlot)
        {
            EnsureSlotCache();
            if (VisibleWorkTypeSlots.Count <= 1 || pivotSlot < 0f)
            {
                return 0f;
            }

            return Mathf.Max(pivotSlot, Mathf.Abs((VisibleWorkTypeSlots.Count - 1) - pivotSlot));
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static int GetActiveWorkGiverSlot(WorkGiverDef workGiverDef)
        {
            if (workGiverDef == null)
            {
                return -1;
            }

            RefreshIfNeeded();
            if (ActiveWorkGiverSlots.TryGetValue(workGiverDef, out int slot))
            {
                return slot;
            }

            return -1;
        }

        private static float GetTransitionPivotSlot()
        {
            if (_isExiting)
            {
                if (_exitWaveSlotPosition >= 0f)
                {
                    return _exitWaveSlotPosition;
                }

                return _exitWorkColumnSlot >= 0 ? _exitWorkColumnSlot : _entryWorkColumnSlot;
            }

            return _entryWorkColumnSlot;
        }

        internal static bool TryGetReturnMousePosition(out Vector2 position)
        {
            if (_returnMousePosition.HasValue)
            {
                position = _returnMousePosition.Value;
                return true;
            }

            position = Vector2.zero;
            return false;
        }

        internal static bool TryGetReturnMouseLocalPosition(out Vector2 position)
        {
            if (_returnMouseLocalPosition.HasValue)
            {
                position = _returnMouseLocalPosition.Value;
                return true;
            }

            position = Vector2.zero;
            return false;
        }

        internal static bool TryGetCursorRestorePosition(out Vector2 position, out string suppressionReason)
        {
            if (!_returnMousePosition.HasValue)
            {
                position = Vector2.zero;
                suppressionReason = "no stored return position";
                return false;
            }

            if (_cursorMovedSinceEnter)
            {
                position = Vector2.zero;
                suppressionReason = "cursor moved after entering sub-work";
                return false;
            }

            position = _returnMousePosition.Value;
            suppressionReason = null;
            return true;
        }

        internal static void Enter(
            WorkTypeDef workType,
            Vector2? returnMousePosition = null,
            float baseHeaderDrawWidth = -1f,
            Vector2? returnMouseLocalPosition = null)
        {
            EnterCore(
                workType,
                returnMousePosition,
                baseHeaderDrawWidth,
                returnMouseLocalPosition,
                sourceWorkColumnSlot: -1);
        }

        internal static void EnterFromSourceSlot(
            WorkTypeDef workType,
            Vector2? returnMousePosition,
            float baseHeaderDrawWidth,
            Vector2? returnMouseLocalPosition,
            int sourceWorkColumnSlot)
        {
            EnterCore(
                workType,
                returnMousePosition,
                baseHeaderDrawWidth,
                returnMouseLocalPosition,
                sourceWorkColumnSlot);
        }

        private static void EnterCore(
            WorkTypeDef workType,
            Vector2? returnMousePosition,
            float baseHeaderDrawWidth,
            Vector2? returnMouseLocalPosition,
            int sourceWorkColumnSlot)
        {
            if (workType == null)
            {
                LogSubWork("Enter requested with null work type; exiting immediately.");
                ExitImmediate();
                return;
            }

            if (IsActive)
            {
                LogSubWork(
                    $"Enter requested while already active. previous={_activeWorkType.defName}, exiting={_isExiting}, next={workType.defName}");
            }

            _activeWorkType = workType;
            _isExiting = false;
            _cachedWorkTypeDefName = null;
            _cachedSyncVersion = -1;
            _enteredAt = Time.realtimeSinceStartup;
            _exitingAt = 0f;
            _baseHeaderDrawWidth = baseHeaderDrawWidth > 0f ? baseHeaderDrawWidth : 0f;
            _returnMousePosition = returnMousePosition;
            _returnMouseLocalPosition = returnMouseLocalPosition;
            _entryNativeCursorPosition = NativeCursorPosition.TryGetClientPosition(out Vector2 currentCursorPosition)
                ? currentCursorPosition
                : (Vector2?)null;
            _cursorMovedSinceEnter = false;
            EnsureSlotCache();
            // A chooser preview can rebuild the visible columns before the committed Enter call.
            // Preserve the pre-animation parent-header slot when supplied so both directions pivot
            // on the header the player actually opened, never a fallback edge of the rebuilt grid.
            _entryWorkColumnSlot = sourceWorkColumnSlot >= 0
                ? sourceWorkColumnSlot
                : (VisibleWorkTypeSlots.TryGetValue(workType, out int slot) ? slot : -1);
            _exitWorkColumnSlot = -1;
            _exitWaveSlotPosition = -1f;
            _layoutRefreshPending = true;
            RefreshIfNeeded();
            LogSubWork(
                $"Enter workType={workType.defName}, slot={_entryWorkColumnSlot}, style={TransitionStyle}, animation={UseTransitionAnimation}, returnCursor={_returnMousePosition.HasValue}");
        }

        internal static void Exit()
        {
            if (!IsActive)
            {
                LogSubWork("Exit requested while inactive; ignored.");
                return;
            }

            if (!UseTransitionAnimation)
            {
                LogSubWork($"Exit immediate because transition animation is disabled. workType={_activeWorkType.defName}");
                ExitImmediate();
                return;
            }

            if (_isExiting)
            {
                LogSubWork(
                    $"Exit requested while already exiting. workType={_activeWorkType.defName}, previousSlot={_exitWorkColumnSlot}");
            }

            _isExiting = true;
            _exitingAt = Time.realtimeSinceStartup;

            // Closing is the inverse of opening: collapse into the original parent header,
            // independent of which child cell, back label, or X button requested the exit.
            _exitWorkColumnSlot = _entryWorkColumnSlot;
            _exitWaveSlotPosition = _entryWorkColumnSlot;
            _layoutRefreshPending = true;
            LogSubWork(
                $"Exit requested workType={_activeWorkType.defName}, sourceSlot={_entryWorkColumnSlot}, exitSlot={_exitWorkColumnSlot}, waveSlot={_exitWaveSlotPosition:0.###}, style={TransitionStyle}, cursorMoved={_cursorMovedSinceEnter}");
        }

        internal static void TickTransition()
        {
            TrackCursorMovement();

            if (_isExiting && Time.realtimeSinceStartup - _exitingAt >= TransitionSeconds)
            {
                ExitImmediate();
            }

            if (TickExpandBesideTransitions())
            {
                _layoutRefreshPending = true;
            }
        }

        internal static void ExitImmediate()
        {
            bool hadFocusedState = _activeWorkType != null ||
                _isExiting ||
                ActiveWorkGiversBuffer.Count > 0 ||
                ActiveWorkGiverSlots.Count > 0 ||
                MovedFromBaseline.Count > 0;
            string previous = _activeWorkType?.defName;
            _activeWorkType = null;
            _cachedWorkTypeDefName = null;
            _cachedSyncVersion = -1;
            _isExiting = false;
            _enteredAt = 0f;
            _exitingAt = 0f;
            _baseHeaderDrawWidth = 0f;
            _entryWorkColumnSlot = -1;
            _exitWorkColumnSlot = -1;
            _exitWaveSlotPosition = -1f;
            _returnMousePosition = null;
            _returnMouseLocalPosition = null;
            _entryNativeCursorPosition = null;
            _cursorMovedSinceEnter = false;
            ActiveWorkGiversBuffer.Clear();
            ActiveWorkGiverSlots.Clear();
            MovedFromBaseline.Clear();
            ClearDrawingColumn();
            if (hadFocusedState)
            {
                _layoutRefreshPending = true;
            }
            if (!previous.NullOrEmpty())
            {
                LogSubWork($"Exited sub-work immediately. previous={previous}");
            }
        }

        internal static void ResetForWindowClose()
        {
            ExitImmediate();
            ExpandBesideEntries.Clear();
            _layoutRefreshPending = false;
        }

        internal static BetterWorkTabSettings.SubWorkDrilldownStyle EffectiveDrilldownStyle()
        {
            var style = BetterWorkTabMod.Settings?.subWorkDrilldownStyle ?? DefaultSettings.subWorkDrilldownStyle;
            if (style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside &&
                !FluffyWorkTabGateway.CanHostFluffySubWorkColumns)
            {
                return BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView;
            }

            return style == BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen
                ? BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView
                : style;
        }

        internal static bool IsExpandBesideExpanded(WorkTypeDef workType)
        {
            return workType?.defName != null &&
                   ExpandBesideEntries.TryGetValue(workType.defName, out var entry) &&
                   !entry.IsCollapsing;
        }

        internal static void ToggleExpandBeside(WorkTypeDef workType)
        {
            if (workType?.defName == null)
            {
                return;
            }

            if (!FluffyWorkTabGateway.CanHostFluffySubWorkColumns)
            {
                Enter(workType);
                return;
            }

            if (IsActive)
            {
                ExitImmediate();
            }

            ColumnReorderAnimationState.Clear();

            if (ExpandBesideEntries.TryGetValue(workType.defName, out var existing) && !existing.IsCollapsing)
            {
                existing.BeginCollapse();
                _layoutRefreshPending = true;
                LogSubWork($"Expand-beside collapse requested. workType={workType.defName}");
                return;
            }

            ExpandBesideEntries[workType.defName] = new ExpandBesideEntry(workType);
            _layoutRefreshPending = true;
            RefreshIfNeeded();
            LogSubWork($"Expand-beside expand requested. workType={workType.defName}");
        }

        internal static void CollapseAllExpandBeside()
        {
            if (ExpandBesideEntries.Count == 0)
            {
                return;
            }

            ColumnReorderAnimationState.Clear();
            foreach (ExpandBesideEntry entry in ExpandBesideEntries.Values)
            {
                entry.BeginCollapse();
            }
            _layoutRefreshPending = true;
        }

        internal static void CollapseAllExpandBesideImmediate()
        {
            if (ExpandBesideEntries.Count == 0)
            {
                return;
            }

            ColumnReorderAnimationState.Clear();
            ExpandBesideEntries.Clear();
            _layoutRefreshPending = true;
        }

        internal static float GetExpandBesideWidthProgress(WorkTypeDef workType)
        {
            if (workType?.defName == null ||
                !ExpandBesideEntries.TryGetValue(workType.defName, out var entry))
            {
                return 0f;
            }

            return entry.VisualProgress;
        }

        internal static void SetDrawingColumn(Better_Work_Tab.PawnOrganizer.WorkTabLayoutColumn column)
        {
            if (column.IsExpandBesideChild && column.SubWorkParent != null && column.SubWorkGiver != null)
            {
                _drawingSubWorkParent = column.SubWorkParent;
                _drawingSubWorkGiver = column.SubWorkGiver;
                _drawingSubWorkSlot = column.SubWorkSlot;
                return;
            }

            ClearDrawingColumn();
        }

        internal static void ClearDrawingColumn()
        {
            _drawingSubWorkParent = null;
            _drawingSubWorkGiver = null;
            _drawingSubWorkSlot = -1;
        }

        internal static bool TryGetWorkGiverForColumn(
            Better_Work_Tab.PawnOrganizer.WorkTabLayoutColumn column,
            out WorkGiver workGiver,
            out WorkTypeDef parentWorkType,
            out int slotIndex)
        {
            workGiver = null;
            parentWorkType = null;
            slotIndex = -1;

            if (column.IsExpandBesideChild && column.SubWorkGiver != null && column.SubWorkParent != null)
            {
                parentWorkType = column.SubWorkParent;
                slotIndex = column.SubWorkSlot;
                workGiver = ResolveWorkGiver(column.SubWorkParent, column.SubWorkGiver);
                return workGiver != null;
            }

            if (IsExpandBesideActive)
            {
                return false;
            }

            if (TryGetWorkGiverForColumn(column.Column, out workGiver, out slotIndex))
            {
                parentWorkType = _activeWorkType;
                return true;
            }

            return false;
        }

        internal static bool TryGetCurrentDrawingWorkGiver(
            PawnColumnDef column,
            out WorkGiver workGiver,
            out WorkTypeDef parentWorkType,
            out int slotIndex)
        {
            workGiver = null;
            parentWorkType = null;
            slotIndex = -1;

            if (_drawingSubWorkGiver != null &&
                _drawingSubWorkParent != null &&
                (column == null || column.workType == _drawingSubWorkParent))
            {
                workGiver = ResolveWorkGiver(_drawingSubWorkParent, _drawingSubWorkGiver);
                parentWorkType = _drawingSubWorkParent;
                slotIndex = _drawingSubWorkSlot;
                return workGiver != null;
            }

            if (IsExpandBesideActive)
            {
                return false;
            }

            if (TryGetWorkGiverForColumn(column, out workGiver, out slotIndex))
            {
                parentWorkType = _activeWorkType;
                return true;
            }

            return false;
        }

        internal static bool ConsumeLayoutRefresh()
        {
            bool pending = _layoutRefreshPending;
            _layoutRefreshPending = false;
            return pending;
        }

        private static void TrackCursorMovement()
        {
            if (!IsActive ||
                _isExiting ||
                _cursorMovedSinceEnter ||
                !_entryNativeCursorPosition.HasValue ||
                !NativeCursorPosition.TryGetClientPosition(out Vector2 currentPosition))
            {
                return;
            }

            if (Time.realtimeSinceStartup - _enteredAt < CursorMoveSuppressGraceSeconds)
            {
                return;
            }

            float distance = Vector2.Distance(currentPosition, _entryNativeCursorPosition.Value);
            if (distance < CursorMoveSuppressThreshold)
            {
                return;
            }

            _cursorMovedSinceEnter = true;
            LogSubWork(
                $"Cursor moved after entering sub-work. distance={distance:0.##}, entry={_entryNativeCursorPosition.Value}, current={currentPosition}");
        }

        private static void LogSubWork(string message)
        {
            BetterWorkTabMod.DebugLog("[SubWorkDrilldown] " + message, DebugFeature.SubWork);
        }

        private static bool TickExpandBesideTransitions()
        {
            bool changed = false;
            var removeKeys = new List<string>();
            foreach (var pair in ExpandBesideEntries)
            {
                ExpandBesideEntry entry = pair.Value;
                if (entry.IsCollapsing && entry.VisualProgress <= 0.001f)
                {
                    removeKeys.Add(pair.Key);
                    changed = true;
                    continue;
                }

                if (entry.VisualProgress < 0.999f)
                {
                    changed = true;
                }
            }

            for (int i = 0; i < removeKeys.Count; i++)
            {
                ExpandBesideEntries.Remove(removeKeys[i]);
            }

            return changed;
        }

        private static int ComputeExpandBesideSignature(bool includeProgress = true)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + ExpandBesideEntries.Count;
                foreach (var pair in ExpandBesideEntries)
                {
                    var entry = pair.Value;
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(pair.Key ?? string.Empty);
                    hash = hash * 31 + (entry.IsCollapsing ? 1 : 0);
                    if (includeProgress)
                    {
                        hash = hash * 31 + Mathf.RoundToInt(entry.VisualProgress * 60f);
                    }
                }

                return hash;
            }
        }

        private static WorkGiver ResolveWorkGiver(WorkTypeDef parentWorkType, WorkGiverDef workGiverDef)
        {
            if (workGiverDef == null)
            {
                return null;
            }

            var givers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(parentWorkType);
            for (int i = 0; i < givers.Count; i++)
            {
                if (givers[i]?.def == workGiverDef)
                {
                    return givers[i];
                }
            }

            return null;
        }

        internal static bool TryGetWorkGiverForColumn(PawnColumnDef column, out WorkGiver workGiver, out int slotIndex)
        {
            workGiver = null;
            slotIndex = GetVisibleWorkColumnSlot(column);
            if (!IsActive || IsExpandBesideActive || slotIndex < 0)
            {
                return false;
            }

            RefreshIfNeeded();
            if (slotIndex >= ActiveWorkGiversBuffer.Count)
            {
                return false;
            }

            workGiver = ActiveWorkGiversBuffer[slotIndex];
            return workGiver?.def != null;
        }

        internal static bool TryGetWorkGiverForWorkTypeSlot(WorkTypeDef slotWorkType, out WorkGiver workGiver, out int slotIndex)
        {
            workGiver = null;
            slotIndex = -1;
            if (!IsActive || slotWorkType == null)
            {
                return false;
            }

            var tableDef = PawnTableDefOf.Work;
            if (tableDef?.columns == null)
            {
                return false;
            }

            for (int i = 0; i < tableDef.columns.Count; i++)
            {
                var column = tableDef.columns[i];
                if (column?.workType == slotWorkType && column.Worker is PawnColumnWorker_WorkPriority)
                {
                    return TryGetWorkGiverForColumn(column, out workGiver, out slotIndex);
                }
            }

            return false;
        }

        internal static bool IsWorkGiverMovedFromBaseline(WorkGiverDef workGiverDef)
        {
            if (!IsActive || workGiverDef == null)
            {
                return false;
            }

            RefreshIfNeeded();
            return MovedFromBaseline.Contains(workGiverDef);
        }

        internal static bool IsBlankWorkColumn(PawnColumnDef column)
        {
            return !_isExiting && IsBlankWorkColumnInternal(column);
        }

        private static bool IsBlankWorkColumnInternal(PawnColumnDef column)
        {
            return IsActive && GetVisibleWorkColumnSlot(column) >= 0 && !TryGetWorkGiverForColumn(column, out _, out _);
        }

        internal static int GetVisibleWorkColumnSlot(PawnColumnDef column)
        {
            if (column == null || !(column.Worker is PawnColumnWorker_WorkPriority) || column.workType == null)
            {
                return -1;
            }

            EnsureSlotCache();
            if (VisibleColumnSlots.TryGetValue(column, out int slot))
            {
                return slot;
            }

            return VisibleWorkTypeSlots.TryGetValue(column.workType, out slot) ? slot : -1;
        }

        internal static int ComparePawnsForColumn(PawnColumnDef column, Pawn a, Pawn b)
        {
            if (!TryGetWorkGiverForColumn(column, out var workGiver, out _))
            {
                return 0;
            }

            float valueA = GetPrioritySortValue(a, workGiver.def);
            float valueB = GetPrioritySortValue(b, workGiver.def);
            return valueA.CompareTo(valueB);
        }

        private static float GetPrioritySortValue(Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork || workGiverDef == null)
            {
                return -2f;
            }

            if (_activeWorkType != null && pawn.WorkTypeIsDisabled(_activeWorkType))
            {
                return -1f;
            }

            int defaultPriority = Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, _activeWorkType);
            int priority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiverDef, defaultPriority);
            priority = Better_Work_Tab.Features.TimePriority.TimePriorityService.GetEffectiveWorkGiverPriority(
                pawn,
                _activeWorkType,
                workGiverDef,
                priority);
            if (priority <= Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.DisabledPriority)
            {
                return -1f;
            }

            return Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.GetMaxPriority() + 1 - priority;
        }

        private static void RefreshIfNeeded()
        {
            if (_activeWorkType == null)
            {
                ActiveWorkGiversBuffer.Clear();
                ActiveWorkGiverSlots.Clear();
                return;
            }

            int syncVersion = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (_cachedWorkTypeDefName == _activeWorkType.defName && _cachedSyncVersion == syncVersion)
            {
                return;
            }

            ActiveWorkGiversBuffer.Clear();
            ActiveWorkGiversBuffer.AddRange(WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(_activeWorkType));
            RebuildActiveWorkGiverSlotCache();
            RebuildMovedBaselineCache();
            _cachedWorkTypeDefName = _activeWorkType.defName;
            _cachedSyncVersion = syncVersion;
        }

        private static void EnsureSlotCache()
        {
            int signature = ComputeSlotSignature();
            if (signature == _cachedSlotSignature)
            {
                return;
            }

            VisibleColumnSlots.Clear();
            VisibleWorkTypeSlots.Clear();

            var columns = PawnTableDefOf.Work?.columns;
            if (columns != null)
            {
                int slot = 0;
                var hidden = BetterWorkTabMod.Settings?.hiddenWorktypes;
                for (int i = 0; i < columns.Count; i++)
                {
                    var candidate = columns[i];
                    if (candidate?.workType == null || !(candidate.Worker is PawnColumnWorker_WorkPriority))
                    {
                        continue;
                    }

                    if (hidden != null && hidden.Contains(candidate.workType.defName))
                    {
                        continue;
                    }

                    VisibleColumnSlots[candidate] = slot;
                    // Fluffy can contribute later WorkPriority columns for the same
                    // WorkTypeDef. The first visible column is the canonical BWT parent
                    // header; replacing it with a later duplicate makes Focus View pivot
                    // from an off-screen slot during enter/exit transitions.
                    if (!VisibleWorkTypeSlots.ContainsKey(candidate.workType))
                    {
                        VisibleWorkTypeSlots[candidate.workType] = slot;
                    }
                    slot++;
                }
            }

            _cachedSlotSignature = signature;
        }

        private static int ComputeSlotSignature()
        {
            unchecked
            {
                int hash = 17;
                var columns = PawnTableDefOf.Work?.columns;
                if (columns != null)
                {
                    hash = hash * 31 + columns.Count;
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var column = columns[i];
                        if (column?.workType == null || !(column.Worker is PawnColumnWorker_WorkPriority))
                        {
                            continue;
                        }

                        hash = hash * 31 + column.shortHash;
                        hash = hash * 31 + column.workType.shortHash;
                    }
                }

                var hidden = BetterWorkTabMod.Settings?.hiddenWorktypes;
                if (hidden != null)
                {
                    hash = hash * 31 + hidden.Count;
                    for (int i = 0; i < hidden.Count; i++)
                    {
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(hidden[i] ?? string.Empty);
                    }
                }

                return hash;
            }
        }

        private static void RebuildMovedBaselineCache()
        {
            MovedFromBaseline.Clear();

            if (_activeWorkType == null)
            {
                return;
            }

            for (int i = 0; i < ActiveWorkGiversBuffer.Count; i++)
            {
                var def = ActiveWorkGiversBuffer[i]?.def;
                if (def != null && WorkGiverReassignmentManager.ShouldShowMovedWorkGiverMarker(_activeWorkType, def))
                {
                    MovedFromBaseline.Add(def);
                }
            }
        }

        private static void RebuildActiveWorkGiverSlotCache()
        {
            ActiveWorkGiverSlots.Clear();
            for (int i = 0; i < ActiveWorkGiversBuffer.Count; i++)
            {
                WorkGiverDef def = ActiveWorkGiversBuffer[i]?.def;
                if (def != null && !ActiveWorkGiverSlots.ContainsKey(def))
                {
                    ActiveWorkGiverSlots.Add(def, i);
                }
            }
        }

        private sealed class ExpandBesideEntry
        {
            internal ExpandBesideEntry(WorkTypeDef workType)
            {
                WorkType = workType;
                _startedAt = Time.realtimeSinceStartup;
            }

            private readonly float _startedAt;
            private float _collapsingAt;

            internal WorkTypeDef WorkType { get; }

            internal bool IsCollapsing { get; private set; }

            internal float VisualProgress
            {
                get
                {
                    if (!UseTransitionAnimation)
                    {
                        return IsCollapsing ? 0f : 1f;
                    }

                    float startedAt = IsCollapsing ? _collapsingAt : _startedAt;
                    float progress = Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / ExpandBesideTransitionSeconds);
                    return IsCollapsing ? 1f - progress : progress;
                }
            }

            internal void BeginCollapse()
            {
                if (IsCollapsing)
                {
                    return;
                }

                IsCollapsing = true;
                _collapsingAt = Time.realtimeSinceStartup;
            }
        }
    }
}
