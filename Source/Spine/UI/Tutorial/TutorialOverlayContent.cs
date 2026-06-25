namespace Spine.UI.Tutorial
{
    public readonly struct TutorialOverlayContent
    {
        public TutorialOverlayContent(
            string title,
            string body,
            string primaryButton = null,
            string dismissButton = null,
            string secondaryButton = null)
        {
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            PrimaryButton = primaryButton;
            SecondaryButton = string.IsNullOrEmpty(secondaryButton)
                ? "Related settings"
                : secondaryButton;
            DismissButton = string.IsNullOrEmpty(dismissButton)
                ? "Already know Better Work Tab"
                : dismissButton;
        }

        public string Title { get; }
        public string Body { get; }
        public string PrimaryButton { get; }
        public string SecondaryButton { get; }
        public string DismissButton { get; }
        public bool HasPrimaryButton => !string.IsNullOrEmpty(PrimaryButton);
        public bool HasSecondaryButton => !string.IsNullOrEmpty(SecondaryButton);
    }
}
