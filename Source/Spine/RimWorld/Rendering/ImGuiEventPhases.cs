using UnityEngine;

namespace Spine.RimWorld.Rendering
{
    public enum ImGuiEventPhase
    {
        Layout,
        Repaint,
        Input,
        Other
    }

    public static class ImGuiEventPhases
    {
        public static ImGuiEventPhase Classify(EventType eventType)
        {
            switch (eventType)
            {
                case EventType.Layout:
                    return ImGuiEventPhase.Layout;
                case EventType.Repaint:
                    return ImGuiEventPhase.Repaint;
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseMove:
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                case EventType.KeyDown:
                case EventType.KeyUp:
                    return ImGuiEventPhase.Input;
                default:
                    return ImGuiEventPhase.Other;
            }
        }

        public static bool ShouldReserveGeometry(EventType eventType) => eventType == EventType.Layout;
        public static bool ShouldDraw(EventType eventType) => eventType == EventType.Repaint;
        public static bool ShouldHandleInput(EventType eventType) => Classify(eventType) == ImGuiEventPhase.Input;
    }
}
