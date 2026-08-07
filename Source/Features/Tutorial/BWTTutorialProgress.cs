using System.Collections.Generic;
using Spine.UI.Tutorial;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// What the tour considers finished, and why.
    ///
    /// A lesson is settled either because the player worked through it or
    /// because their colony already showed the feature in use. Progress counts,
    /// anchor pulsing and the "is the course done" test all want the union, so
    /// they read <see cref="IsSettled"/>. Only the list rows care about the
    /// difference, and they say so rather than claiming a lesson was taught.
    /// </summary>
    internal readonly struct BWTTutorialProgressSnapshot
    {
        private readonly ICollection<string> completed;
        private readonly ICollection<string> alreadyUsed;

        internal BWTTutorialProgressSnapshot(
            ICollection<string> completed,
            ICollection<string> alreadyUsed)
        {
            this.completed = completed;
            this.alreadyUsed = alreadyUsed;
        }

        internal static BWTTutorialProgressSnapshot For(BetterWorkTabSettings settings)
        {
            return new BWTTutorialProgressSnapshot(
                settings?.completedTutorialLessonIds,
                settings?.tutorialLessonIdsAlreadyUsed);
        }

        internal bool IsSettled(string lessonId)
        {
            return TutorialProgressTransitions.IsCompleted(completed, lessonId) ||
                   IsAlreadyUsed(lessonId);
        }

        internal bool IsAlreadyUsed(string lessonId)
        {
            return alreadyUsed != null &&
                   !string.IsNullOrEmpty(lessonId) &&
                   alreadyUsed.Contains(lessonId);
        }
    }
}
