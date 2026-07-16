using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Receives semantic color-preview activity from the reusable settings drawer.
    /// The host decides where and how that preview is rendered.
    /// </summary>
    public interface ISettingColorPreviewSink
    {
        void PreviewHover(SettingDefinition definition, Color color);
        void BeginPicker(SettingDefinition definition, Color color);
        void PreviewPicker(SettingDefinition definition, Color color);
        void EndPicker(SettingDefinition definition);
    }

    public enum SettingClassification
    {
        Preference,
        State
    }

    /// <summary>
    /// Defines a single configurable setting with optional parent-child relationships.
    /// </summary>
    public class SettingDefinition
    {
        /// <summary>
        /// Unique identifier for the setting (e.g., "highlights.master").
        /// </summary>
        public string Id;

        /// <summary>
        /// Name of the field on the settings object that stores this value.
        /// Null for non-data entries such as headers, spacers, and buttons.
        /// </summary>
        public string FieldName;

        /// <summary>
        /// XML key used for settings scribing. Null means use FieldName.
        /// </summary>
        public string ScribeKey;

        /// <summary>
        /// Optional legacy default used only when an XML key is absent.
        /// </summary>
        public object ScribeDefaultOverride;

        /// <summary>
        /// True when a setting needs a hand-written Scribe call because its absent-key default
        /// depends on other migrated values.
        /// </summary>
        public bool DisableAutoScribe;

        /// <summary>
        /// Preferences are scribed and reset by the registry. State is not reset.
        /// </summary>
        public SettingClassification Classification = SettingClassification.Preference;

        /// <summary>
        /// Parent setting identifier. Null indicates a root item.
        /// </summary>
        public string ParentId;

        /// <summary>
        /// Human-readable label shown in the UI (fallback if no translation is found).
        /// </summary>
        public string Label;

        /// <summary>
        /// Tooltip text shown on hover (fallback if no translation is found).
        /// </summary>
        public string Tooltip;

        /// <summary>
        /// Optional non-displayed aliases that make this setting easier to find in search.
        /// Use for mod names, common synonyms, or legacy terms that should not clutter the label.
        /// </summary>
        public string[] SearchKeywords;

        /// <summary>
        /// Controls draw order within a hierarchy level. Lower values appear first.
        /// </summary>
        public int SortOrder;

        /// <summary>
        /// Optional color override for headers.
        /// </summary>
        public Color? HeaderColor;

        /// <summary>
        /// Widget type used to render this setting.
        /// </summary>
        public SettingType Type;

        /// <summary>
        /// Default value used when resetting the setting.
        /// </summary>
        public object DefaultValue;

        /// <summary>
        /// Minimum allowed value for numeric settings.
        /// </summary>
        public float? MinValue;

        /// <summary>
        /// Maximum allowed value for numeric settings.
        /// </summary>
        public float? MaxValue;

        /// <summary>
        /// Label shown at the left end of sliders.
        /// </summary>
        public string MinLabel;

        /// <summary>
        /// Label shown at the right end of sliders.
        /// </summary>
        public string MaxLabel;

        /// <summary>
        /// Optional numeric value display format. Uses string.Format with the value as argument 0.
        /// </summary>
        public string ValueFormat;

        /// <summary>
        /// Enum type for enum-based settings.
        /// </summary>
        public Type EnumType;

        /// <summary>
        /// If true, the setting is visible in the Simple view.
        /// </summary>
        public bool ShowInSimpleView;

        /// <summary>
        /// If true, the setting is visible in the Advanced view. Defaults to true.
        /// </summary>
        public bool ShowInAdvancedView = true;

        /// <summary>
        /// Optional predicate that determines runtime visibility.
        /// </summary>
        public Func<object, bool> VisibleWhen;

        /// <summary>
        /// Optional rules that disable this setting at runtime without hiding it. The first active
        /// rule explains itself under the row and, when it names a suppressing setting, links to it.
        /// Suppression cascades: children of a suppressed setting are disabled too.
        /// </summary>
        public List<SettingSuppression> Suppressions;

        /// <summary>
        /// Returns the first suppression currently in force, or null when the setting is live.
        /// </summary>
        public SettingSuppression GetActiveSuppression(object settingsObject)
        {
            if (Suppressions == null)
            {
                return null;
            }

            for (int i = 0; i < Suppressions.Count; i++)
            {
                SettingSuppression suppression = Suppressions[i];
                if (suppression != null && suppression.IsActive(settingsObject))
                {
                    return suppression;
                }
            }

            return null;
        }

        /// <summary>
        /// When true and this is a boolean parent, children are disabled when the parent is unchecked.
        /// </summary>
        public bool ControlsChildVisibility;

        /// <summary>
        /// If true, a restart warning should be shown after changing the value.
        /// </summary>
        public bool RequiresRestart;

        /// <summary>
        /// Callback invoked when the value changes. Receives the settings object.
        /// </summary>
        public Action<object> OnChanged;

        /// <summary>
        /// When true for a Bool type, render it with header styling (bold/underline) while keeping toggle behavior.
        /// </summary>
        public bool EmphasizeAsHeader = false;

        /// <summary>
        /// Provides the list of options for a DropdownListAdder setting.
        /// </summary>
        public Func<IEnumerable<string>> DropdownOptionsProvider;

        /// <summary>
        /// Callback invoked when an option is selected from a DropdownListAdder.
        /// </summary>
        public Action<string> OnOptionAdded;

        /// <summary>
        /// Draws a custom row. Return true when the row changed settings.
        /// </summary>
        public Func<Rect, string, string, object, bool, bool> CustomDrawer;

        /// <summary>
        /// Returns true when a custom row differs from its default state.
        /// </summary>
        public Func<object, bool> CustomHasNonDefaultValue;

        /// <summary>
        /// Restores a custom row to its default state.
        /// </summary>
        public Action<object> CustomReset;
    }
}
