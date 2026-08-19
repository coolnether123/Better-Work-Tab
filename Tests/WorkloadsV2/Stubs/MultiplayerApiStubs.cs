namespace Multiplayer.API
{
    public sealed class SyncMethodAttribute : System.Attribute
    {
    }

    public delegate void SyncWorkerAction<T>(SyncWorker sync, ref T value);

    public static class MP
    {
        public static bool enabled;
        public static bool IsInMultiplayer;
        public static bool IsHosting;
        public static bool IsExecutingSyncCommand;
        public static string PlayerName;
        public static int RegisteredSyncWorkerCount;

        public static void RegisterAll() { }
        public static void RegisterSyncWorker<T>(SyncWorkerAction<T> worker)
        {
            RegisteredSyncWorkerCount++;
        }
    }

    public sealed class SyncWorker
    {
        public bool isWriting;
        public void Bind<T>(ref T value) { }
        public void Write<T>(T value) { }
        public T Read<T>() { return default(T); }
    }
}
