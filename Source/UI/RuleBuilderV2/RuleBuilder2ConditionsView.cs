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
    internal sealed class RuleBuilder2ConditionsView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private readonly RuleBuilder2Layout layout;
        private readonly DragDropController<RuleBuilder2Condition> conditionDragController;
        private readonly ClickOrDragGate<RuleBuilder2Condition> conditionClickGate = new ClickOrDragGate<RuleBuilder2Condition>();
        private string conditionSearch = "";
        private Vector2 activeConditionsScroll;
        private Rect conditionListRect;
        private List<RuleBuilder2Condition> currentConditions = new List<RuleBuilder2Condition>();
        private RuleBuilder2Condition pendingConditionDrag;
        private const float ConditionDragThreshold = 5f;

        internal RuleBuilder2ConditionsView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout)
        {
            this.window = window;
            this.flow = flow;
            this.layout = layout;
            conditionDragController = new DragDropController<RuleBuilder2Condition>(CalculateConditionTargetIndex);
        }

        /// <summary>
        /// Conditions are tested in order, so their order is part of the rule --
        /// and Better Work Tab reorders lists by dragging them. This follows the
        /// same switch as the rest of it rather than adding one of its own.
        /// </summary>
        private static bool DragReorderEnabled =>
            BetterWorkTabMod.Settings?.enableDragDropReordering ?? true;

        internal void DrawConditionsSection(Rect rect, RuleBuilder2Card card)
        {
            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_ConditionsBlockTitle"));
            Rect add = new Rect(rect.xMax - 142f, rect.y + 6f, 132f, 26f);
            if (Widgets.ButtonText(add, "+ " + T("BWT_RuleBuilder2_AddCondition")))
            {
                ShowAddConditionMenu(card);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(add, T("BWT_RuleBuilder2_AddCondition_Tooltip"));

            Rect active = new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, Mathf.Max(0f, rect.height - 48f));
            DrawActiveConditions(active, card);
            GUI.EndGroup();
        }

        internal void DrawActiveConditions(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.45f));
            var conditions = card.Conditions.Conditions;

            if (conditions.Count == 0)
            {
                GUI.color = Color.gray;
                DrawSafeLabel(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), T("BWT_RuleBuilder2_NoConditions"));
                GUI.color = Color.white;
                return;
            }

            RuleBuilder2ConditionGrouping.NormalizeFirstCondition(conditions);

            Rect inner = rect.ContractedBy(6f);
            conditionListRect = inner;
            currentConditions = conditions;
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, conditions.Count * layout.Metrics.ConditionRowStride));
            Widgets.BeginScrollView(inner, ref activeConditionsScroll, view);
            float y = 0f;
            for (int i = 0; i < conditions.Count; i++)
            {
                RuleBuilder2Condition condition = conditions[i];
                Rect row = new Rect(0f, y, view.width, layout.Metrics.ConditionRowHeight);
                DrawConditionRow(row, card, condition, i);
                if (i > 0)
                {
                    DrawConditionJoiner(row, condition);
                }

                y += layout.Metrics.ConditionRowStride;
            }
            Widgets.EndScrollView();

            if (DragReorderEnabled)
            {
                HandleConditionListInput(inner, conditions);
                DrawConditionDragOverlay(inner);
            }
        }

        internal void DrawConditionRow(Rect rect, RuleBuilder2Card card, RuleBuilder2Condition condition, int index)
        {
            bool showReorderButtons = !DragReorderEnabled;
            float editorWidth = ConditionEditorWidth(condition.Kind);
            bool isAlternative = index > 0 && condition.OrWithPrevious;
            RuleBuilder2ConditionRowRects row = layout.ConditionRow(rect, showReorderButtons, editorWidth, isAlternative);
            if (isAlternative)
            {
                rect = new Rect(
                    rect.x + layout.Metrics.ConditionAlternativeIndent,
                    rect.y,
                    Mathf.Max(1f, rect.width - layout.Metrics.ConditionAlternativeIndent),
                    rect.height);
            }
            bool dragging = conditionDragController.IsActive &&
                            conditionDragController.CurrentSession?.DraggedItem == condition;

            Widgets.DrawBoxSolid(rect, condition.Enabled ? new Color(0.18f, 0.18f, 0.18f, 0.95f) : new Color(0.11f, 0.11f, 0.11f, 0.95f));
            if (dragging)
            {
                Widgets.DrawBoxSolid(rect.ContractedBy(1f), new Color(0f, 0f, 0f, 0.28f));
            }
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            Widgets.DrawBox(rect, 1);

            Widgets.Checkbox(row.Checkbox.position, ref condition.Enabled);

            string text = RuleBuilder2ConditionCatalog.GetConditionText(condition, card.Target.ResolveWorkType());
            DrawFittedLabel(row.Label, text);
            if (editorWidth > 0f)
            {
                DrawConditionInlineEditor(row.Editor, card, condition);
            }

            if (showReorderButtons)
            {
                if (Widgets.ButtonText(row.Up, "^") && index > 0)
                {
                    card.Conditions.Conditions.RemoveAt(index);
                    card.Conditions.Conditions.Insert(index - 1, condition);
                    flow.RefreshPreview();
                }

                if (Widgets.ButtonText(row.Down, "v") && index < card.Conditions.Conditions.Count - 1)
                {
                    card.Conditions.Conditions.RemoveAt(index);
                    card.Conditions.Conditions.Insert(index + 1, condition);
                    flow.RefreshPreview();
                }
            }

            if (Widgets.ButtonText(row.Remove, "X"))
            {
                card.Conditions.Conditions.Remove(condition);
                flow.RefreshPreview();
            }
        }

        /// <summary>
        /// The and/or link between this row and the one above it.
        ///
        /// Drawn in the gap between rows rather than inside either of them,
        /// because it describes the join and not the condition -- and because
        /// the row itself is a drag handle, so a button living in it would have
        /// to fight the drag gate for the click.
        /// </summary>
        private void DrawConditionJoiner(Rect row, RuleBuilder2Condition condition)
        {
            float gap = layout.Metrics.ConditionRowStride - layout.Metrics.ConditionRowHeight;
            Rect joiner = new Rect(
                row.x + 34f,
                row.y - gap - 1f,
                layout.Metrics.ConditionJoinerWidth,
                layout.Metrics.ConditionJoinerHeight + 2f);

            bool isOr = condition.OrWithPrevious;
            string label = isOr
                ? T("BWT_RuleBuilder2_ConditionJoinOr")
                : T("BWT_RuleBuilder2_ConditionJoinAnd");

            Color previous = GUI.color;
            GUI.color = isOr ? new Color(0.9f, 0.82f, 0.55f) : Color.gray;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            if (Widgets.ButtonInvisible(joiner))
            {
                condition.OrWithPrevious = !condition.OrWithPrevious;
                flow.RefreshPreview();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            if (Mouse.IsOver(joiner))
            {
                Widgets.DrawHighlight(joiner);
            }

            Widgets.Label(joiner, label);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previous;
            TooltipHandler.TipRegion(joiner, T("BWT_RuleBuilder2_ConditionJoin_Tooltip"));
        }

        private int CalculateConditionTargetIndex(Vector2 mousePos)
        {
            float biasedY = mousePos.y + (layout.Metrics.ConditionRowStride * 0.5f);
            return ListDragCalculator.CalculateInsertionIndex(
                currentConditions.Count,
                biasedY,
                conditionListRect.y,
                activeConditionsScroll.y,
                _ => layout.Metrics.ConditionRowStride);
        }

        /// <summary>
        /// Starts a drag only once the pointer has actually travelled, so the
        /// controls sharing the row -- the checkbox, the value editor, the
        /// remove button -- still take ordinary clicks.
        /// </summary>
        private void HandleConditionListInput(Rect listRect, List<RuleBuilder2Condition> conditions)
        {
            Event evt = Event.current;
            if (evt == null || conditions.Count == 0)
            {
                return;
            }

            if (conditionDragController.IsActive)
            {
                ListDragSession<RuleBuilder2Condition> session = conditionDragController.CurrentSession;
                conditionDragController.UpdateDrag(evt.mousePosition);
                conditionDragController.ApplyAutoScroll(
                    ref activeConditionsScroll,
                    evt.mousePosition,
                    listRect,
                    conditions.Count * layout.Metrics.ConditionRowStride,
                    Time.deltaTime);

                if (evt.type == EventType.MouseUp ||
                    evt.rawType == EventType.MouseUp ||
                    !UnityEngine.Input.GetMouseButton(0))
                {
                    conditionClickGate.ClearIfTracking(session?.DraggedItem);
                    FinalizeConditionDrop(conditions);
                    if (evt.type == EventType.MouseUp)
                    {
                        evt.Use();
                    }
                }

                return;
            }

            if (evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                listRect.Contains(evt.mousePosition))
            {
                pendingConditionDrag = GetConditionAtMouse(conditions, listRect, evt.mousePosition);
                if (pendingConditionDrag != null)
                {
                    conditionClickGate.Begin(pendingConditionDrag, evt.button, evt.mousePosition);
                }

                return;
            }

            if (evt.type == EventType.MouseDrag &&
                pendingConditionDrag != null &&
                conditionClickGate.RegisterDrag(pendingConditionDrag, evt.mousePosition, ConditionDragThreshold))
            {
                int sourceIndex = conditions.IndexOf(pendingConditionDrag);
                if (sourceIndex >= 0 &&
                    conditionDragController.TryStartDrag(pendingConditionDrag, sourceIndex, conditions.Count, evt.mousePosition))
                {
                    conditionClickGate.MarkDragStarted(pendingConditionDrag);
                }

                evt.Use();
                pendingConditionDrag = null;
                return;
            }

            if (evt.type == EventType.MouseUp && pendingConditionDrag != null)
            {
                conditionClickGate.ClearIfTracking(pendingConditionDrag);
                pendingConditionDrag = null;
            }
        }

        private RuleBuilder2Condition GetConditionAtMouse(
            List<RuleBuilder2Condition> conditions,
            Rect listRect,
            Vector2 mousePosition)
        {
            float localY = mousePosition.y - listRect.y + activeConditionsScroll.y;
            int index = Mathf.FloorToInt(localY / layout.Metrics.ConditionRowStride);
            return index >= 0 && index < conditions.Count ? conditions[index] : null;
        }

        private void DrawConditionDragOverlay(Rect listRect)
        {
            if (!conditionDragController.IsActive)
            {
                return;
            }

            ListDragSession<RuleBuilder2Condition> session = conditionDragController.CurrentSession;
            if (session == null)
            {
                return;
            }

            float lineY = ListDragVisuals.GetInsertionLineY(
                session.TargetIndex,
                Enumerable.Repeat(layout.Metrics.ConditionRowStride, currentConditions.Count).ToList(),
                listRect.y,
                activeConditionsScroll.y);
            if (lineY >= listRect.y && lineY <= listRect.yMax)
            {
                ListDragVisuals.DrawInsertionLine(listRect.x, lineY, listRect.width);
            }
        }

        /// <summary>
        /// FinalizeDrag reorders the list it is handed, and that list is the
        /// card's own <see cref="RuleBuilder2ConditionGroup.Conditions"/>, so the
        /// rule is already in its new order by the time this returns. Only the
        /// preview needs telling.
        /// </summary>
        private void FinalizeConditionDrop(List<RuleBuilder2Condition> conditions)
        {
            if (conditionDragController.FinalizeDrag(conditions) != DragEndReason.Success)
            {
                return;
            }

            // A row dragged to the top has nothing left to be an alternative to.
            RuleBuilder2ConditionGrouping.NormalizeFirstCondition(conditions);
            flow.RefreshPreview();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        internal void ShowAddConditionMenu(RuleBuilder2Card card)
        {
            bool showAdvanced = BetterWorkTabMod.Settings?.ruleBuilder2ShowAdvancedConditions ?? false;
            var options = new List<FloatMenuOption>();
            foreach (var group in RuleBuilder2ConditionCatalog.Grouped(showAdvanced, ""))
            {
                options.Add(new FloatMenuOption(group.Key, null));
                foreach (var def in group)
                {
                    RuleBuilder2ConditionDefinition local = def;
                    options.Add(new FloatMenuOption("  " + local.Label, () =>
                    {
                        RuleBuilder2Condition condition = local.Create();
                        card.Conditions.Conditions.Add(condition);
                        flow.RefreshPreview();
                    }));
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// How much room one condition's value editor needs, or zero when it has
        /// no value to edit.
        ///
        /// Kept beside <see cref="DrawConditionInlineEditor"/> deliberately: the
        /// numbers here are the widths that method lays out, and if the two ever
        /// disagree a stepper runs under the row buttons. Zero matters most --
        /// "has the highest relevant skill", "is naturally always active" and
        /// "has a child on the map" take no parameter, and were being given a
        /// priority stepper by the fall-through case. It did nothing, since
        /// NormalizeCondition ignores those kinds, but it claimed a third of the
        /// row and told the player a number mattered when none did.
        /// </summary>
        private static float ConditionEditorWidth(RuleBuilder2ConditionKind kind)
        {
            switch (kind)
            {
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    return 268f;
                case RuleBuilder2ConditionKind.SkillMinimum:
                case RuleBuilder2ConditionKind.SkillMaximum:
                case RuleBuilder2ConditionKind.PassionAtLeast:
                    return 216f;
                case RuleBuilder2ConditionKind.Trait:
                case RuleBuilder2ConditionKind.Xenotype:
                    return 180f;
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                    return 170f;
                case RuleBuilder2ConditionKind.Gender:
                    return 110f;
                case RuleBuilder2ConditionKind.ExistingPriorityAtLeast:
                case RuleBuilder2ConditionKind.ExistingPriorityEquals:
                case RuleBuilder2ConditionKind.TopWorkTypesBySkill:
                    return 98f;
                default:
                    return 0f;
            }
        }

        internal void DrawConditionInlineEditor(Rect rect, RuleBuilder2Card card, RuleBuilder2Condition condition)
        {
            switch (condition.Kind)
            {
                case RuleBuilder2ConditionKind.SkillMinimum:
                case RuleBuilder2ConditionKind.SkillMaximum:
                case RuleBuilder2ConditionKind.PassionAtLeast:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 110f, rect.height), GetSkillButtonLabel(condition, card.Target.ResolveWorkType())))
                    {
                        ShowSkillMenu(condition, card.Target.ResolveWorkType());
                    }
                    DrawIntStepper(new Rect(rect.x + 118f, rect.y, 98f, rect.height), ref condition.IntValue, 0, 99);
                    break;
                case RuleBuilder2ConditionKind.Trait:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 180f, rect.height), GetDefButtonLabel<TraitDef>(condition, T("BWT_SelectTrait"))))
                    {
                        ShowDefMenu<TraitDef>(condition);
                    }
                    break;
                case RuleBuilder2ConditionKind.Xenotype:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 180f, rect.height), GetDefButtonLabel<XenotypeDef>(condition, T("BWT_RuleBuilder2_XenotypeFallback"))))
                    {
                        ShowDefMenu<XenotypeDef>(condition, int.MaxValue);
                    }
                    break;
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 150f, rect.height), GetDefButtonLabel<PawnCapacityDef>(condition, T("BWT_RuleBuilder2_CapacityFallback"))))
                    {
                        ShowDefMenu<PawnCapacityDef>(condition);
                    }
                    DrawFloatStepper(new Rect(rect.x + 158f, rect.y, 110f, rect.height), ref condition.FloatValue, 0f, 2f);
                    break;
                case RuleBuilder2ConditionKind.Gender:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 110f, rect.height), condition.TextValue.NullOrEmpty() ? T("BWT_RuleBuilder2_AnyGender") : condition.TextValue))
                    {
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption(T("BWT_RuleBuilder2_GenderMale"), () => condition.TextValue = Gender.Male.ToString()),
                            new FloatMenuOption(T("BWT_RuleBuilder2_GenderFemale"), () => condition.TextValue = Gender.Female.ToString())
                        }));
                    }
                    break;
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                    Widgets.CheckboxLabeled(new Rect(rect.x, rect.y, 170f, rect.height), T("BWT_RuleBuilder2_Assigned"), ref condition.BoolValue);
                    break;
                case RuleBuilder2ConditionKind.TopWorkTypesBySkill:
                    // A count of Work types, not a priority. It shared the
                    // priority stepper's range, so this offered 0 -- which
                    // matches nobody -- and stopped at whatever the priority
                    // maximum happened to be.
                    DrawIntStepper(new Rect(rect.x, rect.y, 98f, rect.height), ref condition.IntValue, 1, 20);
                    break;
                case RuleBuilder2ConditionKind.ExistingPriorityAtLeast:
                case RuleBuilder2ConditionKind.ExistingPriorityEquals:
                    DrawIntStepper(new Rect(rect.x, rect.y, 98f, rect.height), ref condition.IntValue, 0, RuleBuilder2PriorityRange.Max);
                    RuleBuilder2PriorityRange.NormalizeCondition(condition);
                    break;
                default:
                    // Nothing to edit. Listed explicitly rather than falling
                    // through to a stepper, so a new parameterless condition
                    // gets no editor by default instead of a meaningless one.
                    break;
            }
        }

        internal void DrawConditionPicker(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.45f));
            Widgets.DrawBox(rect, 1);

            float y = rect.y + 6f;
            bool showAdvanced = BetterWorkTabMod.Settings?.ruleBuilder2ShowAdvancedConditions ?? false;
            foreach (var group in RuleBuilder2ConditionCatalog.Grouped(showAdvanced, conditionSearch))
            {
                GUI.color = Color.yellow;
                DrawFittedLabel(new Rect(rect.x + 8f, y, rect.width - 16f, 24f), group.Key);
                GUI.color = Color.white;
                y += 26f;

                foreach (var def in group)
                {
                    Rect button = new Rect(rect.x + 8f, y, rect.width - 16f, 26f);
                    if (Widgets.ButtonText(button, def.Label))
                    {
                        RuleBuilder2Condition condition = def.Create();
                        card.Conditions.Conditions.Add(condition);
                    }

                    if (!def.Tooltip.NullOrEmpty())
                    {
                        TooltipHandler.TipRegion(button, def.Tooltip);
                    }

                    y += 30f;
                    if (y > rect.yMax - 26f)
                    {
                        return;
                    }
                }
            }
        }

        internal string GetSkillButtonLabel(RuleBuilder2Condition condition, WorkTypeDef workType)
        {
            SkillDef skill = condition.DefName.NullOrEmpty()
                ? workType?.relevantSkills?.FirstOrDefault()
                : DefDatabase<SkillDef>.GetNamedSilentFail(condition.DefName);
            return skill?.LabelCap.ToString() ?? T("BWT_RuleBuilder2_SkillFallback");
        }

        internal void ShowSkillMenu(RuleBuilder2Condition condition, WorkTypeDef workType)
        {
            var options = RuleBuilder2ConditionCatalog.GetSkillOptions(workType)
                .Select(skill => new FloatMenuOption(skill.LabelCap.ToString(), () => condition.DefName = skill.defName))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        internal static string GetDefButtonLabel<T>(RuleBuilder2Condition condition, string fallback) where T : Def
        {
            if (condition.DefName.NullOrEmpty())
            {
                return fallback;
            }

            T def = DefDatabase<T>.GetNamedSilentFail(condition.DefName);
            return def?.LabelCap.ToString() ?? condition.DefName;
        }

        internal static void ShowDefMenu<T>(RuleBuilder2Condition condition, int maxOptions = 80) where T : Def
        {
            var options = DefDatabase<T>.AllDefsListForReading
                .OrderBy(def => def.label)
                .Take(maxOptions)
                .Select(def => new FloatMenuOption(def.LabelCap.ToString(), () => condition.DefName = def.defName))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
