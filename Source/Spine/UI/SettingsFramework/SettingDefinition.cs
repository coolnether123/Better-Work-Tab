using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spine.UI.SettingsFramework
{
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
        /// Optional rules that disable this setting at runtime without hiding it.
        /// </summary>
        public List<SettingSuppression> Suppressions;

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
    }
}
