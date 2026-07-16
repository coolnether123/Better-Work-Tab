using System;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using RimWorld;
using Spine.RimWorld.Rendering;
using UnityEngine;

namespace Better_Work_Tab.UI.WorkGrid.Contracts
{
    public enum WorkGridRendererMode
    {
        Auto,
        Optimized,
        Vanilla,

        // Serialized 2.0 preview settings may still contain "Legacy". Keep the alias so those
        // saves load as Vanilla while all current UI and diagnostics use the player-facing name.
        [Obsolete("Use Vanilla. This alias exists only for saved-setting compatibility.")]
        Legacy = Vanilla
    }

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
        GameState = 1 << 8,
        PawnListOrder = 1 << 9,
        ColumnLayout = 1 << 10,
        Priority = 1 << 11,
        CapabilitySkill = 1 << 12,
        ScheduleHour = 1 << 13,
        SubWorkOverride = 1 << 14,
        SettingsThemeLanguageScale = 1 << 15,
        HoverInteraction = 1 << 16,
        Animation = 1 << 17,
        All = Presentation | Rows | Columns | HeaderGeometry | HeaderText | Viewport | WindowSize |
              RenderResources | GameState | PawnListOrder | ColumnLayout | Priority |
              CapabilitySkill | ScheduleHour | SubWorkOverride | SettingsThemeLanguageScale |
              HoverInteraction | Animation
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
            int renderResources,
            WorkGridRevisionSet categoryRevisions,
            int priorityDirtyCount)
        {
            Presentation = presentation;
            Rows = rows;
            Columns = columns;
            HeaderGeometry = headerGeometry;
            HeaderText = headerText;
            Viewport = viewport;
            WindowSize = windowSize;
            RenderResources = renderResources;
            CategoryRevisions = categoryRevisions;
            PriorityDirtyCount = priorityDirtyCount;
        }

        public int Presentation { get; }
        public int Rows { get; }
        public int Columns { get; }
        public int HeaderGeometry { get; }
        public int HeaderText { get; }
        public int Viewport { get; }
        public int WindowSize { get; }
        public int RenderResources { get; }
        public WorkGridRevisionSet CategoryRevisions { get; }
        public int PriorityDirtyCount { get; }
    }

    public enum WorkGridSelectionScopeKind
    {
        Window,
        Grid,
        Layer,
        Column
    }

    public readonly struct WorkGridSelectionScope : IEquatable<WorkGridSelectionScope>
    {
        public static readonly WorkGridSelectionScope Window =
            new WorkGridSelectionScope(WorkGridSelectionScopeKind.Window, "work-tab");

        public WorkGridSelectionScope(WorkGridSelectionScopeKind kind, string id)
        {
            Kind = kind;
            Id = id ?? string.Empty;
        }

        public WorkGridSelectionScopeKind Kind { get; }
        public string Id { get; }

        public bool Equals(WorkGridSelectionScope other) =>
            Kind == other.Kind && string.Equals(Id, other.Id, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is WorkGridSelectionScope other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Id ?? string.Empty);
            }
        }

        public override string ToString() => Kind + ":" + Id;
    }

    [Flags]
    public enum WorkGridFeatureFlags
    {
        None = 0,
        SkillOverlay = 1 << 0,
        Dividers = 1 << 1,
        SubWork = 1 << 2
    }

    [Flags]
    public enum WorkGridLayerFlags
    {
        None = 0,
        Headers = 1 << 0,
        PinnedRows = 1 << 1,
        PawnRows = 1 << 2,
        Dividers = 1 << 3,
        All = Headers | PinnedRows | PawnRows | Dividers
    }

    public readonly struct WorkGridRenderConfiguration
    {
        public WorkGridRenderConfiguration(WorkGridFeatureFlags features, WorkGridLayerFlags layers)
        {
            Features = features;
            Layers = layers;
        }

        public WorkGridFeatureFlags Features { get; }
        public WorkGridLayerFlags Layers { get; }
    }

    public readonly struct WorkGridPresentationAccess
    {
        public WorkGridPresentationAccess(
            PawnTable table,
            WorkGridSnapshot snapshot = null,
            WorkGridGeometrySnapshot geometry = null)
        {
            Table = table;
            Snapshot = snapshot;
            Geometry = geometry;
        }

        public PawnTable Table { get; }
        public WorkGridSnapshot Snapshot { get; }
        public WorkGridGeometrySnapshot Geometry { get; }
    }

    public readonly struct WorkGridRenderContext
    {
        public WorkGridRenderContext(
            ImGuiEventPhase eventPhase,
            EventType eventType,
            IWorkTabLayoutController layout,
            WorkGridPresentationAccess presentation,
            Rect viewport,
            Rect windowRect,
            int frameNumber,
            WorkTabInvalidationVersion invalidationVersions,
            WorkGridRenderConfiguration configuration,
            WorkGridSelectionScope scope)
        {
            EventPhase = eventPhase;
            EventType = eventType;
            Layout = layout;
            Presentation = presentation;
            Viewport = viewport;
            WindowRect = windowRect;
            FrameNumber = frameNumber;
            InvalidationVersions = invalidationVersions;
            Configuration = configuration;
            Scope = scope;
        }

        public ImGuiEventPhase EventPhase { get; }
        public EventType EventType { get; }
        public IWorkTabLayoutController Layout { get; }
        public WorkGridPresentationAccess Presentation { get; }
        public Rect Viewport { get; }
        public Rect WindowRect { get; }
        public int FrameNumber { get; }
        public WorkTabInvalidationVersion InvalidationVersions { get; }
        public WorkGridRenderConfiguration Configuration { get; }
        public WorkGridSelectionScope Scope { get; }
    }

    public interface IWorkGridRenderer
    {
        string Id { get; }
        int Priority { get; }
        bool IsAvailable(in WorkGridRenderContext context);
        void Prepare(in WorkGridRenderContext context);
        void Draw(in WorkGridRenderContext context);
        void HandleEvent(in WorkGridRenderContext context);
        void ReleaseTransient(in WorkGridRenderContext context);
    }
}
