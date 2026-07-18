using UnityEngine;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    internal static class SubWorkDrilldownInput
    {
        internal static bool IsEnabled
        {
            get
            {
                var settings = BetterWorkTabMod.Settings;
                return settings != null && settings.enableSubWorkDrilldown;
            }
        }

        internal static bool MatchesGesture(Event evt)
        {
            if (!IsEnabled || evt == null || evt.type != EventType.MouseDown)
            {
                return false;
            }

            return MatchesShortcut(evt);
        }

        internal static bool MatchesShortcut(Event evt)
        {
            return IsEnabled && evt != null && MatchesModifier(evt) && MatchesButton(evt);
        }

        internal static bool ShouldOfferCtrlLeftDiscovery(Event evt)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!IsEnabled ||
                settings == null ||
                settings.subWorkCtrlClickNoticeDismissed ||
                evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0)
            {
                return false;
            }

            bool alreadyConfigured =
                settings.subWorkDrilldownModifier == BetterWorkTabSettings.SubWorkDrilldownModifier.Ctrl &&
                settings.subWorkDrilldownButton == BetterWorkTabSettings.SubWorkDrilldownButton.Left;
            if (alreadyConfigured)
            {
                return false;
            }

            bool controlHeld = evt.control ||
                (evt.modifiers & EventModifiers.Control) != 0 ||
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);
            bool shiftHeld = evt.shift ||
                (evt.modifiers & EventModifiers.Shift) != 0 ||
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            return controlHeld && !shiftHeld;
        }

        private static bool MatchesModifier(Event evt)
        {
            var modifier = BetterWorkTabMod.Settings.subWorkDrilldownModifier;
            if (modifier == BetterWorkTabSettings.SubWorkDrilldownModifier.Shift)
            {
                return evt.shift ||
                       (evt.modifiers & EventModifiers.Shift) != 0 ||
                       Input.GetKey(KeyCode.LeftShift) ||
                       Input.GetKey(KeyCode.RightShift);
            }

            return evt.control ||
                   (evt.modifiers & EventModifiers.Control) != 0 ||
                   Input.GetKey(KeyCode.LeftControl) ||
                   Input.GetKey(KeyCode.RightControl);
        }

        internal static bool MatchesButton(Event evt)
        {
            var button = BetterWorkTabMod.Settings.subWorkDrilldownButton;
            if (button == BetterWorkTabSettings.SubWorkDrilldownButton.Right)
            {
                return evt.button == 1;
            }

            return evt.button == 0;
        }

        internal static string GestureLabel()
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return "the configured shortcut";
            }

            string modifier = settings.subWorkDrilldownModifier == BetterWorkTabSettings.SubWorkDrilldownModifier.Shift
                ? "Shift"
                : "Ctrl";
            string button = settings.subWorkDrilldownButton == BetterWorkTabSettings.SubWorkDrilldownButton.Right
                ? "right-click"
                : "click";

            return modifier + "-" + button;
        }
    }
}
