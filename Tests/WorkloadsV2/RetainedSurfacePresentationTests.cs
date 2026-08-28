using System;
using Better_Work_Tab.UI;
using UnityEngine;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class RetainedSurfacePresentationTests
    {
        internal static void Run()
        {
            NeutralTintIsAppliedDuringPresentation();
            CallerTintIsRestoredAfterPresentation();
            CallerTintIsRestoredWhenPresentationThrows();
            NestedPresentationsRestoreInLifoOrder();
        }

        private static void NeutralTintIsAppliedDuringPresentation()
        {
            GUI.color = new Color(0.42f, 0.51f, 0.63f, 0.37f);
            using (RetainedSurfacePresentation.EnterNeutralTextureTint())
            {
                AssertColor(
                    Color.white,
                    GUI.color,
                    "retained texture pixels must be presented with an opaque white tint");
            }
        }

        private static void CallerTintIsRestoredAfterPresentation()
        {
            Color callerColor = new Color(0.18f, 0.27f, 0.36f, 0.45f);
            GUI.color = callerColor;
            using (RetainedSurfacePresentation.EnterNeutralTextureTint())
            {
                // Simulate a draw helper changing GUI.color internally. Scope
                // disposal must still restore the caller's state.
                GUI.color = new Color(0.91f, 0.82f, 0.73f, 0.64f);
            }

            AssertColor(
                callerColor,
                GUI.color,
                "retained presentation must not leak its tint into following IMGUI draws");
        }

        private static void CallerTintIsRestoredWhenPresentationThrows()
        {
            Color callerColor = new Color(0.77f, 0.66f, 0.55f, 0.44f);
            GUI.color = callerColor;
            try
            {
                using (RetainedSurfacePresentation.EnterNeutralTextureTint())
                {
                    throw new InvalidOperationException("simulated texture draw failure");
                }
            }
            catch (InvalidOperationException)
            {
                // The test is asserting the scope's finally-equivalent
                // disposal behavior, not the simulated draw failure.
            }

            AssertColor(
                callerColor,
                GUI.color,
                "retained presentation must restore tint after a draw failure");
        }

        private static void NestedPresentationsRestoreInLifoOrder()
        {
            Color outerColor = new Color(0.11f, 0.22f, 0.33f, 0.44f);
            Color innerCallerColor = new Color(0.55f, 0.66f, 0.77f, 0.88f);
            GUI.color = outerColor;
            using (RetainedSurfacePresentation.EnterNeutralTextureTint())
            {
                AssertColor(Color.white, GUI.color, "outer presentation must enter neutral tint");
                GUI.color = innerCallerColor;
                using (RetainedSurfacePresentation.EnterNeutralTextureTint())
                {
                    AssertColor(Color.white, GUI.color, "inner presentation must enter neutral tint");
                }

                AssertColor(
                    innerCallerColor,
                    GUI.color,
                    "inner presentation must restore its immediate caller");
            }

            AssertColor(
                outerColor,
                GUI.color,
                "outer presentation must restore the original caller");
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            TestAssert.True(
                expected.r == actual.r &&
                expected.g == actual.g &&
                expected.b == actual.b &&
                expected.a == actual.a,
                message);
        }
    }
}
