using System;
using Spine.Api;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Spine.UI.ContextualSettings
{
    public enum ContextualSettingsTargetLevel
    {
        Root = 0,
        Group = 1,
        Exact = 2
    }

#if RWT_LEGACY_BCL
    public struct ContextualSettingsTarget
#else
    public readonly struct ContextualSettingsTarget
#endif
    {
        private ContextualSettingsTarget(
            ContextualSettingsTargetLevel level,
            string settingId,
            string fallbackGroupId)
        {
            Level = level;
            SettingId = settingId;
            FallbackGroupId = fallbackGroupId;
        }

#if RWT_LEGACY_BCL
        public ContextualSettingsTargetLevel Level;
        public string SettingId;
        public string FallbackGroupId;
#else
        public ContextualSettingsTargetLevel Level { get; }
        public string SettingId { get; }
        public string FallbackGroupId { get; }
#endif

        public static ContextualSettingsTarget Exact(
            string settingId,
            string fallbackGroupId = null) =>
            new ContextualSettingsTarget(
                ContextualSettingsTargetLevel.Exact,
                RequireId(settingId),
                fallbackGroupId);

        public static ContextualSettingsTarget Group(string settingId) =>
            new ContextualSettingsTarget(
                ContextualSettingsTargetLevel.Group,
                RequireId(settingId),
                null);

        public static ContextualSettingsTarget Root() =>
            new ContextualSettingsTarget(
                ContextualSettingsTargetLevel.Root,
                null,
                null);

        private static string RequireId(string value)
        {
            if (LegacyBcl.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A contextual settings target identifier is required.",
                    nameof(value));
            }

            return value;
        }
    }

#if RWT_LEGACY_BCL
    public struct ContextualSettingsBindingOptions
#else
    public readonly struct ContextualSettingsBindingOptions
#endif
    {
        /// <summary>
        /// Configures overlap priority and, when the feature already owns a
        /// useful tooltip, lets Spine register that tooltip once. Contextual
        /// settings never appends an Alt-click hint to world or feature UI.
        /// </summary>
        public ContextualSettingsBindingOptions(
            int priority = 0,
            string featureTooltip = null,
            string settingsHint = null,
            bool registerTooltip = false)
        {
            Priority = priority;
            FeatureTooltip = featureTooltip;
            SettingsHint = settingsHint;
            RegisterTooltip = registerTooltip;
        }

#if RWT_LEGACY_BCL
        public int Priority;
        public string FeatureTooltip;
#else
        public int Priority { get; }
        public string FeatureTooltip { get; }
#endif

        /// <summary>
        /// Retained for binary compatibility. Spine no longer displays
        /// contextual-settings hints beside gameplay features.
        /// </summary>
#if RWT_LEGACY_BCL
        public string SettingsHint;
        public bool RegisterTooltip;
#else
        public string SettingsHint { get; }
        public bool RegisterTooltip { get; }
#endif

        public static ContextualSettingsBindingOptions WithTooltip(
            string featureTooltip,
            int priority = 0,
            string settingsHint = null) =>
            new ContextualSettingsBindingOptions(
                priority,
                featureTooltip,
                settingsHint,
                true);

        public static ContextualSettingsBindingOptions HintOnly(
            int priority = 0,
            string settingsHint = null) =>
            new ContextualSettingsBindingOptions(
                priority,
                null,
                settingsHint,
                false);
    }

    public interface IContextualSettingsLease : IDisposable
    {
        bool Bind(
            Rect visibleRect,
            ContextualSettingsTarget target,
            ContextualSettingsBindingOptions options = default(ContextualSettingsBindingOptions));
    }

    /// <summary>
    /// Low-boilerplate bindings for the two common contextual-settings targets.
    /// </summary>
    public static class ContextualSettingsLeaseExtensions
    {
        public static bool BindSetting(
            this IContextualSettingsLease lease,
            Rect visibleRect,
            string settingId,
            string fallbackGroupId = null,
            int priority = 0,
            string featureTooltip = null)
        {
            if (!CanBind(lease, visibleRect, settingId))
            {
                return false;
            }

            return lease.Bind(
                visibleRect,
                ContextualSettingsTarget.Exact(settingId, fallbackGroupId),
                Options(priority, featureTooltip));
        }

        public static bool BindSettingsGroup(
            this IContextualSettingsLease lease,
            Rect visibleRect,
            string groupId,
            int priority = 0,
            string featureTooltip = null)
        {
            if (!CanBind(lease, visibleRect, groupId))
            {
                return false;
            }

            return lease.Bind(
                visibleRect,
                ContextualSettingsTarget.Group(groupId),
                Options(priority, featureTooltip));
        }

        private static ContextualSettingsBindingOptions Options(
            int priority,
            string featureTooltip)
        {
            return LegacyBcl.IsNullOrWhiteSpace(featureTooltip)
                ? ContextualSettingsBindingOptions.HintOnly(priority)
                : ContextualSettingsBindingOptions.WithTooltip(
                    featureTooltip,
                    priority);
        }

        private static bool CanBind(
            IContextualSettingsLease lease,
            Rect visibleRect,
            string id) =>
            lease != null &&
            !LegacyBcl.IsNullOrWhiteSpace(id) &&
            !float.IsNaN(visibleRect.width) &&
            !float.IsInfinity(visibleRect.width) &&
            !float.IsNaN(visibleRect.height) &&
            !float.IsInfinity(visibleRect.height) &&
            visibleRect.width > 0f &&
            visibleRect.height > 0f;
    }

    public interface IContextualSettingsFacade
    {
        SpineApiDescriptor Descriptor { get; }

        IContextualSettingsLease Acquire(
            string consumerId,
            Mod mod,
            SettingsListDrawer settingsDrawer,
            object settingsObject);
    }
}
