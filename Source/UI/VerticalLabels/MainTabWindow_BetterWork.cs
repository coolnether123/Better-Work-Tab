using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using Spine.UI.ColourPicker;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Main work tab window that coordinates PawnOrganizer layout with vanilla rendering.
    /// </summary>
    public class MainTabWindow_BetterWork : MainTabWindow_Work
    {
        private const float RightEdgeMargin = 10f;
        private const float InfoIconSize = 24f;
        private const float HeightSnapThreshold = 12f;
        private const float WindowSnapCooldownSeconds = 0.25f;
        private static bool _pendingWindowSnap;
        private static bool _columnsReordered;
        private WorkTabLayoutColumn? _hoveredColumn;
        private float _lastKnownHeight = -1f;
        private float _lastWindowSnapTime;
        private PawnColumnDef _lastSortColumn;
        private bool _lastSortDescending;

        private static Color CurrentRowTextColor = Color.white;
        private static readonly Color ColumnReorderTint = new Color(1f, 0.85f, 0.2f, 0.28f);

        public override void PreOpen()
        {
            base.PreOpen();
            closeOnClickedOutside = !BetterWorkTabMod.Settings.disableLeftClickClose;
            ClearColumnReorderFlag();
            _lastSortColumn = null;
            _lastSortDescending = false;

            if (PawnOrganizerSystem.Instance == null)
            {
                var widthStore = new ColumnWidthPersistence();
                new PawnOrganizerSystem(widthStore);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            PawnTable table = GetPawnTable();
            if (table == null)
            {
                return;
            }

            var organizer = PawnOrganizerSystem.Instance;
            Vector2 tableOrigin = new Vector2(inRect.x, inRect.y + ExtraTopSpace);
            var snapshot = BuildSnapshotForOrganizer(table);

            organizer?.Update(table, tableOrigin, snapshot);

            Event evt = Event.current;
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                organizer?.HandleInput(evt);
                ProcessRightClicks(organizer?.Layout);
            }

            AdjustWindowHeight(organizer?.Layout, inRect);

            DrawWorkTable(table, organizer?.Layout, inRect);

            organizer?.DrawDragOverlays();

            DrawManualPrioritiesCheckbox();
            DrawPriorityLegend(inRect);

            Rect infoRect = GetInfoIconRect(inRect);
            DrawBottomRightButtons(inRect, infoRect);
            DrawInfoButton(infoRect);
            DrawBottomCounters(inRect, table);
        }

        private IPawnOrganizerSnapshot BuildSnapshotForOrganizer(PawnTable table)
        {
            var pawns = new List<Pawn>(table.PawnsListForReading);
            var comp = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            var dividers = comp?.CurrentWorklist?.Dividers ?? new List<PawnDivider>();
            return new WorkTabSnapshot(pawns, dividers);
        }

        private void ProcessRightClicks(IWorkTabLayoutController layout)
        {
            if (layout == null)
            {
                return;
            }

            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 1)
            {
                return;
            }


            if (!layout.TryGetRowAt(evt.mousePosition, out var row))
            {
                return;
            }

            if (row.Divider != null)
            {
                ShowDividerContextMenu(row.Divider);
                evt.Use();
                return;
            }

            if (row.Pawn == null)
            {
                return;
            }

            if (TryGetBodyColumnAt(layout, evt.mousePosition, out var column) &&
                column.Column?.Worker is PawnColumnWorker_Label)
            {
                ShowPawnContextMenu(row.Pawn);
                evt.Use();
            }
        }

        private void ShowPawnContextMenu(Pawn pawn)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Insert divider above", () => InsertDividerAbove(pawn)),
                new FloatMenuOption("Insert divider below", () => InsertDividerBelow(pawn)),
                new FloatMenuOption("Change title...", () => ShowRenamePawnDialog(pawn)),
                new FloatMenuOption("Set background color...", () => ShowBackgroundColorPicker(pawn))
            };

            if (PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out _))
            {
                options.Add(new FloatMenuOption("Clear background color", () =>
                {
                    PawnOrganizer.API.PawnColorDatabase.ClearColor(pawn);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowRenamePawnDialog(Pawn pawn)
        {
            Find.WindowStack.Add(pawn.NamePawnDialog());
        }

        private void ShowDividerContextMenu(PawnDivider divider)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit...", () =>
                {
                    Find.WindowStack.Add(new Dialog_EditDivider(divider));
                }),
                new FloatMenuOption("Delete", () =>
                {
                    PawnOrganizerSystem.Instance?.Layout.RemoveDivider(divider);
                    UI.MainTabWindow_BetterWork.FlagWindowSnap();
                    MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                })
            };

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void InsertDividerAbove(Pawn pawn)
        {
            var worklist = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || worklist == null)
            {
                return;
            }

            layout.AddDividerBeforePawn(pawn, "New Divider", Color.gray);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            FlagWindowSnap(); 
        }

        private void InsertDividerBelow(Pawn pawn)
        {
            var worklist = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || worklist == null)
            {
                return;
            }

            layout.AddDividerAfterPawn(pawn, "New Divider", Color.gray);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            FlagWindowSnap(); 
        }

        private void ShowBackgroundColorPicker(Pawn pawn)
        {
            Color current = PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out var stored) ? stored : Color.white;
            Find.WindowStack.Add(new Dialog_ColourPicker(current, (newColor, _) =>
            {
                PawnOrganizerSystem.Instance?.SetPawnBackgroundColor(pawn, newColor);
            }));
        }

        private void DrawWorkTable(PawnTable table, IWorkTabLayoutController layout, Rect inRect)
        {
            if (layout == null || table == null)
            {
                return;
            }

            CalculateScrollRects(layout, inRect, out var outRect, out var viewRect);
            UpdateHoveredColumn(layout);
            UpdateSortState(table);

            DrawHeaders(layout, table);
            DrawRows(table, layout, outRect, viewRect);
        }

        private void UpdateSortState(PawnTable table)
        {
            if (table == null)
            {
                _lastSortColumn = null;
                _lastSortDescending = false;
                return;
            }

            var current = table.SortingBy;
            bool descending = current != null && table.SortingDescending;
            if (!ReferenceEquals(current, _lastSortColumn) || descending != _lastSortDescending)
            {
                ClearColumnReorderFlag();
                _lastSortColumn = current;
                _lastSortDescending = descending;
            }
        }

        private void DrawHeaders(IWorkTabLayoutController layout, PawnTable table)
        {
            foreach (var column in layout.Columns)
            {
                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;
                bool showReorder = _columnsReordered && isWorkColumn;
                bool highlightHeader = BetterWorkTabMod.Settings.enableRowColumnHighlights &&
                                       _hoveredColumn.HasValue &&
                                       ReferenceEquals(_hoveredColumn.Value.Column, column.Column);

                if (showReorder)
                {
                    Widgets.DrawBoxSolid(column.HeaderRect, ColumnReorderTint);
                }

                if (highlightHeader)
                {
                    var highlightColor = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
                    Widgets.DrawBoxSolid(column.HeaderRect, highlightColor);
                    Widgets.DrawBox(column.HeaderRect, 1);
                }

                column.Column.Worker.DoHeader(column.HeaderRect, table);

                if (showReorder)
                {
                    DrawColumnReorderMarker(column.HeaderRect);
                }
            }
        }

        private void DrawColumnReorderMarker(Rect headerRect)
        {
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            var prevColor = GUI.color;

            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 0.92f, 0.25f);

            Rect starRect = new Rect(headerRect.x, headerRect.y + 2f, headerRect.width, 12f);
            Widgets.Label(starRect, "*");

            GUI.color = prevColor;
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        private void DrawRows(PawnTable table, IWorkTabLayoutController layout, Rect outRect, Rect viewRect)
        {
            if (layout?.Rows == null)
            {
                return;
            }

            Widgets.BeginScrollView(outRect, ref table.scrollPosition, viewRect);
            try
            {
                var nameColumn = FindNameColumn(layout.Columns);

                foreach (var row in layout.Rows)
                {
                    Rect rowRect = new Rect(0f, row.OffsetY, viewRect.width, row.Height);
                    DrawRowBackground(row, rowRect);

                    if (BetterWorkTabMod.Settings.enableRowColumnHighlights && _hoveredColumn.HasValue)
                    {
                        DrawColumnHighlight(rowRect, _hoveredColumn.Value);
                    }

                    if (row.Pawn != null)
                    {
                        DrawPawnRow(table, row, rowRect, layout.Columns);
                        DrawPawnRowOverlay(row, rowRect);
                    }
                    else if (row.Divider != null && nameColumn.HasValue)
                    {
                        DrawDividerRow(row.Divider, rowRect, nameColumn.Value);
                    }

                    GUI.color = new Color(1f, 1f, 1f, 0.12f);
                    Widgets.DrawLineHorizontal(0f, rowRect.yMax - 1f, viewRect.width);
                    GUI.color = Color.white;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] DrawRows failed: {ex}");
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawRowBackground(WorkTabLayoutRow row, Rect rect)
        {
            if (row.Pawn != null && PawnOrganizer.API.PawnColorDatabase.TryGetColor(row.Pawn, out var pawnColor) && pawnColor.a > 0f)
            {
                var overlay = new Color(pawnColor.r, pawnColor.g, pawnColor.b, Mathf.Clamp(pawnColor.a, 0.08f, 0.6f));
                Widgets.DrawBoxSolid(rect, overlay);

                CurrentRowTextColor = Spine.UI.TextColorHelper.GetContrastingTextColor(overlay);
            }
            else if (row.Divider != null)
            {
                var dividerColor = row.Divider.DividerColor;
                if (row.Divider.IsCollapsed)
                {
                    dividerColor.a = Mathf.Clamp01(dividerColor.a * 0.6f);
                }
                Widgets.DrawBoxSolid(rect, dividerColor);
            }
        }

        private void CalculateScrollRects(IWorkTabLayoutController layout, Rect inRect, out Rect outRect, out Rect viewRect)
        {
            float headerHeight = layout.HeaderHeight;
            outRect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + headerHeight,
                layout.Table.Size.x,
                Mathf.Max(0f, inRect.height - headerHeight - ExtraBottomSpace));

            float widthWithoutScrollbar = layout.Table.Size.x - 16f;
            float totalColumnWidth = layout.Columns.Count > 0
                ? layout.Columns[layout.Columns.Count - 1].OffsetX + layout.Columns[layout.Columns.Count - 1].Width
                : widthWithoutScrollbar;
            float viewWidth = Mathf.Max(widthWithoutScrollbar, totalColumnWidth);
            float contentHeight = Mathf.Max(layout.ContentHeight, 1f);
            viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(contentHeight, outRect.height));
        }

        private WorkTabLayoutColumn? FindNameColumn(IReadOnlyList<WorkTabLayoutColumn> columns)
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

        private void UpdateHoveredColumn(IWorkTabLayoutController layout)
        {
            if (layout == null)
            {
                _hoveredColumn = null;
                return;
            }

            var evtType = Event.current.type;
            if (evtType != EventType.Repaint && evtType != EventType.MouseMove)
            {
                return;
            }

            _hoveredColumn = null;
            Vector2 mousePosition = Event.current.mousePosition;
            if (layout.TryGetColumnAt(mousePosition, out var headerColumn))
            {
                _hoveredColumn = headerColumn;
            }
        }

        private bool TryGetBodyColumnAt(IWorkTabLayoutController layout, Vector2 mousePosition, out WorkTabLayoutColumn column)
        {
            column = default;
            if (layout?.Table == null)
            {
                return false;
            }

            float headerTop = layout.TableOrigin.y + layout.HeaderHeight;
            float bodyBottom = layout.TableOrigin.y + layout.Table.Size.y;
            if (mousePosition.y < headerTop || mousePosition.y > bodyBottom)
            {
                return false;
            }

            float localX = mousePosition.x - layout.TableOrigin.x;
            if (localX < 0f)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var candidate = layout.Columns[i];
                if (localX >= candidate.OffsetX && localX <= candidate.OffsetX + candidate.Width)
                {
                    column = candidate;
                    return true;
                }
            }

            return false;
        }

        private void DrawColumnHighlight(Rect rowRect, WorkTabLayoutColumn column)
        {
            Rect highlightRect = new Rect(column.OffsetX, rowRect.y, column.Width, rowRect.height);
            var color = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
            var overlay = new Color(color.r, color.g, color.b, Mathf.Clamp(color.a, 0.08f, 0.35f));
            Widgets.DrawBoxSolid(highlightRect, overlay);
        }

        private void DrawDividerRow(PawnDivider divider, Rect rowRect, WorkTabLayoutColumn nameColumn)
        {
            Rect cellRect = new Rect(nameColumn.OffsetX, rowRect.y, nameColumn.Width, rowRect.height);
            DrawDividerToggle(divider, cellRect);
            DrawDividerLabel(divider, cellRect);
        }

        private void DrawDividerToggle(PawnDivider divider, Rect labelCellRect)
        {
            Rect arrowRect = new Rect(labelCellRect.xMin + 6f, labelCellRect.y + (labelCellRect.height - 16f) / 2f, 18f, 16f);
            string arrowChar = divider.IsCollapsed ? "▶" : "▼";
            if (Widgets.ButtonInvisible(arrowRect))
            {
                ToggleDividerCollapsed(divider);
            }
            var originalAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(arrowRect, arrowChar);
            Text.Anchor = originalAnchor;
            TooltipHandler.TipRegion(arrowRect, divider.IsCollapsed ? "Expand section" : "Collapse section");
        }

        private void ToggleDividerCollapsed(PawnDivider divider)
        {
            if (divider == null)
            {
                return;
            }

            divider.IsCollapsed = !divider.IsCollapsed;
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            FlagWindowSnap();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        private void DrawPawnRow(PawnTable table, WorkTabLayoutRow row, Rect rowRect, IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            foreach (var column in columns)
            {
                Rect cellRect = new Rect(column.OffsetX, rowRect.y, column.Width, rowRect.height);
                column.Column.Worker.DoCell(cellRect, row.Pawn, table);
            }
        }

        private void DrawPawnRowOverlay(WorkTabLayoutRow row, Rect rowRect)
        {
            if (row.Pawn == null)
            {
                return;
            }

            if (Find.Selector.IsSelected(row.Pawn))
            {
                Widgets.DrawHighlight(rowRect, 0.6f);
            }

            if (BetterWorkTabMod.Settings.enableRowColumnHighlights && Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
            }

            if (row.Pawn != null && row.Pawn.Downed)
            {
                GUI.color = new Color(1f, 0f, 0f, 0.5f);
                Widgets.DrawLineHorizontal(rowRect.xMin, rowRect.center.y, rowRect.width);
                GUI.color = Color.white;
            }
        }

        private void DrawDividerLabel(PawnDivider divider, Rect labelCellRect)
        {
            if (!divider.ShowLabel)
            {
                return;
            }

            var originalAnchor = Text.Anchor;
            var originalFont = Text.Font;
            var originalColor = GUI.color;

            try
            {
                GUI.color = Spine.UI.TextColorHelper.GetContrastingTextColor(divider.DividerColor);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = divider.LabelFont;

                // Increase the left indent to match pawn name padding
                labelCellRect.xMin += 33f; 

                Widgets.Label(labelCellRect, divider.DividerName ?? "Divider");
            }
            finally
            {
                Text.Anchor = originalAnchor;
                Text.Font = originalFont;
                GUI.color = originalColor;
            }
        }

        private void AdjustWindowHeight(IWorkTabLayoutController layout, Rect inRect)
        {
            if (PawnOrganizerSystem.Instance?.IsDraggingRow == true)
            {
                return;
            }

            if (layout == null)
            {
                return;
            }

            float desiredHeight = ExtraTopSpace + ExtraBottomSpace +
                                 layout.HeaderHeight + layout.ContentHeight +
                                 Margin * 2f;

            float maxHeight = Verse.UI.screenHeight - 45f;
            float minHeight = InitialSize.y;

            float previousHeight = windowRect.height;
            windowRect.height = Mathf.Clamp(desiredHeight, minHeight, maxHeight);
            if (_lastKnownHeight < 0f)
            {
                _lastKnownHeight = windowRect.height;
            }

            float heightDelta = Mathf.Abs(windowRect.height - _lastKnownHeight);
            if (heightDelta > 0.5f)
            {
                _lastKnownHeight = windowRect.height;
            }

            if (Prefs.DevMode && heightDelta > 0.01f)
            {
                Log.Message($"[BWT] Height: {_lastKnownHeight:F1} -> {desiredHeight:F1}, delta={heightDelta:F1}, pendingSnap={_pendingWindowSnap}");
            }

            bool autoSnapRequested = heightDelta > HeightSnapThreshold;
            bool shouldSnap = _pendingWindowSnap;
            if (!shouldSnap && autoSnapRequested && Prefs.DevMode)
            {
                Log.Message($"[BWT] Auto-snap suppressed (delta={heightDelta:F1})");
            }
            if (!shouldSnap)
            {
                return;
            }

            if (PawnOrganizerSystem.Instance?.IsDraggingRow == true)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now - _lastWindowSnapTime < WindowSnapCooldownSeconds && Mathf.Approximately(previousHeight, windowRect.height))
            {
                return;
            }

            _pendingWindowSnap = false;
            windowRect.y = Mathf.Max(0f, (Verse.UI.screenHeight - 35f) - windowRect.height);
            _lastWindowSnapTime = now;
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

        private void DrawBottomRightButtons(Rect inRect, Rect gearRect)
        {
            HeaderButtons.DrawBottomRightGrouped(inRect, gearRect);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.LowerLeft;
            Rect textRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            string dragInstruction = BetterWorkTabMod.Settings.requireCtrlForDrag
                ? "Ctrl + drag to reorder"
                : "Drag to reorder";
            Widgets.Label(textRect, $"Shift toggles overlay | {dragInstruction}");
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

        private void DrawBottomCounters(Rect inRect, PawnTable table)
        {
            bool showPawns = BetterWorkTabMod.Settings.showPawnCountAtBottom;
            bool showBeds = BetterWorkTabMod.Settings.showBedCountAtBottom;
            if (!showPawns && !showBeds)
            {
                return;
            }

            int pawnCount = showPawns ? table?.cachedPawns?.Count ?? 0 : 0;
            int bedCount = 0;
            if (showBeds)
            {
                Map map = Find.CurrentMap;
                if (map?.listerBuildings != null)
                {
                    var beds = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>();
                    if (beds != null)
                    {
                        foreach (var bed in beds)
                        {
                            if (bed == null || bed.ForPrisoners || bed.Faction != Faction.OfPlayer)
                            {
                                continue;
                            }

                            bedCount += bed.SleepingSlotsCount;
                        }
                    }
                }
            }

            string label = string.Empty;
            if (showPawns)
            {
                label = $"Colonists: {pawnCount}";
            }
            if (showBeds)
            {
                if (!string.IsNullOrEmpty(label))
                {
                    label += " | ";
                }
                label += $"Beds: {bedCount}";
            }

            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            var rect = new Rect(inRect.x + 6f, inRect.yMax - 45f, inRect.width * 0.5f, 20f);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(rect, label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
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
            ClearColumnReorderFlag();
            _lastSortColumn = null;
            _lastSortDescending = false;
        }

        internal static void MarkColumnsReordered()
        {
            _columnsReordered = true;
        }

        internal static void ClearColumnReorderFlag()
        {
            _columnsReordered = false;
        }

        internal static void FlagWindowSnap()
        {
            _pendingWindowSnap = true;
        }
    }
}
