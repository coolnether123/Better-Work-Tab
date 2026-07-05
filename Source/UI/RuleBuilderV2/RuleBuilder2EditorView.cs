using Better_Work_Tab.Features.Rules.RuleBuilder2;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2EditorView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private readonly RuleBuilder2Layout layout;
        private readonly RuleBuilder2TargetPickerView targetPickerView;
        private readonly RuleBuilder2ConditionsView conditionsView;
        private readonly RuleBuilder2ActionScheduleView actionScheduleView;
        private readonly RuleBuilder2PreviewView previewView;
        private Vector2 editorScroll;
        private Vector2 editorWindowOffset;

        internal RuleBuilder2EditorView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout,
            RuleBuilder2TargetPickerView targetPickerView,
            RuleBuilder2ConditionsView conditionsView,
            RuleBuilder2ActionScheduleView actionScheduleView,
            RuleBuilder2PreviewView previewView)
        {
            this.window = window;
            this.flow = flow;
            this.layout = layout;
            this.targetPickerView = targetPickerView;
            this.conditionsView = conditionsView;
            this.actionScheduleView = actionScheduleView;
            this.previewView = previewView;
        }

        internal float EditorScrollY
        {
            set => editorScroll.y = value;
        }

        internal bool MapCheckExpanded { get; set; }

        internal void ResetEditorScroll()
        {
            editorScroll = Vector2.zero;
        }

        internal void DrawEditor(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            RuleBuilder2Card card = flow.ActiveCard;
            if (card == null || flow.Ruleset?.Cards?.Contains(card) != true)
            {
                DrawEmptyState(inner);
                return;
            }

            float contentHeight = GetEditorContentHeight(inner, card);
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, contentHeight));
            editorWindowOffset = new Vector2(inner.x - editorScroll.x, inner.y - editorScroll.y);
            Widgets.BeginScrollView(inner, ref editorScroll, view);

            float y = 0f;
            DrawRuleHeader(new Rect(0f, y, view.width, 58f), card);
            y += 66f;

            float targetHeight = card.Target.HasTarget ? layout.Metrics.TargetSelectedHeight : Mathf.Max(260f, inner.height - 120f);
            targetPickerView.DrawTargetSection(new Rect(0f, y, view.width, targetHeight), card);
            y += targetHeight + layout.Metrics.Gap;

            if (card.Target.HasTarget)
            {
                float conditionsHeight = GetConditionsHeight(card);
                conditionsView.DrawConditionsSection(new Rect(0f, y, view.width, conditionsHeight), card);
                y += conditionsHeight + layout.Metrics.Gap;

                float actionHeight = IsScheduleAction(card) ? 226f : 120f;
                actionScheduleView.DrawActionSection(new Rect(0f, y, view.width, actionHeight), card);
                y += actionHeight + layout.Metrics.Gap;

                float mapHeight = GetMapCheckHeight(card);
                DrawMapCheck(new Rect(0f, y, view.width, mapHeight), card);
                y += mapHeight + layout.Metrics.Gap;
            }

            DrawFooter(new Rect(0f, y, view.width, 46f), card);
            Widgets.EndScrollView();
        }

        internal Rect EditorRect(Rect rect)
        {
            return new Rect(
                editorWindowOffset.x + rect.x,
                editorWindowOffset.y + rect.y,
                rect.width,
                rect.height);
        }

        internal Rect EditorRect(Rect parent, Rect local)
        {
            return EditorRect(new Rect(
                parent.x + local.x,
                parent.y + local.y,
                local.width,
                local.height));
        }

        private void DrawEmptyState(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            DrawSafeLabel(new Rect(rect.x + 20f, rect.center.y - 36f, rect.width - 40f, 32f), T("BWT_RuleBuilder2_EmptyEditorHint"));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect add = new Rect(rect.center.x - 70f, rect.center.y + 4f, 140f, layout.Metrics.ButtonHeight);
            if (Widgets.ButtonText(add, "+ " + T("BWT_RuleBuilder2_AddRule")))
            {
                flow.AddRule();
                ResetEditorScroll();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(add, T("BWT_RuleBuilder2_AddRule_Tooltip"));
        }

        private void DrawRuleHeader(Rect rect, RuleBuilder2Card card)
        {
            Rect name = new Rect(rect.x, rect.y, Mathf.Min(360f, rect.width * 0.48f), 28f);
            Rect sentence = new Rect(rect.x, name.yMax + 6f, rect.width, 24f);
            card.Name = Widgets.TextField(name, card.Name ?? "");
            GUI.color = Color.gray;
            DrawFittedLabel(sentence, BuildRuleSentenceSummary(flow, window.ActiveSurface, card));
            GUI.color = Color.white;
        }

        private void DrawMapCheck(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.96f));
            Widgets.DrawBox(rect, 1);

            Rect title = new Rect(rect.x + 10f, rect.y + 6f, Mathf.Max(1f, rect.width - 146f), 26f);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.82f, 0.55f);
            DrawFittedLabel(title, T("BWT_RuleBuilder2_MapCheckBlockTitle"));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            Rect button = new Rect(rect.xMax - 126f, rect.y + 6f, 116f, 26f);
            if (Widgets.ButtonText(button, T("BWT_RuleBuilder2_RunPreview")))
            {
                MapCheckExpanded = true;
                flow.ShowPreview = true;
                flow.RefreshPreview();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(button, T("BWT_RuleBuilder2_RunPreview_Tooltip"));

            if (!MapCheckExpanded)
            {
                if (Widgets.ButtonInvisible(new Rect(rect.x, rect.y, Mathf.Max(0f, button.x - rect.x), rect.height)))
                {
                    MapCheckExpanded = true;
                    flow.ShowPreview = true;
                    flow.RefreshPreview();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }

                return;
            }

            Rect content = new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, Mathf.Max(0f, rect.height - 48f));
            previewView.DrawPreviewSection(content, card);
        }

        private void DrawFooter(Rect rect, RuleBuilder2Card card)
        {
            Rect delete = new Rect(rect.xMax - 110f, rect.y + 6f, 110f, 32f);
            if (!card.IsConfirmed)
            {
                Rect save = new Rect(rect.x, rect.y + 6f, 130f, 32f);
                if (Widgets.ButtonText(save, T("BWT_RuleBuilder2_SaveRule")))
                {
                    flow.ConfirmCard(card);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }

            if (Widgets.ButtonText(delete, T("BWT_RuleBuilder2_DeleteRule")))
            {
                flow.DeleteCard(card);
                ResetEditorScroll();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }

        private float GetEditorContentHeight(Rect inner, RuleBuilder2Card card)
        {
            float height = 66f;
            height += (card.Target.HasTarget ? layout.Metrics.TargetSelectedHeight : Mathf.Max(260f, inner.height - 120f)) + layout.Metrics.Gap;
            if (card.Target.HasTarget)
            {
                height += GetConditionsHeight(card) + layout.Metrics.Gap;
                height += (IsScheduleAction(card) ? 226f : 120f) + layout.Metrics.Gap;
                height += GetMapCheckHeight(card) + layout.Metrics.Gap;
            }

            return height + 56f;
        }

        private float GetConditionsHeight(RuleBuilder2Card card)
        {
            int count = card.Conditions?.Conditions?.Count ?? 0;
            float visibleRows = Mathf.Clamp(Mathf.Max(2, count), 2, 5);
            return 48f + visibleRows * layout.Metrics.ConditionRowStride;
        }

        private float GetMapCheckHeight(RuleBuilder2Card card)
        {
            if (!MapCheckExpanded)
            {
                return 42f;
            }

            int rows = flow.ShowPreview
                ? flow.Preview.Count(result => result.Target == card.Target)
                : 0;
            float visibleRows = Mathf.Clamp(Mathf.Max(2, rows), 2, 6);
            float matchedPanel = flow.ShowMatchedPanel && flow.SelectedWorkTabContext.HasValue ? 80f : 0f;
            return 48f + matchedPanel + visibleRows * 38f;
        }

        private static bool IsScheduleAction(RuleBuilder2Card card)
        {
            return card?.Action?.Kind == RuleBuilder2ActionKind.SetTimeSchedule ||
                   card?.Action?.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule;
        }
    }
}
