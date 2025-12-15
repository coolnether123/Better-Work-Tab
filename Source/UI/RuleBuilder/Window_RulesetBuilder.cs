using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.Panels;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using Better_Work_Tab.UI.RuleBuilder.Widgets;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RWWidgets = Verse.Widgets;
using Better_Work_Tab.UI;

namespace Better_Work_Tab.UI.RuleBuilder
{
    /// <summary>
    /// Main window for the interactive ruleset builder.
    /// Organizes UI into three columns:
    /// 1. Work Type selector (left)
    /// 2. Priority selector (middle)
    /// 3. Condition editor (right)
    /// </summary>
    public class Window_RulesetBuilder : Window
    {
        // ═══════════════════════════════════════════════════════════════
        // STATE & PANELS
        // ═══════════════════════════════════════════════════════════════

        private RuleBuilderState _state;
        private WorkTypeListPanel _workTypePanel;
        private PrioritySelectorPanel _priorityPanel;
        private ConditionEditorPanel _conditionPanel;

        private float _lastRulesetClickTime;
        private string _lastRulesetClickedName;
        private const float DoubleClickTimeWindow = 0.3f;

        // ═══════════════════════════════════════════════════════════════
        // WINDOW SETUP
        // ═══════════════════════════════════════════════════════════════

        public Window_RulesetBuilder()
        {
            forcePause = false;
            doCloseX = true;
            preventCameraMotion = true;
            resizeable = false;
            draggable = false;
        }

        public override Vector2 InitialSize => new Vector2(1100f, 700f);

        public override void PreOpen()
        {
            base.PreOpen();

            // Initialize state
            _state = new RuleBuilderState();
            _state.SelectedRuleset = BetterWorkTabMod.Settings.CurrentRuleset
                ?? BetterWorkTabMod.Settings.SavedRulesets?.FirstOrDefault();

            // Initialize panels
            _workTypePanel = new WorkTypeListPanel();
            _priorityPanel = new PrioritySelectorPanel();
            _conditionPanel = new ConditionEditorPanel();

            // Wire up state events
            _state.OnWorkTypeChanged += _ => _workTypePanel.InvalidateCache();
        }

        // ═══════════════════════════════════════════════════════════════
        // MAIN DRAW
        // ═══════════════════════════════════════════════════════════════

        public override void DoWindowContents(Rect inRect)
        {
            // Header with ruleset dropdown
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, RuleBuilderConstants.NavBarHeight);
            DrawHeader(headerRect);

            // Content area
            Rect contentRect = new Rect(
                inRect.x,
                headerRect.yMax + 8f,
                inRect.width,
                inRect.height - headerRect.height - 12f);

            DrawThreeColumnLayout(contentRect);

            // Drag Drop
            if (_state?.DragController != null)
            {
                _state.DragController.Update();
                _state.DragController.DrawDragVisual();
            }
        }

        /// <summary>
        /// Draws the header with ruleset selection and new button.
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.CardBackground);
            RWWidgets.DrawBox(rect, 1);

            // Title
            Rect titleRect = new Rect(rect.x + 12f, rect.y + 8f, 200f, 24f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(titleRect, "BWT_RuleBuilder_Title".Translate());
            GUI.color = Color.white;

            // Ruleset dropdown/rename
            Rect dropdownRect = new Rect(
                rect.x + rect.width / 2f - 150f,
                rect.y + 6f,
                300f,
                28f);

            DrawRulesetDropdownWithRename(dropdownRect);

            // New ruleset button
            Rect newButtonRect = new Rect(rect.xMax - 110f, rect.y + 6f, 100f, 28f);
            if (RWWidgets.ButtonText(newButtonRect, "+ " + "BWT_New".Translate()))
            {
                CreateNewRuleset();
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Draws ruleset selector with double-click rename support.
        /// </summary>
        private void DrawRulesetDropdownWithRename(Rect rect)
        {
            var rulesets = BetterWorkTabMod.Settings.SavedRulesets ?? new List<WorkAssignmentRuleset>();
            var selected = _state.SelectedRuleset;

            string label = selected?.Name ?? "BWT_SelectRuleset".Translate();

            // Draw the button
            if (RWWidgets.ButtonText(rect, label))
            {
                HandleRulesetClick(selected);
            }

            // Handle double-click for rename
            if (Event.current.type == EventType.MouseDown &&
                Event.current.clickCount == 2 &&
                rect.Contains(Event.current.mousePosition) &&
                selected != null && !selected.IsDefault)
            {
                Event.current.Use();
                OpenRenameDialog(selected);
            }
        }

        private void HandleRulesetClick(WorkAssignmentRuleset selected)
        {
            var rulesets = BetterWorkTabMod.Settings.SavedRulesets ?? new List<WorkAssignmentRuleset>();

            var options = new List<FloatMenuOption>();

            foreach (var ruleset in rulesets)
            {
                var local = ruleset;
                string optionLabel = local.Name + (local.IsDefault ? " *" : "");
                
                options.Add(new FloatMenuOption(optionLabel, () =>
                {
                    _state.SelectedRuleset = local;
                    BetterWorkTabMod.Settings.CurrentRuleset = local;
                }));
            }

            options.Add(new FloatMenuOption("BWT_RuleBuilder_ManageRulesets".Translate(), () =>
            {
                Find.WindowStack.Add(new Window_RulesManager());
            }));

            if (selected != null && !selected.IsDefault)
            {
                options.Add(new FloatMenuOption("BWT_Rename".Translate(), () =>
                {
                    OpenRenameDialog(selected);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenRenameDialog(WorkAssignmentRuleset ruleset)
        {
            var dialog = new Dialog_RenameRuleset(ruleset, () =>
            {
                BetterWorkTabMod.Settings.Write();
            });

            Find.WindowStack.Add(dialog);
        }

        /// <summary>
        /// Creates a new ruleset with a default name.
        /// </summary>
        private void CreateNewRuleset()
        {
            var rulesets = BetterWorkTabMod.Settings.SavedRulesets;
            if (rulesets == null)
            {
                rulesets = new List<WorkAssignmentRuleset>();
                BetterWorkTabMod.Settings.SavedRulesets = rulesets;
            }

            var newRuleset = new WorkAssignmentRuleset(
                $"{"BWT_NewRuleset".Translate()} {rulesets.Count + 1}",
                new List<WorkAssignmentParameters>(),
                resetBeforeApplying: true,
                isDefault: false);

            rulesets.Add(newRuleset);
            _state.SelectedRuleset = newRuleset;
            BetterWorkTabMod.Settings.CurrentRuleset = newRuleset;
            BetterWorkTabMod.Settings.Write();
        }

        /// <summary>
        /// Draws the three-column layout.
        /// </summary>
        private void DrawThreeColumnLayout(Rect rect)
        {
            // Column 1: Work Types
            Rect col1Rect = new Rect(
                rect.x,
                rect.y,
                RuleBuilderConstants.WorkTypeListColumnWidth,
                rect.height);

            // Column 2: Priorities
            Rect col2Rect = new Rect(
                col1Rect.xMax + RuleBuilderConstants.ColumnGap,
                rect.y,
                RuleBuilderConstants.PrioritySelectorColumnWidth,
                rect.height);

            // Column 3: Conditions (remaining space)
            Rect col3Rect = new Rect(
                col2Rect.xMax + RuleBuilderConstants.ColumnGap,
                rect.y,
                rect.width - col1Rect.width - col2Rect.width - RuleBuilderConstants.ColumnGap * 2,
                rect.height);

            // Draw all three columns
            _workTypePanel.Draw(col1Rect, _state);
            _priorityPanel.Draw(col2Rect, _state);
            _conditionPanel.Draw(col3Rect, _state);
        }

        /// <summary>
        /// Saves settings when window closes.
        /// </summary>
        public override void PostClose()
        {
            base.PostClose();
            BetterWorkTabMod.Settings.Write();
        }
    }
}
