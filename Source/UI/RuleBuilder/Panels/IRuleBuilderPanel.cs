using Better_Work_Tab.UI.RuleBuilder.State;
using UnityEngine;

namespace Better_Work_Tab.UI.RuleBuilder.Panels
{
    /// <summary>
    /// Interface for rule builder panels. Panels are stateless and operate
    /// against the shared RuleBuilderState.
    /// </summary>
    public interface IRuleBuilderPanel
    {
        RuleBuilderStep AssociatedStep { get; }
        void Draw(Rect rect, RuleBuilderState state);
        void OnActivate(RuleBuilderState state);
        void OnDeactivate(RuleBuilderState state);
        float GetMinimumHeight(RuleBuilderState state);
    }
}
