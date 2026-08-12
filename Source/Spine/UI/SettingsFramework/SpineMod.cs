using System;
using System.Collections.Generic;
using Spine.Api;
using Spine.UI.ContextualSettings;
using UnityEngine;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Low-boilerplate host for the common RimWorld mod pattern: one persisted
    /// settings object, one definition-driven settings page, and contextual
    /// settings navigation. Gameplay initialization remains in the consumer.
    /// </summary>
    public abstract class SpineMod<TSettings> : Mod
        where TSettings : ModSettings, new()
    {
        private static SpineMod<TSettings> current;
        private readonly string consumerId;
        private readonly IReadOnlyList<SettingDefinition> definitions;
        private readonly ModSettingsPageOptions options;
        private IModSettingsPage settingsPage;

        protected SpineMod(
            ModContentPack content,
            string consumerId,
            SemanticVersion minimumSpineVersion,
            IReadOnlyList<SettingDefinition> definitions,
            SpineCapability additionalCapabilities = SpineCapability.None,
            ModSettingsPageOptions options = null)
            : base(content)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            SpineApi.Runtime.Require(new SpineRequirement(
                consumerId,
                minimumSpineVersion,
                SpineCapability.Settings |
                SpineCapability.ContextualSettings |
                SpineCapability.ModSettingsPages |
                additionalCapabilities));

            this.consumerId = consumerId;
            this.definitions = definitions;
            this.options = options;
            ManagedSettings = GetSettings<TSettings>();
            current = this;
        }

        protected TSettings ManagedSettings { get; }

        /// <summary>
        /// The settings instance owned by the active mod host for this settings
        /// type. Consumer code can access it through its derived mod type without
        /// repeating an Instance field or settings forwarding property.
        /// </summary>
        public static TSettings Settings => current?.ManagedSettings;

        /// <summary>
        /// The contextual-settings lease owned by the active mod host. Access is
        /// lazy so consumers pay for settings-page infrastructure only when used.
        /// </summary>
        public static IContextualSettingsLease ContextualSettings =>
            current?.GetSettingsPage().ContextualSettings;

        protected abstract string SettingsCategoryLabel { get; }

        public sealed override string SettingsCategory()
        {
            return SettingsCategoryLabel;
        }

        public sealed override void DoSettingsWindowContents(Rect inRect)
        {
            GetSettingsPage().Draw(inRect);
        }

        private IModSettingsPage GetSettingsPage()
        {
            if (settingsPage == null)
            {
                settingsPage = SpineApi.Settings.Acquire(
                    consumerId,
                    this,
                    ManagedSettings,
                    definitions,
                    WriteSettings,
                    options);
            }

            return settingsPage;
        }
    }
}
