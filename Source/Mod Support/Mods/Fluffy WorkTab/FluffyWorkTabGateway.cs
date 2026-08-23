using System;
using Better_Work_Tab.API;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGiverReassignments;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Layout;
using HarmonyLib;
using RimWorld;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;
using Verse.Sound;
using static Better_Work_Tab.UI.Settings.SettingIDs;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// Single crossing point for Better Work Tab code that needs to know about
    /// Fluffy Work Tab. Keep package IDs, external type names, texture paths, and
    /// save-shape details behind this facade.
    /// </summary>
    internal static class FluffyWorkTabGateway
    {
        private const float ChooserButtonHeight = 32f;
        private const float ChooserButtonHorizontalInset = 16f;
        private const float ChooserButtonBottomInset = 14f;
        private static bool _chooserActive;
        private static WorkTypeDef _chooserWorkType;
        private static Rect _chooserSourceRect;
        private static int _chooserSourceWorkColumnSlot = -1;
        private static Rect _focusChoiceRegionRect;
        private static Rect _expandChoiceRegionRect;
        private static Rect _focusChoiceButtonRect;
        private static Rect _expandChoiceButtonRect;
        private static Rect _rememberChoiceRect;
        private static bool _chooserRememberChoice = true;
        private static BetterWorkTabSettings.SubWorkDrilldownStyle _chooserPreviewStyle =
            BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
        private static WorkTypeDef _chooserPreviewWorkType;
        private static Type _fluffyWorkTypeWorkerType;
        private static Type _fluffyWorkGiverWorkerType;
        private const int FluffyDefaultMaxPriority = 9;
        private const int FluffyDefaultDefaultPriority = 3;
        private static bool _fluffySettingsFieldsResolved;
        private static FieldInfo _fluffyMaxPriorityField;
        private static FieldInfo _fluffyDefaultPriorityField;
        private static Type _fluffyWorkGiverColumnDefType;
        private static Type _fluffyPriorityManagerType;
        private static Type _fluffyPriorityTrackerType;
        private static FieldInfo _fluffyWorkGiverField;
        private static FieldInfo _fluffyWorkTypeExpandedField;
        private static FieldInfo _fluffyMainTabTableField;
        private static PropertyInfo _fluffyPriorityManagerGetProperty;
        private static PropertyInfo _fluffyPriorityManagerIndexer;
        private static MethodInfo _fluffyGetWorkTypePriorityMethod;
        private static MethodInfo _fluffyGetWorkGiverPriorityMethod;
        private static MethodInfo _fluffySetWorkTypePriorityAtHourMethod;
        private static MethodInfo _fluffySetWorkGiverPriorityAtHourMethod;
        private static object _fluffyHostedWindowInstance;
        private static bool _fluffyColumnTypesResolved;
        private static bool _fluffyPriorityTypesResolved;
        private static bool _fluffyHostedColumnsDisabled;
        private static bool _externalFluffyColumnsUnavailable;
        private static readonly FluffyWorkTabPriorityProvider ExternalPriorityProvider =
            new FluffyWorkTabPriorityProvider();
        private static readonly FluffyWorkTabExternalStore ExternalStore =
            new FluffyWorkTabExternalStore();
        private static bool _priorityProviderRegistered;
        private static bool _externalStoreRegistered;
        private static bool _priorityImporterRegistered;
        private static readonly Dictionary<string, PawnColumnDef> HostedWorkTypeColumns =
            new Dictionary<string, PawnColumnDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PawnColumnDef> HostedWorkGiverColumns =
            new Dictionary<string, PawnColumnDef>(StringComparer.Ordinal);
        private static readonly Dictionary<PawnColumnDef, WorkGiverDef> NativeHostedWorkGivers =
            new Dictionary<PawnColumnDef, WorkGiverDef>();
        private static readonly string[] FluffyBaseSearchKeywords =
        {
            "Fluffy",
            "Fluffy Work Tab",
            "Fluffy WorkTab",
            "Sleek Work Priorities",
            "WorkTab",
            "external work tab",
            "mod compatibility"
        };
        private static readonly string[] FluffyControlsSearchKeywords = FluffyKeywords(
            "manual priorities", "top controls", "top buttons", "icons");
        private static readonly string[] FluffyScheduleSearchKeywords = FluffyKeywords(
            "hourly priorities", "hour selector", "schedule", "time of day");
        private static readonly string[] FluffyOwnershipSearchKeywords = FluffyKeywords(
            "Work tab owner", "which mod opens Work", "switch Work tab", "visible columns");
        private static readonly string[] FluffySpecificJobsSearchKeywords = FluffyKeywords(
            "specific jobs", "individual jobs", "sub-work", "expand beside", "focused view");

        private static string[] FluffyKeywords(params string[] behaviorKeywords)
        {
            int behaviorCount = behaviorKeywords?.Length ?? 0;
            var combined = new string[FluffyBaseSearchKeywords.Length + behaviorCount];
            Array.Copy(FluffyBaseSearchKeywords, combined, FluffyBaseSearchKeywords.Length);
            if (behaviorCount > 0)
            {
                Array.Copy(
                    behaviorKeywords,
                    0,
                    combined,
                    FluffyBaseSearchKeywords.Length,
                    behaviorCount);
            }

            return combined;
        }

        internal static bool IsPresent => FluffyWorkTabCoexistence.IsFluffyWorkTabPresent;

        internal static string DetectedPackageId => FluffyWorkTabCoexistence.DetectedPackageId;

        internal static bool BetterWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.BetterWorkTabOwnsWorkTab;

        internal static bool FluffyOwnsWorkTab => FluffyWorkTabCoexistence.FluffyOwnsWorkTab;

        internal static bool SleekWorkPrioritiesPresent => SleekWorkTabGateway.IsPresent;

        internal static bool SleekWorkPrioritiesOwnsWorkTab => SleekWorkTabGateway.SleekOwnsWorkTab;

        internal static bool BetterWorkTabHostsSleek => SleekWorkTabGateway.BetterWorkTabHostsSleek;

        internal static bool AnyExternalWorkTabPresent =>
            IsPresent || SleekWorkPrioritiesPresent;

        internal static bool PriorityDataAuthorityRequestsFluffy =>
            BetterWorkTabMod.Settings?.priorityDataAuthority ==
            PriorityDataAuthorityPreference.FluffyWorkTab;

        internal static bool ExternalWorkTabOwnsWorkTab =>
            FluffyWorkTabCoexistence.ExternalWorkTabOwnsWorkTab;

        internal static string ActiveExternalWorkTabName
        {
            get
            {
                if (FluffyOwnsWorkTab)
                {
                    return "Fluffy Work Tab";
                }

                if (SleekWorkPrioritiesOwnsWorkTab)
                {
                    return "Sleek Work Priorities";
                }

                if (BetterWorkTabHostsSleek)
                {
                    return "Better Work Tab + Sleek Work Priorities";
                }

                return null;
            }
        }

        internal static bool ShouldRunBetterWorkTabFeatures => FluffyWorkTabCoexistence.ShouldRunBetterWorkTabFeatures;

        internal static bool FluffyStyleFeaturesEnabled =>
            BetterWorkTabMod.Settings?.enableFluffyStyleFeatures ?? DefaultSettings.enableFluffyStyleFeatures;

        internal static bool HasRightExpandingDrilldown => FluffyStyleFeaturesEnabled;

        internal static bool CanHostFluffySubWorkColumns =>
            FluffyStyleFeaturesEnabled && !_fluffyHostedColumnsDisabled;

        internal static bool HasScheduleStrip => IsPresent;

        internal static bool HasIconSet => IsPresent;

        internal static bool ShouldPreservePawnNameColumnWidth =>
            FluffyStyleFeaturesEnabled && BetterWorkTabOwnsWorkTab;

        internal static bool IsSubWorkStyleChooserActive => _chooserActive && _chooserWorkType != null;

        internal static WorkTypeDef SubWorkStyleChooserWorkType => _chooserWorkType;

        private static readonly IModSettingsContributor SettingsContributor = new FluffyWorkTabSettingsContributor();

        internal static string ColumnVisibilitySettingLabel => "Show Fluffy Work Tab columns";

        internal static string ColumnVisibilitySettingTooltip =>
            "When Fluffy Work Tab is loaded and Better Work Tab owns the Work tab, keep Fluffy's Mood, Job, Detailed Copy/Paste, and Favourite columns visible.";

        internal static bool IsKnownPackageId(string packageId)
        {
            return FluffyWorkTabCoexistence.IsKnownFluffyPackageId(packageId) ||
                SleekWorkTabIdentity.IsKnownPackageId(packageId);
        }

        internal static void RegisterSettings()
        {
            BWTModSettingsApi.RegisterContributor(SettingsContributor);
        }

        private static string GetPriorityDataAuthorityLabel(PriorityDataAuthorityPreference preference)
        {
            switch (preference)
            {
                case PriorityDataAuthorityPreference.BetterWorkTab:
                    return "Better Work Tab";
                case PriorityDataAuthorityPreference.FluffyWorkTab:
                    return IsPresent ? "Fluffy Work Tab" : "Fluffy Work Tab (unavailable)";
                default:
                    return "Automatic";
            }
        }

        private static string GetPriorityDataAuthorityDescription(PriorityDataAuthorityPreference preference)
        {
            switch (preference)
            {
                case PriorityDataAuthorityPreference.BetterWorkTab:
                    return "Choose this when BWT's priority scheduler and specific-job behavior should be canonical. Better Work Tab owns work-type, specific-job, and 24-hour priority data, then keeps Fluffy Work Tab synchronized when it is available.";
                case PriorityDataAuthorityPreference.FluffyWorkTab:
                    return IsPresent
                        ? "Choose this when Fluffy Work Tab's tracker should own work-type, specific-job, and 24-hour priority data, even while Better Work Tab renders the UI. Authority handoffs import the active values so BWT stays synchronized; BWT-only layouts, rules, colors, and reassignment definitions remain BWT-owned."
                        : "Fluffy Work Tab is unavailable. This preference is preserved and Better Work Tab safely owns the data until Fluffy returns; no priority data is discarded."
                          + " Choose it now if you want the preference ready for a later Fluffy installation.";
                default:
                    return "Preserve Better Work Tab's existing compatibility policy: the active compatible data owner follows the current Work-tab integration. Drawing ownership stays independent, and BWT-only layouts, rules, colors, and reassignment definitions remain BWT-owned.";
            }
        }

        private static void OnPriorityDataAuthorityChanged(BetterWorkTabSettings settings)
        {
            settings?.NormalizePrioritySettings();
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.Priority |
                WorkTabDirtyFlags.ScheduleHour |
                WorkTabDirtyFlags.Rows |
                WorkTabDirtyFlags.Presentation);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        /// <summary>
        /// Registers Fluffy Work Tab with Better Work Tab's max-priority provider registry so it is
        /// selected like any other priority mod. Fluffy prefixes Pawn_WorkSettings.GetPriority and
        /// SetPriority, so whenever it is loaded it defines the usable priority range.
        /// </summary>
        internal static void RegisterPriorityProvider()
        {
            // Do not register an unavailable adapter. The authority/provider registries use the
            // registration count as their hot-path gate, so a missing optional mod must remain an
            // empty snapshot rather than a repeatedly probed unavailable store.
            if (!IsPresent)
            {
                return;
            }

            if (_priorityProviderRegistered &&
                !PriorityProviderRegistry.IsCurrentProviderRegistration(ExternalPriorityProvider))
            {
                _priorityProviderRegistered = false;
            }

            if (_externalStoreRegistered &&
                !ExternalWorkTabRegistry.IsCurrentStoreRegistration(ExternalStore))
            {
                _externalStoreRegistered = false;
                _priorityImporterRegistered = false;
            }

            if (!_externalStoreRegistered)
            {
                _priorityImporterRegistered = false;
            }

            if (!_priorityProviderRegistered &&
                PriorityProviderRegistry.RegisterProvider(ExternalPriorityProvider))
            {
                _priorityProviderRegistered = true;
            }

            if (!_externalStoreRegistered && ExternalWorkTabApi.RegisterStore(ExternalStore))
            {
                _externalStoreRegistered = true;
            }

            if (_externalStoreRegistered &&
                !_priorityImporterRegistered &&
                ExternalWorkTabApi.RegisterPriorityImporter(ExternalStore))
            {
                _priorityImporterRegistered = true;
            }
        }

        /// <summary>
        /// Rechecks an optional assembly at the one post-load/reconciliation boundary, then fills
        /// registrations that could not be made during the initial provider discovery pass.
        /// </summary>
        internal static void ReconcileOptionalRegistration()
        {
            FluffyWorkTabCoexistence.ReconcileDetection();
            RegisterPriorityProvider();
        }

        // --- Priority mirroring -------------------------------------------------------------
        // Better Work Tab's stores are the source of truth; Fluffy's tracker is kept in step behind
        // this façade. Callers outside this module go through ExternalPriorityMirror.

        internal static IDisposable SuspendPriorityMirroring() => FluffyWorkTabSync.Suspend();

        internal static bool PriorityMirroringSuspended => FluffyWorkTabSync.IsSuspended;

        internal static bool TimePriorityScheduleMirroringEnabled =>
            BetterWorkTabMod.Settings?.enableFluffyTimePriorityMirroring ??
            DefaultSettings.enableFluffyTimePriorityMirroring;

        internal static void MirrorWorkType(Pawn pawn, WorkTypeDef workType)
        {
            FluffyWorkTabSync.PushWorkType(pawn, workType);
        }

        internal static void MirrorWorkGiver(Pawn pawn, WorkGiverDef workGiver)
        {
            FluffyWorkTabSync.PushWorkGiver(pawn, workGiver);
        }

        internal static void MirrorWorkTypeForAllPawns(WorkTypeDef workType)
        {
            FluffyWorkTabSync.PushWorkTypeForAllPawns(workType);
        }

        internal static void MirrorWorkGiverForAllPawns(WorkGiverDef workGiver)
        {
            FluffyWorkTabSync.PushWorkGiverForAllPawns(workGiver);
        }

        internal static int MirrorAllPawns()
        {
            return FluffyWorkTabSync.PushAllPawns();
        }

        /// <summary>
        /// Builds the suppression rule for settings that Fluffy Work Tab overrides while it owns the
        /// Work tab window. The link points at the owner setting so the player can hand the tab back.
        /// </summary>
        internal static SettingSuppression CreateWorkTabOwnedByFluffySuppression(string reason)
        {
            return new SettingSuppression
            {
                When = _ => ExternalWorkTabOwnsWorkTab,
                Reason = _ => reason,
                SuppressorSettingId = CompatFluffyWorkTabOwner,
                LinkLabel = "Work tab owner"
            };
        }

        internal static void ApplyDesiredOwner(bool reopenIfOpen = false)
        {
            FluffyWorkTabCoexistence.ApplyDesiredOwner(reopenIfOpen);
        }

        internal static void SwitchToBetterWorkTab()
        {
            FluffyWorkTabCoexistence.SwitchToBetterWorkTab();
        }

        internal static void SwitchToBetterWorkTabWithSleek()
        {
            FluffyWorkTabCoexistence.SwitchToBetterWorkTabWithSleek();
        }

        internal static void SwitchToExternalWorkTab()
        {
            FluffyWorkTabCoexistence.SwitchToFluffyWorkTab();
        }

        internal static void SwitchToSleekWorkPriorities()
        {
            FluffyWorkTabCoexistence.SwitchToSleekWorkPriorities();
        }

        internal static void ApplyColumnVisibility()
        {
            FluffyWorkTabCoexistence.ApplyColumnVisibility();
        }

        internal static void MigratePriorityDataIfNeeded(GameComponent_BWTWorldSettings component)
        {
            FluffyWorkTabMigrationResult result = FluffyWorkTabMigration.MigrateIfNeeded(component);
            FluffyWorkTabMigrationPrompt.QueueIfNeeded(component, result);
        }

        internal static bool HasPriorityMigrationHistory(GameComponent_BWTWorldSettings component)
        {
            return FluffyWorkTabMigration.HasMigrationHistory(component);
        }

        internal static void ExposePriorityMigrationVersion(ref int version)
        {
            FluffyWorkTabMigration.ExposeMigrationVersion(ref version);
        }

        internal static void ExposeCompatibilityPromptVersion(ref int version)
        {
            Scribe_Values.Look(ref version, "fluffyWorkTabCompatibilityPromptVersion", 0);
        }

        internal static bool TryGetWorkTypePriority(Pawn pawn, WorkTypeDef workType, out int priority)
        {
            return TryGetWorkTypePriority(pawn, workType, TimePriorityService.GetCurrentHour(pawn), out priority);
        }

        internal static bool TryGetWorkTypePriority(Pawn pawn, WorkTypeDef workType, int hour, out int priority)
        {
            priority = 0;
            if (!TryGetPriorityTracker(pawn, out object tracker) || workType == null)
            {
                return false;
            }

            try
            {
                hour = Mathf.Clamp(hour, 0, TimePriorityService.HoursPerDay - 1);
                priority = (int)_fluffyGetWorkTypePriorityMethod.Invoke(tracker, new object[] { workType, hour });
                return true;
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("[FluffyWorkTab] Failed to read work type priority: " + ex.Message, DebugFeature.ModSupport);
                return false;
            }
        }

        internal static bool TryGetWorkGiverPriority(Pawn pawn, WorkGiverDef workGiver, int hour, out int priority)
        {
            priority = 0;
            if (!TryGetPriorityTracker(pawn, out object tracker) || workGiver == null)
            {
                return false;
            }

            try
            {
                hour = Mathf.Clamp(hour, 0, TimePriorityService.HoursPerDay - 1);
                priority = (int)_fluffyGetWorkGiverPriorityMethod.Invoke(tracker, new object[] { workGiver, hour });
                return true;
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("[FluffyWorkTab] Failed to read work giver priority: " + ex.Message, DebugFeature.ModSupport);
                return false;
            }
        }

        internal static bool TrySetWorkTypePriorities(Pawn pawn, WorkTypeDef workType, int[] priorities)
        {
            if (!TryGetPriorityTracker(pawn, out object tracker) || workType == null)
            {
                return false;
            }

            try
            {
                int[] normalized = NormalizeFluffyPriorities(priorities);
                for (int hour = 0; hour < normalized.Length; hour++)
                {
                    bool recache = hour == normalized.Length - 1;
                    _fluffySetWorkTypePriorityAtHourMethod.Invoke(
                        tracker,
                        new object[] { workType, normalized[hour], hour, recache });
                }

                return true;
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("[FluffyWorkTab] Failed to write work type priorities: " + ex.Message, DebugFeature.ModSupport);
                return false;
            }
        }

        internal static bool TrySetWorkGiverPriorities(Pawn pawn, WorkGiverDef workGiver, int[] priorities)
        {
            if (!TryGetPriorityTracker(pawn, out object tracker) || workGiver == null)
            {
                return false;
            }

            try
            {
                int[] normalized = NormalizeFluffyPriorities(priorities);
                for (int hour = 0; hour < normalized.Length; hour++)
                {
                    bool recache = hour == normalized.Length - 1;
                    _fluffySetWorkGiverPriorityAtHourMethod.Invoke(
                        tracker,
                        new object[] { workGiver, normalized[hour], hour, recache });
                }

                return true;
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog("[FluffyWorkTab] Failed to write work giver priorities: " + ex.Message, DebugFeature.ModSupport);
                return false;
            }
        }

        internal static bool TryGetManualPriorityToggleIcon(out Texture2D texture)
        {
            return FluffyWorkTabAssets.TryGetManualPriorityToggleIcon(out texture);
        }

        internal static bool TryGetIcon(FluffyWorkTabIcon icon, out Texture2D texture)
        {
            return FluffyWorkTabAssets.TryGetIcon(icon, out texture);
        }

        internal static void DrawWorkTabSwitchButton(Rect inRect)
        {
            if (IsSubWorkStyleChooserActive)
            {
                return;
            }

            FluffyWorkTabCoexistenceUI.DrawWorkTabSwitchButton(inRect);
        }

        internal static void DrawCenteredWorkTabSwitchButton(Rect buttonRect)
        {
            if (IsSubWorkStyleChooserActive)
            {
                return;
            }

            FluffyWorkTabCoexistenceUI.DrawCenteredWorkTabSwitchButton(buttonRect);
        }

        /// <summary>
        /// Draws the in-context switch between BWT's focused specific-job view and
        /// the optional right-expanding presentation.
        /// </summary>
        /// <returns>The horizontal space reserved inside the pawn-name cell.</returns>
        internal static float DrawSubWorkViewModeToggle(Rect labelCellRect)
        {
            WorkTypeDef workType = SubWorkDrilldownState.ActiveWorkType;
            bool switchToExpand = workType != null;
            if (workType == null)
            {
                foreach (WorkTypeDef expandedWorkType in SubWorkDrilldownState.ExpandBesideWorkTypes)
                {
                    workType = expandedWorkType;
                    break;
                }
            }

            if (workType == null || BetterWorkTabMod.Settings == null)
            {
                return 0f;
            }

            const float width = 126f;
            Rect buttonRect = new Rect(
                labelCellRect.xMax - width - 4f,
                labelCellRect.y + 3f,
                width,
                Mathf.Max(1f, labelCellRect.height - 6f));
            string label = switchToExpand
                ? "BWT_SubWork_UseExpandedView".Translate()
                : "BWT_SubWork_UseFocusedView".Translate();
            string tooltip = switchToExpand
                ? "BWT_SubWork_UseExpandedView_Tooltip".Translate()
                : "BWT_SubWork_UseFocusedView_Tooltip".Translate();

            if (Widgets.ButtonText(buttonRect, label))
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                if (switchToExpand)
                {
                    settings.enableFluffyStyleFeatures = true;
                    settings.subWorkDrilldownStyle =
                        BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside;
                    SubWorkDrilldownState.ExitImmediate();
                    SubWorkDrilldownState.ToggleExpandBeside(workType);
                }
                else
                {
                    settings.enableFluffyStyleFeatures = false;
                    settings.subWorkDrilldownStyle =
                        BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView;
                    SubWorkDrilldownState.CollapseAllExpandBesideImmediate();
                    SubWorkDrilldownState.Enter(workType);
                }

                settings.Write();
                BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
                PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
#if !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
#endif
            }

            TooltipHandler.TipRegion(buttonRect, tooltip);
            return width + 8f;
        }

        internal static void DrawSettingsBannerIfNeeded(ref Rect inRect)
        {
            FluffyWorkTabCoexistenceUI.DrawSettingsBannerIfNeeded(ref inRect);
            SleekWorkTabCoexistenceUI.DrawSettingsBannerIfNeeded(ref inRect);
        }

        internal static bool TryStartSubWorkDrilldownStyleChooser(
            IWorkTabLayoutController layout,
            WorkTypeDef workType,
            Rect sourceRect)
        {
            if (!CanHostFluffySubWorkColumns ||
                workType == null ||
                BetterWorkTabMod.Settings == null ||
                BetterWorkTabMod.Settings.subWorkDrilldownStyle != BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen)
            {
                return false;
            }

            _chooserActive = true;
            _chooserWorkType = workType;
            _chooserSourceRect = sourceRect;
            _chooserSourceWorkColumnSlot = -1;
            if (layout?.Columns != null)
            {
                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    WorkTabLayoutColumn column = layout.Columns[i];
                    if (!column.IsExpandBesideChild && column.Column?.workType == workType)
                    {
                        _chooserSourceWorkColumnSlot = SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column);
                        break;
                    }
                }
            }
            _chooserRememberChoice = true;
            CenterChooserSourceColumn(layout, instant: false);
            return true;
        }

        internal static bool DebugChooseSubWorkDrilldownStyle(
            BetterWorkTabSettings.SubWorkDrilldownStyle style,
            out WorkTypeDef workType)
        {
            workType = null;
            if (!_chooserActive ||
                style == BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen ||
                BetterWorkTabMod.Settings == null)
            {
                return false;
            }

            workType = _chooserWorkType;
            BetterWorkTabMod.Settings.subWorkDrilldownStyle = style;
            BetterWorkTabMod.Settings.subWorkCtrlClickNoticeDismissed = true;
            BetterWorkTabMod.Settings.Write();
            BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
            PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            ClearSubWorkDrilldownStyleChooser(clearPreview: false);
            return workType != null;
        }

        internal static void DebugCancelSubWorkDrilldownStyleChooser()
        {
            ClearSubWorkDrilldownStyleChooser();
        }

        internal static void ResetSubWorkDrilldownStyleChooserForWindowClose()
        {
            ClearSubWorkDrilldownStyleChooser(clearPreview: true, immediatePreviewExit: true);
        }

        internal static void RegisterSubWorkStyleChooserRegions(
            Rect focusRegion,
            Rect expandRegion,
            Rect rememberRegion)
        {
            _focusChoiceRegionRect = focusRegion;
            _expandChoiceRegionRect = expandRegion;
            _rememberChoiceRect = rememberRegion;

            _focusChoiceButtonRect = BuildChoiceButtonRect(
                focusRegion,
                anchorRight: false);
            _expandChoiceButtonRect = BuildChoiceButtonRect(
                expandRegion,
                anchorRight: true);
        }

        internal static bool TryHandleSubWorkDrilldownStyleChooserInput(
            IWorkTabLayoutController layout,
            Event evt,
            out WorkTypeDef workType,
            out BetterWorkTabSettings.SubWorkDrilldownStyle style,
            out int sourceWorkColumnSlot)
        {
            workType = null;
            style = BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
            sourceWorkColumnSlot = -1;
            if (!_chooserActive || evt == null)
            {
                return false;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                ClearSubWorkDrilldownStyleChooser();
                evt.Use();
                return true;
            }

            if (evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            if (_rememberChoiceRect.Contains(evt.mousePosition))
            {
                _chooserRememberChoice = !_chooserRememberChoice;
                evt.Use();
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                return true;
            }

            if (_focusChoiceButtonRect.Contains(evt.mousePosition))
            {
                style = BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView;
            }
            else if (_expandChoiceButtonRect.Contains(evt.mousePosition))
            {
                style = BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside;
            }
            else
            {
                evt.Use();
                return true;
            }

            workType = _chooserWorkType;
            sourceWorkColumnSlot = _chooserSourceWorkColumnSlot;
            if (_chooserRememberChoice)
            {
                BetterWorkTabMod.Settings.subWorkDrilldownStyle = style;
                BetterWorkTabMod.Settings.subWorkCtrlClickNoticeDismissed = true;
                BetterWorkTabMod.Settings.Write();
                BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
                PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
            }
            ClearSubWorkDrilldownStyleChooser(clearPreview: false);
            evt.Use();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        internal static void UpdateSubWorkDrilldownStyleChooserPreview(IWorkTabLayoutController layout)
        {
            if (!_chooserActive || _chooserWorkType == null)
            {
                ClearSubWorkDrilldownStyleChooserPreview(immediateFocusExit: false);
                return;
            }

            BetterWorkTabSettings.SubWorkDrilldownStyle hoverStyle = GetSubWorkDrilldownStyleChooserHover();
            if (hoverStyle == _chooserPreviewStyle && _chooserPreviewWorkType == _chooserWorkType)
            {
                return;
            }

            bool switchingFromFocusToExpand =
                _chooserPreviewStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView &&
                hoverStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside;
            ClearSubWorkDrilldownStyleChooserPreview(immediateFocusExit: switchingFromFocusToExpand);

            if (hoverStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView)
            {
                SubWorkDrilldownState.CollapseAllExpandBeside();
                SubWorkDrilldownState.EnterFromSourceSlot(
                    _chooserWorkType,
                    null,
                    SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(layout?.Table, layout?.HeaderHeight ?? -1f),
                    null,
                    _chooserSourceWorkColumnSlot);
                _chooserPreviewStyle = hoverStyle;
                _chooserPreviewWorkType = _chooserWorkType;
                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(UI.WorkGrid.Contracts.WorkTabDirtyFlags.Columns | UI.WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
                return;
            }

            if (hoverStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                if (SubWorkDrilldownState.IsActive)
                {
                    SubWorkDrilldownState.ExitImmediate();
                }

                SubWorkDrilldownState.CollapseAllExpandBesideImmediate();
                SubWorkDrilldownState.ToggleExpandBeside(_chooserWorkType);
                _chooserPreviewStyle = hoverStyle;
                _chooserPreviewWorkType = _chooserWorkType;
                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(UI.WorkGrid.Contracts.WorkTabDirtyFlags.Columns | UI.WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
            }
        }

        internal static void DrawSubWorkDrilldownStyleChooser(IWorkTabLayoutController layout, Rect inRect)
        {
            if (!_chooserActive || _chooserWorkType == null || layout == null)
            {
                return;
            }

            CenterChooserSourceColumn(layout, instant: false);

            BetterWorkTabSettings.SubWorkDrilldownStyle hoverStyle = GetSubWorkDrilldownStyleChooserHover();
            DrawChoiceButton(
                _focusChoiceButtonRect,
                "BWT_SubWork_Chooser_FocusTitle".Translate(),
                hoverStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView);
            DrawChoiceButton(
                _expandChoiceButtonRect,
                "BWT_SubWork_Chooser_ExpandTitle".Translate(),
                hoverStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside);
            DrawRememberChoice();
            DrawSubWorkStyleChooserNote(inRect);
        }

        internal static bool TryBuildHostedColumnSpecs(
            PawnColumnDef sourceWorkColumn,
            WorkTypeDef workType,
            out PawnColumnDef hostedWorkTypeColumn,
            out List<PawnColumnDef> hostedWorkGiverColumns)
        {
            hostedWorkTypeColumn = null;
            hostedWorkGiverColumns = null;
            if (!CanHostFluffySubWorkColumns || workType == null)
            {
                return false;
            }

            hostedWorkTypeColumn = GetOrCreateHostedWorkTypeColumn(sourceWorkColumn, workType);
            if (hostedWorkTypeColumn == null)
            {
                return false;
            }

            hostedWorkGiverColumns = new List<PawnColumnDef>();
            List<WorkGiverDef> workGivers = GetBwtDisplayWorkGivers(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                PawnColumnDef child = GetOrCreateHostedWorkGiverColumn(workGivers[i]);
                if (child != null)
                {
                    hostedWorkGiverColumns.Add(child);
                }
            }

            return hostedWorkGiverColumns.Count > 0;
        }

        internal static bool TryBuildHostedPreviewColumns(
            PawnColumnDef sourceWorkColumn,
            WorkTypeDef workType,
            float startX,
            float parentWidth,
            float childWidth,
            out List<WorkTabLayoutColumn> columns)
        {
            columns = null;
            if (!TryBuildHostedColumnSpecs(
                    sourceWorkColumn,
                    workType,
                    out PawnColumnDef parentColumn,
                    out List<PawnColumnDef> childColumns))
            {
                return false;
            }

            columns = new List<WorkTabLayoutColumn>();
            float x = startX;
            columns.Add(new WorkTabLayoutColumn(
                parentColumn,
                new Rect(x, 0f, parentWidth, 1f),
                x,
                parentWidth));
            x += parentWidth;

            for (int i = 0; i < childColumns.Count; i++)
            {
                WorkGiverDef workGiverDef = TryGetHostedWorkGiver(childColumns[i]);
                if (workGiverDef == null)
                {
                    continue;
                }

                columns.Add(new WorkTabLayoutColumn(
                    childColumns[i],
                    new Rect(x, 0f, childWidth, 1f),
                    x,
                    childWidth,
                    workType,
                    workGiverDef,
                    i,
                    isExpandBesideChild: true));
                x += childWidth;
            }

            return columns.Count > 1;
        }

        internal static WorkGiverDef TryGetHostedWorkGiver(PawnColumnDef column)
        {
            return TryGetFluffyWorkGiver(column);
        }

        internal static WorkGiverDef TryGetFluffyWorkGiver(PawnColumnDef column)
        {
            if (column == null)
            {
                return null;
            }

            if (NativeHostedWorkGivers.TryGetValue(column, out WorkGiverDef nativeWorkGiver))
            {
                return nativeWorkGiver;
            }

            if (!IsPresent || !EnsureFluffyColumnTypes() || !_fluffyWorkGiverColumnDefType.IsInstanceOfType(column))
            {
                return null;
            }

            try
            {
                return _fluffyWorkGiverField?.GetValue(column) as WorkGiverDef;
            }
            catch (Exception ex)
            {
                DisableExternalFluffyColumns("reading hosted work-giver field", ex);
                return null;
            }
        }

        internal static void PrepareHostedDraw(PawnTable table)
        {
            if (table == null ||
                !IsPresent ||
                _externalFluffyColumnsUnavailable ||
                !EnsureFluffyColumnTypes() ||
                !FluffyWorkTabCoexistence.TryGetFluffyWorkTabWindowType(out Type windowType))
            {
                return;
            }

            try
            {
                if (_fluffyMainTabTableField == null)
                {
                    _fluffyMainTabTableField = AccessTools.Field(typeof(MainTabWindow_PawnTable), "table");
                    if (_fluffyMainTabTableField == null)
                    {
                        DisableExternalFluffyColumns("resolving hosted table field", null);
                        return;
                    }
                }

                if (_fluffyHostedWindowInstance == null ||
                    !windowType.IsInstanceOfType(_fluffyHostedWindowInstance))
                {
                    _fluffyHostedWindowInstance = Activator.CreateInstance(windowType);
                }

                _fluffyMainTabTableField.SetValue(_fluffyHostedWindowInstance, table);
            }
            catch (Exception ex)
            {
                DisableExternalFluffyColumns("preparing hosted Fluffy draw", ex);
            }
        }

        internal static bool WasHostedWorkTypeCollapsed(PawnColumnDef column)
        {
            if (!IsHostedFluffyWorkTypeColumn(column))
            {
                return false;
            }

            object worker = column.Worker;
            if (worker == null ||
                _fluffyWorkTypeWorkerType == null ||
                worker.GetType() != _fluffyWorkTypeWorkerType ||
                _fluffyWorkTypeExpandedField == null)
            {
                return false;
            }

            try
            {
                if (_fluffyWorkTypeExpandedField.GetValue(worker) is bool expanded && !expanded)
                {
                    _fluffyWorkTypeExpandedField.SetValue(worker, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DisableExternalFluffyColumns("checking hosted work-type collapse state", ex);
            }

            return false;
        }

        internal static bool IsHostedFluffyColumn(PawnColumnDef column)
        {
            return IsHostedFluffyWorkTypeColumn(column) || IsHostedFluffyWorkGiverColumn(column);
        }

        internal static bool IsFluffyColumn(PawnColumnDef column)
        {
            if (column == null)
            {
                return false;
            }

            if (HostedWorkTypeColumns.ContainsValue(column) || HostedWorkGiverColumns.ContainsValue(column))
            {
                return true;
            }

            if (!IsPresent || !EnsureFluffyColumnTypes())
            {
                return false;
            }

            Type workerType = column.Worker?.GetType();
            return workerType == _fluffyWorkTypeWorkerType ||
                   workerType == _fluffyWorkGiverWorkerType;
        }

        /// <summary>
        /// True only for external Fluffy work-giver columns. BWT-native hosted columns are deliberately
        /// excluded so their BWT worker and header pipeline remain independent from Fluffy's assembly.
        /// </summary>
        internal static bool IsFluffyWorkGiverColumn(PawnColumnDef column)
        {
            return column != null &&
                   IsPresent &&
                   EnsureFluffyColumnTypes() &&
                   column.Worker?.GetType() == _fluffyWorkGiverWorkerType;
        }

        internal static bool IsHostedFluffyWorkGiverColumn(PawnColumnDef column)
        {
            return column != null &&
                (NativeHostedWorkGivers.ContainsKey(column) ||
                 (IsFluffyWorkGiverColumn(column) && HostedWorkGiverColumns.ContainsValue(column)));
        }

        internal static bool IsHostedFluffyWorkTypeColumn(PawnColumnDef column)
        {
            return column != null && HostedWorkTypeColumns.ContainsValue(column);
        }

        internal static float GetHostedColumnWidth(PawnColumnDef column, PawnTable table, float fallback)
        {
            // Fluffy replaces the normal root Work columns with its own worker type.
            // Those roots must retain PawnTable's already-distributed vanilla width.
            // Only BWT-hosted specific-job columns use Fluffy's compact minimum width.
            if (!IsHostedFluffyColumn(column))
            {
                return fallback;
            }

            try
            {
                return Mathf.Max(1f, column.Worker.GetMinWidth(table));
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[FluffyWorkTab] Failed to query hosted column width for " + column.defName + ": " + ex.Message,
                    DebugFeature.SubWork);
                return fallback;
            }
        }

        internal static Rect GetHostedHeaderLaneRect(PawnColumnDef column, PawnTable table, Rect headerRect)
        {
            if (!IsHostedFluffyColumn(column) || table == null || headerRect.height <= 1f)
            {
                return headerRect;
            }

            if (IsHostedFluffyWorkGiverColumn(column))
            {
                return headerRect;
            }

            float laneHeight;
            try
            {
                laneHeight = Mathf.Clamp(column.Worker.GetMinHeaderHeight(table), 1f, headerRect.height);
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[FluffyWorkTab] Failed to query hosted header height for " + column.defName + ": " + ex.Message,
                    DebugFeature.SubWork);
                return headerRect;
            }

            return new Rect(headerRect.x, headerRect.yMax - laneHeight, headerRect.width, laneHeight);
        }

        private static void DrawChoiceButton(Rect rect, string caption, bool forceHover)
        {
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return;
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (forceHover)
            {
                Widgets.DrawHighlight(rect);
            }

            Widgets.ButtonText(rect, "BWT_SubWork_Chooser_UseThisView".Translate());
            TooltipHandler.TipRegion(rect, caption);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawSubWorkStyleChooserNote(Rect inRect)
        {
            Rect noteRect;
            if (_rememberChoiceRect.width > 1f)
            {
                noteRect = new Rect(
                    _rememberChoiceRect.xMin,
                    _rememberChoiceRect.yMax,
                    _rememberChoiceRect.width,
                    22f);
            }
            else
            {
                noteRect = new Rect(inRect.xMin + 80f, inRect.yMax - 44f, Mathf.Max(1f, inRect.width - 160f), 24f);
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(
                noteRect,
                (_chooserRememberChoice
                    ? "BWT_SubWork_Chooser_SettingsNote"
                    : "BWT_SubWork_Chooser_AskAgainNote").Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawRememberChoice()
        {
            if (_rememberChoiceRect.width <= 1f || _rememberChoiceRect.height <= 1f)
            {
                return;
            }

            if (Mouse.IsOver(_rememberChoiceRect))
            {
                Widgets.DrawHighlight(_rememberChoiceRect);
            }

            const float checkboxSize = 20f;
            Rect checkboxRect = new Rect(
                _rememberChoiceRect.xMin + 4f,
                _rememberChoiceRect.center.y - (checkboxSize * 0.5f),
                checkboxSize,
                checkboxSize);
            GUI.color = Color.white;
            GUI.DrawTexture(
                checkboxRect,
                _chooserRememberChoice ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(
                    checkboxRect.xMax + 7f,
                    _rememberChoiceRect.y,
                    Mathf.Max(1f, _rememberChoiceRect.xMax - checkboxRect.xMax - 11f),
                    _rememberChoiceRect.height),
                "BWT_SubWork_Chooser_RememberChoice".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void ClearSubWorkDrilldownStyleChooser(
            bool clearPreview = true,
            bool immediatePreviewExit = false)
        {
            if (clearPreview)
            {
                ClearSubWorkDrilldownStyleChooserPreview(immediateFocusExit: immediatePreviewExit);
            }
            else
            {
                _chooserPreviewStyle = BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
                _chooserPreviewWorkType = null;
            }

            _chooserActive = false;
            _chooserWorkType = null;
            _chooserSourceRect = Rect.zero;
            _chooserSourceWorkColumnSlot = -1;
            _focusChoiceRegionRect = Rect.zero;
            _expandChoiceRegionRect = Rect.zero;
            _focusChoiceButtonRect = Rect.zero;
            _expandChoiceButtonRect = Rect.zero;
            _rememberChoiceRect = Rect.zero;
            _chooserRememberChoice = true;
        }

        private static void ClearSubWorkDrilldownStyleChooserPreview(bool immediateFocusExit)
        {
            if (_chooserPreviewStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView &&
                SubWorkDrilldownState.ActiveWorkType == _chooserPreviewWorkType)
            {
                if (immediateFocusExit)
                {
                    SubWorkDrilldownState.ExitImmediate();
                }
                else
                {
                    SubWorkDrilldownState.Exit();
                }
            }
            else if (_chooserPreviewStyle == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                if (immediateFocusExit)
                {
                    SubWorkDrilldownState.CollapseAllExpandBesideImmediate();
                }
                else
                {
                    SubWorkDrilldownState.CollapseAllExpandBeside();
                }
            }

            _chooserPreviewStyle = BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
            _chooserPreviewWorkType = null;
        }

        private static BetterWorkTabSettings.SubWorkDrilldownStyle GetSubWorkDrilldownStyleChooserHover()
        {
            Event evt = Event.current;
            if (evt == null)
            {
                return BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
            }

            Vector2 mousePosition = evt.mousePosition;
            if (_focusChoiceRegionRect.Contains(mousePosition) || _focusChoiceButtonRect.Contains(mousePosition))
            {
                return BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView;
            }

            if (_expandChoiceRegionRect.Contains(mousePosition) || _expandChoiceButtonRect.Contains(mousePosition))
            {
                return BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside;
            }

            return BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
        }

        internal static Rect ResolveSubWorkStyleChooserSourceRect(IWorkTabLayoutController layout)
        {
            if (layout?.Columns != null && _chooserWorkType != null)
            {
                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    var column = layout.Columns[i];
                    if (!column.IsExpandBesideChild && column.Column?.workType == _chooserWorkType)
                    {
                        _chooserSourceRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                        return _chooserSourceRect;
                    }
                }
            }

            return _chooserSourceRect;
        }

        private static void CenterChooserSourceColumn(IWorkTabLayoutController layout, bool instant)
        {
            PawnTable table = layout?.Table;
            if (table == null)
            {
                return;
            }

            Rect sourceRect = ResolveSubWorkStyleChooserSourceRect(layout);
            float localCenter = table.scrollPosition.x + (sourceRect.center.x - layout.TableOrigin.x);
            float targetX = Mathf.Max(0f, localCenter - (table.Size.x * 0.5f));
            Vector2 scroll = table.scrollPosition;
            scroll.x = instant ? targetX : Mathf.Lerp(scroll.x, targetX, 0.18f);
            table.scrollPosition = scroll;
        }

        private static Rect BuildChoiceButtonRect(Rect region, bool anchorRight)
        {
            if (region.width <= 1f || region.height <= 1f)
            {
                return Rect.zero;
            }

            float width = Mathf.Max(120f, region.width - (ChooserButtonHorizontalInset * 2f));
            float x = anchorRight ? region.xMax - ChooserButtonHorizontalInset - width : region.xMin + ChooserButtonHorizontalInset;
            return new Rect(
                Mathf.Clamp(x, region.xMin, Mathf.Max(region.xMin, region.xMax - width)),
                region.yMax - ChooserButtonBottomInset - ChooserButtonHeight,
                width,
                ChooserButtonHeight);
        }

        private static bool EnsureFluffyColumnTypes()
        {
            if (_externalFluffyColumnsUnavailable)
            {
                return false;
            }

            if (_fluffyColumnTypesResolved)
            {
                return _fluffyWorkTypeWorkerType != null &&
                       _fluffyWorkGiverWorkerType != null &&
                       _fluffyWorkGiverColumnDefType != null &&
                       _fluffyWorkGiverField != null &&
                       _fluffyWorkTypeExpandedField != null;
            }

            _fluffyColumnTypesResolved = true;
            _fluffyWorkTypeWorkerType = AccessTools.TypeByName("WorkTab.PawnColumnWorker_WorkType");
            _fluffyWorkGiverWorkerType = AccessTools.TypeByName("WorkTab.PawnColumnWorker_WorkGiver");
            _fluffyWorkGiverColumnDefType = AccessTools.TypeByName("WorkTab.PawnColumnDef_WorkGiver");
            _fluffyWorkGiverField = _fluffyWorkGiverColumnDefType?.GetField(
                "workgiver",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _fluffyWorkTypeExpandedField = _fluffyWorkTypeWorkerType?.GetField(
                "_expanded",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            bool resolved = _fluffyWorkTypeWorkerType != null &&
                _fluffyWorkGiverWorkerType != null &&
                _fluffyWorkGiverColumnDefType != null &&
                _fluffyWorkGiverField != null &&
                _fluffyWorkTypeExpandedField != null;
            if (!resolved)
            {
                _externalFluffyColumnsUnavailable = true;
                BetterWorkTabMod.DebugLog(
                    "[FluffyWorkTab] External column API is unavailable; using BWT-native Fluffy-style columns.",
                    DebugFeature.ModSupport);
            }

            return resolved;
        }

        private static bool TryGetPriorityTracker(Pawn pawn, out object tracker)
        {
            tracker = null;
            if (pawn == null || !EnsureFluffyPriorityTypes())
            {
                return false;
            }

            try
            {
                object manager = _fluffyPriorityManagerGetProperty?.GetValue(null, null);
                tracker = _fluffyPriorityManagerIndexer?.GetValue(manager, new object[] { pawn });
                return tracker != null;
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[FluffyWorkTab] Failed to resolve priority tracker: " + ex.Message,
                    DebugFeature.ModSupport);
                tracker = null;
                return false;
            }
        }

        private static bool EnsureFluffyPriorityTypes()
        {
            if (_fluffyPriorityTypesResolved)
            {
                return HasFluffyPriorityAccess();
            }

            _fluffyPriorityTypesResolved = true;
            _fluffyPriorityManagerType = AccessTools.TypeByName("WorkTab.PriorityManager");
            _fluffyPriorityTrackerType = AccessTools.TypeByName("WorkTab.PriorityTracker");
            if (_fluffyPriorityManagerType == null || _fluffyPriorityTrackerType == null)
            {
                return false;
            }

            _fluffyPriorityManagerGetProperty = AccessTools.Property(_fluffyPriorityManagerType, "Get");
            _fluffyPriorityManagerIndexer = AccessTools.Property(_fluffyPriorityManagerType, "Item");
            _fluffyGetWorkTypePriorityMethod = AccessTools.Method(
                _fluffyPriorityTrackerType,
                "GetPriority",
                new[] { typeof(WorkTypeDef), typeof(int) });
            _fluffyGetWorkGiverPriorityMethod = AccessTools.Method(
                _fluffyPriorityTrackerType,
                "GetPriority",
                new[] { typeof(WorkGiverDef), typeof(int) });
            _fluffySetWorkTypePriorityAtHourMethod = AccessTools.Method(
                _fluffyPriorityTrackerType,
                "SetPriority",
                new[] { typeof(WorkTypeDef), typeof(int), typeof(int), typeof(bool) });
            _fluffySetWorkGiverPriorityAtHourMethod = AccessTools.Method(
                _fluffyPriorityTrackerType,
                "SetPriority",
                new[] { typeof(WorkGiverDef), typeof(int), typeof(int), typeof(bool) });

            return HasFluffyPriorityAccess();
        }

        private static bool HasFluffyPriorityAccess()
        {
            return _fluffyPriorityManagerType != null &&
                   _fluffyPriorityTrackerType != null &&
                   _fluffyPriorityManagerGetProperty != null &&
                   _fluffyPriorityManagerIndexer != null &&
                   _fluffyGetWorkTypePriorityMethod != null &&
                   _fluffyGetWorkGiverPriorityMethod != null &&
                   _fluffySetWorkTypePriorityAtHourMethod != null &&
                   _fluffySetWorkGiverPriorityAtHourMethod != null;
        }

        /// <summary>
        /// Fluffy Work Tab's own configured ceiling, defaulting to its shipped value of 9.
        /// </summary>
        internal static int MaxPriority => ReadFluffySetting(
            ref _fluffyMaxPriorityField,
            "maxPriority",
            FluffyDefaultMaxPriority);

        /// <summary>
        /// Fluffy Work Tab's configured priority for newly enabled work.
        /// </summary>
        internal static int DefaultPriority => Mathf.Clamp(
            ReadFluffySetting(ref _fluffyDefaultPriorityField, "defaultPriority", FluffyDefaultDefaultPriority),
            1,
            MaxPriority);

        private static int ReadFluffySetting(ref FieldInfo cachedField, string fieldName, int fallback)
        {
            EnsureFluffySettingsFields();
            if (cachedField == null)
            {
                return fallback;
            }

            try
            {
                return Mathf.Clamp((int)cachedField.GetValue(null), 1, FluffyDefaultMaxPriority);
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[FluffyWorkTab] Failed to read " + fieldName + ": " + ex.Message,
                    DebugFeature.ModSupport);
                return fallback;
            }
        }

        private static void EnsureFluffySettingsFields()
        {
            if (_fluffySettingsFieldsResolved)
            {
                return;
            }

            _fluffySettingsFieldsResolved = true;
            Type settingsType = AccessTools.TypeByName("WorkTab.Settings");
            if (settingsType == null)
            {
                // The agent harness can simulate the external Work-tab window
                // without loading Fluffy's settings assembly. Real partial or
                // incompatible installs should also degrade to shipped defaults.
                return;
            }

            _fluffyMaxPriorityField = AccessTools.Field(settingsType, "maxPriority");
            _fluffyDefaultPriorityField = AccessTools.Field(settingsType, "defaultPriority");
        }

        private static void DisableHostedColumns(string operation, Exception ex)
        {
            if (_fluffyHostedColumnsDisabled)
            {
                return;
            }

            _fluffyHostedColumnsDisabled = true;
            ClearSubWorkDrilldownStyleChooser();
            SubWorkDrilldownState.CollapseAllExpandBeside();
            string detail = ex == null ? string.Empty : ": " + ex.Message;
            BetterWorkTabMod.DebugLog(
                "[FluffyWorkTab] Disabling Fluffy-style specific-job columns after " + operation + detail,
                DebugFeature.ModSupport);
        }

        private static void DisableExternalFluffyColumns(string operation, Exception ex)
        {
            if (_externalFluffyColumnsUnavailable)
            {
                return;
            }

            _externalFluffyColumnsUnavailable = true;
            _fluffyHostedWindowInstance = null;
            string detail = ex == null ? string.Empty : ": " + ex.Message;
            BetterWorkTabMod.DebugLog(
                "[FluffyWorkTab] External column compatibility is unavailable after " + operation +
                detail + "; BWT-native Fluffy-style columns remain enabled.",
                DebugFeature.ModSupport);
        }

        /// <summary>
        /// Clamps a Better Work Tab schedule into Fluffy Work Tab's accepted range.
        /// </summary>
        /// <remarks>
        /// This must clamp, never truncate to zero. WorkTab.PriorityTracker.SetPriority turns any value
        /// above <c>Settings.maxPriority</c> into 0, which means "disabled" — so handing Fluffy an
        /// extended Better Work Tab priority would silently switch the work off instead of capping it.
        /// Better Work Tab allows priorities up to <see cref="PriorityConstants.ExtendedHardMax"/> while
        /// Fluffy allows at most 9, so this narrowing is always possible.
        /// </remarks>
        private static int[] NormalizeFluffyPriorities(int[] priorities)
        {
            int maxPriority = MaxPriority;
            var normalized = new int[TimePriorityService.HoursPerDay];
            for (int hour = 0; hour < normalized.Length; hour++)
            {
                int priority = priorities != null && hour < priorities.Length ? priorities[hour] : 0;
                normalized[hour] = priority <= 0
                    ? 0
                    : Mathf.Clamp(priority, 1, maxPriority);
            }

            return normalized;
        }

        private sealed class FluffyWorkTabSettingsContributor : IModSettingsContributor
        {
            private const string WorkTabOwnedByFluffyReason = "An external Work tab integration is running the Work tab right now.";

            public BWTModSettingsSection CreateSettingsSection(SettingsScope<BetterWorkTabSettings> scope)
            {
                return new BWTModSettingsSection
                {
                    Header =                     scope.Define(
                        CompatFluffyWorkTabHeader,
                        SettingType.Header,
                        "Fluffy-style Work Tab",
                        tooltip: "BWT-native options inspired by Fluffy's Work Tab, plus compatibility controls when Fluffy Work Tab or Sleek Work Priorities is installed."
                    )
                        .SearchableBy(FluffyBaseSearchKeywords)
                        .Ordered(0)
                        .Accented(new Color(0.67f, 0.75f, 0.92f)),
                    Children = new[]
                    {
                                                scope.Toggle(
                            FluffyStyleFeatures,
                            settings => settings.enableFluffyStyleFeatures,
                            "Use these controls",
                            tooltip: "Use BWT's Fluffy-inspired Work tab controls and right-expanding specific-job columns. BWT owns the UI and priority data; Fluffy Work Tab is not required.",
                            onChanged: _ =>
                            {
                                SubWorkDrilldownState.CollapseAllExpandBeside();
                                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                            }
                        )
                            .DefaultTo(DefaultSettings.enableFluffyStyleFeatures)
                            .SearchableBy(FluffyControlsSearchKeywords)
                            .ControlsChildren()
                            .Ordered(0),
                                                scope.Under(CompatFluffyWorkTabOwnership).Toggle(
                            FluffyStyleTopButtons,
                            settings => settings.showFluffyStyleTopButtons,
                            "Show top controls",
                            tooltip: "When Fluffy Work Tab is installed, show its familiar icon controls for manual priorities, time schedules, and expanding or collapsing specific jobs."
                        )
                            .DefaultTo(DefaultSettings.showFluffyStyleTopButtons)
                            .SearchableBy(FluffyControlsSearchKeywords)
                            .Ordered(1),
                                                scope.Under(FluffyStyleFeatures).Toggle(
                            FluffyStyleStandaloneTopButtons,
                            settings => settings.showStandaloneFluffyStyleTopButtons,
                            "Show text substitute controls",
                            tooltip: "Without Fluffy Work Tab, optionally show BWT-drawn text substitutes for the three top controls. Off by default because Fluffy's icon assets are unavailable."
                        )
                            .DefaultTo(DefaultSettings.showStandaloneFluffyStyleTopButtons)
                            .SearchableBy(FluffyControlsSearchKeywords)
                            .Ordered(2)
                            .ShownWhen(_ => !FluffyWorkTabGateway.IsPresent),
                                                scope.Under(CompatFluffyWorkTabOwnership).Toggle(
                            FluffyStyleScheduleAssigner,
                            settings => settings.enableFluffyScheduleAssigner,
                            "Use hour-selection scheduler",
                            tooltip: "Use Fluffy's original bottom hour selector: choose hours, then click normal priority boxes to assign those hours. This option requires Fluffy Work Tab because it uses Fluffy's scheduler assets."
                        )
                            .DefaultTo(DefaultSettings.enableFluffyScheduleAssigner)
                            .SearchableBy(FluffyScheduleSearchKeywords)
                            .Ordered(3),
                                                scope.Define(
                            CompatFluffyWorkTabOwnership,
                            SettingType.Header,
                            "Fluffy Work Tab compatibility",
                            tooltip: "Choose which compatible mod runs the Work tab. When Fluffy Work Tab is installed, also choose whether its columns remain visible."
                        )
                            .SearchableBy(FluffyOwnershipSearchKeywords)
                            .Ordered(20)
                            .Configure(definition =>
                            {
                                definition.Suppressions = new List<SettingSuppression>
                                    {
                                    OptionalModSettingsAvailability.Require(
                                    () => FluffyWorkTabGateway.AnyExternalWorkTabPresent ||
                                          FluffyWorkTabGateway.PriorityDataAuthorityRequestsFluffy,
                                    "Fluffy Work Tab or Sleek Work Priorities",
                                    "https://steamcommunity.com/sharedfiles/filedetails/?id=3453549086")
                                    };
                            }),
                                                scope.Under(CompatFluffyWorkTabOwnership).Enum(
                            CompatFluffyWorkTabOwner,
                            settings => settings.preferredWorkTabOwner,
                            "Which mod opens the Work tab?",
                            tooltip: "Choose BWT-only, Sleek-only, Fluffy-only, or the mixed BWT + Sleek host where BWT owns rows, dividers, and headers while Sleek renders its priority cells and companion work cards.",
                            onChanged: value =>
                            {
                                if (value is BetterWorkTabSettings changedSettings)
                                {
                                    changedSettings.workTabOwnerSelectionMade = true;
                                }
                                PriorityAuthorityBroker.NotifyPotentialAuthorityChanged();
                                FluffyWorkTabCoexistence.ApplyDesiredOwner(reopenIfOpen: true);
                            }
                        )
                            .DefaultTo(DefaultSettings.preferredWorkTabOwner)
                            .SearchableBy(FluffyOwnershipSearchKeywords)
                            .Ordered(21),
                        scope.Under(CompatFluffyWorkTabOwnership).Enum(
                            CompatFluffyWorkTabPriorityAuthority,
                            settings => settings.priorityDataAuthority,
                            "Priority data source",
                            tooltip: "Choose which compatible system owns shared work priorities. This is independent of which mod opens or draws the Work tab.",
                            labelProvider: GetPriorityDataAuthorityLabel,
                            descriptionProvider: GetPriorityDataAuthorityDescription,
                            onChanged: OnPriorityDataAuthorityChanged
                        )
                            .DefaultTo(DefaultSettings.priorityDataAuthority)
                            .SearchableBy(FluffyKeywords(
                                "priority data source", "priority authority", "priority tracker", "shared priorities",
                                "work type priorities", "specific job priorities", "hourly schedules"))
                            .Ordered(22),
                                                scope.Under(CompatFluffyWorkTabOwnership).Toggle(
                            CompatExternalWorkTabColumns,
                            settings => settings.showExternalWorkTabColumns,
                            FluffyWorkTabGateway.ColumnVisibilitySettingLabel,
                            tooltip: FluffyWorkTabGateway.ColumnVisibilitySettingTooltip,
                            onChanged: _ => FluffyWorkTabGateway.ApplyColumnVisibility()
                        )
                            .DefaultTo(DefaultSettings.showExternalWorkTabColumns)
                            .SearchableBy(FluffyOwnershipSearchKeywords)
                            .Ordered(23)
                            .Configure(definition =>
                            {
                                definition.Suppressions = new List<SettingSuppression>
                                    {
                                    CreateWorkTabOwnedByFluffySuppression(WorkTabOwnedByFluffyReason)
                                    };
                            }),
                                                scope.Under(FluffyStyleFeatures).Define(
                            CompatFluffyWorkTabSpecificJobs,
                            SettingType.Header,
                            "Specific jobs",
                            tooltip: "Choose how BWT opens a Work column into its individual jobs."
                        )
                            .SearchableBy(FluffySpecificJobsSearchKeywords)
                            .Ordered(10),
                                                scope.Under(CompatFluffyWorkTabSpecificJobs).Enum(
                            SubWorkDrilldownStyle,
                            settings => settings.subWorkDrilldownStyle,
                            "Specific-job view",
                            tooltip: "Choose BWT's focused full-tab view or Fluffy-inspired right-expanding columns. Both modes are implemented by BWT and work without Fluffy Work Tab installed.",
                            onChanged: _ => HeaderDrawingCoordinator.NotifyAngledHeadersChanged()
                        )
                            .DefaultTo(DefaultSettings.subWorkDrilldownStyle)
                            .SearchableBy(FluffySpecificJobsSearchKeywords)
                            .Ordered(11)
                            .Configure(definition =>
                            {
                                definition.Suppressions = new List<SettingSuppression>
                                    {
                                    CreateWorkTabOwnedByFluffySuppression(WorkTabOwnedByFluffyReason)
                                    };
                            }),
                                                scope.Under(CompatFluffyWorkTabHeader).Define(
                            ControlsFluffyHeader,
                            SettingType.Header,
                            "Native gestures",
                            tooltip: "Contextual controls defined by Fluffy Work Tab. Fluffy uses fixed mouse-and-modifier gestures rather than RimWorld-rebindable key definitions."
                        )
                            .SearchableBy(FluffyKeywords("controls", "keybindings", "keyboard", "mouse", "shortcuts"))
                            .Ordered(100)
                            .Accented(new Color(0.67f, 0.75f, 0.92f))
                            .ShownWhen(_ => FluffyWorkTabGateway.IsPresent),
                                                scope.Under(ControlsFluffyHeader).Custom(
                            ControlsFluffyExpand,
                            (rect, label, tooltip, _, disabled) =>
                                BWTSettingWidgets.DrawReadOnlyValue(rect, label, "Ctrl-click Work header", tooltip, disabled),
                            "Open or close specific jobs",
                            tooltip: "Fluffy's native Ctrl-header gesture is preserved. When BWT owns the tab, the configured specific-job shortcut routes through BWT and Expand beside opens Fluffy-style columns."
                        )
                            .SearchableBy(FluffyKeywords("ctrl click", "expand", "collapse", "specific jobs"))
                            .Ordered(101)
                            .ShownWhen(_ => FluffyWorkTabGateway.IsPresent),
                                                scope.Under(ControlsFluffyHeader).Custom(
                            ControlsFluffyBatch,
                            (rect, label, tooltip, _, disabled) =>
                                BWTSettingWidgets.DrawReadOnlyValue(rect, label, "Shift-click / Shift-wheel", tooltip, disabled),
                            "Change a whole Work column",
                            tooltip: "Shift-scroll changes all capable pawn priorities. Shift-click changes a whole specific-job column; on root Work headers, BWT's optional grouping action owns Shift-left-click."
                        )
                            .SearchableBy(FluffyKeywords("shift scroll", "shift click", "batch priority", "all pawns"))
                            .Ordered(102)
                            .ShownWhen(_ => FluffyWorkTabGateway.IsPresent),
                                                scope.Under(ControlsFluffyHeader).Custom(
                            ControlsFluffyPawnRows,
                            (rect, label, tooltip, _, disabled) =>
                                BWTSettingWidgets.DrawReadOnlyValue(rect, label, "Shift-click / Shift-wheel", tooltip, disabled),
                            "Adjust a pawn row",
                            tooltip: "Fluffy's Shift-click and Shift-wheel pawn-name gesture remains available when Fluffy owns the Work tab. BWT-owned layouts keep fixed aligned pawn-row heights."
                        )
                            .SearchableBy(FluffyKeywords("pawn row height", "shift pawn label", "name column"))
                            .Ordered(103)
                            .ShownWhen(_ => FluffyWorkTabGateway.IsPresent)
                    }
                };
            }
        }

        private static PawnColumnDef GetOrCreateHostedWorkTypeColumn(PawnColumnDef sourceWorkColumn, WorkTypeDef workType)
        {
            string key = workType.defName ?? string.Empty;
            if (HostedWorkTypeColumns.TryGetValue(key, out PawnColumnDef existing))
            {
                return existing;
            }

            try
            {
                var column = new PawnColumnDef
                {
                    defName = "BWT_FluffyStyle_WorkType_" + key,
                    workerClass = typeof(PawnColumnWorker_BwtSubWorkPriority),
                    workType = workType,
                    sortable = sourceWorkColumn?.sortable ?? true,
                    moveWorkTypeLabelDown = sourceWorkColumn?.moveWorkTypeLabelDown ?? false
                };
                column.PostLoad();
                HostedWorkTypeColumns[key] = column;
                return column;
            }
            catch (Exception ex)
            {
                DisableHostedColumns("creating hosted work-type column", ex);
                return null;
            }
        }

        private static PawnColumnDef GetOrCreateHostedWorkGiverColumn(WorkGiverDef workGiver)
        {
            // The work-giver column carries its parent work type because the vanilla work-priority
            // worker expects one. BWT's layout metadata supplies the individual work giver.
            if (workGiver?.defName == null || workGiver.workType == null || !CanHostFluffySubWorkColumns)
            {
                return null;
            }

            string key = workGiver.defName;
            if (HostedWorkGiverColumns.TryGetValue(key, out PawnColumnDef existing))
            {
                return existing;
            }

            try
            {
                var column = new PawnColumnDef();
                if (column == null)
                {
                    DisableHostedColumns("creating Fluffy-style work-giver column instance", null);
                    return null;
                }

                column.defName = "BWT_FluffyStyle_WorkGiver_" + key;
                column.workerClass = typeof(PawnColumnWorker_BwtSubWorkPriority);
                column.description = workGiver.description;

                // Mirrors WorkTab.DefGenerator_GenerateImpliedDefs_PreResolve: the work-giver column carries
                // its parent work type. Vanilla's DoHeader reads def.workType.labelShort before Fluffy's
                // transpiler swaps in the work-giver label, so leaving this null throws a NullReferenceException
                // the moment the hosted column is drawn.
                column.workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver) ?? workGiver.workType;
                column.sortable = true;
                NativeHostedWorkGivers[column] = workGiver;
                column.PostLoad();
                HostedWorkGiverColumns[key] = column;
                return column;
            }
            catch (Exception ex)
            {
                DisableHostedColumns("creating hosted work-giver column", ex);
                return null;
            }
        }

        private static List<WorkGiverDef> GetBwtDisplayWorkGivers(WorkTypeDef workType)
        {
            var result = new List<WorkGiverDef>();
            if (workType == null)
            {
                return result;
            }

            IReadOnlyList<WorkGiver> workGivers =
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; workGivers != null && i < workGivers.Count; i++)
            {
                WorkGiverDef def = workGivers[i]?.def;
                if (def != null)
                {
                    result.Add(def);
                }
            }

            return result;
        }
    }
}
