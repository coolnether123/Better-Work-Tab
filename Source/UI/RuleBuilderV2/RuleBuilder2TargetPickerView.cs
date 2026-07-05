using System.Collections.Generic;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.UI.RuleBuilder;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2TargetPickerView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private readonly RuleBuilder2Layout layout;
        private string targetSearch = "";
        private Vector2 targetListScroll;
        private List<RuleBuilder2TargetCatalogEntry> targetCatalog;
        private readonly HashSet<string> expandedTargetWorkTypes = new HashSet<string>();

        internal RuleBuilder2TargetPickerView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout)
        {
            this.window = window;
            this.flow = flow;
            this.layout = layout;
        }

        internal void DrawTargetSection(Rect rect, RuleBuilder2Card card)
        {
            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            RuleBuilder2TargetSectionRects target = layout.TargetSection(rect, card.Target.HasTarget);

            if (card.Target.HasTarget)
            {
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.96f));
                Rect title = new Rect(target.Inner.x, target.Inner.y + 4f, Mathf.Max(1f, target.Summary.x - target.Inner.x - 6f), 24f);
                GUI.color = new Color(0.9f, 0.82f, 0.55f);
                DrawFittedLabel(title, T("BWT_RuleBuilder2_TargetBlockTitle"));
                GUI.color = Color.white;

                string label = BuildTargetLabel(card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
                if (label == T("BWT_RuleBuilder2_WorkFallback") && !card.Target.DisplayLabel.NullOrEmpty())
                {
                    label = card.Target.DisplayLabel;
                }
                Rect scope = new Rect(target.Summary.xMax - 112f, target.Summary.y + 1f, 104f, 24f);
                Rect labelRect = new Rect(target.Summary.x, target.Summary.y, Mathf.Max(1f, scope.x - target.Summary.x - 8f), target.Summary.height);
                if (Mouse.IsOver(labelRect))
                {
                    Widgets.DrawHighlight(labelRect);
                }
                DrawFittedLabel(labelRect, label);
                if (Better_Work_Tab.WidgetsCompat.ButtonInvisible(labelRect))
                {
                    if (RuleBuilderGateway.FlashRuleBuilder2Target(card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver()))
                    {
                        UISoundCompat.TickTiny.PlayOneShotOnCamera();
                    }
                }
                TooltipHandler.TipRegion(
                    labelRect,
                    RuleBuilderGateway.IsRuleBuilder2WorkTabOpen()
                        ? T("BWT_RuleBuilder2_TargetFlash_Tooltip").Formatted(label).ToString()
                        : T("BWT_RuleBuilder2_TargetFlashClosed_Tooltip"));
                GUI.color = card.Target.IsSubWorkTarget ? new Color(0.72f, 0.86f, 1f, 1f) : Color.gray;
                DrawFittedLabel(scope, card.Target.IsSubWorkTarget ? T("BWT_RuleBuilder2_TargetSpecificJob") : T("BWT_RuleBuilder2_TargetWholeWork"));
                GUI.color = Color.white;
                if (Better_Work_Tab.WidgetsCompat.ButtonText(target.Change, T("BWT_Change")))
                {
                    card.Target.WorkTypeDefName = "";
                    card.Target.WorkGiverDefName = "";
                    card.Target.DisplayLabel = "";
                    ResetTargetPickerState();
                }
                TooltipHandler.TipRegion(target.Change, T("BWT_RuleBuilder2_TargetChange_Tooltip"));
                GUI.EndGroup();
                return;
            }

            DrawSectionChrome(rect, T("BWT_RuleBuilder2_TargetBlockTitle"));
            string previousSearch = targetSearch ?? "";
            targetSearch = Widgets.TextField(target.Search, previousSearch);
            if (targetSearch != previousSearch)
            {
                targetListScroll = Vector2.zero;
            }
            DrawTargetList(target.List, card);
            GUI.EndGroup();
        }

        internal void DrawTargetList(Rect rect, RuleBuilder2Card card)
        {
            List<RuleBuilder2TargetCatalogEntry> entries = RuleBuilder2TargetCatalog.BuildVisible(
                GetTargetCatalog(),
                targetSearch,
                expandedTargetWorkTypes);
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, entries.Count * 34f + 8f));
            Widgets.BeginScrollView(rect, ref targetListScroll, view);

            if (entries.Count == 0)
            {
                GUI.color = Color.gray;
                DrawSafeLabel(new Rect(8f, 8f, view.width - 16f, 28f), T("BWT_RuleBuilder2_TargetNoMatches"));
                GUI.color = Color.white;
                Widgets.EndScrollView();
                return;
            }

            float y = 4f;
            foreach (RuleBuilder2TargetCatalogEntry entry in entries)
            {
                Rect row = new Rect(0f, y, view.width, 30f);
                DrawTargetCatalogRow(row, card, entry);
                y += 34f;
            }

            Widgets.EndScrollView();
        }

        internal List<RuleBuilder2TargetCatalogEntry> GetTargetCatalog()
        {
            targetCatalog ??= RuleBuilder2TargetCatalog.BuildAll();
            return targetCatalog;
        }

        internal void DrawTargetCatalogRow(Rect rect, RuleBuilder2Card card, RuleBuilder2TargetCatalogEntry entry)
        {
            bool selected = entry.Matches(card.Target);
            bool expanded = expandedTargetWorkTypes.Contains(entry.WorkTypeKey);
            Color fill = selected
                ? new Color(0.24f, 0.3f, 0.22f, 0.95f)
                : entry.IsSubWork
                    ? new Color(0.12f, 0.12f, 0.12f, 0.82f)
                    : new Color(0.16f, 0.15f, 0.13f, 0.9f);

            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, fill);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            Widgets.DrawBox(rect, selected ? 2 : 1);

            float indent = entry.IsSubWork ? 28f : 0f;
            Rect expandRect = new Rect(rect.x + 5f + indent, rect.y + 3f, 24f, 24f);
            Rect labelRect = new Rect(expandRect.xMax + 6f, rect.y + 3f, Mathf.Max(1f, rect.xMax - expandRect.xMax - 126f), 24f);
            Rect scopeRect = new Rect(rect.xMax - 112f, rect.y + 3f, 104f, 24f);

            if (!entry.IsSubWork && entry.HasSubWork)
            {
                if (Better_Work_Tab.WidgetsCompat.ButtonText(expandRect, expanded ? "-" : "+"))
                {
                    ToggleTargetExpanded(entry.WorkTypeKey);
                    UISoundCompat.TickTiny.PlayOneShotOnCamera();
                }
            }
            else
            {
                GUI.color = Color.gray;
                DrawFittedLabel(expandRect, entry.IsSubWork ? ">" : "");
                GUI.color = Color.white;
            }

            DrawFittedLabel(labelRect, entry.Label);
            GUI.color = entry.IsSubWork ? new Color(0.72f, 0.86f, 1f, 1f) : Color.gray;
            DrawFittedLabel(scopeRect, entry.IsSubWork ? T("BWT_RuleBuilder2_TargetSpecificJob") : T("BWT_RuleBuilder2_TargetWholeWork"));
            GUI.color = Color.white;

            if (Better_Work_Tab.WidgetsCompat.ButtonInvisible(new Rect(labelRect.x - 4f, rect.y, scopeRect.xMax - labelRect.x + 4f, rect.height)))
            {
                flow.SelectTarget(card, entry.WorkTypeDef, entry.WorkGiverDef, RuleBuilder2TargetSource.BuilderList);
                ExpandTargetParentIfSpecificJob(card.Target);
                UISoundCompat.TickLow.PlayOneShotOnCamera();
            }

            if (entry.IsSubWork)
            {
                TooltipHandler.TipRegion(rect, T("BWT_RuleBuilder2_TargetSpecificJobTooltip"));
            }
            else if (entry.HasSubWork)
            {
                TooltipHandler.TipRegion(rect, T("BWT_RuleBuilder2_TargetWholeWorkTooltip"));
            }
        }

        internal void ToggleTargetExpanded(string workTypeKey)
        {
            if (workTypeKey.NullOrEmpty())
            {
                return;
            }

            if (!expandedTargetWorkTypes.Add(workTypeKey))
            {
                expandedTargetWorkTypes.Remove(workTypeKey);
            }
        }

        internal void ExpandTargetParentIfSpecificJob(RuleBuilder2Target target)
        {
            if (target?.IsSubWorkTarget == true && !target.WorkTypeDefName.NullOrEmpty())
            {
                expandedTargetWorkTypes.Add(target.WorkTypeDefName);
            }
        }

        internal void ResetTargetPickerState()
        {
            targetSearch = "";
            targetListScroll = Vector2.zero;
        }
    }
}
