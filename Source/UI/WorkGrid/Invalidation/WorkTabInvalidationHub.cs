using System.Threading;
using Better_Work_Tab.UI.WorkGrid.Contracts;

namespace Better_Work_Tab.UI.WorkGrid.Invalidation
{
    /// <summary>
    /// Renderer-neutral invalidation ledger. Renderers compare immutable versions when active.
    /// </summary>
    public static class WorkTabInvalidationHub
    {
        private static readonly object LedgerLock = new object();
        private static readonly WorkGridInvalidationLedger Ledger = new WorkGridInvalidationLedger();
        private static int _presentation;
        private static int _rows;
        private static int _columns;
        private static int _headerGeometry;
        private static int _headerText;
        private static int _viewport;
        private static int _windowSize;
        private static int _renderResources;

        public static WorkTabInvalidationVersion Current
        {
            get
            {
                WorkGridRevisionSet revisions;
                int priorityDirtyCount;
                lock (LedgerLock)
                {
                    revisions = Ledger.Current;
                    priorityDirtyCount = Ledger.PriorityDirtyCount;
                }

                return new WorkTabInvalidationVersion(
                    LegacyVolatile.Read(ref _presentation),
                    LegacyVolatile.Read(ref _rows),
                    LegacyVolatile.Read(ref _columns),
                    LegacyVolatile.Read(ref _headerGeometry),
                    LegacyVolatile.Read(ref _headerText),
                    LegacyVolatile.Read(ref _viewport),
                    LegacyVolatile.Read(ref _windowSize),
                    LegacyVolatile.Read(ref _renderResources),
                    revisions,
                    priorityDirtyCount);
            }
        }

        public static void Invalidate(WorkTabDirtyFlags flags)
        {
            if ((flags & WorkTabDirtyFlags.Presentation) != 0) Interlocked.Increment(ref _presentation);
            if ((flags & WorkTabDirtyFlags.Rows) != 0) Interlocked.Increment(ref _rows);
            if ((flags & WorkTabDirtyFlags.Columns) != 0) Interlocked.Increment(ref _columns);
            if ((flags & WorkTabDirtyFlags.HeaderGeometry) != 0) Interlocked.Increment(ref _headerGeometry);
            if ((flags & WorkTabDirtyFlags.HeaderText) != 0) Interlocked.Increment(ref _headerText);
            if ((flags & WorkTabDirtyFlags.Viewport) != 0) Interlocked.Increment(ref _viewport);
            if ((flags & WorkTabDirtyFlags.WindowSize) != 0) Interlocked.Increment(ref _windowSize);
            if ((flags & WorkTabDirtyFlags.RenderResources) != 0) Interlocked.Increment(ref _renderResources);

            WorkGridInvalidationCategory categories = MapCategories(flags);
            if (categories != WorkGridInvalidationCategory.None)
            {
                lock (LedgerLock)
                {
                    Ledger.Invalidate(categories);
                }
            }
        }

        public static void InvalidatePriority(int pawnId, ushort workTypeId)
        {
            Interlocked.Increment(ref _presentation);
            lock (LedgerLock)
            {
                Ledger.InvalidatePriority(new WorkGridPriorityKey(pawnId, workTypeId));
            }
        }

        public static void InvalidateCategory(WorkGridInvalidationCategory categories)
        {
            lock (LedgerLock)
            {
                Ledger.Invalidate(categories);
            }
        }

        public static void ClearConsumedPriorityKeys()
        {
            lock (LedgerLock)
            {
                Ledger.ClearPriorityDirtyKeys();
            }
        }

        public static void ResetForGameTeardown()
        {
            Invalidate(WorkTabDirtyFlags.All);
            lock (LedgerLock)
            {
                Ledger.ClearPriorityDirtyKeys();
            }
        }

        private static WorkGridInvalidationCategory MapCategories(WorkTabDirtyFlags flags)
        {
            WorkGridInvalidationCategory result = WorkGridInvalidationCategory.None;
            if ((flags & WorkTabDirtyFlags.GameState) != 0) result |= WorkGridInvalidationCategory.GameState;
            if ((flags & (WorkTabDirtyFlags.PawnListOrder | WorkTabDirtyFlags.Rows)) != 0) result |= WorkGridInvalidationCategory.PawnListOrder;
            if ((flags & (WorkTabDirtyFlags.ColumnLayout | WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry | WorkTabDirtyFlags.WindowSize)) != 0) result |= WorkGridInvalidationCategory.ColumnLayout;
            if ((flags & WorkTabDirtyFlags.Priority) != 0) result |= WorkGridInvalidationCategory.Priority;
            if ((flags & WorkTabDirtyFlags.CapabilitySkill) != 0) result |= WorkGridInvalidationCategory.CapabilitySkill;
            if ((flags & WorkTabDirtyFlags.ScheduleHour) != 0) result |= WorkGridInvalidationCategory.ScheduleHour;
            if ((flags & WorkTabDirtyFlags.SubWorkOverride) != 0) result |= WorkGridInvalidationCategory.SubWorkOverride;
            if ((flags & (WorkTabDirtyFlags.SettingsThemeLanguageScale | WorkTabDirtyFlags.HeaderText | WorkTabDirtyFlags.RenderResources)) != 0) result |= WorkGridInvalidationCategory.SettingsThemeLanguageScale;
            if ((flags & (WorkTabDirtyFlags.HoverInteraction | WorkTabDirtyFlags.Viewport)) != 0) result |= WorkGridInvalidationCategory.HoverInteraction;
            if ((flags & WorkTabDirtyFlags.Animation) != 0) result |= WorkGridInvalidationCategory.Animation;
            if ((flags & WorkTabDirtyFlags.Presentation) != 0)
            {
                result |= WorkGridInvalidationCategory.Priority |
                          WorkGridInvalidationCategory.CapabilitySkill |
                          WorkGridInvalidationCategory.ScheduleHour |
                          WorkGridInvalidationCategory.SubWorkOverride;
            }
            return result;
        }
    }
}
