using Better_Work_Tab.Features;
using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Dialog for renaming a ruleset.
    /// </summary>
    public class Dialog_RenameRuleset : Window
    {
        private readonly WorkAssignmentRuleset _ruleset;
        private string _buffer;
        private readonly Action _onConfirm;

        private const float FieldHeight = 30f;
        private const float ButtonHeight = 35f;
        private const float Padding = 12f;

        public Dialog_RenameRuleset(WorkAssignmentRuleset ruleset, Action onConfirm = null)
        {
            _ruleset = ruleset;
            _buffer = ruleset?.Name ?? "";
            _onConfirm = onConfirm;

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(400f, 180f);

        public override void DoWindowContents(Rect inRect)
        {
            // Handle Enter key
            bool enterPressed = false;
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                enterPressed = true;
                Event.current.Use();
            }

            // Title
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(titleRect, "BWT_RenameRuleset".Translate());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // Input field
            Rect fieldRect = new Rect(
                inRect.x + Padding,
                titleRect.yMax + Padding,
                inRect.width - Padding * 2,
                FieldHeight);

            GUI.SetNextControlName("RulesetNameField");
            _buffer = Widgets.TextField(fieldRect, _buffer, 64);

            // Buttons
            float buttonWidth = (inRect.width - Padding * 3) / 2f;

            Rect confirmRect = new Rect(
                inRect.x + Padding,
                fieldRect.yMax + Padding,
                buttonWidth,
                ButtonHeight);

            Rect cancelRect = new Rect(
                confirmRect.xMax + Padding,
                fieldRect.yMax + Padding,
                buttonWidth,
                ButtonHeight);

            if (Widgets.ButtonText(confirmRect, "BWT_Confirm".Translate()) || enterPressed)
            {
                if (!string.IsNullOrEmpty(_buffer.Trim()) && _ruleset != null)
                {
                    _ruleset.Name = _buffer.Trim();
                    _onConfirm?.Invoke();
                    Close();
                }
            }

            if (Widgets.ButtonText(cancelRect, "BWT_Cancel".Translate()))
            {
                Close();
            }

            // Focus field on open
            if (GUI.GetNameOfFocusedControl() != "RulesetNameField")
            {
                GUI.FocusControl("RulesetNameField");
            }
        }
    }
}
