using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public class Dialog_WarningWithCheckbox : Window
    {
        private readonly string _text;
        private readonly string _title;
        private readonly System.Action _onConfirm;
        private readonly System.Action<bool> _setDoNotShowAgain;
        private bool _doNotShowAgain;

        public override Vector2 InitialSize => new Vector2(500f, 250f);

        public Dialog_WarningWithCheckbox(string text, string title, System.Action onConfirm, System.Action<bool> setDoNotShowAgain)
        {
            _text = text;
            _title = title;
            _onConfirm = onConfirm;
            _setDoNotShowAgain = setDoNotShowAgain;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 42f), _title);
            Text.Font = GameFont.Small;

            float margin = 10f;
            float buttonHeight = 35f;
            float checkboxHeight = 24f;

            // Content area
            Rect textRect = new Rect(0f, 42f, inRect.width, inRect.height - 42f - buttonHeight - checkboxHeight - margin * 2);
            Widgets.Label(textRect, _text);

            // Checkbox
            Rect checkboxRect = new Rect(0f, inRect.height - buttonHeight - checkboxHeight - margin, inRect.width, checkboxHeight);
            Widgets.CheckboxLabeled(checkboxRect, "BWT_DoNotShowAgain".Translate(), ref _doNotShowAgain);

            // Buttons
            float btnWidth = (inRect.width - margin) / 2f;
            Rect confirmRect = new Rect(0f, inRect.height - buttonHeight, btnWidth, buttonHeight);
            if (Widgets.ButtonText(confirmRect, "Confirm".Translate()))
            {
                if (_doNotShowAgain)
                {
                    _setDoNotShowAgain?.Invoke(true);
                }
                _onConfirm?.Invoke();
                Close();
            }

            Rect cancelRect = new Rect(btnWidth + margin, inRect.height - buttonHeight, btnWidth, buttonHeight);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }
    }
}
