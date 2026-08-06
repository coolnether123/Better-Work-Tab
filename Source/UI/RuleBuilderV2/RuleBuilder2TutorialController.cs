using System.Collections.Generic;
using Better_Work_Tab.Features.Tutorial;
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

    /// <summary>
    /// Walks a player through building their first rule.
    ///
    /// The walkthrough is observational: each card explains one part of the
    /// builder and then waits for the player to actually do it, because the
    /// builder is a tool you learn by using rather than by reading. Every step
    /// that waits also offers Next, so a player who does not want to be led is
    /// never stuck behind a card, and Skip ends the whole thing.
    ///
    /// Steps advance from the same call sites the real UI already used --
    /// selecting a target, adding a condition, editing the action, previewing,
    /// confirming -- so the tutorial follows what the player did rather than
    /// maintaining a parallel idea of where they are.
    /// </summary>
    internal sealed class RuleBuilder2TutorialController
    {
        private const string SuggestionsHintSettingId = "bwt.ruleBuilder2.suggestionsHint.v1";
        private readonly TutorialTextViewport bodyViewport = new TutorialTextViewport();
        private readonly TutorialOverlayStyle cardStyle = new TutorialOverlayStyle();
        private bool suggestionsHintRequested;

        /// <summary>
        /// The walkthrough in order. Steps left out of this list stay in the
        /// enum because the chosen step is persisted as its numeric value, and
        /// renumbering would silently move a saved player to a different step.
        /// </summary>
        private static readonly RuleBuilder2TutorialStep[] Walkthrough =
        {
            RuleBuilder2TutorialStep.Welcome,
            RuleBuilder2TutorialStep.RuleDeck,
            RuleBuilder2TutorialStep.Target,
            RuleBuilder2TutorialStep.SubWorkTarget,
            RuleBuilder2TutorialStep.Condition,
            RuleBuilder2TutorialStep.Action,
            RuleBuilder2TutorialStep.Schedule,
            RuleBuilder2TutorialStep.Preview,
            RuleBuilder2TutorialStep.Confirm,
            RuleBuilder2TutorialStep.MatchedConditions,
            RuleBuilder2TutorialStep.ReplaySettings
        };

        /// <summary>
        /// The translation-key stem for each step. Written out rather than
        /// derived from the enum name because several stems are shorter than the
        /// step they belong to, and a mismatched key fails by rendering itself
        /// on the card instead of throwing.
        /// </summary>
        private static string KeyStem(RuleBuilder2TutorialStep step)
        {
            switch (step)
            {
                case RuleBuilder2TutorialStep.Welcome: return "Welcome";
                case RuleBuilder2TutorialStep.RuleDeck: return "Deck";
                case RuleBuilder2TutorialStep.Target: return "Target";
                case RuleBuilder2TutorialStep.SubWorkTarget: return "SubWork";
                case RuleBuilder2TutorialStep.Condition: return "Condition";
                case RuleBuilder2TutorialStep.Action: return "Action";
                case RuleBuilder2TutorialStep.Schedule: return "Schedule";
                case RuleBuilder2TutorialStep.Preview: return "Preview";
                case RuleBuilder2TutorialStep.Confirm: return "Confirm";
                case RuleBuilder2TutorialStep.MatchedConditions: return "Matched";
                case RuleBuilder2TutorialStep.ReplaySettings: return "Replay";
                default: return "Welcome";
            }
        }

        internal bool IsActive => IsWalkthroughActive || IsSuggestionsHintActive;

        internal RuleBuilder2TutorialStep ActiveStep =>
            IsWalkthroughActive
                ? CurrentStep
                : IsSuggestionsHintActive
                    ? RuleBuilder2TutorialStep.RuleDeck
                    : RuleBuilder2TutorialStep.Complete;

        internal void RequestSuggestionsHint()
        {
            if (!HasSeenSuggestionsHint())
            {
                suggestionsHintRequested = true;
            }
        }

        internal bool TryHandleInput(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects, Event evt)
        {
            if (!IsActive || evt == null)
            {
                return false;
            }

            Rect card = GetCardRect(bounds, focusRects);
            bool overCard = card.Contains(evt.mousePosition);
            if (overCard && bodyViewport.TryHandleScroll(GetBodyRect(card), GetBody(), evt))
            {
                return true;
            }

            if (evt.type == EventType.KeyDown &&
                (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                Advance();
                evt.Use();
                return true;
            }

            if (!overCard)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (TryGetButtonAt(card, evt.mousePosition, out TutorialButton button))
                {
                    switch (button)
                    {
                        case TutorialButton.Settings:
                            OpenSettings();
                            break;
                        case TutorialButton.Skip:
                            Finish();
                            break;
                        default:
                            Advance();
                            break;
                    }
                }

                evt.Use();
                return true;
            }

            return evt.type == EventType.MouseDrag || evt.type == EventType.ScrollWheel;
        }

        internal void Draw(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects)
        {
            if (!IsActive)
            {
                return;
            }

            Rect card = GetCardRect(bounds, focusRects);
            DrawDimOutside(card, bounds);
            DrawCard(card);

            // No animated click demo on the card's own button.
            //
            // A gesture demo earns its place when it is teaching a gesture the
            // player would not guess -- Ctrl-clicking a priority cell, dragging a
            // specific job between columns. Miming a left click on a button that
            // is already lit up in front of them teaches nothing, and it kept
            // drawing attention back to the thing they were about to click
            // anyway.
        }

        internal void ObserveTargetSelected(bool subWork)
        {
            CompleteIfWaitingOn(RuleBuilder2TutorialStep.Target);

            // Picking a specific job satisfies the specific-jobs step as well,
            // whether the player reached it by following the previous card or by
            // Ctrl-clicking straight past it. Leaving that card up to explain
            // something they just did would read as the tutorial not watching.
            if (subWork)
            {
                CompleteIfWaitingOn(RuleBuilder2TutorialStep.SubWorkTarget);
            }
        }

        internal void ObserveConditionAdded()
        {
            CompleteIfWaitingOn(RuleBuilder2TutorialStep.Condition);
        }

        internal void ObserveActionEdited()
        {
            CompleteIfWaitingOn(RuleBuilder2TutorialStep.Action);
        }

        internal void ObservePreview()
        {
            CompleteIfWaitingOn(RuleBuilder2TutorialStep.Preview);
        }

        internal void ObserveConfirmed()
        {
            CompleteIfWaitingOn(RuleBuilder2TutorialStep.Confirm);
        }

        internal void Reset()
        {
            if (BetterWorkTabMod.Settings == null)
            {
                return;
            }

            BetterWorkTabMod.Settings.showRuleBuilder2Tutorial = true;
            SetStep(RuleBuilder2TutorialStep.Welcome);
            bodyViewport.Reset();
        }

        internal void ResetOverlayAnimation()
        {
            bodyViewport.Reset();
        }

        /// <summary>
        /// Moves past a step the player has just carried out for real.
        ///
        /// Only advances when that step is the one on screen, so performing an
        /// action the tutorial has already covered -- or has not reached yet --
        /// does not skip anybody forward through cards they never read.
        /// </summary>
        private void CompleteIfWaitingOn(RuleBuilder2TutorialStep step)
        {
            if (IsWalkthroughActive && CurrentStep == step)
            {
                Advance();
            }
        }

        private void Advance()
        {
            if (!IsWalkthroughActive)
            {
                DismissSuggestionsHint();
                return;
            }

            int index = System.Array.IndexOf(Walkthrough, CurrentStep);
            if (index < 0 || index + 1 >= Walkthrough.Length)
            {
                Finish();
                return;
            }

            SetStep(Walkthrough[index + 1]);
            bodyViewport.Reset();
            BetterWorkTabMod.Settings?.Write();
        }

        private void Finish()
        {
            SetStep(RuleBuilder2TutorialStep.Complete);
            if (BetterWorkTabMod.Settings != null)
            {
                BetterWorkTabMod.Settings.showRuleBuilder2Tutorial = false;
                BetterWorkTabMod.Settings.Write();
            }

            BWTGeneralTutorial.NotifyRuleBuilderTutorialCompleted();
        }

        private void DismissSuggestionsHint()
        {
            BetterWorkTabMod.Settings?.RecordViewedSetting(SuggestionsHintSettingId);
            suggestionsHintRequested = false;
            BetterWorkTabMod.Settings?.Write();
        }

        private static bool IsWalkthroughActive =>
            BetterWorkTabMod.Settings?.showRuleBuilder2Tutorial == true &&
            CurrentStep != RuleBuilder2TutorialStep.Complete;

        private bool IsSuggestionsHintActive =>
            !IsWalkthroughActive && suggestionsHintRequested && !HasSeenSuggestionsHint();

        private static RuleBuilder2TutorialStep CurrentStep
        {
            get
            {
                int raw = BetterWorkTabMod.Settings?.ruleBuilder2TutorialStep ?? 0;
                return System.Enum.IsDefined(typeof(RuleBuilder2TutorialStep), raw)
                    ? (RuleBuilder2TutorialStep)raw
                    : RuleBuilder2TutorialStep.Welcome;
            }
        }

        private Rect GetCardRect(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects)
        {
            RuleBuilder2TutorialStep key = ActiveStep;
            if (focusRects != null &&
                focusRects.TryGetValue(key, out Rect card) &&
                card.width > 0f &&
                card.height > 0f)
            {
                card.height = CalculateCardHeight(card.width);
                return ClampCard(card, bounds.ContractedBy(12f));
            }

            float width = 430f;
            float height = CalculateCardHeight(width);
            Rect fallback = new Rect(
                bounds.center.x - width / 2f,
                bounds.center.y - height / 2f,
                width,
                height);
            return ClampCard(fallback, bounds.ContractedBy(12f));
        }

        private float CalculateCardHeight(float width)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float innerWidth = Mathf.Max(1f, width - 36f);
            float bodyHeight = Mathf.Ceil(Text.CalcHeight(GetBody(), innerWidth));
            Text.Font = previousFont;
            return 128f + Mathf.Max(40f, bodyHeight);
        }

        private static Rect ClampCard(Rect card, Rect bounds)
        {
            float x = Mathf.Clamp(card.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - card.width));
            float y = Mathf.Clamp(card.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - card.height));
            return new Rect(x, y, card.width, card.height);
        }

        private static void DrawDimOutside(Rect card, Rect bounds)
        {
            Color previous = GUI.color;
            Color dim = new Color(0f, 0f, 0f, 0.14f);
            GUI.color = dim;
            Widgets.DrawBoxSolid(new Rect(bounds.xMin, bounds.yMin, bounds.width, Mathf.Max(0f, card.yMin - bounds.yMin)), dim);
            Widgets.DrawBoxSolid(new Rect(bounds.xMin, card.yMax, bounds.width, Mathf.Max(0f, bounds.yMax - card.yMax)), dim);
            Widgets.DrawBoxSolid(new Rect(bounds.xMin, card.yMin, Mathf.Max(0f, card.xMin - bounds.xMin), card.height), dim);
            Widgets.DrawBoxSolid(new Rect(card.xMax, card.yMin, Mathf.Max(0f, bounds.xMax - card.xMax), card.height), dim);
            GUI.color = previous;
        }

        private void DrawCard(Rect rect)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;

            TutorialCardRenderer.Draw(rect, cardStyle);

            Rect inner = rect.ContractedBy(18f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f), GetTitle());

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            bodyViewport.Draw(GetBodyRect(rect), GetBody());

            // Input is handled in TryHandleInput; drawing the buttons here keeps
            // them painted and hit-testable on the same frame.
            Widgets.ButtonText(GetAdvanceButtonRect(rect), GetAdvanceLabel());

            if (IsWalkthroughActive)
            {
                Widgets.ButtonText(GetSkipButtonRect(rect), T("BWT_RuleBuilder2_Tutorial_Skip"));
            }
            else
            {
                Widgets.ButtonText(GetSettingsButtonRect(rect), T("BWT_RuleBuilder2_Tutorial_Settings"));
            }

            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private bool TryGetButtonAt(Rect card, Vector2 mousePosition, out TutorialButton button)
        {
            if (GetAdvanceButtonRect(card).Contains(mousePosition))
            {
                button = TutorialButton.Dismiss;
                return true;
            }

            if (IsWalkthroughActive && GetSkipButtonRect(card).Contains(mousePosition))
            {
                button = TutorialButton.Skip;
                return true;
            }

            if (!IsWalkthroughActive && GetSettingsButtonRect(card).Contains(mousePosition))
            {
                button = TutorialButton.Settings;
                return true;
            }

            button = TutorialButton.None;
            return false;
        }

        private Rect GetAdvanceButtonRect(Rect card)
        {
            Rect inner = card.ContractedBy(18f);
            float width = IsWalkthroughActive ? 110f : 130f;
            return new Rect(inner.xMax - width, inner.yMax - 32f, width, 32f);
        }

        private static Rect GetSkipButtonRect(Rect card)
        {
            Rect inner = card.ContractedBy(18f);
            return new Rect(inner.x, inner.yMax - 32f, 140f, 32f);
        }

        private static Rect GetSettingsButtonRect(Rect card)
        {
            Rect inner = card.ContractedBy(18f);
            return new Rect(inner.x, inner.yMax - 32f, 150f, 32f);
        }

        private static Rect GetBodyRect(Rect card)
        {
            Rect inner = card.ContractedBy(18f);
            float y = inner.y + 38f;
            float buttonTop = inner.yMax - 32f;
            return new Rect(inner.x, y, inner.width, Mathf.Max(1f, buttonTop - y - 10f));
        }

        private string GetAdvanceLabel()
        {
            if (!IsWalkthroughActive)
            {
                return T("BWT_RuleBuilder2_Tutorial_GotIt");
            }

            return CurrentStep == Walkthrough[Walkthrough.Length - 1]
                ? T("BWT_RuleBuilder2_Tutorial_Finish")
                : T("BWT_RuleBuilder2_Tutorial_Next");
        }

        private string GetTitle()
        {
            return IsWalkthroughActive
                ? T("BWT_RuleBuilder2_Tutorial_" + KeyStem(CurrentStep) + "Title")
                : T("BWT_RuleBuilder2_Tutorial_SuggestionsTitle");
        }

        private string GetBody()
        {
            return IsWalkthroughActive
                ? T("BWT_RuleBuilder2_Tutorial_" + KeyStem(CurrentStep) + "Body")
                : T("BWT_RuleBuilder2_Tutorial_SuggestionsBody");
        }

        private static bool HasSeenSuggestionsHint()
        {
            return BetterWorkTabMod.Settings?.HasViewedSetting(SuggestionsHintSettingId) == true;
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

        private enum TutorialButton
        {
            None,
            Dismiss,
            Skip,
            Settings
        }
    }
}
