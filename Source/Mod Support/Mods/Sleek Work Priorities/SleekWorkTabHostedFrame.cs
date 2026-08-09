using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI;

namespace Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities
{
    /// <summary>
    /// Opens only Sleek's per-frame visual state around BWT's renderer in mixed mode.
    /// BWT remains the host and owns window geometry, column/row layout, dividers, headers,
    /// and input. Sleek's existing worker patches can then render its priority cells against
    /// the BWT table without running Sleek's whole-window prefix.
    /// </summary>
    internal static class SleekWorkTabHostedFrame
    {
        private const string RenderPatchTypeName = "SleekWorkPriorities.Patch_WorkTab_Render";
        private const string FrameTypeName = "SleekWorkPriorities.WorkTabFrame";
        private const string BoardUiTypeName = "SleekWorkPriorities.WorkBoardUI";
        private const string SettingsTypeName = "SleekWorkPriorities.SleekWorkSettings";
        private const string GridHoverTypeName = "SleekWorkPriorities.GridHover";
        private const string WorkHoverTypeName = "SleekWorkPriorities.WorkHover";
        private const string FocusStateTypeName = "SleekWorkPriorities.FocusState";
        private const string FocusCompanionTypeName = "SleekWorkPriorities.WorkFocusCompanionWindow";
        private const string WorkTabResizingTypeName = "SleekWorkPriorities.WorkTabResizing";
        private const string SleekScrollbarsTypeName = "SleekWorkPriorities.SleekScrollbars";
        private const string InlineJobColumnsTypeName = "SleekWorkPriorities.InlineJobColumns";

        private static bool _resolved;
        private static Type _renderPatchType;
        private static FieldInfo _workTableField;
        private static PropertyInfo _gridOriginProperty;
        private static PropertyInfo _gridViewportProperty;
        private static PropertyInfo _headerOriginProperty;
        private static MethodInfo _frameBegin;
        private static MethodInfo _frameEnd;
        private static MethodInfo _beginWorkRender;
        private static MethodInfo _endWorkRender;
        private static MethodInfo _gridHoverTick;
        private static MethodInfo _workHoverTick;
        private static FieldInfo _settingsInstance;
        private static PropertyInfo _backgroundProperty;
        private static PropertyInfo _focusDockVisible;
        private static PropertyInfo _companionBlocksWorkInput;
        private static PropertyInfo _blocksWorkInputAtPointer;
        private static FieldInfo _sleekGridField;
        private static MethodInfo _ensureFocusCompanion;
        private static MethodInfo _closeFocusCompanion;
        private static MethodInfo _sleekScrollbarsApply;
        private static MethodInfo _sleekScrollbarsRestore;
        private static MethodInfo _inlineApplyPendingToggle;
        private static MethodInfo _inlineReconcile;

        private static readonly Color FallbackBackground =
            new Color(0.08627451f, 5f / 51f, 0.11372549f, 1f);

        internal static IDisposable Enter(MainTabWindow_BetterWork host, Rect rect, PawnTable table)
        {
            if (!SleekWorkTabGateway.BetterWorkTabHostsSleek || host == null || table == null)
            {
                return NullScope.Instance;
            }

            ResolveMembers();
            if (!CanEnter)
            {
                return NullScope.Instance;
            }

            bool frameStarted = false;
            bool renderStarted = false;
            bool scrollbarsApplied = false;
            bool companionBlocks = false;
            bool guiSuppressed = false;
            bool guiEnabledBeforeSuppression = GUI.enabled;
            try
            {
                _workTableField.SetValue(null, table);
                SetProperty(_gridOriginProperty, rect.x);
                SetProperty(_headerOriginProperty, rect.x);
                SetProperty(_gridViewportProperty, Mathf.Max(1f, rect.width));

                _frameBegin.Invoke(null, null);
                frameStarted = true;

                object settings = _settingsInstance.GetValue(null);
                if (settings == null)
                {
                    throw new InvalidOperationException("Sleek settings are unavailable");
                }

                // Sleek's whole-window prefix normally maintains its synthetic
                // inline-job columns. Mixed mode bypasses that prefix, so keep
                // this small lifecycle part active before BWT snapshots the
                // table. BWT still owns the resulting table order and geometry.
                if (SubWorkDrilldownState.IsActive && SubWorkDrilldownState.ActiveWorkType != null)
                {
                    SleekWorkTabGateway.SyncMixedSubWorkExpansion(
                        table,
                        SubWorkDrilldownState.ActiveWorkType);
                }
                else
                {
                    SleekWorkTabGateway.RestoreMixedSubWorkExpansion(table);
                }
                _inlineApplyPendingToggle?.Invoke(null, new object[] { table });
                _inlineReconcile?.Invoke(null, new object[] { table });

                _beginWorkRender.Invoke(null, new[] { settings });
                renderStarted = true;
                _gridHoverTick.Invoke(null, null);
                _workHoverTick.Invoke(null, null);

                companionBlocks = GetCompanionBlocksWorkInput();
                if (_companionBlocksWorkInput != null)
                {
                    SetProperty(_companionBlocksWorkInput, companionBlocks);
                }

                guiSuppressed = companionBlocks && Event.current.type != EventType.Repaint;
                if (guiSuppressed)
                {
                    GUI.enabled = false;
                }

                bool sleekGrid = _sleekGridField?.GetValue(settings) is bool enabled && enabled;
                if (sleekGrid && Event.current.type != EventType.Layout && _sleekScrollbarsApply != null)
                {
                    _sleekScrollbarsApply.Invoke(null, null);
                    scrollbarsApplied = true;
                }

                return new ActiveScope(
                    renderStarted,
                    frameStarted,
                    scrollbarsApplied,
                    companionBlocks,
                    guiSuppressed,
                    guiEnabledBeforeSuppression);
            }
            catch (Exception exception)
            {
                if (scrollbarsApplied)
                {
                    InvokeSafely(_sleekScrollbarsRestore);
                }
                if (guiSuppressed)
                {
                    GUI.enabled = guiEnabledBeforeSuppression;
                }
                if (companionBlocks)
                {
                    SetPropertySafely(_companionBlocksWorkInput, false);
                }
                if (renderStarted)
                {
                    InvokeSafely(_endWorkRender);
                }
                if (frameStarted)
                {
                    InvokeSafely(_frameEnd);
                }

                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed hosted frame could not start: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                return NullScope.Instance;
            }
        }

        /// <summary>
        /// Sleek normally creates its focus-card companion from the postfix of its
        /// own MainTabWindow_Work renderer. Mixed mode intentionally bypasses that
        /// whole-window renderer, so keep the companion lifecycle attached to the
        /// BWT host instead. The companion remains Sleek-owned UI; BWT only supplies
        /// the host window instance it is positioned beside.
        /// </summary>
        internal static void SyncFocusCompanion(MainTabWindow_BetterWork host)
        {
            if (!SleekWorkTabGateway.BetterWorkTabHostsSleek || host == null)
            {
                return;
            }

            ResolveMembers();
            if (_focusDockVisible == null || _ensureFocusCompanion == null || _closeFocusCompanion == null)
            {
                return;
            }

            try
            {
                bool dockVisible = _focusDockVisible.GetValue(null, null) is bool value && value;
                if (dockVisible)
                {
                    _ensureFocusCompanion.Invoke(null, new object[] { host });
                }
                else
                {
                    _closeFocusCompanion.Invoke(null, null);
                }
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed focus companion synchronization skipped: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
            }
        }

        internal static void DrawHeaderBackdrop(Rect rect)
        {
            if (Event.current.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            ResolveMembers();
            Color background = FallbackBackground;
            try
            {
                if (_backgroundProperty?.GetValue(null, null) is Color sleekBackground)
                {
                    background = sleekBackground;
                }
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed header backdrop fallback: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
            }

            Widgets.DrawBoxSolid(rect, background);
        }

        private static bool CanEnter =>
            _workTableField != null &&
            _gridOriginProperty != null &&
            _gridViewportProperty != null &&
            _headerOriginProperty != null &&
            _frameBegin != null &&
            _frameEnd != null &&
            _beginWorkRender != null &&
            _endWorkRender != null &&
            _gridHoverTick != null &&
            _workHoverTick != null &&
            _settingsInstance != null;

        private static bool GetCompanionBlocksWorkInput()
        {
            try
            {
                return _blocksWorkInputAtPointer?.GetValue(null, null) is bool value && value;
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Companion input state read failed: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                return false;
            }
        }

        private static void ResolveMembers()
        {
            if (_resolved)
            {
                return;
            }

            _renderPatchType = AccessTools.TypeByName(RenderPatchTypeName);
            Type frameType = AccessTools.TypeByName(FrameTypeName);
            Type boardUiType = AccessTools.TypeByName(BoardUiTypeName);
            Type settingsType = AccessTools.TypeByName(SettingsTypeName);
            Type gridHoverType = AccessTools.TypeByName(GridHoverTypeName);
            Type workHoverType = AccessTools.TypeByName(WorkHoverTypeName);
            Type focusStateType = AccessTools.TypeByName(FocusStateTypeName);
            Type focusCompanionType = AccessTools.TypeByName(FocusCompanionTypeName);
            Type resizingType = AccessTools.TypeByName(WorkTabResizingTypeName);
            Type scrollbarsType = AccessTools.TypeByName(SleekScrollbarsTypeName);
            if (_renderPatchType == null || frameType == null || boardUiType == null ||
                settingsType == null || gridHoverType == null || workHoverType == null)
            {
                return;
            }

            _workTableField = AccessTools.Field(_renderPatchType, "WorkTable");
            _gridOriginProperty = AccessTools.Property(_renderPatchType, "GridOriginX");
            _gridViewportProperty = AccessTools.Property(_renderPatchType, "GridViewportWidth");
            _headerOriginProperty = AccessTools.Property(_renderPatchType, "HeaderDrawOriginX");
            _frameBegin = AccessTools.Method(frameType, "Begin");
            _frameEnd = AccessTools.Method(frameType, "End");
            _beginWorkRender = AccessTools.Method(boardUiType, "BeginWorkRender");
            _endWorkRender = AccessTools.Method(boardUiType, "EndWorkRender");
            _gridHoverTick = AccessTools.Method(gridHoverType, "Tick");
            _workHoverTick = AccessTools.Method(workHoverType, "FrameTick");
            _settingsInstance = AccessTools.Field(settingsType, "Instance");
            _sleekGridField = AccessTools.Field(settingsType, "sleekGrid");
            _backgroundProperty = AccessTools.Property(boardUiType, "Background");
            if (resizingType != null)
            {
                _companionBlocksWorkInput = AccessTools.Property(
                    resizingType,
                    "CompanionBlocksWorkInput");
            }

            if (focusCompanionType != null)
            {
                _blocksWorkInputAtPointer = AccessTools.Property(
                    focusCompanionType,
                    "BlocksWorkInputAtPointer");
            }

            if (scrollbarsType != null)
            {
                _sleekScrollbarsApply = AccessTools.Method(scrollbarsType, "Apply");
                _sleekScrollbarsRestore = AccessTools.Method(scrollbarsType, "Restore");
            }

            Type inlineJobColumnsType = AccessTools.TypeByName(InlineJobColumnsTypeName);
            if (inlineJobColumnsType != null)
            {
                _inlineApplyPendingToggle = AccessTools.Method(
                    inlineJobColumnsType,
                    "ApplyPendingToggle",
                    new[] { typeof(PawnTable) });
                _inlineReconcile = AccessTools.Method(
                    inlineJobColumnsType,
                    "Reconcile",
                    new[] { typeof(PawnTable) });
            }
            if (focusStateType != null)
            {
                _focusDockVisible = AccessTools.Property(focusStateType, "DockVisible");
            }

            if (focusCompanionType != null)
            {
                _ensureFocusCompanion = AccessTools.Method(
                    focusCompanionType,
                    "Ensure",
                    new[] { typeof(MainTabWindow_Work) });
                _closeFocusCompanion = AccessTools.Method(focusCompanionType, "CloseIfOpen");
            }

            // Retry resolution if a late-loaded Sleek build exposed only part
            // of its optional surface during mod construction.
            _resolved = CanEnter;
        }

        private static void SetProperty(PropertyInfo property, object value)
        {
            MethodInfo setter = property?.GetSetMethod(true);
            if (setter == null)
            {
                throw new MissingMethodException(property?.Name, "set");
            }

            setter.Invoke(null, new[] { value });
        }

        private static void InvokeSafely(MethodInfo method)
        {
            try
            {
                method?.Invoke(null, null);
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Mixed hosted frame cleanup failed: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
            }
        }

        private static void SetPropertySafely(PropertyInfo property, object value)
        {
            try
            {
                if (property != null)
                {
                    SetProperty(property, value);
                }
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Hosted frame property cleanup failed: " +
                    exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
            }
        }

        private sealed class ActiveScope : IDisposable
        {
            private bool _renderStarted;
            private bool _frameStarted;
            private bool _scrollbarsApplied;
            private bool _companionBlocks;
            private bool _guiSuppressed;
            private readonly bool _guiEnabledBeforeSuppression;

            internal ActiveScope(
                bool renderStarted,
                bool frameStarted,
                bool scrollbarsApplied,
                bool companionBlocks,
                bool guiSuppressed,
                bool guiEnabledBeforeSuppression)
            {
                _renderStarted = renderStarted;
                _frameStarted = frameStarted;
                _scrollbarsApplied = scrollbarsApplied;
                _companionBlocks = companionBlocks;
                _guiSuppressed = guiSuppressed;
                _guiEnabledBeforeSuppression = guiEnabledBeforeSuppression;
            }

            public void Dispose()
            {
                if (_scrollbarsApplied)
                {
                    _scrollbarsApplied = false;
                    InvokeSafely(_sleekScrollbarsRestore);
                }
                if (_guiSuppressed)
                {
                    _guiSuppressed = false;
                    GUI.enabled = _guiEnabledBeforeSuppression;
                }
                if (_companionBlocks)
                {
                    _companionBlocks = false;
                    SetPropertySafely(_companionBlocksWorkInput, false);
                }
                if (_renderStarted)
                {
                    _renderStarted = false;
                    InvokeSafely(_endWorkRender);
                }
                if (_frameStarted)
                {
                    _frameStarted = false;
                    InvokeSafely(_frameEnd);
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            internal static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
