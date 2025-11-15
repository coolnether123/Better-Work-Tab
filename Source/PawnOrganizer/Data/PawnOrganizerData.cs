using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.Data
{
    /// <summary>
    /// Represents a grouping of pawns with metadata (name, color, collapsed state).
    /// </summary>
    public class PawnGroup : IExposable
    {
        public string GroupName = "New Group";
        public Color GroupColor = Color.white;
        public List<Pawn> Members = new List<Pawn>();
        public bool IsCollapsed = false;

        public void ExposeData()
        {
            Scribe_Values.Look(ref GroupName, "GroupName", "New Group");
            Scribe_Values.Look(ref GroupColor, "GroupColor", Color.white);
            Scribe_Values.Look(ref IsCollapsed, "IsCollapsed", false);
            Scribe_Collections.Look(ref Members, "Members", LookMode.Reference);
        }
    }

    /// <summary>
    /// Visual divider row (not a pawn, but rendered in the pawn table).
    /// </summary>
    public class PawnGroupDivider
    {
        public PawnGroup Group;
        public PawnGroupDivider(PawnGroup group) => Group = group;
    }

    /// <summary>
    /// Either a Pawn or a PawnGroupDivider.
    /// </summary>
    public abstract class PawnTableElement
    {
        public abstract bool IsDivider { get; }
    }

    public class PawnElement : PawnTableElement
    {
        public Pawn Pawn;
        public PawnElement(Pawn p) => Pawn = p;
        public override bool IsDivider => false;
    }

    public class DividerElement : PawnTableElement
    {
        public PawnGroup Group;
        public DividerElement(PawnGroup g) => Group = g;
        public override bool IsDivider => true;
    }
}
