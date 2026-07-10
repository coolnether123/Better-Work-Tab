using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Headers.Vanilla;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Calculates the extra header ceiling needed by the full-tab sub-work drilldown.
    /// The body rows stay anchored; only the header area is allowed to grow upward.
    /// </summary>
    internal static class SubWorkDrilldownHeaderGeometry
    {
        private static float _lastNormalHeaderHeight;
        private static string _cachedExpansionWorkTypeDefName;
        private static float _cachedFullExpansion = -1f;

        internal static void RecordNormalHeaderHeight(PawnTable table, float currentHeaderHeight)
        {
            if (SubWorkDrilldownState.HasAnyDrilldown)
            {
                return;
            }

            float candidate = currentHeaderHeight > 0f
                ? currentHeaderHeight
                : table?.cachedHeaderHeight ?? 0f;
            if (candidate > 0f)
            {
                _lastNormalHeaderHeight = candidate;
            }
        }

        internal static float GetBaseHeaderDrawWidth(PawnTable table, float fallback)
        {
            if (SubWorkDrilldownState.IsActive && SubWorkDrilldownState.BaseHeaderDrawWidth > 0f)
            {
                return SubWorkDrilldownState.BaseHeaderDrawWidth;
            }

            // Expand-beside may increase the header ceiling for longer child labels. The
            // angled label's horizontal draw width must remain at its pre-expansion value,
            // otherwise half of every height increase shifts the label start to the left.
            if (SubWorkDrilldownState.HasAnyDrilldown && _lastNormalHeaderHeight > 0f)
            {
                return _lastNormalHeaderHeight;
            }

            if (fallback > 0f)
            {
                return fallback;
            }

            if (_lastNormalHeaderHeight > 0f)
            {
                return _lastNormalHeaderHeight;
            }

            return table?.cachedHeaderHeight ?? 0f;
        }

        internal static float GetEffectiveHeaderHeight(PawnTable table)
        {
            float baseHeight = GetNormalHeaderHeight(table);
            return baseHeight + GetHeaderHeightExpansion(table);
        }

        internal static float GetHeaderHeightExpansion(PawnTable table)
        {
            if (!SubWorkDrilldownState.HasAnyDrilldown || table == null)
            {
                _cachedExpansionWorkTypeDefName = null;
                _cachedFullExpansion = -1f;
                return 0f;
            }

            if (SubWorkDrilldownState.IsActive && SubWorkDrilldownState.UseClassicTransition)
            {
                _cachedExpansionWorkTypeDefName = null;
                _cachedFullExpansion = -1f;
                return 0f;
            }

            string activeDefName = SubWorkDrilldownState.IsActive
                ? SubWorkDrilldownState.ActiveWorkType?.defName ?? string.Empty
                : "expand-beside:" + SubWorkDrilldownState.MeasurementSignature;
            if (_cachedExpansionWorkTypeDefName != activeDefName || _cachedFullExpansion < 0f)
            {
                if (SubWorkDrilldownState.IsActive && SubWorkDrilldownState.IsExiting)
                {
                    _cachedFullExpansion = 0f;
                    _cachedExpansionWorkTypeDefName = activeDefName;
                    return 0f;
                }

                float required = GetRequiredHeaderHeight(table);
                _cachedFullExpansion = Mathf.Max(0f, Mathf.Ceil(required - GetNormalHeaderHeight(table)));
                _cachedExpansionWorkTypeDefName = activeDefName;
            }

            float progress = SubWorkDrilldownState.IsActive
                ? SubWorkDrilldownState.ModeVisualProgress
                : SubWorkDrilldownState.ExpandBesideHeaderExpansionProgress;
            return _cachedFullExpansion * Mathf.Clamp01(progress);
        }

        private static float GetNormalHeaderHeight(PawnTable table)
        {
            if (_lastNormalHeaderHeight > 0f)
            {
                return _lastNormalHeaderHeight;
            }

            return table?.cachedHeaderHeight ?? 0f;
        }

        private static float GetRequiredHeaderHeight(PawnTable table)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.enableAngledHeaders)
            {
                var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                int maxLevel = solver?.GetMaxLevelUsed() ?? 1;
                if (maxLevel <= 1)
                {
                    return table.cachedHeaderHeight;
                }

                return Mathf.Max(table.cachedHeaderHeight, VanillaHeaderMetrics.GetRequiredHeaderHeight(maxLevel));
            }

            float needed = AngledLabelDrawer.GetNeededHeight(table);
            if (SubWorkDrilldownState.IsExpandBesideActive)
            {
                needed = Mathf.Max(needed, GetExpandBesideAngledHeaderNeededHeight());
            }

            int padding = HeaderUtility.IsAnyCJKVertical(table) ? 2 : 10;
            return Mathf.Ceil(needed + padding);
        }

        private static float GetExpandBesideAngledHeaderNeededHeight()
        {
            float maxHeight = 0f;
            float rotation = AngledLabelDrawer.CurrentRotation;
            float absSin = Mathf.Abs(Mathf.Sin(rotation * Mathf.Deg2Rad));
            float absCos = Mathf.Abs(Mathf.Cos(rotation * Mathf.Deg2Rad));

            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                foreach (WorkTypeDef workType in SubWorkDrilldownState.ExpandBesideWorkTypes)
                {
                    var workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
                    for (int i = 0; i < workGivers.Count; i++)
                    {
                        WorkGiverDef workGiverDef = workGivers[i]?.def;
                        if (workGiverDef == null)
                        {
                            continue;
                        }

                        string label = WorkGiverDisplayNameService.HeaderLabel(
                            workGiverDef,
                            WorkGiverHeaderLabelStyle.Standard);
                        if (label.NullOrEmpty())
                        {
                            continue;
                        }

                        Vector2 size = Text.CalcSize(label);
                        float height = (size.x * absSin) + (size.y * absCos);
                        maxHeight = Mathf.Max(maxHeight, height);
                    }
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }

            return maxHeight + AngledLabelDrawer.STEM_BOTTOM_GAP;
        }
    }
}
