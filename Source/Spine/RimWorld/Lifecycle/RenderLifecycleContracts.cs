using System;

namespace Spine.RimWorld.Api
{
    public enum RenderResetReason
    {
        Manual,
        MapTeardown,
        GameTeardown,
        SettingsChanged,
        DeviceResourcesChanged
    }

    public interface IRenderLifecycle : IDisposable
    {
        void Reset(RenderResetReason reason);
    }
}
