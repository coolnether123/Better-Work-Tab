using System;
using Better_Work_Tab;
using Better_Work_Tab.Features;
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
        private bool _showLabel;
        private GameFont _labelFont;
        private float _height;
        private static readonly GameFont[] FontOptions = { GameFont.Small, GameFont.Medium };

#if v0_13
        public override Vector2 InitialWindowSize => new Vector2(360f, 380f);
#else
        public override Vector2 InitialSize => new Vector2(360f, 380f);
#endif

        public Dialog_EditDivider(PawnDivider divider)
        {
            _divider = divider;
            _nameBuffer = divider.DividerName ?? "Divider";
            _currentColor = divider.DividerColor;
            _showLabel = divider.ShowLabel;
            _labelFont = divider.LabelFont;
            _height = divider.Height > 0f ? divider.Height : (BetterWorkTabMod.Settings?.dividerHeight ?? 18f);

            forcePause = false;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), "Edit divider");

            GUI.SetNextControlName("DividerRename");
            _nameBuffer = Widgets.TextField(new Rect(0f, 28f, inRect.width, 32f), _nameBuffer ?? string.Empty);
            if (!_focusedField)
            {
                GUI.FocusControl("DividerRename");
                _focusedField = true;
            }

            float colorTop = 70f;
            Rect colorStripRect = new Rect(0f, colorTop, inRect.width, 12f);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(colorStripRect, _currentColor);
            Widgets.DrawBox(colorStripRect, 1);

            Rect colorButtonRect = new Rect(0f, colorStripRect.yMax + 6f, inRect.width, 30f);
            if (BetterWorkTabMod.Settings?.allowCustomDividerColors ?? true)
            {
                if (Better_Work_Tab.WidgetsCompat.ButtonText(colorButtonRect, "Divider Color"))
                {
                    Find.WindowStack.Add(new Dialog_ColourPicker(_currentColor, (picked, closing) =>
                    {
                        _currentColor = picked;
                    }));
                }
            }
            else
            {
                GUI.color = Color.gray;
                Better_Work_Tab.WidgetsCompat.ButtonText(colorButtonRect, "Divider Color (Disabled)");
                GUI.color = Color.white;
            }

            float optionsTop = colorButtonRect.yMax + 14f;
            Rect showLabelRect = new Rect(0f, optionsTop, inRect.width, 30f);
            Better_Work_Tab.WidgetsCompat.CheckboxLabeled(showLabelRect, "Show label", ref _showLabel);

            Rect fontButtonRect = new Rect(0f, showLabelRect.yMax + 8f, inRect.width, 30f);
            GUI.enabled = _showLabel;
            if (Better_Work_Tab.WidgetsCompat.ButtonText(fontButtonRect, $"Text size: {GetFontLabel(_labelFont)}"))
            {
                _labelFont = GetNextFont(_labelFont);
            }
            GUI.enabled = true;

            float heightBlockTop = fontButtonRect.yMax + 12f;
            var heightLabelRect = new Rect(0f, heightBlockTop, inRect.width, 22f);
            Widgets.Label(heightLabelRect, $"Height: {_height:F0}px");
            var heightSliderRect = new Rect(0f, heightLabelRect.yMax + 15f, inRect.width, 27);
            _height = WidgetsCompat.HorizontalSlider(heightSliderRect, _height, 10f, 80f, false, "Thin", "Tall");

            float buttonY = inRect.height - 50f;
            Rect okButton = new Rect(inRect.width - 170f, buttonY, 70f, 30f);
            Rect cancelButton = new Rect(inRect.width - 85f, buttonY, 70f, 30f);

            bool enterPressed = Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Return;

            if (Better_Work_Tab.WidgetsCompat.ButtonText(okButton, "OK".Translate()) || enterPressed)
            {
                if (ApplyChanges())
                {
                    if (enterPressed) Event.current.Use();
                    Close();
                }
            }
            if (Better_Work_Tab.WidgetsCompat.ButtonText(cancelButton, "Cancel".Translate()))
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
                MessageCompat.Message("NameCannotBeEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            _divider.DividerName = trimmed;
            _divider.DividerColor = _currentColor;
            _divider.ShowLabel = _showLabel;
            _divider.LabelFont = _labelFont;
            _divider.Height = Mathf.Clamp(_height, 10f, 80f);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            return true;
        }

        private static GameFont GetNextFont(GameFont current)
        {
            int index = Array.IndexOf(FontOptions, current);
            if (index < 0)
            {
                index = 0;
            }
            index = (index + 1) % FontOptions.Length;
            return FontOptions[index];
        }

        private static string GetFontLabel(GameFont font)
        {
            switch (font)
            {
                case GameFont.Tiny:
                    return "Tiny";
                case GameFont.Small:
                    return "Small";
                case GameFont.Medium:
                    return "Medium";
                default:
                    return font.ToString();
            }
        }
    }
}
