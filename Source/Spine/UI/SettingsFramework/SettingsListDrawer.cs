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
        private readonly SettingsHierarchy _hierarchy;
        private Vector2 _scrollPosition;
        private string _searchQuery = string.Empty;
        private readonly QuickSearchWidget _searchWidget = new QuickSearchWidget();

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
        /// Creates a new drawer for a hierarchy.
        /// </summary>
        public SettingsListDrawer(SettingsHierarchy hierarchy)
        {
            _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
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
            Rect listRect = new Rect(rect.x, listStartY, rect.width, rect.height - (listStartY - rect.y));
            DrawSettingsList(listRect, settingsObject, viewMode, onSettingsChanged);
        }

        private void DrawHeader(Rect rect, ref SettingsViewMode viewMode)
        {
            Rect searchRect = new Rect(rect.x, rect.y, rect.width * 0.55f, rect.height);
            Rect toggleRect = new Rect(rect.xMax - 200f, rect.y, 200f, rect.height);

            _searchWidget.OnGUI(searchRect, () => { });
            _searchQuery = _searchWidget.filter.Text ?? string.Empty;

            DrawViewToggle(toggleRect, ref viewMode);
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
            IEnumerable<SettingDefinition> source = string.IsNullOrWhiteSpace(_searchQuery)
                ? _hierarchy.GetFlattenedForView(viewMode, settingsObject)
                : _hierarchy.Search(_searchQuery, viewMode);

            var visibleSettings = new List<SettingDefinition>();
            foreach (var setting in source)
            {
                if (settingsObject != null && setting.VisibleWhen != null && !setting.VisibleWhen(settingsObject))
                {
                    continue;
                }

                visibleSettings.Add(setting);
            }

            if (visibleSettings.Count == 0)
            {
                string emptyLabel = NoResultsLabel;

                // If we're in Simple view and nothing matches, hint that Advanced may have results.
                if (viewMode == SettingsViewMode.Simple && !string.IsNullOrWhiteSpace(_searchQuery))
                {
                    var advancedMatches = _hierarchy.Search(_searchQuery, SettingsViewMode.Advanced);
                    if (advancedMatches != null)
                    {
                        foreach (var _ in advancedMatches)
                        {
                            emptyLabel = "Switch to advanced mode for more settings";
                            break;
                        }
                    }
                }

                Widgets.Label(rect, emptyLabel);
                return;
            }

            float viewHeight = visibleSettings.Count * RowHeight;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);

            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float curY = 0f;
            foreach (var def in visibleSettings)
            {
                int depth = _hierarchy.GetDepth(def);
                bool disabledByAncestor = _hierarchy.IsDisabledByAncestor(def, settingsObject);

                Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);
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
    }
}
