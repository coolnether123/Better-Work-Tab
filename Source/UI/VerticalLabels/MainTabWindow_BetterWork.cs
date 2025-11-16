using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.Input;
using Better_Work_Tab.Selection;
using Better_Work_Tab.Sorting;
using Better_Work_Tab.Persistence;
using Better_Work_Tab.ColumnManagement;
using Better_Work_Tab.RowManagement;
using Better_Work_Tab.ContextMenu;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Custom Work tab window that owns all layout/rendering (pawns, dividers, columns).
    /// </summary>
    public class MainTabWindow_BetterWork : MainTabWindow_Work
    {

        // Feature managers (initialized in constructor or PreOpen)
        private Input.InputManager _inputManager;
        private Selection.PawnSelectionManager _selectionManager;
        private Sorting.ColumnSortManager _sortManager;
        private Persistence.ColumnStateManager _columnStateManager;
        private ColumnManagement.ColumnResizeHandler _resizeHandler;
        private ColumnManagement.ColumnVisibilityManager _visibilityManager;
        private RowManagement.RowNavigationHandler _navigationHandler;
        private RowManagement.RowClickHandler _rowClickHandler;
        private ContextMenu.RowContextMenuManager _rowContextMenu;
        private ContextMenu.HeaderContextMenuManager _headerContextMenu;

        private const float RightEdgeMargin = 10f;
        private const float InfoIconSize = 24f;
        private int _lastPawnCount = -1;
        private int _lastDividerCount = -1;
        private float _maxHeight = -1f;
        private static bool _pendingWindowSnap;

        public override void PreOpen()
        {
            base.PreOpen();
            InitializeManagers();
            _pendingWindowSnap = true;
        }

        private void InitializeManagers()
        {
            _selectionManager = new Selection.PawnSelectionManager();
            _sortManager = new Sorting.ColumnSortManager();
            _columnStateManager = new Persistence.ColumnStateManager();
            _visibilityManager = new ColumnManagement.ColumnVisibilityManager(_columnStateManager);
            _resizeHandler = new ColumnManagement.ColumnResizeHandler(_columnStateManager);
            _navigationHandler = new RowManagement.RowNavigationHandler(_selectionManager);
            _rowClickHandler = new RowManagement.RowClickHandler(_selectionManager);
            _rowContextMenu = new ContextMenu.RowContextMenuManager();
            _headerContextMenu = new ContextMenu.HeaderContextMenuManager(_visibilityManager, _sortManager);

            // Get references to existing drag controllers
            if (PawnOrganizerSystem.Instance == null)
            {
                new PawnOrganizerSystem(_columnStateManager); // Instantiate the singleton
            }
            var organizer = PawnOrganizerSystem.Instance;

            _inputManager = new Input.InputManager(
                _selectionManager,
                _sortManager,
                _resizeHandler,
                _rowClickHandler,
                _navigationHandler,
                _visibilityManager,
                _rowContextMenu,
                _headerContextMenu,
                organizer?.RowDrag,      // Pass existing drag controller
                organizer?.ColumnDrag);  // Pass existing drag controller
        }


        public override void DoWindowContents(Rect inRect)
        {
            PawnTable pawnTable = GetPawnTable();
            if (pawnTable == null)
            {
                return;
            }

            Vector2 tableOrigin = new Vector2(inRect.x, inRect.y + ExtraTopSpace);

            var organizer = PawnOrganizerSystem.Instance;
            organizer?.UpdateState(pawnTable, tableOrigin);

            // Process existing drag system input FIRST
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)
            {
                organizer?.HandleInput(Event.current);
            }

            // Process new input manager input SECOND
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)
            {
                _inputManager?.ProcessInput(Event.current, organizer?.Layout);
            }

            // Let vanilla handle internal state updates
            if (Event.current.type != EventType.Repaint)
            {
                pawnTable.PawnTableOnGUI(tableOrigin);
            }

            // Window snapping logic
            if (organizer?.Layout != null)
            {
                int currentPawnCount = pawnTable.PawnsListForReading.Count;
                int currentDividerCount = organizer.CurrentWorklist?.Dividers?.Count ?? 0;

                if (_pendingWindowSnap || _lastPawnCount != currentPawnCount || _lastDividerCount != currentDividerCount)
                {
                    EnsureWindowRectMatchesContent(organizer.Layout);
                    _lastPawnCount = currentPawnCount;
                    _lastDividerCount = currentDividerCount;
                    _pendingWindowSnap = false;
                }
            }

            if (Event.current.type == EventType.Layout) return;

            // Draw UI components
            DrawManualPrioritiesCheckbox();
            DrawPriorityLegend(inRect);
            DrawWorkTable(pawnTable, organizer?.Layout, inRect);
            organizer?.DrawDragOverlays();

            var gearRect = GetInfoIconRect(inRect);
            DrawBottomRightButtons(inRect, gearRect);
            DrawInfoButton(gearRect);

            // Draw resize handles
            _resizeHandler?.DrawResizeHandles(organizer?.Layout, Event.current.mousePosition);
        }

        internal static void FlagWindowSnap()
        {
            _pendingWindowSnap = true;
        }

        private void DrawWorkTable(PawnTable table, IWorkTabLayoutController layout, Rect inRect)
        {
            if (layout == null || table == null)
            {
                return;
            }

            DrawHeaders(layout, table);
            DrawRows(table, layout, inRect);

            // Draw column separators
            // ColumnManagement.ColumnSeparatorRenderer.DrawSeparators(layout);
        }

        private void DrawHeaders(IWorkTabLayoutController layout, PawnTable table)
        {
            foreach (var column in layout.Columns)
            {
                // Don't draw custom hover - vanilla handles it
                // VisualFeedback.HoverEffectManager.DrawHeaderHover(column, Event.current.mousePosition);

                // Draw header (vanilla or custom)
                column.Column.Worker.DoHeader(column.HeaderRect, table);

                // Draw sort indicator
                UI.SortIndicatorRenderer.DrawSortIndicator(column, _sortManager.State);

                // Only enhance tooltip if sorting
                if (_sortManager.State.SortColumn == column.Column)
                {
                    VisualFeedback.TooltipManager.DrawHeaderTooltip(column, _sortManager.State);
                }
            }
        }

                private void DrawRows(PawnTable table, IWorkTabLayoutController layout, Rect inRect)
                {
                    if (layout?.Table == null) return;
        
                    float headerBottom = layout.TableOrigin.y + layout.HeaderHeight;
        
                    // Available height is the space left in the window
                    float maxAvailableHeight = inRect.height - headerBottom - this.ExtraBottomSpace;
                    if (maxAvailableHeight < 0f) maxAvailableHeight = 0f;
        
                    // Viewport is the visible scroll area
                    float viewportHeight;
                    if (windowRect.height < _maxHeight)
                    {
                        // If window is not at max height, viewport should be exactly the content height
                        viewportHeight = layout.ContentHeight;
                    }
                    else
                    {
                        // If window is at max height, viewport is clamped to available space
                        viewportHeight = maxAvailableHeight;
                    }
        
                    Rect outRect = new Rect(
                        layout.TableOrigin.x,
                        headerBottom,
                        layout.Table.Size.x,
                        viewportHeight);
        
                    // View rect is the total scrollable content area
                    float viewHeight = layout.ContentHeight;
                    Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);
        
                    var scroll = table.scrollPosition;
                    Widgets.BeginScrollView(outRect, ref scroll, viewRect);
                    table.scrollPosition = scroll;
        
            // Get visible rows only (culling optimization)
            var visibleRows = RowManagement.RowCullingManager.GetVisibleRows(
                layout, 
                outRect, 
                scroll);

            // Draw cells for visible rows
            foreach (var row in visibleRows)
            {
                if (row.IsDivider || row.Pawn == null) continue;

                foreach (var column in layout.Columns)
                {
                    if (!_visibilityManager.IsVisible(column.Column))
                        continue;

                    Rect cellRect = new Rect(
                        column.OffsetX,
                        row.OffsetY,
                        column.Width,
                        row.Height);

                    column.Column.Worker.DoCell(cellRect, row.Pawn, table);
                }
            }

            // Draw row separators
            float contentWidth = viewRect.width;
            VisualFeedback.RowSeparatorRenderer.DrawAllSeparators(visibleRows, contentWidth);

            // Draw row overlays
            foreach (var row in visibleRows)
            {
                Rect rowRect = new Rect(0f, row.OffsetY, contentWidth, row.Height);

                if (row.IsDivider)
                {
                    DrawDividerRow(layout, row, contentWidth, rowRect);
                }
                else if (row.Pawn != null)
                {
                    DrawPawnRowOverlay(row, rowRect);
                }
            }
        
                    Widgets.EndScrollView();
                }
        private void DrawPawnRowOverlay(WorkTabLayoutRow row, Rect rowRect)
        {
            // Hover effect
            VisualFeedback.HoverEffectManager.DrawRowHover(row, rowRect);

            // Selection highlight
            if (_selectionManager.State.IsSelected(row.Pawn))
            {
                Widgets.DrawHighlightSelected(rowRect);
            }

            // Favorite indicator
            UI.PawnMarkingRenderer.DrawFavoriteIndicator(rowRect, row.Pawn);

            // Inspect button (removed as users reported it clutters interface)
            // UI.PawnInspectButton.DrawInspectButton(rowRect, row.Pawn);

            // Downed indicator
            if (row.Pawn.Downed)
            {
                GUI.color = new Color(1f, 0f, 0f, 0.5f);
                Widgets.DrawLineHorizontal(0f, rowRect.center.y, rowRect.width);
                GUI.color = Color.white;
            }
        }

        private void DrawDividerRow(IWorkTabLayoutController layout, WorkTabLayoutRow row, float contentWidth, Rect rowRect)
        {
            var divider = row.Divider;
            if (divider == null)
            {
                return;
            }

            Color fill = divider.DividerColor;
            Widgets.DrawBoxSolid(rowRect, fill);
            if (BetterWorkTabMod.Settings.drawDividerHighlight)
            {
                Widgets.DrawBox(rowRect, 1);
            }

            float nameColumnOffset = GetNameColumnOffset(layout);
            Rect labelRect = new Rect(
                Mathf.Max(0f, nameColumnOffset),
                rowRect.y,
                Mathf.Max(0f, contentWidth - nameColumnOffset - 24f),
                rowRect.height);

            bool clicked =
                Widgets.ButtonInvisible(labelRect);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GetFontForDividerHeight(row.Height);
            Widgets.Label(labelRect, divider.DividerName ?? "Divider");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small; // Reset to default

            if (clicked)
            {
                OpenDividerEditor(divider);
            }

            Rect deleteRect = new Rect(
                rowRect.xMax - 18f,
                rowRect.y + (rowRect.height - 16f) / 2f,
                16f,
                16f);
            if (Widgets.ButtonImage(deleteRect, TexButton.CloseXSmall))
            {
                PawnOrganizerSystem.Instance?.Layout.RemoveDivider(divider);
            }
        }

        private GameFont GetFontForDividerHeight(float height)
        {
            if (height < 12f)
            {
                return GameFont.Tiny;
            }
            if (height < 22f)
            {
                return GameFont.Small;
            }
            return GameFont.Medium;
        }

        private void OpenDividerEditor(PawnDivider divider)
        {
            if (divider == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_EditDivider(divider));
        }

        private void EnsureWindowRectMatchesContent(IWorkTabLayoutController layout)
        {
            if (layout?.Table == null)
                return;

            Vector2 minSize = base.InitialSize;
            float targetWidth = Mathf.Max(minSize.x, layout.Table.Size.x + this.Margin * 2f);

            // Calculate desired height based on content
            float rawContentHeight = ExtraTopSpace + ExtraBottomSpace +
                                      layout.HeaderHeight + layout.ContentHeight +
                                      this.Margin * 2f;

            // Cap height at screen space (main tab bar is at bottom, ~35px)
            _maxHeight = global::Verse.UI.screenHeight - 35f - 10f; // 10f buffer from bottom edge
            float newHeight = Mathf.Clamp(rawContentHeight, minSize.y, _maxHeight); // CLAMP here!

            windowRect.width = targetWidth;
            windowRect.height = newHeight; // This is now capped

            if (this.Anchor == MainTabWindowAnchor.Left)
            {
                windowRect.x = 0f;
            }
            else
            {
                windowRect.x = global::Verse.UI.screenWidth - windowRect.width;
            }

            windowRect.y = (global::Verse.UI.screenHeight - 35f) - windowRect.height;
        }

        private static float GetNameColumnOffset(IWorkTabLayoutController layout)
        {
            foreach (var column in layout.Columns)
            {
                if (column.Column.Worker is PawnColumnWorker_Label)
                {
                    return column.OffsetX;
                }
            }
            return 0f;
        }

        private void DrawManualPrioritiesCheckbox()
        {
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Rect rect = new Rect(5f, 5f, 140f, 30f);
            bool wasEnabled = Current.Game.playSettings.useWorkPriorities;
            Widgets.CheckboxLabeled(rect, "ManualPriorities".Translate(), ref Current.Game.playSettings.useWorkPriorities);
            bool isEnabled = Current.Game.playSettings.useWorkPriorities;
            if (wasEnabled != isEnabled)
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                    {
                        pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                    }
                }
            }
            if (Current.Game.playSettings.useWorkPriorities)
            {
                using (new TextBlock(new Color(1f, 1f, 1f, 0.5f)))
                {
                    Widgets.Label(new Rect(rect.x, rect.yMax - 6f, rect.width, 60f), "PriorityOneDoneFirst".Translate());
                }
            }
            else
            {
                UIHighlighter.HighlightOpportunity(rect, "ManualPriorities-Off");
            }
        }

        private void DrawPriorityLegend(Rect rect)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(370f, rect.y + 5f, 160f, 30f), "<= " + "HigherPriority".Translate());
            Widgets.Label(new Rect(630f, rect.y + 5f, 160f, 30f), "LowerPriority".Translate() + " =>");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawInfoButton(Rect gearRect)
        {
            if (Widgets.ButtonImage(gearRect, TexButton.Info))
            {
                var mod = LoadedModManager.GetMod<BetterWorkTabMod>();
                if (mod != null)
                {
                    Find.WindowStack.Add(new Dialog_ModSettings(mod));
                }
            }
        }

        private static Rect GetInfoIconRect(Rect inRect)
        {
            return new Rect(
                inRect.xMax - InfoIconSize - RightEdgeMargin,
                inRect.yMax - InfoIconSize - 10f,
                InfoIconSize,
                InfoIconSize);
        }

        private void DrawBottomRightButtons(Rect inRect, Rect gearRect)
        {
            HeaderButtons.DrawBottomRightGrouped(inRect, gearRect);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.LowerLeft;
            Rect textRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            Widgets.Label(textRect, "Shift to switch mode | ctrl to reorder");
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private PawnTable GetPawnTable()
        {
            var field = typeof(MainTabWindow_PawnTable).GetField("table", BindingFlags.NonPublic | BindingFlags.Instance);
            return (PawnTable)field?.GetValue(this);
        }

        public override void PreClose()
        {
            base.PreClose();
            HighlightManager.ClearHighlight();
            Better_Work_Tab.Patches.WorkTabReorder_PostOpen.ResetAppliedOrderFlag();
        }
    }
}
