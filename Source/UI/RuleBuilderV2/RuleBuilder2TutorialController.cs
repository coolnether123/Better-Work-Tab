using System.Collections.Generic;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.UI;
using RimWorld;
using Spine.UI.Tutorial;
using Spine.UI.WidgetExtensions;
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

            if (evt.type == EventType.KeyDown &&
                (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                Advance();
                evt.Use();
                return true;
            }

            // Only the band's own two buttons are claimed. Everything else in the
            // builder stays live, because the walkthrough advances by watching
            // the player use it -- swallowing input would make that impossible.
            BWTTutorialStripLayout layout = BuildStripLayout(bounds);
            if (evt.type != EventType.MouseDown && evt.type != EventType.MouseUp)
            {
                return false;
            }

            bool overSkip = layout.SkipRect.Contains(evt.mousePosition);
            bool overExit = layout.ExitRect.Contains(evt.mousePosition);
            if (!overSkip && !overExit)
            {
                return false;
            }

            if (evt.type == EventType.MouseUp)
            {
                if (overExit)
                {
                    Finish();
                }
                else
                {
                    Advance();
                }
            }

            evt.Use();
            return true;
        }

        /// <summary>
        /// Draws the walkthrough as a docked instruction band across the top of
        /// the builder, plus a gold outline and a gesture demo over whatever the
        /// current step is pointing at.
        ///
        /// The band is the same surface the Work-tab tutorial uses, for the same
        /// reason: it keeps one position and shape regardless of what the panel
        /// beneath it is doing, so the instruction never moves or covers the
        /// control it is describing. Floating cards did both.
        /// </summary>
        internal void Draw(Rect bounds, Dictionary<RuleBuilder2TutorialStep, Rect> focusRects)
        {
            if (!IsActive)
            {
                return;
            }

            BWTTutorialStripLayout layout = BuildStripLayout(bounds);
            BWTTutorialStrip.Draw(layout, BuildStripContent());

            if (!IsWalkthroughActive)
            {
                return;
            }

            // Point at the live control, never at the band's own buttons, and
            // never at a rect that is not currently on screen.
            if (focusRects != null &&
                focusRects.TryGetValue(CurrentStep, out Rect anchor) &&
                anchor.width > 1f &&
                anchor.height > 1f &&
                bounds.Overlaps(anchor))
            {
                ConnectedOutlineDrawer.DrawClosed(
                    new[]
                    {
                        new Vector2(anchor.xMin, anchor.yMin),
                        new Vector2(anchor.xMax, anchor.yMin),
                        new Vector2(anchor.xMax, anchor.yMax),
                        new Vector2(anchor.xMin, anchor.yMax)
                    },
                    BWTUiPalette.TutorAccent,
                    2f);

                // No prompt on the demo: the band states the action a few pixels
                // away, and a floating badge repeating it was rejected the last
                // time this tutorial was designed.
                BWTTutorialGestureDemo.DrawExternal(
                    "rule-builder:" + CurrentStep,
                    anchor,
                    GestureFor(CurrentStep));
            }
        }

        /// <summary>
        /// The gesture each step is asking for, so the demo shows the actual
        /// input rather than a generic click.
        /// </summary>
        private static BWTTutorialGestureDemo.GestureKind GestureFor(RuleBuilder2TutorialStep step)
        {
            switch (step)
            {
                case RuleBuilder2TutorialStep.SubWorkTarget:
                    return BWTTutorialGestureDemo.GestureKind.CtrlClick;
                case RuleBuilder2TutorialStep.Welcome:
                case RuleBuilder2TutorialStep.ReplaySettings:
                    return BWTTutorialGestureDemo.GestureKind.None;
                default:
                    return BWTTutorialGestureDemo.GestureKind.LeftClick;
            }
        }

        private BWTTutorialStripContent BuildStripContent()
        {
            if (!IsWalkthroughActive)
            {
                return new BWTTutorialStripContent(
                    BWTTutorialStripMode.Lesson,
                    T("BWT_RuleBuilder2_Tutorial_SuggestionsBody"),
                    0,
                    0);
            }

            int index = System.Array.IndexOf(Walkthrough, CurrentStep);
            return new BWTTutorialStripContent(
                BWTTutorialStripMode.Lesson,
                GetInstruction(),
                Mathf.Max(0, index),
                Walkthrough.Length);
        }

        private static BWTTutorialStripLayout BuildStripLayout(Rect bounds)
        {
            Rect strip = new Rect(
                bounds.x + 8f,
                bounds.y + 6f,
                Mathf.Max(1f, bounds.width - 16f),
                BWTTutorialStrip.RowHeight);

            Rect inner = strip.ContractedBy(6f);
            float buttonWidth = 92f;
            Rect exit = new Rect(inner.xMax - buttonWidth, inner.y, buttonWidth, inner.height);
            Rect skip = new Rect(exit.xMin - buttonWidth - 6f, inner.y, buttonWidth, inner.height);
            Rect progress = new Rect(inner.x, inner.y, 74f, inner.height);
            Rect instruction = new Rect(
                progress.xMax + 8f,
                inner.y,
                Mathf.Max(1f, skip.xMin - progress.xMax - 16f),
                inner.height);

            return new BWTTutorialStripLayout(strip, progress, instruction, skip, exit);
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

        /// <summary>
        /// The one line the band shows. Written to say what to do next rather
        /// than to describe the panel, and short enough to sit on a single row
        /// at the widths the builder actually opens at.
        /// </summary>
        private string GetInstruction()
        {
            return T("BWT_RuleBuilder2_Strip_" + KeyStem(CurrentStep));
        }

        private static bool HasSeenSuggestionsHint()
        {
            return BetterWorkTabMod.Settings?.HasViewedSetting(SuggestionsHintSettingId) == true;
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
