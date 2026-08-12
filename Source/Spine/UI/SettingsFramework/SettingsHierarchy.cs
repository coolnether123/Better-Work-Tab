using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Builds and queries a parent-child hierarchy for settings definitions.
    /// Handles sorting, depth calculation, ancestor checks, and view filtering.
    /// </summary>
    public class SettingsHierarchy
    {
        private readonly Dictionary<string, SettingDefinition> _byId;
        private readonly Dictionary<string, List<SettingDefinition>> _childrenOf;
        private readonly List<SettingDefinition> _rootSettings;

        public int SettingCount { get; }
        public int AdvancedOnlySettingCount { get; }

        /// <summary>
        /// Creates a new hierarchy from a flat list of definitions.
        /// </summary>
        public SettingsHierarchy(IEnumerable<SettingDefinition> definitions)
        {
            _byId = new Dictionary<string, SettingDefinition>();
            _childrenOf = new Dictionary<string, List<SettingDefinition>>();
            _rootSettings = new List<SettingDefinition>();

            foreach (var def in definitions ?? Enumerable.Empty<SettingDefinition>())
            {
                if (def == null || string.IsNullOrEmpty(def.Id))
                {
                    continue;
                }

                _byId[def.Id] = def;
            }

            SettingCount = _byId.Values.Count(definition =>
                !IsNonConfigurable(definition));
            AdvancedOnlySettingCount = _byId.Values.Count(definition =>
                !IsNonConfigurable(definition) &&
                !definition.ShowInSimpleView &&
                definition.ShowInAdvancedView);

            foreach (var def in _byId.Values)
            {
                if (string.IsNullOrEmpty(def.ParentId) || !_byId.ContainsKey(def.ParentId))
                {
                    _rootSettings.Add(def);
                    continue;
                }

                if (!_childrenOf.TryGetValue(def.ParentId, out var list))
                {
                    list = new List<SettingDefinition>();
                    _childrenOf[def.ParentId] = list;
                }

                list.Add(def);
            }

            SortList(_rootSettings);
            foreach (var childList in _childrenOf.Values)
            {
                SortList(childList);
            }
        }

        /// <summary>
        /// Returns all root-level settings sorted by sort order.
        /// </summary>
        public IReadOnlyList<SettingDefinition> GetRootSettings()
        {
            return _rootSettings;
        }

        /// <summary>
        /// Returns direct children for a parent identifier (empty list when none).
        /// </summary>
        public IReadOnlyList<SettingDefinition> GetChildren(string parentId)
        {
            if (string.IsNullOrEmpty(parentId) || !_childrenOf.TryGetValue(parentId, out var list))
            {
                return Array.Empty<SettingDefinition>();
            }

            return list;
        }

        /// <summary>
        /// Calculates hierarchy depth (0 for root).
        /// </summary>
        public int GetDepth(SettingDefinition setting)
        {
            int depth = 0;
            var current = setting;

            while (current != null && !string.IsNullOrEmpty(current.ParentId) &&
                   _byId.TryGetValue(current.ParentId, out var parent))
            {
                depth++;
                current = parent;
            }

            return depth;
        }

        /// <summary>
        /// Returns the parent definition for a setting, or null if it is root or unknown.
        /// </summary>
        public SettingDefinition GetParent(SettingDefinition setting)
        {
            if (setting == null || string.IsNullOrEmpty(setting.ParentId))
            {
                return null;
            }

            _byId.TryGetValue(setting.ParentId, out var parent);
            return parent;
        }

        public SettingDefinition GetById(string settingId)
        {
            if (string.IsNullOrEmpty(settingId))
            {
                return null;
            }

            _byId.TryGetValue(settingId, out var setting);
            return setting;
        }

        public IEnumerable<SettingDefinition> GetAncestors(SettingDefinition setting)
        {
            var current = setting;
            while (current != null && !string.IsNullOrEmpty(current.ParentId) &&
                   _byId.TryGetValue(current.ParentId, out var parent))
            {
                yield return parent;
                current = parent;
            }
        }

        /// <summary>
        /// Checks if the setting is disabled by any ancestor that controls visibility.
        /// </summary>
        public bool IsDisabledByAncestor(SettingDefinition setting, object settingsObject)
        {
            var current = setting;
            while (current != null && !string.IsNullOrEmpty(current.ParentId) &&
                   _byId.TryGetValue(current.ParentId, out var parent))
            {
                if (parent.ControlsChildVisibility && parent.Type == SettingType.Bool)
                {
                    if (!ReadBoolValue(parent, settingsObject))
                    {
                        return true;
                    }
                }

                current = parent;
            }

            return false;
        }

        /// <summary>
        /// Returns a flattened list in display order for the requested view.
        /// Applies VisibleWhen predicates using the supplied settings object.
        /// </summary>
        public IEnumerable<SettingDefinition> GetFlattenedForView(
            SettingsViewMode viewMode,
            object settingsObject)
        {
            foreach (var root in _rootSettings)
            {
                foreach (var item in EnumerateWithChildren(root, viewMode, settingsObject))
                {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// Searches settings by visible text, identifier, or registered aliases within the requested view.
        /// When a group/header matches, its visible descendants are included so grouped settings can be
        /// discovered through the group's searchable terms.
        /// </summary>
        public IEnumerable<SettingDefinition> Search(
            string query,
            SettingsViewMode viewMode,
            object settingsObject = null,
            Func<SettingDefinition, string> getLabel = null,
            Func<SettingDefinition, string> getTooltip = null)
        {
            var ordered = _rootSettings.SelectMany(s => EnumerateWithChildren(s, viewMode, settingsObject));

            if (string.IsNullOrWhiteSpace(query))
            {
                return ordered;
            }

            string needle = query.ToLowerInvariant();
            string normalizedNeedle = NormalizeSearchText(query);
            return ordered.Where(def =>
                SettingMatchesSearch(def, needle, normalizedNeedle, getLabel, getTooltip) ||
                HasMatchingAncestor(def, needle, normalizedNeedle, getLabel, getTooltip));
        }

        private bool HasMatchingAncestor(
            SettingDefinition setting,
            string needle,
            string normalizedNeedle,
            Func<SettingDefinition, string> getLabel,
            Func<SettingDefinition, string> getTooltip)
        {
            foreach (SettingDefinition ancestor in GetAncestors(setting))
            {
                if (SettingMatchesSearch(ancestor, needle, normalizedNeedle, getLabel, getTooltip))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SettingMatchesSearch(
            SettingDefinition def,
            string needle,
            string normalizedNeedle,
            Func<SettingDefinition, string> getLabel,
            Func<SettingDefinition, string> getTooltip)
        {
            if (def == null)
            {
                return false;
            }

            if (SearchTextMatches(getLabel?.Invoke(def), needle, normalizedNeedle) ||
                SearchTextMatches(def.Label, needle, normalizedNeedle) ||
                SearchTextMatches(getTooltip?.Invoke(def), needle, normalizedNeedle) ||
                SearchTextMatches(def.Tooltip, needle, normalizedNeedle) ||
                SearchTextMatches(def.Id, needle, normalizedNeedle))
            {
                return true;
            }

            if (def.SearchKeywords == null)
            {
                return false;
            }

            for (int i = 0; i < def.SearchKeywords.Length; i++)
            {
                if (SearchTextMatches(def.SearchKeywords[i], needle, normalizedNeedle))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SearchTextMatches(string text, string needle, string normalizedNeedle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string haystack = text.ToLowerInvariant();
            if (haystack.Contains(needle))
            {
                return true;
            }

            string normalizedHaystack = NormalizeSearchText(text);
            return !string.IsNullOrEmpty(normalizedNeedle) && normalizedHaystack.Contains(normalizedNeedle);
        }

        private static string NormalizeSearchText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            char[] buffer = new char[text.Length];
            int length = 0;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '\u2010' || c == '\u2011' || c == '\u2012' || c == '\u2013' || c == '\u2014')
                {
                    continue;
                }

                buffer[length++] = char.ToLowerInvariant(c);
            }

            return new string(buffer, 0, length);
        }

        private IEnumerable<SettingDefinition> EnumerateWithChildren(
            SettingDefinition setting,
            SettingsViewMode viewMode,
            object settingsObject)
        {
            if (IsVisibleInView(setting, viewMode))
            {
                if (settingsObject == null || setting.VisibleWhen == null || setting.VisibleWhen(settingsObject))
                {
                    yield return setting;
                }
                else
                {
                    // If hidden by predicate, skip children too
                    yield break;
                }
            }

            if (_childrenOf.TryGetValue(setting.Id, out var children))
            {
                foreach (var child in children)
                {
                    foreach (var desc in EnumerateWithChildren(child, viewMode, settingsObject))
                    {
                        yield return desc;
                    }
                }
            }
        }


        private static bool IsVisibleInView(SettingDefinition def, SettingsViewMode viewMode)
        {
            if (viewMode == SettingsViewMode.All)
            {
                return def.ShowInSimpleView || def.ShowInAdvancedView;
            }

            // Advanced is a superset of Simple, not a sibling list: it is the
            // simple list plus the settings hidden from it. ShowInAdvancedView
            // therefore means "advanced-only"; simple membership always implies
            // advanced membership. Enforcing it here makes it an invariant of
            // the framework rather than a convention that one stray
            // "ShowInAdvancedView = false" can quietly break.
            return viewMode == SettingsViewMode.Simple
                ? def.ShowInSimpleView
                : def.ShowInAdvancedView || def.ShowInSimpleView;
        }

        private static bool IsNonConfigurable(SettingDefinition definition)
        {
            return definition.Type == SettingType.Header ||
                definition.Type == SettingType.Spacer;
        }

        private static bool ReadBoolValue(SettingDefinition def, object settingsObject)
        {
            if (settingsObject == null || string.IsNullOrEmpty(def.FieldName))
            {
                return true;
            }

            var field = settingsObject.GetType().GetField(def.FieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field != null && field.FieldType == typeof(bool))
            {
                return (bool)field.GetValue(settingsObject);
            }

            return true;
        }

        private static void SortList(List<SettingDefinition> list)
        {
            list.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }
    }

    /// <summary>
    /// View mode used to filter which settings are displayed.
    /// </summary>
    public enum SettingsViewMode
    {
        Simple,
        Advanced,
        All
    }
}
