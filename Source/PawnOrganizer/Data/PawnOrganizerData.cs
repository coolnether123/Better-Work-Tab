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
        public bool IsCollapsed = false;
        public float Height = DefaultSettings.dividerHeight;

        public void ExposeData()
        {
            Better_Work_Tab.ScribeCompat.LookValue(ref DividerName, "DividerName", "New Divider");
            Better_Work_Tab.ScribeCompat.LookValue(ref DividerColor, "DividerColor", Color.gray);
            Better_Work_Tab.ScribeCompat.LookValue(ref DisplayOrder, "DisplayOrder", 0);
            Better_Work_Tab.ScribeCompat.LookValue(ref ShowLabel, "ShowLabel", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref LabelFont, "LabelFont", GameFont.Small);
            Better_Work_Tab.ScribeCompat.LookValue(ref IsCollapsed, "IsCollapsed", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref Height, "Height", DefaultSettings.dividerHeight);
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
        public override int DisplayOrder => Better_Work_Tab.PawnOrganizer.RowOrderUtility.GetPawnRowOrder(Pawn);
    }

    public class DividerElement : DisplayElement
    {
        public PawnDivider Divider;
        public DividerElement(PawnDivider d) => Divider = d;
        public override bool IsDivider => true;
        public override int DisplayOrder => Divider.DisplayOrder;
    }
}
