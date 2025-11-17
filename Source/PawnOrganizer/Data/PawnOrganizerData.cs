using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.Data
{
    /// <summary>
    /// A simple visual divider that can be positioned between pawn rows.
    /// </summary>
    /// <summary>
    /// A simple visual divider that can be positioned between pawn rows.
    /// </summary>
    public class PawnDivider : IExposable
    {
        public string DividerName = "New Divider";
        public Color DividerColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        public int DisplayOrder = 0; // Position in the pawn list (like pawn.displayOrder)
        public bool ShowLabel = true;
        public GameFont LabelFont = GameFont.Small;

        public void ExposeData()
        {
            Scribe_Values.Look(ref DividerName, "DividerName", "New Divider");
            Scribe_Values.Look(ref DividerColor, "DividerColor", Color.gray);
            Scribe_Values.Look(ref DisplayOrder, "DisplayOrder", 0);
            Scribe_Values.Look(ref ShowLabel, "ShowLabel", true);
            Scribe_Values.Look(ref LabelFont, "LabelFont", GameFont.Small);
        }

        /// <summary>
        /// Creates a deep copy of this divider.
        /// This is essential for creating a new Worklist based on the current one
        /// without them sharing the same divider instances.
        /// </summary>
        public PawnDivider Copy()
        {
            return (PawnDivider)this.MemberwiseClone();
        }
    }

        /// <summary>
        /// Unified element for rendering - either a pawn or a divider.
        /// </summary>
        public abstract class DisplayElement
    {
        public abstract bool IsDivider { get; }
        public abstract int DisplayOrder { get; }
    }

    public class PawnElement : DisplayElement
    {
        public Pawn Pawn;
        public PawnElement(Pawn p) => Pawn = p;
        public override bool IsDivider => false;
        public override int DisplayOrder => Pawn.playerSettings?.displayOrder ?? 0;
    }

    public class DividerElement : DisplayElement
    {
        public PawnDivider Divider;
        public DividerElement(PawnDivider d) => Divider = d;
        public override bool IsDivider => true;
        public override int DisplayOrder => Divider.DisplayOrder;
    }
}
