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

        public override Vector2 InitialSize => new Vector2(590f, 330f);

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
            AdvanceForSmokeTest();
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

        internal void AdvanceForSmokeTest()
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

        internal bool StatePreservedForSmokeTest => StateWasPreserved();
    }
}
