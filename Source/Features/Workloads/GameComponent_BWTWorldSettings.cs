using Better_Work_Tab.Features;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.Data;
using Multiplayer.API;
using Spine.Profiling;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    public class GameComponent_BWTWorldSettings : GameComponent
    {
        public List<Worklist> SavedWorklists = new List<Worklist>();
        public Worklist CurrentWorklist = null;
        public List<string> ColumnCurrentOrder = new List<string>();
        public List<string> ColumnBaselineOrder = new List<string>();
        public List<PawnDivider> ActiveDividers = new List<PawnDivider>();
        public WorkGiverReassignmentData WorkGiverReassignments = new WorkGiverReassignmentData();

        public GameComponent_BWTWorldSettings(Game game) : base()
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            if (MultiplayerBridge.Active)
                BWTLocalProfileStore.LoadOrCreateForCurrentSession();

            WorkColumnOrderManager.InitializeOnGameLoad();

            if (MultiplayerBridge.Active)
                LoadLocalUiStateIntoRuntime();

            DisplayElementPool.Clear();
            EnsureCurrentWorklist();
            EnsureWorkGiverReassignmentData();
            WorkGiverReassignmentManager.MigrateLegacySettingsDataIfNeeded(this);
            ColumnBaselineManager.EnsureBaseline(this);

            SpineTiming.Enabled = BetterWorkTabMod.Settings?.enableProfiler ?? false;
        }

        public override void ExposeData()
        {
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

            if (!MultiplayerBridge.Active)
            {
                Scribe_Collections.Look(ref SavedWorklists, "SavedWorklists", LookMode.Deep, new object[0]);
                Scribe_Deep.Look(ref CurrentWorklist, "CurrentWorklist");
                Scribe_Collections.Look(ref ActiveDividers, "ActiveDividers", LookMode.Deep);
            }
            else
            {
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    BWTLocalProfileStore.LoadOrCreateForCurrentSession();
                }

                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    // Don't call SaveIfDirty() here - it would nest Scribe operations!
                    // Just mark dirty; the timer in GameComponentUpdate() will save it
                    BWTLocalProfileStore.MarkDirty();
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (ColumnBaselineOrder == null)
                {
                    ColumnBaselineOrder = new List<string>();
                }

                if (ColumnCurrentOrder == null)
                {
                    ColumnCurrentOrder = new List<string>();
                }

                if (ColumnBaselineOrder.Count == 0)
                {
                    ColumnBaselineOrder = ColumnBaselineManager.CaptureCurrentOrder();
                }

                if (ColumnCurrentOrder.Count == 0)
                {
                    ColumnCurrentOrder = new List<string>(ColumnBaselineOrder);
                }

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

        private int _profileSaveTimer = 0;

        public override void GameComponentUpdate()
        {
            SpineTiming.OnFrameStart();
            Patch_WorkPriority_DoCell_Unified.TrimCacheIfNeeded();
            base.GameComponentUpdate();

            // Save profile every 300 ticks (~5 seconds) if dirty
            // This avoids Scribe nesting issues when called from ExposeData
            _profileSaveTimer++;
            if (_profileSaveTimer > 300)
            {
                _profileSaveTimer = 0;
                if (MultiplayerBridge.Active)
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
            profile.ActiveDividers = ActiveDividers ?? new List<PawnDivider>();
            profile.SelectedWorklistName = CurrentWorklist?.RenamableLabel;
            BWTLocalProfileStore.MarkDirty();
        }
    }
}
