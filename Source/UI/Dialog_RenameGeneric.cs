using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public class Dialog_RenameGeneric : Window
    {
        private string _currentName;
        private readonly Action<string> _callback;
        private bool _focusedRenameField;
        private int _startAcceptingInputAtFrame;

        public override Vector2 InitialSize => new Vector2(280f, 191f);

        public Dialog_RenameGeneric(string currentName, Action<string> callback)
        {
            _currentName = currentName;
            _callback = callback;
            
            forcePause = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public void WasOpenedByHotkey()
        {
            _startAcceptingInputAtFrame = Time.frameCount + 1;
        }

        private bool AcceptsInput => _startAcceptingInputAtFrame <= Time.frameCount;

        protected virtual int MaxNameLength => 50; // Increased from 28 for longer divider names

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            bool enterPressed = false;
            if (Event.current.type == EventType.KeyUp && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                enterPressed = true;
                Event.current.Use();
            }
            
            Rect topPart = new Rect(inRect);
            topPart.height = 35f;
            
            Widgets.Label(topPart, "Rename".Translate());

            GUI.SetNextControlName("RenameField");
            string text = Widgets.TextField(new Rect(0f, 45f, inRect.width, 35f), _currentName);
            if (AcceptsInput && text.Length < MaxNameLength)
            {
                _currentName = text;
            }

            if (!_focusedRenameField)
            {
                Verse.UI.FocusControl("RenameField", this);
                _focusedRenameField = true;
            }

            if (Widgets.ButtonText(new Rect(15f, inRect.height - 35f - 10f, inRect.width - 30f, 35f), "OK".Translate()) || enterPressed)
            {
                if (_currentName.Length == 0)
                {
                    Messages.Message("NameCannotBeEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    _callback(_currentName);
                    Close();
                }
            }
        }
    }
}
