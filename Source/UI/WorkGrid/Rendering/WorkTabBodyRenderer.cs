using System;
using System.Collections.Generic;
using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Dividers;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.Workloads;
using RimWorld;
using Spine.Profiling;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Owns the scroll-view body of the Work tab, including row presentation and
    /// body geometry used by the surrounding interaction routes.
    /// </summary>
    internal sealed class WorkTabBodyRenderer
    {
        private const float MinimumPawnRenderHeight = 30f;
        private const float HorizontalCullBuffer = 64f;
        private const float RowCullBuffer = 60f;

        private readonly WorkTabViewportController _viewportController;
        private readonly List<WorkTabLayoutColumn> _visibleRenderColumns = new List<WorkTabLayoutColumn>(64);
        private readonly List<InspectionColumnBinding> _inspectionColumnBindings =
            new List<InspectionColumnBinding>(64);
        private readonly WorkloadInspectionColumnIndex _inspectionColumnIndex =
            new WorkloadInspectionColumnIndex();
        private readonly Dictionary<int, int> _inspectionVisibleRows =
            new Dictionary<int, int>();
        private readonly List<int> _inspectionVisiblePawnRows = new List<int>(32);
        private readonly Dictionary<int, float> _inspectionVisibleRowOffsets =
            new Dictionary<int, float>();
        private readonly Dictionary<int, WorkloadInspectionCellKind> _inspectionGlobalColumns =
            new Dictionary<int, WorkloadInspectionCellKind>();
        private readonly WorkloadInspectionCellMasks _inspectionCellMasks =
            new WorkloadInspectionCellMasks();
        private IReadOnlyList<WorkTabLayoutColumn> _inspectionBindingColumns;
        private int _inspectionBindingLayoutRevision = int.MinValue;
        private int _inspectionBindingSubWorkRevision = int.MinValue;
        private static Color CurrentRowTextColor = Color.white;

        private readonly struct InspectionColumnBinding
        {
            internal InspectionColumnBinding(
                WorkTypeDef workType,
                WorkGiverDef workGiver,
                bool isExpandBesideChild,
                bool isPriorityCell)
            {
                WorkType = workType;
                WorkGiver = workGiver;
                IsExpandBesideChild = isExpandBesideChild;
                IsPriorityCell = isPriorityCell;
            }

            internal WorkTypeDef WorkType { get; }
            internal WorkGiverDef WorkGiver { get; }
            internal bool IsExpandBesideChild { get; }
            internal bool IsPriorityCell { get; }
        }

        internal WorkTabBodyRenderer(WorkTabViewportController viewportController)
        {
            _viewportController = viewportController ??
                throw new ArgumentNullException(nameof(viewportController));
        }

        internal void DrawRows(
            PawnTable table,
            IWorkTabLayoutController layout,
            WorkTabViewport viewport,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            if (layout == null)
            {
                return;
            }

            var rowDescriptors = layout.GetRowDescriptors();
            var columns = layout.Columns;
            if (rowDescriptors == null || rowDescriptors.Count == 0 ||
                columns == null || columns.Count == 0)
            {
                table.scrollPosition = Vector2.zero;
                return;
            }

            WorkTabHorizontalScrollbarDragResult horizontalScrollbarDrag =
                _viewportController.PrepareHorizontalScrollbarDrag(
                    viewport.OutRect,
                    viewport.ViewRect,
                    table.scrollPosition,
                    Event.current);

            Widgets.BeginScrollView(viewport.OutRect, ref table.scrollPosition, viewport.ViewRect);
            try
            {
                if (horizontalScrollbarDrag.ApplyCapturedScroll)
                {
                    Vector2 scrollPosition = table.scrollPosition;
                    scrollPosition.x = horizontalScrollbarDrag.CapturedScrollX;
                    table.scrollPosition = scrollPosition;
                }

                _viewportController.CaptureHorizontalScrollbarDragIfNeeded(
                    in horizontalScrollbarDrag,
                    table.scrollPosition);

                if (Event.current.type == EventType.Layout)
                {
                    return;
                }

                var nameColumn = FindNameColumn(columns);
                IReadOnlyList<WorkTabLayoutColumn> renderColumns = columns;
                if (snapshotLayer == null &&
                    viewport.ViewRect.width > viewport.OutRect.width + 0.5f)
                {
                    float visibleLeft = table.scrollPosition.x - HorizontalCullBuffer;
                    float visibleRight = table.scrollPosition.x + viewport.OutRect.width + HorizontalCullBuffer;
                    _visibleRenderColumns.Clear();
                    for (int i = 0; i < columns.Count; i++)
                    {
                        WorkTabLayoutColumn column = columns[i];
                        WorkGridAnimatedColumnGeometry geometry =
                            WorkGridInteractionGeometry.GetAnimatedColumn(column);
                        if (geometry.BodyContentX + geometry.Width >= visibleLeft &&
                            geometry.BodyContentX <= visibleRight)
                        {
                            _visibleRenderColumns.Add(column);
                        }
                    }

                    renderColumns = _visibleRenderColumns;
                }

                WorkGridGeometrySnapshot rowGeometry = layout.GeometrySnapshot;
                if (rowGeometry != null && rowGeometry.Rows.Count != rowDescriptors.Count)
                {
                    rowGeometry = null;
                }

                WorkGridIndexRange visibleRows = new WorkGridIndexRange(0, rowDescriptors.Count);
                if (rowGeometry != null)
                {
                    visibleRows = rowGeometry.GetVisibleRowRange(
                        viewport.OutRect,
                        table.scrollPosition.y,
                        RowCullBuffer);
                }

                int visibleStart = Math.Max(
                    0,
                    Math.Min(rowDescriptors.Count, visibleRows.Start));
                long requestedEnd = (long)visibleRows.Start + visibleRows.Count;
                int visibleEnd = requestedEnd <= visibleStart
                    ? visibleStart
                    : requestedEnd >= rowDescriptors.Count
                        ? rowDescriptors.Count
                        : (int)requestedEnd;
                visibleRows = new WorkGridIndexRange(
                    visibleStart,
                    visibleEnd - visibleStart);

                // Calculate dimensions once for all highlight operations.
                float totalWidth = CalculateTotalColumnWidth(columns);
                float totalHeight = layout.ContentHeight;

                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawAllHighlights", () => DrawAllHighlights(
                        rowDescriptors,
                        columns,
                        totalWidth,
                        totalHeight,
                        rowGeometry,
                        visibleRows));
                    SpineTiming.Time("WorkTab.Rows.DrawAllRowContent", () => DrawAllRowContent(table,
                        rowDescriptors,
                        renderColumns,
                        viewport.ViewRect.width,
                        nameColumn,
                        snapshotLayer,
                        rowGeometry,
                        visibleRows));
                    SpineTiming.Time("WorkTab.Rows.DrawRowSeparators", () => DrawRowSeparators(
                        rowDescriptors,
                        viewport.ViewRect.width,
                        rowGeometry,
                        visibleRows));
                    SpineTiming.Time("WorkTab.Rows.DrawWorkloadMembership", () => DrawWorkloadMembershipIndicators(
                        rowDescriptors,
                        totalWidth,
                        rowGeometry,
                        visibleRows));
                    SpineTiming.Time("WorkTab.Rows.DrawWorkloadInspection", () => DrawWorkloadInspectionHighlights(
                        rowDescriptors,
                        columns,
                        totalWidth,
                        rowGeometry,
                        visibleRows,
                        layout.LayoutRevision,
                        table.scrollPosition.x,
                        viewport.OutRect.width));
                }
                else
                {
                    // Phase 1: Draw all highlights (selected, hovered, float menu, similar worktypes).
                    DrawAllHighlights(
                        rowDescriptors,
                        columns,
                        totalWidth,
                        totalHeight,
                        rowGeometry,
                        visibleRows);

                    // Phase 2: Draw actual row content (pawn data, divider labels, backgrounds).
                    DrawAllRowContent(table,
                        rowDescriptors,
                        renderColumns,
                        viewport.ViewRect.width,
                        nameColumn,
                        snapshotLayer,
                        rowGeometry,
                        visibleRows);

                    // Phase 3: Draw separator lines between rows.
                    DrawRowSeparators(
                        rowDescriptors,
                        viewport.ViewRect.width,
                        rowGeometry,
                        visibleRows);

                    DrawWorkloadMembershipIndicators(
                        rowDescriptors,
                        totalWidth,
                        rowGeometry,
                        visibleRows);

                    // Inspection is a contextual overlay, not one of the
                    // optional normal highlight features. Paint it after row
                    // content so changed cells/rows remain visible even when
                    // ordinary highlights are disabled or content draws over
                    // the earlier highlight pass.
                    DrawWorkloadInspectionHighlights(
                        rowDescriptors,
                        columns,
                        totalWidth,
                        rowGeometry,
                        visibleRows,
                        layout.LayoutRevision,
                        table.scrollPosition.x,
                        viewport.OutRect.width);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawWorkloadMembershipIndicators(
            List<RowDescriptor> rowDescriptors,
            float totalWidth,
            WorkGridGeometrySnapshot rowGeometry,
            WorkGridIndexRange visibleRows)
        {
            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview == null || !preview.IsActive)
            {
                return;
            }

            WorkloadMembershipSnapshot snapshot;
            try
            {
                snapshot = preview.GetMembershipSnapshot();
                if (snapshot == null)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                // Membership presentation is an optional preview affordance.
                // It must not quarantine the optimized renderer if a transient
                // pawn-scope lookup fails during a frame.
                Log.ErrorOnce(
                    "[BWT] Workload membership row presentation failed; continuing without scope accents.\n" +
                    exception,
                    0x4257544D);
                return;
            }

            float currentY = 0f;
            for (int rowIndex = visibleRows.Start; rowIndex < visibleRows.EndExclusive; rowIndex++)
            {
                if (rowGeometry != null)
                {
                    currentY = rowGeometry.Rows[rowIndex].OffsetY;
                }

                RowDescriptor descriptor = rowDescriptors[rowIndex];
                if (descriptor?.Pawn != null)
                {
                    PawnKey pawnKey = WorkTabEffectiveStateIds.ForPawn(descriptor.Pawn);
                    if (pawnKey.IsValid && snapshot.TryGetClassification(
                            pawnKey,
                            out WorkloadMembershipClassification classification))
                    {
                        DrawWorkloadMembershipIndicator(
                            new Rect(0f, currentY, totalWidth, descriptor.Height),
                            classification);
                    }
                }

                if (rowGeometry == null)
                {
                    currentY += descriptor?.Height ?? 0f;
                }
            }
        }

        private static void DrawWorkloadMembershipIndicator(
            Rect rowRect,
            WorkloadMembershipClassification classification)
        {
            Color accent;
            Color fill = Color.clear;
            string tooltip;
            switch (classification)
            {
                case WorkloadMembershipClassification.Included:
                    accent = new Color(0.30f, 0.74f, 0.66f, 0.88f);
                    tooltip = "Workload preview: represented and included.";
                    break;
                case WorkloadMembershipClassification.UnrepresentedNew:
                    accent = new Color(0.95f, 0.72f, 0.28f, 0.92f);
                    fill = new Color(0.95f, 0.72f, 0.28f, 0.055f);
                    tooltip = "Workload preview: new or unrepresented pawn. " +
                              "Right-click the pawn row to include it in this application.";
                    break;
                case WorkloadMembershipClassification.UnchangedOutsideScope:
                    accent = new Color(0.55f, 0.58f, 0.60f, 0.78f);
                    fill = new Color(0f, 0f, 0f, 0.09f);
                    tooltip = "Workload preview: outside this workload's scope; unchanged by Apply.";
                    break;
                case WorkloadMembershipClassification.ExplicitlyExcluded:
                    accent = new Color(0.82f, 0.36f, 0.36f, 0.92f);
                    fill = new Color(0.70f, 0.16f, 0.16f, 0.07f);
                    tooltip = "Workload preview: excluded for this application; live values remain unchanged.";
                    break;
                case WorkloadMembershipClassification.StaleMissing:
                    accent = new Color(0.68f, 0.38f, 0.38f, 0.82f);
                    fill = new Color(0f, 0f, 0f, 0.08f);
                    tooltip = "Workload preview: represented by the workload but not currently available.";
                    break;
                default:
                    return;
            }

            if (fill.a > 0f)
            {
                Widgets.DrawBoxSolid(rowRect, fill);
            }

            Rect markerRect = new Rect(
                rowRect.xMin,
                rowRect.yMin,
                Mathf.Min(4f, rowRect.width),
                rowRect.height);
            if (markerRect.width > 0f && markerRect.height > 0f)
            {
                Widgets.DrawBoxSolid(markerRect, accent);
                TooltipHandler.TipRegion(markerRect, tooltip);
            }
        }

        internal bool TryGetRowAt(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out WorkTabLayoutRow row)
        {
            row = default;
            return layout?.GeometrySnapshot != null && layout.TryGetRowAt(mousePosition, out row);
        }

        internal bool TryGetBodyColumnAt(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out WorkTabLayoutColumn column)
        {
            column = default;
            return layout?.GeometrySnapshot != null && layout.TryGetBodyColumnAt(mousePosition, out column);
        }

        internal bool TryGetPriorityBoxHit(
            IWorkTabLayoutController layout,
            WorkTabLayoutRow row,
            WorkTabLayoutColumn column,
            Vector2 mousePosition,
            out Rect priorityBoxRect)
        {
            priorityBoxRect = default;
            if (layout == null ||
                row.Pawn == null ||
                !(column.Column?.Worker is PawnColumnWorker_WorkPriority))
            {
                return false;
            }

            Rect rowRect = layout.GetScreenRect(row);
            Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(column, rowRect);
            priorityBoxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            return priorityBoxRect.Contains(mousePosition);
        }

        /// <summary>
        /// Calculates the total width of all columns combined once per frame.
        /// </summary>
        private static float CalculateTotalColumnWidth(IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            float totalWidth = 0f;
            for (int i = 0; i < columns.Count; i++)
            {
                totalWidth += columns[i].Width;
            }

            return totalWidth;
        }

        /// <summary>
        /// Phase 1: Draws all highlighting overlays.
        /// This includes selected and hovered rows, float-menu worktype columns,
        /// hovered columns, and similar-worktype columns.
        /// </summary>
        private static void DrawAllHighlights(
            List<RowDescriptor> rowDescriptors,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float totalWidth,
            float totalHeight,
            WorkGridGeometrySnapshot rowGeometry,
            WorkGridIndexRange visibleRows)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FeaturesHighlights,
                    settings?.ShowPawnAndWorktypeHighlights ?? DefaultSettings.ShowPawnAndWorktypeHighlights))
            {
                return;
            }

            WorkTabLayoutColumn? hoveredColumn = null;
            WorkTypeDef hoveredWorkType = null;
            bool timePriorityOwnsMouse = TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                                         BWTWorkTabTutorial.OwnsCurrentPointer;

            // 1. Detect hovered column.
            if (BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.HighlightsHover,
                    settings?.ShowCursorPawnAndWorktypeHighlight ??
                        DefaultSettings.ShowCursorPawnAndWorktypeHighlight) &&
                !timePriorityOwnsMouse)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    WorkGridAnimatedColumnGeometry geometry =
                        WorkGridInteractionGeometry.GetAnimatedColumn(col);
                    var columnRect = new Rect(
                        geometry.BodyContentX,
                        0f,
                        geometry.Width,
                        totalHeight);

                    if (Mouse.IsOver(columnRect))
                    {
                        hoveredColumn = col;
                        hoveredWorkType = col.Column?.workType;
                        break;
                    }
                }
            }

            if (hoveredWorkType == null && !timePriorityOwnsMouse)
            {
                hoveredWorkType = PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType;
            }

            MouseStateManager.UpdateHoverState(hoveredColumn);

            var cachedSimilarWorktypes = hoveredWorkType != null
                ? WorkColumnOrderManager.GetSimilarWorktypes(hoveredWorkType)
                : null;

            Pawn highlightedPawn = HighlightState.GetHighlightedPawn();
            WorkTypeDef highlightedWorkType = HighlightState.GetHighlightedWorkType();
            WorkGiverDef highlightedWorkGiver = HighlightState.GetHighlightedWorkGiver();
            bool floatMenuOpen = Find.WindowStack?.IsOpen<FloatMenu>() == true;

            // 2. Draw horizontal highlights (rows).
            float currentY = 0f;
            for (int i = visibleRows.Start; i < visibleRows.EndExclusive; i++)
            {
                if (rowGeometry != null)
                {
                    currentY = rowGeometry.Rows[i].OffsetY;
                }

                var descriptor = rowDescriptors[i];
                Rect rowRect = new Rect(0f, currentY, totalWidth, descriptor.Height);

                bool isFloatMenuPawn = descriptor.IsPawn &&
                    highlightedPawn != null &&
                    descriptor.Pawn == highlightedPawn &&
                    BWTWorkTabEffectiveSettings.GetBool(
                        SettingIDs.HighlightsFloatMenu,
                        settings?.ShowFloatMenuPawnAndWorktypeHighlight ??
                            DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight);

                if (isFloatMenuPawn)
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetFloatMenuColor());
                }

                if (BWTWorkTabEffectiveSettings.GetBool(
                        SettingIDs.HighlightsHover,
                        settings?.ShowCursorPawnAndWorktypeHighlight ??
                            DefaultSettings.ShowCursorPawnAndWorktypeHighlight) &&
                    !timePriorityOwnsMouse &&
                    !floatMenuOpen &&
                    Mouse.IsOver(rowRect))
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetRowHoverColor());
                }
                else if (!isFloatMenuPawn &&
                         descriptor.IsPawn &&
                         Find.Selector.IsSelected(descriptor.Pawn) &&
                         BWTWorkTabEffectiveSettings.GetBool(
                             SettingIDs.HighlightsSelected,
                             settings?.DoSelectedPawnHighlight ??
                                 DefaultSettings.DoSelectedPawnHighlight))
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetSelectedPawnColor());
                }

                if (rowGeometry == null)
                {
                    currentY += descriptor.Height;
                }
            }

            // 3. Draw divider highlights if active.
            if (BWTWorkTabEffectiveSettings.GetBool(
                    "dividers.highlight",
                    settings?.highlightDividersOnHover ?? DefaultSettings.highlightDividersOnHover) &&
                BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FeaturesDividers,
                    settings?.enableDividers ?? DefaultSettings.enableDividers))
            {
                currentY = 0f;
                for (int i = visibleRows.Start; i < visibleRows.EndExclusive; i++)
                {
                    if (rowGeometry != null)
                    {
                        currentY = rowGeometry.Rows[i].OffsetY;
                    }

                    var descriptor = rowDescriptors[i];
                    Rect rowRect = new Rect(0f, currentY, totalWidth, descriptor.Height);

                    if (descriptor.IsDivider &&
                        !timePriorityOwnsMouse &&
                        !floatMenuOpen &&
                        Mouse.IsOver(rowRect))
                    {
                        HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetRowHoverColor());
                    }

                    if (rowGeometry == null)
                    {
                        currentY += descriptor.Height;
                    }
                }
            }

            // 4. Draw vertical highlights (columns).
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                WorkGridAnimatedColumnGeometry geometry =
                    WorkGridInteractionGeometry.GetAnimatedColumn(column);
                Rect columnRect = new Rect(
                    geometry.BodyContentX,
                    0f,
                    geometry.Width,
                    totalHeight);
                bool isWorkColumn = WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column);
                bool isFloatMenuColumn = isWorkColumn &&
                    IsColumnHighlightedByFloatMenu(column, highlightedWorkType, highlightedWorkGiver);
                bool isTimePrioritySourceColumn = isWorkColumn &&
                    TimePriorityScheduleEditor.ShouldHighlightSourceColumn(column);

                if (isFloatMenuColumn && BWTWorkTabEffectiveSettings.GetBool(
                        SettingIDs.HighlightsFloatMenu,
                        settings?.ShowFloatMenuPawnAndWorktypeHighlight ??
                            DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight))
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetFloatMenuColor());
                }

                if (isTimePrioritySourceColumn)
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetColumnHoverColor());
                }
                else if (isWorkColumn &&
                         BWTWorkTabEffectiveSettings.GetBool(
                             SettingIDs.HighlightsHover,
                             settings?.ShowCursorPawnAndWorktypeHighlight ??
                                 DefaultSettings.ShowCursorPawnAndWorktypeHighlight) &&
                         !timePriorityOwnsMouse &&
                         !floatMenuOpen &&
                         hoveredWorkType != null &&
                         hoveredWorkType == column.Column.workType)
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetColumnHoverColor());
                }
                else if (!isFloatMenuColumn &&
                         isWorkColumn &&
                         BWTWorkTabEffectiveSettings.GetBool(
                             SettingIDs.HighlightsSimilar,
                             settings?.ShowSimilarWorktypeHighlight ??
                                 DefaultSettings.ShowSimilarWorktypeHighlight) &&
                         cachedSimilarWorktypes != null &&
                         cachedSimilarWorktypes.Contains(column.Column.workType))
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetSimilarWorktypeColor());
                }

            }

        }

        private void DrawWorkloadInspectionHighlights(
            List<RowDescriptor> rowDescriptors,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float totalWidth,
            WorkGridGeometrySnapshot rowGeometry,
            WorkGridIndexRange visibleRows,
            int layoutRevision,
            float horizontalScrollX,
            float horizontalViewportWidth)
        {
            if (!WorkloadPreviewController.IsInspectionActiveForCurrentTab)
            {
                return;
            }

            WorkloadPreviewController preview = WorkloadPreviewController.Current;
            if (preview == null)
            {
                return;
            }

            if (!preview.HasInspectionCellTargets)
            {
                if (preview.HasInspectionRowLevelChanges)
                {
                    DrawWorkloadInspectionRows(
                        rowDescriptors,
                        totalWidth,
                        rowGeometry,
                        visibleRows,
                        preview);
                }

                // Membership-only and presentation-only changes are rendered
                // by their own paths (or have no grid visual). Do not walk the
                // body columns for them.
                return;
            }

            EnsureInspectionColumnBindings(columns, layoutRevision);
            if (_inspectionColumnBindings.Count == 0)
            {
                return;
            }

            IReadOnlyList<WorkloadInspectionTarget> targets = preview.InspectionTargets;
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            _inspectionVisibleRows.Clear();
            _inspectionVisiblePawnRows.Clear();
            _inspectionVisibleRowOffsets.Clear();
            _inspectionGlobalColumns.Clear();
            _inspectionCellMasks.Clear();
            float currentY = rowGeometry != null && visibleRows.Start < rowGeometry.Rows.Count
                ? rowGeometry.Rows[visibleRows.Start].OffsetY
                : 0f;
            for (int rowIndex = visibleRows.Start; rowIndex < visibleRows.EndExclusive; rowIndex++)
            {
                if (rowGeometry != null)
                {
                    currentY = rowGeometry.Rows[rowIndex].OffsetY;
                }

                _inspectionVisibleRowOffsets[rowIndex] = currentY;
                Pawn pawn = rowDescriptors[rowIndex]?.Pawn;
                if (pawn != null && pawn.thingIDNumber > 0)
                {
                    _inspectionVisibleRows[pawn.thingIDNumber] = rowIndex;
                    _inspectionVisiblePawnRows.Add(rowIndex);
                }
                if (rowGeometry == null)
                {
                    currentY += rowDescriptors[rowIndex]?.Height ?? 0f;
                }
            }

            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                WorkloadInspectionTarget target = targets[targetIndex];
                IReadOnlyList<int> matchingColumns = GetInspectionColumns(target);
                WorkloadInspectionCellKind kind = GetInspectionCellKind(target.Kind);
                if (matchingColumns.Count == 0 || kind == WorkloadInspectionCellKind.None)
                {
                    continue;
                }

                if (target.Scope == WorkloadTargetScope.GlobalShared)
                {
                    for (int columnIndex = 0; columnIndex < matchingColumns.Count; columnIndex++)
                    {
                        int index = matchingColumns[columnIndex];
                        if (index < 0 || index >= columns.Count ||
                            !IsInspectionColumnVisible(
                                columns[index],
                                horizontalScrollX,
                                horizontalViewportWidth))
                        {
                            continue;
                        }

                        WorkloadInspectionCellKind existing;
                        _inspectionGlobalColumns.TryGetValue(index, out existing);
                        _inspectionGlobalColumns[index] = existing | kind;
                    }
                }
                else if (_inspectionVisibleRows.TryGetValue(target.PawnId, out int rowIndex))
                {
                    AddInspectionColumnsForRow(
                        rowIndex,
                        matchingColumns,
                        kind,
                        columns,
                        horizontalScrollX,
                        horizontalViewportWidth);
                }
            }

            foreach (KeyValuePair<int, WorkloadInspectionCellKind> globalColumn in _inspectionGlobalColumns)
            {
                for (int rowIndex = 0; rowIndex < _inspectionVisiblePawnRows.Count; rowIndex++)
                {
                    _inspectionCellMasks.Add(
                        _inspectionVisiblePawnRows[rowIndex],
                        globalColumn.Key,
                        globalColumn.Value);
                }
            }

            foreach (KeyValuePair<WorkloadInspectionCellKey, WorkloadInspectionCellKind> cell in
                     _inspectionCellMasks.Entries)
            {
                int rowIndex = cell.Key.RowIndex;
                int columnIndex = cell.Key.ColumnIndex;
                if (!_inspectionVisibleRowOffsets.TryGetValue(rowIndex, out float rowY) ||
                    rowIndex < visibleRows.Start || rowIndex >= visibleRows.EndExclusive ||
                    rowIndex >= rowDescriptors.Count || columnIndex < 0 ||
                    columnIndex >= columns.Count)
                {
                    continue;
                }

                RowDescriptor descriptor = rowDescriptors[rowIndex];
                if (descriptor?.Pawn == null)
                {
                    continue;
                }

                Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyContentRect(
                    columns[columnIndex],
                    new Rect(0f, rowY, totalWidth, descriptor.Height));
                DrawInspectionCell(
                    cellRect,
                    _inspectionColumnBindings[columnIndex].IsExpandBesideChild,
                    cell.Value);
            }
        }

        private void AddInspectionColumnsForRow(
            int rowIndex,
            IReadOnlyList<int> matchingColumns,
            WorkloadInspectionCellKind kind,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float horizontalScrollX,
            float horizontalViewportWidth)
        {
            for (int columnIndex = 0; columnIndex < matchingColumns.Count; columnIndex++)
            {
                AddInspectionCell(
                    rowIndex,
                    matchingColumns[columnIndex],
                    kind,
                    columns,
                    horizontalScrollX,
                    horizontalViewportWidth);
            }
        }

        private void AddInspectionCell(
            int rowIndex,
            int columnIndex,
            WorkloadInspectionCellKind kind,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float horizontalScrollX,
            float horizontalViewportWidth)
        {
            if (columnIndex < 0 || columnIndex >= _inspectionColumnBindings.Count ||
                columnIndex >= columns.Count ||
                !IsInspectionColumnVisible(
                    columns[columnIndex],
                    horizontalScrollX,
                    horizontalViewportWidth))
            {
                return;
            }

            _inspectionCellMasks.Add(rowIndex, columnIndex, kind);
        }

        private IReadOnlyList<int> GetInspectionColumns(WorkloadInspectionTarget target)
        {
            switch (target.Kind)
            {
                case WorkloadInspectionTargetKind.ParentPriority:
                    return _inspectionColumnIndex.GetParentColumns(target.WorkType);
                case WorkloadInspectionTargetKind.SpecificPriority:
                    return _inspectionColumnIndex.GetSpecificColumns(target.WorkType, target.WorkGiver);
                case WorkloadInspectionTargetKind.Schedule:
                    if (target.ScheduleKind == (int)WorkloadScheduleTargetKind.ParentWorkType)
                    {
                        return _inspectionColumnIndex.GetParentColumns(target.WorkType);
                    }
                    return _inspectionColumnIndex.GetSpecificColumns(target.WorkType, target.WorkGiver);
                case WorkloadInspectionTargetKind.Ordering:
                    return _inspectionColumnIndex.GetOrderingColumns(target.WorkType);
                default:
                    return new int[0];
            }
        }

        private static WorkloadInspectionCellKind GetInspectionCellKind(
            WorkloadInspectionTargetKind kind)
        {
            switch (kind)
            {
                case WorkloadInspectionTargetKind.ParentPriority:
                    return WorkloadInspectionCellKind.ParentPriority;
                case WorkloadInspectionTargetKind.SpecificPriority:
                    return WorkloadInspectionCellKind.SpecificPriority;
                case WorkloadInspectionTargetKind.Schedule:
                    return WorkloadInspectionCellKind.Schedule;
                case WorkloadInspectionTargetKind.Ordering:
                    return WorkloadInspectionCellKind.Ordering;
                default:
                    return WorkloadInspectionCellKind.None;
            }
        }

        private static bool IsInspectionColumnVisible(
            WorkTabLayoutColumn column,
            float horizontalScrollX,
            float horizontalViewportWidth)
        {
            if (horizontalViewportWidth <= 0f)
            {
                return true;
            }

            WorkGridAnimatedColumnGeometry geometry =
                WorkGridInteractionGeometry.GetAnimatedColumn(column);
            float visibleLeft = horizontalScrollX - HorizontalCullBuffer;
            float visibleRight = horizontalScrollX + horizontalViewportWidth + HorizontalCullBuffer;
            return geometry.BodyContentX + geometry.Width >= visibleLeft &&
                geometry.BodyContentX <= visibleRight;
        }

        private static void DrawWorkloadInspectionRows(
            List<RowDescriptor> rowDescriptors,
            float totalWidth,
            WorkGridGeometrySnapshot rowGeometry,
            WorkGridIndexRange visibleRows,
            WorkloadPreviewController preview)
        {
            float currentY = 0f;
            for (int rowIndex = visibleRows.Start; rowIndex < visibleRows.EndExclusive; rowIndex++)
            {
                if (rowGeometry != null)
                {
                    currentY = rowGeometry.Rows[rowIndex].OffsetY;
                }

                RowDescriptor descriptor = rowDescriptors[rowIndex];
                if (descriptor?.Pawn != null &&
                    preview.IsInspectionRowLevelChanged(descriptor.Pawn))
                {
                    HighlightDrawer.DrawHighlight(
                        new Rect(0f, currentY, totalWidth, descriptor.Height),
                        new Color(0.34f, 0.65f, 0.62f, 0.18f));
                }

                if (rowGeometry == null)
                {
                    currentY += descriptor?.Height ?? 0f;
                }
            }
        }

        private void EnsureInspectionColumnBindings(
            IReadOnlyList<WorkTabLayoutColumn> columns,
            int layoutRevision)
        {
            int subWorkRevision = SubWorkDrilldownState.LayoutSignature;
            if (ReferenceEquals(_inspectionBindingColumns, columns) &&
                _inspectionBindingLayoutRevision == layoutRevision &&
                _inspectionBindingSubWorkRevision == subWorkRevision)
            {
                return;
            }

            _inspectionColumnBindings.Clear();
            _inspectionColumnIndex.Clear();
            if (columns == null)
            {
                _inspectionBindingColumns = columns;
                _inspectionBindingLayoutRevision = layoutRevision;
                _inspectionBindingSubWorkRevision = subWorkRevision;
                return;
            }

            for (int i = 0; i < columns.Count; i++)
            {
                WorkTabLayoutColumn column = columns[i];
                WorkTypeDef workType = column.SubWorkParent ?? column.Column?.workType;
                WorkGiverDef workGiver = column.SubWorkGiver;
                if (workType == null || workGiver == null)
                {
                    WorkGiver resolvedWorkGiver;
                    WorkTypeDef resolvedParent;
                    if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                            column,
                            out resolvedWorkGiver,
                            out resolvedParent,
                            out _))
                    {
                        workGiver = resolvedWorkGiver?.def;
                        workType = resolvedParent ?? workType;
                    }
                }

                bool isPriorityCell = column.Column?.Worker is PawnColumnWorker_WorkPriority;
                _inspectionColumnBindings.Add(new InspectionColumnBinding(
                    workType,
                    workGiver,
                    column.IsExpandBesideChild,
                    isPriorityCell));
                _inspectionColumnIndex.Add(
                    i,
                    workType?.defName,
                    workGiver?.defName,
                    isPriorityCell);
            }

            _inspectionBindingColumns = columns;
            _inspectionBindingLayoutRevision = layoutRevision;
            _inspectionBindingSubWorkRevision = subWorkRevision;
        }

        private static void DrawInspectionCell(
            Rect cellRect,
            bool isExpandBesideChild,
            WorkloadInspectionCellKind kind)
        {
            Rect priorityRect = WorkPriorityCellGeometry.GetDrawnPriorityBoxRect(
                cellRect,
                isExpandBesideChild);
            if ((kind & WorkloadInspectionCellKind.ParentPriority) != 0)
            {
                HighlightDrawer.DrawHighlight(
                    priorityRect,
                    new Color(0.30f, 0.75f, 0.68f, 0.32f));
            }
            if ((kind & WorkloadInspectionCellKind.SpecificPriority) != 0)
            {
                Rect specificRect = priorityRect.ContractedBy(2f);
                if (specificRect.width > 0f && specificRect.height > 0f)
                {
                    HighlightDrawer.DrawHighlight(
                        specificRect,
                        new Color(0.45f, 0.58f, 0.92f, 0.34f));
                }
            }
            if ((kind & WorkloadInspectionCellKind.Schedule) != 0)
            {
                DrawInspectionStripe(
                    cellRect,
                    new Color(0.95f, 0.70f, 0.25f, 0.88f),
                    right: false);
            }
            if ((kind & WorkloadInspectionCellKind.Ordering) != 0)
            {
                DrawInspectionStripe(
                    cellRect,
                    new Color(0.72f, 0.46f, 0.90f, 0.88f),
                    right: true);
            }
        }

        private static void DrawInspectionStripe(Rect cellRect, Color color, bool right)
        {
            Rect stripe = new Rect(
                right ? cellRect.xMax - 4f : cellRect.xMin + 1f,
                cellRect.yMin + 1f,
                Mathf.Min(3f, Mathf.Max(0f, cellRect.width - 2f)),
                Mathf.Max(0f, cellRect.height - 2f));
            if (stripe.width <= 0f || stripe.height <= 0f)
            {
                return;
            }

            Widgets.DrawBoxSolid(stripe, color);
        }

        private static bool IsColumnHighlightedByFloatMenu(
            WorkTabLayoutColumn column,
            WorkTypeDef highlightedWorkType,
            WorkGiverDef highlightedWorkGiver)
        {
            if (highlightedWorkType == null || !WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column))
            {
                return false;
            }

            if (highlightedWorkGiver != null)
            {
                return SubWorkDrilldownState.TryGetWorkGiverForColumn(
                           column,
                           out var workGiver,
                           out var parentWorkType,
                           out _) &&
                       parentWorkType == highlightedWorkType &&
                       workGiver?.def == highlightedWorkGiver;
            }

            if (column.Column?.workType == null)
            {
                return false;
            }

            return column.Column.workType == highlightedWorkType;
        }

        /// <summary>
        /// Phase 2: Draws the actual content of each row.
        /// </summary>
        private static void DrawAllRowContent(
            PawnTable table,
            List<RowDescriptor> rowDescriptors,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float viewWidth,
            WorkTabLayoutColumn? nameColumn,
            IWorkGridSnapshotLayer snapshotLayer,
            WorkGridGeometrySnapshot rowGeometry,
            WorkGridIndexRange visibleRows)
        {
            float currentY = 0f;
            for (int i = visibleRows.Start; i < visibleRows.EndExclusive; i++)
            {
                if (rowGeometry != null)
                {
                    currentY = rowGeometry.Rows[i].OffsetY;
                }

                var descriptor = rowDescriptors[i];
                Rect rowRect = new Rect(0f, currentY, viewWidth, descriptor.Height);
                DrawSingleRowContent(table, descriptor, columns, rowRect, nameColumn, i, snapshotLayer);
                if (rowGeometry == null)
                {
                    currentY += descriptor.Height;
                }
            }
        }

        private static void DrawSingleRowContent(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            WorkTabLayoutColumn? nameColumn,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            if (descriptor.IsPawn)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawPawnRowContent", () => DrawPawnRowContent(table, descriptor, columns, rowRect, rowIndex, snapshotLayer));
                }
                else
                {
                    DrawPawnRowContent(table, descriptor, columns, rowRect, rowIndex, snapshotLayer);
                }
            }
            else if (descriptor.IsDivider)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawDividerRowContent", () => DrawDividerRowContent(descriptor, rowRect, nameColumn, rowIndex, snapshotLayer));
                }
                else
                {
                    DrawDividerRowContent(descriptor, rowRect, nameColumn, rowIndex, snapshotLayer);
                }
            }
        }

        private static void DrawPawnRowContent(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            if (rowRect.height <= 0.5f)
            {
                return;
            }

            if (rowRect.height < MinimumPawnRenderHeight - 0.5f)
            {
                GUI.BeginGroup(rowRect);
                try
                {
                    Rect clippedRowRect = new Rect(0f, 0f, rowRect.width, MinimumPawnRenderHeight);
                    DrawPawnRowContentUnclipped(table, descriptor, columns, clippedRowRect, rowIndex, snapshotLayer);
                }
                finally
                {
                    GUI.EndGroup();
                }

                return;
            }

            DrawPawnRowContentUnclipped(table, descriptor, columns, rowRect, rowIndex, snapshotLayer);
        }

        private static void DrawPawnRowContentUnclipped(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            Color snapshotTextColor;
            if (snapshotLayer == null ||
                !snapshotLayer.TryDrawRowBackground(rowIndex, rowRect, out snapshotTextColor))
            {
                DrawRowBackground(descriptor.Pawn, null, rowRect);
            }
            else
            {
                CurrentRowTextColor = snapshotTextColor;
            }

            DrawPawnRow(table, descriptor.Pawn, rowRect, columns, rowIndex, snapshotLayer);
            DrawPawnRowOverlay(descriptor.Pawn, rowRect);
        }

        private static void DrawDividerRowContent(
            RowDescriptor descriptor,
            Rect rowRect,
            WorkTabLayoutColumn? nameColumn,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            Color ignoredTextColor;
            if (snapshotLayer == null ||
                !snapshotLayer.TryDrawRowBackground(rowIndex, rowRect, out ignoredTextColor))
            {
                DrawRowBackground(null, descriptor.Divider, rowRect);
            }

            if (nameColumn.HasValue)
            {
                DrawDividerRow(descriptor.Divider, rowRect, nameColumn.Value);
            }
        }

        /// <summary>
        /// Phase 3: Draws thin separator lines between rows.
        /// </summary>
        private static void DrawRowSeparators(
            List<RowDescriptor> rowDescriptors,
            float viewWidth,
            WorkGridGeometrySnapshot rowGeometry,
            WorkGridIndexRange visibleRows)
        {
            float currentY = 0f;

            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            for (int i = visibleRows.Start; i < visibleRows.EndExclusive; i++)
            {
                if (i >= rowDescriptors.Count - 1)
                {
                    continue;
                }

                if (rowGeometry != null)
                {
                    currentY = rowGeometry.Rows[i].OffsetY;
                }

                var descriptor = rowDescriptors[i];
                currentY += descriptor.Height;
                Widgets.DrawLineHorizontal(0f, currentY - 1f, viewWidth);
            }

            GUI.color = Color.white;
        }

        private static void DrawRowBackground(Pawn pawn, PawnDivider divider, Rect rect)
        {
            if (pawn != null &&
                PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out var pawnColor) &&
                pawnColor.a > 0f)
            {
                var overlay = new Color(
                    pawnColor.r,
                    pawnColor.g,
                    pawnColor.b,
                    Mathf.Clamp(pawnColor.a, 0.08f, 0.6f));
                Widgets.DrawBoxSolid(rect, overlay);

                CurrentRowTextColor = Spine.UI.TextColorHelper.GetContrastingTextColor(overlay);
            }
            else if (divider != null)
            {
                var settings = BetterWorkTabMod.Settings;
                var dividerColor = divider.DividerColor;
                if (!BWTWorkTabEffectiveSettings.GetBool(
                        SettingIDs.DividersCustomColors,
                        settings?.allowCustomDividerColors ?? true))
                {
                    dividerColor = Color.gray;
                }

                float minAlpha = settings?.dividerMinAlpha ?? 0.35f;
                dividerColor.a = Mathf.Max(dividerColor.a, minAlpha);
                if (divider.IsCollapsed)
                {
                    dividerColor.a = Mathf.Clamp01(dividerColor.a * 0.6f);
                }

                Widgets.DrawBoxSolid(rect, dividerColor);
            }
        }

        private static WorkTabLayoutColumn? FindNameColumn(IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Column?.Worker is PawnColumnWorker_Label)
                {
                    return columns[i];
                }
            }

            return null;
        }

        private static void DrawDividerRow(
            PawnDivider divider,
            Rect rowRect,
            WorkTabLayoutColumn nameColumn)
        {
            Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyContentRect(
                nameColumn,
                rowRect);
            DrawDividerToggle(divider, cellRect);
            DrawDividerLabel(divider, cellRect);
        }

        private static void DrawDividerToggle(PawnDivider divider, Rect labelCellRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.DividersCollapse,
                    settings?.allowDividerCollapse ?? DefaultSettings.allowDividerCollapse) ||
                TimePriorityScheduleEditor.IsTransientDivider(divider))
            {
                return;
            }

            Rect arrowRect = new Rect(
                labelCellRect.xMin + 6f,
                labelCellRect.y + (labelCellRect.height - 16f) / 2f,
                18f,
                16f);
            string arrowChar = divider.IsCollapsed ? "▶" : "▼";
            if (Widgets.ButtonInvisible(arrowRect))
            {
                ToggleDividerCollapsed(divider);

                if (MultiplayerBridge.Active)
                {
                    LayoutSharingManager.NotifyLayoutChanged();
                }
            }

            var originalAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(arrowRect, arrowChar);
            Text.Anchor = originalAnchor;
            TooltipHandler.TipRegion(arrowRect, divider.IsCollapsed ? "Expand section" : "Collapse section");
        }

        private static void ToggleDividerCollapsed(PawnDivider divider)
        {
            if (divider == null)
            {
                return;
            }

            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FeaturesDividers,
                    settings?.enableDividers ?? DefaultSettings.enableDividers) ||
                !BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.DividersCollapse,
                    settings?.allowDividerCollapse ?? DefaultSettings.allowDividerCollapse))
            {
                return;
            }

            divider.IsCollapsed = !divider.IsCollapsed;
            DividerCollapseAnimationState.Start(divider, divider.IsCollapsed);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        private static void DrawPawnRow(
            PawnTable table,
            Pawn pawn,
            Rect rowRect,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            bool scheduleOpen = FluffyTimeScheduleAssigner.IsOpen;
            WorkTypeDef expandedParentPriorityWorkType = null;
            int expandedParentPriority = WorkPrioritySystem.DisabledPriority;
            snapshotLayer?.BeginRow();
            try
            {
                for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                {
                    WorkTabLayoutColumn column = columns[columnIndex];
                    if (snapshotLayer != null && !snapshotLayer.ShouldVisitCell(rowIndex, columnIndex))
                    {
                        continue;
                    }

                    Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyContentRect(
                        column,
                        rowRect);

                    if (snapshotLayer != null && snapshotLayer.TryDrawCell(rowIndex, columnIndex, cellRect))
                    {
                        continue;
                    }

                    // BWT still publishes active sub-work geometry and owns its header labels,
                    // but mixed mode lets Sleek draw and edit its per-job value in this cell.
                    if (WorkTabEffectiveStateRuntime.IsPreviewActive &&
                        SleekWorkTabGateway.BetterWorkTabHostsSleek &&
                        SubWorkDrilldownState.TryGetWorkGiverForColumn(
                            column,
                            out _,
                            out _,
                            out _))
                    {
                        WorkTabEffectiveStateRuntime.ReportBlocked(
                            WorkTabEffectiveStateDimension.SpecificJobOverride,
                            "Sleek owns this mixed cell and has no preview editor bridge.");
                        continue;
                    }

                    // Expand-beside children are owned by BWT. Sending them through the
                    // vanilla WorkPriority worker only for Harmony to intercept and route
                    // them back here adds a prefix, global drawing scope, and virtual call
                    // per cell. Focus-view columns still use the worker because their
                    // parent/sub-work crossfade is implemented by that patch.
                    if (column.IsExpandBesideChild &&
                        SubWorkDrilldownState.TryGetWorkGiverForColumn(
                            column,
                            out WorkGiver expandedWorkGiver,
                            out WorkTypeDef expandedParentWorkType,
                            out _))
                    {
                        if (expandedParentPriorityWorkType != expandedParentWorkType)
                        {
                            expandedParentPriorityWorkType = expandedParentWorkType;
                            expandedParentPriority = WorkTabEffectiveStateRuntime.GetParentPriority(
                                pawn,
                                expandedParentWorkType,
                                WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(
                                    pawn,
                                    expandedParentWorkType));
                        }

                        WorkGiverPriorityBoxRenderer.DrawPriorityBox(
                            expandedWorkGiver,
                            expandedParentWorkType,
                            pawn,
                            WorkPriorityCellGeometry.GetFluffyStyleSubWorkPriorityBoxRect(cellRect),
                            knownParentPriority: expandedParentPriority);
                        continue;
                    }

                    if (!WorkTabEffectiveStateRuntime.IsPreviewActive &&
                        scheduleOpen &&
                        !FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column) &&
                        column.Column?.workType != null &&
                        FluffyTimeScheduleAssigner.TryDrawWorkTypeCell(cellRect, pawn, column.Column.workType))
                    {
                        continue;
                    }

                    column.Column.Worker.DoCell(cellRect, pawn, table);
                }
            }
            finally
            {
                snapshotLayer?.EndRow();
            }
        }

        private static void DrawPawnRowOverlay(Pawn pawn, Rect rowRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (pawn == null)
            {
                return;
            }

            if (RuleBuilderGateway.ShouldHighlightRuleBuilder2Pawn(pawn))
            {
                HighlightDrawer.DrawHighlight(rowRect, new Color(1f, 0.82f, 0.18f, 0.18f));
            }

            // The open 24-hour editor belongs to one pawn's row. Marking that row ties
            // the hours to a colonist using the Work tab's selected-row treatment.
            if (TimePriorityScheduleEditor.IsSchedulingPawn(pawn))
            {
                HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetSelectedPawnColor());
            }

            if (Find.Selector.IsSelected(pawn) && BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.HighlightsSelected,
                    settings?.DoSelectedPawnHighlight ??
                        DefaultSettings.DoSelectedPawnHighlight))
            {
                HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetSelectedPawnColor());
            }

            if (pawn.Downed)
            {
                GUI.color = new Color(1f, 0f, 0f, 0.5f);
                Widgets.DrawLineHorizontal(rowRect.xMin, rowRect.center.y, rowRect.width);
                GUI.color = Color.white;
            }
        }

        private static void DrawDividerLabel(PawnDivider divider, Rect labelCellRect)
        {
            if (!divider.ShowLabel)
            {
                return;
            }

            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.DividersLabels,
                    settings?.showDividerLabels ?? true) || labelCellRect.height < 14f)
            {
                return;
            }

            var originalAnchor = Text.Anchor;
            var originalFont = Text.Font;
            var originalColor = GUI.color;
            bool originalWordWrap = Text.WordWrap;

            try
            {
                GUI.color = Spine.UI.TextColorHelper.GetContrastingTextColor(divider.DividerColor);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = divider.LabelFont;

                bool hasCollapseToggle = BWTWorkTabEffectiveSettings.GetBool(
                        SettingIDs.DividersCollapse,
                        settings?.allowDividerCollapse ?? DefaultSettings.allowDividerCollapse) &&
                    !TimePriorityScheduleEditor.IsTransientDivider(divider);
                float indent = hasCollapseToggle ? 33f : 6f;
                labelCellRect.xMin += indent;

                if (labelCellRect.width > 4f)
                {
                    string label = divider.DividerName ?? "Divider";
                    Text.WordWrap = false;
                    Text.Font = SelectDividerLabelFont(label, labelCellRect, divider.LabelFont);
                    label = TrimDividerLabelToFit(label, labelCellRect);
                    Widgets.Label(labelCellRect, label);
                }
            }
            finally
            {
                Text.Anchor = originalAnchor;
                Text.Font = originalFont;
                GUI.color = originalColor;
                Text.WordWrap = originalWordWrap;
            }
        }

        private static GameFont SelectDividerLabelFont(string label, Rect rect, GameFont preferredFont)
        {
            GameFont originalFont = Text.Font;
            try
            {
                foreach (GameFont font in DividerLabelFontCandidates(preferredFont))
                {
                    Text.Font = font;
                    Vector2 size = Text.CalcSize(label);
                    if (size.x <= rect.width && size.y <= rect.height + 2f)
                    {
                        return font;
                    }
                }

                return GameFont.Tiny;
            }
            finally
            {
                Text.Font = originalFont;
            }
        }

        private static IEnumerable<GameFont> DividerLabelFontCandidates(GameFont preferredFont)
        {
            if (preferredFont == GameFont.Medium)
            {
                yield return GameFont.Medium;
                yield return GameFont.Small;
                yield return GameFont.Tiny;
                yield break;
            }

            if (preferredFont == GameFont.Small)
            {
                yield return GameFont.Small;
                yield return GameFont.Tiny;
                yield break;
            }

            yield return GameFont.Tiny;
        }

        private static string TrimDividerLabelToFit(string label, Rect rect)
        {
            if (string.IsNullOrEmpty(label) || Text.CalcSize(label).x <= rect.width)
            {
                return label;
            }

            const string suffix = "...";
            float suffixWidth = Text.CalcSize(suffix).x;
            if (suffixWidth >= rect.width)
            {
                return string.Empty;
            }

            int low = 0;
            int high = label.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = label.Substring(0, mid) + suffix;
                if (Text.CalcSize(candidate).x <= rect.width)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low <= 0 ? suffix : label.Substring(0, low) + suffix;
        }
    }
}
