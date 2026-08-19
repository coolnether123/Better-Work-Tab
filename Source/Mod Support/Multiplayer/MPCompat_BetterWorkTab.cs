using Multiplayer.API;
using Verse;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2.Runtime;

namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    [StaticConstructorOnStartup]
    internal static class MPCompat_BetterWorkTab
    {
        static MPCompat_BetterWorkTab()
        {
            if (!MP.enabled)
                return;

            // Workload MP support is an all-or-nothing capability.  The
            // registration routine refuses API versions without both a real
            // sender binding and host-only enforcement, so 1.3 cannot leave a
            // partially registered workload protocol behind.
            WorkloadTransactionMultiplayer.TryRegisterProtocol();
            Workload2Backend.RegisterMultiplayerProtocolCallbacks();
            MP.RegisterAll();
        }
    }

    internal sealed class GameComponent_BWTWorkloadTransactionPump : GameComponent
    {
        public GameComponent_BWTWorkloadTransactionPump(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (!MultiplayerBridge.Active || Find.TickManager == null)
                return;

            // Registration is retryable when the game enters multiplayer
            // after mod startup, but remains permanently unavailable when the
            // loaded API cannot enforce host-only synchronized methods.
            WorkloadTransactionMultiplayer.TryRegisterProtocol();
            WorkloadTransactionMultiplayer.CheckSessionContext(Find.TickManager.TicksGame);
            WorkloadTransactionMultiplayer.Protocol.CheckTimeout(Find.TickManager.TicksGame);
        }
    }
}
