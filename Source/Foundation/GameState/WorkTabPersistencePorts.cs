using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.Data;

namespace Better_Work_Tab.Foundation.GameState
{
    internal interface IWorkTabScheduleStore<TSchedule>
    {
        List<TSchedule> ScheduleRows { get; set; }
    }

    internal interface IWorkTabColumnOrderState
    {
        List<string> CurrentOrder { get; }
        List<string> BaselineOrder { get; set; }
        int Generation { get; }
        void SetCurrentOrder(List<string> order);
    }

    internal interface IWorkTabReassignmentState
    {
        WorkGiverReassignmentData Data { get; set; }
        WorkGiverReassignmentData EnsureData();
    }

    internal interface IWorkTabCustomLabelState
    {
        Dictionary<string, string> WorkTypeLabels { get; }
        Dictionary<string, string> WorkGiverLabels { get; }
    }

    internal interface IWorkTabDividerState
    {
        List<PawnDivider> ActiveDividers { get; set; }
    }

    internal interface IWorkTabWorldSchemaState
    {
        int Version { get; set; }
    }

    internal interface IWorkTabCompatibilityMigrationState
    {
        int ExternalPriorityVersion { get; set; }
        int CompatibilityPromptVersion { get; set; }
    }

    /// <summary>
    /// Compatibility-shell callbacks used by the per-game lifecycle owner.
    /// The root coordinates lifecycle order without depending on the concrete
    /// save component or any feature record type.
    /// </summary>
    internal interface IWorkTabGameLifecyclePort
    {
        bool MultiplayerActive { get; }
        bool IsLocalProfileLoaded { get; }
        void LoadOrCreateLocalProfile();
        void LoadLocalUiStateIntoRuntime();
        void EnsurePersistenceBoundary();
        void RefreshPersistenceDiagnostics();
        void EnsureCurrentWorklist();
        void MigrateLegacyReassignmentData();
        void MigrateFluffyPriorityData();
        void ReconcilePostLoad(string currentWorklistName);
    }

    /// <summary>
    /// Runtime maintenance callbacks that must stay at the GameComponent
    /// boundary because their implementations bridge optional/local profile
    /// state. The root owns when the callbacks run.
    /// </summary>
    internal interface IWorkTabGameMaintenancePort
    {
        void MaintainLocalProfile();
    }

    internal sealed class WorkTabGameState
    {
        internal WorkTabGameState(
            IWorkTabScheduleStore<TimePriorityScheduleData> schedules,
            IWorkTabColumnOrderState columnOrder,
            IWorkTabReassignmentState reassignments,
            IWorkTabCustomLabelState customLabels,
            IWorkTabDividerState dividers,
            IWorkTabWorldSchemaState worldSchema,
            IWorkTabCompatibilityMigrationState compatibilityMigrations)
        {
            Schedules = schedules;
            ColumnOrder = columnOrder;
            Reassignments = reassignments;
            CustomLabels = customLabels;
            Dividers = dividers;
            WorldSchema = worldSchema;
            CompatibilityMigrations = compatibilityMigrations;
        }

        internal IWorkTabScheduleStore<TimePriorityScheduleData> Schedules { get; }
        internal IWorkTabColumnOrderState ColumnOrder { get; }
        internal IWorkTabReassignmentState Reassignments { get; }
        internal IWorkTabCustomLabelState CustomLabels { get; }
        internal IWorkTabDividerState Dividers { get; }
        internal IWorkTabWorldSchemaState WorldSchema { get; }
        internal IWorkTabCompatibilityMigrationState CompatibilityMigrations { get; }
    }
}
