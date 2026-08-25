using System;
using System.Threading;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.Migration;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.Data;
using Spine.Profiling;
using Verse;

namespace Better_Work_Tab.Foundation.GameState
{
    /// <summary>
    /// Neutral monotonic token for global Work-tab presentation settings.
    /// Application and settings consumers observe this source without knowing
    /// which optional preview implementation supplies the presentation port.
    /// </summary>
    internal static class WorkTabPresentationRevision
    {
        private static long _current;

        internal static long Current => Interlocked.Read(ref _current);

        internal static long Advance()
        {
            while (true)
            {
                long expected = Interlocked.Read(ref _current);
                if (expected == long.MaxValue)
                {
                    return expected;
                }

                long next = expected + 1L;
                if (Interlocked.CompareExchange(ref _current, next, expected) == expected)
                {
                    return next;
                }
            }
        }
    }

    /// <summary>
    /// Per-game composition root. The save-facing GameComponent supplies the
    /// persisted state adapter, but feature runtime ownership lives here.
    /// </summary>
    internal sealed class WorkTabGameRoot
    {
        internal WorkTabGameRoot(
            Game game,
            WorkTabGameState state,
            IWorkTabGameLifecyclePort lifecyclePort,
            IWorkTabGameMaintenancePort maintenancePort)
        {
            Game = game;
            State = state;
            ScheduleRuntime = new TimePriorityScheduleRuntime(state.Schedules);
            Application = new WorkTabApplication(game, ScheduleRuntime);
            WorldSchema = new WorkTabWorldSchemaRuntime(state.WorldSchema);
            Lifecycle = new WorkTabGameLifecycle(
                state,
                Application,
                WorldSchema,
                lifecyclePort ?? WorkTabNoOpLifecyclePort.Instance);
            Maintenance = new WorkTabGameMaintenance(
                Application,
                maintenancePort ?? WorkTabNoOpMaintenancePort.Instance);
        }

        internal Game Game { get; }
        internal WorkTabGameState State { get; }
        internal TimePriorityScheduleRuntime ScheduleRuntime { get; }
        internal WorkTabApplication Application { get; }
        internal WorkTabWorldSchemaRuntime WorldSchema { get; }
        internal WorkTabGameLifecycle Lifecycle { get; }
        internal WorkTabGameMaintenance Maintenance { get; }
    }

    /// <summary>
    /// Current-game lookup that does not require feature domains to know the
    /// compatibility GameComponent type. Reference equality rejects stale
    /// roots during a game transition.
    /// </summary>
    internal static class WorkTabGameRoots
    {
        private static System.WeakReference<Game> _game;
        private static System.WeakReference<WorkTabGameRoot> _root;

        internal static WorkTabGameRoot Attach<TState>(
            Game game,
            TState state)
            where TState : IWorkTabScheduleStore<TimePriorityScheduleData>,
                IWorkTabColumnOrderState,
                IWorkTabReassignmentState,
                IWorkTabCustomLabelState,
                IWorkTabDividerState,
                IWorkTabWorldSchemaState,
                IWorkTabCompatibilityMigrationState
        {
            var gameState = new WorkTabGameState(
                state,
                state,
                state,
                state,
                state,
                state,
                state);
            var root = new WorkTabGameRoot(
                game,
                gameState,
                WorkTabNoOpLifecyclePort.Instance,
                WorkTabNoOpMaintenancePort.Instance);
            _game = new System.WeakReference<Game>(game);
            _root = new System.WeakReference<WorkTabGameRoot>(root);
            return root;
        }

        internal static WorkTabGameRoot Attach<TState>(
            Game game,
            TState state,
            IWorkTabGameLifecyclePort lifecyclePort,
            IWorkTabGameMaintenancePort maintenancePort)
            where TState : IWorkTabScheduleStore<TimePriorityScheduleData>,
                IWorkTabColumnOrderState,
                IWorkTabReassignmentState,
                IWorkTabCustomLabelState,
                IWorkTabDividerState,
                IWorkTabWorldSchemaState,
                IWorkTabCompatibilityMigrationState
        {
            var gameState = new WorkTabGameState(
                state,
                state,
                state,
                state,
                state,
                state,
                state);
            var root = new WorkTabGameRoot(game, gameState, lifecyclePort, maintenancePort);
            _game = new System.WeakReference<Game>(game);
            _root = new System.WeakReference<WorkTabGameRoot>(root);
            return root;
        }

        internal static WorkTabGameRoot For(Game game)
        {
            if (game == null || _game == null || _root == null ||
                !_game.TryGetTarget(out Game attachedGame) ||
                !ReferenceEquals(game, attachedGame) ||
                !_root.TryGetTarget(out WorkTabGameRoot root))
            {
                return null;
            }

            return root;
        }
    }

    /// <summary>
    /// Non-serialized world-schema classification and save guard. The
    /// persisted version remains in the narrow state port; diagnostics and
    /// read-only policy are runtime concerns owned by the game root.
    /// </summary>
    internal sealed class WorkTabWorldSchemaRuntime
    {
        private readonly IWorkTabWorldSchemaState _state;
        private BWTWorldSchemaState _classification = BWTWorldSchemaState.Current;
        private string _diagnostic = string.Empty;

        internal WorkTabWorldSchemaRuntime(IWorkTabWorldSchemaState state)
        {
            _state = state;
        }

        internal BWTWorldSchemaState Classification => _classification;
        internal string Diagnostic => _diagnostic;
        internal bool IsReadOnly =>
            !BWT20UpgradePolicy.CanPersistWorldSchema(_state?.Version ?? 0);

        internal void RefreshDiagnostics()
        {
            int version = _state?.Version ?? 0;
            _classification = BWT20UpgradePolicy.ClassifyWorldSchema(version);
            _diagnostic = string.Empty;

            switch (_classification)
            {
                case BWTWorldSchemaState.Missing:
                    _diagnostic =
                        "The Better Work Tab world schema marker is missing; legacy behavior remains active until an explicit upgrade.";
                    break;
                case BWTWorldSchemaState.KnownOld:
                    _diagnostic =
                        "The Better Work Tab world schema is known-old; an explicit upgrade remains pending.";
                    break;
                case BWTWorldSchemaState.Newer:
                    _diagnostic =
                        "The Better Work Tab world schema is newer than this build; world saves are blocked to prevent downgrade.";
                    break;
                case BWTWorldSchemaState.Unknown:
                    _diagnostic =
                        "The Better Work Tab world schema is unknown; world saves are blocked until it is understood.";
                    break;
            }

            if (IsReadOnly)
            {
                Log.WarningOnce(
                    "[BWT] " + _diagnostic,
                    154927303);
            }
        }

        internal void EnsureCanSave()
        {
            RefreshDiagnostics();
            if (IsReadOnly)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(_diagnostic)
                        ? "The Better Work Tab world schema is read-only for diagnostics."
                        : _diagnostic);
            }
        }
    }

    /// <summary>
    /// Per-game startup and post-load sequencing. The save component remains
    /// responsible for Scribe, while this service owns the order in which
    /// domain runtimes become ready around those persistence boundaries.
    /// </summary>
    internal sealed class WorkTabGameLifecycle
    {
        private readonly WorkTabGameState _state;
        private readonly WorkTabApplication _application;
        private readonly WorkTabWorldSchemaRuntime _worldSchema;
        private readonly IWorkTabGameLifecyclePort _port;

        internal WorkTabGameLifecycle(
            WorkTabGameState state,
            WorkTabApplication application,
            WorkTabWorldSchemaRuntime worldSchema,
            IWorkTabGameLifecyclePort port)
        {
            _state = state;
            _application = application;
            _worldSchema = worldSchema;
            _port = port;
        }

        internal void FinalizeInit()
        {
            TimePriorityScheduleEditor.ResetForGameTransition();

            if (_port.MultiplayerActive)
            {
                // Local profile I/O stays on the post-Scribe game lifecycle
                // boundary. The port is the compatibility shell; this service
                // owns when it is invoked.
                _port.LoadOrCreateLocalProfile();
            }

            WorkColumnOrderManager.InitializeOnGameLoad();

            if (_port.MultiplayerActive && _port.IsLocalProfileLoaded)
            {
                _port.LoadLocalUiStateIntoRuntime();
            }

            _port.EnsurePersistenceBoundary();
            _worldSchema.RefreshDiagnostics();

            DisplayElementPool.Clear();
            _port.EnsureCurrentWorklist();
            _state.Reassignments?.EnsureData();
            _port.MigrateLegacyReassignmentData();
            ColumnBaselineManager.EnsureBaseline(_state.ColumnOrder);
            EnsureApplicationRevisionEpoch();
            if (TimePriorityService.NotifyLoaded())
            {
                _application.ReportObservedScheduleChange();
            }

            _port.MigrateFluffyPriorityData();
            ConfigureTiming();
        }

        internal void PrepareForSave()
        {
            _worldSchema.EnsureCanSave();
            TimePriorityService.NormalizeBeforeSave();
        }

        internal void AcceptTrustedLoadedScheduleCollection()
        {
            TimePriorityService.AcceptTrustedLoadedScheduleCollection(
                _state.Schedules?.ScheduleRows);
        }

        internal void CompletePostLoad(string currentWorklistName)
        {
            _worldSchema.RefreshDiagnostics();

            if (_state.ColumnOrder.BaselineOrder == null)
            {
                _state.ColumnOrder.BaselineOrder = new System.Collections.Generic.List<string>();
            }

            _port.RefreshPersistenceDiagnostics();

            var loadedColumnOrder =
                _state.ColumnOrder.CurrentOrder ?? new System.Collections.Generic.List<string>();

            if (_state.ColumnOrder.BaselineOrder.Count == 0)
            {
                _state.ColumnOrder.BaselineOrder = ColumnBaselineManager.CaptureCurrentOrder();
            }

            if (loadedColumnOrder.Count == 0)
            {
                loadedColumnOrder = new System.Collections.Generic.List<string>(
                    _state.ColumnOrder.BaselineOrder);
            }

            _state.ColumnOrder.SetCurrentOrder(loadedColumnOrder);
            _port.ReconcilePostLoad(currentWorklistName);

            if (_state.Dividers.ActiveDividers == null)
            {
                _state.Dividers.ActiveDividers = new System.Collections.Generic.List<PawnDivider>();
            }

            _state.Reassignments?.EnsureData();
            _port.MigrateLegacyReassignmentData();
            EnsureApplicationRevisionEpoch();
            TimePriorityService.NotifyPostLoad();
        }

        internal void RefreshWorldSchemaDiagnostics()
        {
            _worldSchema.RefreshDiagnostics();
        }

        private void EnsureApplicationRevisionEpoch()
        {
            string seed = Find.World?.info?.seedString ?? string.Empty;
            int epoch = 17;
            for (int index = 0; index < seed.Length; index++)
            {
                epoch = unchecked(epoch * 31 + seed[index]);
            }

            _application.SetRevisionEpoch(epoch == 0 ? 1 : epoch);
        }

        private static void ConfigureTiming()
        {
            SpineTiming.Configure(
                message => BetterWorkTabMod.DebugLog(message, DebugFeature.Performance),
                () => WorkTabUsageState.OpenSeconds,
                "Work tab open");
            SpineTiming.Enabled = BetterWorkTabMod.Settings?.enableProfiler ?? false;
        }
    }

    /// <summary>
    /// Per-frame and GUI maintenance owned by the game root. Local-profile
    /// details are supplied through a narrow callback so this service stays
    /// independent of feature records and Scribe schema.
    /// </summary>
    internal sealed class WorkTabGameMaintenance
    {
        private readonly WorkTabApplication _application;
        private readonly IWorkTabGameMaintenancePort _port;
        private int _lastTimePriorityHour = -1;

        internal WorkTabGameMaintenance(
            WorkTabApplication application,
            IWorkTabGameMaintenancePort port)
        {
            _application = application;
            _port = port;
        }

        internal void BeforeGameComponentUpdate()
        {
            if (SpineTiming.Enabled)
            {
                SpineTiming.OnFrameStart();
            }

            if (Find.MainTabsRoot?.OpenTab?.TabWindow is Better_Work_Tab.UI.MainTabWindow_BetterWork)
            {
                Patch_WorkPriority_DoCell_Unified.TrimCacheIfNeeded();
            }
        }

        internal void AfterGameComponentUpdate()
        {
            if (TimePriorityService.IsRuntimeActive)
            {
                int currentHour = TimePriorityService.GetCurrentHour(null);
                if (currentHour != _lastTimePriorityHour)
                {
                    _lastTimePriorityHour = currentHour;
                    _application.NotifyHourBoundary();
                }
            }

            _port.MaintainLocalProfile();
        }

        internal void OnGUI()
        {
            SpineTiming.HandleInput();
        }
    }

    internal sealed class WorkTabNoOpLifecyclePort : IWorkTabGameLifecyclePort
    {
        internal static readonly WorkTabNoOpLifecyclePort Instance = new WorkTabNoOpLifecyclePort();

        public bool MultiplayerActive => false;
        public bool IsLocalProfileLoaded => false;
        public void LoadOrCreateLocalProfile() { }
        public void LoadLocalUiStateIntoRuntime() { }
        public void EnsurePersistenceBoundary() { }
        public void RefreshPersistenceDiagnostics() { }
        public void EnsureCurrentWorklist() { }
        public void MigrateLegacyReassignmentData() { }
        public void MigrateFluffyPriorityData() { }
        public void ReconcilePostLoad(string currentWorklistName) { }
    }

    internal sealed class WorkTabNoOpMaintenancePort : IWorkTabGameMaintenancePort
    {
        internal static readonly WorkTabNoOpMaintenancePort Instance = new WorkTabNoOpMaintenancePort();

        public void MaintainLocalProfile() { }
    }
}
