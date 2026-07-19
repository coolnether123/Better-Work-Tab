using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.WorkManager
{
    /// <summary>
    /// Hosts Work Manager's own Work-tab control row inside BWT. Calling the
    /// upstream drawer keeps Work Manager authoritative for its state, actions,
    /// textures, translations, and tooltips.
    /// </summary>
    internal static class WorkManagerCompatibility
    {
        internal const string PackageId = "lordkuper.workmanager";

        private const string PatchTypeName =
            "LordKuper.WorkManager.Patches.MainTabWindowWorkPatch";
        private const float ControlHostOffset = 196f;
        private const float ControlHostWidth = 112f;

        private static bool resolved;
        private static Action<Rect> drawControls;

        internal static bool IsPresent =>
            ModLister.GetActiveModWithIdentifier(PackageId, ignorePostfix: true) != null;

        internal static void DrawControls(Rect workTabRect)
        {
            if (!IsPresent || !TryResolveDrawer())
            {
                return;
            }

            float x = Mathf.Min(
                workTabRect.x + ControlHostOffset,
                workTabRect.xMax - ControlHostWidth - 4f);
            var hostRect = new Rect(
                Mathf.Max(workTabRect.x, x),
                workTabRect.y,
                ControlHostWidth,
                workTabRect.height);

            try
            {
                drawControls(hostRect);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Better Work Tab] Work Manager controls could not be drawn inside BWT.\n" +
                    exception,
                    0x42574D47);
            }
        }

        private static bool TryResolveDrawer()
        {
            if (resolved)
            {
                return drawControls != null;
            }

            resolved = true;
            Type patchType = Type.GetType(PatchTypeName + ", LordKuper.WorkManager", throwOnError: false) ??
                             HarmonyLib.AccessTools.TypeByName(PatchTypeName);
            MethodInfo postfix = patchType?.GetMethod(
                "Postfix",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Rect) },
                null);
            if (postfix == null)
            {
                Log.WarningOnce(
                    "[Better Work Tab] Work Manager is active, but its Work-tab control drawer was not found. " +
                    "Per-column automation remains available.",
                    0x42574D48);
                return false;
            }

            try
            {
                drawControls = (Action<Rect>)Delegate.CreateDelegate(typeof(Action<Rect>), postfix);
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Better Work Tab] Work Manager's control drawer could not be connected: " +
                    exception.Message,
                    0x42574D49);
            }

            return drawControls != null;
        }
    }
}
