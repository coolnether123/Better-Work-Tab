using System.Collections.Generic;
using RimWorld;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal enum RuleBuilder2TutorialStep
    {
        Welcome,
        BlankRuleset,
        RuleDeck,
        Target,
        SubWorkTarget,
        Condition,
        Action,
        Schedule,
        Preview,
        Confirm,
        MatchedConditions,
        ReplaySettings,
        Complete
    }

    internal sealed class RuleBuilder2TutorialController
    {
        private readonly TutorialOverlayController overlay = new TutorialOverlayController();

        internal bool IsActive =>
            BetterWorkTabMod.Settings?.showRuleBuilder2Tutorial == true &&
            CurrentStep != RuleBuilder2TutorialStep.Complete;

        private RuleBuilder2TutorialStep CurrentStep
        {
            get
            {
                int raw = BetterWorkTabMod.Settings?.ruleBuilder2TutorialStep ?? 0;
                return System.Enum.IsDefined(typeof(RuleBuilder2TutorialStep), raw)
                    ? (RuleBuilder2TutorialStep)raw
                    : RuleBuilder2TutorialStep.Welcome;
            }
        }

        internal bool TryHandleInput(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects, Event evt)
        {
            if (!IsActive)
            {
                return false;
            }

            RuleBuilder2TutorialStep step = CurrentStep;
            return overlay.TryHandleInput(
                bounds,
                BuildContent(step),
                BuildFocusList(step, focusRects),
                evt,
                () => Advance(step),
                OpenSettings,
                null,
                Skip);
        }

        internal void Draw(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects)
        {
            if (!IsActive)
            {
                return;
            }

            RuleBuilder2TutorialStep step = CurrentStep;
            overlay.Draw(
                bounds,
                BuildContent(step),
                BuildFocusList(step, focusRects),
                BuildHints(step),
                () => Advance(step),
                OpenSettings,
                null,
                Skip);
        }

        internal void ObserveTargetSelected(bool subWork)
        {
            if (!IsActive)
            {
                return;
            }

            if (CurrentStep == RuleBuilder2TutorialStep.Target)
            {
                SetStep(subWork ? RuleBuilder2TutorialStep.Condition : RuleBuilder2TutorialStep.SubWorkTarget);
            }
            else if (CurrentStep == RuleBuilder2TutorialStep.SubWorkTarget && subWork)
            {
                SetStep(RuleBuilder2TutorialStep.Condition);
            }
        }

        internal void ObserveConditionAdded()
        {
            if (IsActive && CurrentStep == RuleBuilder2TutorialStep.Condition)
            {
                SetStep(RuleBuilder2TutorialStep.Action);
            }
        }

        internal void ObserveActionEdited()
        {
            if (IsActive && CurrentStep == RuleBuilder2TutorialStep.Action)
            {
                SetStep(RuleBuilder2TutorialStep.Schedule);
            }
        }

        internal void ObservePreview()
        {
            if (IsActive && CurrentStep == RuleBuilder2TutorialStep.Preview)
            {
                SetStep(RuleBuilder2TutorialStep.Confirm);
            }
        }

        internal void ObserveConfirmed()
        {
            if (IsActive && CurrentStep == RuleBuilder2TutorialStep.Confirm)
            {
                SetStep(RuleBuilder2TutorialStep.MatchedConditions);
            }
        }

        internal void Reset()
        {
            SetStep(RuleBuilder2TutorialStep.Welcome);
            overlay.ResetAnimation();
        }

        private static TutorialOverlayContent BuildContent(RuleBuilder2TutorialStep step)
        {
            switch (step)
            {
                case RuleBuilder2TutorialStep.Welcome:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_WelcomeTitle"),
                        T("BWT_RuleBuilder2_Tutorial_WelcomeBody"),
                        T("BWT_RuleBuilder2_Tutorial_Start"),
                        T("BWT_RuleBuilder2_Tutorial_Skip"));
                case RuleBuilder2TutorialStep.BlankRuleset:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_CreateTitle"),
                        T("BWT_RuleBuilder2_Tutorial_CreateBody"),
                        T("BWT_RuleBuilder2_Tutorial_Next"));
                case RuleBuilder2TutorialStep.RuleDeck:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_DeckTitle"),
                        T("BWT_RuleBuilder2_Tutorial_DeckBody"),
                        T("BWT_RuleBuilder2_Tutorial_Next"));
                case RuleBuilder2TutorialStep.Target:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_TargetTitle"),
                        T("BWT_RuleBuilder2_Tutorial_TargetBody"),
                        null);
                case RuleBuilder2TutorialStep.SubWorkTarget:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_SubWorkTitle"),
                        T("BWT_RuleBuilder2_Tutorial_SubWorkBody"),
                        T("BWT_RuleBuilder2_Tutorial_SkipSubWork"));
                case RuleBuilder2TutorialStep.Condition:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_ConditionTitle"),
                        T("BWT_RuleBuilder2_Tutorial_ConditionBody"),
                        null);
                case RuleBuilder2TutorialStep.Action:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_ActionTitle"),
                        T("BWT_RuleBuilder2_Tutorial_ActionBody"),
                        null);
                case RuleBuilder2TutorialStep.Schedule:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_ScheduleTitle"),
                        T("BWT_RuleBuilder2_Tutorial_ScheduleBody"),
                        T("BWT_RuleBuilder2_Tutorial_Next"));
                case RuleBuilder2TutorialStep.Preview:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_PreviewTitle"),
                        T("BWT_RuleBuilder2_Tutorial_PreviewBody"),
                        null);
                case RuleBuilder2TutorialStep.Confirm:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_ConfirmTitle"),
                        T("BWT_RuleBuilder2_Tutorial_ConfirmBody"),
                        null);
                case RuleBuilder2TutorialStep.MatchedConditions:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_MatchedTitle"),
                        T("BWT_RuleBuilder2_Tutorial_MatchedBody"),
                        T("BWT_RuleBuilder2_Tutorial_Next"));
                case RuleBuilder2TutorialStep.ReplaySettings:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_ReplayTitle"),
                        T("BWT_RuleBuilder2_Tutorial_ReplayBody"),
                        T("BWT_RuleBuilder2_Tutorial_Finish"));
                default:
                    return new TutorialOverlayContent(
                        T("BWT_RuleBuilder2_Tutorial_WelcomeTitle"),
                        T("BWT_RuleBuilder2_Tutorial_CompleteBody"),
                        T("BWT_RuleBuilder2_Tutorial_Finish"));
            }
        }

        private static List<Rect> BuildFocusList(RuleBuilder2TutorialStep step, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects)
        {
            if (focusRects != null && focusRects.TryGetValue(step, out Rect rect) && rect.width > 0f && rect.height > 0f)
            {
                return new List<Rect> { rect };
            }

            return new List<Rect>();
        }

        private static List<TutorialOverlayShortcutHint> BuildHints(RuleBuilder2TutorialStep step)
        {
            if (step == RuleBuilder2TutorialStep.Target)
            {
                return new List<TutorialOverlayShortcutHint>
                {
                    new TutorialOverlayShortcutHint(T("BWT_RuleBuilder2_Tutorial_ClickHeaderHint"), 0)
                };
            }

            return new List<TutorialOverlayShortcutHint>();
        }

        private void Advance(RuleBuilder2TutorialStep step)
        {
            switch (step)
            {
                case RuleBuilder2TutorialStep.Target:
                case RuleBuilder2TutorialStep.Condition:
                case RuleBuilder2TutorialStep.Action:
                case RuleBuilder2TutorialStep.Preview:
                case RuleBuilder2TutorialStep.Confirm:
                    return;
                case RuleBuilder2TutorialStep.SubWorkTarget:
                    SetStep(RuleBuilder2TutorialStep.Condition);
                    return;
                case RuleBuilder2TutorialStep.ReplaySettings:
                case RuleBuilder2TutorialStep.Complete:
                    SetStep(RuleBuilder2TutorialStep.Complete);
                    BetterWorkTabMod.Settings.showRuleBuilder2Tutorial = false;
                    return;
                default:
                    SetStep(step + 1);
                    return;
            }
        }

        private void Skip()
        {
            SetStep(RuleBuilder2TutorialStep.Complete);
            if (BetterWorkTabMod.Settings != null)
            {
                BetterWorkTabMod.Settings.showRuleBuilder2Tutorial = false;
            }
        }

        private static void OpenSettings()
        {
            var mod = LoadedModManager.GetMod<BetterWorkTabMod>();
            if (mod != null)
            {
                Find.WindowStack.Add(new Dialog_ModSettings(mod));
            }
        }

        private static void SetStep(RuleBuilder2TutorialStep step)
        {
            if (BetterWorkTabMod.Settings == null)
            {
                return;
            }

            BetterWorkTabMod.Settings.ruleBuilder2TutorialStep = (int)step;
        }

        private static string T(string key)
        {
            return key.CanTranslate() ? key.Translate().ToString() : key;
        }
    }
}
