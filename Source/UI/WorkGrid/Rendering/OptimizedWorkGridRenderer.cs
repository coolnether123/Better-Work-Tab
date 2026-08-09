using System;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using RimWorld;
using Spine.RimWorld.Rendering;
using Spine.RimWorld.Rendering.GuiState;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal sealed class OptimizedWorkGridRenderer : IWorkGridRenderer, IWorkGridSnapshotLayer, IDisposable
    {
        internal const string RendererId = "bwt.optimized-layered";
        private readonly IWorkGridDrawingSurface _drawingSurface;
        private WorkGridSnapshot _snapshot;
        private int[] _cellLookup = Array.Empty<int>();
        private WorkGridIndexRange _visibleRows;
        private WorkGridIndexRange _visibleColumns;
        private bool _delegateFeatureCells;
        private GuiStateScope _cellBatchState;
        private bool _cellBatchActive;

        internal OptimizedWorkGridRenderer(IWorkGridDrawingSurface drawingSurface)
        {
            _drawingSurface = drawingSurface ?? throw new ArgumentNullException(nameof(drawingSurface));
        }

        public string Id => RendererId;
        public int Priority => 100;

        public bool IsAvailable(in WorkGridRenderContext context)
        {
            return context.Presentation.Snapshot != null &&
                   context.Presentation.Geometry != null &&
                   context.Layout != null &&
                   context.Presentation.Table != null;
        }

        public void Prepare(in WorkGridRenderContext context)
        {
            WorkGridSnapshot snapshot = context.Presentation.Snapshot;
            if (!ReferenceEquals(snapshot, _snapshot))
            {
                _snapshot = snapshot;
                BuildCellLookup(snapshot);
            }

            if (snapshot == null)
            {
                return;
            }

            _delegateFeatureCells =
                ((context.Configuration.Features & WorkGridFeatureFlags.SkillOverlay) != 0 &&
                 ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted) ||
                SubWorkDrilldownState.HasAnyDrilldown ||
                FluffyTimeScheduleAssigner.IsOpen ||
                SleekWorkTabGateway.BetterWorkTabHostsSleek;

            if (context.EventPhase == ImGuiEventPhase.Repaint)
            {
                Vector2 scroll = context.Presentation.Table.scrollPosition;
                _visibleRows = context.Presentation.Geometry.GetVisibleRowRange(context.Viewport, scroll.y);
                _visibleColumns = context.Presentation.Geometry.GetVisibleColumnRange(context.Viewport, scroll.x);
            }
        }

        public void Draw(in WorkGridRenderContext context)
        {
            if (context.EventPhase != ImGuiEventPhase.Repaint)
            {
                _drawingSurface.DrawNativeWorkTable(
                    context.Presentation.Table,
                    context.Layout,
                    context.WindowRect);
                return;
            }

            _drawingSurface.DrawSnapshotWorkTable(in context, this);
        }

        public void HandleEvent(in WorkGridRenderContext context)
        {
        }

        public void ReleaseTransient(in WorkGridRenderContext context)
        {
        }

        public bool TryDrawRowBackground(int rowIndex, Rect rowRect, out Color textColor)
        {
            textColor = Color.white;
            if (_snapshot == null || rowIndex < 0 || rowIndex >= _snapshot.Rows.Count)
            {
                return false;
            }

            WorkGridRowEntry row = _snapshot.Rows[rowIndex];
            if ((row.VisualFlags & WorkGridRowVisualFlags.HasBackground) == 0)
            {
                return false;
            }

            Color color = UnpackColor(row.BackgroundColor);
            Widgets.DrawBoxSolid(rowRect, color);
            if (row.Kind == WorkGridRowKind.Pawn)
            {
                textColor = Spine.UI.TextColorHelper.GetContrastingTextColor(color);
            }
            return true;
        }

        public void BeginRow()
        {
            EndCellBatch();
        }

        public void EndRow()
        {
            EndCellBatch();
        }

        public bool TryDrawCell(int rowIndex, int columnIndex, Rect cellRect)
        {
            if (_snapshot == null ||
                _delegateFeatureCells ||
                rowIndex < 0 || columnIndex < 0 ||
                rowIndex >= _snapshot.Rows.Count || columnIndex >= _snapshot.Columns.Count ||
                _snapshot.Columns[columnIndex].WorkerKind != WorkGridColumnWorkerKind.WorkPriority)
            {
                EndCellBatch();
                return false;
            }

            int lookupIndex = (rowIndex * _snapshot.Columns.Count) + columnIndex;
            if (lookupIndex < 0 || lookupIndex >= _cellLookup.Length)
            {
                EndCellBatch();
                return false;
            }

            int cellIndex = _cellLookup[lookupIndex];
            if (cellIndex < 0)
            {
                EndCellBatch();
                return false;
            }

            WorkCellVisualState cell = _snapshot.Cells[cellIndex];
            if (!_cellBatchActive)
            {
                _cellBatchState = GuiStateScope.Capture();
                _cellBatchActive = true;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
            }
            DrawCell(cellRect, cell);
            return true;
        }

        public bool ShouldVisitCell(int rowIndex, int columnIndex)
        {
            return rowIndex >= _visibleRows.Start && rowIndex < _visibleRows.EndExclusive &&
                   columnIndex >= _visibleColumns.Start && columnIndex < _visibleColumns.EndExclusive;
        }

        public void Dispose()
        {
            EndCellBatch();
            _snapshot = null;
            _cellLookup = Array.Empty<int>();
        }

        private void EndCellBatch()
        {
            if (!_cellBatchActive)
            {
                return;
            }

            _cellBatchState.Dispose();
            _cellBatchActive = false;
        }

        private void BuildCellLookup(WorkGridSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Rows.Count == 0 || snapshot.Columns.Count == 0)
            {
                _cellLookup = Array.Empty<int>();
                return;
            }

            int length = checked(snapshot.Rows.Count * snapshot.Columns.Count);
            _cellLookup = new int[length];
            for (int index = 0; index < length; index++)
            {
                _cellLookup[index] = -1;
            }

            int rowIndex = 0;
            for (int cellIndex = 0; cellIndex < snapshot.Cells.Count; cellIndex++)
            {
                WorkCellVisualState cell = snapshot.Cells[cellIndex];
                while (rowIndex < snapshot.Rows.Count && snapshot.Rows[rowIndex].PawnId != cell.PawnId)
                {
                    rowIndex++;
                }
                if (rowIndex >= snapshot.Rows.Count)
                {
                    break;
                }

                _cellLookup[(rowIndex * snapshot.Columns.Count) + cell.ColumnIndex] = cellIndex;
            }
        }

        private void DrawCell(Rect cellRect, WorkCellVisualState cell)
        {
            bool ageDisabled = (cell.Flags & WorkCellVisualFlags.AgeDisabled) != 0;
            if ((cell.Flags & WorkCellVisualFlags.Disabled) != 0 && !ageDisabled)
            {
                return;
            }

            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            if (ageDisabled)
            {
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxBGTex_AgeDisabled);
                return;
            }

            if ((cell.Flags & WorkCellVisualFlags.Incapable) != 0)
            {
                GUI.color = new Color(1f, 0.3f, 0.3f);
            }

            // Snapshot eligibility already rejects non-observational patches to RimWorld's
            // Work-box hooks. Reuse the captured visual state here so Repaint does not
            // recalculate skills, ideology, active work, and passion for every visible cell.
            DrawCachedWorkBoxBackground(boxRect, cell);

            GUI.color = Color.white;
            if (_snapshot.ManualPriorities)
            {
                if (cell.Priority > WorkPrioritySystem.DisabledPriority)
                {
                    GUI.color = UnpackColor(cell.PriorityColor);
                    Widgets.Label(boxRect.ContractedBy(-3f), ((int)cell.Priority).ToStringCached());
                    GUI.color = Color.white;
                }
            }
            else if (cell.Priority > WorkPrioritySystem.DisabledPriority)
            {
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
            }

            DrawStaticFeatureOverlays(boxRect, cell);
        }

        private void DrawCachedWorkBoxBackground(Rect boxRect, WorkCellVisualState cell)
        {
            Texture2D baseTexture;
            Texture2D blendTexture;
            switch (cell.SkillBand)
            {
                case 0:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Awful;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    break;
                case 1:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    break;
                default:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Excellent;
                    break;
            }

            Color baseColor = GUI.color;
            GUI.DrawTexture(boxRect, baseTexture);
            GUI.color = new Color(baseColor.r, baseColor.g, baseColor.b, cell.SkillBlend);
            GUI.DrawTexture(boxRect, blendTexture);

            if ((cell.Flags & WorkCellVisualFlags.IdeologyWarning) != 0)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxOverlay_PreceptWarning);
            }
            if ((cell.Flags & WorkCellVisualFlags.LowSkillWarning) != 0)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(boxRect.ContractedBy(-2f), WidgetsWork.WorkBoxOverlay_Warning);
            }
            if (cell.Passion > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Rect passionRect = boxRect;
                passionRect.xMin = boxRect.center.x;
                passionRect.yMin = boxRect.center.y;
                GUI.DrawTexture(
                    passionRect,
                    cell.Passion == 1
                        ? WidgetsWork.PassionWorkboxMinorIcon
                        : WidgetsWork.PassionWorkboxMajorIcon);
            }

            GUI.color = Color.white;
        }

        private static void DrawStaticFeatureOverlays(Rect boxRect, WorkCellVisualState cell)
        {
            if ((cell.Flags & (WorkCellVisualFlags.Disabled | WorkCellVisualFlags.Incapable)) != 0)
            {
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if ((cell.Flags & WorkCellVisualFlags.BestPawn) != 0 &&
                settings != null &&
                !settings.disableBestPawnHighlight)
            {
                BetterWorkTabSettings.ShowUIMode mode = settings.ShowUIMode_ShowPawnForSkillSquare;
                if (mode == BetterWorkTabSettings.ShowUIMode.Always || mode == ShiftHelper.State)
                {
                    Widgets.DrawBoxSolidWithOutline(
                        boxRect.ExpandedBy(1f),
                        Color.clear,
                        settings.Color_BestPawnForSkillSquare,
                        settings.bestPawnHighlightThickness);
                }
            }

            if ((cell.Flags & WorkCellVisualFlags.OverrideRing) != 0)
            {
                PriorityOverrideRing.Draw(boxRect);
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
    }
}
