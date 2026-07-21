using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal enum BWTTutorialLessonFeedbackValue
    {
        Unanswered,
        Keep,
        Revise,
        NoLesson
    }

    internal enum BWTTutorialBehaviorFeedbackValue
    {
        Unanswered,
        Yes,
        No,
        NotSure
    }

    internal sealed class BWTTutorialLessonFeedback : IExposable
    {
        public string lessonId = string.Empty;
        public BWTTutorialLessonFeedbackValue lessonValue;
        public BWTTutorialBehaviorFeedbackValue behaviorValue;
        public string note = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref lessonId, "lessonId", string.Empty);
            Scribe_Values.Look(ref lessonValue, "lessonValue", BWTTutorialLessonFeedbackValue.Unanswered);
            Scribe_Values.Look(ref behaviorValue, "behaviorValue", BWTTutorialBehaviorFeedbackValue.Unanswered);
            Scribe_Values.Look(ref note, "note", string.Empty);
        }

        internal bool HasResponse =>
            lessonValue != BWTTutorialLessonFeedbackValue.Unanswered ||
            behaviorValue != BWTTutorialBehaviorFeedbackValue.Unanswered ||
            !string.IsNullOrWhiteSpace(note);
    }

    /// <summary>Persistence and mutation only; it never advances a lesson.</summary>
    internal static class BWTTutorialFeedbackStore
    {
        internal static BWTTutorialLessonFeedback GetOrCreate(BetterWorkTabSettings settings, string lessonId)
        {
            Ensure(settings);
            BWTTutorialLessonFeedback response = settings.tutorialLessonFeedback.FirstOrDefault(
                item => string.Equals(item.lessonId, lessonId, StringComparison.Ordinal));
            if (response != null)
            {
                return response;
            }

            response = new BWTTutorialLessonFeedback { lessonId = lessonId ?? string.Empty };
            settings.tutorialLessonFeedback.Add(response);
            return response;
        }

        internal static BWTTutorialLessonFeedback Find(BetterWorkTabSettings settings, string lessonId)
        {
            Ensure(settings);
            return settings.tutorialLessonFeedback.FirstOrDefault(
                item => string.Equals(item.lessonId, lessonId, StringComparison.Ordinal));
        }

        internal static void Clear(BetterWorkTabSettings settings)
        {
            Ensure(settings);
            settings.tutorialLessonFeedback.Clear();
            settings.tutorialOverallFeedback = string.Empty;
            settings.Write();
        }

        internal static void Ensure(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.tutorialLessonFeedback ??= new List<BWTTutorialLessonFeedback>();
            settings.tutorialOverallFeedback ??= string.Empty;
        }
    }
}
