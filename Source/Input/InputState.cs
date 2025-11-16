using UnityEngine;
using Verse;

namespace Better_Work_Tab.Input
{
    /// <summary>
    /// Immutable snapshot of the current input state for a single frame.
    /// </summary>
    public readonly struct InputState
    {
        public InputState(Event evt)
        {
            if (evt == null)
            {
                EventType = EventType.Ignore;
                MousePosition = Vector2.zero;
                Button = -1;
                ClickCount = 0;
                Shift = false;
                Control = false;
                Alt = false;
                KeyCode = KeyCode.None;
                ScrollDelta = Vector2.zero;
                return;
            }

            EventType = evt.type;
            MousePosition = evt.mousePosition;
            Button = evt.button;
            ClickCount = evt.clickCount;
            Shift = evt.shift;
            Control = evt.control;
            Alt = evt.alt;
            KeyCode = evt.keyCode;
            ScrollDelta = evt.delta;
        }

        public EventType EventType { get; }
        public Vector2 MousePosition { get; }
        public int Button { get; }
        public int ClickCount { get; }
        public bool Shift { get; }
        public bool Control { get; }
        public bool Alt { get; }
        public KeyCode KeyCode { get; }
        public Vector2 ScrollDelta { get; }

        public bool IsMouseDown => EventType == EventType.MouseDown;
        public bool IsMouseUp => EventType == EventType.MouseUp;
        public bool IsMouseDrag => EventType == EventType.MouseDrag;
        public bool IsKeyDown => EventType == EventType.KeyDown;
        public bool IsScroll => EventType == EventType.ScrollWheel;
        public bool IsRepaint => EventType == EventType.Repaint;
        public bool IsLayout => EventType == EventType.Layout;

        public bool IsLeftClick => IsMouseDown && Button == 0;
        public bool IsRightClick => IsMouseDown && Button == 1;
        public bool IsDoubleClick => IsLeftClick && ClickCount == 2;
    }
}
