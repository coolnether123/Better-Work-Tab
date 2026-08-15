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
            Preflight = ReadOnly(preflight);
            Installation = ReadOnly(installation);
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

        private static IReadOnlyList<BwtRaisedPriorityPatchAttempt> ReadOnly(
            IEnumerable<BwtRaisedPriorityPatchAttempt> values)
        {
            return new ReadOnlyCollection<BwtRaisedPriorityPatchAttempt>(
                (values ?? Enumerable.Empty<BwtRaisedPriorityPatchAttempt>()).ToList());
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

        private readonly IReadOnlyList<BwtRaisedPriorityPatchDefinition> _definitions;
        // InstallProduction creates a short-lived coordinator at mod startup. Keep this guard
        // process-wide so a second caller cannot run PatchAll again after a partial attempt.
        private static bool _patchAllAttempted;

        internal BwtRaisedPriorityFeatureInstaller(
            IEnumerable<BwtRaisedPriorityPatchDefinition> definitions)
        {
            if (definitions == null)
                throw new ArgumentNullException("definitions");

            _definitions = new ReadOnlyCollection<BwtRaisedPriorityPatchDefinition>(
                definitions.Where(value => value != null).ToList());
            if (_definitions.Count != 6)
                throw new ArgumentException("The raised-priority feature requires exactly six transpilers.", "definitions");
        }

        internal static bool IsFeatureActive { get; private set; }
        internal static BwtRaisedPriorityInstallReport LastReport { get; private set; }

        internal static BwtRaisedPriorityInstallReport InstallProduction(Harmony harmony)
        {
            return new BwtRaisedPriorityFeatureInstaller(CreateProductionDefinitions())
                .Install(harmony, runPatchAll: true);
        }

        internal BwtRaisedPriorityInstallReport Install(Harmony harmony, bool runPatchAll)
        {
            IsFeatureActive = false;
            var diagnostics = new List<string>();
            if (harmony == null)
            {
                diagnostics.Add("No Harmony instance was supplied.");
                return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                    null, null, false, false, false, diagnostics);
            }

            List<BwtRaisedPriorityPatchAttempt> preflight = new List<BwtRaisedPriorityPatchAttempt>();
            int ownedCount = CountOwnedTranspilers(harmony);
            if (ownedCount == _definitions.Count)
            {
                IsFeatureActive = true;
                return CreateReport(BwtRaisedPriorityInstallState.AlreadyInstalled,
                    preflight, null, false, false, true, diagnostics);
            }

            if (ownedCount != 0 && !TryRemoveOwnedTranspilers(harmony, diagnostics))
            {
                return CreateReport(BwtRaisedPriorityInstallState.RollbackFailed,
                    preflight, null, false, true, false, diagnostics);
            }

            preflight = Preflight(harmony);
            if (preflight.Any(value => !value.Succeeded))
            {
                diagnostics.Add("At least one required raised-priority transpiler failed preflight.");
                return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                    preflight, null, false, false, true, diagnostics);
            }

            if (runPatchAll && _patchAllAttempted)
            {
                diagnostics.Add("PatchAll was already attempted; refusing a second installation pass.");
                return CreateReport(BwtRaisedPriorityInstallState.PreflightRejected,
                    preflight, null, false, false, true, diagnostics);
            }

            BwtRaisedPriorityInstallState installingState = BwtRaisedPriorityInstallState.Installing;
            List<BwtRaisedPriorityPatchAttempt> installation;
            using (BwtRaisedPriorityInstallationSession session =
                BwtRaisedPriorityInstallationSession.Begin())
            {
                _patchAllAttempted |= runPatchAll;
                try
                {
                    if (runPatchAll)
                        harmony.PatchAll(typeof(BwtRaisedPriorityFeatureInstaller).Assembly);
                    else
                        InstallDefinitionsIndividually(harmony);
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
                CountOwnedTranspilers(harmony) == _definitions.Count;
            if (installed)
            {
                IsFeatureActive = true;
                return CreateReport(BwtRaisedPriorityInstallState.Installed,
                    preflight, installation, runPatchAll, false, true, diagnostics);
            }

            diagnostics.Add("The six required transpilers were not installed as one complete feature.");
            bool rollbackSucceeded = TryRemoveOwnedTranspilers(harmony, diagnostics);
            if (rollbackSucceeded)
                installingState = BwtRaisedPriorityInstallState.RolledBack;
            else
                installingState = BwtRaisedPriorityInstallState.RollbackFailed;

            IsFeatureActive = false;
            return CreateReport(installingState, preflight, installation, runPatchAll,
                true, rollbackSucceeded, diagnostics);
        }

        private List<BwtRaisedPriorityPatchAttempt> Preflight(Harmony harmony)
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
                        definition.Target, out generator, 0);
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

        private int CountOwnedTranspilers(Harmony harmony)
        {
            int count = 0;
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (OwnsTranspiler(definition))
                    count++;
            }
            return count;
        }

        private bool OwnsTranspiler(BwtRaisedPriorityPatchDefinition definition)
        {
            if (definition.Target == null || definition.Transpiler == null)
                return false;

            HarmonyLib.Patches patches = HarmonyLib.Harmony.GetPatchInfo(definition.Target);
            return patches != null && patches.Transpilers != null &&
                patches.Transpilers.Any(patch => patch != null &&
                    String.Equals(patch.owner, HarmonyOwnerId, StringComparison.Ordinal) &&
                    patch.PatchMethod == definition.Transpiler);
        }

        private bool TryRemoveOwnedTranspilers(Harmony harmony, IList<string> diagnostics)
        {
            bool success = true;
            foreach (BwtRaisedPriorityPatchDefinition definition in _definitions)
            {
                if (!OwnsTranspiler(definition))
                    continue;

                try
                {
                    // Unpatch by the exact BWT method only after checking the owner. This leaves
                    // every foreign prefix, postfix, finalizer, and transpiler untouched.
                    harmony.Unpatch(definition.Target, definition.Transpiler);
                    if (OwnsTranspiler(definition))
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
            LastReport = report;
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
                    AccessTools.Method(typeof(Better_Work_Tab.Patches.Patch_PawnColumnWorker_Label_DoCell), "Transpiler"))
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
