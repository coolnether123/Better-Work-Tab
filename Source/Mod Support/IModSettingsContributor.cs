using Spine.UI.SettingsFramework;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Allows mod-support modules and external mods to contribute one settings section.
    /// </summary>
    public interface IModSettingsContributor
    {
        BWTModSettingsSection CreateSettingsSection();
    }
}
