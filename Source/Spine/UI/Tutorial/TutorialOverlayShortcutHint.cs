namespace Spine.UI.Tutorial
{
    public readonly struct TutorialOverlayShortcutHint
    {
        public TutorialOverlayShortcutHint(string text, int focusIndex = -1)
        {
            Text = text ?? string.Empty;
            FocusIndex = focusIndex;
        }

        public string Text { get; }
        public int FocusIndex { get; }
    }
}
