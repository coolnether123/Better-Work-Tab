using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PriorityCycleMathTests
    {
        internal static void Run()
        {
            TestAssert.Equal(2, PriorityCycleMath.StepEnabled(3, 4, decrease: true),
                "left click decreases an enabled priority");
            TestAssert.Equal(0, PriorityCycleMath.StepEnabled(1, 4, decrease: true),
                "left click decreases priority one to disabled");
            TestAssert.Equal(4, PriorityCycleMath.StepEnabled(3, 4, decrease: false),
                "right click increases an enabled priority");
            TestAssert.Equal(0, PriorityCycleMath.StepEnabled(4, 4, decrease: false),
                "right click wraps the active maximum to disabled");
        }
    }
}
