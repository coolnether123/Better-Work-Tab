using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Better_Work_Tab.Features;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.Dividers;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Compatibility;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.PawnOrganizer.Patches;
using Better_Work_Tab.DragDrop;
using RimWorld;
using Spine.RimWorld.Rendering;
using Spine.RimWorld.Rendering.GuiState;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal sealed class OptimizedWorkGridRenderer : IWorkGridRenderer, IWorkGridSnapshotLayer,
        IWorkGridVisibleColumnRangeProvider, IPreparedWorkGridRowLayer,
        IWorkGridRowEventTraversalPolicy, IDisposable
    {
        internal const string RendererId = "bwt.optimized-layered";
        private readonly IWorkGridDrawingSurface _drawingSurface;
        private WorkGridSnapshot _snapshot;
        private WorkGridIndexRange _visibleRows;
        private WorkGridIndexRange _visibleColumns;
        private ImGuiEventPhase _eventPhase;
        private bool _delegateShiftedSkillOverlay;
        private bool _delegateScheduleCells;
        private bool _delegateSleekPriorityCells;
        private GuiStateScope _cellBatchState;
        private Color _cellBatchColor;
        private bool _cellBatchActive;
        private readonly RetainedWorkBoxRowCache _retainedRows = new RetainedWorkBoxRowCache();
        private readonly List<RetainedWorkBoxRowCache.Cell> _retainedCells =
            new List<RetainedWorkBoxRowCache.Cell>(32);
        private readonly List<PendingParentCell> _pendingParentCells =
            new List<PendingParentCell>(32);
        private readonly List<PendingSubWorkCell> _pendingSubWorkCells =
            new List<PendingSubWorkCell>(32);
        private PreparedWorkRowPacket[] _preparedRowPackets = Array.Empty<PreparedWorkRowPacket>();
        private readonly Dictionary<WorkTypeDef, int> _parentColumnByWorkType =
            new Dictionary<WorkTypeDef, int>();
        private readonly Dictionary<HoverColumnKey, int> _columnIndexByHoverKey =
            new Dictionary<HoverColumnKey, int>();
        private static readonly System.Action RepaintCopyPasteNoOp = delegate { };
        private long _cellLookupTopologyRevision = long.MinValue;
        private int _renderResourcesRevision;
        private bool _copyPasteClipboardAvailable;
        private int _hoveredRowIndex = -1;
        private int _hoveredColumnIndex = -1;
        private int _headerHoveredColumnIndex = -1;
        private IReadOnlyList<WorkTabLayoutColumn> _currentLayoutColumns =
            Array.Empty<WorkTabLayoutColumn>();
        private IReadOnlyList<WorkTabLayoutRow> _currentLayoutRows =
            Array.Empty<WorkTabLayoutRow>();
        private WorkTypeDef[] _liveWorkTypesByColumn = Array.Empty<WorkTypeDef>();
        private WorkGiver[] _liveWorkGiversByColumn = Array.Empty<WorkGiver>();
        private IWorkTabLayoutController _liveReferenceLayout;
        private int _liveReferenceLayoutRevision = int.MinValue;
        private WorkGridGeometrySnapshot _currentGeometry;
        private bool _hasMatchingLayoutRevision;
        private bool _liveReferenceTopologyValid;

        internal OptimizedWorkGridRenderer(IWorkGridDrawingSurface drawingSurface)
        {
            _drawingSurface = drawingSurface ?? throw new ArgumentNullException(nameof(drawingSurface));
        }

        public string Id => RendererId;
        public int Priority => 100;

        public WorkGridIndexRange VisibleColumnRange => _visibleColumns;

        public bool IsAvailable(in WorkTabView context)
        {
            // The snapshot is built inside the effective-state scope, so a
            // preview captures projected parent priorities, specific-job
            // state, and its validated global manual mode. An
            // external priority owner still lacks a safe content revision and
            // therefore remains on the native correctness path.
            return !PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority &&
                   WorkGridSnapshotProvider.IsActiveEffectiveStateCurrent() &&
                   context.Snapshot != null &&
                   context.Geometry != null &&
                   context.Layout != null &&
                   context.Table != null &&
                   context.HasMatchingLayoutRevision;
        }

        public void Prepare(in WorkTabView context)
        {
            _eventPhase = context.EventPhase;
            _currentLayoutColumns = context.Layout?.Columns ?? Array.Empty<WorkTabLayoutColumn>();
            _currentLayoutRows = context.Layout?.Rows ?? Array.Empty<WorkTabLayoutRow>();
            bool passGeometryChanged = !ReferenceEquals(_currentGeometry, context.Geometry);
            _currentGeometry = context.Geometry;
            _hasMatchingLayoutRevision = context.HasMatchingLayoutRevision;
            WorkGridSnapshot snapshot = context.Snapshot;
            bool snapshotChanged = !ReferenceEquals(snapshot, _snapshot);
            if (snapshotChanged)
            {
                _snapshot = snapshot;
            }

            bool topologyChanged = snapshot == null ||
                snapshot.TopologyRevision != _cellLookupTopologyRevision;
            bool liveLayoutChanged = !ReferenceEquals(_liveReferenceLayout, context.Layout) ||
                _liveReferenceLayoutRevision != context.Layout?.LayoutRevision;
            if (topologyChanged || liveLayoutChanged || passGeometryChanged)
            {
                // Packets contain finished-pass rectangles. A replacement layout
                // or geometry owner may legitimately reuse the same numeric
                // revision, so identity changes must discard those rectangles.
                _preparedRowPackets = snapshot == null
                    ? Array.Empty<PreparedWorkRowPacket>()
                    : new PreparedWorkRowPacket[snapshot.Rows.Count];
            }

            if (topologyChanged || liveLayoutChanged)
            {
                BuildColumnLookup(snapshot);
                _cellLookupTopologyRevision = snapshot?.TopologyRevision ?? long.MinValue;
                _liveReferenceLayout = context.Layout;
                _liveReferenceLayoutRevision = context.Layout?.LayoutRevision ?? int.MinValue;
            }

            if (snapshot == null)
            {
                return;
            }

            bool skillOverlayEnabled = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesOverlay);
            _delegateShiftedSkillOverlay = skillOverlayEnabled &&
                ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;
            _delegateScheduleCells = FluffyTimeScheduleAssigner.IsOpen;
            _delegateSleekPriorityCells = SleekWorkTabGateway.BetterWorkTabHostsSleek;
            _renderResourcesRevision = context.InvalidationVersions.RenderResources;

            if (context.EventPhase == ImGuiEventPhase.Repaint)
            {
                WorkGiverPriorityBoxRenderer.MaintainResetAnimations();
                _copyPasteClipboardAvailable = ReadCopyPasteClipboardState();
                Vector2 scroll = context.Table.scrollPosition;
                _visibleRows = context.Geometry.GetVisibleRowRange(context.Viewport, scroll.y);
                _visibleColumns = context.Geometry.GetVisibleColumnRange(context.Viewport, scroll.x);

                // The body renderer already culls with live animated geometry.
                // Avoid applying a second, stable snapshot filter while columns move.
                if (ColumnReorderAnimationState.IsActive)
                {
                    _visibleColumns = new WorkGridIndexRange(0, _snapshot.Columns.Count);
                }

                ResolveHoverTargets(in context);
            }
        }

        public void Draw(in WorkTabView context)
        {
            _drawingSurface.DrawBody(in context, this);
        }

        public void HandleEvent(in WorkTabView context)
        {
        }

        public void ReleaseTransient(in WorkTabView context)
        {
        }

        public bool TryOwnRowBackground(int rowIndex, Rect rowRect)
        {
            if (_snapshot == null ||
                !_hasMatchingLayoutRevision ||
                !_liveReferenceTopologyValid ||
                rowIndex < 0 ||
                rowIndex >= _snapshot.Rows.Count)
            {
                return false;
            }

            WorkGridRowEntry row = _snapshot.Rows[rowIndex];
            if (!HasMatchingLiveRow(rowIndex, row))
            {
                return false;
            }
            if ((row.VisualFlags & WorkGridRowVisualFlags.HasBackground) != 0)
            {
                Widgets.DrawBoxSolid(rowRect, UnpackColor(row.BackgroundColor));
            }

            return true;
        }

        public void BeginRow()
        {
            EndCellBatch();
            _retainedCells.Clear();
            _pendingParentCells.Clear();
            _pendingSubWorkCells.Clear();
        }

        public void EndRow()
        {
            EndCellBatch();
        }

        public bool TryDrawCell(int rowIndex, int columnIndex, Rect cellRect)
        {
            if (_snapshot == null ||
                !_hasMatchingLayoutRevision ||
                !_liveReferenceTopologyValid ||
                rowIndex < 0 || columnIndex < 0 ||
                rowIndex >= _snapshot.Rows.Count || columnIndex >= _snapshot.Columns.Count)
            {
                EndCellBatch();
                return false;
            }

            WorkGridColumnEntry column = _snapshot.Columns[columnIndex];
            if ((column.WorkerKind != WorkGridColumnWorkerKind.WorkPriority &&
                 column.WorkerKind != WorkGridColumnWorkerKind.SubWorkPriority) ||
                _delegateSleekPriorityCells)
            {
                EndCellBatch();
                return false;
            }

            int lookupIndex = (rowIndex * _snapshot.Columns.Count) + columnIndex;
            if (lookupIndex < 0 || lookupIndex >= _snapshot.CellIndexes.Count)
            {
                EndCellBatch();
                return false;
            }

            int cellIndex = _snapshot.CellIndexes[lookupIndex];
            if (cellIndex < 0)
            {
                EndCellBatch();
                return false;
            }

            WorkCellVisualState cell = _snapshot.Cells[cellIndex];
            bool focusViewActive = SubWorkDrilldownState.IsActive &&
                !SubWorkDrilldownState.IsExpandBesideActive;
            bool subWorkCell = column.WorkerKind == WorkGridColumnWorkerKind.SubWorkPriority;
            if (!subWorkCell && !focusViewActive &&
                (_delegateShiftedSkillOverlay || _delegateScheduleCells))
            {
                EndCellBatch();
                return false;
            }

            if (subWorkCell)
            {
                return TryDrawSubWorkCell(rowIndex, columnIndex, cellRect, column, cell);
            }

            if (!TryGetLiveCellReferences(
                    rowIndex,
                    columnIndex,
                    out Pawn pawn,
                    out WorkTypeDef workType,
                    out _))
            {
                EndCellBatch();
                return false;
            }

            EnsureCellBatch(GameFont.Medium);

            float visualAlpha = focusViewActive
                ? SubWorkDrilldownState.ParentWorkContentAlpha
                : 1f;
            if (_eventPhase == ImGuiEventPhase.Repaint &&
                visualAlpha > 0.999f &&
                !ColumnReorderAnimationState.IsActive)
            {
                WorkBoxVisualState visual = CreateVisual(cell);
                Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
                _retainedCells.Add(new RetainedWorkBoxRowCache.Cell(
                    cell.PawnId,
                    columnIndex,
                    boxRect,
                    visual,
                    cell.Priority));
                _pendingParentCells.Add(new PendingParentCell(
                    cellRect,
                    boxRect,
                    cell,
                    visual,
                    pawn,
                    workType));
            }
            else if (_eventPhase == ImGuiEventPhase.Repaint)
            {
                DrawCellInBatch(
                    cellRect,
                    cell.Priority,
                    cell.PriorityColor,
                    cell.Flags,
                    cell.SkillBand,
                    cell.SkillBlend,
                    cell.Passion,
                    visualAlpha);
                if (!focusViewActive)
                {
                    DrawParentHover(cellRect, cell, pawn, workType);
                }
            }
            return true;
        }

        public bool ShouldVisitCell(int rowIndex, int columnIndex)
        {
            if (!_hasMatchingLayoutRevision || !_liveReferenceTopologyValid)
            {
                return true;
            }
            if (rowIndex < 0 || rowIndex >= _snapshot.Rows.Count ||
                !HasMatchingLiveRow(rowIndex, _snapshot.Rows[rowIndex]))
            {
                return true;
            }
            return rowIndex >= _visibleRows.Start && rowIndex < _visibleRows.EndExclusive &&
                   columnIndex >= _visibleColumns.Start && columnIndex < _visibleColumns.EndExclusive;
        }

        public bool ShouldTraverseRows(Event currentEvent)
        {
            bool viewportScroll = currentEvent.type == EventType.ScrollWheel ||
                (currentEvent.type == EventType.Used &&
                 currentEvent.rawType == EventType.ScrollWheel);
            if (!viewportScroll)
            {
                return true;
            }

            // The window already routes BWT priority input directly to one cell,
            // and BeginScrollView owns viewport movement. Skip the subsequent row
            // walk only when topology inspection proved every remaining worker is
            // wheel-passive and no live compatibility feature needs native DoCell.
            return _delegateSleekPriorityCells ||
                   _delegateScheduleCells ||
                   _snapshot == null ||
                   !_hasMatchingLayoutRevision ||
                   !_liveReferenceTopologyValid ||
                   !WorkGridVanillaCompatibilityPolicy.CanSkipViewportScrollRowTraversal(
                       _currentLayoutColumns,
                       _snapshot);
        }

        public void Dispose()
        {
            EndCellBatch();
            _retainedRows.Dispose();
            _snapshot = null;
            _cellLookupTopologyRevision = long.MinValue;
            _preparedRowPackets = Array.Empty<PreparedWorkRowPacket>();
            _parentColumnByWorkType.Clear();
            _columnIndexByHoverKey.Clear();
            _currentLayoutColumns = Array.Empty<WorkTabLayoutColumn>();
            _currentLayoutRows = Array.Empty<WorkTabLayoutRow>();
            _liveWorkTypesByColumn = Array.Empty<WorkTypeDef>();
            _liveWorkGiversByColumn = Array.Empty<WorkGiver>();
            _copyPasteClipboardAvailable = false;
            _liveReferenceLayout = null;
            _liveReferenceLayoutRevision = int.MinValue;
            _currentGeometry = null;
            _hasMatchingLayoutRevision = false;
            _liveReferenceTopologyValid = false;
        }

        internal void ReleaseRetainedResources()
        {
            try
            {
                FinalizeTransientRenderState();
            }
            finally
            {
                _retainedRows.Dispose();
            }
        }

        internal void FinalizeTransientRenderState()
        {
            EndCellBatch();
        }

        internal void ResetRetainedRowResourceFailureLatchForReopen()
        {
            _retainedRows.ResetResourceFailureLatchForReopen();
        }

        /// <summary>
        /// Returns a packet only after this pass has validated the row's live
        /// pawn through <see cref="IsLiveRenderablePawn"/>. A successful result
        /// gives the caller a work-capable pawn for this Repaint; callers should
        /// use the direct path when the boundary returns false instead of
        /// repeating the row checks downstream.
        /// </summary>
        public bool TryGetPreparedRow(
            int rowIndex,
            Rect rowRect,
            out PreparedWorkRowPacket packet)
        {
            packet = null;
            if (_eventPhase != ImGuiEventPhase.Repaint ||
                _snapshot == null ||
                !_hasMatchingLayoutRevision ||
                !_liveReferenceTopologyValid ||
                _delegateSleekPriorityCells ||
                (_delegateScheduleCells && WorkTabEffectiveStateRuntime.IsPreviewActive) ||
                ColumnReorderAnimationState.IsActive ||
                DividerCollapseAnimationState.HasActiveAnimations ||
                DividerInsertionAnimationState.HasActiveAnimations ||
                SubWorkDrilldownState.IsTransitioning ||
                rowIndex < 0 ||
                rowIndex >= _snapshot.Rows.Count ||
                rowIndex >= _snapshot.PreparedRows.Count ||
                rowIndex >= _preparedRowPackets.Length)
            {
                return false;
            }

            WorkGridRowEntry row = _snapshot.Rows[rowIndex];
            WorkGridPreparedRowSpan span = _snapshot.PreparedRows[rowIndex];
            if (row.Kind != WorkGridRowKind.Pawn ||
                span.CellCount == 0 ||
                !TryGetLivePawn(rowIndex, row.PawnId, out _))
            {
                return false;
            }

            bool focusViewActive = SubWorkDrilldownState.IsActive &&
                !SubWorkDrilldownState.IsExpandBesideActive;
            float parentAlpha = focusViewActive
                ? SubWorkDrilldownState.ParentWorkContentAlpha
                : 1f;
            if (parentAlpha > 0.001f && parentAlpha < 0.999f)
            {
                return false;
            }

            var mode = new PreparedWorkRowMode(
                focusViewActive: focusViewActive,
                hideParentWork: parentAlpha <= 0.001f,
                delegateShiftedSkillOverlay: _delegateShiftedSkillOverlay,
                delegateScheduleCells: _delegateScheduleCells);
            var request = new PreparedWorkRowBuildRequest(
                snapshot: _snapshot,
                geometry: _currentGeometry,
                visibleColumns: _visibleColumns,
                rowIndex: rowIndex,
                rowHeight: rowRect.height,
                span: span,
                mode: mode);
            PreparedWorkRowPacket current = _preparedRowPackets[rowIndex];
            if (current != null && current.Matches(in request))
            {
                packet = current;
                return true;
            }

            packet = PreparedWorkRowPacketBuilder.Build(in request);
            _preparedRowPackets[rowIndex] = packet;
            return packet != null;
        }

        public void DrawPreparedRun(
            PreparedWorkRowPacket packet,
            int runIndex,
            float rowOffsetY)
        {
            PreparedWorkRowRun run = packet.Runs[runIndex];
            Color baseColor = GUI.color;
            bool retained = _retainedRows.TryDraw(
                run.Retained,
                rowOffsetY,
                baseColor,
                _snapshot.LayoutRevision,
                _snapshot.RetainedVisualKey,
                _renderResourcesRevision);
            if (!retained)
            {
                DrawPreparedRunDirect(packet, run, rowOffsetY, baseColor);
            }
            else
            {
                DrawPreparedRunLiveForeground(packet, run, rowOffsetY, baseColor);
            }

            DrawPreparedRunDynamic(packet, runIndex, run, rowOffsetY, baseColor);
        }

        public void DrawPreparedCopyPaste(
            int columnIndex,
            Pawn pawn,
            float rowOffsetY)
        {
            WorkGridColumnGeometry geometry = _currentGeometry.Columns[columnIndex];
            // PawnColumnWorker_CopyPasteWorkPriorities normalizes its input to
            // the vanilla button width and row height before drawing. Keep the
            // same normalization even when the surrounding layout is wider or
            // taller than the native column.
            Rect cellRect = new Rect(
                geometry.OffsetX,
                rowOffsetY,
                CopyPasteUI.CopyPasteColumnWidth,
                30f);

            // The native worker remains responsible for input, callbacks, and
            // tooltips. Repaint only needs the vanilla button presentation, so
            // the shared no-op delegates avoid a closure per pawn row.
            if (TimePriorityScheduleEditor.TryDrawScheduleCopyPasteWorkPrioritiesCell(
                    cellRect,
                    pawn))
            {
                return;
            }

            CopyPasteUI.DoCopyPasteButtons(
                cellRect,
                RepaintCopyPasteNoOp,
                _copyPasteClipboardAvailable ? RepaintCopyPasteNoOp : null);
        }

        public bool DrawPreparedPawnLabel(PreparedWorkRowPacket packet, float rowOffsetY)
        {
            PreparedPawnLabelCell label = packet.PawnLabel;
            if (!TryGetLivePawn(packet.RowIndex, packet.PawnId, out Pawn pawn))
            {
                return false;
            }
            Rect cellRect = OffsetY(label.CellRect, rowOffsetY);
            if (pawn.health.summaryHealth.SummaryHealthPercent < 0.99f ||
                (!BWTWorkTabTutorial.OwnsCurrentPointer && Mouse.IsOver(cellRect)))
            {
                // Health and hover chrome sit behind the label text in the
                // native worker. Use it for those live rows rather than alter
                // draw order around the retained text.
                return false;
            }

            DrawPreparedPawnLabelText(label, rowOffsetY);

            Rect iconRect = OffsetY(label.IconRect, rowOffsetY);
            if (label.Presentation.ShowIcon)
            {
                if (Find.Selector.IsSelected(pawn))
                {
                    SelectionDrawerUtility.DrawSelectionOverlayWholeGUI(
                        iconRect.ContractedBy(2f));
                }
                Widgets.ThingIcon(iconRect, pawn);
                ModSupportManager.OnPawnRowDrawn(pawn, iconRect);
                if (label.Presentation.ContrastMode)
                {
                    // The BWT contrast prefix emits this callback, then its
                    // existing postfix emits it again.
                    ModSupportManager.OnPawnRowDrawn(pawn, iconRect);
                }
                WorkTabDiagnostics.RecordPawnLabelIcon(pawn, iconRect);
            }

            if (Widgets.ButtonInvisible(cellRect))
            {
                CameraJumper.TryJumpAndSelect(pawn);
                if (Current.ProgramState == ProgramState.Playing && Event.current.button == 0 &&
                    PawnLabelCloseAdapter.ShouldCloseWorkTab())
                {
                    Find.MainTabsRoot.EscapeCurrentTab(false);
                }
            }
            if (!label.Presentation.ContrastMode)
            {
                // Match PawnColumnWorker_Label's observable GUI-state exit.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
            }
            return true;
        }

        private static void DrawPreparedPawnLabelText(
            PreparedPawnLabelCell label,
            float rowOffsetY)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                // The snapshot owns stable text and geometry, but the glyphs
                // stay live. Drawing font pixels into a transparent retained
                // surface and blending that surface again degrades thin strokes.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                if (label.Presentation.ContrastMode)
                {
                    GUI.color = label.Presentation.BaseTextColor;
                }
                Widgets.Label(OffsetY(label.TextRect, rowOffsetY), label.Text);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private void EndCellBatch()
        {
            if (!_cellBatchActive)
            {
                return;
            }

            try
            {
                FlushRetainedCells();
            }
            finally
            {
                _retainedCells.Clear();
                _pendingParentCells.Clear();
                _pendingSubWorkCells.Clear();
                _cellBatchState.Dispose();
                _cellBatchActive = false;
            }
        }

        private void EnsureCellBatch(GameFont font)
        {
            if (!_cellBatchActive)
            {
                _cellBatchState = GuiStateScope.Capture();
                _cellBatchColor = GUI.color;
                _cellBatchActive = true;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
            }
            if (Text.Font != font)
            {
                Text.Font = font;
            }
        }

        private void FlushRetainedCells()
        {
            if (_pendingParentCells.Count == 0 && _pendingSubWorkCells.Count == 0)
            {
                return;
            }

            bool retained = _retainedRows.TryDraw(
                _retainedCells,
                _cellBatchColor,
                _snapshot.LayoutRevision,
                _snapshot.RetainedVisualKey,
                _renderResourcesRevision);
            for (int index = 0; index < _pendingParentCells.Count; index++)
            {
                PendingParentCell pending = _pendingParentCells[index];
                if (retained)
                {
                    PreparedWorkBoxRenderer.DrawLiveForeground(
                        pending.BoxRect,
                        pending.Visual,
                        pending.Cell.Priority,
                        _cellBatchColor,
                        visualAlpha: 1f,
                        compactText: false);
                    GUI.color = _cellBatchColor;
                }
                else
                {
                    Text.Font = GameFont.Medium;
                    PreparedWorkBoxRenderer.DrawInBatchWithoutStaticFeatureOverlays(
                        pending.BoxRect,
                        pending.Visual,
                        pending.Cell.Priority,
                        1f,
                        _cellBatchColor);
                }

                PreparedWorkBoxRenderer.DrawDynamicOverlays(
                    pending.BoxRect,
                    pending.Visual,
                    _cellBatchColor);
                DrawParentHover(
                    pending.CellRect,
                    pending.Cell,
                    pending.Pawn,
                    pending.WorkType);
            }

            for (int index = 0; index < _pendingSubWorkCells.Count; index++)
            {
                PendingSubWorkCell pending = _pendingSubWorkCells[index];
                if (retained)
                {
                    PreparedWorkBoxRenderer.DrawLiveForeground(
                        pending.BoxRect,
                        pending.Visual,
                        pending.DisplayPriority,
                        _cellBatchColor,
                        visualAlpha: 1f,
                        compactText: pending.BoxRect.width <=
                            WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f);
                    GUI.color = _cellBatchColor;
                }
                else
                {
                    Text.Font = pending.BoxRect.width <=
                        WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f
                            ? GameFont.Tiny
                            : GameFont.Medium;
                    PreparedWorkBoxRenderer.DrawInBatch(
                        pending.BoxRect,
                        pending.Visual,
                        pending.DisplayPriority,
                        1f,
                        _cellBatchColor);
                }

                WorkGiverPriorityBoxRenderer.DrawPreparedPriorityOverlay(
                    pending.WorkGiver,
                    pending.Pawn,
                    pending.BoxRect,
                    pending.HasDynamicRing);
            }
        }

        /// <summary>
        /// Replays the transparent foreground omitted from the retained surface.
        /// The packet owns these indexes and display values, so this pass does
        /// not repeat cell filtering or resolve live domain state.
        /// </summary>
        private static void DrawPreparedRunLiveForeground(
            PreparedWorkRowPacket packet,
            PreparedWorkRowRun run,
            float rowOffsetY,
            Color baseColor)
        {
            if (run.LiveForegroundSlotIndexes.Length == 0)
            {
                return;
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;
            try
            {
                for (int index = 0; index < run.LiveForegroundSlotIndexes.Length; index++)
                {
                    PreparedWorkRowCell slot =
                        packet.Slots[run.LiveForegroundSlotIndexes[index]];
                    int displayPriority = slot.IsSubWork
                        ? slot.Cell.SubWork.EffectivePriority
                        : slot.Cell.Priority;
                    PreparedWorkBoxRenderer.DrawLiveForeground(
                        OffsetY(slot.BoxRect, rowOffsetY),
                        slot.Visual,
                        displayPriority,
                        baseColor,
                        visualAlpha: 1f,
                        compactText: slot.IsSubWork &&
                            slot.BoxRect.width <=
                                WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f);
                }
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
            }
        }

        private void DrawPreparedRunDirect(
            PreparedWorkRowPacket packet,
            PreparedWorkRowRun run,
            float rowOffsetY,
            Color baseColor)
        {
            GuiStateScope state = GuiStateScope.Capture();
            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                for (int index = 0; index < run.SlotIndexes.Length; index++)
                {
                    PreparedWorkRowCell slot = packet.Slots[run.SlotIndexes[index]];
                    Rect boxRect = OffsetY(slot.BoxRect, rowOffsetY);
                    Text.Font = slot.IsSubWork && boxRect.width <=
                        WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f
                            ? GameFont.Tiny
                            : GameFont.Medium;
                    int displayPriority = slot.IsSubWork
                        ? slot.Cell.SubWork.EffectivePriority
                        : slot.Cell.Priority;
                    if (slot.IsSubWork)
                    {
                        PreparedWorkBoxRenderer.DrawInBatch(
                            boxRect,
                            slot.Visual,
                            displayPriority,
                            1f,
                            baseColor);
                    }
                    else
                    {
                        PreparedWorkBoxRenderer.DrawInBatchWithoutStaticFeatureOverlays(
                            boxRect,
                            slot.Visual,
                            displayPriority,
                            1f,
                            baseColor);
                    }
                }
            }
            finally
            {
                state.Dispose();
            }
        }

        private void DrawPreparedRunDynamic(
            PreparedWorkRowPacket packet,
            int runIndex,
            PreparedWorkRowRun run,
            float rowOffsetY,
            Color baseColor)
        {
            bool hasActiveResetAnimations =
                WorkGiverPriorityBoxRenderer.HasActiveResetAnimations;
            // This pass owns prepared overlays, reset-animation replay, and
            // hover chrome. Stable runs with none of those inputs need no
            // dynamic work after their retained/direct pass has completed.
            // IsVisible also owns schedule-editor authority validation, which
            // may finish a stale close; keep that lifecycle read in the gate.
            if (run.ParentDynamicSlotIndexes.Length == 0 &&
                run.SubWorkRingSlotIndexes.Length == 0 &&
                !hasActiveResetAnimations &&
                packet.RowIndex != _hoveredRowIndex &&
                _headerHoveredColumnIndex < 0 &&
                !TimePriorityScheduleEditor.IsVisible)
            {
                return;
            }

            for (int index = 0; index < run.ParentDynamicSlotIndexes.Length; index++)
            {
                PreparedWorkRowCell slot = packet.Slots[run.ParentDynamicSlotIndexes[index]];
                PreparedWorkBoxRenderer.DrawDynamicOverlays(
                    OffsetY(slot.BoxRect, rowOffsetY),
                    slot.Visual,
                    baseColor);
            }

            int headerSlot = GetRunSlot(packet, runIndex, _headerHoveredColumnIndex);
            bool pointerOwned = TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                BWTWorkTabTutorial.OwnsCurrentPointer;
            if (!pointerOwned && headerSlot >= 0)
            {
                Widgets.DrawHighlight(OffsetY(packet.Slots[headerSlot].CellRect, rowOffsetY));
            }

            int hoveredSlot = packet.RowIndex == _hoveredRowIndex
                ? GetRunSlot(packet, runIndex, _hoveredColumnIndex)
                : -1;
            for (int index = 0; index < run.SubWorkRingSlotIndexes.Length; index++)
            {
                DrawSubWorkOverlay(
                    packet,
                    packet.Slots[run.SubWorkRingSlotIndexes[index]],
                    rowOffsetY);
            }

            if (hasActiveResetAnimations)
            {
                for (int index = 0; index < run.SubWorkSlotIndexes.Length; index++)
                {
                    int slotIndex = run.SubWorkSlotIndexes[index];
                    PreparedWorkRowCell slot = packet.Slots[slotIndex];
                    if (TryGetLiveCellReferences(
                            packet.RowIndex,
                            slot.ColumnIndex,
                            out _,
                            out _,
                            out WorkGiver workGiver) &&
                        !slot.Cell.SubWork.HasDynamicRing &&
                        workGiver?.def != null &&
                        WorkGiverPriorityBoxRenderer.HasResetAnimation(
                            slot.Cell.PawnId,
                            workGiver.def))
                    {
                        DrawSubWorkOverlay(packet, slot, rowOffsetY);
                    }
                }
            }

            if (hoveredSlot >= 0)
            {
                PreparedWorkRowCell slot = packet.Slots[hoveredSlot];
                if (slot.IsSubWork)
                {
                    bool hasLiveWorkGiver = TryGetLiveCellReferences(
                        packet.RowIndex,
                        slot.ColumnIndex,
                        out _,
                        out _,
                        out WorkGiver workGiver);
                    if (hasLiveWorkGiver &&
                        workGiver?.def != null &&
                        !slot.Cell.SubWork.HasDynamicRing &&
                        (!hasActiveResetAnimations ||
                         !WorkGiverPriorityBoxRenderer.HasResetAnimation(
                             slot.Cell.PawnId,
                             workGiver.def)))
                    {
                        DrawSubWorkOverlay(packet, slot, rowOffsetY);
                    }
                }
                else if (!pointerOwned)
                {
                    DrawParentTooltip(packet, slot, rowOffsetY);
                }
            }
        }

        private void DrawSubWorkOverlay(
            PreparedWorkRowPacket packet,
            PreparedWorkRowCell slot,
            float rowOffsetY)
        {
            if (!TryGetLiveCellReferences(
                    packet.RowIndex,
                    slot.ColumnIndex,
                    out Pawn pawn,
                    out _,
                    out WorkGiver workGiver))
            {
                return;
            }

            WorkGiverPriorityBoxRenderer.DrawPreparedPriorityOverlay(
                workGiver,
                pawn,
                OffsetY(slot.BoxRect, rowOffsetY),
                slot.Cell.SubWork.HasDynamicRing);
        }

        private void DrawParentTooltip(
            PreparedWorkRowPacket packet,
            PreparedWorkRowCell slot,
            float rowOffsetY)
        {
            if (!TryGetLiveCellReferences(
                    packet.RowIndex,
                    slot.ColumnIndex,
                    out Pawn pawn,
                    out WorkTypeDef workType,
                    out _))
            {
                return;
            }

            Rect boxRect = OffsetY(slot.BoxRect, rowOffsetY);
            Vector2 mousePosition = Event.current.mousePosition;
            if (!boxRect.Contains(mousePosition) || !Mouse.IsOver(boxRect))
            {
                return;
            }

            bool incapable = (slot.Cell.Flags & WorkCellVisualFlags.Incapable) != 0;
            TooltipHandler.TipRegion(
                boxRect,
                () => WidgetsWork.TipForPawnWorker(pawn, workType, incapable),
                pawn.thingIDNumber ^ workType.GetHashCode());
        }

        private static int GetRunSlot(
            PreparedWorkRowPacket packet,
            int runIndex,
            int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= packet.SlotByColumn.Length)
            {
                return -1;
            }
            return packet.RunByColumn[columnIndex] == runIndex
                ? packet.SlotByColumn[columnIndex]
                : -1;
        }

        private static Rect OffsetY(Rect rect, float offsetY)
        {
            rect.y += offsetY;
            return rect;
        }

        private void BuildColumnLookup(WorkGridSnapshot snapshot)
        {
            _parentColumnByWorkType.Clear();
            _columnIndexByHoverKey.Clear();
            _liveReferenceTopologyValid = false;
            if (snapshot == null)
            {
                _liveWorkTypesByColumn = Array.Empty<WorkTypeDef>();
                _liveWorkGiversByColumn = Array.Empty<WorkGiver>();
                return;
            }

            _liveWorkTypesByColumn = new WorkTypeDef[snapshot.Columns.Count];
            _liveWorkGiversByColumn = new WorkGiver[snapshot.Columns.Count];
            bool columnsValid = _currentLayoutColumns.Count == snapshot.Columns.Count;
            for (int columnIndex = 0; columnIndex < snapshot.Columns.Count; columnIndex++)
            {
                WorkGridColumnEntry column = snapshot.Columns[columnIndex];
                if (columnIndex >= _currentLayoutColumns.Count)
                {
                    columnsValid = false;
                    continue;
                }

                WorkTabLayoutColumn layoutColumn = _currentLayoutColumns[columnIndex];
                SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    layoutColumn,
                    out WorkGiver workGiver,
                    out WorkTypeDef subWorkParent,
                    out _);
                WorkTypeDef workType = subWorkParent ?? layoutColumn.Column?.workType;
                if (workType?.shortHash == column.WorkTypeId)
                {
                    _liveWorkTypesByColumn[columnIndex] = workType;
                }
                if (workGiver?.def?.shortHash == column.WorkGiverId)
                {
                    _liveWorkGiversByColumn[columnIndex] = workGiver;
                }

                if (column.WorkerKind == WorkGridColumnWorkerKind.WorkPriority &&
                    _liveWorkTypesByColumn[columnIndex] == null)
                {
                    columnsValid = false;
                }
                else if (column.WorkerKind == WorkGridColumnWorkerKind.CopyPasteWorkPriorities &&
                         (layoutColumn.Column?.Worker == null ||
                          layoutColumn.Column.Worker.GetType() !=
                              typeof(PawnColumnWorker_CopyPasteWorkPriorities)))
                {
                    // The prepared command replays CopyPasteUI only when the
                    // same vanilla worker was classified during snapshot build.
                    columnsValid = false;
                }
                else if (column.WorkerKind == WorkGridColumnWorkerKind.SubWorkPriority &&
                         (_liveWorkTypesByColumn[columnIndex] == null ||
                          _liveWorkGiversByColumn[columnIndex]?.def == null))
                {
                    columnsValid = false;
                }

                if (column.WorkerKind == WorkGridColumnWorkerKind.WorkPriority &&
                    workType != null)
                {
                    _parentColumnByWorkType[workType] = columnIndex;
                }
                _columnIndexByHoverKey[new HoverColumnKey(layoutColumn)] = columnIndex;
            }

            _liveReferenceTopologyValid = columnsValid && HasMatchingLiveRows(snapshot);
        }

        private bool ReadCopyPasteClipboardState()
        {
            if (_snapshot == null || !_liveReferenceTopologyValid)
            {
                return false;
            }

            for (int columnIndex = 0; columnIndex < _snapshot.Columns.Count; columnIndex++)
            {
                if (_snapshot.Columns[columnIndex].WorkerKind !=
                    WorkGridColumnWorkerKind.CopyPasteWorkPriorities)
                {
                    continue;
                }

                PawnColumnWorker_CopyPasteWorkPriorities worker =
                    (PawnColumnWorker_CopyPasteWorkPriorities)
                        _currentLayoutColumns[columnIndex].Column.Worker;
                return WorkGridVanillaCompatibilityPolicy.ReadCopyPasteClipboard(worker);
            }

            return false;
        }

        private bool HasMatchingLiveRows(WorkGridSnapshot snapshot)
        {
            if (_currentLayoutRows.Count != snapshot.Rows.Count)
            {
                return false;
            }

            for (int rowIndex = 0; rowIndex < snapshot.Rows.Count; rowIndex++)
            {
                if (!HasMatchingLiveRow(rowIndex, snapshot.Rows[rowIndex]))
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasMatchingLiveRow(int rowIndex, WorkGridRowEntry prepared)
        {
            if (rowIndex < 0 || rowIndex >= _currentLayoutRows.Count)
            {
                return false;
            }

            WorkTabLayoutRow live = _currentLayoutRows[rowIndex];
            return prepared.Kind == WorkGridRowKind.Pawn
                ? IsLiveRenderablePawn(live.Pawn) &&
                  live.Pawn.thingIDNumber == prepared.PawnId
                : live.IsDivider;
        }

        private bool TryGetLivePawn(int rowIndex, int expectedPawnId, out Pawn pawn)
        {
            pawn = rowIndex >= 0 && rowIndex < _currentLayoutRows.Count
                ? _currentLayoutRows[rowIndex].Pawn
                : null;
            return IsLiveRenderablePawn(pawn) && pawn.thingIDNumber == expectedPawnId;
        }

        /// <summary>
        /// Owns the live-row precondition for prepared drawing: the pawn must be
        /// alive, present, and work-capable. Death/destruction can race the
        /// table's roster recache; failing this boundary sends the row back
        /// through native drawing, so prepared callers do not add duplicate
        /// null or eligibility checks.
        /// </summary>
        private static bool IsLiveRenderablePawn(Pawn pawn)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   !pawn.Destroyed &&
                   pawn.workSettings != null &&
                   pawn.workSettings.EverWork;
        }

        /// <summary>
        /// Resolves live interaction objects from the pass-captured layout. The
        /// immutable snapshot owns pixels and identifiers only; hover, tooltips,
        /// selection, and native fallback receive their entities through this
        /// separate lookup and fail closed if topology no longer matches.
        /// </summary>
        private bool TryGetLiveCellReferences(
            int rowIndex,
            int columnIndex,
            out Pawn pawn,
            out WorkTypeDef workType,
            out WorkGiver workGiver)
        {
            workType = columnIndex >= 0 && columnIndex < _liveWorkTypesByColumn.Length
                ? _liveWorkTypesByColumn[columnIndex]
                : null;
            workGiver = columnIndex >= 0 && columnIndex < _liveWorkGiversByColumn.Length
                ? _liveWorkGiversByColumn[columnIndex]
                : null;
            int expectedPawnId = rowIndex >= 0 && rowIndex < _snapshot.Rows.Count
                ? _snapshot.Rows[rowIndex].PawnId
                : -1;
            return TryGetLivePawn(rowIndex, expectedPawnId, out pawn) && workType != null;
        }

        private void ResolveHoverTargets(in WorkTabView context)
        {
            _hoveredRowIndex = -1;
            _hoveredColumnIndex = -1;
            _headerHoveredColumnIndex = -1;

            WorkTypeDef headerWorkType =
                PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType;
            if (headerWorkType != null &&
                _parentColumnByWorkType.TryGetValue(headerWorkType, out int headerColumnIndex))
            {
                _headerHoveredColumnIndex = headerColumnIndex;
            }

            Event current = Event.current;
            if (current == null || context.Layout == null || !context.HasMatchingLayoutRevision)
            {
                return;
            }

            Vector2 mousePosition = current.mousePosition;
            if (context.Layout.TryGetRowAt(mousePosition, out WorkTabLayoutRow row) &&
                row.Pawn != null)
            {
                _hoveredRowIndex = row.VisualIndex;
            }
            if (!context.Layout.TryGetBodyColumnAt(mousePosition, out WorkTabLayoutColumn hovered))
            {
                return;
            }

            if (_columnIndexByHoverKey.TryGetValue(
                    new HoverColumnKey(hovered),
                    out int hoveredColumnIndex))
            {
                _hoveredColumnIndex = hoveredColumnIndex;
            }
        }

        private bool TryDrawSubWorkCell(
            int rowIndex,
            int columnIndex,
            Rect cellRect,
            WorkGridColumnEntry column,
            WorkCellVisualState cell)
        {
            WorkGridSubWorkVisualState presentation = cell.SubWork;
            if (_eventPhase != ImGuiEventPhase.Repaint ||
                !presentation.IsPrepared ||
                !presentation.CanUseStablePresentation ||
                !TryGetLiveCellReferences(
                    rowIndex,
                    columnIndex,
                    out Pawn pawn,
                    out _,
                    out WorkGiver workGiver) ||
                workGiver?.def == null)
            {
                EndCellBatch();
                return false;
            }

            Rect priorityBoxRect = column.IsExpandBesideChild
                ? WorkPriorityCellGeometry.GetFluffyStyleSubWorkPriorityBoxRect(cellRect)
                : WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            bool stablePawnBox = !_delegateScheduleCells &&
                !SubWorkDrilldownState.IsTransitioning &&
                !ColumnReorderAnimationState.IsActive;
            if (stablePawnBox)
            {
                int displayPriority = presentation.EffectivePriority;
                bool compactText = priorityBoxRect.width <=
                    WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f;
                EnsureCellBatch(compactText ? GameFont.Tiny : GameFont.Medium);
                _retainedCells.Add(new RetainedWorkBoxRowCache.Cell(
                    cell.PawnId,
                    column.ColumnIndex,
                    priorityBoxRect,
                    presentation.WorkBoxVisual,
                    displayPriority));
                _pendingSubWorkCells.Add(new PendingSubWorkCell(
                    workGiver,
                    pawn,
                    priorityBoxRect,
                    presentation.WorkBoxVisual,
                    presentation.HasDynamicRing,
                    displayPriority));
                return true;
            }

            EndCellBatch();
            return false;
        }

        private static void DrawCellStandalone(
            Rect cellRect,
            byte priority,
            Color priorityColor,
            WorkCellVisualFlags flags,
            byte skillBand,
            float skillBlend,
            byte passion,
            float visualAlpha)
        {
            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            var visual = new WorkBoxVisualState(
                priority,
                skillBand,
                skillBlend,
                passion,
                priorityColor,
                flags);
            PreparedWorkBoxRenderer.Draw(boxRect, visual, priority, visualAlpha);
        }

        private void DrawCellInBatch(
            Rect cellRect,
            byte priority,
            Color priorityColor,
            WorkCellVisualFlags flags,
            byte skillBand,
            float skillBlend,
            byte passion,
            float visualAlpha)
        {
            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            var visual = new WorkBoxVisualState(
                priority,
                skillBand,
                skillBlend,
                passion,
                priorityColor,
                flags);
            PreparedWorkBoxRenderer.DrawInBatch(
                boxRect,
                visual,
                priority,
                visualAlpha,
                _cellBatchColor);
        }

        private static WorkBoxVisualState CreateVisual(WorkCellVisualState cell)
        {
            return new WorkBoxVisualState(
                cell.Priority,
                cell.SkillBand,
                cell.SkillBlend,
                cell.Passion,
                cell.PriorityColor,
                cell.Flags);
        }

        private static void DrawParentHover(
            Rect cellRect,
            WorkCellVisualState cell,
            Pawn pawn,
            WorkTypeDef workType)
        {
            if (TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                BWTWorkTabTutorial.OwnsCurrentPointer)
            {
                return;
            }

            if (PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType == workType)
            {
                Widgets.DrawHighlight(cellRect);
            }

            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            Vector2 mousePosition = Event.current.mousePosition;
            if (boxRect.Contains(mousePosition) && Mouse.IsOver(boxRect))
            {
                bool incapable = (cell.Flags & WorkCellVisualFlags.Incapable) != 0;
                TooltipHandler.TipRegion(
                    boxRect,
                    () => WidgetsWork.TipForPawnWorker(pawn, workType, incapable),
                    pawn.thingIDNumber ^ workType.GetHashCode());
            }
        }

        private static Color UnpackColor(uint packed)
        {
            return new Color32(
                (byte)packed,
                (byte)(packed >> 8),
                (byte)(packed >> 16),
                (byte)(packed >> 24));
        }

        private readonly struct PendingParentCell
        {
            internal PendingParentCell(
                Rect cellRect,
                Rect boxRect,
                WorkCellVisualState cell,
                WorkBoxVisualState visual,
                Pawn pawn,
                WorkTypeDef workType)
            {
                CellRect = cellRect;
                BoxRect = boxRect;
                Cell = cell;
                Visual = visual;
                Pawn = pawn;
                WorkType = workType;
            }

            internal Rect CellRect { get; }
            internal Rect BoxRect { get; }
            internal WorkCellVisualState Cell { get; }
            internal WorkBoxVisualState Visual { get; }
            internal Pawn Pawn { get; }
            internal WorkTypeDef WorkType { get; }
        }

        private readonly struct HoverColumnKey : IEquatable<HoverColumnKey>
        {
            internal HoverColumnKey(WorkTabLayoutColumn column)
            {
                Column = column.Column;
                SubWorkGiver = column.SubWorkGiver;
                SubWorkSlot = column.SubWorkSlot;
                IsExpandBesideChild = column.IsExpandBesideChild;
            }

            private PawnColumnDef Column { get; }
            private WorkGiverDef SubWorkGiver { get; }
            private int SubWorkSlot { get; }
            private bool IsExpandBesideChild { get; }

            public bool Equals(HoverColumnKey other)
            {
                return ReferenceEquals(Column, other.Column) &&
                       ReferenceEquals(SubWorkGiver, other.SubWorkGiver) &&
                       SubWorkSlot == other.SubWorkSlot &&
                       IsExpandBesideChild == other.IsExpandBesideChild;
            }

            public override bool Equals(object obj)
            {
                return obj is HoverColumnKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Column != null ? RuntimeHelpers.GetHashCode(Column) : 0;
                    hash = (hash * 397) ^
                        (SubWorkGiver != null ? RuntimeHelpers.GetHashCode(SubWorkGiver) : 0);
                    hash = (hash * 397) ^ SubWorkSlot;
                    hash = (hash * 397) ^ (IsExpandBesideChild ? 1 : 0);
                    return hash;
                }
            }
        }

        private readonly struct PendingSubWorkCell
        {
            internal PendingSubWorkCell(
                WorkGiver workGiver,
                Pawn pawn,
                Rect boxRect,
                WorkBoxVisualState visual,
                bool hasDynamicRing,
                int displayPriority)
            {
                WorkGiver = workGiver;
                Pawn = pawn;
                BoxRect = boxRect;
                Visual = visual;
                HasDynamicRing = hasDynamicRing;
                DisplayPriority = displayPriority;
            }

            internal WorkGiver WorkGiver { get; }
            internal Pawn Pawn { get; }
            internal Rect BoxRect { get; }
            internal WorkBoxVisualState Visual { get; }
            internal bool HasDynamicRing { get; }
            internal int DisplayPriority { get; }
        }

    }
}
