using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    [DefOf]
    public static class BWTConceptDefOf
    {
        public static ConceptDef BWT_WorkRules;
        public static ConceptDef BWT_SpecificJobs;
        public static ConceptDef BWT_RuleSuggestions;

        static BWTConceptDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BWTConceptDefOf));
        }
    }

    /// <summary>
    /// Better Work Tab's help, expressed as RimWorld concepts.
    ///
    /// The mod used to carry its own walkthrough for this: a step index, a
    /// progress counter, an instruction band, and a highlight of its own
    /// design. All of it duplicated the learning readout sitting in the corner
    /// of the same screen, and none of it looked like the game. A concept is
    /// dismissed once and never returns, it is listed with every other concept,
    /// and <see cref="TutorSystem.AdaptiveTrainingEnabled"/> is checked before
    /// any of it happens -- so a player who turned RimWorld's help off has
    /// turned this off too, without Better Work Tab adding a setting to say so.
    ///
    /// Teach per window, not per step. The Rule Builder is a form; a player who
    /// can see it can fill it in. What they cannot see is what a rule does to
    /// colonists who have not arrived yet, which is what these say.
    /// </summary>
    internal static class BWTConcepts
    {
        internal static void TeachWorkRules()
        {
            Teach(BWTConceptDefOf.BWT_WorkRules, OpportunityType.Important);
        }

        /// <summary>
        /// Only good to know: the Work tab is perfectly usable without ever
        /// drilling into a specific job, and this fires while the player is
        /// opening a tab rather than asking a question. At this level RimWorld
        /// holds it back whenever the readout is already busy.
        /// </summary>
        internal static void TeachSpecificJobs()
        {
            Teach(BWTConceptDefOf.BWT_SpecificJobs, OpportunityType.GoodToKnow);
        }

        internal static void TeachRuleSuggestions()
        {
            Teach(BWTConceptDefOf.BWT_RuleSuggestions, OpportunityType.Important);
        }

        /// <summary>
        /// Puts a concept back in the readout even if the player has already
        /// dismissed it, for the "show this again" paths.
        ///
        /// The two halves have different requirements and must not share a
        /// guard. <see cref="PlayerKnowledgeDatabase"/> is a static store bound
        /// to the player's config at startup, so forgetting a concept works
        /// with no colony loaded -- which is the common case, because mod
        /// settings are usually opened from the main menu. Only the readout
        /// needs a live tutor, and there the concept will surface by itself the
        /// next time a game is running.
        /// </summary>
        internal static void Replay(ConceptDef concept)
        {
            if (concept == null)
            {
                return;
            }

            PlayerKnowledgeDatabase.SetKnowledge(concept, 0f);
            Teach(concept, OpportunityType.Critical);
        }

        internal static void ReplayAll()
        {
            Replay(BWTConceptDefOf.BWT_WorkRules);
            Replay(BWTConceptDefOf.BWT_SpecificJobs);
            Replay(BWTConceptDefOf.BWT_RuleSuggestions);
        }

        /// <summary>
        /// Concepts live in the game's tutor, so there is nothing to teach on
        /// the main menu -- the Rule Builder can be opened from mod settings
        /// with no colony behind it.
        /// </summary>
        private static void Teach(ConceptDef concept, OpportunityType opportunity)
        {
            if (concept == null || Current.ProgramState != ProgramState.Playing || Find.Tutor == null)
            {
                return;
            }

            LessonAutoActivator.TeachOpportunity(concept, opportunity);
        }
    }
}
