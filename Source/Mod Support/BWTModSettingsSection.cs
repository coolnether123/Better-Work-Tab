using System.Collections.Generic;
using Spine.UI.SettingsFramework;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// One contributed settings section: a header plus the rows under it.
    /// </summary>
    public sealed class BWTModSettingsSection
    {
        public SettingDefinition Header;
        public IEnumerable<SettingDefinition> Children;
    }
}
