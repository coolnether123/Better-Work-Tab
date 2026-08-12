using System;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Describes one optional filter the settings drawer can apply to its visible rows.
    /// </summary>
    public class SettingsFilterDefinition
    {
        public string Id;
        public string Label;
        public string Tooltip;
        public string Category;
        public string CategoryLabel;
        public Func<SettingDefinition, object, bool> Predicate;
        public bool IncludeChildrenOfMatches = true;

        public bool Matches(SettingDefinition definition, object settingsObject)
        {
            return definition != null && Predicate != null && Predicate(definition, settingsObject);
        }
    }
}
