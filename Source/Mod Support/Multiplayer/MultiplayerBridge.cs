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
