using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGiverReassignments;
using System.Collections.Generic;
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
        private const float ChooserPanelWidth = 230f;
        private const float ChooserPanelHeight = 190f;
        private const float ChooserGap = 18f;
        private const float ChooserButtonHeight = 30f;
        private static bool _chooserActive;
        private static WorkTypeDef _chooserWorkType;
        private static Rect _chooserSourceRect;
        private static Rect _focusChoiceButtonRect;
        private static Rect _expandChoiceButtonRect;

        internal static bool IsPresent => FluffyWorkTabCoexistence.IsFluffyWorkTabPresent;

        internal static string DetectedPackageId => FluffyWorkTabCoexistence.DetectedPackageId;

        internal static bool BetterWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.BetterWorkTabOwnsWorkTab;

        internal static bool ExternalWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.FluffyOwnsWorkTab;

        internal static bool ShouldRunBetterWorkTabFeatures => FluffyWorkTabCoexistence.ShouldRunBetterWorkTabFeatures;

        internal static bool HasRightExpandingDrilldown => IsPresent;

        internal static bool HasScheduleStrip => IsPresent;

        internal static bool HasIconSet => IsPresent;

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
            if (!IsPresent ||
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
                return false;
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

            Rect sourceRect = ResolveCurrentSourceRect(layout);
            Rect focusRect = new Rect(
                Mathf.Max(inRect.xMin + 8f, sourceRect.xMin - ChooserGap - ChooserPanelWidth),
                Mathf.Max(inRect.yMin + 34f, sourceRect.yMin + 10f),
                ChooserPanelWidth,
                ChooserPanelHeight);
            Rect expandRect = new Rect(
                Mathf.Min(inRect.xMax - ChooserPanelWidth - 8f, sourceRect.xMax + ChooserGap),
                focusRect.y,
                ChooserPanelWidth,
                ChooserPanelHeight);

            DrawChooserPanel(focusRect, "Focused view", "Better Work Tab", BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView);
            DrawChooserPanel(expandRect, "Expand beside", "Like Work Tab (Fluffy's)", BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside);

            _focusChoiceButtonRect = new Rect(focusRect.x + 12f, focusRect.yMax - ChooserButtonHeight - 12f, focusRect.width - 24f, ChooserButtonHeight);
            _expandChoiceButtonRect = new Rect(expandRect.x + 12f, expandRect.yMax - ChooserButtonHeight - 12f, expandRect.width - 24f, ChooserButtonHeight);

            Widgets.ButtonText(_focusChoiceButtonRect, "Use this view");
            Widgets.ButtonText(_expandChoiceButtonRect, "Use this view");

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(
                new Rect(focusRect.xMin, Mathf.Max(focusRect.yMax, expandRect.yMax) + 4f, expandRect.xMax - focusRect.xMin, 24f),
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
            _focusChoiceButtonRect = Rect.zero;
            _expandChoiceButtonRect = Rect.zero;
        }

        private static Rect ResolveCurrentSourceRect(IWorkTabLayoutController layout)
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

            Rect sourceRect = ResolveCurrentSourceRect(layout);
            float localCenter = table.scrollPosition.x + (sourceRect.center.x - layout.TableOrigin.x);
            float targetX = Mathf.Max(0f, localCenter - (table.Size.x * 0.5f));
            Vector2 scroll = table.scrollPosition;
            scroll.x = instant ? targetX : Mathf.Lerp(scroll.x, targetX, 0.18f);
            table.scrollPosition = scroll;
        }

        private static void DrawChooserPanel(
            Rect rect,
            string title,
            string subtitle,
            BetterWorkTabSettings.SubWorkDrilldownStyle style)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.08f, 0.09f, 0.92f));
            Widgets.DrawBox(rect);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 24f), title);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.72f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 30f, rect.width - 16f, 22f), subtitle);

            DrawChooserPreview(new Rect(rect.x + 12f, rect.y + 58f, rect.width - 24f, 76f), style);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawChooserPreview(Rect rect, BetterWorkTabSettings.SubWorkDrilldownStyle style)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.14f, 0.16f, 0.18f, 0.85f));
            Widgets.DrawBox(rect);

            IReadOnlyList<WorkGiver> givers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(_chooserWorkType);
            int count = Mathf.Min(givers.Count, style == BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView ? 4 : 3);
            float columnWidth = Mathf.Max(28f, rect.width / Mathf.Max(1, count + (style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside ? 1 : 0)));
            float x = rect.x + 4f;

            if (style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                DrawPreviewColumn(new Rect(x, rect.y + 5f, columnWidth, rect.height - 10f), WorkTypeDisplayNameService.HeaderLabel(_chooserWorkType), false);
                x += columnWidth;
            }

            for (int i = 0; i < count; i++)
            {
                WorkGiverDef def = givers[i]?.def;
                DrawPreviewColumn(
                    new Rect(x, rect.y + 5f, columnWidth, rect.height - 10f),
                    WorkGiverDisplayNameService.HeaderLabel(def),
                    true);
                x += columnWidth;
            }
        }

        private static void DrawPreviewColumn(Rect rect, string label, bool child)
        {
            Widgets.DrawBoxSolid(rect.ContractedBy(1f), child ? new Color(0.2f, 0.24f, 0.28f, 0.95f) : new Color(0.16f, 0.18f, 0.2f, 0.95f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = child ? new Color(0.86f, 0.92f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.9f);
            Widgets.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 28f), label);
            Rect boxRect = new Rect(rect.center.x - 10f, rect.yMax - 26f, 20f, 20f);
            Widgets.DrawBoxSolid(boxRect, new Color(0.1f, 0.12f, 0.13f, 1f));
            Widgets.DrawBox(boxRect);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
