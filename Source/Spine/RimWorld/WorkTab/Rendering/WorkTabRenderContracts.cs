using System;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using UnityEngine;

namespace Spine.RimWorld.WorkTab.Rendering
{
    [Flags]
    public enum WorkTabDirtyFlags
    {
        None = 0,
        Presentation = 1 << 0,
        Rows = 1 << 1,
        Columns = 1 << 2,
        HeaderGeometry = 1 << 3,
        HeaderText = 1 << 4,
        Viewport = 1 << 5,
        WindowSize = 1 << 6,
        RenderResources = 1 << 7,
        All = Presentation | Rows | Columns | HeaderGeometry | HeaderText | Viewport | WindowSize | RenderResources
    }

    public readonly struct WorkTabInvalidationVersion
    {
        internal WorkTabInvalidationVersion(
            int presentation,
            int rows,
            int columns,
            int headerGeometry,
            int headerText,
            int viewport,
            int windowSize,
            int renderResources)
        {
            Presentation = presentation;
            Rows = rows;
            Columns = columns;
            HeaderGeometry = headerGeometry;
            HeaderText = headerText;
            Viewport = viewport;
            WindowSize = windowSize;
            RenderResources = renderResources;
        }

        public int Presentation { get; }
        public int Rows { get; }
        public int Columns { get; }
        public int HeaderGeometry { get; }
        public int HeaderText { get; }
        public int Viewport { get; }
        public int WindowSize { get; }
        public int RenderResources { get; }
    }

    public readonly struct WorkTabFrameContext
    {
        internal WorkTabFrameContext(
            Rect windowRect,
            PawnTable table,
            IWorkTabLayoutController layout,
            EventType eventType,
            int unityFrame,
            WorkTabInvalidationVersion versions)
        {
            WindowRect = windowRect;
            Table = table;
            Layout = layout;
            EventType = eventType;
            UnityFrame = unityFrame;
            Versions = versions;
        }

        public Rect WindowRect { get; }
        public PawnTable Table { get; }
        public IWorkTabLayoutController Layout { get; }
        public EventType EventType { get; }
        public int UnityFrame { get; }
        public WorkTabInvalidationVersion Versions { get; }
    }

    /// <summary>
    /// Paints one complete Work-table surface from canonical BWT layout geometry.
    /// Feature commands and authoritative state remain owned by BWT.
    /// </summary>
    public interface IWorkTabRenderModule
    {
        string Id { get; }
        int Priority { get; }
        bool IsAvailable { get; }
        void Render(in WorkTabFrameContext context);
    }
}
