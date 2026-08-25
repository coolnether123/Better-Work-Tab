using System;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.TimePriority;
using Verse;

namespace Better_Work_Tab.Foundation.GameState
{
    /// <summary>
    /// Per-game composition root. The save-facing GameComponent supplies the
    /// persisted state adapter, but feature runtime ownership lives here.
    /// </summary>
    internal sealed class WorkTabGameRoot
    {
        internal WorkTabGameRoot(
            Game game,
            WorkTabGameState state)
        {
            Game = game;
            State = state;
            ScheduleRuntime = new TimePriorityScheduleRuntime(state.Schedules);
            Application = new WorkTabApplication(game, ScheduleRuntime);
        }

        internal Game Game { get; }
        internal WorkTabGameState State { get; }
        internal TimePriorityScheduleRuntime ScheduleRuntime { get; }
        internal WorkTabApplication Application { get; }
    }

    /// <summary>
    /// Current-game lookup that does not require feature domains to know the
    /// compatibility GameComponent type. Reference equality rejects stale
    /// roots during a game transition.
    /// </summary>
    internal static class WorkTabGameRoots
    {
        private static System.WeakReference<Game> _game;
        private static System.WeakReference<WorkTabGameRoot> _root;

        internal static WorkTabGameRoot Attach<TState>(
            Game game,
            TState state)
            where TState : IWorkTabScheduleStore<TimePriorityScheduleData>,
                IWorkTabColumnOrderState,
                IWorkTabReassignmentState,
                IWorkTabCustomLabelState,
                IWorkTabDividerState,
                IWorkTabWorldSchemaState
        {
            var gameState = new WorkTabGameState(
                state,
                state,
                state,
                state,
                state,
                state);
            var root = new WorkTabGameRoot(game, gameState);
            _game = new System.WeakReference<Game>(game);
            _root = new System.WeakReference<WorkTabGameRoot>(root);
            return root;
        }

        internal static WorkTabGameRoot For(Game game)
        {
            if (game == null || _game == null || _root == null ||
                !_game.TryGetTarget(out Game attachedGame) ||
                !ReferenceEquals(game, attachedGame) ||
                !_root.TryGetTarget(out WorkTabGameRoot root))
            {
                return null;
            }

            return root;
        }
    }
}
