using System.Collections.Generic;
using System.Linq;

namespace Better_Work_Tab.UI.Settings
{
    public sealed class BWTSettingsFocusRequest
    {
        public BWTSettingsFocusRequest(
            string label,
            string tooltip,
            string targetSettingId,
            bool preferAdvancedView,
            IEnumerable<string> settingIds)
        {
            Label = string.IsNullOrEmpty(label) ? "Context" : label;
            Tooltip = tooltip ?? string.Empty;
            TargetSettingId = targetSettingId;
            PreferAdvancedView = preferAdvancedView;
            SettingIds = settingIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList() ?? new List<string>();
        }

        public string Label { get; }
        public string Tooltip { get; }
        public string TargetSettingId { get; }
        public bool PreferAdvancedView { get; }
        public IReadOnlyList<string> SettingIds { get; }
    }
}
