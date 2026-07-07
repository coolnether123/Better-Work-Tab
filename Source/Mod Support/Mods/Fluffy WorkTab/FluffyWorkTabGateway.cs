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
        private const float ChooserButtonHeight = 30f;
        private const float ChooserButtonWidth = 156f;
        private static bool _chooserActive;
        private static WorkTypeDef _chooserWorkType;
        private static Rect _chooserSourceRect;
        private static Rect _focusChoiceRegionRect;
        private static Rect _expandChoiceRegionRect;
        private static Rect _focusChoiceButtonRect;
        private static Rect _expandChoiceButtonRect;
        internal static bool DebugForceSubWorkStyleChooserAvailable;
        internal static BetterWorkTabSettings.SubWorkDrilldownStyle DebugForcedSubWorkStyleChooserHover =
            BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;

        internal static bool IsPresent => FluffyWorkTabCoexistence.IsFluffyWorkTabPresent;

        internal static string DetectedPackageId => FluffyWorkTabCoexistence.DetectedPackageId;

        internal static bool BetterWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.BetterWorkTabOwnsWorkTab;

        internal static bool ExternalWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.FluffyOwnsWorkTab;

        internal static bool ShouldRunBetterWorkTabFeatures => FluffyWorkTabCoexistence.ShouldRunBetterWorkTabFeatures;

        internal static bool HasRightExpandingDrilldown => IsPresent;

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
            if ((!IsPresent && !DebugForceSubWorkStyleChooserAvailable) ||
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
                "Expand beside - like Work Tab (Fluffy's)",
                DebugForcedSubWorkStyleChooserHover == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside);
            DrawSubWorkStyleChooserNote(inRect);
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
    }
}
