using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using UnityEngine;
using Verse;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2RulePickerSource
    {
        internal string Name { get; private set; }
        internal List<RuleBuilder2Card> Cards { get; private set; }

        internal static RuleBuilder2RulePickerSource FromRuleset(string name, RuleBuilder2Ruleset ruleset)
        {
            return new RuleBuilder2RulePickerSource
            {
                Name = name.NullOrEmpty() ? T("BWT_RuleBuilder2_NewRulesetName") : name,
                Cards = ruleset?.Cards?
                    .Where(RuleBuilder2FlowController.IsVisibleRuleCard)
                    .OrderBy(card => card.SortOrder)
                    .ToList() ?? new List<RuleBuilder2Card>()
            };
        }
    }

    internal sealed class Window_RuleBuilder2RulePicker : Window
    {
        private readonly string title;
        private readonly List<RuleBuilder2RulePickerSource> sources;
        private readonly Action<List<RuleBuilder2Card>> onAddSelected;
        private readonly HashSet<string> selectedIds = new HashSet<string>();
        private Vector2 scroll;
        private int sourceIndex;

        internal Window_RuleBuilder2RulePicker(
            string title,
            List<RuleBuilder2RulePickerSource> sources,
            Action<List<RuleBuilder2Card>> onAddSelected)
        {
            this.title = title;
            this.sources = sources ?? new List<RuleBuilder2RulePickerSource>();
            this.onAddSelected = onAddSelected;
            forcePause = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = true;
        }

#if v0_13 || vAlpha4
        public override Vector2 InitialWindowSize => new Vector2(690f, 520f);
#else
        public override Vector2 InitialSize => new Vector2(690f, 520f);
#endif

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            DrawFittedLabel(new Rect(inRect.x, inRect.y, inRect.width, 30f), title);
            Text.Font = GameFont.Small;

            Rect sourceButton = new Rect(inRect.x, inRect.y + 38f, inRect.width, 30f);
            Rect list = new Rect(inRect.x, sourceButton.yMax + 10f, inRect.width, Mathf.Max(0f, inRect.height - 124f));
            Rect cancel = new Rect(inRect.xMax - 198f, inRect.yMax - 34f, 88f, 30f);
            Rect add = new Rect(cancel.xMax + 8f, cancel.y, 102f, 30f);

            if (sources.Count == 0)
            {
                Widgets.DrawMenuSection(list);
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                DrawSafeLabel(list.ContractedBy(16f), T("BWT_RuleBuilder2_RulePickerNoSources"));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            else
            {
                DrawSourceDropdown(sourceButton);
                DrawRuleList(list, sources[sourceIndex]);
            }

            if (Better_Work_Tab.WidgetsCompat.ButtonText(cancel, T("BWT_Cancel")))
            {
                Close();
            }

            bool hasSelection = selectedIds.Count > 0;
            if (Better_Work_Tab.WidgetsCompat.ButtonText(add, T("BWT_RuleBuilder2_RulePickerAddSelected"), active: hasSelection))
            {
                RuleBuilder2RulePickerSource source = sources[sourceIndex];
                List<RuleBuilder2Card> selected = source.Cards
                    .Where(card => card != null && selectedIds.Contains(card.StableId))
                    .ToList();
                onAddSelected?.Invoke(selected);
                Close();
            }
        }

        private void DrawSourceDropdown(Rect rect)
        {
            RuleBuilder2RulePickerSource source = sources[sourceIndex];
            if (Better_Work_Tab.WidgetsCompat.ButtonText(rect, TruncateToWidth(source.Name + " v", rect.width - 8f)))
            {
                var options = new List<FloatMenuOption>();
                for (int i = 0; i < sources.Count; i++)
                {
                    int localIndex = i;
                    string label = localIndex == sourceIndex ? "* " + sources[i].Name : sources[i].Name;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        sourceIndex = localIndex;
                        selectedIds.Clear();
                        scroll = Vector2.zero;
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawRuleList(Rect rect, RuleBuilder2RulePickerSource source)
        {
            Widgets.DrawMenuSection(rect);
            if (source.Cards.Count == 0)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                DrawSafeLabel(rect.ContractedBy(16f), T("BWT_RuleBuilder2_RulePickerNoRules"));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, source.Cards.Count * 54f + 6f));
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = 4f;
            foreach (RuleBuilder2Card card in source.Cards)
            {
                DrawRuleRow(new Rect(4f, y, view.width - 8f, 48f), card);
                y += 54f;
            }

            Widgets.EndScrollView();
        }

        private void DrawRuleRow(Rect rect, RuleBuilder2Card card)
        {
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, selectedIds.Contains(card.StableId)
                ? new Color(0.22f, 0.27f, 0.2f, 0.95f)
                : new Color(0.13f, 0.13f, 0.13f, 0.95f));
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            Widgets.DrawBox(rect, 1);

            bool selected = selectedIds.Contains(card.StableId);
            Rect check = new Rect(rect.x + 8f, rect.y + 12f, 24f, 24f);
            Widgets.Checkbox(new Vector2(check.x, check.y), ref selected);
            SetSelected(card, selected);

            Rect target = new Rect(check.xMax + 8f, rect.y + 4f, Mathf.Max(1f, rect.width - check.width - 24f), 22f);
            Rect summary = new Rect(target.x, target.yMax + 2f, target.width, 22f);
            DrawFittedLabel(target, BuildCardTargetSummary(card));
            GUI.color = Color.gray;
            DrawFittedLabel(summary, BuildConditionsSummary(card) + " - " + BuildActionSummary(card));
            GUI.color = Color.white;

            if (!Mouse.IsOver(check) && Better_Work_Tab.WidgetsCompat.ButtonInvisible(new Rect(target.x, rect.y, rect.xMax - target.x, rect.height)))
            {
                selected = !selectedIds.Contains(card.StableId);
                SetSelected(card, selected);
            }
        }

        private void SetSelected(RuleBuilder2Card card, bool selected)
        {
            if (card == null)
            {
                return;
            }

            if (selected)
            {
                selectedIds.Add(card.StableId);
            }
            else
            {
                selectedIds.Remove(card.StableId);
            }
        }
    }
}
