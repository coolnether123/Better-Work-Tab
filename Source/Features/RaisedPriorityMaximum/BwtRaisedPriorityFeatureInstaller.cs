using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Better_Work_Tab.Transpilers.BwtExactProfile;
using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal enum BwtRaisedPriorityInstallState
    {
        NotAttempted,
        PreflightRejected,
        Installing,
        Installed,
        AlreadyInstalled,
        RolledBack,
        RollbackFailed
    }

    internal static class BwtRaisedPriorityPatchIds
    {
        internal const string TipForPawnWorker = "TipForPawnWorker";
        internal const string DrawWorkBoxFor = "DrawWorkBoxFor";
        internal const string HeaderClicked = "HeaderClicked";
        internal const string SetPriority = "SetPriority";
        internal const string DoHeader = "DoHeader";
        internal const string LabelDoCell = "Label.DoCell";
    }

    internal sealed class BwtRaisedPriorityPatchDefinition
    {
        internal BwtRaisedPriorityPatchDefinition(string id, MethodBase target, MethodInfo transpiler)
        {
            Id = id;
            Target = target;
            Transpiler = transpiler;
        }

        internal string Id { get; private set; }
        internal MethodBase Target { get; private set; }
        internal MethodInfo Transpiler { get; private set; }
    }

    internal sealed class BwtRaisedPriorityPatchAttempt
    {
        internal BwtRaisedPriorityPatchAttempt(
            string id, PatchOutcome outcome, IEnumerable<PatchDiagnostic> diagnostics, string detail)
        {
            Id = id;
            Outcome = outcome;
            Diagnostics = new ReadOnlyCollection<PatchDiagnostic>(
                (diagnostics ?? Enumerable.Empty<PatchDiagnostic>()).Where(value => value != null).ToList());
            Detail = detail ?? String.Empty;
        }

        internal string Id { get; private set; }
        internal PatchOutcome Outcome { get; private set; }
        internal IReadOnlyList<PatchDiagnostic> Diagnostics { get; private set; }
        internal string Detail { get; private set; }

        internal bool Succeeded
        {
            get { return Outcome == PatchOutcome.Applied || Outcome == PatchOutcome.AlreadyApplied; }
        }

        internal static BwtRaisedPriorityPatchAttempt FromResult(
            BwtRaisedPriorityPatchDefinition definition, BwtPatchResult result)
        {
            if (result == null)
            {
                return new BwtRaisedPriorityPatchAttempt(
                    definition.Id, PatchOutcome.Failed, null,
                    "The transpiler returned no BWT exact-profile result.");
            }

            return new BwtRaisedPriorityPatchAttempt(
                definition.Id, result.Outcome, result.Diagnostics, result.TargetMethodId);
        }

        internal static BwtRaisedPriorityPatchAttempt Failed(
            BwtRaisedPriorityPatchDefinition definition, string detail)
        {
            return new BwtRaisedPriorityPatchAttempt(
                definition.Id, PatchOutcome.Failed, null, detail);
        }

        public override string ToString()
        {
            string codes = String.Join(",", Diagnostics.Select(value => value.Code.ToString()).ToArray());
            return Id + "=" + Outcome + (codes.Length == 0 ? String.Empty : "[" + codes + "]") +
                (String.IsNullOrEmpty(Detail) ? String.Empty : ":" + Detail);
        }
    }

    internal sealed class BwtRaisedPriorityInstallReport
    {
        internal BwtRaisedPriorityInstallReport(
            BwtRaisedPriorityInstallState state,
            IEnumerable<BwtRaisedPriorityPatchAttempt> preflight,
            IEnumerable<BwtRaisedPriorityPatchAttempt> installation,
            bool patchAllInvoked, bool rollbackAttempted, bool rollbackSucceeded,
            IEnumerable<string> diagnostics)
        {
            State = state;
            Preflight = new ReadOnlyCollection<BwtRaisedPriorityPatchAttempt>(
                (preflight ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>()).ToList());
            Installation = new ReadOnlyCollection<BwtRaisedPriorityPatchAttempt>(
                (installation ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>()).ToList());
            PatchAllInvoked = patchAllInvoked;
            RollbackAttempted = rollbackAttempted;
            RollbackSucceeded = rollbackSucceeded;
            Diagnostics = new ReadOnlyCollection<string>(
                (diagnostics ?? Enumerable.Empty<string>()).Where(value => !String.IsNullOrEmpty(value)).ToList());
        }

        internal BwtRaisedPriorityInstallState State { get; private set; }
        internal IReadOnlyList<BwtRaisedPriorityPatchAttempt> Preflight { get; private set; }
        internal IReadOnlyList<BwtRaisedPriorityPatchAttempt> Installation { get; private set; }
        internal IReadOnlyList<string> Diagnostics { get; private set; }
        internal bool PatchAllInvoked { get; private set; }
        internal bool RollbackAttempted { get; private set; }
        internal bool RollbackSucceeded { get; private set; }
        internal bool FeatureActive
        {
            get { return State == BwtRaisedPriorityInstallState.Installed ||
                State == BwtRaisedPriorityInstallState.AlreadyInstalled; }
        }

        internal string Format()
        {
            string preflight = String.Join(";", Preflight.Select(value => value.ToString()).ToArray());
            string installation = String.Join(";", Installation.Select(value => value.ToString()).ToArray());
            string diagnostics = String.Join(";", Diagnostics.ToArray());
            return "state=" + State + ";preflight=" + preflight + ";installation=" + installation +
                ";patchAll=" + PatchAllInvoked + ";rollback=" + RollbackAttempted + "/" +
                RollbackSucceeded + (diagnostics.Length == 0 ? String.Empty : ";diagnostics=" + diagnostics);
        }

    }

    /// <summary>
    /// Captures exact-profile outcomes while Harmony is installing BWT's raised-priority patches.
    /// Harmony invokes transpilers synchronously, so a thread-local session is sufficient and does
    /// not make the runtime patch surface depend on a global mutable result slot.
    /// </summary>
    internal sealed class BwtRaisedPriorityInstallationSession : IDisposable
    {
        [ThreadStatic]
        private static BwtRaisedPriorityInstallationSession _current;

        private readonly BwtRaisedPriorityInstallationSession _previous;
        private readonly Dictionary<string, BwtPatchResult> _results =
            new Dictionary<string, BwtPatchResult>(StringComparer.Ordinal);
        private bool _disposed;

        private BwtRaisedPriorityInstallationSession()
        {
            _previous = _current;
            _current = this;
        }

        internal static BwtRaisedPriorityInstallationSession Begin()
        {
            return new BwtRaisedPriorityInstallationSession();
        }

        internal static void Record(string id, BwtPatchResult result)
        {
            if (_current == null || String.IsNullOrEmpty(id))
                return;

            _current._results[id] = result;
        }

        internal bool TryGet(string id, out BwtPatchResult result)
        {
            return _results.TryGetValue(id, out result);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _current = _previous;
        }
    }

    /// <summary>
    /// Owns the only multi-method installation boundary for the BWT raised-priority feature.
    /// It deliberately stays BWT-local and uses the exact-profile engine through the six existing
    /// Harmony entrypoints. The generalized Fluent Transpiler project is not involved.
    /// </summary>
    internal sealed class BwtRaisedPriorityFeatureInstaller
    {
        internal const string HarmonyOwnerId = "Coolnether123.betterworktab";
        internal const string RaisedPriorityHarmonyOwnerId =
            "Coolnether123.betterworktab.raised-priority";
        internal const string RaisedPriorityPatchCategory =
            "BetterWorkTab.RaisedPriority";

        private const int AllTranspilers = Int32.MaxValue;
        private static readonly object InstallationSync = new object();

        private readonly IReadOnlyList<BwtRaisedPriorityPatchDefinition> _definitions;
        private readonly string _raisedPriorityOwnerId;
        private static volatile bool _isFeatureActive;
        private static BwtRaisedPriorityInstallReport _lastReport;

        internal BwtRaisedPriorityFeatureInstaller(
            IEnumerable<BwtRaisedPriorityPatchDefinition> definitions,
            string raisedPriorityOwnerId = RaisedPriorityHarmonyOwnerId)
        {
            if (definitions == null)
                throw new ArgumentNullException("definitions");

            if (String.IsNullOrEmpty(raisedPriorityOwnerId))
                throw new ArgumentException("A raised-priority Harmony owner is required.",
                    "raisedPriorityOwnerId");

            _definitions = new ReadOnlyCollection<BwtRaisedPriorityPatchDefinition>(
                definitions.Where(value => value != null).ToList());
            if (_definitions.Count != 6)
                throw new ArgumentException("The raised-priority feature requires exactly six transpilers.", "definitions");
            _raisedPriorityOwnerId = raisedPriorityOwnerId;
        }

        internal static bool IsFeatureActive
        {
            get { return _isFeatureActive; }
        }

        internal static BwtRaisedPriorityInstallReport LastReport
        {
            get
            {
                lock (InstallationSync)
                    return _lastReport;
            }
        }

        internal static BwtRaisedPriorityInstallReport InstallProduction(Harmony harmony)
        {
            return new BwtRaisedPriorityFeatureInstaller(CreateProductionDefinitions())
                .Install(harmony, runPatchAll: true);
        }

        internal BwtRaisedPriorityInstallReport Install(Harmony harmony, bool runPatchAll)
        {
            lock (InstallationSync)
            {
                _isFeatureActive = false;
                var diagnostics = new List<string>();
                bool patchAllInvoked = runPatchAll;
                if (harmony == null)
                {
                    diagnostics.Add("No Harmony instance was supplied.");
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        null, null, false, false, false, diagnostics);
                }

                Harmony raisedPriorityHarmony = new Harmony(_raisedPriorityOwnerId);
                bool cleanupAttempted = false;
                bool cleanupSucceeded = true;

                if (runPatchAll && !TryPatchUncategorized(harmony, diagnostics))
                {
                    if (HasAnyOwnedTranspilers())
                    {
                        cleanupAttempted = true;
                        cleanupSucceeded = TryRemoveOwnedTranspilers(
                            raisedPriorityHarmony, diagnostics);
                    }

                    return CreateReport(
                        cleanupSucceeded
                            ? BwtRaisedPriorityInstallState.RolledBack
                            : BwtRaisedPriorityInstallState.RollbackFailed,
                        null, null, patchAllInvoked, cleanupAttempted, cleanupSucceeded, diagnostics);
                }

                int ownedCount = CountOwnedTranspilers();
                if (ownedCount == _definitions.Count)
                {
                    List<BwtRaisedPriorityPatchAttempt> revalidation = Preflight();
                    if (revalidation.Count == _definitions.Count &&
                        revalidation.All(value => value.Succeeded) &&
                        revalidation.All(value => value.Outcome == PatchOutcome.AlreadyApplied))
                    {
                        _isFeatureActive = true;
                        return CreateReport(BwtRaisedPriorityInstallState.AlreadyInstalled,
                            revalidation, null, patchAllInvoked, false, true, diagnostics);
                    }

                    diagnostics.Add(
                        "The existing raised-priority installation failed full-composition revalidation; " +
                        "it will be removed and evaluated again.");
                    cleanupAttempted = true;
                    cleanupSucceeded = TryRemoveOwnedTranspilers(
                        raisedPriorityHarmony, diagnostics);
                    if (!cleanupSucceeded)
                    {
                        return CreateReport(BwtRaisedPriorityInstallState.RollbackFailed,
                            revalidation, null, patchAllInvoked, true, false, diagnostics);
                    }
                }
                else if (ownedCount != 0 || HasAnyOwnedTranspilers())
                {
                    cleanupAttempted = true;
                    cleanupSucceeded = TryRemoveOwnedTranspilers(
                        raisedPriorityHarmony, diagnostics);
                    if (!cleanupSucceeded)
                    {
                        return CreateReport(BwtRaisedPriorityInstallState.RollbackFailed,
                            null, null, patchAllInvoked, true, false, diagnostics);
                    }
                }

                List<BwtRaisedPriorityPatchAttempt> preflight = Preflight();
                if (preflight.Count != _definitions.Count ||
                    preflight.Any(value => !value.Succeeded))
                {
                    diagnostics.Add("At least one required raised-priority transpiler failed preflight " +
                        "against the fully composed IL.");
                    return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                        preflight, null, patchAllInvoked,
                        cleanupAttempted, cleanupSucceeded, diagnostics);
                }

                List<BwtRaisedPriorityPatchAttempt> installation;
                using (BwtRaisedPriorityInstallationSession session =
                    BwtRaisedPriorityInstallationSession.Begin())
                {
                    try
                    {
                        InstallDefinitionsIndividually(raisedPriorityHarmony);
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add("Harmony installation threw " + exception.GetType().FullName + ": " +
                            Trim(exception.Message));
                    }

                    installation = CollectInstallationAttempts(session);
                }

                bool installed = installation.Count == _definitions.Count &&
                    installation.All(value => value.Succeeded) &&
                    CountOwnedTranspilers() == _definitions.Count;
                if (installed)
                {
                    _isFeatureActive = true;
                    return CreateReport(BwtRaisedPriorityInstallState.Installed,
                        preflight, installation, patchAllInvoked, false, true, diagnostics);
                }

                diagnostics.Add("The six required transpilers were not installed as one complete feature.");
                bool rollbackSucceeded = TryRemoveOwnedTranspilers(
                    raisedPriorityHarmony, diagnostics);
                BwtRaisedPriorityInstallState state = rollbackSucceeded
                    ? BwtRaisedPriorityInstallState.RolledBack
                    : BwtRaisedPriorityInstallState.RollbackFailed;

                _isFeatureActive = false;
                return CreateReport(state, preflight, installation, patchAllInvoked,
                    true, rollbackSucceeded, diagnostics);
            }
        }

        private bool TryPatchUncategorized(Harmony harmony, IList<string> diagnostics)
        {
            try
            {
                harmony.PatchAllUncategorized(
                    typeof(BwtRaisedPriorityFeatureInstaller).Assembly);
                return true;
            }
            catch (Exception exception)
            {
                diagnostics.Add("Harmony uncategorized installation threw " +
                    exception.GetType().FullName + ": " + Trim(exception.Message));
                return false;
            }
        }

        private List<BwtRaisedPriorityPatchAttempt> Preflight()
        {
            var results = new List<BwtRaisedPriorityPatchAttempt>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                try
                {
                    if (definition.Target == null || definition.Transpiler == null)
                    {
                        results.Add(BwtRaisedPriorityPatchAttempt.Failed(
                            definition, "The target or transpiler method could not be resolved."));
                        continue;
                    }

                    ILGenerator generator;
                    List<CodeInstruction> current = PatchProcessor.GetCurrentInstructions(
                        definition.Target, out generator, AllTranspilers);
                    if (generator == null)
                    {
                        generator = new DynamicMethod(
                            "bwt-raised-priority-preflight", typeof(void), Type.EmptyTypes)
                            .GetILGenerator();
                    }
                    BwtPatchResult result = InvokeTranspiler(definition, current, generator);
                    results.Add(BwtRaisedPriorityPatchAttempt.FromResult(definition, result));
                }
                catch (Exception exception)
                {
                    results.Add(BwtRaisedPriorityPatchAttempt.Failed(
                        definition, exception.GetType().FullName + ": " + Trim(exception.Message)));
                }
            }
            return results;
        }

        private void InstallDefinitionsIndividually(Harmony harmony)
        {
            // Harmony recomputes the live, fully composed instruction stream for each patch.
            // Reusing the preflight list would hide a foreign change between the two phases;
            // the installation session records that live invocation and rolls back on failure.
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (definition.Target == null || definition.Transpiler == null)
                    throw new InvalidOperationException("A raised-priority patch definition is incomplete.");

                harmony.CreateProcessor(definition.Target)
                    .AddTranspiler(new HarmonyMethod(definition.Transpiler))
                    .Patch();
            }
        }

        private List<BwtRaisedPriorityPatchAttempt> CollectInstallationAttempts(
            BwtRaisedPriorityInstallationSession session)
        {
            var results = new List<BwtRaisedPriorityPatchAttempt>();
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                BwtPatchResult result;
                if (session.TryGet(definition.Id, out result))
                    results.Add(BwtRaisedPriorityPatchAttempt.FromResult(definition, result));
                else
                    results.Add(BwtRaisedPriorityPatchAttempt.Failed(
                        definition, "Harmony did not report an outcome for this required transpiler."));
            }
            return results;
        }

        private int CountOwnedTranspilers()
        {
            int count = 0;
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (OwnsTranspiler(definition))
                    count++;
            }
            return count;
        }

        private bool HasAnyOwnedTranspilers()
        {
            return _definitions.Any(definition =>
                definition.Target != null && HasAnyOwnedTranspiler(definition.Target));
        }

        private bool OwnsTranspiler(BwtRaisedPriorityPatchDefinition definition)
        {
            if (definition.Target == null || definition.Transpiler == null)
                return false;

            HarmonyLib.Patches patches = HarmonyLib.Harmony.GetPatchInfo(definition.Target);
            return patches != null && patches.Transpilers != null &&
                patches.Transpilers.Any(patch => patch != null &&
                    String.Equals(patch.owner, _raisedPriorityOwnerId, StringComparison.Ordinal) &&
                    patch.PatchMethod == definition.Transpiler);
        }

        private bool HasAnyOwnedTranspiler(MethodBase target)
        {
            HarmonyLib.Patches patches = HarmonyLib.Harmony.GetPatchInfo(target);
            return patches != null && patches.Transpilers != null &&
                patches.Transpilers.Any(patch => patch != null &&
                    String.Equals(patch.owner, _raisedPriorityOwnerId, StringComparison.Ordinal));
        }

        private bool TryRemoveOwnedTranspilers(Harmony harmony, IList<string> diagnostics)
        {
            bool success = true;
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (definition.Target == null || !HasAnyOwnedTranspiler(definition.Target))
                    continue;

                try
                {
                    // The exact-method overload is unsafe when a foreign owner registered the
                    // same MethodInfo. Restrict cleanup by owner and patch type instead.
                    harmony.Unpatch(definition.Target, HarmonyPatchType.Transpiler,
                        _raisedPriorityOwnerId);
                    if (HasAnyOwnedTranspiler(definition.Target))
                    {
                        success = false;
                        diagnostics.Add("BWT transpiler cleanup remained active for " + definition.Id + ".");
                    }
                }
                catch (Exception exception)
                {
                    success = false;
                    diagnostics.Add("BWT transpiler cleanup failed for " + definition.Id + ": " +
                        exception.GetType().FullName + ": " + Trim(exception.Message));
                }
            }
            return success;
        }

        private BwtRaisedPriorityInstallReport CreateReport(
            BwtRaisedPriorityInstallState state,
            IEnumerable<BwtRaisedPriorityPatchAttempt> preflight,
            IEnumerable<BwtRaisedPriorityPatchAttempt> installation,
            bool patchAllInvoked, bool rollbackAttempted, bool rollbackSucceeded,
            IEnumerable<string> diagnostics)
        {
            BwtRaisedPriorityInstallReport report = new BwtRaisedPriorityInstallReport(
                state,
                preflight ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>(),
                installation ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>(),
                patchAllInvoked, rollbackAttempted, rollbackSucceeded, diagnostics);
            _lastReport = report;
            return report;
        }

        private static BwtPatchResult InvokeTranspiler(
            BwtRaisedPriorityPatchDefinition definition,
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            ParameterInfo[] parameters = definition.Transpiler.GetParameters();
            var arguments = new object[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                Type parameterType = parameters[index].ParameterType;
                if (index == 0)
                    arguments[index] = instructions;
                else if (parameterType == typeof(ILGenerator))
                    arguments[index] = generator;
                else if (typeof(MethodBase).IsAssignableFrom(parameterType))
                    arguments[index] = definition.Target;
                else
                    throw new InvalidOperationException(
                        "Unsupported BWT transpiler parameter " + parameterType.FullName + ".");
            }

            object value = definition.Transpiler.Invoke(null, arguments);
            return value as BwtPatchResult;
        }

        private static IReadOnlyList<BwtRaisedPriorityPatchDefinition> CreateProductionDefinitions()
        {
            return new ReadOnlyCollection<BwtRaisedPriorityPatchDefinition>(new[]
            {
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.TipForPawnWorker,
                    AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker)),
                    AccessTools.Method(typeof(Patch_WidgetsWork_TipForPawnWorker), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.DrawWorkBoxFor,
                    AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor)),
                    AccessTools.Method(typeof(Patch_WidgetsWork_DrawWorkBoxFor), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.HeaderClicked,
                    AccessTools.Method(typeof(PawnColumnWorker_WorkPriority),
                        nameof(PawnColumnWorker_WorkPriority.HeaderClicked)),
                    AccessTools.Method(typeof(Patch_PawnColumnWorker_WorkPriority_HeaderClicked), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.SetPriority,
                    AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority)),
                    AccessTools.Method(typeof(Patch_Pawn_WorkSettings_SetPriority), "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.DoHeader,
                    AccessTools.Method(typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader),
                        new[] { typeof(Rect), typeof(PawnTable) }),
                    AccessTools.Method(typeof(Better_Work_Tab.UI.Headers.Patch_PawnColumnWorker_DoHeader_DisableHighlight),
                        "Transpiler")),
                new BwtRaisedPriorityPatchDefinition(
                    BwtRaisedPriorityPatchIds.LabelDoCell,
                    AccessTools.Method(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell)),
                    AccessTools.Method(
                        typeof(Better_Work_Tab.Patches.Patch_PawnColumnWorker_Label_DoCell_Transpiler),
                        "Transpiler"))
            });
        }

        private static string Trim(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;
            return value.Length <= 240 ? value : value.Substring(0, 240);
        }
    }
}
