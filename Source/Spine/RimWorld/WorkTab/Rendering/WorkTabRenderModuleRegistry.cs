using System;
using System.Collections.Generic;
using Verse;

namespace Spine.RimWorld.WorkTab.Rendering
{
    /// <summary>
    /// Local presentation registry for optional Work-tab render modules.
    /// Registration never changes gameplay or synchronized state.
    /// </summary>
    public static class WorkTabRenderModuleRegistry
    {
        internal const string NativeModuleId = "bwt.native-imgui";
        private static readonly object Sync = new object();
        private static readonly List<RegisteredModule> Modules = new List<RegisteredModule>();
        private static volatile RegisteredModule[] _snapshot = Array.Empty<RegisteredModule>();
        private static volatile string _activeModuleId = NativeModuleId;

        internal static RegisteredModule[] Snapshot => _snapshot;
        public static string ActiveModuleId => _activeModuleId;

        /// <summary>
        /// Registers a renderer and returns its ownership token. Dispose the token to unregister.
        /// </summary>
        public static IDisposable Register(IWorkTabRenderModule module)
        {
            if (!TryDescribe(module, out RegisteredModule descriptor) ||
                string.Equals(descriptor.Id, NativeModuleId, StringComparison.Ordinal))
            {
                return null;
            }

            lock (Sync)
            {
                for (int i = 0; i < Modules.Count; i++)
                {
                    if (string.Equals(Modules[i].Id, descriptor.Id, StringComparison.Ordinal))
                    {
                        return null;
                    }
                }

                Modules.Add(descriptor);
                _snapshot = Modules.ToArray();
                return new Registration(descriptor);
            }
        }

        internal static void ReportActiveModule(string moduleId)
        {
            _activeModuleId = string.IsNullOrEmpty(moduleId) ? NativeModuleId : moduleId;
        }

        private static bool TryDescribe(IWorkTabRenderModule module, out RegisteredModule descriptor)
        {
            descriptor = default;
            if (module == null)
            {
                return false;
            }

            try
            {
                string id = module.Id;
                if (string.IsNullOrEmpty(id))
                {
                    return false;
                }

                descriptor = new RegisteredModule(module, id, module.Priority);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BWT] Work-tab render module registration failed while reading metadata.\n" + ex);
                return false;
            }
        }

        private static void Unregister(RegisteredModule descriptor)
        {
            lock (Sync)
            {
                for (int i = 0; i < Modules.Count; i++)
                {
                    if (!ReferenceEquals(Modules[i].Module, descriptor.Module))
                    {
                        continue;
                    }

                    Modules.RemoveAt(i);
                    _snapshot = Modules.ToArray();
                    return;
                }
            }
        }

        internal readonly struct RegisteredModule
        {
            internal RegisteredModule(IWorkTabRenderModule module, string id, int priority)
            {
                Module = module;
                Id = id;
                Priority = priority;
            }

            internal IWorkTabRenderModule Module { get; }
            internal string Id { get; }
            internal int Priority { get; }
        }

        private sealed class Registration : IDisposable
        {
            private RegisteredModule _descriptor;
            private bool _disposed;

            internal Registration(RegisteredModule descriptor)
            {
                _descriptor = descriptor;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Unregister(_descriptor);
                _descriptor = default;
            }
        }
    }
}
