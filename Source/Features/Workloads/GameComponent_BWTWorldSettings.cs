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
    public class GameComponent_BWTWorldSettings : GameComponent
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
        internal BWTWorldSchemaState WorldSchemaState { get; private set; } = BWTWorldSchemaState.Current;
        internal string WorldSchemaDiagnostic { get; private set; } = string.Empty;
        internal bool IsWorldSchemaReadOnly =>
            !BWT20UpgradePolicy.CanPersistWorldSchema(BWTWorldSchemaVersion);
        private bool _worldSchemaMarkerMalformed;
        private int _lastTimePriorityHour = -1;

        public GameComponent_BWTWorldSettings(Game game) : base()
        {
        }

        internal void SetColumnCurrentOrder(List<string> order)
        {
            ColumnCurrentOrder = order ?? new List<string>();
            ColumnOrderGeneration++;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            TimePriorityScheduleEditor.ResetForGameTransition();

            if (MultiplayerBridge.Active)
            {
                // Local profiles use a standalone Scribe document. Game.FinalizeInit runs only
                // after the save game's ScribeLoader.FinalizeLoading has completed, so this is
                // the safe lifecycle boundary for profile I/O. Never move this call into
                // ExposeData: doing so nests the global Scribe loader and invalidates RimWorld's
                // PostLoadIniter enumeration during Multiplayer save/reload.
                BWTLocalProfileStore.LoadOrCreateForCurrentSession();
            }

            WorkColumnOrderManager.InitializeOnGameLoad();

            if (MultiplayerBridge.Active && BWTLocalProfileStore.IsLoadedForCurrentSession)
                LoadLocalUiStateIntoRuntime();

            EnsureWorkloadV2Persistence();
            RefreshWorldSchemaDiagnostics();

            DisplayElementPool.Clear();
            EnsureCurrentWorklist();
            EnsureWorkGiverReassignmentData();
            WorkGiverReassignmentManager.MigrateLegacySettingsDataIfNeeded(this);
            ColumnBaselineManager.EnsureBaseline(this);
            TimePriorityService.NotifyLoaded();
            FluffyWorkTabGateway.MigratePriorityDataIfNeeded(this);

            SpineTiming.Configure(
                message => BetterWorkTabMod.DebugLog(message, DebugFeature.Performance),
                () => WorkTabUsageState.OpenSeconds,
                "Work tab open");
            SpineTiming.Enabled = BetterWorkTabMod.Settings?.enableProfiler ?? false;
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
                RefreshWorldSchemaDiagnostics();
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
                TimePriorityService.NormalizeBeforeSave();
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
                RefreshWorldSchemaDiagnostics();
                if (ColumnBaselineOrder == null)
                {
                    ColumnBaselineOrder = new List<string>();
                }

                EnsureWorkloadV2Persistence().RefreshDiagnostics();

                List<string> loadedColumnOrder = ColumnCurrentOrder ?? new List<string>();

                if (ColumnBaselineOrder.Count == 0)
                {
                    ColumnBaselineOrder = ColumnBaselineManager.CaptureCurrentOrder();
                }

                if (loadedColumnOrder.Count == 0)
                {
                    loadedColumnOrder = new List<string>(ColumnBaselineOrder);
                }

                SetColumnCurrentOrder(loadedColumnOrder);

                if (!MultiplayerBridge.Active)
                {
                    if (SavedWorklists != null)
                    {
                        for (int i = 0; i < SavedWorklists.Count; i++)
                        {
                            SavedWorklists[i].EnsureCollections();
                        }
                    }

                    if (!string.IsNullOrEmpty(currentWorklistName) && SavedWorklists != null)
                    {
                        CurrentWorklist = SavedWorklists.FirstOrDefault(
                            w => w.RenamableLabel == currentWorklistName);
                    }

                    if (CurrentWorklist == null && SavedWorklists != null && SavedWorklists.Count > 0)
                    {
                        CurrentWorklist = SavedWorklists[0];
                    }

                    EnsureCurrentWorklist();

                    if ((ActiveDividers == null || ActiveDividers.Count == 0) && CurrentWorklist != null && CurrentWorklist.Dividers != null)
                    {
                        ActiveDividers = new List<PawnDivider>(CurrentWorklist.Dividers.Select(d => d?.Copy()).Where(d => d != null));
                    }
                }
                else
                {
                    LoadLocalUiStateIntoRuntime();
                    EnsureCurrentWorklist();
                }

                if (ActiveDividers == null)
                {
                    ActiveDividers = new List<PawnDivider>();
                }

                EnsureWorkGiverReassignmentData();
                WorkGiverReassignmentManager.MigrateLegacySettingsDataIfNeeded(this);
                TimePriorityService.NotifyLoaded();
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
            if (SpineTiming.Enabled)
            {
                SpineTiming.OnFrameStart();
            }

            if (Find.MainTabsRoot?.OpenTab?.TabWindow is Better_Work_Tab.UI.MainTabWindow_BetterWork)
            {
                Patch_WorkPriority_DoCell_Unified.TrimCacheIfNeeded();
            }

            base.GameComponentUpdate();

            if (TimePriorityService.IsRuntimeActive)
            {
                int currentHour = TimePriorityService.GetCurrentHour(null);
                if (currentHour != _lastTimePriorityHour)
                {
                    _lastTimePriorityHour = currentHour;
                    TimePriorityService.NotifyHourBoundaryIfNeeded();
                }
            }

            if (!MultiplayerBridge.Active && BWTLocalProfileStore.Current != null)
            {
                // Flush before the standalone profile is discarded when MP is
                // disabled or the game is torn down.
                BWTLocalProfileStore.LoadOrCreateForCurrentSession();
            }
            else if (MultiplayerBridge.Active && !BWTLocalProfileStore.IsLoadedForCurrentSession)
            {
                if (BWTLocalProfileStore.LoadOrCreateForCurrentSession())
                {
                    LoadLocalUiStateIntoRuntime();
                }
            }

            if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
            {
                BWTLocalProfileStore.FlushIfDirty();
            }
            else
            {
                // A dirty profile is retried every frame after Scribe becomes
                // inactive; it is never dependent on the old 300-tick window.
                BWTLocalProfileStore.SaveIfDirty();
            }
        }

        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();

            // Handle 1 / Shift+1 for reporting / clearing
            SpineTiming.HandleInput();
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

            WorkloadsV2.RefreshDiagnostics();
            return WorkloadsV2;
        }

        internal void NotifyWorkloadV2Changed()
        {
            if (MultiplayerBridge.Active)
            {
                PersistLocalUiState();
            }
        }

        private void RefreshWorldSchemaDiagnostics()
        {
            WorldSchemaState = BWT20UpgradePolicy.ClassifyWorldSchema(BWTWorldSchemaVersion);
            WorldSchemaDiagnostic = string.Empty;

            switch (WorldSchemaState)
            {
                case BWTWorldSchemaState.Missing:
                    WorldSchemaDiagnostic =
                        "The Better Work Tab world schema marker is missing; legacy behavior remains active until an explicit upgrade.";
                    break;
                case BWTWorldSchemaState.KnownOld:
                    WorldSchemaDiagnostic =
                        "The Better Work Tab world schema is known-old; an explicit upgrade remains pending.";
                    break;
                case BWTWorldSchemaState.Newer:
                    WorldSchemaDiagnostic =
                        "The Better Work Tab world schema is newer than this build; world saves are blocked to prevent downgrade.";
                    break;
                case BWTWorldSchemaState.Unknown:
                    WorldSchemaDiagnostic =
                        "The Better Work Tab world schema is unknown; world saves are blocked until it is understood.";
                    break;
            }

            if (IsWorldSchemaReadOnly)
            {
                Log.WarningOnce(
                    "[BWT] " + WorldSchemaDiagnostic,
                    154927303);
            }
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
