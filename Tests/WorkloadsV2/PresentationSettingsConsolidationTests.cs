using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Executable session contracts plus narrow integration contracts for the
    /// Unity-bound presentation setting boundary.
    /// </summary>
    internal static class PresentationSettingsConsolidationTests
    {
        private static readonly Regex FacadeCallPattern = new Regex(
            @"BWTWorkTabEffectiveSettings\.(?<method>GetBool|GetInt|GetColor)\((?<argument>[^\)]*)\)",
            RegexOptions.Compiled);

        public static void Run()
        {
            string root = FindRepositoryRoot();
            string router = Read(root, "Source", "UI", "Settings", "BWTWorkTabContextSettingsRouter.cs");
            string registry = Read(root, "Source", "UI", "Settings", "BWTSettingsRegistry.cs");
            string settingIds = Read(root, "Source", "UI", "Settings", "SettingIDs.cs");
            string gateway = Read(root, "Source", "UI", "Workloads", "WorkloadGateway.cs");
            string previewPort = Read(root, "Source", "UI", "Settings", "WorkTabPresentationPreviewPort.cs");
            string workloadState = Read(root, "Source", "Features", "Workloads", "V2", "WorkloadState.cs");
            string chronos = Read(root, "Source", "Mod Support", "Mods", "Chronos Pointer", "ChronosPointerSupport.cs");
            string fluffy = Read(root, "Source", "Mod Support", "Mods", "Fluffy WorkTab", "FluffyWorkTabGateway.cs");
            string fluffyCoexistence = Read(root, "Source", "Mod Support", "Mods", "Fluffy WorkTab", "FluffyWorkTabCoexistence.cs");
            string legacyImporter = Read(root, "Source", "UI", "Settings", "LegacySettingsJsonImporter.cs");

            Dictionary<string, string> idsByName = ReadSettingIds(settingIds);
            FallbackFreeFacadesHaveCompatibleContributors(
                ReadFacadeUsage(root, idsByName), registry, chronos, fluffy, idsByName);
            PreviewStampTracksStartEditRebaseForkAndSwitch();
            FirstRefreshAndPerFieldFallbackAreBehavioral();
            SetClearAndReleaseAreBehavioral();
            WriterBehaviorIsTransactional();
            PreviewLifecycleAndFirstRefreshAreGuarded(router, gateway, previewPort);
            CachedFacadeUsesPreparedSnapshot(router);
            SteadySettingsDrawerPathIsTokenGated(router, Read(
                root, "Source", "UI", "BetterWorkTabSettingsUI.cs"));
            AllGlobalWriteRoutesAdvanceTheSnapshotToken(
                router, registry, fluffy, fluffyCoexistence, legacyImporter);
            ProjectionDelegatesSetClearReleaseNormalizationToTheModel(router, workloadState);
        }

        private static void PreviewStampTracksStartEditRebaseForkAndSwitch()
        {
            WorkloadTemplate source = TestSupport.Template(
                WorkloadProjectedState.Empty,
                stableId: "presentation-source",
                label: "Presentation Source");
            WorkloadSession opened = WorkloadSession.OpenCaptured(
                source,
                WorkloadProjectedState.Empty,
                WorkloadSession.GetSourceIdentity(source));
            TestAssert.NotNull(opened, "the preview stamp test requires a captured source identity");
            string openedStamp = opened.PreviewStamp;
            TestAssert.True(!string.IsNullOrWhiteSpace(openedStamp),
                "opening a preview must create an exact cache stamp");
            WorkloadSession edited = opened.Edit(draft => draft.SetPresentationSetting(
                "ui.angled", WorkloadScalarValue.FromBoolean(true)));
            TestAssert.False(string.Equals(openedStamp, edited.PreviewStamp, StringComparison.Ordinal),
                "a staged presentation edit must replace the preview cache stamp");

            WorkloadSession switched = WorkloadSession.OpenCaptured(
                source,
                WorkloadProjectedState.Empty,
                WorkloadSession.GetSourceIdentity(source));
            TestAssert.False(string.Equals(openedStamp, switched.PreviewStamp, StringComparison.Ordinal),
                "a replacement preview with the same saved workload must not reuse a stamp");

            WorkloadSessionDecision update = edited.Update();
            TestAssert.True(update.Accepted, "the edited preview must be saveable for rebase coverage");
            WorkloadSession rebased = Rebase(edited, update);
            TestAssert.NotNull(rebased, "a matching update receipt must retain the open preview");
            TestAssert.False(string.Equals(edited.PreviewStamp, rebased.PreviewStamp, StringComparison.Ordinal),
                "a persistence rebase must replace the preview cache stamp");

            WorkloadSessionDecision fork = rebased.Fork("presentation-fork", "Presentation Fork");
            TestAssert.True(fork.Accepted, "the rebased preview must be forkable for source-switch coverage");
            WorkloadSession forked = Rebase(rebased, fork);
            TestAssert.NotNull(forked, "a matching fork receipt must retain the preview");
            TestAssert.False(string.Equals(rebased.PreviewStamp, forked.PreviewStamp, StringComparison.Ordinal),
                "a fork rebase must replace the preview cache stamp");
            TestAssert.Equal("presentation-fork", forked.SourceTemplate.StableId,
                "the forked stamp must be rooted in the active target source");
        }

        private static WorkloadSession Rebase(
            WorkloadSession session,
            WorkloadSessionDecision decision)
        {
            WorkloadTemplate target = decision.ResultTemplate;
            return session.RebaseAfterPersistence(new WorkloadPersistenceReceipt(
                decision.Kind,
                session.SourceTemplate.StableId,
                target.StableId,
                session.SourceIdentity,
                WorkloadSession.GetSourceIdentity(target),
                target,
                10,
                "presentation-before",
                11,
                "presentation-after",
                decision.Kind == WorkloadDecisionKind.Fork
                    ? target.StableId
                    : session.SourceTemplate.StableId));
        }

        private static void FirstRefreshAndPerFieldFallbackAreBehavioral()
        {
            var defaults = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal)
            {
                { "enabled", WorkloadScalarValue.FromBoolean(true) },
                { "rotation", WorkloadScalarValue.FromInteger(15) },
                { "opacity", WorkloadScalarValue.FromInteger(80) }
            };
            IDictionary<string, WorkloadScalarValue> first = WorkloadPresentationValueCache.Capture(
                defaults.Keys,
                null,
                (string id, out WorkloadScalarValue value) =>
                {
                    value = WorkloadScalarValue.Empty;
                    return id == "rotation" &&
                        (value = WorkloadScalarValue.FromInteger(25)).Kind == WorkloadScalarKind.Integer;
                },
                (string id, out WorkloadScalarValue value) => defaults.TryGetValue(id, out value));
            TestAssert.True(first["enabled"].BooleanValue,
                "the first failed active refresh must retain the registry bool default");
            TestAssert.Equal(25, first["rotation"].IntegerValue,
                "a readable field must retain its live value during the first refresh");
            TestAssert.Equal(80, first["opacity"].IntegerValue,
                "the first failed active refresh must retain every other registry default");

            IDictionary<string, WorkloadScalarValue> refreshed = WorkloadPresentationValueCache.Capture(
                defaults.Keys,
                first,
                (string id, out WorkloadScalarValue value) =>
                {
                    value = WorkloadScalarValue.Empty;
                    if (id == "enabled")
                    {
                        value = WorkloadScalarValue.FromBoolean(false);
                        return true;
                    }

                    if (id == "opacity")
                    {
                        throw new InvalidOperationException("optional reader failure");
                    }

                    return false;
                },
                (string id, out WorkloadScalarValue value) => defaults.TryGetValue(id, out value));
            TestAssert.False(refreshed["enabled"].BooleanValue,
                "a successful field refresh must replace only that cached scalar");
            TestAssert.Equal(25, refreshed["rotation"].IntegerValue,
                "a failed field refresh must preserve its prior live scalar");
            TestAssert.Equal(80, refreshed["opacity"].IntegerValue,
                "an exception in one field must not default the other cached scalars");
        }

        private static void SetClearAndReleaseAreBehavioral()
        {
            var draft = new WorkloadDraft(WorkloadProjectedState.Empty);
            draft.SetPresentationSetting("ui.angled", WorkloadScalarValue.FromBoolean(true));
            WorkloadIntent<WorkloadSettingValue> set =
                draft.ProjectedState.PresentationSettingIntents.Single().Intent;
            TestAssert.True(set.HasValue && set.Value.Scalar.BooleanValue,
                "Set must retain a workload-owned presentation value");

            draft.ClearPresentationSetting("ui.angled");
            WorkloadIntent<WorkloadSettingValue> clear =
                draft.ProjectedState.PresentationSettingIntents.Single().Intent;
            TestAssert.True(clear.IsClear,
                "Clear must retain a presentation tombstone instead of releasing ownership");

            draft.ReleasePresentationSetting("ui.angled");
            TestAssert.Equal(0, draft.ProjectedState.PresentationSettingIntents.Count,
                "Release must remove the explicit presentation ownership entry");
        }

        private static void WriterBehaviorIsTransactional()
        {
            PartialApplyCompensatesConfirmedWrites();
            ExactPostWriteMismatchRequiresRecovery();
            ConcurrentRollbackNeverOverwritesEitherKey();
            PersistenceFailureCompensatesAndReportsTruthfully();
        }

        private static void PartialApplyCompensatesConfirmedWrites()
        {
            var store = new FakePresentationSettingsStore(
                new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal)
                {
                    { "a", WorkloadScalarValue.FromInteger(0) },
                    { "b", WorkloadScalarValue.FromInteger(0) }
                })
            {
                FailWriteKey = "b"
            };
            var transaction = new WorkloadPresentationSettingsTransaction(store);
            WorkloadPresentationSettingsSnapshot snapshot = Capture(transaction, "a", "b");
            WorkloadPresentationSettingsMutationReceipt receipt = transaction.TryApply(
                snapshot,
                Scalars(("a", 1), ("b", 1)),
                false);
            TestAssert.False(receipt.Succeeded, "a partial apply must fail");
            TestAssert.True(receipt.CompensationAttempted && receipt.CompensationSucceeded,
                "a partial apply must compensate its confirmed first write");
            TestAssert.Equal(0, store.Value("a").IntegerValue,
                "compensation must restore the confirmed first write");
            TestAssert.False(receipt.RecoveryRequired,
                "successful compensation must not claim manual recovery is required");
        }

        private static void ExactPostWriteMismatchRequiresRecovery()
        {
            var store = new FakePresentationSettingsStore(Scalars(("a", 0)))
            {
                WriteTransform = (key, value) => WorkloadScalarValue.FromInteger(2)
            };
            var transaction = new WorkloadPresentationSettingsTransaction(store);
            WorkloadPresentationSettingsMutationReceipt receipt = transaction.TryApply(
                Capture(transaction, "a"), Scalars(("a", 1)), false);
            TestAssert.False(receipt.Succeeded, "a normalized mismatch must fail the apply");
            TestAssert.True(receipt.CompensationAttempted && !receipt.CompensationSucceeded &&
                receipt.RecoveryRequired,
                "an unverified post-write value must report manual recovery truthfully");
            TestAssert.Equal(2, store.Value("a").IntegerValue,
                "the writer must not overwrite a value it cannot prove it owns");
        }

        private static void ConcurrentRollbackNeverOverwritesEitherKey()
        {
            AssertConcurrentRollbackPreserves("a");
            AssertConcurrentRollbackPreserves("b");
        }

        private static void AssertConcurrentRollbackPreserves(string concurrentKey)
        {
            var store = new FakePresentationSettingsStore(Scalars(("a", 0), ("b", 0)));
            var transaction = new WorkloadPresentationSettingsTransaction(store);
            WorkloadPresentationSettingsSnapshot snapshot = Capture(transaction, "a", "b");
            TestAssert.True(transaction.TryApply(snapshot, Scalars(("a", 1), ("b", 1)), false).Succeeded,
                "the rollback race setup must apply both writer-owned values");
            bool injected = false;
            store.BeforeRead = (target, key) =>
            {
                if (!injected && target.MutationCount == 2 && key == concurrentKey)
                {
                    injected = true;
                    target.WriteOutside(key, WorkloadScalarValue.FromInteger(2));
                }
            };
            WorkloadPresentationSettingsMutationReceipt receipt = transaction.TryRollback(snapshot, false);
            TestAssert.False(receipt.Succeeded && !receipt.RecoveryRequired,
                "a concurrent rollback write must stop recovery rather than succeed");
            TestAssert.Equal(2, store.Value(concurrentKey).IntegerValue,
                "rollback must not overwrite concurrent C for " + concurrentKey);
        }

        private static void PersistenceFailureCompensatesAndReportsTruthfully()
        {
            var store = new FakePresentationSettingsStore(Scalars(("a", 0)))
            {
                PersistFailuresRemaining = 1
            };
            var transaction = new WorkloadPresentationSettingsTransaction(store);
            WorkloadPresentationSettingsMutationReceipt receipt = transaction.TryApply(
                Capture(transaction, "a"), Scalars(("a", 1)), true);
            TestAssert.False(receipt.Succeeded, "a persistence failure must fail the apply");
            TestAssert.True(receipt.PersistenceRequested && receipt.CompensationAttempted &&
                receipt.CompensationSucceeded && !receipt.RecoveryRequired,
                "a recovered persistence failure must expose the compensation outcome");
            TestAssert.Equal(0, store.Value("a").IntegerValue,
                "persistence compensation must restore the exact captured value");
            TestAssert.Equal(2, store.PersistAttempts,
                "the failed apply and successful compensation must both attempt persistence");
        }

        private static WorkloadPresentationSettingsSnapshot Capture(
            WorkloadPresentationSettingsTransaction transaction,
            params string[] keys)
        {
            TestAssert.True(transaction.TryCapture(keys, out WorkloadPresentationSettingsSnapshot snapshot,
                    out string reason),
                "the production transaction must capture the fake store: " + reason);
            return snapshot;
        }

        private static Dictionary<string, WorkloadScalarValue> Scalars(
            params (string Key, int Value)[] values)
        {
            var result = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            for (int index = 0; index < values.Length; index++)
            {
                result.Add(values[index].Key, WorkloadScalarValue.FromInteger(values[index].Value));
            }

            return result;
        }

        private static Dictionary<string, string> ReadSettingIds(string source)
        {
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(
                         source,
                         @"public const string\s+(?<name>[A-Za-z_]\w*)\s*=\s*\""(?<id>[^\""\r\n]+)\"""))
            {
                ids[match.Groups["name"].Value] = match.Groups["id"].Value;
            }

            TestAssert.True(ids.Count > 0, "SettingIDs constants could not be read.");
            return ids;
        }

        private static Dictionary<string, HashSet<string>> ReadFacadeUsage(
            string root,
            IReadOnlyDictionary<string, string> idsByName)
        {
            var usage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            int callCount = 0;
            foreach (string path in Directory.GetFiles(
                         Path.Combine(root, "Source"), "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in FacadeCallPattern.Matches(File.ReadAllText(path)))
                {
                    callCount++;
                    string argument = match.Groups["argument"].Value.Trim();
                    TestAssert.False(argument.Contains(","),
                        "fallback overload remains in " + path + ": " + argument);
                    string id = ResolveSettingId(argument, idsByName, path);
                    if (!usage.TryGetValue(id, out HashSet<string> methods))
                    {
                        methods = new HashSet<string>(StringComparer.Ordinal);
                        usage.Add(id, methods);
                    }

                    methods.Add(match.Groups["method"].Value);
                }
            }

            TestAssert.Equal(117, callCount, "all presentation facade calls must use the fallback-free cache");
            TestAssert.Equal(52, usage.Count, "facade calls must resolve exactly 52 active registry setting IDs");
            return usage;
        }

        private static string ResolveSettingId(
            string argument,
            IReadOnlyDictionary<string, string> idsByName,
            string path)
        {
            if (argument.Length >= 2 && argument[0] == '\"' && argument[argument.Length - 1] == '\"')
            {
                return argument.Substring(1, argument.Length - 2);
            }

            const string prefix = "SettingIDs.";
            string name = argument.StartsWith(prefix, StringComparison.Ordinal)
                ? argument.Substring(prefix.Length)
                : argument;
            if (idsByName.TryGetValue(name, out string id))
            {
                return id;
            }

            throw new InvalidOperationException(
                "facade argument cannot be resolved in " + path + ": " + argument);
        }

        private static void FallbackFreeFacadesHaveCompatibleContributors(
            IReadOnlyDictionary<string, HashSet<string>> usage,
            string registry,
            string chronos,
            string fluffy,
            IReadOnlyDictionary<string, string> idsByName)
        {
            TestAssert.Contains(registry, "ChronosPointerSupport.RegisterSettings();",
                "the Chronos Pointer registry contributor must be initialized");
            TestAssert.Contains(registry, "FluffyWorkTabGateway.RegisterSettings();",
                "the Fluffy WorkTab registry contributor must be initialized");
            string contributors = registry + "\n" + chronos + "\n" + fluffy;
            foreach (KeyValuePair<string, HashSet<string>> entry in usage)
            {
                TestAssert.Equal(1, entry.Value.Count,
                    "one setting must not use incompatible facade kinds: " + entry.Key);
                string builder = entry.Value.Single() == "GetBool"
                    ? "Toggle"
                    : entry.Value.Single() == "GetInt" ? "Int|NumericInt" : "Colour";
                IEnumerable<string> forms = idsByName
                    .Where(pair => string.Equals(pair.Value, entry.Key, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .Concat(new[] { "\"" + entry.Key + "\"" });
                TestAssert.True(forms.Any(form => IsCompatibleContributor(contributors, builder, form)),
                    "no compatible facade contributor was found for " + entry.Key);
            }
        }

        private static bool IsCompatibleContributor(string source, string builder, string form)
        {
            string argument = form.StartsWith("\"", StringComparison.Ordinal)
                ? Regex.Escape(form)
                : "(?:SettingIDs\\.)?" + Regex.Escape(form) + @"\b";
            return Regex.IsMatch(source, @"\.(?:" + builder + @")\s*\(\s*" + argument);
        }

        private static void PreviewLifecycleAndFirstRefreshAreGuarded(
            string router,
            string gateway,
            string previewPort)
        {
            string refresh = MethodBody(router, "internal static void Refresh()");
            foreach (string fragment in new[]
            {
                "IWorkTabPresentationPreviewPort previewPort = _previewPort;",
                "previewPort.TryReadPresentationPreview(",
                "BWTWorkloadPresentationSnapshot.Failed("
            })
            {
                TestAssert.Contains(refresh, fragment,
                    "first active refresh must fail closed through " + fragment);
            }

            foreach (string forbidden in new[]
            {
                "WorkloadPreviewController",
                "ProjectedWorkTabEffectiveStateProvider",
                "WorkloadSession",
                "WorkloadGateway",
                "SynchronizeAfterInput"
            })
            {
                TestAssert.False(router.Contains(forbidden),
                    "contextual settings must not own workload implementation detail " + forbidden);
            }
            TestAssert.Contains(previewPort, "interface IWorkTabPresentationPreviewPort",
                "settings must depend on one neutral optional presentation-preview boundary");
            TestAssert.Contains(gateway, "IWorkTabPresentationPreviewPort",
                "the workload preview must implement the settings boundary");
            TestAssert.Contains(gateway, "TryMutatePresentation(",
                "preview mutation and synchronization must remain behind the workload boundary");

            string observe = MethodBody(router, "internal static void ObservePreviewIdentity(");
            TestAssert.Contains(observe, "_observedPreviewIdentity",
                "preview observation must use the exact opaque identity and a close sentinel");
            TestAssert.Contains(observe, "InvalidateWorkTabPresentationCore(",
                "a new stamp must invalidate the cached presentation snapshot");

            string open = MethodBody(gateway, "private void OpenSession(");
            string synchronize = MethodBody(gateway, "internal bool SynchronizeAfterInput()");
            string acceptDraft = MethodBody(gateway, "private void AcceptDraftReplacement(");
            string rebuild = MethodBody(gateway, "private void RebuildProjection(");
            string close = MethodBody(gateway, "private void ClearLocalSession()");
            TestAssert.Contains(open, "RebuildProjection(_session.ProjectedState)",
                "preview start must cross the observed projection boundary");
            TestAssert.Contains(synchronize, "AcceptDraftReplacement(",
                "synchronized staged edits must cross the shared accepted-draft boundary");
            TestAssert.Contains(acceptDraft, "_session = accepted",
                "the shared accepted-draft boundary must install the accepted session");
            TestAssert.Contains(acceptDraft, "ObservePreviewIdentity(\n                _session?.PreviewStamp)",
                "the shared accepted-draft boundary must replace the observed session stamp");
            TestAssert.True(
                acceptDraft.IndexOf("_session = accepted", StringComparison.Ordinal) <
                acceptDraft.IndexOf("ObservePreviewIdentity(", StringComparison.Ordinal),
                "the accepted session must be installed before its preview stamp is observed");
            TestAssert.Contains(rebuild, "ObservePreviewIdentity(\n                _session?.PreviewStamp)",
                "preview switches and rebases must replace the observed session stamp");
            TestAssert.Contains(close, "ObservePreviewIdentity(null)",
                "preview close must discard the observed session stamp");
        }

        private static void CachedFacadeUsesPreparedSnapshot(string router)
        {
            string facade = Slice(router,
                "internal static class BWTWorkTabEffectiveSettings",
                "internal sealed class BWTWorkloadSettingDefinitionState");
            foreach (string fragment in new[]
            {
                "PresentationSnapshot.GetBool(settingId)",
                "PresentationSnapshot.GetInt(settingId)",
                "PresentationSnapshot.GetColor(settingId)"
            })
            {
                TestAssert.Contains(facade, fragment, "cached facade is missing " + fragment);
            }
            TestAssert.False(facade.Contains("GetField(") || facade.Contains("ColorUtility.") || facade.Contains("new "),
                "the per-cell facade must not reflect, parse, or allocate");
            TestAssert.Contains(router, "PreparedPresentationSettings",
                "field and scalar discovery must be cached during registry preparation");
        }

        private static void SteadySettingsDrawerPathIsTokenGated(
            string router,
            string settingsUi)
        {
            string prepareDrawer = MethodBody(settingsUi, "PrepareDrawer =");
            TestAssert.Contains(prepareDrawer, "EnsureFresh();",
                "the complete settings drawer path must use the cheap semantic refresh gate");
            TestAssert.False(prepareDrawer.Contains(".Refresh();"),
                "the complete settings drawer path must not rebuild every Layout/Repaint");

            string ensureFresh = MethodBody(router, "internal static void EnsureFresh()");
            TestAssert.Contains(ensureFresh, "_snapshotSettingsRevision != _globalSettingsRevision",
                "the drawer refresh gate must use only the semantic session/revision token");
            TestAssert.False(ensureFresh.Contains("Capture") ||
                ensureFresh.Contains("Field.GetValue") ||
                ensureFresh.Contains("new Dictionary"),
                "the steady drawer refresh gate must not capture fields or rebuild dictionaries");

            string draw = MethodBody(router, "private static bool DrawStageableSettingRow(");
            TestAssert.Contains(draw, "state.OpenColorPicker",
                "visible color rows must reuse their prepared picker callback");
            TestAssert.False(draw.Contains("=> OpenColorPicker"),
                "visible color rows must not allocate a capturing picker callback per draw");

            string defaults = Slice(router,
                "internal sealed class BWTWorkloadSettingDefinitionState",
                "internal sealed class BWTWorkloadPresentationSettingsStore");
            TestAssert.Contains(defaults, "DefaultScalar",
                "reset and non-default checks must consume prepared scalar defaults");
            TestAssert.False(draw.Contains("ToColorScalar"),
                "color defaults must not be formatted during Layout/Repaint");
            TestAssert.Contains(MethodBody(router, "private static void InstallStageableDefinition("),
                "WorkloadScalarValue defaultValue = state.DefaultScalar;",
                "visible reset controls must reuse their prepared scalar default");
            TestAssert.False(router.Contains("class BWTWorkloadSettingsApplyWriter"),
                "the behaviorless one-caller writer subclass must stay inlined");
            TestAssert.Contains(MethodBody(router, "internal static WorkloadPresentationSettingsTransaction CreateApplyWriter()"),
                "new BWTWorkloadPresentationSettingsStore()",
                "the writer factory must compose the store-neutral transaction directly");
        }

        private static void AllGlobalWriteRoutesAdvanceTheSnapshotToken(
            string router,
            string registry,
            string fluffy,
            string fluffyCoexistence,
            string legacyImporter)
        {
            TestAssert.Contains(MethodBody(router, "internal static long NotifyGlobalSettingsChanged("),
                "AdvanceGlobalSettingsRevision(",
                "ordinary global writes must use the snapshot token route");
            TestAssert.Contains(MethodBody(registry, "private static void PrepareDefinition("),
                "NotifyGlobalSettingsChanged(def.Id)",
                "registry OnChanged writes must advance the snapshot token");
            TestAssert.Contains(fluffy, "settings.Write();\n                BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();",
                "Fluffy compatibility writes must advance the snapshot token");
            TestAssert.Contains(fluffyCoexistence, "settings.Write();\n            BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();",
                "Fluffy coexistence writes must advance the snapshot token");
            TestAssert.Contains(MethodBody(legacyImporter,
                    "internal static bool TryImport("),
                "settings.Write();\n            BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();",
                "legacy imports must invalidate the cached presentation values");
        }

        private static void ProjectionDelegatesSetClearReleaseNormalizationToTheModel(
            string router,
            string workloadState)
        {
            string copy = MethodBody(router,
                "CopyPresentationIntents(\n                IReadOnlyList<PresentationIntentEntry> entries)");
            TestAssert.Contains(copy, "entries[i]",
                "the snapshot must consume presentation-only typed intent entries from its port");
            TestAssert.False(copy.Contains("PresentationSettings"),
                "the snapshot must not rebuild a second legacy presentation map");
            TestAssert.Contains(copy, "!entry.Intent.IsNoOpinion",
                "the snapshot must retain effective Set and Clear intent semantics");
            TestAssert.Contains(router, "intent.IsClear",
                "the effective snapshot must preserve explicit Clear intent");
            foreach (string fragment in new[]
            {
                "NormalizePresentationSettingIntents(",
                "SynchronizePresentationSettings(",
                "SetPresentationSettingIntent(",
                "WorkloadIntent<WorkloadSettingValue>.Clear",
                "ReleasePresentationSetting(string key)"
            })
            {
                TestAssert.Contains(workloadState, fragment,
                    "the model must own Set/Clear/Release normalization through " + fragment);
                }
        }

        private sealed class FakePresentationSettingsStore : IWorkloadPresentationSettingsStore
        {
            private readonly object _identity = new object();
            private readonly Dictionary<string, WorkloadScalarValue> _values;

            internal FakePresentationSettingsStore(
                IDictionary<string, WorkloadScalarValue> values)
            {
                _values = new Dictionary<string, WorkloadScalarValue>(
                    values,
                    StringComparer.Ordinal);
            }

            internal string FailWriteKey { get; set; }
            internal int PersistFailuresRemaining { get; set; }
            internal int PersistAttempts { get; private set; }
            internal int MutationCount { get; private set; }
            internal Action<FakePresentationSettingsStore, string> BeforeRead { get; set; }
            internal Func<string, WorkloadScalarValue, WorkloadScalarValue> WriteTransform { get; set; }

            public object Identity => _identity;
            public long Revision { get; private set; }

            public bool TryRead(string settingId, out WorkloadScalarValue value)
            {
                BeforeRead?.Invoke(this, settingId);
                return _values.TryGetValue(settingId, out value);
            }

            public bool TryCanonicalize(
                string settingId,
                WorkloadScalarValue requested,
                out WorkloadScalarValue canonical,
                out string reason)
            {
                canonical = requested;
                reason = string.Empty;
                return _values.ContainsKey(settingId);
            }

            public bool TryWrite(
                string settingId,
                WorkloadScalarValue value,
                out string reason)
            {
                reason = string.Empty;
                if (string.Equals(settingId, FailWriteKey, StringComparison.Ordinal))
                {
                    reason = "injected write failure";
                    return false;
                }

                _values[settingId] = WriteTransform == null
                    ? value
                    : WriteTransform(settingId, value);
                return true;
            }

            public long BeginOwnedMutation(IEnumerable<string> settingIds)
            {
                MutationCount++;
                Revision++;
                return Revision;
            }

            public IDisposable BeginWriteLease() => NoopLease.Instance;

            public bool TryPersist(out string reason)
            {
                PersistAttempts++;
                if (PersistFailuresRemaining > 0)
                {
                    PersistFailuresRemaining--;
                    reason = "injected persistence failure";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            internal WorkloadScalarValue Value(string key) => _values[key];

            internal void WriteOutside(string key, WorkloadScalarValue value)
            {
                _values[key] = value;
                Revision++;
            }
        }

        private sealed class NoopLease : IDisposable
        {
            internal static readonly NoopLease Instance = new NoopLease();

            public void Dispose()
            {
            }
        }

        private static string FindRepositoryRoot()
        {
            return TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Settings", "BWTWorkTabContextSettingsRouter.cs"),
                "presentation settings source");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = parts.Aggregate(root, Path.Combine);
            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string Slice(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            int end = start < 0 ? -1 : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            TestAssert.True(start >= 0 && end > start, "expected source boundary is missing: " + startMarker);
            return source.Substring(start, end - start);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            int open = start < 0 ? -1 : source.IndexOf('{', start + signature.Length);
            TestAssert.True(open >= 0, "expected method boundary is missing: " + signature);
            int depth = 0;
            for (int index = open; index < source.Length; index++)
            {
                if (source[index] == '{') depth++;
                else if (source[index] == '}' && --depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }

            throw new InvalidOperationException("unterminated method boundary: " + signature);
        }
    }
}
