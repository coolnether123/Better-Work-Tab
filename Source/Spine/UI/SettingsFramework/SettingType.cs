using System;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Supported widget types for rendering settings entries.
    /// </summary>
    public enum SettingType
    {
        /// <summary>Checkbox toggle for boolean values.</summary>
        Bool,

        /// <summary>Color swatch with picker dialog.</summary>
        Color,

        /// <summary>Dropdown selection for enum values.</summary>
        Enum,

        /// <summary>Clickable action button.</summary>
        Button,

        /// <summary>Non-interactive section header.</summary>
        Header,

        /// <summary>Custom immediate-mode row supplied by a setting definition.</summary>
        Custom,

        /// <summary>Continuous float value dragged between a minimum and maximum.</summary>
        Slider,

        /// <summary>Integer input with optional bounds.</summary>
        Int,

        /// <summary>Horizontal float input with optional bounds.</summary>
        Float,

        /// <summary>Empty space for visual separation.</summary>
        Spacer,

        /// <summary>
        /// Dropdown action that offers options supplied at draw time and reports
        /// the selected option to the consumer.
        /// </summary>
        DropdownListAdder,

        /// <summary>Integer input with +/- buttons and a text field.</summary>
        NumericInt
    }
}
