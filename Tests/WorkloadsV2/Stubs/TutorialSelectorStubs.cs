using Spine.UI.Tutorial;
using UnityEngine;

namespace Verse
{
    public enum GameFont
    {
        Small
    }

    public static class Text
    {
        public static TextAnchor Anchor { get; set; }
        public static GameFont Font { get; set; }
        public static bool WordWrap { get; set; }
    }

    public static class Widgets
    {
        public static Color MouseoverOptionColor => Color.white;
        public static Color NormalOptionColor => Color.white;

        public static void DrawShadowAround(Rect rect) { }
        public static void DrawWindowBackgroundTutor(Rect rect) { }
        public static void Label(Rect rect, string text) { }
        public static void DrawHighlightIfMouseover(Rect rect) { }
        public static void DrawBoxSolid(Rect rect, Color color) { }
    }

    public static class TooltipHandler
    {
        public static void TipRegion(Rect rect, string text) { }
    }
}

namespace UnityEngine
{
    public enum EventType
    {
        Ignore,
        Repaint,
        KeyDown,
        KeyUp,
        MouseDown,
        MouseUp,
        MouseMove,
        MouseDrag,
        ScrollWheel,
        Used
    }

    public enum KeyCode
    {
        None,
        Escape
    }

    public sealed class Event
    {
        public static Event current { get; set; }
        public EventType type;
        public KeyCode keyCode;
        public int button;
        public Vector2 mousePosition;

        public void Use() { type = EventType.Used; }
    }
}

namespace Verse.Sound
{
    public static class MouseoverSounds
    {
        public static void DoRegion(Rect rect) { }
    }
}

namespace Spine.UI.WidgetExtensions
{
    public static class ConnectedOutlineDrawer
    {
        public static void DrawClosed(Vector2[] path, Color color, float thickness) { }
    }
}

namespace Better_Work_Tab.UI
{
    public static class TutorialSelectorTextExtensions
    {
        public static string Truncate(this string value, float width)
        {
            return value ?? string.Empty;
        }
    }
}

namespace Better_Work_Tab.Features.Tutorial
{
    internal readonly struct BWTTutorialProgressSnapshot
    {
        internal bool IsSettled(string lessonId) => false;
        internal bool IsAlreadyUsed(string lessonId) => false;
    }
}
