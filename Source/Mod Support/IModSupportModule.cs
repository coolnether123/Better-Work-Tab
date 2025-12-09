using Better_Work_Tab.Features.Rules;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Contract for all optional mod integration modules.
    /// Each module is discovered by PackageId and gets a chance to:
    /// - perform one-time initialization (Harmony patches / reflection caching)
    /// - react to core Better Work Tab events (layout refresh, row draw, rule evaluation)
    /// Keep implementations lightweight; any failure should be contained so other modules keep working.
    /// </summary>
    public interface IModSupportModule
    {
        /// <summary>Unique About.xml packageId of the target mod (used for detection).</summary>
        string PackageId { get; }

        /// <summary>
        /// Optional display name for debug logging / UI. Defaults to PackageId if not overridden.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Called once when the target mod is detected and loaded. Do reflection caching and Harmony here.
        /// Throwing is discouraged; handle errors internally and return gracefully.
        /// </summary>
        void OnModsDetected();

        /// <summary>
        /// Called when Better Work Tab's pawn table is about to refresh its layout.
        /// Use this to clear caches or adjust column/row concerns.
        /// </summary>
        void OnPawnTableRefresh(PawnTable table);

        /// <summary>
        /// Called when the pawn label cell is drawn. The rect provided is the pawn icon square,
        /// so overlays can be placed relative to the icon (e.g., badge bottom-right).
        /// </summary>
        void OnPawnRowDrawn(Pawn pawn, Rect iconRect);

        /// <summary>
        /// Called each time the rules engine evaluates a pawn's parameters.
        /// Use this to inject data from the external mod into rule evaluation.
        /// </summary>
        void OnRulesEvaluated(Pawn pawn, WorkAssignmentParameters currentParameters);
    }
}
