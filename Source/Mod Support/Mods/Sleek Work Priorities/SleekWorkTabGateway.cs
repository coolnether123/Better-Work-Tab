using System;
using System.Collections.Generic;
using System.Reflection;
using Better_Work_Tab.API;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities
{
    /// <summary>
    /// Compatibility boundary for Sleek Work Priorities. BWT never references Sleek's assembly
    /// directly; the optional integration is package/type-probed and fails closed when a member is
    /// absent in a future Sleek release.
    /// </summary>
    internal static class SleekWorkTabGateway
    {
        private const string SleekWorkCompatTypeName = "SleekWorkPriorities.SleekWorkCompat";
        private const string SleekWorkStartupTypeName = "SleekWorkPriorities.SleekWorkStartup";
        private const string WorkTypeOrderTypeName = "SleekWorkPriorities.WorkTypeOrder";
        private const string SleekWorkGiverStoreTypeName = "SleekWorkPriorities.SleekWorkGiverStore";
        private const string SleekToolbarTypeName = "SleekWorkPriorities.Patch_Toolbar";
        private const string RenderPatchTypeName = "SleekWorkPriorities.Patch_WorkTab_Render";
        private const string SleekWorkSearchTypeName = "SleekWorkPriorities.WorkSearch";
        private const string SleekInlineJobColumnsTypeName = "SleekWorkPriorities.InlineJobColumns";
        private const string SleekInlineJobWorkerTypeName = "SleekWorkPriorities.PawnColumnWorker_SleekInlineJob";

        private static readonly Harmony CompatibilityHarmony =
            new Harmony("Coolnether123.betterworktab.sleek");
        private static readonly SleekWorkTabExternalStore ExternalStore =
            new SleekWorkTabExternalStore();
        private static bool _externalStoreRegistered;

        private static bool? _detected;
        private static bool _detectionComplete;
        private static string _detectedPackageId;
        private static Type _patchedCompatType;
        private static bool _betterWorkTabGetterPatched;
        private static bool _fluffyWorkTabGetterPatched;
        private static bool _sleekRenderPostfixPatched;
        private static bool _sleekPawnTablePositionPatched;
        private static bool _sleekWorkTypeOrderEnabledPatched;
        private static bool _sleekWorkTypeOrderReconcilePatched;
        private static PropertyInfo _sleekStoreGetter;
        private static MethodInfo _sleekGetOverrideMethod;
        private static MethodInfo _sleekSetOverrideMethod;
        private static MethodInfo _sleekDrawToolbarExtrasMethod;
        private static MethodInfo _sleekDrawToolbarToggleMethod;
        private static bool _sleekToolbarResolved;
        private static bool _sleekSearchResolved;
        private static PropertyInfo _sleekSearchActive;
        private static PropertyInfo _sleekSearchText;
        private static PropertyInfo _sleekSearchSelectedJob;
        private static MethodInfo _sleekSearchMatches;
        private static Type _sleekInlineJobWorkerType;
        private static MethodInfo _sleekInlineJobGiverFor;
        private static MethodInfo _sleekInlineJobDefFor;
        private static MethodInfo _sleekInlineJobToggle;
        private static MethodInfo _sleekInlineJobApplyPendingToggle;
        private static MethodInfo _sleekInlineJobCollapse;
        private static Func<bool> _sleekInlineActiveGetter;
        private static Func<WorkTypeDef> _sleekInlineExpandedGetter;
        private static bool _mixedSubWorkExpansionCaptured;
        private static WorkTypeDef _mixedSubWorkPreviousExpansion;
        private static Func<bool> _sleekSearchActiveGetter;
        private static Func<string> _sleekSearchTextGetter;
        private static Func<WorkGiverDef> _sleekSearchSelectedJobGetter;
        private static Func<Pawn, bool> _sleekSearchMatchesDelegate;
        private static IReadOnlyList<Pawn> _mixedSearchSource;
        private static IReadOnlyList<Pawn> _mixedSearchResult;
        private static bool _mixedSearchWasActive;
        private static string _mixedSearchText;
        private static WorkGiverDef _mixedSearchSelectedJob;
        private static int _mixedSearchSourceCount = -1;
        private static int _mixedSearchSourceSignature;
        private static int _mixedSearchRevision;

        internal static int MixedSearchRevision => _mixedSearchRevision;

        internal static bool IsPresent
        {
            get
            {
                EnsureDetected();
                return _detected == true;
            }
        }

        internal static string DetectedPackageId
        {
            get
            {
                EnsureDetected();
                return _detectedPackageId;
            }
        }

        internal static bool SleekOwnsWorkTab =>
            IsPresent &&
            BetterWorkTabMod.Settings?.preferredWorkTabOwner == WorkTabOwnerPreference.SleekWorkPriorities;

        /// <summary>
        /// The mixed mode keeps BWT's window, row layout, dividers, and input pipeline active,
        /// while allowing Sleek's own cell/header/order patches to execute inside that host.
        /// </summary>
        internal static bool BetterWorkTabHostsSleek =>
            IsPresent &&
            BetterWorkTabMod.Settings?.preferredWorkTabOwner ==
                WorkTabOwnerPreference.BetterWorkTabWithSleekWorkPriorities;

        internal static bool SleekCodeRuns => SleekOwnsWorkTab || BetterWorkTabHostsSleek;

        internal static string StoreId => SleekWorkTabIdentity.ProviderId;

        internal static void Initialize()
        {
            RegisterPriorityProvider();
            FluffyWorkTabCoexistence.ApplyDefaultExternalCompatibility();
            TryApplyCompatibilityPatch();

            // Sleek's assembly and its optional late compatibility scan may not exist yet when BWT's
            // Mod constructor runs. Reconcile after all long-loading events as well as immediately.
            LongEventHandler.ExecuteWhenFinished(ReconcileCompatibility);
        }

        internal static void RegisterPriorityProvider()
        {
            if (!IsPresent)
            {
                return;
            }

            if (_externalStoreRegistered &&
                !ExternalWorkTabRegistry.IsCurrentStoreRegistration(ExternalStore))
            {
                _externalStoreRegistered = false;
            }

            if (!_externalStoreRegistered && ExternalWorkTabApi.RegisterStore(ExternalStore))
            {
                _externalStoreRegistered = true;
            }
        }

        internal static int ImportChildRanksToBetterWorkTab()
        {
            return SleekWorkTabPriorityHandoff.ImportChildRanksToBetterWorkTab();
        }

        internal static IReadOnlyList<Pawn> ApplyMixedSearch(IReadOnlyList<Pawn> pawns)
        {
            if (!BetterWorkTabHostsSleek || pawns == null)
            {
                return pawns;
            }

            EnsureSleekSearchAccessors();
            if (_sleekSearchActiveGetter == null || _sleekSearchMatchesDelegate == null)
            {
                return pawns;
            }

            try
            {
                bool active = _sleekSearchActiveGetter();
                string text = _sleekSearchTextGetter?.Invoke() ?? "";
                WorkGiverDef selectedJob = _sleekSearchSelectedJobGetter?.Invoke();
                int sourceSignature = 17;
                unchecked
                {
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        sourceSignature = sourceSignature * 31 +
                            (pawns[i]?.thingIDNumber ?? 0);
                    }
                }
                if (!active)
                {
                    if (_mixedSearchWasActive || _mixedSearchSource != null)
                    {
                        _mixedSearchRevision++;
                    }
                    _mixedSearchSource = null;
                    _mixedSearchResult = null;
                    _mixedSearchSourceCount = -1;
                    _mixedSearchSourceSignature = 0;
                    _mixedSearchWasActive = false;
                    _mixedSearchText = null;
                    _mixedSearchSelectedJob = null;
                    return pawns;
                }

                if (ReferenceEquals(_mixedSearchSource, pawns) &&
                    _mixedSearchSourceCount == pawns.Count &&
                    _mixedSearchSourceSignature == sourceSignature &&
                    _mixedSearchWasActive == active &&
                    string.Equals(_mixedSearchText, text, StringComparison.Ordinal) &&
                    ReferenceEquals(_mixedSearchSelectedJob, selectedJob))
                {
                    return _mixedSearchResult ?? pawns;
                }

                _mixedSearchRevision++;
                List<Pawn> filtered = new List<Pawn>(pawns.Count);
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (_sleekSearchMatchesDelegate(pawn))
                    {
                        filtered.Add(pawn);
                    }
                }

                _mixedSearchSource = pawns;
                _mixedSearchSourceCount = pawns.Count;
                _mixedSearchSourceSignature = sourceSignature;
                _mixedSearchWasActive = active;
                _mixedSearchText = text;
                _mixedSearchSelectedJob = selectedJob;
                _mixedSearchResult = filtered;
                return filtered;
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed search projection skipped: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                return pawns;
            }
        }

        internal static void DrawMixedToolbarExtras(Rect rect)
        {
            if (!BetterWorkTabHostsSleek)
            {
                return;
            }

            EnsureSleekToolbarMethod();
            try
            {
                _sleekDrawToolbarToggleMethod?.Invoke(null, null);
                // Let Sleek lay out its complete toolbar against the full BWT
                // width. Its search/help cluster belongs on the right; BWT's
                // centered owner/settings controls are drawn afterward into
                // the open middle slot.
                _sleekDrawToolbarExtrasMethod?.Invoke(null, new object[] { rect });
                SleekWorkTabCoexistenceUI.DrawMixedToolbarExtras(rect);
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed toolbar draw skipped: " + exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
            }
        }

        internal static bool MixedSearchShowsWorkType(WorkTypeDef workType)
        {
            if (!BetterWorkTabHostsSleek || workType == null)
            {
                return true;
            }

            EnsureSleekSearchAccessors();
            WorkGiverDef selectedJob = _sleekSearchSelectedJobGetter?.Invoke();
            return selectedJob == null || selectedJob.workType == workType;
        }

        internal static bool MixedSearchShowsWorkGiver(WorkGiverDef workGiver)
        {
            if (!BetterWorkTabHostsSleek || workGiver == null)
            {
                return true;
            }

            EnsureSleekSearchAccessors();
            WorkGiverDef selectedJob = _sleekSearchSelectedJobGetter?.Invoke();
            return selectedJob == null || selectedJob == workGiver;
        }

        internal static bool IsSleekInlineJobColumn(PawnColumnDef column)
        {
            if (column?.Worker == null)
            {
                return false;
            }

            EnsureSleekInlineJobAccessors();
            return _sleekInlineJobWorkerType?.IsInstanceOfType(column.Worker) == true;
        }

        internal static bool TryGetSleekInlineJobGiver(
            PawnColumnDef column,
            out WorkGiverDef workGiver)
        {
            workGiver = null;
            if (!IsSleekInlineJobColumn(column) || _sleekInlineJobGiverFor == null)
            {
                return false;
            }

            try
            {
                workGiver = _sleekInlineJobGiverFor.Invoke(null, new object[] { column }) as WorkGiverDef;
                return workGiver != null;
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Inline job search projection skipped: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                return false;
            }
        }

        internal static bool TryGetSleekInlineJobColumn(
            WorkGiverDef workGiver,
            out PawnColumnDef column)
        {
            column = null;
            if (!BetterWorkTabHostsSleek || workGiver == null)
            {
                return false;
            }

            EnsureSleekInlineJobAccessors();
            if (_sleekInlineJobDefFor == null)
            {
                return false;
            }

            try
            {
                column = _sleekInlineJobDefFor.Invoke(null, new object[] { workGiver }) as PawnColumnDef;
                return column?.Worker != null;
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Inline job column projection skipped: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                return false;
            }
        }

        internal static void SyncMixedSubWorkExpansion(PawnTable table, WorkTypeDef workType)
        {
            if (!BetterWorkTabHostsSleek || table == null || workType == null)
            {
                return;
            }

            EnsureSleekInlineJobAccessors();
            if (_sleekInlineActiveGetter == null ||
                _sleekInlineExpandedGetter == null ||
                !_sleekInlineActiveGetter())
            {
                return;
            }

            if (!_mixedSubWorkExpansionCaptured)
            {
                _mixedSubWorkPreviousExpansion = _sleekInlineExpandedGetter();
                _mixedSubWorkExpansionCaptured = true;
            }

            if (_sleekInlineExpandedGetter() == workType || _sleekInlineJobToggle == null)
            {
                return;
            }

            try
            {
                _sleekInlineJobToggle.Invoke(null, new object[] { workType, table });
                _sleekInlineJobApplyPendingToggle?.Invoke(null, new object[] { table });
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed sub-work expansion sync skipped: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
            }
        }

        internal static void RestoreMixedSubWorkExpansion(PawnTable table)
        {
            if (!_mixedSubWorkExpansionCaptured)
            {
                return;
            }

            EnsureSleekInlineJobAccessors();
            try
            {
                WorkTypeDef current = _sleekInlineExpandedGetter?.Invoke();
                if (current != _mixedSubWorkPreviousExpansion)
                {
                    if (_mixedSubWorkPreviousExpansion == null)
                    {
                        _sleekInlineJobCollapse?.Invoke(null, new object[] { table });
                    }
                    else if (_sleekInlineJobToggle != null)
                    {
                        _sleekInlineJobToggle.Invoke(
                            null,
                            new object[] { _mixedSubWorkPreviousExpansion, table });
                        _sleekInlineJobApplyPendingToggle?.Invoke(null, new object[] { table });
                    }
                }
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed sub-work expansion restore skipped: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
            }
            finally
            {
                _mixedSubWorkExpansionCaptured = false;
                _mixedSubWorkPreviousExpansion = null;
            }
        }

        internal static void DrawMixedHeaderBackdrop(Rect rect)
        {
            if (BetterWorkTabHostsSleek)
            {
                SleekWorkTabHostedFrame.DrawHeaderBackdrop(rect);
            }
        }

        internal static void ReconcileCompatibility()
        {
            ReconcileDetection();
            FluffyWorkTabGateway.ReconcileOptionalRegistration();
            if (!IsPresent)
            {
                return;
            }

            RegisterPriorityProvider();
            FluffyWorkTabCoexistence.ApplyDefaultExternalCompatibility();
            TryApplyCompatibilityPatch();
            InvokeStaticNoArguments(SleekWorkStartupTypeName, "ReconcileCompatibility");
            InvokeStaticNoArguments(WorkTypeOrderTypeName, "Reconcile");
        }

        /// <summary>
        /// Rechecks a cached negative optional-mod result at the explicit post-load reconciliation
        /// boundary. Normal IsPresent reads remain cached, so closed-tab/event paths do not poll.
        /// </summary>
        internal static void ReconcileDetection()
        {
            if (_detected == true)
            {
                return;
            }

            _detected = null;
            _detectionComplete = false;
            _detectedPackageId = null;
            EnsureDetected();
        }

        internal static bool TryGetSleekWorkGiverOverride(
            Pawn pawn,
            WorkGiverDef workGiver,
            out int priority)
        {
            priority = -1;
            if (pawn == null || workGiver == null)
            {
                return false;
            }

            EnsureSleekStoreAccessors();
            if (_sleekStoreGetter == null || _sleekGetOverrideMethod == null)
            {
                return false;
            }

            try
            {
                object store = _sleekStoreGetter.GetValue(null, null);
                if (store == null)
                {
                    return false;
                }

                object value = _sleekGetOverrideMethod.Invoke(
                    store,
                    new object[] { pawn, workGiver });
                if (!(value is int))
                {
                    return false;
                }

                priority = (int)value;
                return true;
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Could not read a Sleek per-job override: " + exception.Message,
                    DebugFeature.ModSupport);
                return false;
            }
        }

        /// <summary>
        /// Writes a Rule Builder sub-work result into Sleek's own per-job
        /// store. BWT's legacy reassignment sidecar cannot be read by Sleek's
        /// cells, so this bridge is required while Sleek is the verified
        /// external priority authority.
        /// A read-back confirms that Sleek accepted the write instead of
        /// silently ignoring it because its board is not active yet.
        /// </summary>
        internal static bool TrySetSleekWorkGiverOverride(
            Pawn pawn,
            WorkGiverDef workGiver,
            int priority)
        {
            if (pawn == null || workGiver == null ||
                !IsVerifiedSleekPriorityAuthority(out long authorityRevision))
            {
                return false;
            }

            EnsureSleekStoreAccessors();
            if (_sleekStoreGetter == null ||
                _sleekSetOverrideMethod == null ||
                _sleekGetOverrideMethod == null)
            {
                return false;
            }

            try
            {
                object store = _sleekStoreGetter.GetValue(null, null);
                if (store == null)
                {
                    return false;
                }

                _sleekSetOverrideMethod.Invoke(
                    store,
                    new object[] { pawn, workGiver, priority });

                bool accepted = _sleekGetOverrideMethod.Invoke(
                           store,
                           new object[] { pawn, workGiver }) is int stored &&
                       stored == RuleBuilder2SleekPriorityTranslation.TranslatePriority(priority);
                return accepted &&
                    IsVerifiedSleekPriorityAuthority(out long afterRevision) &&
                    afterRevision == authorityRevision;
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Could not write a Sleek per-job override: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                return false;
            }
        }

        /// <summary>
        /// Verifies that Sleek is the current, generation-validated external priority owner.
        /// Mixed BWT/Sleek rendering does not satisfy this: BWT remains the shared data owner
        /// there and Sleek's inline worker must be read-only/fallback-rendered.
        /// </summary>
        private static bool IsVerifiedSleekPriorityAuthority(out long authorityRevision)
        {
            authorityRevision = 0L;
            if (!SleekOwnsWorkTab)
            {
                return false;
            }

            if (!PriorityAuthorityResolver.TryGetVerifiedExternalStore(
                    out IExternalWorkTabStore store,
                    out authorityRevision) ||
                !ReferenceEquals(store, ExternalStore))
            {
                authorityRevision = 0L;
                return false;
            }

            return true;
        }

        internal static int GetSharedWorkTypePriority(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return WorkPrioritySystem.DisabledPriority;
            }

            return PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                pawn.workSettings,
                workType);
        }


        private static void TryApplyCompatibilityPatch()
        {
            Type compatType = AccessTools.TypeByName(SleekWorkCompatTypeName);
            if (compatType == null)
            {
                return;
            }

            if (_patchedCompatType != compatType)
            {
                _patchedCompatType = compatType;
                _betterWorkTabGetterPatched = false;
                _fluffyWorkTabGetterPatched = false;
                _sleekRenderPostfixPatched = false;
                _sleekPawnTablePositionPatched = false;
                _sleekWorkTypeOrderEnabledPatched = false;
                _sleekWorkTypeOrderReconcilePatched = false;
            }

            try
            {
                // Sleek's toolbar/render helper methods touch WorkBoardUI's Texture2D state during
                // method preparation. Do not detour those methods while RimWorld is still creating
                // the graphics domain; the owner getter patches are safe during mod construction,
                // and ReconcileCompatibility retries the UI hooks once the game is Playing.
                bool allowUiHooks = Current.ProgramState == ProgramState.Playing;

                if (!_betterWorkTabGetterPatched)
                {
                    MethodInfo getter = AccessTools.PropertyGetter(compatType, "BetterWorkTabActive");
                    if (getter != null)
                    {
                        CompatibilityHarmony.Patch(
                            getter,
                            prefix: new HarmonyMethod(
                                typeof(SleekWorkTabGateway),
                                nameof(BetterWorkTabActivePrefix)));
                        _betterWorkTabGetterPatched = true;
                    }
                }

                if (!_fluffyWorkTabGetterPatched)
                {
                    MethodInfo getter = AccessTools.PropertyGetter(compatType, "FluffyWorkTabActive");
                    if (getter != null)
                    {
                        CompatibilityHarmony.Patch(
                            getter,
                            prefix: new HarmonyMethod(
                                typeof(SleekWorkTabGateway),
                                nameof(FluffyWorkTabActivePrefix)));
                        _fluffyWorkTabGetterPatched = true;
                    }
                }

                // In mixed mode BWT's saved column order is authoritative. Sleek
                // must still run its cell/header adapters, but its optional
                // WorkTypeOrder mutator would otherwise reorder the same table
                // after BWT has applied the player's arrangement.
                if (!_sleekWorkTypeOrderEnabledPatched || !_sleekWorkTypeOrderReconcilePatched)
                {
                    Type orderType = AccessTools.TypeByName(WorkTypeOrderTypeName);
                    MethodInfo enabledGetter = AccessTools.PropertyGetter(orderType, "Enabled");
                    if (!_sleekWorkTypeOrderEnabledPatched && enabledGetter != null)
                    {
                        CompatibilityHarmony.Patch(
                            enabledGetter,
                            prefix: new HarmonyMethod(
                                typeof(SleekWorkTabGateway),
                                nameof(SleekWorkTypeOrderEnabledPrefix)));
                        _sleekWorkTypeOrderEnabledPatched = true;
                    }

                    MethodInfo reconcile = AccessTools.Method(orderType, "Reconcile");
                    if (!_sleekWorkTypeOrderReconcilePatched && reconcile != null)
                    {
                        CompatibilityHarmony.Patch(
                            reconcile,
                            prefix: new HarmonyMethod(
                                typeof(SleekWorkTabGateway),
                                nameof(SleekWorkTypeOrderReconcilePrefix)));
                        _sleekWorkTypeOrderReconcilePatched = true;
                    }
                }

                if (allowUiHooks && !_sleekRenderPostfixPatched)
                {
                    Type renderType = AccessTools.TypeByName(RenderPatchTypeName);
                    MethodInfo renderPostfix = AccessTools.Method(
                        renderType,
                        "Postfix",
                        new[] { typeof(UnityEngine.Rect), typeof(MainTabWindow_Work) });
                    if (renderPostfix != null)
                    {
                        CompatibilityHarmony.Patch(
                            renderPostfix,
                            postfix: new HarmonyMethod(
                                typeof(SleekWorkTabGateway),
                                nameof(SleekWorkTabRenderPostfix)));
                        _sleekRenderPostfixPatched = true;
                    }
                }

                if (allowUiHooks && !_sleekPawnTablePositionPatched)
                {
                    MethodInfo pawnTableGui = AccessTools.Method(
                        typeof(PawnTable),
                        "PawnTableOnGUI",
                        new[] { typeof(UnityEngine.Vector2) });
                    if (pawnTableGui != null)
                    {
                        CompatibilityHarmony.Patch(
                            pawnTableGui,
                            prefix: new HarmonyMethod(
                                typeof(SleekWorkTabGateway),
                                nameof(SleekStrictPawnTablePrefix)));
                        _sleekPawnTablePositionPatched = true;
                    }
                }

            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Better Work Tab] Sleek compatibility patch skipped: " + exception.Message,
                    74239501);
            }
        }

        private static bool BetterWorkTabActivePrefix(ref bool __result)
        {
            // Sleek yields in BWT-only mode. In Sleek-only mode it owns the vanilla Work window;
            // in mixed mode BWT owns the host but Sleek's render/order patches must still run.
            __result = !SleekCodeRuns;
            return false;
        }

        private static bool FluffyWorkTabActivePrefix(ref bool __result)
        {
            // If Fluffy is installed but not selected, it must not make Sleek yield. This matters
            // when the player chooses Sleek from the same owner menu alongside Fluffy and BWT.
            __result = FluffyWorkTabGateway.FluffyOwnsWorkTab;
            return false;
        }

        private static bool SleekWorkTypeOrderEnabledPrefix(ref bool __result)
        {
            if (!BetterWorkTabHostsSleek)
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool SleekWorkTypeOrderReconcilePrefix()
        {
            // The mixed host owns the table definition and saved column order.
            // Strict Sleek is allowed to execute its normal reconciliation.
            return !BetterWorkTabHostsSleek;
        }

        private static void SleekWorkTabRenderPostfix(Rect rect, MainTabWindow_Work __instance)
        {
            SleekWorkTabCoexistenceUI.DrawSleekOnlyBottomChrome(rect);
        }

        private static void SleekStrictPawnTablePrefix(ref Vector2 __0)
        {
            if (!SleekWorkTabGateway.SleekOwnsWorkTab ||
                !(Find.MainTabsRoot?.OpenTab?.TabWindow is MainTabWindow_Work))
            {
                return;
            }

            // BWT adds its footer controls after Sleek's renderer finishes. Move
            // only Sleek's PawnTable render origin upward, leaving Sleek's own
            // toolbar and the BWT footer anchored to the normal window rect.
            __0.y -= HeaderButtons.StrictSleekBottomTableShift;
        }

        private static void InvokeStaticNoArguments(string typeName, string methodName)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
                method?.Invoke(null, null);
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Compatibility reconciliation skipped for " + methodName +
                    ": " + exception.Message,
                    DebugFeature.ModSupport);
            }
        }

        private static void EnsureSleekStoreAccessors()
        {
            if (_sleekStoreGetter != null &&
                _sleekGetOverrideMethod != null &&
                _sleekSetOverrideMethod != null)
            {
                return;
            }

            Type storeType = AccessTools.TypeByName(SleekWorkGiverStoreTypeName);
            if (storeType == null)
            {
                return;
            }

            _sleekStoreGetter = AccessTools.Property(storeType, "Get");
            _sleekGetOverrideMethod = AccessTools.Method(
                storeType,
                "GetOverride",
                new[] { typeof(Pawn), typeof(WorkGiverDef) });
            _sleekSetOverrideMethod = AccessTools.Method(
                storeType,
                "SetOverride",
                new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int) });
        }

        private static void EnsureSleekToolbarMethod()
        {
            if (_sleekToolbarResolved)
            {
                return;
            }

            _sleekToolbarResolved = true;
            Type toolbarType = AccessTools.TypeByName(SleekToolbarTypeName);
            _sleekDrawToolbarToggleMethod = AccessTools.Method(
                toolbarType,
                "Prefix",
                Type.EmptyTypes);
            _sleekDrawToolbarExtrasMethod = AccessTools.Method(
                toolbarType,
                "DrawExtras",
                new[] { typeof(UnityEngine.Rect) });
        }

        private static void EnsureSleekSearchAccessors()
        {
            if (_sleekSearchResolved &&
                _sleekSearchActiveGetter != null &&
                _sleekSearchMatchesDelegate != null)
            {
                return;
            }

            Type searchType = AccessTools.TypeByName(SleekWorkSearchTypeName);
            if (searchType == null)
            {
                return;
            }

            _sleekSearchActive = AccessTools.Property(searchType, "Active");
            _sleekSearchText = AccessTools.Property(searchType, "Text");
            _sleekSearchSelectedJob = AccessTools.Property(searchType, "SelectedJob");
            _sleekSearchMatches = AccessTools.Method(
                searchType,
                "Matches",
                new[] { typeof(Pawn) });

            try
            {
                MethodInfo activeGetter = _sleekSearchActive?.GetGetMethod(true);
                MethodInfo textGetter = _sleekSearchText?.GetGetMethod(true);
                MethodInfo selectedJobGetter = _sleekSearchSelectedJob?.GetGetMethod(true);
                if (activeGetter != null)
                {
                    _sleekSearchActiveGetter =
                        (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), activeGetter);
                }
                if (textGetter != null)
                {
                    _sleekSearchTextGetter =
                        (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), textGetter);
                }
                if (selectedJobGetter != null)
                {
                    _sleekSearchSelectedJobGetter =
                        (Func<WorkGiverDef>)Delegate.CreateDelegate(
                            typeof(Func<WorkGiverDef>),
                            selectedJobGetter);
                }
                if (_sleekSearchMatches != null)
                {
                    _sleekSearchMatchesDelegate =
                        (Func<Pawn, bool>)Delegate.CreateDelegate(
                            typeof(Func<Pawn, bool>),
                            _sleekSearchMatches);
                }
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Search accessor setup failed: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                _sleekSearchActiveGetter = null;
                _sleekSearchMatchesDelegate = null;
            }

            _sleekSearchResolved = _sleekSearchActiveGetter != null &&
                                    _sleekSearchMatchesDelegate != null;
        }

        private static void EnsureSleekInlineJobAccessors()
        {
            if (_sleekInlineJobWorkerType != null &&
                _sleekInlineJobGiverFor != null &&
                _sleekInlineJobDefFor != null &&
                _sleekInlineActiveGetter != null &&
                _sleekInlineExpandedGetter != null)
            {
                return;
            }

            _sleekInlineJobWorkerType = AccessTools.TypeByName(SleekInlineJobWorkerTypeName);
            Type columnsType = AccessTools.TypeByName(SleekInlineJobColumnsTypeName);
            if (columnsType == null)
            {
                return;
            }

            _sleekInlineJobGiverFor = AccessTools.Method(
                columnsType,
                "GiverFor",
                new[] { typeof(PawnColumnDef) });
            _sleekInlineJobDefFor = AccessTools.Method(
                columnsType,
                "DefFor",
                new[] { typeof(WorkGiverDef) });
            _sleekInlineJobToggle = AccessTools.Method(
                columnsType,
                "Toggle",
                new[] { typeof(WorkTypeDef), typeof(PawnTable) });
            _sleekInlineJobApplyPendingToggle = AccessTools.Method(
                columnsType,
                "ApplyPendingToggle",
                new[] { typeof(PawnTable) });
            _sleekInlineJobCollapse = AccessTools.Method(
                columnsType,
                "Collapse",
                new[] { typeof(PawnTable) });

            try
            {
                PropertyInfo active = AccessTools.Property(columnsType, "Active");
                PropertyInfo expanded = AccessTools.Property(columnsType, "Expanded");
                MethodInfo activeGetter = active?.GetGetMethod(true);
                MethodInfo expandedGetter = expanded?.GetGetMethod(true);
                if (activeGetter != null)
                {
                    _sleekInlineActiveGetter =
                        (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), activeGetter);
                }
                if (expandedGetter != null)
                {
                    _sleekInlineExpandedGetter =
                        (Func<WorkTypeDef>)Delegate.CreateDelegate(
                            typeof(Func<WorkTypeDef>),
                            expandedGetter);
                }
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Inline job accessor setup failed: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                _sleekInlineActiveGetter = null;
                _sleekInlineExpandedGetter = null;
            }
        }

        private static void EnsureDetected()
        {
            if (_detected == true || _detectionComplete)
            {
                return;
            }

            _detected = false;
            _detectedPackageId = null;
            try
            {
                List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
                for (int i = 0; mods != null && i < mods.Count; i++)
                {
                    ModContentPack mod = mods[i];
                    if (mod != null &&
                        (SleekWorkTabIdentity.IsKnownPackageId(mod.PackageId) ||
                         SleekWorkTabIdentity.IsKnownPackageId(mod.PackageIdPlayerFacing) ||
                         ContainsSleekIdentity(mod.PackageId) ||
                         ContainsSleekIdentity(mod.PackageIdPlayerFacing) ||
                         ContainsSleekIdentity(mod.Name)))
                    {
                        _detected = true;
                        _detectedPackageId = mod.PackageIdPlayerFacing ?? mod.PackageId;
                        _detectionComplete = true;
                        return;
                    }
                }

                // Some harness/staged ModContentPack instances expose the active package through
                // ModLister/ModsConfig before their RunningModsList entry has a populated PackageId.
                // Keep detection independent of load order so the optional bridge cannot silently
                // disable itself in a valid active-mod list.
                if (ModLister.GetActiveModWithIdentifier(
                        SleekWorkTabIdentity.PackageId,
                        ignorePostfix: true) != null ||
                    ModsConfig.IsActive(SleekWorkTabIdentity.PackageId))
                {
                    _detected = true;
                    _detectedPackageId = SleekWorkTabIdentity.PackageId;
                    _detectionComplete = true;
                    return;
                }

                if (AccessTools.TypeByName("SleekWorkPriorities.SleekWorkPrioritiesMod") != null ||
                    HasLoadedSleekAssembly())
                {
                    _detected = true;
                    _detectedPackageId = "type probe";
                    _detectionComplete = true;
                    return;
                }

                // Cache negative results until explicit ReconcileDetection, so IsPresent is not
                // a per-event mod scan while a late-loaded Workshop assembly remains absent.
                _detectionComplete = true;
            }
            catch (Exception exception)
            {
                // Optional detection must never prevent BWT from loading, but keep the failure
                // visible because a staged ModContentPack can expose different identity members.
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Optional detection failed: " + exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                // Cache the safe negative until explicit ReconcileDetection.
                _detectionComplete = true;
            }
        }

        private static bool ContainsSleekIdentity(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf("sleekworkpriorities", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasLoadedSleekAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly?.GetType(
                        "SleekWorkPriorities.SleekWorkPrioritiesMod",
                        throwOnError: false,
                        ignoreCase: false) != null ||
                    ContainsSleekIdentity(assembly?.GetName().Name))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
