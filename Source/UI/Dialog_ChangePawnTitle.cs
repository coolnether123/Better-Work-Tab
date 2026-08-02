using RimWorld;
using Better_Work_Tab.Features.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public class Dialog_ChangePawnTitle : Window
    {
        private const int MaxTitleLength = 32;

        private readonly Pawn pawn;
        private string titleBuffer;
        private bool focusedField;

        public override Vector2 InitialSize => new Vector2(360f, 205f);

        public Dialog_ChangePawnTitle(Pawn pawn)
        {
            this.pawn = pawn;
            titleBuffer = PawnTitleUtility.GetCurrentTitle(pawn);

            forcePause = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            bool accept = Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (accept)
            {
                Event.current.Use();
            }

            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), "Change pawn title");

            string defaultTitle = PawnTitleUtility.GetDefaultTitle(pawn);
            string currentTitle = PawnTitleUtility.GetCurrentTitle(pawn);
            Widgets.Label(
                new Rect(0f, 28f, inRect.width, 24f),
                "Default: " + (string.IsNullOrEmpty(defaultTitle) ? "(none)" : defaultTitle));

            Rect titleRect = new Rect(0f, 58f, inRect.width, 32f);
            GUI.SetNextControlName("BWTChangePawnTitle");
            string nextTitle = Widgets.TextField(titleRect, titleBuffer ?? string.Empty);
            if (nextTitle != titleBuffer)
            {
                titleBuffer = nextTitle.Length <= MaxTitleLength
                    ? nextTitle
                    : nextTitle.Substring(0, MaxTitleLength);
                BWTGeneralTutorial.NotifyPawnTitleEdited();
            }

            if (!focusedField)
            {
                Verse.UI.FocusControl("BWTChangePawnTitle", this);
                focusedField = true;
            }

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, 96f, inRect.width, 22f), "Leave blank to use the default title.");
            Text.Font = GameFont.Small;

            Rect resetRect = new Rect(0f, inRect.height - 35f, 110f, 32f);
            Rect cancelRect = new Rect(inRect.width - 226f, inRect.height - 35f, 108f, 32f);
            Rect okRect = new Rect(inRect.width - 110f, inRect.height - 35f, 110f, 32f);

            if (Widgets.ButtonText(resetRect, "Use default"))
            {
                titleBuffer = string.Empty;
                ApplyTitle();
                Close();
                return;
            }

            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
                return;
            }

            if (Widgets.ButtonText(okRect, "OK") || accept)
            {
                if ((titleBuffer ?? string.Empty).Trim() == currentTitle)
                {
                    Close();
                    return;
                }

                ApplyTitle();
                Close();
            }

            if (BWTGeneralTutorial.IsActiveLesson(BWTGeneralTutorial.PawnAppearanceLesson, out int tutorialPhase))
            {
                if (tutorialPhase == 2)
                {
                    BWTTutorialGestureDemo.DrawExternal(
                        "pawn-title-entry",
                        titleRect,
                        BWTTutorialGestureDemo.GestureKind.TextEntry,
                        "BWT_Tutorial_Gesture_TypeTitle".Translate());
                }
                else if (tutorialPhase >= 3)
                {
                    BWTTutorialGestureDemo.DrawExternal(
                        "pawn-title-confirm",
                        okRect,
                        BWTTutorialGestureDemo.GestureKind.LeftClick,
                        "BWT_Tutorial_Gesture_Confirm".Translate());
                }
            }
        }

        private void ApplyTitle()
        {
            PawnTitleUtility.TrySetTitle(pawn, titleBuffer);
        }
    }
}
