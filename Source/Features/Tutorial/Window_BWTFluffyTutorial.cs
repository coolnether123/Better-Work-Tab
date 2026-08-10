using System;
using System.Linq;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// Remains open while the Work tab owner changes, so the player can prove
    /// both interfaces coexist and return to the exact BWT configuration.
    /// </summary>
    internal sealed class Window_BWTFluffyTutorial : Window
    {
        private readonly Action onCompleted;
        private readonly Action onDismissed;
        private readonly BetterWorkTabSettings.SubWorkDrilldownStyle initialPresentation;
        private readonly string initialColumnOrder;
        private readonly TutorialTextViewport bodyViewport = new TutorialTextViewport();
        private int phase;
        private bool completed;

        private const float WindowWidth = 590f;
        private const float BodyTop = 46f;
        private const float BodyToButtonGap = 12f;
        private const float ButtonBlockHeight = 42f;

        // Every phase draws its body into the same window, so the height has to
        // suit the longest of them.
        private static readonly string[] BodyKeys =
        {
            "BWT_Tutorial_FluffyCoexistence_ActionSwitch",
            "BWT_Tutorial_FluffyCoexistence_ActionReturn",
            "BWT_Tutorial_FluffyCoexistence_ActionPreserved",
            "BWT_Tutorial_FluffyCoexistence_ActionChanged"
        };

        public override Vector2 InitialSize => new Vector2(WindowWidth, ResolveHeight());

        /// <summary>
        /// Sizes the window to the tallest phase body rather than a fixed height.
        /// The bodies are two lines, so a fixed 330px left roughly a third of the
        /// panel empty between the text and the button, which read as unfinished.
        /// Measuring every phase keeps the window from resizing as it advances.
        /// </summary>
        private float ResolveHeight()
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float contentWidth = WindowWidth - (Margin * 2f);
            float tallestBody = 0f;
            for (int i = 0; i < BodyKeys.Length; i++)
            {
                tallestBody = Mathf.Max(tallestBody, Text.CalcHeight(BodyKeys[i].Translate(), contentWidth));
            }
            Text.Font = previousFont;

            return BodyTop + tallestBody + BodyToButtonGap + ButtonBlockHeight + (Margin * 2f);
        }

        internal Window_BWTFluffyTutorial(Action onCompleted, Action onDismissed = null)
        {
            this.onCompleted = onCompleted;
            this.onDismissed = onDismissed;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            initialPresentation = settings?.subWorkDrilldownStyle ?? DefaultSettings.subWorkDrilldownStyle;
            initialColumnOrder = string.Join("|", settings?.workColumnOrderDefNames ?? Enumerable.Empty<string>());
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!completed)
            {
                onDismissed?.Invoke();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f),
                "BWT_Tutorial_FluffyCoexistence_Title".Translate());
            Text.Font = GameFont.Small;
            string body = phase == 0
                ? "BWT_Tutorial_FluffyCoexistence_ActionSwitch".Translate()
                : phase == 1
                    ? "BWT_Tutorial_FluffyCoexistence_ActionReturn".Translate()
                    : StateWasPreserved()
                        ? "BWT_Tutorial_FluffyCoexistence_ActionPreserved".Translate()
                        : "BWT_Tutorial_FluffyCoexistence_ActionChanged".Translate();

            Rect button = new Rect(inRect.xMax - 230f, inRect.yMax - 42f, 230f, 36f);
            Rect bodyRect = new Rect(
                inRect.x,
                inRect.y + 46f,
                inRect.width,
                Mathf.Max(1f, button.yMin - inRect.y - 58f));
            bodyViewport.Draw(bodyRect, body);
            string label = phase == 0
                ? "BWT_Tutorial_FluffySwitch".Translate()
                : phase == 1
                    ? "BWT_Tutorial_BWTReturn".Translate()
                    : "BWT_Tutorial_FinishLesson".Translate();
            bool clicked = Widgets.ButtonText(button, label);
            BWTTutorialGestureDemo.DrawExternal(
                "fluffy-transition-" + phase,
                button,
                BWTTutorialGestureDemo.GestureKind.LeftClick,
                phase < 2
                    ? "BWT_Tutorial_Gesture_Continue".Translate()
                    : "BWT_Tutorial_Gesture_Complete".Translate());
            if (!clicked)
            {
                return;
            }
            Advance();
        }

        private bool StateWasPreserved()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null &&
                   settings.subWorkDrilldownStyle == initialPresentation &&
                   string.Equals(
                       string.Join("|", settings.workColumnOrderDefNames ?? Enumerable.Empty<string>()),
                       initialColumnOrder,
                       StringComparison.Ordinal);
        }

        private void Advance()
        {
            if (phase == 0)
            {
                FluffyWorkTabGateway.SwitchToExternalWorkTab();
                phase = 1;
            }
            else if (phase == 1)
            {
                FluffyWorkTabGateway.SwitchToBetterWorkTab();
                phase = 2;
            }
            else
            {
                completed = true;
                onCompleted?.Invoke();
                Close();
            }
        }
    }
}
