using Better_Work_Tab.Features.Tutorial;
using Spine.UI.Tutorial;
using UnityEngine;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class TutorialSelectorOwnershipTests
    {
        public static void Run()
        {
            var pawnAnchor = new BWTTutorialAnchor(
                TutorialHubAnchor.PawnName,
                new Rect(20f, 30f, 120f, 36f));
            var headerAnchor = new BWTTutorialAnchor(
                TutorialHubAnchor.WorkHeader,
                new Rect(100f, 20f, 100f, 40f),
                outlinePoints: new[]
                {
                    new Vector2(120f, 20f),
                    new Vector2(200f, 20f),
                    new Vector2(180f, 60f),
                    new Vector2(100f, 60f)
                });
            Vector2 pawnPoint = pawnAnchor.Rect.center;
            Vector2 headerPoint = new Vector2(150f, 40f);

            var unselectedRelease = PointerEvent(EventType.MouseUp, 0, pawnPoint);
            AssertAvailable(
                new BWTTutorialSelector(),
                unselectedRelease,
                "an unselected primary release must remain available to the pawn label");

            var selector = new BWTTutorialSelector();
            var primaryDown = PointerEvent(EventType.MouseDown, 0, pawnPoint);
            TestAssert.True(
                selector.TryHandleSelectionInput(new[] { pawnAnchor }, primaryDown),
                "a primary anchor press must select the anchor");
            TestAssert.Equal(
                EventType.Used,
                primaryDown.type,
                "the primary anchor press must be consumed by tutorial selection");
            TestAssert.Equal(
                TutorialHubAnchor.PawnName,
                selector.PinnedAnchor,
                "selection must be observable through the selector anchor contract");
            TestAssert.False(
                selector.ContainsPointer(Rect.zero, null, null, pawnPoint),
                "pinning an anchor must not expand general pointer ownership");

            var primaryRelease = PointerEvent(EventType.MouseUp, 0, pawnPoint);
            TestAssert.True(
                selector.TryHandlePinnedAnchorMouseUp(primaryRelease),
                "the matching primary release must belong to the selected anchor");
            TestAssert.Equal(
                EventType.Used,
                primaryRelease.type,
                "the matching primary release must be consumed");
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseUp, 0, pawnPoint),
                "a second primary release must not reuse the pending ownership");

            selector = new BWTTutorialSelector();
            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { pawnAnchor }, pawnPoint),
                "the point selection path must pin the anchor");
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseDown, 1, pawnPoint),
                "a selected right press must remain available to pawn context actions");
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseUp, 1, pawnPoint),
                "a selected right release must remain available to pawn context actions");
            AssertAvailable(
                selector,
                PointerEvent(EventType.ScrollWheel, 0, pawnPoint),
                "a selected scroll must remain available to work-tab scrolling");
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseMove, 0, pawnPoint),
                "a selected move must remain available to ordinary hover routing");
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseDrag, 0, pawnPoint),
                "a selected drag must remain available to ordinary drag routing");
            var delayedRelease = PointerEvent(EventType.MouseUp, 0, pawnPoint);
            TestAssert.True(
                selector.TryHandlePinnedAnchorMouseUp(delayedRelease),
                "non-release events must not consume the pending primary release");
            TestAssert.Equal(EventType.Used, delayedRelease.type, "the delayed release must be consumed once");

            selector = new BWTTutorialSelector();
            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { pawnAnchor }, pawnPoint),
                "the anchor must be selected before a clear");
            selector.ClearPinnedSelection();
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseUp, 0, pawnPoint),
                "clearing selection must release the pending anchor");

            selector = new BWTTutorialSelector();
            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { pawnAnchor }, pawnPoint),
                "the old anchor must be selected before replacement");
            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { headerAnchor }, headerPoint),
                "the replacement anchor must be selected");
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseUp, 0, pawnPoint),
                "a release at the replaced anchor must not belong to the current anchor");

            selector = new BWTTutorialSelector();
            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { pawnAnchor }, pawnPoint),
                "the old anchor must be selected before testing replacement release");
            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { headerAnchor }, headerPoint),
                "the replacement anchor must replace the old pending release");
            var replacementRelease = PointerEvent(EventType.MouseUp, 0, headerPoint);
            TestAssert.True(
                selector.TryHandlePinnedAnchorMouseUp(replacementRelease),
                "the pending release must follow the replacement anchor");
            TestAssert.Equal(EventType.Used, replacementRelease.type, "the replacement release must be consumed");

            selector = new BWTTutorialSelector();
            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { headerAnchor }, headerPoint),
                "the angled anchor must be selected at a point in its polygon");
            var outsideShapeRelease = PointerEvent(EventType.MouseUp, 0, new Vector2(105f, 25f));
            AssertAvailable(
                selector,
                outsideShapeRelease,
                "a release in the angled anchor bounding box but outside its shape must remain available");
            AssertAvailable(
                selector,
                PointerEvent(EventType.MouseUp, 0, headerPoint),
                "an outside-shape release must clear the pending ownership");

            TestAssert.True(
                selector.TrySelectAnchorAt(new[] { headerAnchor }, headerPoint),
                "the angled anchor must be selectable again after an outside release");
            var insideShapeRelease = PointerEvent(EventType.MouseUp, 0, headerPoint);
            TestAssert.True(
                selector.TryHandlePinnedAnchorMouseUp(insideShapeRelease),
                "a release inside the angled anchor shape must belong to it");
            TestAssert.Equal(EventType.Used, insideShapeRelease.type, "the angled release must be consumed");
        }

        private static Event PointerEvent(EventType type, int button, Vector2 point)
        {
            return new Event
            {
                type = type,
                button = button,
                mousePosition = point
            };
        }

        private static void AssertAvailable(
            BWTTutorialSelector selector,
            Event evt,
            string message)
        {
            EventType originalType = evt.type;
            TestAssert.False(selector.TryHandlePinnedAnchorMouseUp(evt), message);
            TestAssert.Equal(originalType, evt.type, message + " must preserve the event");
        }
    }
}
