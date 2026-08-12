using System;
using System.Collections.Generic;
using Spine.Api;
using Spine.UI.ContextualSettings;
using UnityEngine;
using Verse;

namespace Spine.UI.SettingsFramework
{
    public sealed class ModSettingsPageOptions
    {
        public float RowHeight { get; set; } = 32f;
    }

    public interface IModSettingsPage : IDisposable
    {
        IContextualSettingsLease ContextualSettings { get; }

        void Draw(Rect rect);
    }

    public interface IModSettingsFacade
    {
        SpineApiDescriptor Descriptor { get; }

        IModSettingsPage Acquire(
            string consumerId,
            Mod mod,
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions,
            Action writeSettings,
            ModSettingsPageOptions options = null);

        void Scribe(
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions);

        IReadOnlyCollection<string> ApplyPreferenceDefaults(
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions);

        void NotifyPreferenceChanges(
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions,
            IEnumerable<string> changedFields = null);

        string EffectiveScribeKey(SettingDefinition definition);
    }

    internal sealed class ModSettingsFacade : IModSettingsFacade
    {
        internal static readonly ModSettingsFacade Instance =
            new ModSettingsFacade();

        private static readonly SpineApiDescriptor CurrentDescriptor =
            new SpineApiDescriptor(
                "CoolNether123.Spine.ModSettingsPages",
                new SemanticVersion(1, 1, 0),
                SpineCapability.Settings |
                SpineCapability.ContextualSettings |
                SpineCapability.ModSettingsPages |
                SpineCapability.SettingsSchema);

        private ModSettingsFacade()
        {
        }

        public SpineApiDescriptor Descriptor => CurrentDescriptor;

        public IModSettingsPage Acquire(
            string consumerId,
            Mod mod,
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions,
            Action writeSettings,
            ModSettingsPageOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(consumerId))
            {
                throw new ArgumentException(
                    "A settings-page consumer identifier is required.",
                    nameof(consumerId));
            }

            if (mod == null)
            {
                throw new ArgumentNullException(nameof(mod));
            }

            if (settingsObject == null)
            {
                throw new ArgumentNullException(nameof(settingsObject));
            }

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            SettingsPreparation.Prepare(settingsObject, definitions);

            var pageOptions = options ?? new ModSettingsPageOptions();
            if (float.IsNaN(pageOptions.RowHeight) ||
                float.IsInfinity(pageOptions.RowHeight) ||
                pageOptions.RowHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Settings row height must be finite and positive.");
            }

            var drawer = new SettingsListDrawer(
                new SettingsHierarchy(definitions))
            {
                RowHeight = pageOptions.RowHeight,
                SimpleLabel = Translate("Spine_Settings_UI_Simple", "Simple"),
                AdvancedLabel = Translate(
                    "Spine_Settings_UI_Advanced",
                    "Advanced"),
                NoResultsLabel = Translate(
                    "Spine_Settings_UI_NoResults",
                    "No settings match"),
                ResetToDefaultLabel = Translate(
                    "Spine_Settings_UI_ResetToDefault",
                    "Reset to default"),
                EditColorLabel = Translate(
                    "Spine_Settings_UI_EditColor",
                    "Edit"),
                GetLabel = definition => Translate(
                    definition?.LabelKey,
                    definition?.Label),
                GetTooltip = definition => Translate(
                    definition?.TooltipKey,
                    definition?.Tooltip)
            };

            IContextualSettingsLease contextualSettings =
                ContextualSettingsService.Instance.Acquire(
                    consumerId,
                    mod,
                    drawer,
                    settingsObject);
            return new ModSettingsPage(
                drawer,
                settingsObject,
                writeSettings,
                contextualSettings);
        }

        public void Scribe(
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions)
        {
            if (settingsObject == null)
            {
                throw new ArgumentNullException(nameof(settingsObject));
            }

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            SettingsScribe.ScribeAll(settingsObject, definitions);
        }

        public IReadOnlyCollection<string> ApplyPreferenceDefaults(
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions)
        {
            return SettingsScribe.ApplyPreferenceDefaults(
                settingsObject,
                definitions);
        }

        public void NotifyPreferenceChanges(
            object settingsObject,
            IReadOnlyList<SettingDefinition> definitions,
            IEnumerable<string> changedFields = null)
        {
            SettingsScribe.NotifyPreferenceChanges(
                settingsObject,
                definitions,
                changedFields);
        }

        public string EffectiveScribeKey(SettingDefinition definition)
        {
            return SettingsScribe.EffectiveScribeKey(definition);
        }

        private static string Translate(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key))
            {
                return fallback ?? string.Empty;
            }

            return key.CanTranslate()
                ? key.Translate().ToString()
                : fallback ?? key;
        }

        private sealed class ModSettingsPage : IModSettingsPage
        {
            private readonly SettingsListDrawer drawer;
            private readonly object settingsObject;
            private readonly Action writeSettings;
            private SettingsViewMode viewMode;
            private IContextualSettingsLease contextualSettings;

            internal ModSettingsPage(
                SettingsListDrawer drawer,
                object settingsObject,
                Action writeSettings,
                IContextualSettingsLease contextualSettings)
            {
                this.drawer = drawer;
                this.settingsObject = settingsObject;
                this.writeSettings = writeSettings;
                this.contextualSettings = contextualSettings;
                viewMode = SettingsViewMode.Simple;
            }

            public IContextualSettingsLease ContextualSettings =>
                contextualSettings;

            public void Draw(Rect rect)
            {
                drawer.Draw(
                    rect,
                    settingsObject,
                    ref viewMode,
                    writeSettings);
            }

            public void Dispose()
            {
                IContextualSettingsLease lease = contextualSettings;
                contextualSettings = null;
                lease?.Dispose();
            }
        }
    }
}
