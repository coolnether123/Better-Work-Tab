using System.Collections.Generic;
using Better_Work_Tab.Features.Tutorial;
using Spine.UI.Tutorial;
using UnityEngine;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class TutorialSelectorOwnershipTests
    {
        public static void Run()
        {
            var selector = new BWTTutorialSelector();
            var anchor = new BWTTutorialAnchor(
                TutorialHubAnchor.PawnName,
                new Rect(20f, 30f, 120f, 36f));
            var anchors = new List<BWTTutorialAnchor> { anchor };
            var hubs = new Dictionary<TutorialHubAnchor, BWTTutorialHubDefinition>
            {
                {
                    TutorialHubAnchor.PawnName,
                    new BWTTutorialHubDefinition(
                        TutorialHubAnchor.PawnName,
                        "Pawn",
                        "Choose a lesson",
                        new[]
                        {
                            new BWTTutorialOptionDefinition("lesson", "Lesson", "Heading", "Body")
                        })
                }
            };
            Rect workBounds = new Rect(0f, 0f, 400f, 400f);
            Vector2 anchorPoint = anchor.Rect.center;

            TestAssert.False(
                selector.ContainsPointer(workBounds, anchors, hubs, anchorPoint),
                "an unselected anchor must leave the pointer available to the pawn label");
            TestAssert.True(
                selector.TrySelectAnchorAt(anchors, anchorPoint),
                "the anchor must become selected at its visible hit point");
            TestAssert.Equal(
                TutorialHubAnchor.PawnName,
                selector.PinnedAnchor,
                "selection must be observable through the selector anchor contract");
            TestAssert.True(
                selector.ContainsPointer(workBounds, anchors, hubs, anchorPoint),
                "the selected anchor must own the matching MouseUp pointer");
            TestAssert.False(
                selector.ContainsPointer(workBounds, anchors, hubs, new Vector2(10f, 10f)),
                "selection must not globally disable ordinary pointer routing");

            selector.ClearPinnedSelection();
            TestAssert.False(
                selector.ContainsPointer(workBounds, anchors, hubs, anchorPoint),
                "clearing selection must release the anchor pointer ownership");
        }
    }
}
