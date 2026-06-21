#if vAlpha4
using System;
using System.IO;
using Better_Work_Tab.Features;
using Better_Work_Tab.UI;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using VerseBase;

namespace Better_Work_Tab
{
    public class Alpha4BootstrapDef : Def
    {
        public Alpha4BootstrapDef()
        {
            Legacy016Bootstrap.Initialize();
        }
    }

    internal static class Alpha4PatchInstaller
    {
        private static readonly Harmony Harmony = new Harmony("Coolnether123.betterworktab.alpha4");
        private static bool installed;

        public static void Install()
        {
            if (installed)
            {
                return;
            }

            installed = true;

            Patch(
                AccessTools.Method(typeof(global::Verse.Find), "ResetBaseReferences"),
                postfix: AccessTools.Method(typeof(Alpha4Patch_Find_ResetBaseReferences), nameof(Alpha4Patch_Find_ResetBaseReferences.Postfix)));

            Patch(
                AccessTools.Method(typeof(Root), "Update"),
                prefix: AccessTools.Method(typeof(Alpha4Patch_Root_EnsureUiRoot), nameof(Alpha4Patch_Root_EnsureUiRoot.Prefix)));

            Patch(
                AccessTools.Method(typeof(Root), "OnGUI"),
                prefix: AccessTools.Method(typeof(Alpha4Patch_Root_EnsureUiRoot), nameof(Alpha4Patch_Root_EnsureUiRoot.Prefix)));

            Patch(
                typeof(Tab_Overview_Work).GetMethod(
                    "PanelOnGUI",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                prefix: AccessTools.Method(typeof(Alpha4Patch_TabOverviewWork_PanelOnGUI), "Prefix"));

            Patch(
                AccessTools.Constructor(typeof(Dialog_Overview)),
                postfix: AccessTools.Method(typeof(Alpha4Patch_DialogOverview_Constructor), nameof(Alpha4Patch_DialogOverview_Constructor.Postfix)));

            Patch(
                AccessTools.Method(typeof(Pawn_WorkSettings), "SetWorkToPriority"),
                prefix: AccessTools.Method(
                    typeof(Better_Work_Tab.Features.Patches.Alpha4Patch_Pawn_WorkSettings_SetWorkToPriority),
                    "Prefix"));

            Patch(
                AccessTools.PropertyGetter(typeof(Pawn_WorkSettings), "ActiveWorkTypesByPriority"),
                postfix: AccessTools.Method(
                    typeof(Better_Work_Tab.Features.Patch_WorkExecutionOrder_ActiveWorkTypesByPriority),
                    nameof(Better_Work_Tab.Features.Patch_WorkExecutionOrder_ActiveWorkTypesByPriority.Postfix)));

            Log.Message("[Better Work Tab] Alpha4 compatibility patches installed.");
        }

        private static void Patch(MethodBase original, MethodInfo prefix = null, MethodInfo postfix = null)
        {
            if (original == null)
            {
                Log.Warning("[Better Work Tab] Alpha4 patch target was not found.");
                return;
            }

            try
            {
                Harmony.Patch(
                    original,
                    prefix == null ? null : new HarmonyMethod(prefix),
                    postfix == null ? null : new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                Log.Error("[Better Work Tab] Alpha4 patch failed for " + original + ": " + ex);
            }
        }
    }

    internal static class Alpha4Patch_Root_EnsureUiRoot
    {
        public static void Prefix(Root __instance)
        {
            Alpha4UiRootRepair.Ensure(__instance);
            Alpha4QuickTestDriver.Update(__instance);
        }
    }

    internal static class Alpha4Patch_Find_ResetBaseReferences
    {
        private static readonly FieldInfo RootRootField = AccessTools.Field(typeof(global::Verse.Find), "rootRoot");
        private static readonly FieldInfo RootEntryField = AccessTools.Field(typeof(global::Verse.Find), "rootEntry");
        private static readonly FieldInfo RootMapField = AccessTools.Field(typeof(global::Verse.Find), "rootMap");
        private static readonly FieldInfo CameraMenuField = AccessTools.Field(typeof(global::Verse.Find), "cameraMenu");
        private static readonly FieldInfo CameraMapField = AccessTools.Field(typeof(global::Verse.Find), "cameraMap");

        public static void Postfix()
        {
            try
            {
                PostfixInner();
            }
            catch (Exception ex)
            {
                Log.Error("[Better Work Tab] Alpha4 root reference repair failed: " + ex);
            }
        }

        private static void PostfixInner()
        {
            GameObject core = GameObject.Find("GameCoreDummy");
            if (core == null)
            {
                return;
            }

            RootEntry entry = core.GetComponent<RootEntry>();
            if (entry != null)
            {
                RootEntryField?.SetValue(null, entry);
                RootRootField?.SetValue(null, entry);
                EnsureMenuCamera();
                return;
            }

            RootMap map = core.GetComponent<RootMap>();
            if (map != null)
            {
                RootMapField?.SetValue(null, map);
                RootRootField?.SetValue(null, map);
                EnsureMapCamera();
            }
        }

        private static void EnsureMenuCamera()
        {
            if (CameraMenuField?.GetValue(null) != null)
            {
                return;
            }

            GameObject cameraObject = GameObject.Find("CameraMenu");
            Camera camera = cameraObject == null ? null : cameraObject.GetComponent<Camera>();
            if (camera != null)
            {
                CameraMenuField.SetValue(null, camera);
            }
        }

        private static void EnsureMapCamera()
        {
            if (CameraMapField?.GetValue(null) != null)
            {
                return;
            }

            GameObject cameraObject = GameObject.Find("CameraMap");
            CameraMap camera = cameraObject == null ? null : cameraObject.GetComponent<CameraMap>();
            if (camera != null)
            {
                CameraMapField.SetValue(null, camera);
            }
        }
    }

    internal static class Alpha4UiRootRepair
    {
        private static readonly FieldInfo RootRootField = AccessTools.Field(typeof(global::Verse.Find), "rootRoot");
        private static readonly FieldInfo RootEntryField = AccessTools.Field(typeof(global::Verse.Find), "rootEntry");
        private static readonly FieldInfo RootMapField = AccessTools.Field(typeof(global::Verse.Find), "rootMap");
        private static bool loggedRootState;

        public static void Ensure(Root root)
        {
            if (root == null)
            {
                return;
            }

            RootRootField?.SetValue(null, root);

            if (!loggedRootState)
            {
                loggedRootState = true;
                string uiRootName = root.uiRoot == null ? "<null>" : root.uiRoot.GetType().FullName;
                int layerCount = root.uiRoot?.layers?.LayerCount ?? -1;
                Log.Message("[Better Work Tab] Alpha4 root state: root=" + root.GetType().FullName + ", uiRoot=" + uiRootName + ", layers=" + layerCount + ", gameMode=" + Game.Mode);
            }

            if (root.uiRoot != null)
            {
                EnsureEntryMenuLayer(root);
                return;
            }

            if (root is RootMap map)
            {
                RootMapField?.SetValue(null, map);
                root.uiRoot = new UIMapRoot();
                Log.Message("[Better Work Tab] Alpha4 repaired missing map UI root.");
                return;
            }

            if (root is RootEntry entry)
            {
                RootEntryField?.SetValue(null, entry);
            }

            root.uiRoot = new UIEntryRoot();
            Log.Message("[Better Work Tab] Alpha4 repaired missing entry UI root.");
            EnsureEntryMenuLayer(root);
        }

        private static void EnsureEntryMenuLayer(Root root)
        {
            if (!(root is RootEntry) || root.uiRoot?.layers == null || root.uiRoot.layers.LayerCount > 0)
            {
                return;
            }

            root.uiRoot.layers.Add(new Page_MainMenu());
            Log.Message("[Better Work Tab] Alpha4 restored missing main-menu layer.");
        }
    }

    internal static class Alpha4ModSettingsPersistence
    {
        private const string FolderName = "BetterWorkTab";

        public static T Load<T>() where T : ModSettings, new()
        {
            string path = SettingsPath(typeof(T));
            try
            {
                T settings = XmlLoader.ItemFromXmlFile<T>(path);
                return settings ?? new T();
            }
            catch (Exception ex)
            {
                Log.Error("[Better Work Tab] Alpha4 settings load failed for " + typeof(T).Name + ": " + ex);
                return new T();
            }
        }

        public static void Save(ModSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            try
            {
                string path = SettingsPath(settings.GetType());
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                XmlSaver.SaveDataObject(settings, path);
            }
            catch (Exception ex)
            {
                Log.Error("[Better Work Tab] Alpha4 settings save failed for " + settings.GetType().Name + ": " + ex);
            }
        }

        private static string SettingsPath(Type settingsType)
        {
            string configPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "Config");
            string settingsPath = Path.Combine(configPath, FolderName);
            return Path.Combine(settingsPath, settingsType.Name + ".xml");
        }
    }

    internal static class Alpha4QuickTestDriver
    {
        private const string QuickTestArgument = "-bwt-alpha4-quicktest";
        private static bool enabled;
        private static bool checkedArgs;
        private static bool queuedGameplayLoad;
        private static bool ranPrioritySelfTest;
        private static bool openedOverview;
        private static bool capturedOverview;
        private static int overviewOpenedFrame;

        public static void Update(Root root)
        {
            if (root == null || !IsEnabled())
            {
                return;
            }

            if (Game.Mode == GameMode.Entry)
            {
                QueueGameplayLoad();
                return;
            }

            RunPrioritySelfTest();
            OpenWorkOverview();
            CaptureOverviewScreenshot();
        }

        private static bool IsEnabled()
        {
            if (checkedArgs)
            {
                return enabled;
            }

            checkedArgs = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], QuickTestArgument, StringComparison.OrdinalIgnoreCase))
                {
                    enabled = true;
                    Log.Message("[Better Work Tab] Alpha4 quicktest driver enabled.");
                    break;
                }
            }

            return enabled;
        }

        private static void QueueGameplayLoad()
        {
            if (queuedGameplayLoad)
            {
                return;
            }

            queuedGameplayLoad = true;
            global::Verse.LongEventHandler.QueueLongEvent(() =>
            {
                MapInitParams.Clear();
                MapInitParams.mapSize = 150;
                MapInitParams.startedFromEntry = false;
                Application.LoadLevel("Gameplay");
            }, "GeneratingWorld".Translate());
            Log.Message("[Better Work Tab] Alpha4 quicktest queued Gameplay load.");
        }

        private static void OpenWorkOverview()
        {
            if (openedOverview || !IsMapReady())
            {
                return;
            }

            if (!HasFreeColonist())
            {
                return;
            }

            if (global::Verse.Find.LayerStack == null || global::Verse.Find.LayerStack.IsOpen(typeof(Dialog_Overview)))
            {
                return;
            }

            global::Verse.Find.LayerStack.Add(new Dialog_Overview());
            openedOverview = true;
            overviewOpenedFrame = Time.frameCount;
            Log.Message("[Better Work Tab] Alpha4 quicktest opened Work overview.");
        }

        private static void RunPrioritySelfTest()
        {
            if (ranPrioritySelfTest || !IsMapReady())
            {
                return;
            }

            Pawn pawn = FirstFreeColonist();
            if (pawn == null)
            {
                return;
            }

            WorkTypeDef workType = FirstEnabledWorkType(pawn);
            Pawn_WorkSettings workSettings = Better_Work_Tab.PawnCompat.WorkSettings(pawn);
            if (workType == null || workSettings == null)
            {
                Log.Warning("[Better Work Tab] Alpha4 max-priority quicktest skipped: no valid pawn/work settings.");
                ranPrioritySelfTest = true;
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings ?? Legacy016ModSettingsStore.Get<BetterWorkTabSettings>();
            BetterWorkTabMod.Settings = settings;
            settings.maxPriorityInt = BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT;
            global::Verse.Find.Map.playSettings.useWorkPriorities = true;

            Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.SetPriority(
                workSettings,
                workType,
                BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT);

            int storedPriority = workSettings.GetPriorityOf(workType);
            int systemPriority = Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.GetPriority(workSettings, workType);
            bool activeContains = false;
            foreach (WorkTypeDef activeWorkType in workSettings.ActiveWorkTypesByPriority)
            {
                if (activeWorkType == workType)
                {
                    activeContains = true;
                    break;
                }
            }

            ranPrioritySelfTest = true;
            Log.Message(
                "[Better Work Tab] Alpha4 max-priority quicktest: pawn=" + pawn.Label +
                ", workType=" + workType.defName +
                ", max=" + settings.EffectiveMaxPriority +
                ", stored=" + storedPriority +
                ", system=" + systemPriority +
                ", activeContains=" + activeContains + ".");
        }

        private static bool IsMapReady()
        {
            return global::Verse.Find.RootMap != null &&
                   global::Verse.Find.Map != null &&
                   global::Verse.Map.Initialized &&
                   global::Verse.Find.LayerStack != null;
        }

        private static bool HasFreeColonist()
        {
            if (global::Verse.Find.ListerPawns == null)
            {
                return false;
            }

            foreach (Pawn ignored in global::Verse.Find.ListerPawns.FreeColonists)
            {
                return true;
            }

            return false;
        }

        private static Pawn FirstFreeColonist()
        {
            if (global::Verse.Find.ListerPawns == null)
            {
                return null;
            }

            foreach (Pawn pawn in global::Verse.Find.ListerPawns.FreeColonists)
            {
                return pawn;
            }

            return null;
        }

        private static WorkTypeDef FirstEnabledWorkType(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            foreach (WorkTypeDef workType in WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder)
            {
                if (workType != null && !pawn.story.WorkTypeIsDisabled(workType))
                {
                    return workType;
                }
            }

            return null;
        }

        private static void CaptureOverviewScreenshot()
        {
            if (!openedOverview || capturedOverview || Time.frameCount < overviewOpenedFrame + 20)
            {
                return;
            }

            capturedOverview = true;
            string path = Path.Combine(Path.GetTempPath(), "bwt-alpha4-quicktest-overview.png");
            Application.CaptureScreenshot(path);
            Log.Message("[Better Work Tab] Alpha4 quicktest captured overview screenshot: " + path);
        }
    }

    internal static class Alpha4Patch_DialogOverview_Constructor
    {
        public static void Postfix(Dialog_Overview __instance)
        {
            if (__instance == null)
            {
                return;
            }

            float width = Mathf.Min(__instance.winRect.width, Mathf.Max(320f, Screen.width - 20f));
            float height = Mathf.Min(__instance.winRect.height, Mathf.Max(240f, Screen.height - 20f));
            __instance.winRect = new Rect(
                Mathf.Max(0f, (Screen.width - width) / 2f),
                Mathf.Max(0f, (Screen.height - height) / 2f),
                width,
                height);
        }
    }

    [HarmonyPatch(typeof(Tab_Overview_Work), "PanelOnGUI")]
    internal static class Alpha4Patch_TabOverviewWork_PanelOnGUI
    {
        private static readonly MainTabWindow_BetterWork Renderer = new MainTabWindow_BetterWork();
        private static bool loggedFallback;
        private static bool loggedRender;

        private static bool Prefix(Rect fillRect)
        {
            try
            {
                if (!loggedRender)
                {
                    loggedRender = true;
                    Log.Message("[Better Work Tab] Alpha4 Work tab renderer active.");
                }

                Legacy016Bootstrap.Initialize();

                Rect innerRect = fillRect.GetInnerRect(10f);
                GUI.BeginGroup(innerRect);
                try
                {
                    Renderer.DoAlpha4PanelContents(new Rect(0f, 0f, innerRect.width, innerRect.height));
                }
                finally
                {
                    GUI.EndGroup();
                }

                return false;
            }
            catch (Exception ex)
            {
                if (!loggedFallback)
                {
                    loggedFallback = true;
                    Log.Error($"[Better Work Tab] Alpha4 Work tab renderer failed; falling back to vanilla. Exception: {ex}");
                }

                return true;
            }
        }
    }
}
#endif
