using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    /// <summary>
    /// Workload-owned runtime view of the legacy and V2 persistence carried by
    /// the historical save component. Runtime workload services depend on this
    /// port, leaving the component as the Scribe-compatible implementation.
    /// </summary>
    internal interface IWorkloadWorldState
    {
        IReadOnlyList<Worklist> SavedWorklists { get; }
        Worklist CurrentWorklist { get; }
        void SelectWorklist(Worklist worklist);
        void CreateWorklist(string label);
        void DeleteWorklist(Worklist worklist);
        void RenameWorklist(Worklist worklist, string newLabel);
        WorkloadV2PersistenceEnvelope EnsureV2Persistence();
        // Controlled Update/Fork writes can prove the post-write document
        // before publishing. Other callers keep the full diagnostic scan.
        void NotifyV2Changed(bool diagnosticsAlreadyVerified = false);
    }

    internal static class WorkloadWorldStates
    {
        private static readonly ConditionalWeakTable<Game, IWorkloadWorldState> States =
            new ConditionalWeakTable<Game, IWorkloadWorldState>();

        internal static void Attach(Game game, IWorkloadWorldState state)
        {
            ArgumentNullException.ThrowIfNull(game);
            if (state == null)
            {
                States.Remove(game);
            }
            else
            {
                States.AddOrUpdate(game, state);
            }
        }

        internal static IWorkloadWorldState For(Game game)
        {
            return game != null && States.TryGetValue(game, out IWorkloadWorldState state)
                ? state
                : null;
        }

        internal static IWorkloadWorldState Current => For(Verse.Current.Game);
    }
}
