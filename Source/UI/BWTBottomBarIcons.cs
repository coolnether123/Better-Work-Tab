using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// The bottom bar's own glyphs.
    ///
    /// Textures have to be loaded on the main thread during startup, which is
    /// what <see cref="StaticConstructorOnStartupAttribute"/> arranges; without
    /// it RimWorld logs a warning naming the offending type. Resolving them
    /// lazily instead would also work but would put a ContentFinder lookup — and
    /// its logging on a miss — inside a per-frame draw.
    ///
    /// They live in their own type rather than on the class that draws them so
    /// the attribute governs only the textures, not the startup timing of every
    /// other static in that class.
    ///
    /// A missing texture yields null rather than throwing: the glyph says which
    /// picker this is, but the name beside it still works without one.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BWTBottomBarIcons
    {
        internal static readonly Texture2D Workload = ContentFinder<Texture2D>.Get("UI/BWTWorkload", false);
        internal static readonly Texture2D Ruleset = ContentFinder<Texture2D>.Get("UI/BWTRuleset", false);
    }
}
