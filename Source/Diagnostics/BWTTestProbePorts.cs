using System;
using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Diagnostics
{
    /// <summary>
    /// Narrow production surface used by the optional TestProbe mod. The
    /// release assembly keeps the ports inert; the probe supplies its own
    /// implementation through its separate assembly and runtime hooks.
    /// </summary>
    internal static class AgentHarnessUtility
    {
        internal static Func<bool> IsEnabledProvider;
        internal static Func<string, string> GetPathProvider;

        internal static bool IsEnabled() => IsEnabledProvider?.Invoke() ?? false;

        internal static string GetPath(string fileName)
        {
            if (GetPathProvider == null)
            {
                throw new InvalidOperationException("The Better Work Tab TestProbe is not loaded.");
            }
            return GetPathProvider(fileName);
        }
    }

    internal static class BWTTutorialAgentHarness
    {
        internal static Action<Rect, IWorkTabLayoutController> RequestHandler;
        internal static void ProcessRequest(Rect inRect, object layout)
        {
            RequestHandler?.Invoke(inRect, layout as IWorkTabLayoutController);
        }
    }

    internal static class RuleBuilder2AgentHarness
    {
        internal static Action<IWorkTabLayoutController> SelectionRequestHandler;
        internal static void ProcessSelectionRequest(object layout)
        {
            SelectionRequestHandler?.Invoke(layout as IWorkTabLayoutController);
        }
    }

    internal static class WorkTabGeometryDiagnostics
    {
        internal static Action<string, Event> PriorityInputTraceHandler;
        internal static Action<Pawn, Rect> PawnLabelIconRectHandler;
        internal static Action<Rect> SubWorkSeparatorHandler;
        internal static Action<IWorkTabLayoutController> HeaderLayoutHandler;
        internal static void RecordPriorityInputTrace(params object[] values)
        {
            if (values.Length >= 2) PriorityInputTraceHandler?.Invoke(values[0] as string, values[1] as Event);
        }
        internal static void RecordPawnLabelIconRect(params object[] values)
        {
            if (values.Length >= 2) PawnLabelIconRectHandler?.Invoke(values[0] as Pawn, values[1] is Rect ? (Rect)values[1] : default(Rect));
        }
        internal static void RecordSubWorkSeparator(params object[] values)
        {
            if (values.Length > 0 && values[0] is Rect) SubWorkSeparatorHandler?.Invoke((Rect)values[0]);
        }
        internal static void DumpHeaderLayoutIfRequested(params object[] values)
        {
            if (values.Length > 0) HeaderLayoutHandler?.Invoke(values[0] as IWorkTabLayoutController);
        }
    }

    internal static class SubWorkTransitionPerfDiagnostics
    {
        internal static Action<string> BeginTransitionHandler;
        internal static Action RecordWorkTabRepaintHandler;
        internal static Action CountPawnTableRecachePostfixHandler;
        internal static Action CountPawnTableSyncWriteHandler;
        internal static Action CountAngledHeaderCacheKeyChangeHandler;
        internal static Action CountAngledHeaderCacheRebuildHandler;
        internal static Action CountHeaderCalcSizeHandler;
        internal static Action CountHeaderTextBuildHandler;
        internal static void BeginTransition(string name) => BeginTransitionHandler?.Invoke(name);
        internal static void RecordWorkTabRepaint() => RecordWorkTabRepaintHandler?.Invoke();
        internal static void CountPawnTableRecachePostfix() => CountPawnTableRecachePostfixHandler?.Invoke();
        internal static void CountPawnTableSyncWrite() => CountPawnTableSyncWriteHandler?.Invoke();
        internal static void CountAngledHeaderCacheKeyChange() => CountAngledHeaderCacheKeyChangeHandler?.Invoke();
        internal static void CountAngledHeaderCacheRebuild() => CountAngledHeaderCacheRebuildHandler?.Invoke();
        internal static void CountHeaderCalcSize() => CountHeaderCalcSizeHandler?.Invoke();
        internal static void CountHeaderTextBuild() => CountHeaderTextBuildHandler?.Invoke();
    }

    internal static class WorkTabProfilingState
    {
        internal static Func<double> OpenSecondsProvider;
        internal static Action<bool> NotifyOpenHandler;
        internal static float OpenSeconds => (float)(OpenSecondsProvider?.Invoke() ?? 0d);
        internal static void NotifyOpen(bool open) => NotifyOpenHandler?.Invoke(open);
    }
}
