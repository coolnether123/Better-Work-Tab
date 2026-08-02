using System;
using Spine.Api;
using Better_Work_Tab.Foundation;

namespace Spine.RimWorld.Api
{
    public interface IRenderAtlas<TKey, TEntry> : ICacheDiagnostics, IDisposable
    {
        bool TryGet(TKey key, out TEntry entry);
        void Reset();
    }
}
