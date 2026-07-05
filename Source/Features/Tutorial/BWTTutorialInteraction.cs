using UnityEngine;

namespace Better_Work_Tab.Features.Tutorial
{
    internal readonly struct BWTTutorialInteraction
    {
        public BWTTutorialInteraction(
            BWTTutorialInteractionKind kind,
            Vector2 mousePosition,
            int mouseButton,
            bool control,
            bool shift,
            bool alt)
        {
            Kind = kind;
            MousePosition = mousePosition;
            MouseButton = mouseButton;
            Control = control;
            Shift = shift;
            Alt = alt;
        }

        public BWTTutorialInteractionKind Kind { get; }
        public Vector2 MousePosition { get; }
        public int MouseButton { get; }
        public bool Control { get; }
        public bool Shift { get; }
        public bool Alt { get; }
    }
}
