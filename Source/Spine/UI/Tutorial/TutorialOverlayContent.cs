namespace Spine.UI.Tutorial
{
    public readonly struct TutorialOverlayContent
    {
        public TutorialOverlayContent(
            string title,
            string body,
            string primaryButton = null,
            string dismissButton = null,
            string secondaryButton = "Related settings",
            string tertiaryButton = null)
        {
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            PrimaryButton = primaryButton;
            // Omitted keeps the common settings shortcut; an explicit null lets
            // decision screens present only the choices that belong there.
            SecondaryButton = secondaryButton;
            TertiaryButton = tertiaryButton;
            DismissButton = string.IsNullOrEmpty(dismissButton)
                ? "Skip for now"
                : dismissButton;
        }

        public string Title { get; }
        public string Body { get; }
        public string PrimaryButton { get; }
        public string SecondaryButton { get; }
        public string TertiaryButton { get; }
        public string DismissButton { get; }
        public bool HasPrimaryButton => !string.IsNullOrEmpty(PrimaryButton);
        public bool HasSecondaryButton => !string.IsNullOrEmpty(SecondaryButton);
        public bool HasTertiaryButton => !string.IsNullOrEmpty(TertiaryButton);
    }
}
