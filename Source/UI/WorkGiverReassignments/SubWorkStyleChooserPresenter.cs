using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Presents and owns the immediate-mode chooser for the first sub-work view selection.
    /// </summary>
    internal sealed class SubWorkStyleChooserPresenter
    {
        private const float ChooserPanelHeight = 118f;
        private const float ChooserRememberRowHeight = 24f;
        private const float ChooserNoteHeight = 22f;
        private const float ChooserWindowHeight = ChooserPanelHeight + ChooserRememberRowHeight + ChooserNoteHeight + 8f;
        private const float ChooserWindowGap = 6f;
        private const int ChooserImmediateWindowId = 984361;

        private readonly SubWorkInteractionController _subWorkInteractionController;
        private Rect _stableSubWorkChooserWindowRect;

        internal SubWorkStyleChooserPresenter(SubWorkInteractionController subWorkInteractionController)
        {
            _subWorkInteractionController = subWorkInteractionController ??
                throw new ArgumentNullException(nameof(subWorkInteractionController));
        }

        internal void ResetForWindowClose()
        {
            _stableSubWorkChooserWindowRect = Rect.zero;
            FluffyWorkTabGateway.ResetSubWorkDrilldownStyleChooserForWindowClose();
        }

        internal void Draw(
            IWorkTabLayoutController layout,
            Rect windowGeometry,
            Rect contentGeometry)
        {
            if (!BwtExpandBesideColumns.CanBuild)
            {
                // A native column-construction failure makes the expand choice
                // unavailable. End the transient chooser and its hover preview so
                // its invisible state cannot continue suppressing other Work-tab UI.
                ResetForWindowClose();
                return;
            }

            if (!FluffyWorkTabGateway.IsSubWorkStyleChooserActive)
            {
                _stableSubWorkChooserWindowRect = Rect.zero;
                return;
            }

            if (!CanShowChooserComparison(layout))
            {
                return;
            }

            // Hover previews deliberately animate the Work window and its columns. The
            // chooser is an input surface, so its screen-space hitboxes must remain fixed
            // from open until selection; otherwise the cursor can fall off a moving choice
            // and repeatedly reverse the preview animation.
            if (_stableSubWorkChooserWindowRect.width <= 1f || _stableSubWorkChooserWindowRect.height <= 1f)
            {
                _stableSubWorkChooserWindowRect = GetChooserWindowRect(windowGeometry, contentGeometry);
            }

            Rect chooserWindowRect = _stableSubWorkChooserWindowRect;
            Find.WindowStack.ImmediateWindow(
                ChooserImmediateWindowId,
                chooserWindowRect,
                WindowLayer.Super,
                () => DrawSubWorkStyleChooserImmediateWindow(layout, chooserWindowRect.AtZero()),
                doBackground: false,
                absorbInputAroundWindow: false,
                shadowAlpha: 0.35f);

            if (!FluffyWorkTabGateway.IsSubWorkStyleChooserActive)
            {
                _stableSubWorkChooserWindowRect = Rect.zero;
            }
        }

        private void DrawSubWorkStyleChooserImmediateWindow(
            IWorkTabLayoutController layout,
            Rect contentRect)
        {
            ChooserComparisonGeometry geometry = BuildChooserComparisonGeometry(contentRect);
            FluffyWorkTabGateway.RegisterSubWorkStyleChooserRegions(
                geometry.FocusRegion,
                geometry.ExpandRegion,
                geometry.RememberRegion);

            Event evt = Event.current;
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                TryHandleSubWorkStyleChooser(layout, evt);
            }

            if (!FluffyWorkTabGateway.IsSubWorkStyleChooserActive)
            {
                return;
            }

            FluffyWorkTabGateway.UpdateSubWorkDrilldownStyleChooserPreview(layout);

            if (evt.type != EventType.Repaint)
            {
                return;
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                DrawChooserChoiceFrame(
                    geometry.FocusRegion,
                    "BWT_SubWork_Chooser_FocusTitle".Translate(),
                    "BWT_SubWork_Chooser_FocusDescription".Translate());
                DrawChooserChoiceFrame(
                    geometry.ExpandRegion,
                    "BWT_SubWork_Chooser_ExpandTitle".Translate(),
                    "BWT_SubWork_Chooser_ExpandDescription".Translate());
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }

            FluffyWorkTabGateway.DrawSubWorkDrilldownStyleChooser(layout, contentRect);
        }

        private bool TryHandleSubWorkStyleChooser(IWorkTabLayoutController layout, Event evt)
        {
            if (!FluffyWorkTabGateway.TryHandleSubWorkDrilldownStyleChooserInput(
                    layout,
                    evt,
                    out var workType,
                    out var style,
                    out int sourceWorkColumnSlot))
            {
                return false;
            }

            if (workType == null || style == BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen)
            {
                return true;
            }

            _subWorkInteractionController.ApplyStyleChooserSelection(
                layout,
                workType,
                style,
                sourceWorkColumnSlot);
            return true;
        }

        private static bool CanShowChooserComparison(IWorkTabLayoutController layout)
        {
            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked ||
                !FluffyWorkTabGateway.IsSubWorkStyleChooserActive ||
                layout?.Table == null ||
                layout.Columns == null)
            {
                return false;
            }

            WorkTypeDef workType = FluffyWorkTabGateway.SubWorkStyleChooserWorkType;
            WorkTabLayoutColumn? sourceColumn = FindChooserSourceColumn(layout, workType);
            if (!sourceColumn.HasValue)
            {
                return false;
            }

            IReadOnlyList<WorkGiver> workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
            if (workGivers == null || workGivers.Count == 0)
            {
                return false;
            }

            if (!BwtExpandBesideColumns.TryBuildColumnSpecs(
                    sourceColumn.Value.Column,
                    workType,
                    out _,
                    out _))
            {
                return false;
            }

            return true;
        }

        private static Rect GetChooserWindowRect(Rect windowRect, Rect contentRect)
        {
            const float screenMargin = 8f;
            float availableWidth = Mathf.Max(360f, windowRect.width - 80f);
            float width = Mathf.Min(860f, availableWidth);
            width = Mathf.Min(width, Mathf.Max(1f, Verse.UI.screenWidth - (screenMargin * 2f)));
            float x = Mathf.Clamp(
                windowRect.xMax - width - 18f,
                screenMargin,
                Mathf.Max(screenMargin, Verse.UI.screenWidth - width - screenMargin));
            float y = Mathf.Max(
                screenMargin,
                windowRect.yMin - ChooserWindowGap - ChooserWindowHeight);
            return new Rect(x, y, width, ChooserWindowHeight);
        }

        private static WorkTabLayoutColumn? FindChooserSourceColumn(IWorkTabLayoutController layout, WorkTypeDef workType)
        {
            if (layout?.Columns == null || workType == null)
            {
                return null;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!column.IsExpandBesideChild && column.Column?.workType == workType)
                {
                    return column;
                }
            }

            return null;
        }

        private static ChooserComparisonGeometry BuildChooserComparisonGeometry(Rect contentRect)
        {
            const float panelGap = 14f;
            Rect panelRect = new Rect(contentRect.x, contentRect.y, contentRect.width, ChooserPanelHeight);
            float choiceWidth = Mathf.Max(160f, (panelRect.width - panelGap) * 0.5f);
            Rect focusRegion = new Rect(panelRect.xMin, panelRect.yMin, choiceWidth, panelRect.height);
            Rect expandRegion = new Rect(focusRegion.xMax + panelGap, panelRect.yMin, choiceWidth, panelRect.height);
            Rect rememberRegion = new Rect(
                panelRect.xMin + 12f,
                panelRect.yMax + 4f,
                Mathf.Max(1f, panelRect.width - 24f),
                ChooserRememberRowHeight);

            return new ChooserComparisonGeometry(
                focusRegion,
                expandRegion,
                rememberRegion);
        }

        private static void DrawChooserChoiceFrame(Rect region, string title, string description)
        {
            Widgets.DrawBoxSolid(region, new Color(0.03f, 0.04f, 0.045f, 1f));
            Widgets.DrawBoxSolid(new Rect(region.xMin, region.yMin, region.width, 2f), new Color(1f, 0.82f, 0.22f, 0.68f));
            Widgets.DrawBoxSolid(new Rect(region.xMin, region.yMax - 2f, region.width, 2f), new Color(1f, 0.82f, 0.22f, 0.38f));
            Widgets.DrawBox(region);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = false;
            GUI.color = Color.white;
            Rect titleRect = new Rect(region.xMin + 14f, region.yMin + 8f, region.width - 28f, 22f);
            Widgets.Label(titleRect, title);

            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            GUI.color = new Color(1f, 1f, 1f, 0.72f);
            Widgets.Label(new Rect(region.xMin + 14f, region.yMin + 28f, region.width - 28f, 32f), description);
            Text.WordWrap = false;
            GUI.color = Color.white;
        }

        private readonly struct ChooserComparisonGeometry
        {
            internal ChooserComparisonGeometry(
                Rect focusRegion,
                Rect expandRegion,
                Rect rememberRegion)
            {
                FocusRegion = focusRegion;
                ExpandRegion = expandRegion;
                RememberRegion = rememberRegion;
            }

            internal Rect FocusRegion { get; }
            internal Rect ExpandRegion { get; }
            internal Rect RememberRegion { get; }
        }
    }
}
