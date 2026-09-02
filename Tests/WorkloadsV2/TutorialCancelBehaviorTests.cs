using System.Collections.Generic;
using Spine.UI.Tutorial;
using UnityEngine;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class TutorialCancelBehaviorTests
    {
        private const int CompletionOutcomePhase = 1000;

        public static void Run()
        {
            TutorialCancelTransitions();
            RoutedEscapeUsesTheSameOperation();
            WindowCancelPrecedence();
        }

        private static void TutorialCancelTransitions()
        {
            var welcome = new State(true, false, string.Empty, 0);
            AssertCancel(welcome, TutorialPresentation.Welcome, TutorialCancelAction.Pause);
            TestAssert.False(welcome.Show, "welcome Escape pauses the tutorial");
            TestAssert.False(welcome.WelcomeCompleted, "welcome Escape leaves welcome unresolved");

            var selector = new State(true, true, string.Empty, 0);
            AssertCancel(selector, TutorialPresentation.Selector, TutorialCancelAction.Pause);
            TestAssert.False(selector.Show, "selector Escape pauses the tutorial");
            TestAssert.True(selector.WelcomeCompleted, "selector Escape preserves welcome completion");

            var lesson = new State(true, true, "priority.change", 2);
            AssertCancel(lesson, TutorialPresentation.Lesson, TutorialCancelAction.ReturnToSelection);
            AssertLessonCleared(lesson, "unfinished lesson Escape");

            var completed = new State(
                true,
                true,
                "priority.change",
                CompletionOutcomePhase,
                new List<string> { "priority.change" });
            AssertCancel(completed, TutorialPresentation.Lesson, TutorialCancelAction.AcknowledgeLessonOutcome);
            AssertLessonCleared(completed, "completed lesson Escape");
            TestAssert.True(
                TutorialProgressTransitions.IsCompleted(completed.Completed, "priority.change"),
                "completed lesson Escape preserves completion progress");
        }

        private static void RoutedEscapeUsesTheSameOperation()
        {
            var state = new State(true, true, "priority.change", 2);
            var owner = new Owner(state);
            Event evt = Escape();

            TestAssert.True(owner.TryHandleRoutedInput(evt), "routed Escape is handled by tutorial ownership");
            TestAssert.Equal(EventType.Used, evt.type, "routed Escape consumes its Event");
            AssertLessonCleared(state, "routed Escape");

            Event unrelated = new Event { type = EventType.KeyUp, keyCode = KeyCode.Escape };
            TestAssert.False(owner.TryHandleRoutedInput(unrelated), "KeyUp does not enter routed Escape handling");
            TestAssert.Equal(EventType.KeyUp, unrelated.type, "unhandled KeyUp remains available");
        }

        private static void WindowCancelPrecedence()
        {
            var subWorkState = new State(true, true, string.Empty, 0);
            var subWorkWindow = new Window(true, new Owner(subWorkState));
            Event subWorkEvent = Escape();
            subWorkWindow.HandleCancel(subWorkEvent);
            TestAssert.Equal(1, subWorkWindow.SubWorkCalls, "sub-work owns native Escape first");
            TestAssert.Equal(0, subWorkWindow.TutorialCalls, "sub-work prevents tutorial Escape");
            TestAssert.Equal(0, subWorkWindow.BaseCalls, "sub-work prevents base close");
            TestAssert.False(subWorkWindow.Closed, "sub-work keeps the Work tab open");
            TestAssert.True(subWorkState.Show, "sub-work leaves tutorial state unchanged");
            TestAssert.Equal(EventType.Used, subWorkEvent.type, "sub-work consumes native Escape");

            var tutorialState = new State(true, true, string.Empty, 0);
            var tutorialWindow = new Window(false, new Owner(tutorialState));
            Event tutorialEvent = Escape();
            tutorialWindow.HandleCancel(tutorialEvent);
            TestAssert.Equal(1, tutorialWindow.TutorialCalls, "tutorial owns Escape after sub-work");
            TestAssert.Equal(0, tutorialWindow.BaseCalls, "tutorial prevents base close");
            TestAssert.False(tutorialWindow.Closed, "tutorial keeps the Work tab open");
            TestAssert.False(tutorialState.Show, "tutorial Escape pauses selector state");
            TestAssert.Equal(EventType.Used, tutorialEvent.type, "tutorial consumes native Escape");

            var baseWindow = new Window(false, null);
            Event baseEvent = Escape();
            baseWindow.HandleCancel(baseEvent);
            TestAssert.Equal(1, baseWindow.BaseCalls, "base owns Escape when no tutorial is active");
            TestAssert.True(baseWindow.Closed, "base Escape closes the Work tab");
            TestAssert.Equal(EventType.Used, baseEvent.type, "base consumes native Escape");
        }

        private static void AssertCancel(State state, TutorialPresentation presentation, TutorialCancelAction action)
        {
            TestAssert.Equal(
                presentation,
                TutorialPresentationPolicy.Resolve(state.Show, state.WelcomeCompleted, state.ActiveLesson),
                "cancel presentation");
            TestAssert.Equal(
                action,
                TutorialCancelPolicy.Resolve(presentation, state.LessonPhase == CompletionOutcomePhase),
                "cancel action");

            Event evt = Escape();
            TestAssert.True(new Owner(state).TryHandleNativeCancel(evt), "tutorial owns native Escape");
            TestAssert.Equal(EventType.Used, evt.type, "tutorial native Escape consumes its Event");
        }

        private static void AssertLessonCleared(State state, string operation)
        {
            TestAssert.Equal(string.Empty, state.ActiveLesson, operation + " clears the active lesson");
            TestAssert.Equal(0, state.LessonPhase, operation + " resets the lesson phase");
            TestAssert.True(state.Show, operation + " keeps the tutorial active");
        }

        private static Event Escape()
        {
            return new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape };
        }

        private sealed class State
        {
            internal State(bool show, bool welcomeCompleted, string activeLesson, int lessonPhase, List<string> completed = null)
            {
                Show = show;
                WelcomeCompleted = welcomeCompleted;
                ActiveLesson = activeLesson;
                LessonPhase = lessonPhase;
                Completed = completed ?? new List<string>();
            }

            internal bool Show;
            internal bool WelcomeCompleted;
            internal string ActiveLesson;
            internal int LessonPhase;
            internal List<string> Completed;
        }

        private sealed class Owner
        {
            private readonly State state;

            internal Owner(State state) { this.state = state; }

            internal bool TryHandleNativeCancel(Event evt)
            {
                if (!TryHandleCancelKey()) return false;
                evt.Use();
                return true;
            }

            internal bool TryHandleRoutedInput(Event evt)
            {
                return evt.type == EventType.KeyDown &&
                    evt.keyCode == KeyCode.Escape &&
                    TryHandleNativeCancel(evt);
            }

            internal bool TryHandleCancelKey()
            {
                TutorialPresentation presentation = TutorialPresentationPolicy.Resolve(
                    state.Show, state.WelcomeCompleted, state.ActiveLesson);
                if (presentation == TutorialPresentation.Hidden) return false;

                TutorialCancelAction action = TutorialCancelPolicy.Resolve(
                    presentation, state.LessonPhase == CompletionOutcomePhase);
                switch (action)
                {
                    case TutorialCancelAction.Pause:
                        state.Show = false;
                        break;
                    case TutorialCancelAction.ReturnToSelection:
                    case TutorialCancelAction.AcknowledgeLessonOutcome:
                        TutorialProgressTransitions.ReturnToSelection(
                            ref state.ActiveLesson, ref state.LessonPhase);
                        break;
                }
                return true;
            }
        }

        private sealed class Window
        {
            private readonly bool subWorkActive;
            private readonly Owner tutorial;

            internal Window(bool subWorkActive, Owner tutorial)
            {
                this.subWorkActive = subWorkActive;
                this.tutorial = tutorial;
            }

            internal int SubWorkCalls;
            internal int TutorialCalls;
            internal int BaseCalls;
            internal bool Closed;

            internal void HandleCancel(Event evt)
            {
                if (subWorkActive)
                {
                    SubWorkCalls++;
                    evt.Use();
                    return;
                }

                if (tutorial != null)
                {
                    TutorialCalls++;
                    if (tutorial.TryHandleCancelKey())
                    {
                        evt.Use();
                        return;
                    }
                }

                BaseCalls++;
                Closed = true;
                evt.Use();
            }
        }
    }
}
