using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Spine.RimWorld.WorkTab.Rendering
{
    internal sealed class WorkTabRenderModuleRouter
    {
        private readonly WorkTabRenderModuleRegistry.RegisteredModule _nativeFallback;
        private readonly HashSet<IWorkTabRenderModule> _quarantined = new HashSet<IWorkTabRenderModule>();
        private WorkTabRenderModuleRegistry.RegisteredModule _active;
        private bool _fallbackPending;

        internal WorkTabRenderModuleRouter(IWorkTabRenderModule nativeFallback)
        {
            if (nativeFallback == null)
            {
                throw new ArgumentNullException(nameof(nativeFallback));
            }

            _nativeFallback = new WorkTabRenderModuleRegistry.RegisteredModule(
                nativeFallback,
                WorkTabRenderModuleRegistry.NativeModuleId,
                int.MinValue);
            _active = _nativeFallback;
            WorkTabRenderModuleRegistry.ReportActiveModule(_nativeFallback.Id);
        }

        internal void Render(in WorkTabFrameContext context)
        {
            if (context.EventType == EventType.Layout)
            {
                ResolveAtSafeBoundary();
            }
            else if (_fallbackPending)
            {
                return;
            }

            WorkTabRenderModuleRegistry.RegisteredModule descriptor = _active;
            try
            {
                descriptor.Module.Render(in context);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(descriptor.Module, _nativeFallback.Module))
                {
                    throw;
                }

                _quarantined.Add(descriptor.Module);
                _fallbackPending = true;
                Log.ErrorOnce(
                    "[BWT] Work-tab render module '" + descriptor.Id +
                    "' failed and was quarantined. Native rendering resumes at the next Layout event.\n" + ex,
                    StringComparer.Ordinal.GetHashCode(descriptor.Id));
            }
        }

        private void ResolveAtSafeBoundary()
        {
            _fallbackPending = false;
            WorkTabRenderModuleRegistry.RegisteredModule selected = _nativeFallback;
            WorkTabRenderModuleRegistry.RegisteredModule[] modules = WorkTabRenderModuleRegistry.Snapshot;
            for (int i = 0; i < modules.Length; i++)
            {
                WorkTabRenderModuleRegistry.RegisteredModule candidate = modules[i];
                if (candidate.Priority <= selected.Priority || !IsAvailable(candidate))
                {
                    continue;
                }

                selected = candidate;
            }

            _active = selected;
            WorkTabRenderModuleRegistry.ReportActiveModule(selected.Id);
        }

        private bool IsAvailable(WorkTabRenderModuleRegistry.RegisteredModule descriptor)
        {
            if (descriptor.Module == null || _quarantined.Contains(descriptor.Module))
            {
                return false;
            }

            try
            {
                return descriptor.Module.IsAvailable;
            }
            catch (Exception ex)
            {
                _quarantined.Add(descriptor.Module);
                Log.ErrorOnce(
                    "[BWT] Work-tab render module availability check failed for '" + descriptor.Id + "'.\n" + ex,
                    StringComparer.Ordinal.GetHashCode(descriptor.Id + ":availability"));
                return false;
            }
        }
    }
}
