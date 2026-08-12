using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Spine.Api;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Spine.UI.ContextualSettings
{
    internal sealed class ContextualSettingsService : IContextualSettingsFacade
    {
        internal const string ApiId = "CoolNether123.Spine.ContextualSettings";
        internal const string HarmonyId = ApiId;
        internal static readonly SemanticVersion ApiVersion =
            new SemanticVersion(1, 1, 0);
        private static readonly object Sync = new object();
        private static readonly FieldInfo DialogModField = AccessTools.Field(
            typeof(Dialog_ModSettings),
            "mod");

        private readonly ContextualSettingsRouterCore router =
            new ContextualSettingsRouterCore();
        private readonly DeferredContextualActionQueue deferred =
            new DeferredContextualActionQueue();
        private readonly Dictionary<string, ConsumerRegistration> consumers =
            new Dictionary<string, ConsumerRegistration>(StringComparer.Ordinal);
        private HarmonyLib.Harmony harmony;
        private bool installed;

        internal static readonly ContextualSettingsService Instance =
            new ContextualSettingsService();

        private ContextualSettingsService()
        {
        }

        public SpineApiDescriptor Descriptor => new SpineApiDescriptor(
            ApiId,
            ApiVersion,
            SpineCapability.ContextualSettings | SpineCapability.Settings);

        public IContextualSettingsLease Acquire(
            string consumerId,
            Mod mod,
            SettingsListDrawer settingsDrawer,
            object settingsObject)
        {
            if (string.IsNullOrWhiteSpace(consumerId))
            {
                throw new ArgumentException(
                    "A contextual-settings consumer identifier is required.",
                    nameof(consumerId));
            }

            if (mod == null) throw new ArgumentNullException(nameof(mod));
            if (settingsDrawer == null) throw new ArgumentNullException(nameof(settingsDrawer));
            if (settingsObject == null) throw new ArgumentNullException(nameof(settingsObject));

            lock (Sync)
            {
                if (consumers.ContainsKey(consumerId))
                {
                    throw new InvalidOperationException(
                        "Contextual settings consumer is already registered: " + consumerId);
                }

                EnsureInstalled();
                var registration = new ConsumerRegistration(
                    consumerId,
                    mod,
                    settingsDrawer,
                    settingsObject);
                bool routerAcquired = false;
                try
                {
                    router.Acquire(consumerId);
                    routerAcquired = true;
                    consumers.Add(consumerId, registration);
                    return new Lease(this, registration);
                }
                catch
                {
                    if (routerAcquired)
                    {
                        router.Release(consumerId);
                    }
                    if (consumers.Count == 0)
                    {
                        Uninstall();
                    }
                    throw;
                }
            }
        }

        private bool Bind(
            ConsumerRegistration consumer,
            Rect visibleRect,
            ContextualSettingsTarget target,
            ContextualSettingsBindingOptions options)
        {
            if (consumer == null || consumer.Released)
            {
                return false;
            }

            Event evt = Event.current;
            if (options.RegisterTooltip)
            {
                string tooltip = ContextualTooltipComposition.FeatureOnly(
                    options.FeatureTooltip);
                if (!string.IsNullOrEmpty(tooltip))
                {
                    TooltipHandler.TipRegion(visibleRect, tooltip);
                }
            }

            long frame = Time.frameCount;
            if (evt != null && evt.type == EventType.Repaint)
            {
                Vector2 screenMin = GUIUtility.GUIToScreenPoint(
                    new Vector2(visibleRect.xMin, visibleRect.yMin));
                Vector2 screenMax = GUIUtility.GUIToScreenPoint(
                    new Vector2(visibleRect.xMax, visibleRect.yMax));
                lock (Sync)
                {
                    router.Register(
                        consumer.ConsumerId,
                        new ContextualHitRect(
                            screenMin.x,
                            screenMin.y,
                            screenMax.x - screenMin.x,
                            screenMax.y - screenMin.y),
                        target,
                        options.Priority,
                        frame);
                }
            }

            if (evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0 ||
                !evt.alt)
            {
                return false;
            }

            Vector2 screenMouse = GUIUtility.GUIToScreenPoint(
                evt.mousePosition);
            var pointerEvent = new ContextualPointerEvent(
                ContextualPointerEventType.MouseDown,
                evt.button,
                evt.alt,
                screenMouse.x,
                screenMouse.y);

            ContextualBindingRecord winner;
            lock (Sync)
            {
                if (!router.TryRoute(pointerEvent, frame, out winner) ||
                    !consumers.TryGetValue(winner.ConsumerId, out ConsumerRegistration winnerConsumer) ||
                    winnerConsumer.Released)
                {
                    return false;
                }

                deferred.Enqueue(() => OpenSettings(winnerConsumer, winner.Target));
            }

            evt.Use();
            return true;
        }

        private void EnsureInstalled()
        {
            if (installed)
            {
                return;
            }

            MethodInfo update = AccessTools.Method(typeof(Root), "Update");
            if (update == null)
            {
                throw new MissingMethodException(typeof(Root).FullName, "Update");
            }

            var instance = new HarmonyLib.Harmony(HarmonyId);
            try
            {
                instance.Patch(
                    update,
                    postfix: new HarmonyMethod(
                        typeof(ContextualSettingsService),
                        nameof(AfterRootUpdate)));
                harmony = instance;
                installed = true;
            }
            catch
            {
                instance.UnpatchAll(HarmonyId);
                throw;
            }
        }

        private static void AfterRootUpdate()
        {
            Instance.DrainDeferred();
        }

        private void DrainDeferred()
        {
            lock (Sync)
            {
                deferred.Drain(exception =>
                    Log.ErrorOnce(
                        "[Spine] Contextual settings navigation failed safely: " + exception,
                        172384921));
            }
        }

        private static void OpenSettings(
            ConsumerRegistration consumer,
            ContextualSettingsTarget target)
        {
            if (consumer == null || consumer.Released || Find.WindowStack == null)
            {
                return;
            }

            consumer.SettingsDrawer.PrepareContextNavigation(
                target,
                consumer.SettingsObject);

            Dialog_ModSettings current =
                Find.WindowStack.WindowOfType<Dialog_ModSettings>();
            if (current != null)
            {
                Mod currentMod = DialogModField?.GetValue(current) as Mod;
                if (ReferenceEquals(currentMod, consumer.Mod))
                {
                    return;
                }

                Find.WindowStack.TryRemove(current, false);
            }

#if RWT_HAS_DIALOG_MOD_SETTINGS_MOD_CTOR
            Find.WindowStack.Add(new Dialog_ModSettings(consumer.Mod));
#else
            Dialog_ModSettings dialog = new Dialog_ModSettings();
            if (DialogModField != null)
            {
                DialogModField.SetValue(dialog, consumer.Mod);
            }
            Find.WindowStack.Add(dialog);
#endif
        }

        private void Release(ConsumerRegistration consumer)
        {
            lock (Sync)
            {
                if (consumer == null || consumer.Released)
                {
                    return;
                }

                consumer.Released = true;
                consumers.Remove(consumer.ConsumerId);
                router.Release(consumer.ConsumerId);
                if (consumers.Count != 0 || !installed)
                {
                    return;
                }

                Uninstall();
            }
        }

        private void Uninstall()
        {
            deferred.Clear();
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            installed = false;
        }

        private sealed class ConsumerRegistration
        {
            internal ConsumerRegistration(
                string consumerId,
                Mod mod,
                SettingsListDrawer settingsDrawer,
                object settingsObject)
            {
                ConsumerId = consumerId;
                Mod = mod;
                SettingsDrawer = settingsDrawer;
                SettingsObject = settingsObject;
            }

            internal string ConsumerId { get; }
            internal Mod Mod { get; }
            internal SettingsListDrawer SettingsDrawer { get; }
            internal object SettingsObject { get; }
            internal bool Released { get; set; }
        }

        private sealed class Lease : IContextualSettingsLease
        {
            private readonly ContextualSettingsService owner;
            private readonly ConsumerRegistration consumer;

            internal Lease(
                ContextualSettingsService owner,
                ConsumerRegistration consumer)
            {
                this.owner = owner;
                this.consumer = consumer;
            }

            public bool Bind(
                Rect visibleRect,
                ContextualSettingsTarget target,
                ContextualSettingsBindingOptions options = default(ContextualSettingsBindingOptions)) =>
                owner.Bind(consumer, visibleRect, target, options);

            public void Dispose() => owner.Release(consumer);
        }
    }
}
