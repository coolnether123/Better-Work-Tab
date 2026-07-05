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
#if v1_2 || v1_1 || v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
            float lineHeight = font == GameFont.Tiny ? 18f : font == GameFont.Medium ? 30f : 22f;
#else
            float lineHeight = Text.LineHeightOf(font);
#endif
            float minHeight = lineHeight + verticalPadding;
            return rect.height >= minHeight
                ? rect
                : new Rect(rect.x, rect.y, rect.width, minHeight);
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

        internal static void DrawSafeLabel(Rect rect, string label)
        {
            Widgets.Label(NormalizeLabelRect(rect, Text.Font), label ?? "");
        }

        internal static void DrawIntStepper(Rect rect, ref int value, int min, int max)
        {
            if (Better_Work_Tab.WidgetsCompat.ButtonText(new Rect(rect.x, rect.y, 24f, rect.height), "-"))
            {
                value = Mathf.Clamp(value - 1, min, max);
            }
            DrawFittedLabel(new Rect(rect.x + 28f, rect.y, 42f, rect.height), value.ToString());
            if (Better_Work_Tab.WidgetsCompat.ButtonText(new Rect(rect.x + 72f, rect.y, 24f, rect.height), "+"))
            {
                value = Mathf.Clamp(value + 1, min, max);
            }
        }

        internal static void DrawFloatStepper(Rect rect, ref float value, float min, float max)
        {
            if (Better_Work_Tab.WidgetsCompat.ButtonText(new Rect(rect.x, rect.y, 24f, rect.height), "-"))
            {
                value = Mathf.Clamp(value - 0.05f, min, max);
            }
            DrawFittedLabel(new Rect(rect.x + 28f, rect.y, 48f, rect.height), value.ToString("0.##"));
            if (Better_Work_Tab.WidgetsCompat.ButtonText(new Rect(rect.x + 80f, rect.y, 24f, rect.height), "+"))
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
            Better_Work_Tab.WidgetsCompat.TextFieldNumeric(rect, ref priority, ref action.PriorityBuffer, min, max);
            action.Priority = RuleBuilder2PriorityRange.Clamp(priority);
            if (priority != action.Priority)
            {
                action.PriorityBuffer = action.Priority.ToString();
            }
        }

        internal static void DrawSectionChrome(Rect rect, string title)
        {
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.96f));
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
            int count = card?.Conditions?.Conditions?.Count(condition => condition != null && condition.Enabled) ?? 0;
            if (count <= 0)
            {
                return T("BWT_RuleBuilder2_NoConditionsSummary");
            }

            string first = RuleBuilder2ConditionCatalog.GetConditionText(
                card.Conditions.Conditions.FirstOrDefault(condition => condition != null && condition.Enabled),
                card.Target.ResolveWorkType());
            return count == 1
                ? first
                : T("BWT_RuleBuilder2_ConditionsSummary").Formatted(count, first).ToString();
        }

        internal static string BuildActionSummary(RuleBuilder2Card card)
        {
            if (card?.Action == null)
            {
                return T("BWT_RuleBuilder2_NoActionSummary");
            }

            return RuleBuilder2Evaluator.GetActionText(card, card.Target.ResolveWorkType(), card.Target.ResolveWorkGiver());
        }

        internal static string BuildRuleSentenceSummary(RuleBuilder2FlowController flow, RuleBuilder2Surface activeSurface, RuleBuilder2Card card)
        {
            return T("BWT_RuleBuilder2_ReadOnlySentence")
                .Formatted(
                    BuildTargetSummary(flow, activeSurface, card),
                    BuildConditionsSummary(card),
                    BuildActionSummary(card))
                .ToString();
        }

    }
}
