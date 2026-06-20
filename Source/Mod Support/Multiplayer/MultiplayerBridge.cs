#if !v1_2 && !v1_1 && !v1_0
using Multiplayer.API;

namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    internal static class MultiplayerBridge
    {
        internal static bool ApiReady => MP.enabled;
        internal static bool Active => MP.enabled && MP.IsInMultiplayer;
        internal static bool Host => Active && MP.IsHosting;
        internal static string LocalPlayerName => MP.enabled ? MP.PlayerName : "SinglePlayer";
    }
}
#else
namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    internal static class MultiplayerBridge
    {
        internal static bool ApiReady => false;
        internal static bool Active => false;
        internal static bool Host => false;
        internal static string LocalPlayerName => "SinglePlayer";
    }
}
#endif
