using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal static class RuleBuilder2UiUtility
    {
        internal static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }

        internal static string TruncateToWidth(string text, float width)
        {
            if (text.NullOrEmpty() || width <= 12f || Text.CalcSize(text).x <= width)
            {
                return text ?? "";
            }

            const string ellipsis = "...";
            for (int length = text.Length; length > 0; length--)
            {
                string candidate = text.Substring(0, length).TrimEnd() + ellipsis;
                if (Text.CalcSize(candidate).x <= width)
                {
                    return candidate;
                }
            }

            return ellipsis;
        }

        internal static Rect NormalizeLabelRect(Rect rect, GameFont font = GameFont.Small, float verticalPadding = 2f)
        {
            float minHeight = Text.LineHeightOf(font) + verticalPadding;
            return rect.height >= minHeight
                ? rect
                : new Rect(rect.x, rect.y, rect.width, minHeight);
        }

        /// <summary>
        /// The width one label needs at a given font.
        ///
        /// Exists so a row that reserves a column for a label can reserve the
        /// width that label actually takes. A hard-coded column is a guess made
        /// against English, and every other language pays for it.
        /// </summary>
        internal static float MeasureLabelWidth(string label, GameFont font = GameFont.Small)
        {
            GameFont previousFont = Text.Font;
            Text.Font = font;
            float width = Text.CalcSize(label ?? string.Empty).x;
            Text.Font = previousFont;
            return width;
        }

        internal static void DrawFittedLabel(Rect rect, string label)
        {
            label = label ?? "";
            rect = NormalizeLabelRect(rect, Text.Font);
            string fitted = TruncateToWidth(label, rect.width - 4f);
            bool previousWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(rect, fitted);
            Text.WordWrap = previousWordWrap;
            if (fitted != label)
            {
                TooltipHandler.TipRegion(rect, label);
            }
        }

        internal static void DrawWrappedLabel(Rect rect, string label)
        {
            bool previousWordWrap = Text.WordWrap;
            Text.WordWrap = true;
            Widgets.Label(rect, label ?? "");
            Text.WordWrap = previousWordWrap;
        }

        internal static float MeasureWrappedHeight(string label, float width, GameFont font = GameFont.Small)
        {
            GameFont previousFont = Text.Font;
            bool previousWordWrap = Text.WordWrap;
            Text.Font = font;
            Text.WordWrap = true;
            float height = Text.CalcHeight(label ?? "", Mathf.Max(1f, width));
            Text.WordWrap = previousWordWrap;
            Text.Font = previousFont;
            return Mathf.Max(Text.LineHeightOf(font), height);
        }

        internal static float ClampWrappedHeight(string label, float width, int maxLines, out bool clamped, GameFont font = GameFont.Small)
        {
            float measured = MeasureWrappedHeight(label, width, font);
            float maxHeight = Text.LineHeightOf(font) * Mathf.Max(1, maxLines);
            clamped = measured > maxHeight + 0.5f;
            return Mathf.Clamp(measured, Text.LineHeightOf(font), maxHeight);
        }

        internal static void DrawSafeLabel(Rect rect, string label)
        {
            Widgets.Label(NormalizeLabelRect(rect, Text.Font), label ?? "");
        }

        internal static void DrawIntStepper(Rect rect, ref int value, int min, int max)
        {
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, 24f, rect.height), "-"))
            {
                value = Mathf.Clamp(value - 1, min, max);
            }
            DrawFittedLabel(new Rect(rect.x + 28f, rect.y, 42f, rect.height), value.ToString());
            if (Widgets.ButtonText(new Rect(rect.x + 72f, rect.y, 24f, rect.height), "+"))
            {
                value = Mathf.Clamp(value + 1, min, max);
            }
        }

        internal static void DrawFloatStepper(Rect rect, ref float value, float min, float max)
        {
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, 24f, rect.height), "-"))
            {
                value = Mathf.Clamp(value - 0.05f, min, max);
            }
            DrawFittedLabel(new Rect(rect.x + 28f, rect.y, 48f, rect.height), value.ToString("0.##"));
            if (Widgets.ButtonText(new Rect(rect.x + 80f, rect.y, 24f, rect.height), "+"))
            {
                value = Mathf.Clamp(value + 0.05f, min, max);
            }
        }

        internal static void DrawIntTextEntry(Rect rect, RuleBuilder2Action action, int min, int max)
        {
            action.PriorityBuffer = string.IsNullOrEmpty(action.PriorityBuffer)
                ? action.Priority.ToString()
                : action.PriorityBuffer;
            int priority = action.Priority;
            Widgets.TextFieldNumeric(rect, ref priority, ref action.PriorityBuffer, min, max);
            action.Priority = RuleBuilder2PriorityRange.Clamp(priority);
            if (priority != action.Priority)
            {
                action.PriorityBuffer = action.Priority.ToString();
            }
        }

        internal static void DrawSectionChrome(Rect rect, string title)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.96f));
            Widgets.DrawBox(rect, 1);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.82f, 0.55f);
            DrawFittedLabel(new Rect(rect.x + 10f, rect.y + 5f, rect.width - 20f, 30f), title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        internal static string BuildTargetLabel(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            string work = GetWorkTypeLabel(workType);
            return workGiver == null ? work : work + " -> " + workGiver.LabelCap;
        }

        internal static string GetWorkTypeLabel(WorkTypeDef workType)
        {
            if (workType == null)
            {
                return T("BWT_RuleBuilder2_WorkFallback");
            }

            string label = workType.LabelCap.ToString();
            return label.NullOrEmpty() ? workType.defName : label;
        }

        internal static string BuildTargetSummary(RuleBuilder2FlowController flow, RuleBuilder2Surface activeSurface, RuleBuilder2Card card)
        {
            if (activeSurface == RuleBuilder2Surface.Main &&
                card == flow.ActiveCard &&
                flow.PreviewedWorkTabContext.HasValue)
            {
                RuleBuilder2WorkTabSelection previewSelection = flow.PreviewedWorkTabContext.Value;
                return T("BWT_RuleBuilder2_HoverTargetSummary")
                    .Formatted(BuildTargetLabel(previewSelection.WorkType, previewSelection.WorkGiver))
                    .ToString();
            }

            if (card?.Target?.HasTarget != true)
            {
                return T("BWT_RuleBuilder2_NoTargetSummary");
            }

            return BuildCardTargetSummary(card);
        }

        internal static string BuildCardTargetSummary(RuleBuilder2Card card)
        {
            if (card?.Target?.HasTarget != true)
            {
                return T("BWT_RuleBuilder2_NoTargetSummary");
            }

            string label = BuildTargetLabel(card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
            return label == T("BWT_RuleBuilder2_WorkFallback") && !card.Target.DisplayLabel.NullOrEmpty()
                ? card.Target.DisplayLabel
                : label;
        }

        internal static string BuildConditionsSummary(RuleBuilder2Card card)
        {
            return BuildConditionsSummary(card, false);
        }

        private static string BuildConditionsSummary(RuleBuilder2Card card, bool sentenceClause)
        {
            // Counted and quoted in runs, not conditions. "(a chef or cooking
            // 8+) and not a brawler" is two requirements, and reading the first
            // one back as just "a chef" would describe a rule nobody wrote.
            List<RuleBuilder2ConditionRun> runs =
                RuleBuilder2ConditionGrouping.BuildRuns(card?.Conditions?.Conditions);
            int count = runs.Count;

            // The sentence counts; the list row quotes. Condition texts are
            // capitalised predicates -- "Has trait Nudist", "Is capable of
            // violence" -- so dropping one into the middle of a sentence after
            // "when" produced "when Has trait Nudist", and quoting one out of
            // several produced "when 3 conditions, starting with X: set to
            // priority 3", which is not a sentence at all. A count reads
            // correctly whatever the conditions say, and the conditions
            // themselves are listed directly underneath it.
            if (sentenceClause)
            {
                if (count <= 0)
                {
                    return T("BWT_RuleBuilder2_RuleSentenceEveryColonist");
                }

                return count == 1
                    ? T("BWT_RuleBuilder2_RuleSentenceMatchingOne")
                    : T("BWT_RuleBuilder2_RuleSentenceMatchingMany").Formatted(count).ToString();
            }

            // The rule list row is a 300px column carrying the conditions and
            // the action. Naming the first condition there did not fit: it
            // truncated mid-word and pushed the action -- what the rule actually
            // does -- off the end entirely. A count leaves room for both.
            if (count <= 0)
            {
                return T("BWT_RuleBuilder2_NoConditionsSummary");
            }

            return count == 1
                ? T("BWT_RuleBuilder2_ConditionCountOne")
                : T("BWT_RuleBuilder2_ConditionCountMany").Formatted(count).ToString();
        }

        internal static string BuildActionSummary(RuleBuilder2Card card)
        {
            return BuildActionSummary(card, true);
        }

        internal static string BuildActionSummary(RuleBuilder2Card card, bool includeTarget)
        {
            if (card?.Action == null)
            {
                return includeTarget
                    ? T("BWT_RuleBuilder2_NoActionSummary")
                    : T("BWT_RuleBuilder2_ActionText_NoActionShort");
            }

            if (!includeTarget)
            {
                switch (card.Action.Kind)
                {
                    case RuleBuilder2ActionKind.Disable:
                        return T("BWT_RuleBuilder2_ActionText_DisableShort");
                    case RuleBuilder2ActionKind.FollowGlobal:
                        return T("BWT_RuleBuilder2_ActionText_FollowGlobalShort");
                    case RuleBuilder2ActionKind.SetTimeSchedule:
                    case RuleBuilder2ActionKind.SetSubWorkSchedule:
                        return T("BWT_RuleBuilder2_ActionText_ScheduleShort");
                    default:
                        return T("BWT_RuleBuilder2_ActionText_SetPriorityShort").Formatted(card.Action.Priority).ToString();
                }
            }

            return RuleBuilder2Evaluator.GetActionText(card, card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
        }

        internal static string BuildRuleSentenceSummary(RuleBuilder2FlowController flow, RuleBuilder2Surface activeSurface, RuleBuilder2Card card)
        {
            if (card?.Target?.HasTarget != true)
            {
                return T("BWT_RuleBuilder2_RuleSentenceNoTarget");
            }

            return T("BWT_RuleBuilder2_ReadOnlySentence")
                .Formatted(
                    BuildTargetSummary(flow, activeSurface, card),
                    BuildConditionsSummary(card, true),
                    BuildActionSummary(card, false))
                .ToString();
        }

    }
}
