using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Draws a scrollable list of hierarchical settings with search and view toggles.
    /// </summary>
    public class SettingsListDrawer
    {
        private const float ResetIconSlotWidth = 26f;
        private const float ResetButtonSize = 20f;
        private const float FooterHeight = 34f;
        private const float ToolbarGap = 8f;

        private readonly SettingsHierarchy _hierarchy;
        private Vector2 _scrollPosition;
        private string _searchQuery = string.Empty;
        private readonly QuickSearchWidget _searchWidget = new QuickSearchWidget();
        private SettingsFilterDefinition _activeFilter;
        private TransferMode _transferMode = TransferMode.None;
        private string _pendingFocusTargetId;

        /// <summary>
        /// Gets or sets the current scroll position. Used for preserving scroll state across drawer recreations.
        /// </summary>
        public Vector2 ScrollPosition
        {
            get => _scrollPosition;
            set => _scrollPosition = value;
        }

        /// <summary>
        /// Pixels of indentation per hierarchy level.
        /// </summary>
        public float IndentPerLevel { get; set; } = 20f;

        /// <summary>
        /// Height of each row.
        /// </summary>
        public float RowHeight { get; set; } = 32f;

        /// <summary>
        /// Callback to translate labels.
        /// </summary>
        public Func<SettingDefinition, string> GetLabel { get; set; }

        /// <summary>
        /// Callback to translate tooltips.
        /// </summary>
        public Func<SettingDefinition, string> GetTooltip { get; set; }

        /// <summary>
        /// Display text for the Simple view toggle.
        /// </summary>
        public string SimpleLabel { get; set; } = "Simple";

        /// <summary>
        /// Display text for the Advanced view toggle.
        /// </summary>
        public string AdvancedLabel { get; set; } = "Advanced";

        /// <summary>
        /// Display text when no settings match.
        /// </summary>
        public string NoResultsLabel { get; set; } = "No results";

        /// <summary>
        /// Label for the color edit button.
        /// </summary>
        public string EditColorLabel { get; set; } = "Edit";

        /// <summary>
        /// When true, changed field-backed settings show a small per-row reset button.
        /// </summary>
        public bool ShowResetIcons { get; set; } = true;

        /// <summary>
        /// Tooltip for per-setting reset buttons.
        /// </summary>
        public string ResetToDefaultLabel { get; set; } = "Reset to default";

        /// <summary>
        /// Optional filters shown by the toolbar filter button.
        /// </summary>
        public IReadOnlyList<SettingsFilterDefinition> Filters { get; set; } = Array.Empty<SettingsFilterDefinition>();

        /// <summary>
        /// Toolbar label shown when no filter is active.
        /// </summary>
        public string FilterLabel { get; set; } = "Filter";

        /// <summary>
        /// Menu label that clears the active filter.
        /// </summary>
        public string AllSettingsFilterLabel { get; set; } = "All settings";

        /// <summary>
        /// Optional import/export footer actions.
        /// </summary>
        public SettingsImportExportActions ImportExportActions { get; set; }

        /// <summary>
        /// Creates a new drawer for a hierarchy.
        /// </summary>
        public SettingsListDrawer(SettingsHierarchy hierarchy)
        {
            _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        }

        public void ApplyContextFilter(SettingsFilterDefinition filter, string targetSettingId)
        {
            if (filter == null)
            {
                return;
            }

            _activeFilter = filter;
            _pendingFocusTargetId = targetSettingId;
            _transferMode = TransferMode.None;
            ClearSearch();
        }

        /// <summary>
        /// Draws the full UI for the settings list including search and view toggle.
        /// </summary>
        public void Draw(
            Rect rect,
            object settingsObject,
            ref SettingsViewMode viewMode,
            Action onSettingsChanged = null)
        {
            if (settingsObject == null)
            {
                return;
            }

            const float headerHeight = 30f;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
            DrawHeader(headerRect, ref viewMode);

            float listStartY = headerRect.yMax + 10f;
            bool drawFooter = ImportExportActions?.HasAnyAction ?? false;
            float footerSpace = drawFooter ? FooterHeight + 8f : 0f;
            Rect listRect = new Rect(rect.x, listStartY, rect.width, rect.height - (listStartY - rect.y) - footerSpace);
            DrawSettingsList(listRect, settingsObject, viewMode, onSettingsChanged);

            if (drawFooter)
            {
                Rect footerRect = new Rect(rect.x, rect.yMax - FooterHeight, rect.width, FooterHeight);
                DrawImportExportFooter(footerRect);
            }
        }

        private void DrawHeader(Rect rect, ref SettingsViewMode viewMode)
        {
            bool hasFilters = Filters != null && Filters.Count > 0;
            float toggleWidth = 200f;
            float filterWidth = hasFilters ? 150f : 0f;
            float searchWidth = Mathf.Max(120f, rect.width - toggleWidth - filterWidth - (hasFilters ? ToolbarGap * 2f : ToolbarGap));
            Rect searchRect = new Rect(rect.x, rect.y, searchWidth, rect.height);
            Rect filterRect = new Rect(searchRect.xMax + ToolbarGap, rect.y, filterWidth, rect.height);
            Rect toggleRect = new Rect(rect.xMax - 200f, rect.y, 200f, rect.height);

            _searchWidget.OnGUI(searchRect, () => { });
            _searchQuery = _searchWidget.filter.Text ?? string.Empty;

            if (hasFilters)
            {
                DrawFilterButton(filterRect);
            }

            DrawViewToggle(toggleRect, ref viewMode);
        }

        private void DrawFilterButton(Rect rect)
        {
            string label = _activeFilter != null ? _activeFilter.Label : FilterLabel;
            if (!Widgets.ButtonText(rect, label))
            {
                if (_activeFilter != null && !string.IsNullOrEmpty(_activeFilter.Tooltip))
                {
                    TooltipHandler.TipRegion(rect, _activeFilter.Tooltip);
                }

                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(AllSettingsFilterLabel, () => _activeFilter = null)
            };

            if (HasFilterCategories())
            {
                AddFilterCategoryOptions(options);
                Find.WindowStack.Add(new FloatMenu(options));
                return;
            }

            foreach (var filter in Filters)
            {
                if (filter == null)
                {
                    continue;
                }

                var localFilter = filter;
                options.Add(new FloatMenuOption(localFilter.Label ?? localFilter.Id, () => _activeFilter = localFilter));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private bool HasFilterCategories()
        {
            foreach (var filter in Filters)
            {
                if (!string.IsNullOrEmpty(filter?.Category) || !string.IsNullOrEmpty(filter?.CategoryLabel))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddFilterCategoryOptions(List<FloatMenuOption> options)
        {
            var categories = new List<FilterCategory>();
            foreach (var filter in Filters)
            {
                if (filter == null)
                {
                    continue;
                }

                string id = string.IsNullOrEmpty(filter.Category) ? "other" : filter.Category;
                string label = string.IsNullOrEmpty(filter.CategoryLabel) ? id : filter.CategoryLabel;
                FilterCategory category = categories.Find(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
                if (category == null)
                {
                    category = new FilterCategory(id, label);
                    categories.Add(category);
                }

                category.Filters.Add(filter);
            }

            foreach (var category in categories)
            {
                var localCategory = category;
                options.Add(new FloatMenuOption(localCategory.Label, () => OpenFilterCategoryMenu(localCategory)));
            }
        }

        private void OpenFilterCategoryMenu(FilterCategory category)
        {
            if (category == null)
            {
                return;
            }

            var options = new List<FloatMenuOption>();

            foreach (var filter in category.Filters)
            {
                if (filter == null)
                {
                    continue;
                }

                var localFilter = filter;
                options.Add(new FloatMenuOption(localFilter.Label ?? localFilter.Id, () => _activeFilter = localFilter));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private sealed class FilterCategory
        {
            internal readonly string Id;
            internal readonly string Label;
            internal readonly List<SettingsFilterDefinition> Filters = new List<SettingsFilterDefinition>();

            internal FilterCategory(string id, string label)
            {
                Id = id;
                Label = label;
            }
        }

        /// <summary>
        /// Draws the simple/advanced view toggle buttons.
        /// </summary>
        private void DrawViewToggle(Rect rect, ref SettingsViewMode viewMode)
        {
            Rect simpleRect = rect.LeftHalf().ContractedBy(2f);
            Rect advancedRect = rect.RightHalf().ContractedBy(2f);

            bool isSimple = viewMode == SettingsViewMode.Simple;

            GUI.color = isSimple ? Color.white : Color.gray;
            if (Widgets.ButtonText(simpleRect, SimpleLabel))
            {
                viewMode = SettingsViewMode.Simple;
            }

            GUI.color = !isSimple ? Color.white : Color.gray;
            if (Widgets.ButtonText(advancedRect, AdvancedLabel))
            {
                viewMode = SettingsViewMode.Advanced;
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// Draws the scrollable list of settings.
        /// </summary>
        private void DrawSettingsList(
            Rect rect,
            object settingsObject,
            SettingsViewMode viewMode,
            Action onSettingsChanged)
        {
            bool isSearching = !string.IsNullOrWhiteSpace(_searchQuery);
            var visibleSettings = BuildVisibleSettings(settingsObject, viewMode, isSearching);

            if (visibleSettings.Count == 0)
            {
                string emptyLabel = NoResultsLabel;

                // If we're in Simple view and nothing matches, hint that Advanced may have results.
                if (viewMode == SettingsViewMode.Simple && !string.IsNullOrWhiteSpace(_searchQuery))
                {
                    var advancedMatches = BuildVisibleSettings(settingsObject, SettingsViewMode.Advanced, useSearch: true);
                    if (advancedMatches.Count > 0)
                    {
                        emptyLabel = "Switch to advanced mode for more settings";
                    }
                }

                Widgets.Label(rect, emptyLabel);
                return;
            }

            float viewHeight = visibleSettings.Count * RowHeight;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);

            ApplyPendingFocusIfNeeded(visibleSettings, rect.height);

            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float curY = 0f;
            foreach (var def in visibleSettings)
            {
                int depth = _hierarchy.GetDepth(def);
                bool disabledByAncestor = _hierarchy.IsDisabledByAncestor(def, settingsObject);

                Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);
                if (isSearching)
                {
                    TryHandleSearchResultDoubleClick(rowRect, def, settingsObject, viewMode, rect.height);
                }

                DrawSettingRow(rowRect, def, settingsObject, disabledByAncestor, depth, onSettingsChanged);
                curY += RowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws a single setting row with indentation and disabled state.
        /// </summary>
        private void DrawSettingRow(
            Rect rect,
            SettingDefinition def,
            object settingsObject,
            bool isDisabledByParent,
            int depth,
            Action onSettingsChanged)
        {
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            string label = GetLabel?.Invoke(def) ?? def.Label ?? def.Id;
            string tooltip = BuildTooltip(def);

            float indent = depth * IndentPerLevel;
            Rect contentRect = new Rect(rect.x + indent, rect.y, rect.width - indent, rect.height);

            bool disabled = isDisabledByParent;
            FieldInfo field = null;
            if (!string.IsNullOrEmpty(def.FieldName))
            {
                field = settingsObject.GetType().GetField(def.FieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            bool reserveResetSlot = ShowResetIcons && IsResettable(def, field);
            if (reserveResetSlot)
            {
                Rect resetRect = new Rect(
                    contentRect.x,
                    contentRect.y + ((contentRect.height - ResetButtonSize) / 2f),
                    ResetButtonSize,
                    ResetButtonSize);

                contentRect.x += ResetIconSlotWidth;
                contentRect.width = Mathf.Max(0f, contentRect.width - ResetIconSlotWidth);

                if (HasNonDefaultValue(field, settingsObject, def))
                {
                    DrawResetButton(resetRect, disabled, () =>
                    {
                        if (ResetSettingToDefault(field, settingsObject, def))
                        {
                            def.OnChanged?.Invoke(settingsObject);
                            onSettingsChanged?.Invoke();
                        }
                    });
                }
            }

            switch (def.Type)
            {
                case SettingType.Bool:
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        bool boolValue = (bool)field.GetValue(settingsObject);
                        bool changed = def.EmphasizeAsHeader
                            ? SettingWidgets.DrawHeaderBool(contentRect, label, ref boolValue, def.HeaderColor, tooltip, disabled)
                            : SettingWidgets.DrawBool(contentRect, label, ref boolValue, tooltip, disabled);
                        if (changed)
                        {
                            field.SetValue(settingsObject, boolValue);
                            def.OnChanged?.Invoke(settingsObject);
                            onSettingsChanged?.Invoke();
                        }
                    }
                    break;
                case SettingType.Int:
                    if (field != null && field.FieldType == typeof(int))
                    {
                        int intValue = (int)field.GetValue(settingsObject);
                        int min = def.MinValue.HasValue ? Mathf.RoundToInt(def.MinValue.Value) : int.MinValue;
                        int max = def.MaxValue.HasValue ? Mathf.RoundToInt(def.MaxValue.Value) : int.MaxValue;
                        if (SettingWidgets.DrawInt(contentRect, label, ref intValue, min, max, tooltip, disabled))
                        {
                            field.SetValue(settingsObject, intValue);
                            def.OnChanged?.Invoke(settingsObject);
                            onSettingsChanged?.Invoke();
                        }
                    }
                    break;
                case SettingType.NumericInt:
                    if (field != null && field.FieldType == typeof(int))
                    {
                        int intValue = (int)field.GetValue(settingsObject);
                        int min = def.MinValue.HasValue ? Mathf.RoundToInt(def.MinValue.Value) : int.MinValue;
                        int max = def.MaxValue.HasValue ? Mathf.RoundToInt(def.MaxValue.Value) : int.MaxValue;
                        if (SettingWidgets.DrawNumericInt(contentRect, label, ref intValue, min, max, tooltip, disabled))
                        {
                            field.SetValue(settingsObject, intValue);
                            def.OnChanged?.Invoke(settingsObject);
                            onSettingsChanged?.Invoke();
                        }
                    }
                    break;
                case SettingType.Float:
                    if (field != null && (field.FieldType == typeof(float) || field.FieldType == typeof(double)))
                    {
                        float floatValue = Convert.ToSingle(field.GetValue(settingsObject));
                        float min = def.MinValue ?? 0f;
                        float max = def.MaxValue ?? 1f;
                        if (SettingWidgets.DrawFloat(contentRect, label, ref floatValue, min, max,
                                def.MinLabel, def.MaxLabel, tooltip, disabled))
                        {
                            field.SetValue(settingsObject, floatValue);
                            def.OnChanged?.Invoke(settingsObject);
                            onSettingsChanged?.Invoke();
                        }
                    }
                    break;
                case SettingType.Color:
                    if (field != null && field.FieldType == typeof(Color))
                    {
                        Color colorValue = (Color)field.GetValue(settingsObject);
                        SettingWidgets.DrawColor(contentRect, label, ref colorValue, tooltip, disabled,
                            (current, onSelected) =>
                            {
                                var dialog = new Spine.UI.ColourPicker.Dialog_ColourPicker(current, (newColor, _) =>
                                {
                                    field.SetValue(settingsObject, newColor);
                                    def.OnChanged?.Invoke(settingsObject);
                                    onSettingsChanged?.Invoke();
                                    onSelected?.Invoke(newColor);
                                });

                                Find.WindowStack.Add(dialog);
                            }, EditColorLabel);
                    }
                    break;
                case SettingType.Enum:
                    if (field != null && def.EnumType != null)
                    {
                        object current = field.GetValue(settingsObject);
                        SettingWidgets.DrawEnum(contentRect, label, current, def.EnumType, tooltip, disabled, selected =>
                        {
                            field.SetValue(settingsObject, selected);
                            def.OnChanged?.Invoke(settingsObject);
                            onSettingsChanged?.Invoke();
                        });
                    }
                    break;
                case SettingType.Button:
                    if (SettingWidgets.DrawButton(contentRect, label, tooltip, disabled))
                    {
                        def.OnChanged?.Invoke(settingsObject);
                        onSettingsChanged?.Invoke();
                    }
                    break;
                case SettingType.Header:
                    SettingWidgets.DrawHeader(contentRect, label, def.HeaderColor);
                    break;
                case SettingType.Spacer:
                    SettingWidgets.DrawSpacer(contentRect);
                    break;
                case SettingType.DropdownListAdder:
                    SettingWidgets.DrawDropdownListAdder(contentRect, label, def.DropdownOptionsProvider, def.OnOptionAdded, tooltip, disabled);
                    break;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        private string BuildTooltip(SettingDefinition def)
        {
            string tooltip = GetTooltip?.Invoke(def) ?? def.Tooltip ?? string.Empty;

            // Append parent chain info for children
            if (!string.IsNullOrEmpty(def.ParentId))
            {
                var parent = _hierarchy.GetParent(def);
                var grandParent = _hierarchy.GetParent(parent);

                List<string> parentParts = new List<string>();
                if (parent != null)
                {
                    parentParts.Add(GetLabel?.Invoke(parent) ?? parent.Label ?? parent.Id);
                }
                if (grandParent != null)
                {
                    parentParts.Add(GetLabel?.Invoke(grandParent) ?? grandParent.Label ?? grandParent.Id);
                }

                if (parentParts.Count > 0)
                {
                    if (!string.IsNullOrEmpty(tooltip))
                    {
                        tooltip += "\n\n";
                    }

                    tooltip += parentParts.Count == 1
                        ? $"Parent: {parentParts[0]}"
                        : $"Parent chain: {string.Join(" › ", parentParts)}";
                }
            }

            return tooltip;
        }

        private List<SettingDefinition> BuildVisibleSettings(
            object settingsObject,
            SettingsViewMode viewMode,
            bool useSearch)
        {
            IEnumerable<SettingDefinition> source = useSearch
                ? _hierarchy.Search(_searchQuery, viewMode)
                : _hierarchy.GetFlattenedForView(viewMode, settingsObject);

            var visibleSettings = new List<SettingDefinition>();
            foreach (var setting in source)
            {
                if (settingsObject != null && setting.VisibleWhen != null && !setting.VisibleWhen(settingsObject))
                {
                    continue;
                }

                if (!MatchesActiveFilter(setting, settingsObject))
                {
                    continue;
                }

                visibleSettings.Add(setting);
            }

            return visibleSettings;
        }

        private bool MatchesActiveFilter(SettingDefinition setting, object settingsObject)
        {
            if (_activeFilter == null)
            {
                return true;
            }

            if (_activeFilter.Matches(setting, settingsObject))
            {
                return true;
            }

            if (!_activeFilter.IncludeChildrenOfMatches)
            {
                return false;
            }

            var parent = _hierarchy.GetParent(setting);
            while (parent != null)
            {
                if (_activeFilter.Matches(parent, settingsObject))
                {
                    return true;
                }

                parent = _hierarchy.GetParent(parent);
            }

            return false;
        }

        private void DrawImportExportFooter(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
            Rect contentRect = rect.ContractedBy(2f);
            contentRect.y += 4f;
            contentRect.height -= 4f;

            if (_transferMode == TransferMode.None)
            {
                float buttonWidth = 110f;
                Rect exportRect = new Rect(contentRect.x, contentRect.y, buttonWidth, contentRect.height);
                Rect importRect = new Rect(exportRect.xMax + 6f, contentRect.y, buttonWidth, contentRect.height);

                if (ImportExportActions.ExportToFile != null || ImportExportActions.ExportToClipboard != null)
                {
                    if (Widgets.ButtonText(exportRect, ImportExportActions.ExportLabel))
                    {
                        _transferMode = TransferMode.Export;
                    }
                }

                if (ImportExportActions.ImportFromFile != null || ImportExportActions.ImportFromClipboard != null)
                {
                    if (Widgets.ButtonText(importRect, ImportExportActions.ImportLabel))
                    {
                        _transferMode = TransferMode.Import;
                    }
                }

                return;
            }

            string prefix = _transferMode == TransferMode.Export
                ? ImportExportActions.ExportLabel
                : ImportExportActions.ImportLabel;
            Rect labelRect = new Rect(contentRect.x, contentRect.y + 5f, 80f, contentRect.height);
            Widgets.Label(labelRect, prefix + ":");

            float optionWidth = 110f;
            Rect fileRect = new Rect(labelRect.xMax + 4f, contentRect.y, optionWidth, contentRect.height);
            Rect clipboardRect = new Rect(fileRect.xMax + 6f, contentRect.y, optionWidth, contentRect.height);
            Rect cancelRect = new Rect(clipboardRect.xMax + 6f, contentRect.y, optionWidth, contentRect.height);

            Action fileAction = _transferMode == TransferMode.Export
                ? ImportExportActions.ExportToFile
                : ImportExportActions.ImportFromFile;
            Action clipboardAction = _transferMode == TransferMode.Export
                ? ImportExportActions.ExportToClipboard
                : ImportExportActions.ImportFromClipboard;

            if (fileAction != null && Widgets.ButtonText(fileRect, ImportExportActions.FileLabel))
            {
                _transferMode = TransferMode.None;
                fileAction();
            }

            if (clipboardAction != null && Widgets.ButtonText(clipboardRect, ImportExportActions.ClipboardLabel))
            {
                _transferMode = TransferMode.None;
                clipboardAction();
            }

            if (Widgets.ButtonText(cancelRect, ImportExportActions.CancelLabel))
            {
                _transferMode = TransferMode.None;
            }
        }

        private bool TryHandleSearchResultDoubleClick(
            Rect rowRect,
            SettingDefinition target,
            object settingsObject,
            SettingsViewMode viewMode,
            float listHeight)
        {
            Event evt = Event.current;
            if (evt == null ||
                evt.type != EventType.MouseDown ||
                evt.clickCount < 2 ||
                !rowRect.Contains(evt.mousePosition))
            {
                return false;
            }

            CenterOnSetting(target, settingsObject, viewMode, listHeight);
            ClearSearch();
            evt.Use();
            return true;
        }

        private void ApplyPendingFocusIfNeeded(List<SettingDefinition> visibleSettings, float listHeight)
        {
            if (string.IsNullOrEmpty(_pendingFocusTargetId) || visibleSettings == null || visibleSettings.Count == 0)
            {
                return;
            }

            int index = visibleSettings.FindIndex(def => string.Equals(def.Id, _pendingFocusTargetId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                index = 0;
            }

            float viewHeight = visibleSettings.Count * RowHeight;
            float maxScrollY = Mathf.Max(0f, viewHeight - listHeight);
            float targetY = index * RowHeight;
            _scrollPosition.y = Mathf.Clamp(targetY - ((listHeight - RowHeight) * 0.5f), 0f, maxScrollY);
            _scrollPosition.x = 0f;
            _pendingFocusTargetId = null;
        }

        private void ClearSearch()
        {
            _searchWidget.Reset();
            _searchWidget.Unfocus();
            _searchQuery = string.Empty;
        }

        private void CenterOnSetting(
            SettingDefinition target,
            object settingsObject,
            SettingsViewMode viewMode,
            float listHeight)
        {
            if (target == null)
            {
                return;
            }

            var fullList = BuildVisibleSettings(settingsObject, viewMode, useSearch: false);
            int index = fullList.FindIndex(def => ReferenceEquals(def, target) || def.Id == target.Id);
            if (index < 0)
            {
                return;
            }

            float viewHeight = fullList.Count * RowHeight;
            float maxScrollY = Mathf.Max(0f, viewHeight - listHeight);
            float targetY = index * RowHeight;
            _scrollPosition.y = Mathf.Clamp(targetY - ((listHeight - RowHeight) * 0.5f), 0f, maxScrollY);
            _scrollPosition.x = 0f;
        }

        private bool IsResettable(SettingDefinition def, FieldInfo field)
        {
            if (def == null || field == null || def.DefaultValue == null)
            {
                return false;
            }

            switch (def.Type)
            {
                case SettingType.Bool:
                case SettingType.Int:
                case SettingType.NumericInt:
                case SettingType.Float:
                case SettingType.Color:
                case SettingType.Enum:
                    return true;
                default:
                    return false;
            }
        }

        private bool HasNonDefaultValue(FieldInfo field, object settingsObject, SettingDefinition def)
        {
            if (field == null || settingsObject == null || def == null)
            {
                return false;
            }

            if (!TryGetDefaultValueForField(field, def.DefaultValue, out var defaultValue))
            {
                return false;
            }

            object currentValue = field.GetValue(settingsObject);
            return !ValuesEqual(currentValue, defaultValue);
        }

        private void DrawResetButton(Rect rect, bool disabled, Action resetAction)
        {
            bool oldEnabled = GUI.enabled;
            Color oldColor = GUI.color;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            bool clicked = Widgets.ButtonText(rect, "R");

            GUI.enabled = oldEnabled;
            GUI.color = oldColor;

            TooltipHandler.TipRegion(rect, ResetToDefaultLabel);
            if (!disabled && clicked)
            {
                resetAction?.Invoke();
                Event.current?.Use();
            }
        }

        private bool ResetSettingToDefault(FieldInfo field, object settingsObject, SettingDefinition def)
        {
            if (field == null || settingsObject == null || def == null)
            {
                return false;
            }

            if (!TryGetDefaultValueForField(field, def.DefaultValue, out var defaultValue))
            {
                return false;
            }

            field.SetValue(settingsObject, defaultValue);
            return true;
        }

        private static bool TryGetDefaultValueForField(FieldInfo field, object configuredDefault, out object value)
        {
            value = null;
            if (field == null || configuredDefault == null)
            {
                return false;
            }

            Type fieldType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
            Type defaultType = configuredDefault.GetType();

            try
            {
                if (fieldType.IsAssignableFrom(defaultType))
                {
                    value = configuredDefault;
                    return true;
                }

                if (fieldType.IsEnum)
                {
                    value = configuredDefault is string text
                        ? Enum.Parse(fieldType, text)
                        : Enum.ToObject(fieldType, configuredDefault);
                    return true;
                }

                value = Convert.ChangeType(configuredDefault, fieldType);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a is float af && b is float bf)
            {
                return Mathf.Approximately(af, bf);
            }

            if (a is double ad && b is double bd)
            {
                return Math.Abs(ad - bd) < 0.0001d;
            }

            if (a is Color ac && b is Color bc)
            {
                return Mathf.Approximately(ac.r, bc.r) &&
                       Mathf.Approximately(ac.g, bc.g) &&
                       Mathf.Approximately(ac.b, bc.b) &&
                       Mathf.Approximately(ac.a, bc.a);
            }

            return Equals(a, b);
        }

        private enum TransferMode
        {
            None,
            Export,
            Import
        }
    }
}
