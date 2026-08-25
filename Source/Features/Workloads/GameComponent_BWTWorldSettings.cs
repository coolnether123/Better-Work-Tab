using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Migration;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.UI.Schedule;
using Better_Work_Tab.UI.Workloads;
using Multiplayer.API;
using Spine.Profiling;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    public class GameComponent_BWTWorldSettings : GameComponent, IWorkloadWorldState
    {
        public List<Worklist> SavedWorklists = new List<Worklist>();
        public Worklist CurrentWorklist = null;
        internal WorkloadV2PersistenceEnvelope WorkloadsV2 = WorkloadV2PersistenceEnvelope.CreateEmpty();
        public List<string> ColumnCurrentOrder = new List<string>();
        public List<string> ColumnBaselineOrder = new List<string>();
        public int ColumnOrderGeneration;
        public List<PawnDivider> ActiveDividers = new List<PawnDivider>();
        public WorkGiverReassignmentData WorkGiverReassignments = new WorkGiverReassignmentData();
        public List<TimePriorityScheduleData> TimePrioritySchedules = new List<TimePriorityScheduleData>();
        public Dictionary<string, string> CustomWorkTypeLabels = new Dictionary<string, string>(System.StringComparer.Ordinal);
        public Dictionary<string, string> CustomWorkGiverLabels = new Dictionary<string, string>(System.StringComparer.Ordinal);
        public int BWTWorldSchemaVersion = BWT20UpgradePolicy.CurrentWorldSchemaVersion;
        public int ExternalWorkTabPriorityMigrationVersion;
        public int FluffyWorkTabCompatibilityPromptVersion;
        internal BWTWorldSchemaState WorldSchemaState => Root.WorldSchema.Classification;
        internal string WorldSchemaDiagnostic => Root.WorldSchema.Diagnostic;
        internal bool IsWorldSchemaReadOnly => Root.WorldSchema.IsReadOnly;
        private bool _worldSchemaMarkerMalformed;
        private const int WorkloadDiagnosticsAuditFrameInterval = 3600;
        private int _nextWorkloadDiagnosticsAuditFrame;
        internal WorkTabGameRoot Root { get; }
        internal WorkTabApplication Application => Root.Application;

        public GameComponent_BWTWorldSettings(Game game) : base()
        {
            WorkloadPresentationSettingsAdapter.EnsureRegistered();
            WorkloadWorldStates.Attach(game, this);
            Root = WorkTabGameRoots.Attach(
                game,
                new PersistenceAdapter(this),
                new RuntimeLifecyclePort(this),
                new RuntimeMaintenancePort(this));
        }

        private sealed class PersistenceAdapter :
            IWorkTabScheduleStore<TimePriorityScheduleData>,
            IWorkTabColumnOrderState,
            IWorkTabReassignmentState,
            IWorkTabCustomLabelState,
            IWorkTabDividerState,
            IWorkTabWorldSchemaState,
            IWorkTabCompatibilityMigrationState
        {
            private readonly GameComponent_BWTWorldSettings _owner;

            internal PersistenceAdapter(GameComponent_BWTWorldSettings owner)
            {
                _owner = owner;
            }

            public List<TimePriorityScheduleData> ScheduleRows
            {
                get => _owner.TimePrioritySchedules;
                set => _owner.TimePrioritySchedules = value;
            }

            public List<string> CurrentOrder => _owner.ColumnCurrentOrder;
            public List<string> BaselineOrder
            {
                get => _owner.ColumnBaselineOrder;
                set => _owner.ColumnBaselineOrder = value;
            }
            public int Generation => _owner.ColumnOrderGeneration;
            public void SetCurrentOrder(List<string> order) =>
                _owner.SetColumnCurrentOrder(order);

            public WorkGiverReassignmentData Data
            {
                get => _owner.WorkGiverReassignments;
                set => _owner.WorkGiverReassignments = value;
            }
            public WorkGiverReassignmentData EnsureData() =>
                _owner.EnsureWorkGiverReassignmentData();

            public Dictionary<string, string> WorkTypeLabels =>
                _owner.CustomWorkTypeLabels;
            public Dictionary<string, string> WorkGiverLabels =>
                _owner.CustomWorkGiverLabels;

            public List<PawnDivider> ActiveDividers
            {
                get => _owner.ActiveDividers;
                set => _owner.ActiveDividers = value;
            }

            public int Version
            {
                get => _owner.BWTWorldSchemaVersion;
                set => _owner.BWTWorldSchemaVersion = value;
            }

            public int ExternalPriorityVersion
            {
                get => _owner.ExternalWorkTabPriorityMigrationVersion;
                set => _owner.ExternalWorkTabPriorityMigrationVersion = value;
            }

            public int CompatibilityPromptVersion
            {
                get => _owner.FluffyWorkTabCompatibilityPromptVersion;
                set => _owner.FluffyWorkTabCompatibilityPromptVersion = value;
            }
        }

        /// <summary>
        /// Keeps workload/profile compatibility at the save shell while the
        /// game root owns lifecycle ordering. No runtime service receives the
        /// concrete GameComponent type.
        /// </summary>
        private sealed class RuntimeLifecyclePort : IWorkTabGameLifecyclePort
        {
            private readonly GameComponent_BWTWorldSettings _owner;

            internal RuntimeLifecyclePort(GameComponent_BWTWorldSettings owner)
            {
                _owner = owner;
            }

            public bool MultiplayerActive => MultiplayerBridge.Active;

            public bool IsLocalProfileLoaded =>
                BWTLocalProfileStore.IsLoadedForCurrentSession;

            public void LoadOrCreateLocalProfile()
            {
                BWTLocalProfileStore.LoadOrCreateForCurrentSession();
            }

            public void LoadLocalUiStateIntoRuntime()
            {
                _owner.LoadLocalUiStateIntoRuntime();
            }

            public void EnsurePersistenceBoundary()
            {
                _owner.EnsureWorkloadV2Persistence();
            }

            public void RefreshPersistenceDiagnostics()
            {
                _owner.EnsureWorkloadV2Persistence().RefreshDiagnostics();
            }

            public void EnsureCurrentWorklist()
            {
                _owner.EnsureCurrentWorklist();
            }

            public void MigrateLegacyReassignmentData()
            {
                WorkGiverReassignmentMigrationAdapter
                    .MigrateLegacySettingsDataIfNeeded(
                        _owner.Root?.State?.Reassignments);
            }

            public void MigrateFluffyPriorityData()
            {
                FluffyWorkTabGateway.MigratePriorityDataIfNeeded(_owner.Root);
            }

            public void ReconcilePostLoad(string currentWorklistName)
            {
                if (!MultiplayerBridge.Active)
                {
                    if (_owner.SavedWorklists != null)
                    {
                        for (int i = 0; i < _owner.SavedWorklists.Count; i++)
                        {
                            _owner.SavedWorklists[i]?.EnsureCollections();
                        }
                    }

                    if (!string.IsNullOrEmpty(currentWorklistName) &&
                        _owner.SavedWorklists != null)
                    {
                        _owner.CurrentWorklist = _owner.SavedWorklists.FirstOrDefault(
                            w => w.RenamableLabel == currentWorklistName);
                    }

                    if (_owner.CurrentWorklist == null &&
                        _owner.SavedWorklists != null &&
                        _owner.SavedWorklists.Count > 0)
                    {
                        _owner.CurrentWorklist = _owner.SavedWorklists[0];
                    }

                    _owner.EnsureCurrentWorklist();

                    if ((_owner.ActiveDividers == null ||
                         _owner.ActiveDividers.Count == 0) &&
                        _owner.CurrentWorklist != null &&
                        _owner.CurrentWorklist.Dividers != null)
                    {
                        _owner.ActiveDividers = new List<PawnDivider>(
                            _owner.CurrentWorklist.Dividers
                                .Select(d => d?.Copy())
                                .Where(d => d != null));
                    }
                }
                else
                {
                    _owner.LoadLocalUiStateIntoRuntime();
                    _owner.EnsureCurrentWorklist();
                }
            }
        }

        private sealed class RuntimeMaintenancePort : IWorkTabGameMaintenancePort
        {
            private readonly GameComponent_BWTWorldSettings _owner;

            internal RuntimeMaintenancePort(GameComponent_BWTWorldSettings owner)
            {
                _owner = owner;
            }

            public void MaintainLocalProfile()
            {
                if (!MultiplayerBridge.Active && BWTLocalProfileStore.Current != null)
                {
                    // Flush before the standalone profile is discarded when
                    // MP is disabled or the game is torn down.
                    BWTLocalProfileStore.LoadOrCreateForCurrentSession();
                }
                else if (MultiplayerBridge.Active &&
                         !BWTLocalProfileStore.IsLoadedForCurrentSession)
                {
                    if (BWTLocalProfileStore.LoadOrCreateForCurrentSession())
                    {
                        _owner.LoadLocalUiStateIntoRuntime();
                    }
                }

                if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
                {
                    BWTLocalProfileStore.FlushIfDirty();
                }
                else
                {
                    // A dirty profile is retried every frame after Scribe
                    // becomes inactive; it is never dependent on the old
                    // 300-tick window.
                    BWTLocalProfileStore.SaveIfDirty();
                }
            }
        }

        internal void SetColumnCurrentOrder(List<string> order)
        {
            List<string> next = order ?? new List<string>();
            if (ColumnCurrentOrder != null && ColumnCurrentOrder.SequenceEqual(next)) return;
            ColumnCurrentOrder = new List<string>(next);
            ColumnOrderGeneration++;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Root.Lifecycle.FinalizeInit();
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // A constructor default is appropriate for a new world, but a
                // loaded document must prove that it carried a schema marker.
                BWTWorldSchemaVersion = 0;
                _worldSchemaMarkerMalformed = HasMalformedWorldSchemaMarker(
                    Scribe.loader?.curXmlParent);
                WorkloadsV2 = WorkloadV2PersistenceEnvelope.CreateMissing();
            }

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Root.Lifecycle.PrepareForSave();
                if (IsWorldSchemaReadOnly)
                {
                    throw new System.InvalidOperationException(
                        string.IsNullOrEmpty(WorldSchemaDiagnostic)
                            ? "The Better Work Tab world schema is read-only for diagnostics."
                            : WorldSchemaDiagnostic);
                }
            }

            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                EnsureCurrentWorklist();
            }

            string currentWorklistName = "";
            if (Scribe.mode == LoadSaveMode.Saving && CurrentWorklist != null)
            {
                currentWorklistName = CurrentWorklist.RenamableLabel;
            }

            Scribe_Values.Look(ref currentWorklistName, "currentWorklistName");
            Scribe_Collections.Look(ref ColumnBaselineOrder, "columnBaselineOrder", LookMode.Value);
            Scribe_Collections.Look(ref ColumnCurrentOrder, "columnCurrentOrder", LookMode.Value);
            Scribe_Deep.Look(ref WorkGiverReassignments, "workGiverReassignments");
            Scribe_Collections.Look(ref TimePrioritySchedules, "timePrioritySchedules", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Root.Lifecycle.AcceptTrustedLoadedScheduleCollection();
            }

            Scribe_Values.Look(ref BWTWorldSchemaVersion, "bwtWorldSchemaVersion", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && _worldSchemaMarkerMalformed)
            {
                // Scribe's numeric loader may fall back to zero for malformed XML. Preserve a
                // distinct unknown sentinel so a damaged/future document cannot be mistaken for
                // an old save and then rewritten through the normal path.
                BWTWorldSchemaVersion = int.MinValue;
            }
            FluffyWorkTabGateway.ExposePriorityMigrationVersion(ref ExternalWorkTabPriorityMigrationVersion);
            FluffyWorkTabGateway.ExposeCompatibilityPromptVersion(ref FluffyWorkTabCompatibilityPromptVersion);
            Scribe_Collections.Look(ref CustomWorkTypeLabels, "customWorkTypeLabels", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref CustomWorkGiverLabels, "customWorkGiverLabels", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                CustomWorkTypeLabels = NormalizeLabelDictionary(CustomWorkTypeLabels);
                CustomWorkGiverLabels = NormalizeLabelDictionary(CustomWorkGiverLabels);
            }

            if (!MultiplayerBridge.Active)
            {
                EnsureWorkloadV2Persistence().RefreshDiagnostics();
                if (Scribe.mode != LoadSaveMode.Saving || WorkloadsV2.ShouldPersist)
                {
                    Scribe_Deep.Look(ref WorkloadsV2, WorkloadV2PersistenceKeys.Envelope);
                }
                Scribe_Collections.Look(ref SavedWorklists, "SavedWorklists", LookMode.Deep, new object[0]);
                Scribe_Deep.Look(ref CurrentWorklist, "CurrentWorklist");
                Scribe_Collections.Look(ref ActiveDividers, "ActiveDividers", LookMode.Deep);
            }
            else
            {
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    // Local profiles are separate per-player documents. Mark the profile dirty
                    // here, then let GameComponentUpdate save it after the enclosing game save
                    // has released the global Scribe state.
                    PersistLocalUiState();
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Root.Lifecycle.CompletePostLoad(currentWorklistName);
            }
        }

        public WorkGiverReassignmentData EnsureWorkGiverReassignmentData()
        {
            if (WorkGiverReassignments == null)
            {
                WorkGiverReassignments = new WorkGiverReassignmentData();
            }

            WorkGiverReassignments.EnsureCollections();
            return WorkGiverReassignments;
        }

        private static Dictionary<string, string> NormalizeLabelDictionary(Dictionary<string, string> labels)
        {
            var normalized = new Dictionary<string, string>(System.StringComparer.Ordinal);
            if (labels == null)
            {
                return normalized;
            }

            foreach (var entry in labels)
            {
                if (!entry.Key.NullOrEmpty() && !entry.Value.NullOrEmpty())
                {
                    normalized[entry.Key] = entry.Value.Trim();
                }
            }

            return normalized;
        }

        public override void GameComponentUpdate()
        {
            Root.Maintenance.BeforeGameComponentUpdate();

            base.GameComponentUpdate();

            AuditWorkloadV2DiagnosticsIfDue();
            if (WorkloadPreviewController.Current?.IsActive == true && Find.TickManager != null)
            {
                WorkloadPawnRosterCache.AuditIfDue(Find.TickManager.TicksGame);
            }

            Root.Maintenance.AfterGameComponentUpdate();
        }

        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();
            Root.Maintenance.OnGUI();
        }

        private void EnsureCurrentWorklist()
        {
            if (CurrentWorklist == null)
            {
                int i = 1;
                while (SavedWorklists.Any(w => w.RenamableLabel == $"Workload {i}"))
                {
                    i++;
                }
                CurrentWorklist = new Worklist { RenamableLabel = $"Workload {i}" };
                BetterWorkTabMod.DebugLog($"[BetterWorkTab] Created default worklist: {CurrentWorklist.RenamableLabel}", DebugFeature.Workloads);
            }

            if (!SavedWorklists.Contains(CurrentWorklist))
            {
                SavedWorklists.Add(CurrentWorklist);
            }

            if (ActiveDividers == null)
            {
                ActiveDividers = new List<PawnDivider>();
            }
            PersistLocalUiState();
        }

        public void SelectWorklist(Worklist worklist)
        {
            if (worklist == null)
            {
                return;
            }

            if (!SavedWorklists.Contains(worklist))
            {
                SavedWorklists.Add(worklist);
            }

            CurrentWorklist = worklist;
            PersistLocalUiState();
        }

        public void ApplyWorklist(Worklist worklist)
        {
            worklist?.Apply();
        }

        public void CreateWorklist(string label)
        {
            string finalLabel = label;
            if (string.IsNullOrEmpty(finalLabel))
            {
                int i = 1;
                while (SavedWorklists.Any(w => w.RenamableLabel == $"Workload {i}"))
                {
                    i++;
                }
                finalLabel = $"Workload {i}";
            }

            var newList = new Worklist(finalLabel);
            SavedWorklists.Add(newList);
            CurrentWorklist = newList;
            PersistLocalUiState();
        }

        public void DeleteWorklist(Worklist worklist)
        {
            if (worklist == null)
                return;

            SavedWorklists.Remove(worklist);
            if (CurrentWorklist == worklist)
            {
                CurrentWorklist = SavedWorklists.FirstOrDefault();
                EnsureCurrentWorklist();
            }
            PersistLocalUiState();
        }

        public void RenameWorklist(Worklist worklist, string newLabel)
        {
            if (worklist == null || string.IsNullOrEmpty(newLabel))
                return;

            worklist.Rename(newLabel);
            PersistLocalUiState();
        }

        private void LoadLocalUiStateIntoRuntime()
        {
            var profile = BWTLocalProfileStore.Current;
            if (profile == null)
                return;

            SavedWorklists = profile.Worklists ?? new List<Worklist>();
            WorkloadsV2 = profile.WorkloadsV2 ?? WorkloadV2PersistenceEnvelope.CreateEmpty();
            foreach (var worklist in SavedWorklists)
            {
                worklist?.EnsureCollections();
            }

            ActiveDividers = profile.ActiveDividers ?? new List<PawnDivider>();

            CurrentWorklist = null;
            if (!string.IsNullOrEmpty(profile.SelectedWorklistName))
            {
                CurrentWorklist = SavedWorklists.FirstOrDefault(
                    w => w.RenamableLabel == profile.SelectedWorklistName);
            }

            if (CurrentWorklist == null && SavedWorklists.Count > 0)
            {
                CurrentWorklist = SavedWorklists[0];
            }
        }

        private void PersistLocalUiState()
        {
            if (!MultiplayerBridge.Active)
                return;

            var profile = BWTLocalProfileStore.Current;
            if (profile == null)
                return;

            profile.Worklists = SavedWorklists ?? new List<Worklist>();
            profile.WorkloadsV2 = EnsureWorkloadV2Persistence();
            profile.ActiveDividers = ActiveDividers ?? new List<PawnDivider>();
            profile.SelectedWorklistName = CurrentWorklist?.RenamableLabel;
            BWTLocalProfileStore.MarkDirty();
        }

        internal WorkloadV2PersistenceEnvelope EnsureWorkloadV2Persistence()
        {
            if (WorkloadsV2 == null)
            {
                WorkloadsV2 = WorkloadV2PersistenceEnvelope.CreateMissing();
            }

            WorkloadsV2.EnsureDiagnosticsCurrent();
            return WorkloadsV2;
        }

        internal void NotifyWorkloadV2Changed()
        {
            WorkloadsV2?.RefreshDiagnostics();
            if (MultiplayerBridge.Active)
            {
                PersistLocalUiState();
            }
        }

        IReadOnlyList<Worklist> IWorkloadWorldState.SavedWorklists => SavedWorklists;
        Worklist IWorkloadWorldState.CurrentWorklist => CurrentWorklist;
        WorkloadV2PersistenceEnvelope IWorkloadWorldState.EnsureV2Persistence() =>
            EnsureWorkloadV2Persistence();
        void IWorkloadWorldState.NotifyV2Changed() => NotifyWorkloadV2Changed();

        private void AuditWorkloadV2DiagnosticsIfDue()
        {
            int currentFrame = UnityEngine.Time.frameCount;
            if (_nextWorkloadDiagnosticsAuditFrame == 0)
            {
                _nextWorkloadDiagnosticsAuditFrame =
                    unchecked(currentFrame + WorkloadDiagnosticsAuditFrameInterval);
                return;
            }

            if (unchecked(currentFrame - _nextWorkloadDiagnosticsAuditFrame) < 0)
            {
                return;
            }

            // Direct mutation is unsupported, but an external mod can still
            // reach the public persistence records. Audit only after the BWT
            // window closes so this defensive scan never enters its render
            // or input path.
            if (Find.MainTabsRoot?.OpenTab?.TabWindow is Better_Work_Tab.UI.MainTabWindow_BetterWork)
            {
                return;
            }

            _nextWorkloadDiagnosticsAuditFrame =
                unchecked(currentFrame + WorkloadDiagnosticsAuditFrameInterval);
            WorkloadsV2?.RefreshDiagnostics();
        }

        private static bool HasMalformedWorldSchemaMarker(XmlNode parent)
        {
            if (parent == null)
            {
                return false;
            }

            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(child.Name, "bwtWorldSchemaVersion", StringComparison.Ordinal))
                {
                    if (!int.TryParse(
                        child.InnerText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out _))
                    {
                        return true;
                    }

                    continue;
                }

                if (HasMalformedWorldSchemaMarker(child))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
