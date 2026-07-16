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
using RimWorld;
using Spine.Api;
using Spine.Caching;
using Spine.RimWorld.Api;
using Spine.RimWorld.Rendering;
using Spine.RimWorld.Rendering.GuiState;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal interface IWorkGridSnapshotLayer
    {
        void BeginRow();
        void EndRow();
        bool TryDrawRowBackground(int rowIndex, Rect rowRect, out Color textColor);
        bool ShouldVisitCell(int rowIndex, int columnIndex);
        bool TryDrawCell(int rowIndex, int columnIndex, Rect cellRect);
    }

    internal readonly struct WorkGridAtlasEntry
    {
        internal WorkGridAtlasEntry(Texture2D baseTexture, Texture2D blendTexture, string priorityText)
        {
            BaseTexture = baseTexture;
            BlendTexture = blendTexture;
            PriorityText = priorityText;
        }

        internal Texture2D BaseTexture { get; }
        internal Texture2D BlendTexture { get; }
        internal string PriorityText { get; }
    }

    internal sealed class WorkGridCellAtlas : IRenderAtlas<WorkGridAtlasKey, WorkGridAtlasEntry>
    {
        internal const long DefaultBudgetBytes = 4L * 1024L * 1024L;
        private const long EntryBytes = 128L;
        private const int VariantCount = 6;
        private const int HotEntryCount = 256 * VariantCount;
        private readonly BoundedLruCache<WorkGridAtlasKey, WorkGridAtlasEntry> _entries;
        private readonly WorkGridAtlasKey[] _hotKeys = new WorkGridAtlasKey[HotEntryCount];
        private readonly WorkGridAtlasEntry[] _hotEntries = new WorkGridAtlasEntry[HotEntryCount];
        private readonly bool[] _hotValid = new bool[HotEntryCount];
        private long _hotHits;

        internal WorkGridCellAtlas(long budgetBytes = DefaultBudgetBytes)
        {
            _entries = new BoundedLruCache<WorkGridAtlasKey, WorkGridAtlasEntry>(budgetBytes);
        }

        public int EntryCount => _entries.EntryCount;
        public long BudgetBytes => _entries.BudgetBytes;
        public long UsedBytes => _entries.UsedBytes;
        public long Hits => _entries.Hits + _hotHits;
        public long Misses => _entries.Misses;
        public long Evictions => _entries.Evictions;

        public bool TryGet(WorkGridAtlasKey key, out WorkGridAtlasEntry entry)
        {
            int hotIndex = GetHotIndex(key);
            if (_hotValid[hotIndex] && _hotKeys[hotIndex].Equals(key))
            {
                _hotHits++;
                entry = _hotEntries[hotIndex];
                return true;
            }

            if (!_entries.TryGet(key, out entry))
            {
                return false;
            }

            StoreHot(hotIndex, key, entry);
            return true;
        }

        internal WorkGridAtlasEntry GetOrCreate(WorkGridAtlasKey key)
        {
            if (TryGet(key, out WorkGridAtlasEntry entry))
            {
                return entry;
            }

            Texture2D baseTexture = null;
            Texture2D blendTexture = null;
            switch (key.Variant)
            {
                case WorkGridAtlasVisualVariant.AgeDisabled:
                    baseTexture = WorkGiverPriorityBoxCompatibility.WorkBoxBGTexAgeDisabled;
                    break;
                case WorkGridAtlasVisualVariant.SkillAwfulBad:
                    baseTexture = WorkGiverPriorityBoxCompatibility.WorkBoxBGTexAgeDisabled;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    break;
                case WorkGridAtlasVisualVariant.SkillBadMid:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    break;
                case WorkGridAtlasVisualVariant.SkillMidExcellent:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Excellent;
                    break;
            }

            string priorityText = key.Priority == 0 ? string.Empty : key.Priority.ToString();
            entry = new WorkGridAtlasEntry(baseTexture, blendTexture, priorityText);
            long evictions = _entries.Evictions;
            _entries.AddOrUpdate(key, entry, EntryBytes);
            if (_entries.Evictions != evictions)
            {
                Array.Clear(_hotValid, 0, _hotValid.Length);
            }
            if (_entries.TryGet(key, out WorkGridAtlasEntry retainedEntry))
            {
                entry = retainedEntry;
                StoreHot(GetHotIndex(key), key, entry);
            }
            return entry;
        }

        public void Reset()
        {
            _entries.Reset();
            Array.Clear(_hotValid, 0, _hotValid.Length);
            _hotHits = 0;
        }
        public void Dispose() => Reset();

        private static int GetHotIndex(WorkGridAtlasKey key)
        {
            return (key.Priority * VariantCount) + (int)key.Variant;
        }

        private void StoreHot(int index, WorkGridAtlasKey key, WorkGridAtlasEntry entry)
        {
            _hotKeys[index] = key;
            _hotEntries[index] = entry;
            _hotValid[index] = true;
        }
    }

    internal sealed class OptimizedWorkGridRenderer : IWorkGridRenderer, IWorkGridSnapshotLayer, IDisposable
    {
        internal const string RendererId = "bwt.optimized-layered";
        private readonly MainTabWindow_BetterWork _host;
        private readonly WorkGridCellAtlas _atlas = new WorkGridCellAtlas();
        private WorkGridSnapshot _snapshot;
        private int[] _cellLookup = ArrayCompat.Empty<int>();
        private WorkGridIndexRange _visibleRows;
        private WorkGridIndexRange _visibleColumns;
        private bool _delegateFeatureCells;
        private int _atlasRevision = int.MinValue;
        private GuiStateScope _cellBatchState;
        private bool _cellBatchActive;

        internal OptimizedWorkGridRenderer(MainTabWindow_BetterWork host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            WorkGridRendererDiagnostics.SetAtlasDiagnostics(_atlas);
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

            int atlasRevision = unchecked(
                (snapshot.UiScaleRevision * 397) ^
                snapshot.FontThemeRevision ^
                snapshot.PriorityRangeRevision);
            if (_atlasRevision != atlasRevision)
            {
                _atlas.Reset();
                _atlasRevision = atlasRevision;
            }

            _delegateFeatureCells =
                ((context.Configuration.Features & WorkGridFeatureFlags.SkillOverlay) != 0 &&
                 ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted) ||
                SubWorkDrilldownState.HasAnyDrilldown ||
                FluffyTimeScheduleAssigner.IsOpen;

            if (context.EventPhase == ImGuiEventPhase.Repaint)
            {
                Vector2 scroll = PawnTableCompat.GetScrollPosition(context.Presentation.Table);
                _visibleRows = context.Presentation.Geometry.GetVisibleRowRange(context.Viewport, scroll.y);
                _visibleColumns = context.Presentation.Geometry.GetVisibleColumnRange(context.Viewport, scroll.x);
            }
        }

        public void Draw(in WorkGridRenderContext context)
        {
            if (context.EventPhase != ImGuiEventPhase.Repaint)
            {
                _host.DrawNativeWorkTable(context.Presentation.Table, context.Layout, context.WindowRect);
                return;
            }

            _host.DrawSnapshotWorkTable(in context, this);
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
            _atlas.Dispose();
            _snapshot = null;
            _cellLookup = ArrayCompat.Empty<int>();
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
                _cellLookup = ArrayCompat.Empty<int>();
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
                WorkGridAtlasEntry age = GetEntry(cell, WorkGridAtlasVisualVariant.AgeDisabled);
                GUI.DrawTexture(boxRect, age.BaseTexture);
                return;
            }

            if ((cell.Flags & WorkCellVisualFlags.Incapable) != 0)
            {
                GUI.color = new Color(1f, 0.3f, 0.3f);
            }

            // Background ownership stays with RimWorld. This deliberately calls the same
            // method as PawnColumnWorker_WorkPriority instead of maintaining a BWT copy of
            // its skill texture blending, ideology warning, low-skill warning, and passion
            // rendering rules.
            #if v1_1 || v1_0 || v0_19
            WidgetsWork.DrawWorkBoxFor(boxRect.x, boxRect.y, cell.Pawn, cell.WorkType, _snapshot.ManualPriorities);
            #else
            WidgetsWork.DrawWorkBoxFor(boxRect, cell.Pawn, cell.WorkType);
            #endif

            GUI.color = Color.white;
            if (_snapshot.ManualPriorities)
            {
                if (cell.Priority > WorkPrioritySystem.DisabledPriority)
                {
                    WorkGridAtlasEntry glyph = GetEntry(cell, WorkGridAtlasVisualVariant.Priority);
                    GUI.color = UnpackColor(cell.PriorityColor);
                    Widgets.Label(boxRect.ContractedBy(-3f), glyph.PriorityText);
                }
            }
            else if (cell.Priority > WorkPrioritySystem.DisabledPriority)
            {
                GetEntry(cell, WorkGridAtlasVisualVariant.Checkbox);
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
            }

            DrawStaticFeatureOverlays(boxRect, cell);
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
                    Color previousColor = GUI.color;
                    GUI.color = settings.Color_BestPawnForSkillSquare;
                    Widgets.DrawBox(boxRect.ExpandedBy(1f), settings.bestPawnHighlightThickness);
                    GUI.color = previousColor;
                }
            }

            if ((cell.Flags & WorkCellVisualFlags.OverrideRing) != 0)
            {
                PriorityOverrideRing.Draw(boxRect);
            }
        }

        private WorkGridAtlasEntry GetEntry(WorkCellVisualState cell, WorkGridAtlasVisualVariant variant)
        {
            return _atlas.GetOrCreate(new WorkGridAtlasKey(
                _snapshot.UiScaleRevision,
                _snapshot.FontThemeRevision,
                _snapshot.PriorityRangeRevision,
                cell.Priority,
                variant));
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
