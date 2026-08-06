using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using UnityEngine;
using Verse;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2GeneratedDraftView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private readonly RuleBuilder2Layout layout;
        private readonly RuleBuilder2EditorView editorView;
        private const int ReadOnlySentenceMaxLines = 3;
        private const float ReadOnlySentenceVerticalPadding = 6f;

        internal RuleBuilder2GeneratedDraftView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout,
            RuleBuilder2EditorView editorView)
        {
            this.window = window;
            this.flow = flow;
            this.layout = layout;
            this.editorView = editorView;
        }

        internal void DrawGeneratedDraftQueue(Rect rect)
        {
            RuleBuilder2DraftQueueRects queue = layout.DraftQueue(rect);
            Widgets.DrawMenuSection(rect);
            List<RuleBuilder2Card> draftCards = flow.DraftQueue?
                .Where(c => c != null && c.Target?.HasTarget == true && !c.IsConfirmed)
                .OrderBy(c => c.SortOrder)
                .ToList() ?? new List<RuleBuilder2Card>();
            if (draftCards.Count == 0)
            {
                window.ShowMainSurface();
                return;
            }

            RuleBuilder2Card card = flow.ActiveCard != null && draftCards.Contains(flow.ActiveCard)
                ? flow.ActiveCard
                : draftCards[0];
            flow.ActiveCard = card;
            int total = draftCards.Count;
            int index = Mathf.Max(1, draftCards.IndexOf(card) + 1);
            Text.Font = GameFont.Medium;
            Rect back = new Rect(queue.Title.xMax - 130f, queue.Title.y, 130f, 28f);
            Rect title = new Rect(queue.Title.x, queue.Title.y, Mathf.Max(0f, back.x - queue.Title.x - 8f), queue.Title.height);
            DrawFittedLabel(title, T("BWT_RuleBuilder2_DraftQueueTitle").Formatted(index, total).ToString());
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            DrawFittedLabel(queue.Subtitle, T("BWT_RuleBuilder2_DraftQueueSubtitle"));
            GUI.color = Color.white;
            if (Widgets.ButtonText(back, T("BWT_RuleBuilder2_BackToRules")))
            {
                window.ShowMainSurface();
            }

            DrawDraftMiniList(queue.Side, card);
            DrawGeneratedDraftPanel(queue.Main, card);
            window.TutorialRects[RuleBuilder2TutorialStep.RuleDeck] = SuggestionsHintRect(queue.Main);
        }

        internal void DrawDraftMiniList(Rect rect, RuleBuilder2Card selected)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.65f));
            float y = rect.y + 6f;
            foreach (RuleBuilder2Card card in flow.DraftQueue.Where(c => c != null && c.Target?.HasTarget == true).Take(16))
            {
                Rect row = new Rect(rect.x + 6f, y, rect.width - 12f, 26f);
                Widgets.DrawBoxSolid(row, card == selected ? new Color(0.24f, 0.28f, 0.22f, 1f) : new Color(0.15f, 0.15f, 0.15f, 0.55f));
                if (card == selected)
                {
                    Widgets.DrawBox(row, 1);
                }
                if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }
                DrawFittedLabel(new Rect(row.x + 6f, row.y + 2f, row.width - 12f, 24f), card.Name);
                if (Widgets.ButtonInvisible(row))
                {
                    flow.ActiveCard = card;
                }
                TooltipHandler.TipRegion(row, T("BWT_RuleBuilder2_DraftRow_Tooltip"));
                y += 30f;
                if (y > rect.yMax - 28f)
                {
                    break;
                }
            }
        }

        internal void DrawGeneratedDraftPanel(Rect rect, RuleBuilder2Card card)
        {
            string sentenceText = BuildRuleSentenceSummary(flow, window.ActiveSurface, card);
            float sentenceHeight = GetReadOnlySentenceBoxHeight(sentenceText, rect.ContractedBy(12f).width - 16f);
            RuleBuilder2GeneratedDraftPanelRects panel = layout.GeneratedDraftPanel(rect, sentenceHeight);
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.95f));
            Widgets.DrawBox(rect, 1);

            Text.Font = GameFont.Medium;
            DrawFittedLabel(panel.Title, card.Name);
            Text.Font = GameFont.Small;

            GUI.color = Color.gray;
            DrawFittedLabel(panel.Notes, card.Notes.NullOrEmpty() ? T("BWT_RuleBuilder2_DraftNoReason") : card.Notes);
            GUI.color = Color.white;

            DrawReadOnlyRuleSentence(panel.Sentence, sentenceText);
            DrawDraftRuleDetails(panel.Preview, card);

            if (Widgets.ButtonText(panel.Reject, T("BWT_RuleBuilder2_RejectDraft")))
            {
                flow.DiscardGeneratedSuggestion(card);
                ReturnToDashboardIfDraftQueueEmpty();
            }
            if (Widgets.ButtonText(panel.Edit, T("BWT_RuleBuilder2_EditDraft")))
            {
                flow.EditGeneratedSuggestion(card);
                editorView.ResetEditorScroll();
                window.ShowMainSurface();
            }
            if (Widgets.ButtonText(panel.Accept, T("BWT_RuleBuilder2_AcceptDraft")))
            {
                flow.KeepGeneratedSuggestion(card);
                ReturnToDashboardIfDraftQueueEmpty();
            }
        }

        internal void DrawDraftRuleDetails(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.09f, 0.09f, 0.5f));

            float y = rect.y + 8f;
            float rowWidth = Mathf.Max(1f, rect.width - 16f);
            DrawDraftDetailRow(new Rect(rect.x + 8f, y, rowWidth, 30f), T("BWT_RuleBuilder2_TableTarget"), BuildTargetSummary(flow, window.ActiveSurface, card), false);
            y += 34f;
            DrawDraftDetailRow(new Rect(rect.x + 8f, y, rowWidth, 30f), T("BWT_RuleBuilder2_TableConditions"), BuildConditionsSummary(card), true);
            y += 34f;
            DrawDraftDetailRow(new Rect(rect.x + 8f, y, rowWidth, 30f), T("BWT_RuleBuilder2_TableAction"), BuildActionSummary(card), false);

            float previewWidth = Mathf.Min(118f, Mathf.Max(1f, rect.width - 16f));
            Rect previewButton = new Rect(rect.xMax - previewWidth - 8f, rect.yMax - 36f, previewWidth, 28f);
            if (Widgets.ButtonText(previewButton, T("BWT_RuleBuilder2_RunPreview")))
            {
                flow.EditGeneratedSuggestion(card);
                editorView.MapCheckExpanded = true;
                flow.RunPreview();
                window.ShowMainSurface();
            }
            TooltipHandler.TipRegion(previewButton, T("BWT_RuleBuilder2_RunPreview_Tooltip"));
        }

        private void ReturnToDashboardIfDraftQueueEmpty()
        {
            if (flow.GetNextGeneratedSuggestion() == null)
            {
                window.ShowMainSurface();
            }
        }

        private void DrawReadOnlyRuleSentence(Rect rect, string sentenceText)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.55f));
            GUI.color = Color.gray;
            bool sentenceClamped;
            float sentenceHeight = GetReadOnlySentenceTextHeight(sentenceText, rect.width - 16f, out sentenceClamped);
            Rect label = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, sentenceHeight);
            DrawWrappedLabel(label, sentenceText);
            GUI.color = Color.white;
            if (sentenceClamped)
            {
                TooltipHandler.TipRegion(rect, sentenceText);
            }
        }

        private static float GetReadOnlySentenceBoxHeight(string sentenceText, float width)
        {
            bool unused;
            return GetReadOnlySentenceTextHeight(sentenceText, width, out unused) + ReadOnlySentenceVerticalPadding;
        }

        private static float GetReadOnlySentenceTextHeight(string sentenceText, float width, out bool clamped)
        {
            return ClampWrappedHeight(sentenceText, Mathf.Max(1f, width), ReadOnlySentenceMaxLines, out clamped);
        }

        internal static void DrawDraftDetailRow(Rect rect, string label, string value, bool alternate)
        {
            Widgets.DrawBoxSolid(rect, alternate ? new Color(0.14f, 0.14f, 0.14f, 0.55f) : new Color(0.11f, 0.11f, 0.11f, 0.35f));
            float labelWidth = Mathf.Min(110f, Mathf.Max(64f, rect.width * 0.32f));
            GUI.color = Color.gray;
            DrawFittedLabel(new Rect(rect.x + 6f, rect.y + 3f, labelWidth, 24f), label);
            GUI.color = Color.white;
            DrawFittedLabel(new Rect(rect.x + labelWidth + 14f, rect.y + 3f, Mathf.Max(1f, rect.width - labelWidth - 20f), 24f), value);
        }

        private static Rect SuggestionsHintRect(Rect rect)
        {
            float width = Mathf.Min(430f, Mathf.Max(300f, rect.width - 48f));
            float height = 170f;
            return new Rect(rect.center.x - width / 2f, rect.y + 118f, width, height);
        }
    }
}
