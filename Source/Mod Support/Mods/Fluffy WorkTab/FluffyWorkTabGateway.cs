using System;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGiverReassignments;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// Single crossing point for Better Work Tab code that needs to know about
    /// Fluffy Work Tab. Keep package IDs, external type names, texture paths, and
    /// save-shape details behind this facade.
    /// </summary>
    internal static class FluffyWorkTabGateway
    {
        private const float ChooserButtonHeight = 30f;
        private const float ChooserButtonWidth = 156f;
        private static bool _chooserActive;
        private static WorkTypeDef _chooserWorkType;
        private static Rect _chooserSourceRect;
        private static Rect _focusChoiceRegionRect;
        private static Rect _expandChoiceRegionRect;
        private static Rect _focusChoiceButtonRect;
        private static Rect _expandChoiceButtonRect;
        private static Type _fluffyWorkTypeWorkerType;
        private static Type _fluffyWorkGiverWorkerType;
        private static Type _fluffyWorkGiverColumnDefType;
        private static FieldInfo _fluffyWorkGiverField;
        private static FieldInfo _fluffyWorkTypeExpandedField;
        private static FieldInfo _fluffyMainTabTableField;
        private static object _fluffyHostedWindowInstance;
        private static bool _fluffyColumnTypesResolved;
        private static readonly Dictionary<string, PawnColumnDef> HostedWorkTypeColumns =
            new Dictionary<string, PawnColumnDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PawnColumnDef> HostedWorkGiverColumns =
            new Dictionary<string, PawnColumnDef>(StringComparer.Ordinal);
        internal static bool DebugForceSubWorkStyleChooserAvailable;
        internal static BetterWorkTabSettings.SubWorkDrilldownStyle DebugForcedSubWorkStyleChooserHover =
            BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;

        internal static bool IsPresent => FluffyWorkTabCoexistence.IsFluffyWorkTabPresent;

        internal static string DetectedPackageId => FluffyWorkTabCoexistence.DetectedPackageId;

        internal static bool BetterWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.BetterWorkTabOwnsWorkTab;

        internal static bool ExternalWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.FluffyOwnsWorkTab;

        internal static bool ShouldRunBetterWorkTabFeatures => FluffyWorkTabCoexistence.ShouldRunBetterWorkTabFeatures;

        internal static bool HasRightExpandingDrilldown => IsPresent;

        internal static bool CanHostFluffySubWorkColumns => IsPresent && EnsureFluffyColumnTypes();

        internal static bool HasScheduleStrip => IsPresent;

        internal static bool HasIconSet => IsPresent;

        internal static bool IsSubWorkStyleChooserActive => _chooserActive && _chooserWorkType != null;

        internal static WorkTypeDef SubWorkStyleChooserWorkType => _chooserWorkType;

        internal static string ColumnVisibilitySettingLabel => "Show Fluffy Work Tab columns";

        internal static string ColumnVisibilitySettingTooltip =>
            "When Fluffy Work Tab is loaded and Better Work Tab owns the Work tab, keep Fluffy's Mood, Job, Detailed Copy/Paste, and Favourite columns visible.";

        internal static bool IsKnownPackageId(string packageId)
        {
            return FluffyWorkTabCoexistence.IsKnownFluffyPackageId(packageId);
        }

        internal static void ApplyDesiredOwner(bool reopenIfOpen = false)
        {
            FluffyWorkTabCoexistence.ApplyDesiredOwner(reopenIfOpen);
        }

        internal static void SwitchToBetterWorkTab()
        {
            FluffyWorkTabCoexistence.SwitchToBetterWorkTab();
        }

        internal static void SwitchToExternalWorkTab()
        {
            FluffyWorkTabCoexistence.SwitchToFluffyWorkTab();
        }

        internal static void ApplyColumnVisibility()
        {
            FluffyWorkTabCoexistence.ApplyColumnVisibility();
        }

        internal static void MigratePriorityDataIfNeeded(GameComponent_BWTWorldSettings component)
        {
            FluffyWorkTabMigration.MigrateIfNeeded(component);
        }

        internal static bool HasPriorityMigrationHistory(GameComponent_BWTWorldSettings component)
        {
            return FluffyWorkTabMigration.HasMigrationHistory(component);
        }

        internal static void ExposePriorityMigrationVersion(ref int version)
        {
            FluffyWorkTabMigration.ExposeMigrationVersion(ref version);
        }

        internal static bool TryGetManualPriorityToggleIcon(out Texture2D texture)
        {
            return FluffyWorkTabAssets.TryGetManualPriorityToggleIcon(out texture);
        }

        internal static void DrawWorkTabSwitchButton(Rect inRect)
        {
            FluffyWorkTabCoexistenceUI.DrawWorkTabSwitchButton(inRect);
        }

        internal static void DrawSettingsBannerIfNeeded(ref Rect inRect)
        {
            FluffyWorkTabCoexistenceUI.DrawSettingsBannerIfNeeded(ref inRect);
        }

        internal static bool TryStartSubWorkDrilldownStyleChooser(
            IWorkTabLayoutController layout,
            WorkTypeDef workType,
            Rect sourceRect)
        {
            if (((!CanHostFluffySubWorkColumns && !DebugForceSubWorkStyleChooserAvailable)) ||
                workType == null ||
                BetterWorkTabMod.Settings == null ||
                BetterWorkTabMod.Settings.subWorkDrilldownStyle != BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen)
            {
                return false;
            }

            _chooserActive = true;
            _chooserWorkType = workType;
            _chooserSourceRect = sourceRect;
            CenterChooserSourceColumn(layout, instant: false);
            return true;
        }

        internal static bool DebugChooseSubWorkDrilldownStyle(
            BetterWorkTabSettings.SubWorkDrilldownStyle style,
            out WorkTypeDef workType)
        {
            workType = null;
            if (!_chooserActive ||
                style == BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen ||
                BetterWorkTabMod.Settings == null)
            {
                return false;
            }

            workType = _chooserWorkType;
            BetterWorkTabMod.Settings.subWorkDrilldownStyle = style;
            BetterWorkTabMod.Settings.Write();
            ClearSubWorkDrilldownStyleChooser();
            return workType != null;
        }

        internal static void DebugCancelSubWorkDrilldownStyleChooser()
        {
            ClearSubWorkDrilldownStyleChooser();
        }

        internal static void RegisterSubWorkStyleChooserRegions(Rect focusRegion, Rect expandRegion)
        {
            _focusChoiceRegionRect = focusRegion;
            _expandChoiceRegionRect = expandRegion;

            _focusChoiceButtonRect = BuildChoiceButtonRect(
                focusRegion,
                anchorRight: false);
            _expandChoiceButtonRect = BuildChoiceButtonRect(
                expandRegion,
                anchorRight: true);
        }

        internal static bool TryHandleSubWorkDrilldownStyleChooserInput(
            IWorkTabLayoutController layout,
            Event evt,
            out WorkTypeDef workType,
            out BetterWorkTabSettings.SubWorkDrilldownStyle style)
        {
            workType = null;
            style = BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
            if (!_chooserActive || evt == null)
            {
                return false;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ClearSubWorkDrilldownStyleChooser();
                evt.Use();
                return true;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            if (_focusChoiceButtonRect.Contains(evt.mousePosition))
            {
                style = BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView;
            }
            else if (_expandChoiceButtonRect.Contains(evt.mousePosition))
            {
                style = BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside;
            }
            else
            {
                evt.Use();
                return true;
            }

            workType = _chooserWorkType;
            BetterWorkTabMod.Settings.subWorkDrilldownStyle = style;
            BetterWorkTabMod.Settings.Write();
            ClearSubWorkDrilldownStyleChooser();
            evt.Use();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        internal static void DrawSubWorkDrilldownStyleChooser(IWorkTabLayoutController layout, Rect inRect)
        {
            if (!_chooserActive || _chooserWorkType == null || layout == null)
            {
                return;
            }

            CenterChooserSourceColumn(layout, instant: false);

            DrawChoiceButton(
                _focusChoiceButtonRect,
                "Focused view - Better Work Tab",
                DebugForcedSubWorkStyleChooserHover == BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView);
            DrawChoiceButton(
                _expandChoiceButtonRect,
                "Fluffy Work Tab expansion",
                DebugForcedSubWorkStyleChooserHover == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside);
            DrawSubWorkStyleChooserNote(inRect);
        }

        internal static bool TryBuildHostedColumnSpecs(
            PawnColumnDef sourceWorkColumn,
            WorkTypeDef workType,
            out PawnColumnDef hostedWorkTypeColumn,
            out List<PawnColumnDef> hostedWorkGiverColumns)
        {
            hostedWorkTypeColumn = null;
            hostedWorkGiverColumns = null;
            if (!CanHostFluffySubWorkColumns || workType == null)
            {
                return false;
            }

            hostedWorkTypeColumn = GetOrCreateHostedWorkTypeColumn(sourceWorkColumn, workType);
            if (hostedWorkTypeColumn == null)
            {
                return false;
            }

            hostedWorkGiverColumns = new List<PawnColumnDef>();
            List<WorkGiverDef> workGivers = GetFluffyOrderedWorkGivers(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                PawnColumnDef child = GetOrCreateHostedWorkGiverColumn(workGivers[i]);
                if (child != null)
                {
                    hostedWorkGiverColumns.Add(child);
                }
            }

            return hostedWorkGiverColumns.Count > 0;
        }

        internal static bool TryBuildHostedPreviewColumns(
            PawnColumnDef sourceWorkColumn,
            WorkTypeDef workType,
            float startX,
            float parentWidth,
            float childWidth,
            out List<WorkTabLayoutColumn> columns)
        {
            columns = null;
            if (!TryBuildHostedColumnSpecs(
                    sourceWorkColumn,
                    workType,
                    out PawnColumnDef parentColumn,
                    out List<PawnColumnDef> childColumns))
            {
                return false;
            }

            columns = new List<WorkTabLayoutColumn>();
            float x = startX;
            columns.Add(new WorkTabLayoutColumn(
                parentColumn,
                new Rect(x, 0f, parentWidth, 1f),
                x,
                parentWidth));
            x += parentWidth;

            for (int i = 0; i < childColumns.Count; i++)
            {
                WorkGiverDef workGiverDef = TryGetHostedWorkGiver(childColumns[i]);
                if (workGiverDef == null)
                {
                    continue;
                }

                columns.Add(new WorkTabLayoutColumn(
                    childColumns[i],
                    new Rect(x, 0f, childWidth, 1f),
                    x,
                    childWidth,
                    workType,
                    workGiverDef,
                    i,
                    isExpandBesideChild: true));
                x += childWidth;
            }

            return columns.Count > 1;
        }

        internal static WorkGiverDef TryGetHostedWorkGiver(PawnColumnDef column)
        {
            if (column == null || !EnsureFluffyColumnTypes() || !_fluffyWorkGiverColumnDefType.IsInstanceOfType(column))
            {
                return null;
            }

            return _fluffyWorkGiverField?.GetValue(column) as WorkGiverDef;
        }

        internal static void PrepareHostedDraw(PawnTable table)
        {
            if (table == null ||
                !CanHostFluffySubWorkColumns ||
                !FluffyWorkTabCoexistence.TryGetFluffyWorkTabWindowType(out Type windowType))
            {
                return;
            }

            if (_fluffyMainTabTableField == null)
            {
                _fluffyMainTabTableField = AccessTools.Field(typeof(MainTabWindow_PawnTable), "table");
            }

            if (_fluffyHostedWindowInstance == null ||
                !windowType.IsInstanceOfType(_fluffyHostedWindowInstance))
            {
                _fluffyHostedWindowInstance = Activator.CreateInstance(windowType);
            }

            _fluffyMainTabTableField?.SetValue(_fluffyHostedWindowInstance, table);
        }

        internal static bool WasHostedWorkTypeCollapsed(PawnColumnDef column)
        {
            if (!IsHostedFluffyWorkTypeColumn(column))
            {
                return false;
            }

            object worker = column.Worker;
            if (worker == null || _fluffyWorkTypeExpandedField == null)
            {
                return false;
            }

            if (_fluffyWorkTypeExpandedField.GetValue(worker) is bool expanded && !expanded)
            {
                _fluffyWorkTypeExpandedField.SetValue(worker, true);
                return true;
            }

            return false;
        }

        internal static bool IsHostedFluffyColumn(PawnColumnDef column)
        {
            if (column == null || !EnsureFluffyColumnTypes())
            {
                return false;
            }

            Type workerType = column.Worker?.GetType();
            return workerType == _fluffyWorkTypeWorkerType ||
                   workerType == _fluffyWorkGiverWorkerType;
        }

        internal static bool IsHostedFluffyWorkTypeColumn(PawnColumnDef column)
        {
            return column != null &&
                   EnsureFluffyColumnTypes() &&
                   column.Worker?.GetType() == _fluffyWorkTypeWorkerType &&
                   HostedWorkTypeColumns.ContainsValue(column);
        }

        internal static float GetHostedColumnWidth(PawnColumnDef column, PawnTable table, float fallback)
        {
            if (!IsHostedFluffyColumn(column))
            {
                return fallback;
            }

            try
            {
                return Mathf.Max(1f, column.Worker.GetMinWidth(table));
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[FluffyWorkTab] Failed to query hosted column width for " + column.defName + ": " + ex.Message,
                    DebugFeature.SubWork);
                return fallback;
            }
        }

        private static void DrawChoiceButton(Rect rect, string caption, bool forceHover)
        {
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return;
            }

            Rect captionRect = new Rect(rect.x, rect.y - 19f, rect.width, 18f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.78f);
            Widgets.Label(captionRect, caption);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (forceHover)
            {
                Widgets.DrawHighlight(rect);
            }

            Widgets.ButtonText(rect, "Use this view");
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawSubWorkStyleChooserNote(Rect inRect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(
                new Rect(inRect.xMin + 80f, inRect.yMax - 44f, Mathf.Max(1f, inRect.width - 160f), 24f),
                "You can change this any time in Better Work Tab's settings.");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void ClearSubWorkDrilldownStyleChooser()
        {
            _chooserActive = false;
            _chooserWorkType = null;
            _chooserSourceRect = Rect.zero;
            _focusChoiceRegionRect = Rect.zero;
            _expandChoiceRegionRect = Rect.zero;
            _focusChoiceButtonRect = Rect.zero;
            _expandChoiceButtonRect = Rect.zero;
            DebugForcedSubWorkStyleChooserHover = BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
        }

        internal static Rect ResolveSubWorkStyleChooserSourceRect(IWorkTabLayoutController layout)
        {
            if (layout?.Columns != null && _chooserWorkType != null)
            {
                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    var column = layout.Columns[i];
                    if (!column.IsExpandBesideChild && column.Column?.workType == _chooserWorkType)
                    {
                        _chooserSourceRect = column.HeaderRect;
                        return column.HeaderRect;
                    }
                }
            }

            return _chooserSourceRect;
        }

        private static void CenterChooserSourceColumn(IWorkTabLayoutController layout, bool instant)
        {
            PawnTable table = layout?.Table;
            if (table == null)
            {
                return;
            }

            Rect sourceRect = ResolveSubWorkStyleChooserSourceRect(layout);
            float localCenter = table.scrollPosition.x + (sourceRect.center.x - layout.TableOrigin.x);
            float targetX = Mathf.Max(0f, localCenter - (table.Size.x * 0.5f));
            Vector2 scroll = table.scrollPosition;
            scroll.x = instant ? targetX : Mathf.Lerp(scroll.x, targetX, 0.18f);
            table.scrollPosition = scroll;
        }

        private static Rect BuildChoiceButtonRect(Rect region, bool anchorRight)
        {
            if (region.width <= 1f || region.height <= 1f)
            {
                return Rect.zero;
            }

            float width = Mathf.Min(ChooserButtonWidth, Mathf.Max(80f, region.width - 8f));
            float x = anchorRight ? region.xMax - width : region.xMin;
            return new Rect(
                Mathf.Clamp(x, region.xMin, Mathf.Max(region.xMin, region.xMax - width)),
                region.yMin + 6f,
                width,
                ChooserButtonHeight);
        }

        private static bool EnsureFluffyColumnTypes()
        {
            if (_fluffyColumnTypesResolved)
            {
                return _fluffyWorkTypeWorkerType != null &&
                       _fluffyWorkGiverWorkerType != null &&
                       _fluffyWorkGiverColumnDefType != null &&
                       _fluffyWorkGiverField != null &&
                       _fluffyWorkTypeExpandedField != null;
            }

            _fluffyColumnTypesResolved = true;
            _fluffyWorkTypeWorkerType = AccessTools.TypeByName("WorkTab.PawnColumnWorker_WorkType");
            _fluffyWorkGiverWorkerType = AccessTools.TypeByName("WorkTab.PawnColumnWorker_WorkGiver");
            _fluffyWorkGiverColumnDefType = AccessTools.TypeByName("WorkTab.PawnColumnDef_WorkGiver");
            _fluffyWorkGiverField = _fluffyWorkGiverColumnDefType?.GetField(
                "workgiver",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fluffyWorkTypeExpandedField = _fluffyWorkTypeWorkerType?.GetField(
                "_expanded",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return _fluffyWorkTypeWorkerType != null &&
                   _fluffyWorkGiverWorkerType != null &&
                   _fluffyWorkGiverColumnDefType != null &&
                   _fluffyWorkGiverField != null &&
                   _fluffyWorkTypeExpandedField != null;
        }

        private static PawnColumnDef GetOrCreateHostedWorkTypeColumn(PawnColumnDef sourceWorkColumn, WorkTypeDef workType)
        {
            string key = workType.defName ?? string.Empty;
            if (HostedWorkTypeColumns.TryGetValue(key, out PawnColumnDef existing))
            {
                return existing;
            }

            var column = new PawnColumnDef
            {
                defName = "BWT_FluffyHosted_WorkType_" + key,
                workerClass = _fluffyWorkTypeWorkerType,
                workType = workType,
                sortable = sourceWorkColumn?.sortable ?? true,
                moveWorkTypeLabelDown = sourceWorkColumn?.moveWorkTypeLabelDown ?? false
            };
            column.PostLoad();
            ForceHostedWorkTypeExpanded(column, expanded: true);
            HostedWorkTypeColumns[key] = column;
            return column;
        }

        private static void ForceHostedWorkTypeExpanded(PawnColumnDef column, bool expanded)
        {
            if (column?.Worker == null || _fluffyWorkTypeExpandedField == null)
            {
                return;
            }

            _fluffyWorkTypeExpandedField.SetValue(column.Worker, expanded);
        }

        private static PawnColumnDef GetOrCreateHostedWorkGiverColumn(WorkGiverDef workGiver)
        {
            if (workGiver?.defName == null || !EnsureFluffyColumnTypes())
            {
                return null;
            }

            string key = workGiver.defName;
            if (HostedWorkGiverColumns.TryGetValue(key, out PawnColumnDef existing))
            {
                return existing;
            }

            PawnColumnDef databaseColumn = DefDatabase<PawnColumnDef>.GetNamedSilentFail("WorkGiver_" + key);
            if (databaseColumn != null && _fluffyWorkGiverColumnDefType.IsInstanceOfType(databaseColumn))
            {
                HostedWorkGiverColumns[key] = databaseColumn;
                return databaseColumn;
            }

            var column = Activator.CreateInstance(_fluffyWorkGiverColumnDefType) as PawnColumnDef;
            if (column == null)
            {
                return null;
            }

            column.defName = "BWT_FluffyHosted_WorkGiver_" + key;
            column.workerClass = _fluffyWorkGiverWorkerType;
            column.sortable = true;
            _fluffyWorkGiverField.SetValue(column, workGiver);
            column.PostLoad();
            HostedWorkGiverColumns[key] = column;
            return column;
        }

        private static List<WorkGiverDef> GetFluffyOrderedWorkGivers(WorkTypeDef workType)
        {
            var result = new List<WorkGiverDef>();
            if (workType?.workGiversByPriority == null)
            {
                return result;
            }

            result.AddRange(workType.workGiversByPriority);
            result.Sort((a, b) => (b?.priorityInType ?? 0f).CompareTo(a?.priorityInType ?? 0f));
            return result;
        }
    }
}
