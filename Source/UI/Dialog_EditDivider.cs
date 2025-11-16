using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using Spine.UI.ColourPicker;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Simple window that lets the user rename a divider and pick a color.
    /// </summary>
    public class Dialog_EditDivider : Window
    {
        private readonly PawnDivider _divider;
        private string _nameBuffer;
        private Color _currentColor;
        private bool _focusedField;

        public override Vector2 InitialSize => new Vector2(360f, 196f);

        public Dialog_EditDivider(PawnDivider divider)
        {
            _divider = divider;
            _nameBuffer = divider.DividerName ?? "Divider";
            _currentColor = divider.DividerColor;

            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), "Rename".Translate());

            GUI.SetNextControlName("DividerRename");
            _nameBuffer = Widgets.TextField(new Rect(0f, 28f, inRect.width, 32f), _nameBuffer ?? string.Empty);
            if (!_focusedField)
            {
                Verse.UI.FocusControl("DividerRename", this);
                _focusedField = true;
            }

            float colorTop = 70f;
            Rect colorStripRect = new Rect(0f, colorTop, inRect.width, 12f);
            Widgets.DrawBoxSolid(colorStripRect, _currentColor);
            Widgets.DrawBox(colorStripRect, 1);

            Rect colorButtonRect = new Rect(0f, colorStripRect.yMax + 6f, inRect.width, 30f);
            if (Widgets.ButtonText(colorButtonRect, "Divider Color"))
            {
                Find.WindowStack.Add(new Dialog_ColourPicker(_currentColor, (picked, closing) =>
                {
                    _currentColor = picked;
                }));
            }

            float buttonY = inRect.height - 35f;
            Rect okButton = new Rect(inRect.width - 170f, buttonY, 80f, 30f);
            Rect cancelButton = new Rect(inRect.width - 85f, buttonY, 80f, 30f);

            bool enterPressed = Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Return;

            if (Widgets.ButtonText(okButton, "OK".Translate()) || enterPressed)
            {
                if (ApplyChanges())
                {
                    if (enterPressed) Event.current.Use();
                    Close();
                }
            }
            if (Widgets.ButtonText(cancelButton, "Cancel".Translate()))
            {
                Close();
            }
        }

        private bool ApplyChanges()
        {
            if (_divider == null)
            {
                return false;
            }

            string trimmed = (_nameBuffer ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                Messages.Message("NameCannotBeEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            _divider.DividerName = trimmed;
            _divider.DividerColor = _currentColor;
            return true;
        }
    }
}
