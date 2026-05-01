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
    /// Three-column editor (work types / priority / conditions) with an optional
    /// live preview panel below showing the simulated work tab result.
    /// </summary>
    public class Window_RulesetBuilder : Window
    {
        // ── State & panels ────────────────────────────────────────────────────

        private RuleBuilderState _state;
        private WorkTypeListPanel _workTypePanel;
        private PrioritySelectorPanel _priorityPanel;
        private ConditionEditorPanel _conditionPanel;
        private PreviewPanel _previewPanel;

        private bool _showPreview = true;

        // ── Window setup ──────────────────────────────────────────────────────

        public Window_RulesetBuilder()
        {
            forcePause = false;
            doCloseX = true;
            preventCameraMotion = true;
            resizeable = false;
            draggable = false;
        }

        public override Vector2 InitialSize => new Vector2(
            Verse.UI.screenWidth,
            Verse.UI.screenHeight - 35f);

        public override void PreOpen()
        {
            base.PreOpen();

            _state = new RuleBuilderState();
            _state.SelectedRuleset = BetterWorkTabMod.Settings.CurrentRuleset
                ?? BetterWorkTabMod.Settings.SavedRulesets?.FirstOrDefault();

            _workTypePanel = new WorkTypeListPanel();
            _priorityPanel = new PrioritySelectorPanel();
            _conditionPanel = new ConditionEditorPanel();
            _previewPanel = new PreviewPanel();

            _state.OnWorkTypeChanged += _ => _workTypePanel.InvalidateCache();
            _state.OnRulesModified += () => _previewPanel.Invalidate();
            _state.OnRulesetChanged += _ => _previewPanel.Invalidate();
        }

        // ── Main draw ─────────────────────────────────────────────────────────

        public override void DoWindowContents(Rect inRect)
        {
            // Header
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, RuleBuilderConstants.NavBarHeight);
            DrawHeader(headerRect);

            // Three-column editor — shrinks vertically when preview is open
            float editorHeight = _showPreview
                ? inRect.height - RuleBuilderConstants.NavBarHeight - 12f
                  - RuleBuilderConstants.PreviewPanelHeight - RuleBuilderConstants.ColumnGap
                : inRect.height - RuleBuilderConstants.NavBarHeight - 12f;

            Rect editorRect = new Rect(
                inRect.x,
                headerRect.yMax + 8f,
                inRect.width,
                editorHeight);

            DrawThreeColumnLayout(editorRect);

            // Preview panel
            if (_showPreview)
            {
                Rect previewRect = new Rect(
                    inRect.x,
                    editorRect.yMax + RuleBuilderConstants.ColumnGap,
                    inRect.width,
                    RuleBuilderConstants.PreviewPanelHeight);

                _previewPanel.Draw(previewRect, _state);
            }

            // Drag overlay (always on top)
            if (_state?.DragController != null)
            {
                _state.DragController.Update();
                _state.DragController.DrawDragVisual();
            }
        }

        // ── Header ────────────────────────────────────────────────────────────

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

            // Ruleset dropdown (centered)
            Rect dropdownRect = new Rect(
                rect.x + rect.width / 2f - 150f,
                rect.y + 6f,
                300f,
                28f);
            DrawRulesetDropdownWithRename(dropdownRect);

            // Right-side buttons — widths sized from text so nothing clips.
            // Layout (right → left): [+ New] [Edit?] [▼/▲ Preview]
            const float btnH = 28f;
            const float btnPad = 16f;
            const float btnGap = 6f;
            Text.Font = GameFont.Small;

            string newLabel = "+ " + "BWT_New".Translate();
            string previewLabel = (_showPreview ? "BWT_PreviewHide" : "BWT_PreviewShow").Translate();
            string editLabel = "BWT_Rename".Translate();

            float newW = Mathf.Max(70f, Text.CalcSize(newLabel).x + btnPad);
            float previewW = Mathf.Max(80f, Text.CalcSize(previewLabel).x + btnPad);
            float editW = Mathf.Max(60f, Text.CalcSize(editLabel).x + btnPad);

            Rect newButtonRect = new Rect(rect.xMax - newW - btnGap, rect.y + 6f, newW, btnH);

            if (_state.SelectedRuleset != null && !_state.SelectedRuleset.IsDefault)
            {
                Rect editButtonRect = new Rect(newButtonRect.x - editW - btnGap, rect.y + 6f, editW, btnH);
                if (RWWidgets.ButtonText(editButtonRect, editLabel))
                    OpenRenameDialog(_state.SelectedRuleset);

                Rect previewButtonRect = new Rect(editButtonRect.x - previewW - btnGap, rect.y + 6f, previewW, btnH);
                DrawPreviewToggleButton(previewButtonRect);
            }
            else
            {
                Rect previewButtonRect = new Rect(newButtonRect.x - previewW - btnGap, rect.y + 6f, previewW, btnH);
                DrawPreviewToggleButton(previewButtonRect);
            }

            if (RWWidgets.ButtonText(newButtonRect, newLabel))
                CreateNewRuleset();

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawPreviewToggleButton(Rect rect)
        {
            string label = _showPreview
                ? "BWT_PreviewHide".Translate()
                : "BWT_PreviewShow".Translate();

            if (RWWidgets.ButtonText(rect, label))
                TogglePreview();
        }

        // ── Preview toggle ────────────────────────────────────────────────────

        private void TogglePreview()
        {
            _showPreview = !_showPreview;
            if (!_showPreview)
                _previewPanel.Invalidate();
        }

        // ── Ruleset dropdown ──────────────────────────────────────────────────

        private void DrawRulesetDropdownWithRename(Rect rect)
        {
            var selected = _state.SelectedRuleset;
            string label = selected?.Name ?? "BWT_SelectRuleset".Translate();

            if (RWWidgets.ButtonText(rect, label))
                HandleRulesetClick(selected);

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

            // Calculate match counts for each ruleset when the dropdown opens
            var previewCalc = new RulesetPreviewCalculator();

            foreach (var ruleset in rulesets)
            {
                var local = ruleset;

                // Show matched-colonist count next to each ruleset name
                var previewResult = previewCalc.Calculate(local);
                string countSuffix = previewResult.HasData
                    ? $"  ({previewResult.MatchedPawns.Count}/{previewResult.Pawns.Count})"
                    : "";
                string optionLabel = local.Name + (local.IsDefault ? " *" : "") + countSuffix;

                options.Add(new FloatMenuOption(optionLabel, () =>
                {
                    _state.SelectedRuleset = local;
                    BetterWorkTabMod.Settings.CurrentRuleset = local;
                }));
            }

            var mode = BetterWorkTabMod.Settings.rulesetViewMode;
            if (mode == BetterWorkTabSettings.RulesetViewMode.Raw || mode == BetterWorkTabSettings.RulesetViewMode.Both)
            {
                string manageLabel = mode == BetterWorkTabSettings.RulesetViewMode.Raw
                    ? "BWT_RuleBuilder_ManageRulesets".Translate()
                    : "BWT_RuleBuilder_ManageRulesets".Translate() + " (Raw)";

                options.Add(new FloatMenuOption(manageLabel, () =>
                    Find.WindowStack.Add(new Window_RulesManager())));
            }

            if (selected != null && !selected.IsDefault)
            {
                options.Add(new FloatMenuOption("BWT_Rename".Translate(), () =>
                    OpenRenameDialog(selected)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenRenameDialog(WorkAssignmentRuleset ruleset)
        {
            Find.WindowStack.Add(new Dialog_RenameRuleset(ruleset, () =>
                BetterWorkTabMod.Settings.Write()));
        }

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

        // ── Three-column editor ───────────────────────────────────────────────

        private void DrawThreeColumnLayout(Rect rect)
        {
            Rect col1Rect = new Rect(
                rect.x, rect.y,
                RuleBuilderConstants.WorkTypeListColumnWidth, rect.height);

            Rect col2Rect = new Rect(
                col1Rect.xMax + RuleBuilderConstants.ColumnGap, rect.y,
                RuleBuilderConstants.PrioritySelectorColumnWidth, rect.height);

            Rect col3Rect = new Rect(
                col2Rect.xMax + RuleBuilderConstants.ColumnGap, rect.y,
                rect.width - col1Rect.width - col2Rect.width - RuleBuilderConstants.ColumnGap * 2,
                rect.height);

            _workTypePanel.Draw(col1Rect, _state);
            _priorityPanel.Draw(col2Rect, _state);
            _conditionPanel.Draw(col3Rect, _state);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public override void PostClose()
        {
            base.PostClose();
            BetterWorkTabMod.Settings.Write();
        }
    }
}
