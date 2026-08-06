using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using RimWorld;
using Spine.DragDropApi;
using Spine.DragDropApi.Util;
using UnityEngine;
using Verse;
using Verse.Sound;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2MasterDetailView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private readonly RuleBuilder2Layout layout;
        private readonly RuleBuilder2EditorView editorView;
        private readonly DragDropController<RuleBuilder2Card> ruleDragController;
        private readonly ClickOrDragGate<RuleBuilder2Card> ruleClickGate = new ClickOrDragGate<RuleBuilder2Card>();
        private Vector2 cardListScroll;
        private Rect ruleListRect;
        private List<RuleBuilder2Card> currentRuleList = new List<RuleBuilder2Card>();
        private RuleBuilder2Card pendingRuleDrag;
        private const float RuleDragThreshold = 5f;

        internal RuleBuilder2MasterDetailView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout,
            RuleBuilder2EditorView editorView)
        {
            this.window = window;
            this.flow = flow;
            this.layout = layout;
            this.editorView = editorView;
            ruleDragController = new DragDropController<RuleBuilder2Card>(CalculateRuleTargetIndex);
        }

        internal void DrawMasterDetail(Rect rect)
        {
            RuleBuilder2MasterDetailRects panes = layout.MasterDetail(rect);
            DrawLeftPane(panes.Left);
            editorView.DrawEditor(panes.Right);
        }

        internal void DrawLeftPane(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            RuleBuilder2LeftPaneRects pane = layout.LeftPane(rect);

            string suggestLabel = GetGenerateDraftButtonLabel(pane.GenerateDraft);
            if (Widgets.ButtonText(pane.GenerateDraft, suggestLabel))
            {
                RequestGenerateDraft();
            }
            TooltipHandler.TipRegion(pane.GenerateDraft, T("BWT_RuleBuilder2_GenerateDraft_Tooltip"));

            DrawRuleList(pane.List);

            if (flow.DraftQueue != null && flow.DraftQueue.Count > 0)
            {
                if (Widgets.ButtonText(pane.Review, T("BWT_RuleBuilder2_ResumeDrafts").Formatted(flow.DraftQueue.Count).ToString()))
                {
                    flow.ActiveCard = flow.GetNextGeneratedSuggestion();
                    window.ShowGeneratedReviewSurface();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }

            DrawAddRuleControl(pane.AddRule);
        }

        private void DrawAddRuleControl(Rect rect)
        {
            Rect primary = new Rect(rect.x, rect.y, Mathf.Max(1f, rect.width - 34f), rect.height);

            Rect menu = new Rect(primary.xMax + 4f, rect.y, 30f, rect.height);
            if (Widgets.ButtonText(primary, "+ " + T("BWT_RuleBuilder2_AddRule")))
            {
                AddBlankRule();
            }
            TooltipHandler.TipRegion(primary, T("BWT_RuleBuilder2_AddRule_Tooltip"));

            if (Widgets.ButtonText(menu, "v"))
            {
                ShowAddRuleMenu();
            }
            TooltipHandler.TipRegion(menu, T("BWT_RuleBuilder2_AddRuleMenu_Tooltip"));
        }

        private void ShowAddRuleMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(T("BWT_RuleBuilder2_AddBlankRule"), AddBlankRule),
                new FloatMenuOption(T("BWT_RuleBuilder2_CopyRulesFromRuleset"), () =>
                {
                    Find.WindowStack.Add(new Window_RuleBuilder2RulePicker(
                        T("BWT_RuleBuilder2_CopyRulesFromRuleset"),
                        flow.BuildRuleBuilder2PickerSources(),
                        AddPickedRules));
                }),
                new FloatMenuOption(T("BWT_RuleBuilder2_CopyRulesFromClassic"), () =>
                {
                    Find.WindowStack.Add(new Window_RuleBuilder2RulePicker(
                        T("BWT_RuleBuilder2_CopyRulesFromClassic"),
                        flow.BuildClassicPickerSources(),
                        AddPickedRules));
                })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void AddPickedRules(List<RuleBuilder2Card> cards)
        {
            if (flow.AddCopiedRules(cards).Count == 0)
            {
                return;
            }

            editorView.ResetEditorScroll();
            window.ShowMainSurface();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private void AddBlankRule()
        {
            flow.AddRule();
            editorView.ResetEditorScroll();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private void RequestGenerateDraft()
        {
            if (Dialog_RuleBuilder2SuggestRulesConfirmation.HasConfirmedThisSession)
            {
                GenerateDraft();
                return;
            }

            Find.WindowStack.Add(new Dialog_RuleBuilder2SuggestRulesConfirmation(GenerateDraft));
        }

        private void GenerateDraft()
        {
            flow.GenerateDraftFromCurrentWorkTab();
            editorView.ResetEditorScroll();
            window.ShowGeneratedReviewSurface();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static string GetGenerateDraftButtonLabel(Rect rect)
        {
            string full = T("BWT_RuleBuilder2_GenerateDraftFull");
            return Text.CalcSize(full).x <= rect.width - 12f
                ? full
                : T("BWT_RuleBuilder2_GenerateDraft");
        }

        private void DrawRuleList(Rect rect)
        {
            List<RuleBuilder2Card> cards = flow.GetVisibleCards();
            currentRuleList = cards;
            ruleListRect = rect;
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, cards.Count * layout.Metrics.RuleRowStride + 4f));
            Widgets.BeginScrollView(rect, ref cardListScroll, view);
            if (cards.Count == 0)
            {
                GUI.color = Color.gray;
                DrawSafeLabel(new Rect(8f, 8f, view.width - 16f, 48f), T("BWT_RuleBuilder2_LeftPaneEmptyHint"));
                GUI.color = Color.white;
                Widgets.EndScrollView();
                return;
            }

            float y = 0f;
            foreach (RuleBuilder2Card card in cards)
            {
                Rect row = new Rect(0f, y, view.width, layout.Metrics.RuleRowHeight);
                DrawRuleRow(row, card);
                y += layout.Metrics.RuleRowStride;
            }

            Widgets.EndScrollView();
            HandleRuleListInput(rect, cards);
            DrawRuleDragOverlay(rect);
        }

        private void DrawRuleRow(Rect rect, RuleBuilder2Card card)
        {
            RuleBuilder2RuleListRowRects row = layout.RuleListRow(rect);
            bool selected = card == flow.ActiveCard;
            bool dragging = ruleDragController.IsActive && ruleDragController.CurrentSession?.DraggedItem == card;
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.22f, 0.27f, 0.2f, 0.95f) : new Color(0.13f, 0.13f, 0.13f, 0.95f));
            if (dragging)
            {
                Widgets.DrawBoxSolid(rect.ContractedBy(1f), new Color(0f, 0f, 0f, 0.28f));
            }
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            Widgets.DrawBox(rect, selected ? 2 : 1);

            Widgets.Checkbox(row.Enabled.position, ref card.Enabled);
            TooltipHandler.TipRegion(row.Enabled, T("BWT_RuleBuilder2_RowEnabled_Tooltip"));
            Text.Font = GameFont.Small;
            DrawFittedLabel(row.Target, BuildTargetSummary(flow, window.ActiveSurface, card));
            GUI.color = Color.gray;
            DrawFittedLabel(row.Summary, BuildConditionsSummary(card) + " - " + BuildActionSummary(card));
            GUI.color = Color.white;
            DrawBadges(row.Badges, card);

            TooltipHandler.TipRegion(row.Select, T("BWT_RuleBuilder2_RowOpen_Tooltip"));
        }

        private int CalculateRuleTargetIndex(Vector2 mousePos)
        {
            float biasedY = mousePos.y + (layout.Metrics.RuleRowStride * 0.5f);
            return ListDragCalculator.CalculateInsertionIndex(
                currentRuleList.Count,
                biasedY,
                ruleListRect.y,
                cardListScroll.y,
                _ => layout.Metrics.RuleRowStride);
        }

        private void HandleRuleListInput(Rect listRect, List<RuleBuilder2Card> cards)
        {
            Event evt = Event.current;
            if (evt == null || cards == null || cards.Count == 0)
            {
                return;
            }

            if (ruleDragController.IsActive)
            {
                ListDragSession<RuleBuilder2Card> session = ruleDragController.CurrentSession;
                ruleDragController.UpdateDrag(evt.mousePosition);
                ruleDragController.ApplyAutoScroll(
                    ref cardListScroll,
                    evt.mousePosition,
                    listRect,
                    cards.Count * layout.Metrics.RuleRowStride,
                    Time.deltaTime);

                if (evt.type == EventType.MouseUp ||
                    evt.rawType == EventType.MouseUp ||
                    !UnityEngine.Input.GetMouseButton(0))
                {
                    ruleClickGate.ClearIfTracking(session?.DraggedItem);
                    FinalizeRuleDrop(cards);
                    if (evt.type == EventType.MouseUp)
                    {
                        evt.Use();
                    }
                }

                return;
            }

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && listRect.Contains(evt.mousePosition))
                    {
                        pendingRuleDrag = GetRuleAtMouse(cards, listRect, evt.mousePosition, out Rect rowRect);
                        if (pendingRuleDrag != null && !IsMouseOverRowEnabled(rowRect, evt.mousePosition))
                        {
                            ruleClickGate.Begin(pendingRuleDrag, evt.button, evt.mousePosition);
                            evt.Use();
                        }
                        else
                        {
                            pendingRuleDrag = null;
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (pendingRuleDrag != null &&
                        ruleClickGate.RegisterDrag(pendingRuleDrag, evt.mousePosition, RuleDragThreshold))
                    {
                        int sourceIndex = cards.IndexOf(pendingRuleDrag);
                        if (sourceIndex >= 0 &&
                            ruleDragController.TryStartDrag(pendingRuleDrag, sourceIndex, cards.Count, evt.mousePosition))
                        {
                            ruleClickGate.MarkDragStarted(pendingRuleDrag);
                        }

                        evt.Use();
                        pendingRuleDrag = null;
                    }
                    else if (pendingRuleDrag != null)
                    {
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (pendingRuleDrag != null &&
                        ruleClickGate.TryComplete(pendingRuleDrag, evt.button, listRect.Contains(evt.mousePosition)))
                    {
                        flow.ActiveCard = pendingRuleDrag;
                        editorView.ResetEditorScroll();
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        evt.Use();
                    }
                    else if (pendingRuleDrag != null)
                    {
                        ruleClickGate.ClearIfTracking(pendingRuleDrag);
                        evt.Use();
                    }

                    pendingRuleDrag = null;
                    break;
            }
        }

        private RuleBuilder2Card GetRuleAtMouse(List<RuleBuilder2Card> cards, Rect listRect, Vector2 mousePosition, out Rect rowRect)
        {
            float localY = mousePosition.y - listRect.y + cardListScroll.y;
            int index = Mathf.FloorToInt(localY / layout.Metrics.RuleRowStride);
            rowRect = new Rect(0f, index * layout.Metrics.RuleRowStride, listRect.width - 16f, layout.Metrics.RuleRowHeight);
            return index >= 0 && index < cards.Count && rowRect.Contains(new Vector2(mousePosition.x - listRect.x, localY))
                ? cards[index]
                : null;
        }

        private bool IsMouseOverRowEnabled(Rect rowRect, Vector2 mousePosition)
        {
            float localX = mousePosition.x - ruleListRect.x;
            float localY = mousePosition.y - ruleListRect.y + cardListScroll.y;
            return layout.RuleListRow(rowRect).Enabled.Contains(new Vector2(localX, localY));
        }

        private void DrawRuleDragOverlay(Rect listRect)
        {
            if (!ruleDragController.IsActive)
            {
                return;
            }

            ListDragSession<RuleBuilder2Card> session = ruleDragController.CurrentSession;
            if (session == null)
            {
                return;
            }

            float lineY = ListDragVisuals.GetInsertionLineY(
                session.TargetIndex,
                Enumerable.Repeat(layout.Metrics.RuleRowStride, currentRuleList.Count).ToList(),
                listRect.y,
                cardListScroll.y);
            if (lineY >= listRect.y && lineY <= listRect.yMax)
            {
                ListDragVisuals.DrawInsertionLine(listRect.x, lineY, listRect.width);
            }
        }

        private void FinalizeRuleDrop(List<RuleBuilder2Card> cards)
        {
            DragEndReason reason = ruleDragController.FinalizeDrag(cards);
            if (reason != DragEndReason.Success)
            {
                return;
            }

            flow.ReorderVisibleCards(cards);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private static void DrawBadges(Rect rect, RuleBuilder2Card card)
        {
            float x = rect.xMax;
            if (!card.IsConfirmed)
            {
                x = DrawBadge(new Rect(x - 44f, rect.y, 44f, rect.height), "draft");
            }

            if (card.Action?.Kind == RuleBuilder2ActionKind.SetTimeSchedule ||
                card.Action?.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule)
            {
                x = DrawBadge(new Rect(x - 36f, rect.y, 36f, rect.height), "plan");
            }

            if (card.Target?.IsSubWorkTarget == true)
            {
                DrawBadge(new Rect(x - 32f, rect.y, 32f, rect.height), "job");
            }
        }

        private static float DrawBadge(Rect rect, string label)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f, 0.92f));
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.MiddleCenter;
            DrawFittedLabel(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return rect.x - 4f;
        }
    }
}
