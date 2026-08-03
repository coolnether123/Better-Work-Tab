using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Better Work Tab's shared surface vocabulary, expressed in RimWorld's own
    /// palette. BWT chrome should read as part of the game rather than as a
    /// separate widget kit, so panels, rows, and accents all resolve here
    /// instead of being hand-mixed at each call site.
    ///
    /// Prefer the vanilla helpers directly where one fits:
    ///   panel/section    Widgets.DrawMenuSection(rect)
    ///   tutorial chrome  Widgets.DrawWindowBackgroundTutor(rect)
    ///   list row         Widgets.DrawOptionBackground(rect, selected)
    ///   hover            Widgets.DrawHighlightIfMouseover(rect)
    ///   selection        Widgets.DrawHighlightSelected(rect)
    ///   floating surface Widgets.DrawShadowAround(rect)
    ///   alternating rows Widgets.DrawAltRect(rect)
    /// This type exists for the colours vanilla exposes only as private fields.
    /// </summary>
    internal static class BWTUiPalette
    {
        /// <summary>
        /// RimWorld's tutor-window border colour (ColorInt 176, 139, 61). Vanilla
        /// keeps it private, so it is mirrored here for accents that must match
        /// <see cref="Widgets.DrawWindowBackgroundTutor"/>.
        /// </summary>
        internal static readonly Color TutorAccent = new ColorInt(176, 139, 61).ToColor;

        /// <summary>
        /// RimWorld's tutor-window fill colour (ColorInt 133, 85, 44), for the
        /// rare case a surface needs the fill without vanilla's border.
        /// </summary>
        internal static readonly Color TutorFill = new ColorInt(133, 85, 44).ToColor;

        /// <summary>Standard dimmed label tone used throughout vanilla UI.</summary>
        internal static readonly Color DimmedText = new Color(1f, 1f, 1f, 0.5f);
    }
}
