namespace Spine.UI.Tutorial
{
    public readonly struct TutorialOverlayContent
    {
        public TutorialOverlayContent(
            string title,
            string body,
            string primaryButton = null,
            string dismissButton = null)
        {
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            PrimaryButton = primaryButton;
            DismissButton = string.IsNullOrEmpty(dismissButton)
                ? "Already know Better Work Tab"
                : dismissButton;
        }

        public string Title { get; }
        public string Body { get; }
        public string PrimaryButton { get; }
        public string DismissButton { get; }
        public bool HasPrimaryButton => !string.IsNullOrEmpty(PrimaryButton);
    }
}
