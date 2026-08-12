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

    /// <summary>
    /// Optional host callbacks for applying color previews transactionally to
    /// the host's real setting state.
    /// </summary>
    public interface ISettingColorPreviewTransactionSink
    {
        void Begin(SettingDefinition definition, object settingsObject, Color originalColor);
        void Preview(SettingDefinition definition, object settingsObject, Color color);
        void Commit(SettingDefinition definition, object settingsObject, Color color);
        void Restore(SettingDefinition definition, object settingsObject, Color originalColor);
    }

    public enum SettingClassification
    {
        Preference,
        State
    }

    /// <summary>
    /// Where an entry is drawn relative to the scrolling region.
    /// </summary>
    public enum SettingPin
    {
        /// <summary>Normal entry inside the scrolling list.</summary>
        None,

        /// <summary>Held above the list; stays visible while the list scrolls.</summary>
        Top,

        /// <summary>Held below the list; stays visible while the list scrolls.</summary>
        Bottom
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
        /// Optional translation-key override. Null uses the registry convention;
        /// an empty value intentionally uses the already-localized fallback.
        /// </summary>
        public string LabelKey;

        /// <summary>
        /// Tooltip text shown on hover (fallback if no translation is found).
        /// </summary>
        public string Tooltip;

        /// <summary>
        /// Optional tooltip translation-key override. Null uses the registry
        /// convention; an empty value intentionally uses the fallback.
        /// </summary>
        public string TooltipKey;

        /// <summary>
        /// Optional non-displayed aliases that make this setting easier to find in search.
        /// Use for mod names, common synonyms, or legacy terms that should not clutter the label.
        /// </summary>
        public string[] SearchKeywords;

        /// <summary>
        /// Controls draw order within a hierarchy level. Lower values appear first.
        /// </summary>
        public int SortOrder = int.MinValue;

        /// <summary>
        /// Optional accent used by a header and the rows grouped beneath it.
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

        /// <summary>Optional lower bound for integer and float-style settings.</summary>
        public float? MinValue;

        /// <summary>Optional upper bound for integer and float-style settings.</summary>
        public float? MaxValue;

        /// <summary>Optional label shown at the lower end of a numeric control.</summary>
        public string MinLabel;

        /// <summary>Optional label shown at the upper end of a numeric control.</summary>
        public string MaxLabel;

        /// <summary>
        /// Optional format string for a numeric readout. The value is supplied
        /// as format argument zero.
        /// </summary>
        public string ValueFormat;

        /// <summary>
        /// Enum type for enum-based settings.
        /// </summary>
        public Type EnumType;

        /// <summary>
        /// Optional player-facing label provider for enum values.
        /// </summary>
        public Func<object, string> EnumLabelProvider;

        /// <summary>
        /// Optional player-facing description provider for enum values.
        /// </summary>
        public Func<object, string> EnumDescriptionProvider;

        /// <summary>
        /// Optional value accessors for a derived setting backed by more than one field.
        /// </summary>
        public Func<object, object> ValueGetter;
        public Action<object, object> ValueSetter;

        /// <summary>Supplies the displayed value for a read-only information row.</summary>
        public Func<object, string> ReadOnlyValueProvider;

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
        /// Optional rules that disable this setting without hiding it.
        /// </summary>
        public List<SettingSuppression> Suppressions;

        /// <summary>
        /// Optional relationships describing settings currently superseded by this setting.
        /// </summary>
        public List<SettingSupersession> Supersessions;

        /// <summary>
        /// Returns the first suppression currently in force, or null when the
        /// setting is live.
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
        /// Returns the currently active supersession relationships.
        /// </summary>
        public IReadOnlyList<SettingSupersession> GetActiveSupersessions(object settingsObject)
        {
            if (Supersessions == null || Supersessions.Count == 0)
            {
                return Array.Empty<SettingSupersession>();
            }

            var active = new List<SettingSupersession>(Supersessions.Count);
            for (int i = 0; i < Supersessions.Count; i++)
            {
                SettingSupersession supersession = Supersessions[i];
                if (supersession != null && supersession.IsActive(settingsObject))
                {
                    active.Add(supersession);
                }
            }

            return active;
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
        /// When true for a boolean row, the consumer may render it with header
        /// emphasis while retaining toggle behavior.
        /// </summary>
        public bool EmphasizeAsHeader;

        /// <summary>Supplies options for a dynamic dropdown action.</summary>
        public Func<IEnumerable<string>> DropdownOptionsProvider;

        /// <summary>Receives an option selected from a dynamic dropdown action.</summary>
        public Action<string> OnOptionAdded;

        /// <summary>
        /// Callback invoked when the value changes. Receives the settings object.
        /// </summary>
        public Action<object> OnChanged;

        /// <summary>
        /// Draws a custom row. Return true when the row changed settings.
        /// </summary>
        public Func<Rect, string, string, object, bool, bool> CustomDrawer;

        /// <summary>Reports whether a custom row differs from its default state.</summary>
        public Func<object, bool> CustomHasNonDefaultValue;

        /// <summary>Restores a custom row to its default state.</summary>
        public Action<object> CustomReset;

        /// <summary>
        /// Holds this entry outside the scrolling region so it stays visible while
        /// the rest of the page scrolls. Useful for a live preview that a player
        /// needs to watch while changing the settings that feed it.
        /// Ignored when the pinned bands would take more than half the page, so a
        /// page can never pin away the list it belongs to.
        /// </summary>
        public SettingPin Pin = SettingPin.None;

        /// <summary>Lowest value a slider setting can take.</summary>
        public float SliderMin;

        /// <summary>Highest value a slider setting can take.</summary>
        public float SliderMax = 1f;

        /// <summary>
        /// Quantises a slider to multiples of this value. Zero leaves it
        /// continuous. Use it when the underlying value has a meaningful
        /// granularity, so a player cannot land on 0.7431.
        /// </summary>
        public float SliderStep;

        /// <summary>
        /// Renders the numeric readout beside a slider. Null shows two decimal
        /// places. Supply one to show a percentage, a tick count, or a word.
        /// </summary>
        public Func<float, string> SliderValueFormatter;

    }
}
