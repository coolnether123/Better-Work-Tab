using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.Data;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Simple object pool for display elements to reduce per-rebuild allocations.
    /// </summary>
    public static class DisplayElementPool
    {
        private static readonly Queue<PawnElement> PawnElementPool = new Queue<PawnElement>(64);
        private static readonly Queue<DividerElement> DividerElementPool = new Queue<DividerElement>(16);

        public static PawnElement GetPawnElement(Pawn pawn)
        {
            if (PawnElementPool.Count > 0)
            {
                var element = PawnElementPool.Dequeue();
                element.Pawn = pawn;
                return element;
            }

            return new PawnElement(pawn);
        }

        public static void ReleasePawnElement(PawnElement element)
        {
            if (element == null)
            {
                return;
            }

            element.Pawn = null;

            if (PawnElementPool.Count < 128)
            {
                PawnElementPool.Enqueue(element);
            }
        }

        public static DividerElement GetDividerElement(PawnDivider divider)
        {
            if (DividerElementPool.Count > 0)
            {
                var element = DividerElementPool.Dequeue();
                element.Divider = divider;
                return element;
            }

            return new DividerElement(divider);
        }

        public static void ReleaseDividerElement(DividerElement element)
        {
            if (element == null)
            {
                return;
            }

            element.Divider = null;

            if (DividerElementPool.Count < 32)
            {
                DividerElementPool.Enqueue(element);
            }
        }

        public static void Clear()
        {
            PawnElementPool.Clear();
            DividerElementPool.Clear();
        }
    }
}
