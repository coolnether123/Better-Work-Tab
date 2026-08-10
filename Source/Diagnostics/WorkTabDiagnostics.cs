using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Diagnostics
{
    /// <summary>
    /// Optional observations emitted by the work-tab renderer and interaction
    /// paths. The default sink is a no-op; a developer assembly may install a
    /// sink without making the player-facing assembly depend on it.
    /// </summary>
    public interface IWorkTabDiagnosticsSink
    {
        void RecordPriorityInput(string source, Event evt);
        void RecordPawnLabelIcon(Pawn pawn, Rect rect);
        void RecordSubWorkSeparator(Rect rect);
        void RecordHeaderLayout(IWorkTabLayoutController layout);
        void RecordWorkTabRepaint();
        void RecordPawnTableRecache();
        void RecordPawnTableSyncWrite();
        void RecordAngledHeaderCacheKeyChange();
        void RecordAngledHeaderCacheRebuild();
        void RecordHeaderCalcSize();
        void RecordHeaderTextBuild();
    }

    public static class WorkTabDiagnostics
    {
        private static readonly IWorkTabDiagnosticsSink NullSink =
            new NullWorkTabDiagnosticsSink();
        private static IWorkTabDiagnosticsSink _sink = NullSink;

        /// <summary>
        /// Installs an observation sink for an external developer assembly.
        /// Passing null restores the no-op default.
        /// </summary>
        public static void Install(IWorkTabDiagnosticsSink sink)
        {
            _sink = sink ?? NullSink;
        }

        internal static void RecordPriorityInput(string source, Event evt)
        {
            _sink.RecordPriorityInput(source, evt);
        }

        internal static void RecordPawnLabelIcon(Pawn pawn, Rect rect)
        {
            _sink.RecordPawnLabelIcon(pawn, rect);
        }

        internal static void RecordSubWorkSeparator(Rect rect)
        {
            _sink.RecordSubWorkSeparator(rect);
        }

        internal static void RecordHeaderLayout(IWorkTabLayoutController layout)
        {
            _sink.RecordHeaderLayout(layout);
        }

        internal static void RecordWorkTabRepaint()
        {
            _sink.RecordWorkTabRepaint();
        }

        internal static void RecordPawnTableRecache()
        {
            _sink.RecordPawnTableRecache();
        }

        internal static void RecordPawnTableSyncWrite()
        {
            _sink.RecordPawnTableSyncWrite();
        }

        internal static void RecordAngledHeaderCacheKeyChange()
        {
            _sink.RecordAngledHeaderCacheKeyChange();
        }

        internal static void RecordAngledHeaderCacheRebuild()
        {
            _sink.RecordAngledHeaderCacheRebuild();
        }

        internal static void RecordHeaderCalcSize()
        {
            _sink.RecordHeaderCalcSize();
        }

        internal static void RecordHeaderTextBuild()
        {
            _sink.RecordHeaderTextBuild();
        }
    }

    internal sealed class NullWorkTabDiagnosticsSink : IWorkTabDiagnosticsSink
    {
        public void RecordPriorityInput(string source, Event evt) { }
        public void RecordPawnLabelIcon(Pawn pawn, Rect rect) { }
        public void RecordSubWorkSeparator(Rect rect) { }
        public void RecordHeaderLayout(IWorkTabLayoutController layout) { }
        public void RecordWorkTabRepaint() { }
        public void RecordPawnTableRecache() { }
        public void RecordPawnTableSyncWrite() { }
        public void RecordAngledHeaderCacheKeyChange() { }
        public void RecordAngledHeaderCacheRebuild() { }
        public void RecordHeaderCalcSize() { }
        public void RecordHeaderTextBuild() { }
    }
}
