using Better_Work_Tab.Features;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RWWidgets = Verse.Widgets;

namespace Better_Work_Tab.UI.RuleBuilder.Panels
{
    /// <summary>
    /// Draws a live preview of what the work tab would look like after applying the selected ruleset.
    ///
    /// Layout:
    ///   [Header: title | matched count | "Show All" toggle]
    ///   [Scrollable grid: pawn name + match dot | one cell per work type]
    ///
    /// Gold borders mark cells where the ruleset would change a pawn's current priority.
    /// A green dot on a pawn row means the ruleset assigns at least one non-zero priority to them.
    /// </summary>
    public class PreviewPanel
    {
        private readonly RulesetPreviewCalculator _calculator = new RulesetPreviewCalculator();

        private RulesetPreviewResult _cachedResult;
        private WorkAssignmentRuleset _cachedForRuleset;

        private bool _showAllWorkTypes = true;
        private Vector2 _scrollPosition;

        private const float HeaderBarHeight = 28f;
        private const float MatchDotSize = 8f;

        // Header row is tall enough for angled text at the current rotation setting.
        // Recomputed when the display work type list changes.
        private float _workTypeHeaderHeight = 80f;
        private List<WorkTypeDef> _lastHeaderWorkTypes;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Discard the cached preview result (call when rules or ruleset change).</summary>
        public void Invalidate()
        {
            _cachedResult = null;
            _cachedForRuleset = null;
        }

        public void Draw(Rect rect, RuleBuilderState state)
        {
            RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.PanelBackground);
            RWWidgets.DrawBox(rect, 1);

            Rect inner = rect.ContractedBy(4f);

            Rect headerRect = new Rect(inner.x, inner.y, inner.width, HeaderBarHeight);
            DrawHeader(headerRect, state);

            Rect gridRect = new Rect(inner.x, headerRect.yMax + 4f, inner.width, inner.yMax - headerRect.yMax - 4f);

            if (Find.CurrentMap == null)
            {
                DrawPlaceholder(gridRect, "BWT_PreviewNoMap".Translate());
                return;
            }

            EnsureCalculated(state.SelectedRuleset);

            if (!_cachedResult.HasData)
            {
                DrawPlaceholder(gridRect, "BWT_PreviewNoColonists".Translate());
                return;
            }

            var displayWorkTypes = GetDisplayWorkTypes(_cachedResult);

            if (displayWorkTypes.Count == 0)
            {
                DrawPlaceholder(gridRect, "BWT_PreviewNoChanges".Translate());
                return;
            }

            DrawGrid(gridRect, _cachedResult, displayWorkTypes);
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void DrawHeader(Rect rect, RuleBuilderState state)
        {
            Text.Font = GameFont.Small;

            // Title
            Rect titleRect = new Rect(rect.x, rect.y, 80f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(titleRect, "BWT_PreviewTitle".Translate());
            GUI.color = Color.white;

            // Matched colonist count
            if (_cachedResult != null && _cachedResult.HasData)
            {
                Rect matchRect = new Rect(rect.x + 90f, rect.y, 200f, rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = RuleBuilderConstants.SubtleTextColor;
                RWWidgets.Label(matchRect,
                    "BWT_PreviewMatched".Translate(_cachedResult.MatchedPawns.Count, _cachedResult.Pawns.Count));
                GUI.color = Color.white;
            }

            // "Show All Work Types" toggle (right-aligned)
            bool newToggle = _showAllWorkTypes;
            Rect toggleRect = new Rect(rect.xMax - 178f, rect.y + 4f, 174f, rect.height - 8f);
            RWWidgets.CheckboxLabeled(toggleRect, "BWT_PreviewShowAll".Translate(), ref newToggle);
            if (newToggle != _showAllWorkTypes)
                _showAllWorkTypes = newToggle;

            // Separator
            RWWidgets.DrawBoxSolid(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                RuleBuilderConstants.HeaderColor * 0.3f);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // ── Grid ──────────────────────────────────────────────────────────────

        private void DrawGrid(Rect rect, RulesetPreviewResult result, List<WorkTypeDef> workTypes)
        {
            float nameCol = RuleBuilderConstants.PreviewNameColumnWidth;
            float cell = RuleBuilderConstants.PreviewCellWidth;
            float rowH = RuleBuilderConstants.PreviewRowHeight;

            RefreshHeaderHeight(workTypes);

            float contentW = nameCol + cell * workTypes.Count + 2f;
            float contentH = _workTypeHeaderHeight + rowH * result.Pawns.Count + 2f;

            RWWidgets.BeginScrollView(rect, ref _scrollPosition, new Rect(0f, 0f, contentW, contentH));

            // Work type header with angled labels
            DrawWorkTypeHeader(new Rect(0f, 0f, contentW, _workTypeHeaderHeight), workTypes);

            // One row per pawn
            float y = _workTypeHeaderHeight;
            for (int i = 0; i < result.Pawns.Count; i++)
            {
                DrawPawnRow(
                    new Rect(0f, y, contentW, rowH),
                    result.Pawns[i],
                    result,
                    workTypes,
                    altRow: (i % 2) == 1);
                y += rowH;
            }

            RWWidgets.EndScrollView();
        }

        private void DrawWorkTypeHeader(Rect rect, List<WorkTypeDef> workTypes)
        {
            float nameCol = RuleBuilderConstants.PreviewNameColumnWidth;
            float cell = RuleBuilderConstants.PreviewCellWidth;

            // Background behind the pawn-name column
            RWWidgets.DrawBoxSolid(
                new Rect(rect.x, rect.y, nameCol, rect.height),
                new Color(0.14f, 0.14f, 0.14f, 0.9f));

            float x = nameCol;
            foreach (var wt in workTypes)
            {
                Rect cellRect = new Rect(x + 1f, rect.y + 1f, cell - 2f, rect.height - 2f);
                RWWidgets.DrawBoxSolid(cellRect, new Color(0.2f, 0.2f, 0.2f, 0.9f));

                string labelText = HeaderUtility.GetHeaderText(wt);
                bool isCJKVertical = BetterWorkTabMod.Settings.useVerticalStackingForCJK
                    && Mathf.Abs(BetterWorkTabMod.Settings.angledHeaderRotation + 90f) < 5f
                    && HeaderUtility.IsCJK(labelText);

                Text.Font = GameFont.Small;
                var layout = new AngledLabelDrawer.AngledLabelLayout(
                    text: labelText,
                    size: Text.CalcSize(labelText),
                    pivot: cellRect.center,
                    showMarker: false,
                    isCJKVertical: isCJKVertical);

                AngledLabelDrawer.Draw(layout, isMouseOver: Mouse.IsOver(cellRect),
                    isSorted: false, sortDescending: false, headerRect: cellRect, column: null);

                x += cell;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawPawnRow(
            Rect rect,
            Pawn pawn,
            RulesetPreviewResult result,
            List<WorkTypeDef> workTypes,
            bool altRow)
        {
            float nameCol = RuleBuilderConstants.PreviewNameColumnWidth;
            float cell = RuleBuilderConstants.PreviewCellWidth;
            bool isMatched = result.MatchedPawns.Contains(pawn);

            // Name cell background
            Color rowBg = altRow
                ? new Color(0.17f, 0.17f, 0.17f, 0.8f)
                : new Color(0.14f, 0.14f, 0.14f, 0.8f);
            RWWidgets.DrawBoxSolid(new Rect(rect.x, rect.y, nameCol, rect.height), rowBg);

            // Match dot
            float dotY = rect.y + (rect.height - MatchDotSize) / 2f;
            GUI.color = isMatched ? RuleBuilderConstants.SuccessColor : new Color(0.35f, 0.35f, 0.35f);
            RWWidgets.DrawBoxSolid(new Rect(rect.x + 4f, dotY, MatchDotSize, MatchDotSize), GUI.color);
            GUI.color = Color.white;

            // Pawn name
            Rect nameRect = new Rect(
                rect.x + MatchDotSize + 8f,
                rect.y,
                nameCol - MatchDotSize - 10f,
                rect.height);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = isMatched ? Color.white : RuleBuilderConstants.SubtleTextColor;
            RWWidgets.Label(nameRect, pawn.NameShortColored);
            GUI.color = Color.white;

            // Pawn row tooltip
            TooltipHandler.TipRegion(
                new Rect(rect.x, rect.y, nameCol, rect.height),
                isMatched
                    ? "BWT_PreviewPawnMatched".Translate(pawn.LabelShort)
                    : "BWT_PreviewPawnUnmatched".Translate(pawn.LabelShort));

            // Priority cells
            float x = nameCol;
            foreach (var wt in workTypes)
            {
                DrawPriorityCell(
                    new Rect(x + 1f, rect.y + 1f, cell - 2f, rect.height - 2f),
                    pawn, wt, result);
                x += cell;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static void DrawPriorityCell(Rect rect, Pawn pawn, WorkTypeDef wt, RulesetPreviewResult result)
        {
            bool isDisabled = pawn.WorkTypeIsDisabled(wt);
            int afterPriority = result.GetAfterPriority(pawn, wt);
            bool changed = result.HasChanged(pawn, wt);

            if (isDisabled)
            {
                RWWidgets.DrawBoxSolid(rect, new Color(0.11f, 0.11f, 0.11f, 0.8f));
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.28f, 0.28f, 0.28f);
                RWWidgets.Label(rect, "×");
            }
            else if (afterPriority > 0)
            {
                int ci = Mathf.Clamp(afterPriority, 0, RuleBuilderConstants.PriorityColors.Length - 1);
                Color fill = RuleBuilderConstants.PriorityColors[ci];
                fill.a = 0.55f;
                RWWidgets.DrawBoxSolid(rect, fill);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.white;
                RWWidgets.Label(rect, afterPriority.ToString());
            }
            else
            {
                RWWidgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.5f));
            }

            // Gold outline on changed cells
            if (changed)
            {
                GUI.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                RWWidgets.DrawBox(rect, 1);
            }

            // Cell tooltip
            if (!isDisabled)
            {
                int beforePriority = result.GetBeforePriority(pawn, wt);
                string tip = changed
                    ? "BWT_PreviewCellChanged".Translate(wt.labelShort, beforePriority, afterPriority)
                    : "BWT_PreviewCellUnchanged".Translate(wt.labelShort, afterPriority);
                TooltipHandler.TipRegion(rect, tip);
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void RefreshHeaderHeight(List<WorkTypeDef> workTypes)
        {
            if (workTypes == _lastHeaderWorkTypes) return;
            _lastHeaderWorkTypes = workTypes;

            float rotation = AngledLabelDrawer.CurrentRotation;
            float absSin = Mathf.Abs(Mathf.Sin(rotation * Mathf.Deg2Rad));
            float absCos = Mathf.Abs(Mathf.Cos(rotation * Mathf.Deg2Rad));
            float maxH = 22f; // minimum

            GameFont savedFont = Text.Font;
            Text.Font = GameFont.Small;

            foreach (var wt in workTypes)
            {
                string label = HeaderUtility.GetHeaderText(wt);
                bool isCJKVertical = BetterWorkTabMod.Settings.useVerticalStackingForCJK
                    && Mathf.Abs(BetterWorkTabMod.Settings.angledHeaderRotation + 90f) < 5f
                    && HeaderUtility.IsCJK(label);

                float h;
                if (isCJKVertical)
                    h = label.Length * Text.LineHeight * BetterWorkTabMod.Settings.cjkVerticalKerning;
                else
                {
                    Vector2 size = Text.CalcSize(label);
                    h = size.x * absSin + size.y * absCos;
                }
                if (h > maxH) maxH = h;
            }

            Text.Font = savedFont;
            _workTypeHeaderHeight = maxH + AngledLabelDrawer.STEM_BOTTOM_GAP + 4f;
        }

        private void EnsureCalculated(WorkAssignmentRuleset ruleset)
        {
            if (_cachedResult != null && _cachedForRuleset == ruleset)
                return;

            _cachedResult = _calculator.Calculate(ruleset);
            _cachedForRuleset = ruleset;
        }

        private List<WorkTypeDef> GetDisplayWorkTypes(RulesetPreviewResult result)
        {
            if (_showAllWorkTypes)
                return result.WorkTypes.ToList();

            // Default: only work types where at least one pawn's priority would change
            return result.WorkTypes
                .Where(wt => result.Pawns.Any(p => result.HasChanged(p, wt)))
                .ToList();
        }

        private static void DrawPlaceholder(Rect rect, string message)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            RWWidgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }
}
