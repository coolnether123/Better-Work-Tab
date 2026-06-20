using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ModAPI.Core
{
    /// <summary>
    /// RimWorld-backed logging shim for the imported Harmony utilities.
    /// Preserves the old MMLog surface so the port stays localized.
    /// </summary>
    public static class MMLog
    {
        private const string Prefix = "[Spine.Harmony] ";
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void Write(string message)
        {
            Log.Message(Prefix + (message ?? string.Empty));
        }

        public static void WriteInfo(string message)
        {
            Log.Message(Prefix + (message ?? string.Empty));
        }

        public static void WriteWarning(string message)
        {
            Log.Warning(Prefix + (message ?? string.Empty));
        }

        public static void WriteError(string message)
        {
            Log.Error(Prefix + (message ?? string.Empty));
        }

        public static void WriteDebug(string message)
        {
            if (!ShouldLogDebug())
            {
                return;
            }

            Log.Message(Prefix + "[Debug] " + (message ?? string.Empty));
        }

        public static void WarnOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key))
            {
                WriteWarning(message);
                return;
            }

            lock (WarnedKeys)
            {
                if (!WarnedKeys.Add(key))
                {
                    return;
                }
            }

            Log.Warning(Prefix + (message ?? string.Empty));
        }

        private static bool ShouldLogDebug()
        {
            if (Prefs.DevMode)
            {
                return true;
            }

            try
            {
                return Better_Work_Tab.BetterWorkTabMod.Settings?.enableDebugLogging ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Compatibility preference surface for the imported transpiler framework.
    /// Defaults are conservative and can later be wired to real settings.
    /// </summary>
    public static class ModPrefs
    {
        public static bool DebugTranspilers => Prefs.DevMode || TryGetBwtDebug();
        public static bool TranspilerSafeMode => true;
        public static bool TranspilerForcePreserveInstructionCount => false;
        public static bool TranspilerFailFastCritical => false;
        public static bool TranspilerCooperativeStrictBuild => false;
        public static bool TranspilerQuarantineOnFailure => false;
        public static bool TranspilerLogValidationWarnings => false;
        public static bool TranspilerWarnOnVirtualCallMismatch => false;
        public static bool TranspilerWarnOnExceptionHandlerMethods => false;

        private static bool TryGetBwtDebug()
        {
            return TryGetBwtSetting(s => s.enableDebugLogging, false);
        }

        private static T TryGetBwtSetting<T>(Func<Better_Work_Tab.BetterWorkTabSettings, T> selector, T fallback)
        {
            if (selector == null)
            {
                return fallback;
            }

            try
            {
                var settings = Better_Work_Tab.BetterWorkTabMod.Settings;
                return settings != null ? selector(settings) : fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}

namespace ModAPI.Reflection
{
    /// <summary>
    /// Minimal reflection helper shim used by HarmonyHelper.SafeInvoke.
    /// </summary>
    public static class Safe
    {
        public static bool TryCall<T>(object instance, string methodName, out T result, params object[] args)
        {
            result = default(T);

            if (instance == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var method = instance.GetType().GetMethod(methodName, flags);
                if (method == null)
                {
                    return false;
                }

                object value = method.Invoke(instance, args);
                if (value is T typed)
                {
                    result = typed;
                    return true;
                }

                if (value == null)
                {
                    result = default(T);
                    return true;
                }

                result = (T)value;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

namespace ModAPI.Spine
{
    public interface IPluginSettings
    {
        bool GetBool(string key, bool fallback);
    }

    public interface IPluginLog
    {
        void Info(string message);
    }

    public interface IPluginContext
    {
        IPluginSettings Settings { get; }
        IPluginLog Log { get; }
    }

    /// <summary>
    /// Placeholder save contract for imported helpers. RimWorld does not use the Sheltered save manager.
    /// </summary>
    public interface ISaveable
    {
    }

    /// <summary>
    /// Minimal compatibility stub so imported generic helpers still compile.
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        public static SaveManager instance => null;

        public bool HasBeenLoaded(ISaveable saveable)
        {
            return true;
        }
    }
}
