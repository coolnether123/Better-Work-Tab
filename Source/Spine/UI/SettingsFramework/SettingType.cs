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

        /// <summary>Integer input with optional buttons.</summary>
        Int,

        /// <summary>Horizontal slider for float values.</summary>
        Float,

        /// <summary>Color swatch with picker dialog.</summary>
        Color,

        /// <summary>Dropdown selection for enum values.</summary>
        Enum,

        /// <summary>Clickable action button.</summary>
        Button,

        /// <summary>Non-interactive section header.</summary>
        Header,

        /// <summary>Empty space for visual separation.</summary>
        Spacer
    }
}
