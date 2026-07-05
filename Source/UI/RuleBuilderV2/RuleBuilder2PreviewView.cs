using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using UnityEngine;
using Verse;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2PreviewView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private Vector2 previewScroll;

        internal RuleBuilder2PreviewView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout,
            RuleBuilder2TutorialController tutorial)
        {
            this.window = window;
            this.flow = flow;
        }

        internal void DrawPreviewSection(Rect rect, RuleBuilder2Card card)
        {
            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            Rect matched = flow.ShowMatchedPanel && flow.SelectedWorkTabContext.HasValue
                ? new Rect(rect.x, rect.y, rect.width, 72f)
                : Rect.zero;
            Rect list = matched.height > 0f
                ? new Rect(rect.x, matched.yMax + 8f, rect.width, Mathf.Max(0f, rect.height - matched.height - 8f))
                : rect;

            if (flow.ShowMatchedPanel && flow.SelectedWorkTabContext.HasValue)
            {
                DrawMatchedPanel(matched, flow.SelectedWorkTabContext.Value);
            }

            if (!flow.ShowPreview)
            {
                GUI.color = Color.gray;
                DrawSafeLabel(list, T("BWT_RuleBuilder2_PreviewHint"));
                TooltipHandler.TipRegion(list, T("BWT_RuleBuilder2_PreviewHint_Tooltip"));
                GUI.color = Color.white;
                GUI.EndGroup();
                return;
            }

            DrawPreviewRows(list, card);
            GUI.EndGroup();
        }

        internal void DrawMatchedPanel(Rect rect, RuleBuilder2WorkTabSelection selection)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.16f, 0.18f, 0.95f));
            Widgets.DrawBox(rect, 1);
            string label = PawnCompat.LabelShortCap(selection.Pawn) ?? T("BWT_RuleBuilder2_PawnFallback");
            string target = BuildTargetLabel(selection.WorkType, selection.WorkGiver);
            DrawFittedLabel(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), T("BWT_RuleBuilder2_MatchedConditions") + ": " + label + " / " + target);

            var card = flow.ActiveCard;
            if (card != null && selection.Pawn != null)
            {
                var result = flow.PreviewPawn(card, selection.Pawn, selection.WorkType, selection.WorkGiver);
                string why = result.Matched
                    ? T("BWT_RuleBuilder2_AppliesNewPriority").Formatted(result.NewPriority).ToString()
                    : T("BWT_RuleBuilder2_DoesNotApplyFailed").Formatted(string.Join(", ", result.ConditionsFailed.Take(2).ToArray())).ToString();
                GUI.color = result.Matched ? Color.green : Color.yellow;
                DrawFittedLabel(new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, 32f), why);
                GUI.color = Color.white;
            }
        }

        internal void DrawPreviewRows(Rect rect, RuleBuilder2Card card)
        {
            flow.RefreshPreview();
            var rows = flow.Preview.Where(result => result.Target == card.Target).ToList();
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, rows.Count * 38f));
            Widgets.BeginScrollView(rect, ref previewScroll, view);
            float y = 0f;
            foreach (RuleBuilder2PreviewResult result in rows)
            {
                Rect row = new Rect(0f, y, view.width, 34f);
                Widgets.DrawBoxSolid(row, result.Matched ? new Color(0.12f, 0.22f, 0.12f, 0.95f) : new Color(0.16f, 0.14f, 0.12f, 0.95f));
                Widgets.DrawBox(row, 1);
                DrawFittedLabel(new Rect(row.x + 8f, row.y + 5f, 150f, 24f), result.PawnLabel);
                DrawFittedLabel(new Rect(row.x + 164f, row.y + 5f, 110f, 24f), result.Matched ? T("BWT_RuleBuilder2_Matched") : T("BWT_RuleBuilder2_NotMatched"));
                DrawFittedLabel(new Rect(row.x + 280f, row.y + 5f, 190f, 24f), T("BWT_RuleBuilder2_CurrentToNew").Formatted(result.CurrentPriority, result.NewPriority).ToString());
                GUI.color = Color.gray;
                DrawFittedLabel(new Rect(row.x + 476f, row.y + 5f, Mathf.Max(1f, row.width - 484f), 24f), result.Matched ? result.ActionText : string.Join(", ", result.ConditionsFailed.Take(2).ToArray()));
                GUI.color = Color.white;
                y += 38f;
            }
            Widgets.EndScrollView();
        }

        internal string BuildPreviewSummary(RuleBuilder2Card card)
        {
            if (!flow.ShowPreview)
            {
                return T("BWT_RuleBuilder2_PreviewNotRunSummary");
            }

            int matched = flow.Preview.Count(result => result.Target == card.Target && result.Matched);
            int total = flow.Preview.Count(result => result.Target == card.Target);
            return T("BWT_RuleBuilder2_PreviewSummary").Formatted(matched, total).ToString();
        }
    }
}
