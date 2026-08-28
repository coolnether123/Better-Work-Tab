using System.Collections.Generic;
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
        private static System.WeakReference<Game> _game;
        private static System.WeakReference<IWorkloadWorldState> _state;

        internal static void Attach(Game game, IWorkloadWorldState state)
        {
            _game = game == null ? null : new System.WeakReference<Game>(game);
            _state = state == null
                ? null
                : new System.WeakReference<IWorkloadWorldState>(state);
        }

        internal static IWorkloadWorldState For(Game game)
        {
            if (game == null || _game == null || _state == null ||
                !_game.TryGetTarget(out Game attachedGame) ||
                !ReferenceEquals(game, attachedGame) ||
                !_state.TryGetTarget(out IWorkloadWorldState state))
            {
                return null;
            }

            return state;
        }

        internal static IWorkloadWorldState Current => For(Verse.Current.Game);
    }
}
