using System;
using System.Collections.Generic;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Defines a single setting's metadata. Pure C# for reusability.
    /// </summary>
    public class SettingDefinition
    {
        public string Id { get; }
        public string Label { get; }
        public string Tooltip { get; }
        public string CategoryId { get; }
        public SettingType Type { get; }
        public string FieldName { get; }
        public object DefaultValue { get; }
        public bool IsFavoritable { get; }

        public SettingDefinition(
            string id,
            string label,
            string tooltip,
            string categoryId,
            SettingType type,
            string fieldName,
            object defaultValue = null,
            bool isFavoritable = true)
        {
            Id = id;
            Label = label;
            Tooltip = tooltip;
            CategoryId = categoryId;
            Type = type;
            FieldName = fieldName;
            DefaultValue = defaultValue;
            IsFavoritable = isFavoritable;
        }
    }

    public enum SettingType
    {
        Bool,
        Int,
        Float,
        Color,
        Enum,
        Button,
        Slider
    }

    /// <summary>
    /// Defines a settings category. Pure C# for reusability.
    /// </summary>
    public class SettingsCategory
    {
        public string Id { get; }
        public string Label { get; }
        public string Description { get; }
        public string IconPath { get; }
        public int DisplayOrder { get; }
        public List<SettingDefinition> Settings { get; }

        public SettingsCategory(
            string id,
            string label,
            string description,
            string iconPath = null,
            int displayOrder = 0)
        {
            Id = id;
            Label = label;
            Description = description;
            IconPath = iconPath;
            DisplayOrder = displayOrder;
            Settings = new List<SettingDefinition>();
        }

        public SettingsCategory AddSetting(SettingDefinition setting)
        {
            Settings.Add(setting);
            return this;
        }
    }

    /// <summary>
    /// Registry for all settings categories. Pure C# singleton pattern.
    /// </summary>
    public class SettingsRegistry
    {
        private static SettingsRegistry _instance;
        public static SettingsRegistry Instance => _instance ?? (_instance = new SettingsRegistry());

        private readonly Dictionary<string, SettingsCategory> _categories;
        private readonly Dictionary<string, SettingDefinition> _allSettings;

        private SettingsRegistry()
        {
            _categories = new Dictionary<string, SettingsCategory>();
            _allSettings = new Dictionary<string, SettingDefinition>();
        }

        public void RegisterCategory(SettingsCategory category)
        {
            _categories[category.Id] = category;
            foreach (var setting in category.Settings)
            {
                _allSettings[setting.Id] = setting;
            }
        }

        public SettingsCategory GetCategory(string id)
        {
            return _categories.TryGetValue(id, out var cat) ? cat : null;
        }

        public SettingDefinition GetSetting(string id)
        {
            return _allSettings.TryGetValue(id, out var setting) ? setting : null;
        }

        public IEnumerable<SettingsCategory> GetAllCategories()
        {
            return _categories.Values;
        }

        public void Clear()
        {
            _categories.Clear();
            _allSettings.Clear();
        }
    }
}
