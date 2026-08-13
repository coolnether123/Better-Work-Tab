using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Spine.UI.ContextualSettings;
using UnityEngine;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Draws a scrollable list of hierarchical settings with search and view toggles.
    /// </summary>
    [StaticConstructorOnStartup]
    public class SettingsListDrawer
    {
        private const float ResetIconSlotWidth = 26f;
        private const float ResetButtonSize = 20f;
        private const float ClearFilterIconSize = 16f;
        private const float ClearFilterIconGap = 6f;
        private const float FooterHeight = 34f;
        private const float ToolbarGap = 8f;
        private const float FocusHighlightSeconds = 1.45f;
        private const float SearchResultDoubleClickMaxSeconds = 0.25f;
        private const float SearchResultDoubleClickMoveTolerance = 5f;
        private const float SuppressionNoticeHeight = 17f;
        private const float SuppressionLinkGap = 6f;
        private const float SupersessionAffordanceSlotWidth = 22f;
        private const float SupersessionAffordanceSize = 18f;

        private static readonly Texture2D ResetIcon =
            ContentFinder<Texture2D>.Get("UI/Buttons/Dev/Reload");
        private static readonly Texture2D ClearIcon =
            ContentFinder<Texture2D>.Get("UI/Widgets/CloseXSmall");

        private readonly SettingsHierarchy _hierarchy;
        private Vector2 _scrollPosition;
        private string _searchQuery = string.Empty;
        private readonly SettingsSearchWidget _searchWidget = new SettingsSearchWidget();
        private SettingsFilterDefinition _activeFilter;
        private string _pendingFocusSettingId;
        private string _highlightedSettingId;
        private float _highlightStartedAt;
        private SettingsViewMode? _pendingContextViewMode;
        private string _lastSearchClickSettingId;
        private float _lastSearchClickTime = -1f;
        private Vector2 _lastSearchClickPosition;
        private TransferMode _transferMode;
        private readonly HashSet<string> _forceVisibleDisabledAncestorIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private SettingDefinition _hoveredColorPreviewDefinition;
        private object _hoveredColorPreviewSettingsObject;
        private Color _hoveredColorPreviewOriginal;
        private bool _hoveredColorPreviewActive;
        private bool _hoveredColorPreviewObservedThisDraw;

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
        /// Additional guidance appended to color-setting tooltips when live preview is available.
        /// </summary>
        public string ColorPreviewTooltip { get; set; }

        /// <summary>
        /// Optional host-owned target for semantic color previews.
        /// </summary>
        public ISettingColorPreviewSink ColorPreviewSink { get; set; }

        /// <summary>
        /// Optional host-owned transaction for applying a color preview to real setting state.
        /// </summary>
        public ISettingColorPreviewTransactionSink ColorPreviewTransactionSink { get; set; }

        /// <summary>
        /// When true, changed field-backed settings show a small per-row reset button.
        /// </summary>
        public bool ShowResetIcons { get; set; } = true;

        /// <summary>
        /// Tooltip for per-setting reset buttons.
        /// </summary>
        public string ResetToDefaultLabel { get; set; } = "Reset to default";

        /// <summary>Color used for the explanation shown under a suppressed row.</summary>
        public Color SuppressionNoticeColor { get; set; } = new Color(0.72f, 0.66f, 0.44f, 1f);

        /// <summary>Color used for links shown in a suppression explanation.</summary>
        public Color SuppressionLinkColor { get; set; } = new Color(0.62f, 0.76f, 1f, 1f);

        /// <summary>Tooltip shown for a link to the setting suppressing a row.</summary>
        public string SuppressionLinkTooltip { get; set; } = "Go to the setting that is overriding this one.";

        /// <summary>Compact label shown while a row with active supersessions is hovered.</summary>
        public string SupersessionAffordanceLabel { get; set; } = "\u21c6";

        /// <summary>Tooltip for the supersession affordance.</summary>
        public string SupersessionAffordanceTooltip { get; set; } =
            "This setting overrides other settings. Click to see which settings are affected.";

        /// <summary>Description shown above affected-setting choices.</summary>
        public string SupersessionMenuDescription { get; set; } =
            "Choose an affected setting to jump to its row.";

        /// <summary>
        /// Pulse color used when a context jump or search double-click focuses a setting row.
        /// </summary>
        public Color FocusHighlightColor { get; set; } = new Color(1f, 0.78f, 0.18f, 1f);

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

        /// <summary>Optional import/export actions shown below the settings list.</summary>
        public SettingsImportExportActions ImportExportActions { get; set; }

        /// <summary>
        /// Optional callback invoked when a row's tooltip is actually hovered.
        /// </summary>
        public Action<SettingDefinition, object> OnSettingTooltipViewed { get; set; }

        /// <summary>Optional callback invoked when the player clicks a settings row.</summary>
        public Action<SettingDefinition, object> OnSettingInteracted { get; set; }

        /// <summary>
        /// Optional preview callback for non-color rows.
        /// </summary>
        public Action<SettingDefinition, object, object> OnSettingPreview { get; set; }

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
            _scrollPosition = Vector2.zero;
            FocusSetting(targetSettingId);
            _pendingFocusSettingId = targetSettingId;
            ClearSearch();
        }

        internal void PrepareContextNavigation(
            ContextualSettingsTarget requestedTarget,
            object settingsObject)
        {
            ContextualNavigationPlan plan = ContextualNavigationResolver.Resolve(
                requestedTarget,
                id =>
                {
                    SettingDefinition definition = _hierarchy.GetById(id);
                    bool available = definition != null &&
                        (definition.VisibleWhen == null ||
                            definition.VisibleWhen(settingsObject)) &&
                        (definition.ShowInSimpleView ||
                            definition.ShowInAdvancedView);
                    return new ContextualNavigationCandidate(
                        definition?.Id,
                        available,
                        definition?.ShowInSimpleView == true);
                });
            if (plan.IsRoot)
            {
                ClearActiveFilter();
                ClearSearch();
                _scrollPosition = Vector2.zero;
                _pendingFocusSettingId = null;
                _highlightedSettingId = null;
                _pendingContextViewMode = null;
                return;
            }

            ClearActiveFilter();
            ClearSearch();
            _pendingFocusSettingId = plan.TargetId;
            FocusSetting(plan.TargetId);
            _pendingContextViewMode =
                !SettingsPresentationPolicy.ShowViewModes(
                    _hierarchy.SettingCount,
                    _hierarchy.AdvancedOnlySettingCount)
                    ? SettingsViewMode.All
                    : plan.UseSimpleView
                        ? SettingsViewMode.Simple
                        : SettingsViewMode.Advanced;
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

            if (_pendingContextViewMode.HasValue)
            {
                viewMode = _pendingContextViewMode.Value;
                _pendingContextViewMode = null;
            }

            bool drawHeader = ShouldDrawHeader();
            float listStartY = rect.y;
            if (drawHeader)
            {
                const float headerHeight = 30f;
                Rect headerRect = new Rect(
                    rect.x,
                    rect.y,
                    rect.width,
                    headerHeight);
                DrawHeader(headerRect, ref viewMode);
                listStartY = headerRect.yMax + 10f;
            }
            else
            {
                viewMode = SettingsViewMode.All;
                ClearActiveFilter();
                ClearSearch();
            }

            bool drawFooter = ImportExportActions?.HasAnyAction == true;
            float footerSpace = drawFooter ? FooterHeight + 8f : 0f;
            Rect listRect = new Rect(rect.x, listStartY, rect.width, rect.height - (listStartY - rect.y) - footerSpace);
            try
            {
                _hoveredColorPreviewObservedThisDraw = false;
                DrawSettingsList(listRect, settingsObject, ref viewMode, onSettingsChanged);
            }
            finally
            {
                if (!_hoveredColorPreviewObservedThisDraw)
                {
                    EndHoveredColorPreview();
                }

                _hoveredColorPreviewObservedThisDraw = false;
            }

            if (drawFooter)
            {
                DrawImportExportFooter(new Rect(rect.x, rect.yMax - FooterHeight, rect.width, FooterHeight));
            }
        }

        private void DrawHeader(Rect rect, ref SettingsViewMode viewMode)
        {
            bool showSearch = SettingsPresentationPolicy.ShowSearch(
                _hierarchy.SettingCount);
            bool showViewModes = SettingsPresentationPolicy.ShowViewModes(
                _hierarchy.SettingCount,
                _hierarchy.AdvancedOnlySettingCount);
            bool showFilters = SettingsPresentationPolicy.ShowFilters(
                _hierarchy.SettingCount);
            bool hasFilters =
                showFilters &&
                Filters != null &&
                Filters.Count > 0;
            if (!showFilters)
            {
                ClearActiveFilter();
            }

            float toggleWidth = showViewModes ? 200f : 0f;
            float filterWidth = hasFilters ? 150f : 0f;
            int visibleControlCount =
                (showSearch ? 1 : 0) +
                (hasFilters ? 1 : 0) +
                (showViewModes ? 1 : 0);
            float gaps = Mathf.Max(0, visibleControlCount - 1) * ToolbarGap;
            float searchWidth = showSearch
                ? Mathf.Max(120f, rect.width - toggleWidth - filterWidth - gaps)
                : 0f;
            Rect searchRect = new Rect(rect.x, rect.y, searchWidth, rect.height);
            float filterX = showSearch
                ? searchRect.xMax + ToolbarGap
                : rect.x;
            Rect filterRect = new Rect(filterX, rect.y, filterWidth, rect.height);
            Rect toggleRect = new Rect(rect.xMax - 200f, rect.y, 200f, rect.height);

            if (showSearch)
            {
                _searchWidget.OnGUI(searchRect, () => { });
                _searchQuery = _searchWidget.filter.Text ?? string.Empty;
            }
            else
            {
                ClearSearch();
            }

            if (hasFilters)
            {
                DrawFilterButton(filterRect);
            }

            if (showViewModes)
            {
                DrawViewToggle(toggleRect, ref viewMode);
            }
            else
            {
                viewMode = SettingsViewMode.All;
            }
        }

        private bool ShouldDrawHeader()
        {
            return SettingsPresentationPolicy.ShowSearch(
                    _hierarchy.SettingCount) ||
                SettingsPresentationPolicy.ShowFilters(
                    _hierarchy.SettingCount) &&
                Filters != null &&
                Filters.Count > 0 ||
                SettingsPresentationPolicy.ShowViewModes(
                    _hierarchy.SettingCount,
                    _hierarchy.AdvancedOnlySettingCount);
        }

        private void DrawFilterButton(Rect rect)
        {
            string label = _activeFilter != null ? _activeFilter.Label : FilterLabel;
            Event evt = Event.current;
            if (_activeFilter != null &&
                evt != null &&
                evt.type == EventType.MouseDown &&
                evt.button == 1 &&
                rect.Contains(evt.mousePosition))
            {
                ClearActiveFilter();
                evt.Use();
                return;
            }

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
                new FloatMenuOption(AllSettingsFilterLabel, ClearActiveFilter)
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
                options.Add(new FloatMenuOption(localFilter.Label ?? localFilter.Id, () => ApplyFilter(localFilter)));
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
                options.Add(new FloatMenuOption(localFilter.Label ?? localFilter.Id, () => ApplyFilter(localFilter)));
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

        private const float PinnedBandGap = 6f;
        private readonly List<SettingDefinition> _pinnedTop =
            new List<SettingDefinition>();
        private readonly List<SettingDefinition> _pinnedBottom =
            new List<SettingDefinition>();
        private readonly List<SettingDefinition> _scrollingSettings =
            new List<SettingDefinition>();

        /// <summary>
        /// Draws the scrollable list of settings.
        /// </summary>
        private void DrawSettingsList(
            Rect rect,
            object settingsObject,
            ref SettingsViewMode viewMode,
            Action onSettingsChanged)
        {
            bool isSearching = !string.IsNullOrWhiteSpace(_searchQuery);
            if (!string.IsNullOrEmpty(_pendingFocusSettingId))
            {
                RevealDisabledAncestorChain(_pendingFocusSettingId);
            }

            var visibleSettings = BuildVisibleSettings(settingsObject, viewMode, isSearching);

            if (visibleSettings.Count == 0)
            {
                DrawEmptyState(rect, settingsObject, ref viewMode);
                return;
            }

            Rect listRect = PartitionPinnedEntries(
                rect,
                visibleSettings,
                settingsObject,
                onSettingsChanged);

            if (!string.IsNullOrEmpty(_pendingFocusSettingId))
            {
                CenterOnSettingId(_pendingFocusSettingId, _scrollingSettings, settingsObject, listRect.height);
                _pendingFocusSettingId = null;
            }

            float clearFilterRowHeight = _activeFilter != null ? RowHeight + 8f : 0f;
            float viewHeight = MeasureTotalHeight(_scrollingSettings, settingsObject) + clearFilterRowHeight;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(listRect, ref _scrollPosition, viewRect);

            float panelY = 0f;
            for (int index = 0; index < _scrollingSettings.Count; index++)
            {
                SettingDefinition candidate = _scrollingSettings[index];
                if (StartsSectionPanel(candidate))
                {
                    float panelEndY = panelY + MeasureRowHeight(candidate, settingsObject);
                    for (int childIndex = index + 1; childIndex < _scrollingSettings.Count; childIndex++)
                    {
                        SettingDefinition child = _scrollingSettings[childIndex];
                        if (!_hierarchy.GetAncestors(child).Any(ancestor => ancestor.Id == candidate.Id))
                        {
                            break;
                        }

                        panelEndY += MeasureRowHeight(child, settingsObject);
                    }

                    float panelInset = _hierarchy.GetDepth(candidate) * IndentPerLevel;
                    SettingWidgets.DrawSectionPanel(
                        new Rect(
                            panelInset,
                            panelY + 2f,
                            Mathf.Max(0f, viewRect.width - panelInset),
                            Mathf.Max(0f, panelEndY - panelY - 4f)),
                        RowHeight - 4f,
                        candidate.HeaderColor);
                }

                panelY += MeasureRowHeight(candidate, settingsObject);
            }

            float curY = 0f;
            SettingDefinition activeSection = null;
            int activeSectionDepth = 0;
            foreach (var def in _scrollingSettings)
            {
                int depth = _hierarchy.GetDepth(def);
                bool startsSectionPanel = StartsSectionPanel(def);
                bool belongsToActiveSection = !startsSectionPanel && activeSection != null;
                int visualDepth = belongsToActiveSection
                    ? Mathf.Max(depth, activeSectionDepth + 1)
                    : depth;
                bool disabledByAncestor = _hierarchy.IsDisabledByAncestor(def, settingsObject);
                SettingSuppression suppression = def.GetActiveSuppression(settingsObject);
                SettingSuppression inheritedSuppression = suppression ??
                    GetActiveAncestorSuppression(def, settingsObject);
                bool allowFocusedDisabledInteraction =
                    (disabledByAncestor || inheritedSuppression != null) &&
                    IsFocusedForcedVisibleSetting(def);

                float rowHeight = MeasureRowHeight(def, settingsObject);
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowHeight);
                Event evt = Event.current;
                if (evt != null && evt.type == EventType.MouseDown && evt.button == 0 && rowRect.Contains(evt.mousePosition))
                {
                    OnSettingInteracted?.Invoke(def, settingsObject);
                }
                if (isSearching)
                {
                    TryHandleSearchResultDoubleClick(rowRect, def, settingsObject, viewMode, rect.height);
                }

                DrawFocusedSettingHighlight(rowRect, def, visualDepth);
                DrawSettingRow(
                    rowRect,
                    def,
                    settingsObject,
                    (disabledByAncestor || inheritedSuppression != null) &&
                        !allowFocusedDisabledInteraction,
                    suppression,
                    visualDepth,
                    activeSection?.HeaderColor,
                    onSettingsChanged);
                if (startsSectionPanel)
                {
                    activeSection = def;
                    activeSectionDepth = depth;
                }

                curY += rowHeight;
            }

            if (_activeFilter != null)
            {
                DrawClearFilterRow(new Rect(0f, curY + 4f, viewRect.width, RowHeight));
            }

            Widgets.EndScrollView();
        }

        private static bool StartsSectionPanel(SettingDefinition definition)
        {
            return definition != null &&
                (definition.Type == SettingType.Header ||
                 definition.Type == SettingType.Bool &&
                 (definition.EmphasizeAsHeader || definition.ControlsChildVisibility));
        }

        /// <summary>
        /// Splits entries into pinned bands and the scrolling remainder, draws the
        /// bands, and returns the rect the scrolling list should occupy. Pinning is
        /// abandoned wholesale when the bands would consume more than half the page,
        /// so a page can never pin away the list it belongs to.
        /// </summary>
        private Rect PartitionPinnedEntries(
            Rect rect,
            List<SettingDefinition> visibleSettings,
            object settingsObject,
            Action onSettingsChanged)
        {
            _pinnedTop.Clear();
            _pinnedBottom.Clear();
            _scrollingSettings.Clear();

            bool anyPinned = false;
            for (int index = 0; index < visibleSettings.Count; index++)
            {
                if (visibleSettings[index].Pin != SettingPin.None)
                {
                    anyPinned = true;
                    break;
                }
            }

            if (!anyPinned)
            {
                _scrollingSettings.AddRange(visibleSettings);
                return rect;
            }

            for (int index = 0; index < visibleSettings.Count; index++)
            {
                SettingDefinition def = visibleSettings[index];
                switch (def.Pin)
                {
                    case SettingPin.Top:
                        _pinnedTop.Add(def);
                        break;
                    case SettingPin.Bottom:
                        _pinnedBottom.Add(def);
                        break;
                    default:
                        _scrollingSettings.Add(def);
                        break;
                }
            }

            float topHeight = MeasureTotalHeight(_pinnedTop, settingsObject);
            float bottomHeight = MeasureTotalHeight(_pinnedBottom, settingsObject);
            if (topHeight + bottomHeight > rect.height * 0.5f)
            {
                _pinnedTop.Clear();
                _pinnedBottom.Clear();
                _scrollingSettings.Clear();
                _scrollingSettings.AddRange(visibleSettings);
                return rect;
            }

            Rect listRect = rect;
            if (topHeight > 0f)
            {
                DrawPinnedBand(
                    new Rect(rect.x, rect.y, rect.width, topHeight),
                    _pinnedTop,
                    settingsObject,
                    onSettingsChanged);
                Widgets.DrawLineHorizontal(
                    rect.x,
                    rect.y + topHeight + (PinnedBandGap / 2f),
                    rect.width);
                listRect.yMin += topHeight + PinnedBandGap;
            }

            if (bottomHeight > 0f)
            {
                float bandY = rect.yMax - bottomHeight;
                DrawPinnedBand(
                    new Rect(rect.x, bandY, rect.width, bottomHeight),
                    _pinnedBottom,
                    settingsObject,
                    onSettingsChanged);
                Widgets.DrawLineHorizontal(
                    rect.x,
                    bandY - (PinnedBandGap / 2f),
                    rect.width);
                listRect.yMax -= bottomHeight + PinnedBandGap;
            }

            return listRect;
        }

        /// <summary>
        /// Draws pinned entries directly, outside any scroll view.
        /// </summary>
        private void DrawPinnedBand(
            Rect bandRect,
            List<SettingDefinition> entries,
            object settingsObject,
            Action onSettingsChanged)
        {
            float curY = bandRect.y;
            for (int index = 0; index < entries.Count; index++)
            {
                SettingDefinition def = entries[index];
                float rowHeight = MeasureRowHeight(def, settingsObject);
                Rect rowRect = new Rect(
                    bandRect.x,
                    curY,
                    bandRect.width - 16f,
                    rowHeight);
                bool disabledByAncestor =
                    _hierarchy.IsDisabledByAncestor(def, settingsObject);
                SettingSuppression suppression = def.GetActiveSuppression(settingsObject);
                SettingSuppression inheritedSuppression = suppression ??
                    GetActiveAncestorSuppression(def, settingsObject);
                bool allowFocusedDisabledInteraction =
                    (disabledByAncestor || inheritedSuppression != null) &&
                    IsFocusedForcedVisibleSetting(def);

                DrawFocusedSettingHighlight(rowRect, def, _hierarchy.GetDepth(def));
                DrawSettingRow(
                    rowRect,
                    def,
                    settingsObject,
                    (disabledByAncestor || inheritedSuppression != null) &&
                        !allowFocusedDisabledInteraction,
                    suppression,
                    _hierarchy.GetDepth(def),
                    null,
                    onSettingsChanged);
                curY += rowHeight;
            }
        }

        /// <summary>
        /// Draws a single setting row with indentation and disabled state.
        /// </summary>
        private void DrawSettingRow(
            Rect rect,
            SettingDefinition def,
            object settingsObject,
            bool isDisabledByParent,
            SettingSuppression suppression,
            int depth,
            Color? sectionColor,
            Action onSettingsChanged)
        {
            string suppressionReason = suppression?.ResolveReason(settingsObject);
            bool hasNotice = !string.IsNullOrEmpty(suppressionReason);
            Rect controlRow = hasNotice
                ? new Rect(rect.x, rect.y, rect.width, rect.height - SuppressionNoticeHeight)
                : rect;

            float indent = depth * IndentPerLevel;
            Rect contentRect = new Rect(controlRow.x + indent, controlRow.y, controlRow.width - indent, controlRow.height);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(GetPanelRowRect(rect, def, depth));
            }

            bool disabled = isDisabledByParent || suppression != null;
            string label = GetLabel?.Invoke(def) ?? def.Label ?? def.Id;
            string tooltip = BuildTooltip(def);
            if (def.Type == SettingType.Color)
            {
                tooltip = AppendTooltip(tooltip, ColorPreviewTooltip);
            }

            FieldInfo field = null;
            if (!string.IsNullOrEmpty(def.FieldName))
            {
                field = settingsObject.GetType().GetField(def.FieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            bool isHovered = Mouse.IsOver(controlRow);
            IReadOnlyList<SettingSupersession> supersessions = Array.Empty<SettingSupersession>();
            IReadOnlyList<SettingDefinition> supersededSettings = Array.Empty<SettingDefinition>();
            if (isHovered && def.Supersessions != null && def.Supersessions.Count > 0)
            {
                supersessions = def.GetActiveSupersessions(settingsObject);
                supersededSettings = ResolveSupersededSettings(supersessions);
            }

            bool reserveResetSlot = ShowResetIcons && IsResettable(def, field);
            Rect visibleResetRect = default(Rect);
            bool hasVisibleReset = false;
            if (reserveResetSlot)
            {
                Rect resetRect;
                if (depth > 0)
                {
                    resetRect = new Rect(
                        contentRect.x - ResetButtonSize,
                        contentRect.y + ((contentRect.height - ResetButtonSize) / 2f),
                        ResetButtonSize,
                        ResetButtonSize);
                }
                else
                {
                    resetRect = new Rect(
                        contentRect.x,
                        contentRect.y + ((contentRect.height - ResetButtonSize) / 2f),
                        ResetButtonSize,
                        ResetButtonSize);
                    contentRect.x += ResetIconSlotWidth;
                    contentRect.width = Mathf.Max(0f, contentRect.width - ResetIconSlotWidth);
                }

                if (HasNonDefaultValue(field, settingsObject, def))
                {
                    visibleResetRect = resetRect;
                    hasVisibleReset = true;
                    DrawResetButton(resetRect, disabled, () =>
                    {
                        if (ResetSettingToDefault(field, settingsObject, def))
                        {
                            HandleSettingChanged(def, settingsObject, onSettingsChanged);
                        }
                    });
                }
            }

            if (supersededSettings.Count > 0)
            {
                contentRect.width = Mathf.Max(0f, contentRect.width - SupersessionAffordanceSlotWidth);
            }

            if (!disabled && isHovered && def.Type != SettingType.ReadOnly)
            {
                NotifySettingPreview(def, settingsObject, field);
            }

            switch (def.Type)
            {
                case SettingType.Bool:
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        bool boolValue = (bool)field.GetValue(settingsObject);
                        bool changed = def.EmphasizeAsHeader || def.ControlsChildVisibility
                            ? SettingWidgets.DrawHeaderBool(
                                contentRect,
                                label,
                                ref boolValue,
                                sectionColor ?? def.HeaderColor,
                                tooltip,
                                disabled)
                            : SettingWidgets.DrawBool(
                                contentRect,
                                label,
                                ref boolValue,
                                tooltip,
                                disabled);
                        if (changed)
                        {
                            field.SetValue(settingsObject, boolValue);
                            HandleSettingChanged(def, settingsObject, onSettingsChanged);
                            NotifySettingPreview(def, settingsObject, field);
                        }
                    }
                    break;
                case SettingType.Int:
                    if (field != null && field.FieldType == typeof(int))
                    {
                        int intValue = (int)field.GetValue(settingsObject);
                        int min = def.MinValue.HasValue
                            ? Mathf.RoundToInt(def.MinValue.Value)
                            : int.MinValue;
                        int max = def.MaxValue.HasValue
                            ? Mathf.RoundToInt(def.MaxValue.Value)
                            : int.MaxValue;
                        if (min > max)
                        {
                            int temporary = min;
                            min = max;
                            max = temporary;
                        }

                        if (SettingWidgets.DrawInt(contentRect, label, ref intValue, min, max, tooltip, disabled))
                        {
                            field.SetValue(settingsObject, intValue);
                            HandleSettingChanged(def, settingsObject, onSettingsChanged);
                            NotifySettingPreview(def, settingsObject, field);
                        }
                    }
                    break;
                case SettingType.NumericInt:
                    if (field != null && field.FieldType == typeof(int))
                    {
                        int intValue = (int)field.GetValue(settingsObject);
                        int min = def.MinValue.HasValue
                            ? Mathf.RoundToInt(def.MinValue.Value)
                            : int.MinValue;
                        int max = def.MaxValue.HasValue
                            ? Mathf.RoundToInt(def.MaxValue.Value)
                            : int.MaxValue;
                        if (min > max)
                        {
                            int temporary = min;
                            min = max;
                            max = temporary;
                        }

                        if (SettingWidgets.DrawNumericInt(contentRect, label, ref intValue, min, max, tooltip, disabled))
                        {
                            field.SetValue(settingsObject, intValue);
                            HandleSettingChanged(def, settingsObject, onSettingsChanged);
                            NotifySettingPreview(def, settingsObject, field);
                        }
                    }
                    break;
                case SettingType.Float:
                    if (field != null &&
                        (field.FieldType == typeof(float) || field.FieldType == typeof(double)))
                    {
                        float floatValue = Convert.ToSingle(field.GetValue(settingsObject));
                        float min = def.MinValue ?? 0f;
                        float max = def.MaxValue ?? 1f;
                        if (min > max)
                        {
                            float temporary = min;
                            min = max;
                            max = temporary;
                        }

                        if (SettingWidgets.DrawFloat(
                                contentRect,
                                label,
                                ref floatValue,
                                min,
                                max,
                                def.MinLabel,
                                def.MaxLabel,
                                def.ValueFormat,
                                tooltip,
                                disabled))
                        {
                            field.SetValue(settingsObject, field.FieldType == typeof(double)
                                ? (object)(double)floatValue
                                : floatValue);
                            HandleSettingChanged(def, settingsObject, onSettingsChanged);
                            NotifySettingPreview(def, settingsObject, field);
                        }
                    }
                    break;
                case SettingType.Slider:
                    if (field != null && field.FieldType == typeof(float))
                    {
                        float floatValue = (float)field.GetValue(settingsObject);
                        string readout = def.SliderValueFormatter != null
                            ? def.SliderValueFormatter(floatValue)
                            : null;
                        if (SettingWidgets.DrawSlider(
                                contentRect,
                                label,
                                ref floatValue,
                                def.SliderMin,
                                def.SliderMax,
                                readout,
                                tooltip,
                                disabled,
                                def.SliderStep))
                        {
                            field.SetValue(settingsObject, floatValue);
                            HandleSettingChanged(def, settingsObject, onSettingsChanged);
                            NotifySettingPreview(def, settingsObject, field);
                        }
                    }
                    break;
                case SettingType.Color:
                    if (field != null && field.FieldType == typeof(Color))
                    {
                        Color colorValue = (Color)field.GetValue(settingsObject);
                        if (!disabled && isHovered)
                        {
                            ColorPreviewSink?.PreviewHover(def, colorValue);
                            BeginHoveredColorPreview(def, settingsObject, colorValue);
                        }

                        SettingWidgets.DrawColor(contentRect, label, ref colorValue, tooltip, disabled,
                            (current, onSelected) =>
                            {
                                EndHoveredColorPreview();
                                ColorPreviewSink?.BeginPicker(def, current);
                                ColorPreviewTransactionSink?.Begin(def, settingsObject, current);
                                bool previewEnded = false;
                                bool previewRestored = false;
                                bool closeCommitted = false;
                                Color committedColor = current;
                                Action endPreview = () =>
                                {
                                    if (previewEnded)
                                    {
                                        return;
                                    }

                                    previewEnded = true;
                                    ColorPreviewSink?.EndPicker(def);
                                };
                                Action restorePreview = () =>
                                {
                                    if (previewRestored)
                                    {
                                        return;
                                    }

                                    previewRestored = true;
                                    ColorPreviewTransactionSink?.Restore(
                                        def,
                                        settingsObject,
                                        committedColor);
                                };
                                var dialog = new Spine.UI.ColourPicker.Dialog_ColourPicker(current, (newColor, closing) =>
                                {
                                    field.SetValue(settingsObject, newColor);
                                    HandleSettingChanged(def, settingsObject, onSettingsChanged);
                                    ColorPreviewTransactionSink?.Commit(def, settingsObject, newColor);
                                    committedColor = newColor;
                                    closeCommitted = closing;
                                    onSelected?.Invoke(newColor);
                                }, previewCallback: newColor =>
                                {
                                    ColorPreviewSink?.PreviewPicker(def, newColor);
                                    ColorPreviewTransactionSink?.Preview(def, settingsObject, newColor);
                                });
                                dialog.onCancel = () =>
                                {
                                    restorePreview();
                                    endPreview();
                                };
                                dialog.onPostClose = () =>
                                {
                                    if (!closeCommitted)
                                    {
                                        restorePreview();
                                    }

                                    endPreview();
                                };

                                Find.WindowStack.Add(dialog);
                            }, EditColorLabel);
                    }
                    break;
                case SettingType.Enum:
                    if ((field != null || def.ValueGetter != null) && def.EnumType != null)
                    {
                        object current = def.ValueGetter != null
                            ? def.ValueGetter(settingsObject)
                            : field.GetValue(settingsObject);
                        SettingWidgets.DrawEnum(contentRect, label, current, def.EnumType, tooltip, disabled, selected =>
                        {
                            if (def.ValueSetter != null)
                            {
                                def.ValueSetter(settingsObject, selected);
                            }
                            else
                            {
                                field.SetValue(settingsObject, selected);
                            }
                            HandleSettingChanged(def, settingsObject, onSettingsChanged);
                            NotifySettingPreview(def, settingsObject, field);
                        }, def.EnumLabelProvider, def.EnumDescriptionProvider);
                    }
                    break;
                case SettingType.Button:
                    if (SettingWidgets.DrawButton(contentRect, label, tooltip, disabled))
                    {
                        HandleSettingChanged(def, settingsObject, onSettingsChanged);
                        NotifySettingPreview(def, settingsObject, field);
                    }
                    break;
                case SettingType.Header:
                    Color previousColor = GUI.color;
                    if (disabled)
                    {
                        GUI.color = Color.gray;
                    }

                    SettingWidgets.DrawHeader(contentRect, label, sectionColor ?? def.HeaderColor);
                    GUI.color = previousColor;
                    break;
                case SettingType.Spacer:
                    SettingWidgets.DrawSpacer(contentRect);
                    break;
                case SettingType.DropdownListAdder:
                    SettingWidgets.DrawDropdownListAdder(
                        contentRect,
                        label,
                        def.DropdownOptionsProvider,
                        def.OnOptionAdded,
                        tooltip,
                        disabled);
                    break;
                case SettingType.Custom:
                    if (def.CustomDrawer != null &&
                        def.CustomDrawer(contentRect, label, tooltip, settingsObject, disabled))
                    {
                        HandleSettingChanged(def, settingsObject, onSettingsChanged);
                        NotifySettingPreview(def, settingsObject, null);
                    }
                    break;
                case SettingType.ReadOnly:
                    SettingWidgets.DrawReadOnly(
                        contentRect,
                        label,
                        def.ReadOnlyValueProvider?.Invoke(settingsObject),
                        tooltip,
                        disabled);
                    break;
            }

            if (supersededSettings.Count > 0 && isHovered)
            {
                Rect affordanceRect = new Rect(
                    controlRow.xMax - SupersessionAffordanceSize - 2f,
                    controlRow.center.y - (SupersessionAffordanceSize / 2f),
                    SupersessionAffordanceSize,
                    SupersessionAffordanceSize);
                DrawSupersessionAffordance(
                    affordanceRect,
                    def,
                    supersessions,
                    supersededSettings,
                    settingsObject);
            }

            if (hasNotice)
            {
                DrawSuppressionNotice(
                    new Rect(
                        controlRow.x + indent,
                        controlRow.yMax,
                        Mathf.Max(0f, rect.width - indent),
                        SuppressionNoticeHeight),
                    suppression,
                    suppressionReason,
                    settingsObject);
            }

            if (!string.IsNullOrEmpty(tooltip) &&
                !DescribedFloatMenu.AnyOpen &&
                (!hasVisibleReset || !Mouse.IsOver(visibleResetRect)))
            {
                TooltipHandler.TipRegion(controlRow, tooltip);
                if (Mouse.IsOver(controlRow))
                {
                    OnSettingTooltipViewed?.Invoke(def, settingsObject);
                }
            }
        }

        private void NotifySettingPreview(
            SettingDefinition definition,
            object settingsObject,
            FieldInfo field)
        {
            if (OnSettingPreview == null || definition == null || definition.Type == SettingType.Color)
            {
                return;
            }

            object currentValue = definition.ValueGetter != null
                ? definition.ValueGetter(settingsObject)
                : field != null
                    ? field.GetValue(settingsObject)
                    : settingsObject;
            OnSettingPreview(definition, settingsObject, currentValue);
        }

        private IReadOnlyList<SettingDefinition> ResolveSupersededSettings(
            IReadOnlyList<SettingSupersession> supersessions)
        {
            if (supersessions == null || supersessions.Count == 0)
            {
                return Array.Empty<SettingDefinition>();
            }

            var resolved = new List<SettingDefinition>(supersessions.Count);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < supersessions.Count; i++)
            {
                SettingSupersession supersession = supersessions[i];
                if (supersession == null || string.IsNullOrEmpty(supersession.SupersededSettingId) ||
                    !seenIds.Add(supersession.SupersededSettingId))
                {
                    continue;
                }

                SettingDefinition target = _hierarchy.GetById(supersession.SupersededSettingId);
                if (target != null)
                {
                    resolved.Add(target);
                }
            }

            return resolved;
        }

        private void DrawSupersessionAffordance(
            Rect rect,
            SettingDefinition source,
            IReadOnlyList<SettingSupersession> supersessions,
            IReadOnlyList<SettingDefinition> supersededSettings,
            object settingsObject)
        {
            string tooltip = SupersessionAffordanceTooltip;
            var affectedLabels = new List<string>(supersededSettings.Count);
            for (int i = 0; i < supersededSettings.Count; i++)
            {
                affectedLabels.Add(GetSettingLabel(supersededSettings[i]));
            }

            if (affectedLabels.Count > 0)
            {
                tooltip = AppendTooltip(tooltip, string.Join(", ", affectedLabels));
            }

            TooltipHandler.TipRegion(rect, tooltip);
            if (Widgets.ButtonText(rect, SupersessionAffordanceLabel))
            {
                OpenSupersessionMenu(source, supersessions, supersededSettings, settingsObject);
                Event.current?.Use();
            }
        }

        private void OpenSupersessionMenu(
            SettingDefinition source,
            IReadOnlyList<SettingSupersession> supersessions,
            IReadOnlyList<SettingDefinition> supersededSettings,
            object settingsObject)
        {
            var options = new List<FloatMenuOption>(supersededSettings.Count);
            var descriptions = new Dictionary<FloatMenuOption, string>();
            for (int i = 0; i < supersededSettings.Count; i++)
            {
                SettingDefinition target = supersededSettings[i];
                SettingSupersession supersession = FindSupersession(supersessions, target.Id);
                string label = supersession?.LinkLabel ?? GetSettingLabel(target);
                SettingDefinition localTarget = target;
                var option = new FloatMenuOption(
                    label,
                    () => JumpToSetting(localTarget, settingsObject));
                options.Add(option);
                descriptions[option] = string.IsNullOrEmpty(supersession?.Description)
                    ? $"{GetSettingLabel(source)} currently takes precedence over {GetSettingLabel(target)}."
                    : supersession.Description;
            }

            if (options.Count == 0)
            {
                return;
            }

            Find.WindowStack.Add(new DescribedFloatMenu(
                options,
                null,
                GetSettingLabel(source),
                SupersessionMenuDescription,
                descriptions));
        }

        private static SettingSupersession FindSupersession(
            IReadOnlyList<SettingSupersession> supersessions,
            string targetId)
        {
            if (supersessions == null || string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            for (int i = 0; i < supersessions.Count; i++)
            {
                SettingSupersession supersession = supersessions[i];
                if (supersession != null &&
                    string.Equals(supersession.SupersededSettingId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    return supersession;
                }
            }

            return null;
        }

        private string GetSettingLabel(SettingDefinition definition)
        {
            return GetLabel?.Invoke(definition) ?? definition?.Label ?? definition?.Id ?? string.Empty;
        }

        private void BeginHoveredColorPreview(
            SettingDefinition definition,
            object settingsObject,
            Color color)
        {
            if (ColorPreviewTransactionSink == null)
            {
                return;
            }

            _hoveredColorPreviewObservedThisDraw = true;
            if (!_hoveredColorPreviewActive ||
                !ReferenceEquals(_hoveredColorPreviewDefinition, definition) ||
                !ReferenceEquals(_hoveredColorPreviewSettingsObject, settingsObject))
            {
                EndHoveredColorPreview();
                _hoveredColorPreviewDefinition = definition;
                _hoveredColorPreviewSettingsObject = settingsObject;
                _hoveredColorPreviewOriginal = color;
                _hoveredColorPreviewActive = true;
                ColorPreviewTransactionSink.Begin(definition, settingsObject, color);
            }

            ColorPreviewTransactionSink.Preview(definition, settingsObject, color);
        }

        private void EndHoveredColorPreview()
        {
            if (!_hoveredColorPreviewActive)
            {
                return;
            }

            ColorPreviewTransactionSink?.Restore(
                _hoveredColorPreviewDefinition,
                _hoveredColorPreviewSettingsObject,
                _hoveredColorPreviewOriginal);
            _hoveredColorPreviewDefinition = null;
            _hoveredColorPreviewSettingsObject = null;
            _hoveredColorPreviewActive = false;
        }

        private static string AppendTooltip(string tooltip, string addition)
        {
            if (string.IsNullOrEmpty(addition))
            {
                return tooltip;
            }

            return string.IsNullOrEmpty(tooltip)
                ? addition
                : tooltip + "\n\n" + addition;
        }

        private float MeasureRowHeight(SettingDefinition def, object settingsObject)
        {
            SettingSuppression suppression = def?.GetActiveSuppression(settingsObject);
            bool hasNotice = suppression != null &&
                !string.IsNullOrEmpty(suppression.ResolveReason(settingsObject));
            return hasNotice ? RowHeight + SuppressionNoticeHeight : RowHeight;
        }

        private float MeasureTotalHeight(List<SettingDefinition> settings, object settingsObject)
        {
            float total = 0f;
            for (int i = 0; i < settings.Count; i++)
            {
                total += MeasureRowHeight(settings[i], settingsObject);
            }

            return total;
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

        private SettingSuppression GetActiveAncestorSuppression(
            SettingDefinition setting,
            object settingsObject)
        {
            foreach (SettingDefinition ancestor in _hierarchy.GetAncestors(setting))
            {
                SettingSuppression suppression = ancestor.GetActiveSuppression(settingsObject);
                if (suppression != null)
                {
                    return suppression;
                }
            }

            return null;
        }

        private static bool HasExternalSuppressionAction(SettingSuppression suppression)
        {
            return suppression != null &&
                !string.IsNullOrEmpty(suppression.ExternalActionUrl);
        }

        private void DrawSuppressionNotice(
            Rect rect,
            SettingSuppression suppression,
            string reason,
            object settingsObject)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;

            try
            {
                float reasonWidth = Mathf.Min(Text.CalcSize(reason).x, rect.width);
                Rect reasonRect = new Rect(rect.x, rect.y, reasonWidth, rect.height);
                GUI.color = SuppressionNoticeColor;
                Widgets.Label(reasonRect, reason);

                if (HasExternalSuppressionAction(suppression))
                {
                    string actionLabel = string.IsNullOrEmpty(suppression.ExternalActionLabel)
                        ? "Open"
                        : suppression.ExternalActionLabel;
                    float actionWidth = Text.CalcSize(actionLabel).x;
                    float actionX = reasonRect.xMax + SuppressionLinkGap;
                    if (actionX + actionWidth <= rect.xMax)
                    {
                        Rect actionRect = new Rect(actionX, rect.y, actionWidth, rect.height);
                        bool hovered = Mouse.IsOver(actionRect);
                        GUI.color = hovered ? Color.white : SuppressionLinkColor;
                        bool clicked = Widgets.ButtonText(
                            actionRect,
                            actionLabel,
                            drawBackground: false,
                            doMouseoverSound: true,
                            textColor: GUI.color,
                            active: true
#if RWT_BUTTON_TEXT_OVERRIDE_TEXT_ANCHOR
                            ,
                            overrideTextAnchor: TextAnchor.MiddleLeft);
#else
                            );
#endif
                        Widgets.DrawLineHorizontal(
                            actionRect.x,
                            actionRect.yMax - 3f,
                            actionWidth);
                        if (!string.IsNullOrEmpty(suppression.ExternalActionTooltip))
                        {
                            TooltipHandler.TipRegion(actionRect, suppression.ExternalActionTooltip);
                        }

                        if (clicked)
                        {
                            SteamUtility.OpenUrl(suppression.ExternalActionUrl);
                        }
                    }
                }

                SettingDefinition suppressor = _hierarchy.GetById(suppression.SuppressorSettingId);
                if (suppressor == null)
                {
                    return;
                }

                string linkText = suppression.LinkLabel ??
                    GetLabel?.Invoke(suppressor) ?? suppressor.Label ?? suppressor.Id;
                float linkWidth = Text.CalcSize(linkText).x;
                float linkX = rect.x + reasonWidth + SuppressionLinkGap;
                if (linkX + linkWidth > rect.xMax)
                {
                    return;
                }

                Rect linkRect = new Rect(linkX, rect.y, linkWidth, rect.height);
                bool linkHovered = Mouse.IsOver(linkRect);
                GUI.color = linkHovered ? Color.white : SuppressionLinkColor;
                Widgets.Label(linkRect, linkText);
                Widgets.DrawLineHorizontal(linkRect.x, linkRect.yMax - 3f, linkWidth);
                TooltipHandler.TipRegion(linkRect, SuppressionLinkTooltip);
                if (Widgets.ButtonInvisible(linkRect))
                {
                    JumpToSuppressor(suppressor, settingsObject);
                    Event.current?.Use();
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private void JumpToSuppressor(
            SettingDefinition suppressor,
            object settingsObject)
        {
            JumpToSetting(suppressor, settingsObject);
        }

        private void JumpToSetting(
            SettingDefinition target,
            object settingsObject)
        {
            if (target == null)
            {
                return;
            }

            ClearSearch();
            if (_activeFilter != null &&
                !MatchesFilter(target, settingsObject, _activeFilter))
            {
                ClearActiveFilter();
            }

            RevealDisabledAncestorChain(target.Id);
            _pendingFocusSettingId = target.Id;
            FocusSetting(target.Id);
        }

        private List<SettingDefinition> BuildVisibleSettings(
            object settingsObject,
            SettingsViewMode viewMode,
            bool useSearch)
        {
            return BuildVisibleSettings(settingsObject, viewMode, useSearch, _activeFilter, false);
        }

        private List<SettingDefinition> BuildVisibleSettings(
            object settingsObject,
            SettingsViewMode viewMode,
            bool useSearch,
            SettingsFilterDefinition filter,
            bool ignoreFilter)
        {
            IEnumerable<SettingDefinition> source = useSearch
                ? _hierarchy.Search(_searchQuery, viewMode, settingsObject, GetLabel, GetTooltip)
                : _hierarchy.GetFlattenedForView(viewMode, settingsObject);

            var visibleSettings = new List<SettingDefinition>();
            foreach (var setting in source)
            {
                if (settingsObject != null && setting.VisibleWhen != null && !setting.VisibleWhen(settingsObject))
                {
                    continue;
                }

                if (!ignoreFilter && !MatchesFilter(setting, settingsObject, filter))
                {
                    continue;
                }

                if (!useSearch &&
                    _hierarchy.IsDisabledByAncestor(setting, settingsObject) &&
                    !IsForcedVisibleThroughDisabledAncestor(setting))
                {
                    continue;
                }

                visibleSettings.Add(setting);
            }

            return visibleSettings;
        }

        private bool MatchesFilter(SettingDefinition setting, object settingsObject, SettingsFilterDefinition filter)
        {
            if (filter == null)
            {
                return true;
            }

            if (filter.Matches(setting, settingsObject))
            {
                return true;
            }

            if (!filter.IncludeChildrenOfMatches)
            {
                return false;
            }

            var parent = _hierarchy.GetParent(setting);
            while (parent != null)
            {
                if (filter.Matches(parent, settingsObject))
                {
                    return true;
                }

                parent = _hierarchy.GetParent(parent);
            }

            return false;
        }

        private void DrawEmptyState(Rect rect, object settingsObject, ref SettingsViewMode viewMode)
        {
            EmptyStateAction action = GetEmptyStateAction(settingsObject, viewMode);
            if (action != null)
            {
                if (DrawClickableEmptyState(rect, action.Label))
                {
                    if (action.SwitchToViewMode.HasValue)
                    {
                        viewMode = action.SwitchToViewMode.Value;
                    }

                    action.Action?.Invoke();
                }
                return;
            }

            Widgets.Label(rect, NoResultsLabel);
        }

        private EmptyStateAction GetEmptyStateAction(object settingsObject, SettingsViewMode viewMode)
        {
            bool isSearching = !string.IsNullOrWhiteSpace(_searchQuery);
            if (!isSearching)
            {
                if (_activeFilter != null)
                {
                    return new EmptyStateAction("Remove filter for more settings", ClearActiveFilter);
                }

                return null;
            }

            SettingsFilterDefinition suggestedFilter = FindSuggestedFilter(settingsObject, viewMode);
            if (suggestedFilter != null)
            {
                return new EmptyStateAction(
                    $"Change to {suggestedFilter.Label ?? suggestedFilter.Id} filter for those settings",
                    () => ApplySuggestedFilter(suggestedFilter, settingsObject, viewMode));
            }

            if (_activeFilter != null)
            {
                var unfilteredMatches = BuildVisibleSettings(
                    settingsObject,
                    viewMode,
                    useSearch: true,
                    filter: null,
                    ignoreFilter: true);
                if (unfilteredMatches.Count > 0)
                {
                    return new EmptyStateAction("Remove filter for more settings", ClearActiveFilter);
                }
            }

            if (viewMode == SettingsViewMode.Simple)
            {
                var advancedMatches = BuildVisibleSettings(
                    settingsObject,
                    SettingsViewMode.Advanced,
                    useSearch: true,
                    filter: _activeFilter,
                    ignoreFilter: false);
                if (advancedMatches.Count > 0)
                {
                    return EmptyStateAction.SwitchView(
                        "Switch to advanced mode for more settings",
                        SettingsViewMode.Advanced);
                }

                if (_activeFilter != null)
                {
                    var advancedUnfilteredMatches = BuildVisibleSettings(
                        settingsObject,
                        SettingsViewMode.Advanced,
                        useSearch: true,
                        filter: null,
                        ignoreFilter: true);
                    if (advancedUnfilteredMatches.Count > 0)
                    {
                        return new EmptyStateAction("Remove filter for more settings", ClearActiveFilter);
                    }
                }
            }

            return null;
        }

        private SettingsFilterDefinition FindSuggestedFilter(object settingsObject, SettingsViewMode viewMode)
        {
            if (Filters == null || Filters.Count == 0 || string.IsNullOrWhiteSpace(_searchQuery))
            {
                return null;
            }

            foreach (var filter in Filters)
            {
                if (filter == null || ReferenceEquals(filter, _activeFilter))
                {
                    continue;
                }

                if (FilterTextMatchesSearch(filter))
                {
                    return filter;
                }
            }

            foreach (var filter in Filters)
            {
                if (filter == null || ReferenceEquals(filter, _activeFilter))
                {
                    continue;
                }

                var matches = BuildVisibleSettings(
                    settingsObject,
                    viewMode,
                    useSearch: true,
                    filter: filter,
                    ignoreFilter: false);
                if (matches.Count > 0)
                {
                    return filter;
                }
            }

            return null;
        }

        private bool FilterTextMatchesSearch(SettingsFilterDefinition filter)
        {
            string needle = (_searchQuery ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(needle) || filter == null)
            {
                return false;
            }

            string text = $"{filter.Id} {filter.Label} {filter.Tooltip} {filter.Category} {filter.CategoryLabel}".ToLowerInvariant();
            return text.Contains(needle);
        }

        private void ApplySuggestedFilter(
            SettingsFilterDefinition filter,
            object settingsObject,
            SettingsViewMode viewMode)
        {
            ApplyFilter(filter);
            var matchesWithSearch = BuildVisibleSettings(
                settingsObject,
                viewMode,
                useSearch: true,
                filter: filter,
                ignoreFilter: false);
            if (matchesWithSearch.Count == 0)
            {
                ClearSearch();
            }
        }

        private bool DrawClickableEmptyState(Rect rect, string label)
        {
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, 28f);
            Color oldColor = GUI.color;
            Event evt = Event.current;
            bool hovered = evt != null && labelRect.Contains(evt.mousePosition);
            GUI.color = hovered ? Color.white : new Color(0.8f, 0.85f, 1f);
            Widgets.Label(labelRect, label);
            Vector2 size = Text.CalcSize(label);
            float underlineWidth = Mathf.Min(size.x, labelRect.width);
            Widgets.DrawLineHorizontal(labelRect.x, labelRect.y + size.y + 1f, underlineWidth);
            GUI.color = oldColor;

            if (Widgets.ButtonInvisible(labelRect))
            {
                Event.current?.Use();
                return true;
            }

            return false;
        }

        private void DrawClearFilterRow(Rect rect)
        {
            const string label = "Clear filter";
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Vector2 labelSize = Text.CalcSize(label);
            float width = ClearFilterIconSize + ClearFilterIconGap + labelSize.x;
            Rect buttonRect = new Rect(
                rect.center.x - (width / 2f),
                rect.y + ((rect.height - Mathf.Max(ClearFilterIconSize, labelSize.y)) / 2f),
                width,
                Mathf.Max(ClearFilterIconSize, labelSize.y));
            Rect iconRect = new Rect(
                buttonRect.x,
                buttonRect.center.y - (ClearFilterIconSize / 2f),
                ClearFilterIconSize,
                ClearFilterIconSize);
            Rect labelRect = new Rect(
                iconRect.xMax + ClearFilterIconGap,
                buttonRect.y,
                labelSize.x,
                buttonRect.height);

            Rect hitRect = new Rect(
                buttonRect.x - 4f,
                buttonRect.y - 4f,
                buttonRect.width + 8f,
                buttonRect.height + 8f);
            Widgets.DrawHighlightIfMouseover(hitRect);
            GUI.DrawTexture(iconRect, ClearIcon);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            if (Widgets.ButtonInvisible(hitRect, true))
            {
                ClearActiveFilter();
                Event.current?.Use();
            }
        }

        private void ApplyFilter(SettingsFilterDefinition filter)
        {
            _activeFilter = filter;
            _scrollPosition = Vector2.zero;
        }

        private void ClearActiveFilter()
        {
            if (_activeFilter == null)
            {
                return;
            }

            ApplyFilter(null);
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
                evt.button != 0 ||
                !rowRect.Contains(evt.mousePosition))
            {
                return false;
            }

            string targetId = target?.Id;
            float now = Time.realtimeSinceStartup;
            bool sameTarget = !string.IsNullOrEmpty(targetId) &&
                string.Equals(_lastSearchClickSettingId, targetId, StringComparison.OrdinalIgnoreCase);
            bool quickEnough = _lastSearchClickTime >= 0f &&
                now - _lastSearchClickTime <= SearchResultDoubleClickMaxSeconds;
            bool closeEnough =
                Vector2.Distance(_lastSearchClickPosition, evt.mousePosition) <= SearchResultDoubleClickMoveTolerance;

            if (!sameTarget || !quickEnough || !closeEnough || evt.clickCount != 2)
            {
                _lastSearchClickSettingId = targetId;
                _lastSearchClickTime = now;
                _lastSearchClickPosition = evt.mousePosition;
                return false;
            }

            _lastSearchClickSettingId = null;
            _lastSearchClickTime = -1f;
            RevealDisabledAncestorChain(target.Id);
            CenterOnSetting(target, settingsObject, viewMode, listHeight);
            FocusSetting(target?.Id);
            ClearSearch();
            evt.Use();
            return true;
        }

        private void ClearSearch()
        {
            _searchWidget.Reset();
            _searchWidget.Unfocus();
            _searchQuery = string.Empty;
            _lastSearchClickSettingId = null;
            _lastSearchClickTime = -1f;
        }

        private void FocusSetting(string settingId)
        {
            if (string.IsNullOrEmpty(settingId))
            {
                return;
            }

            _highlightedSettingId = settingId;
            _highlightStartedAt = Time.realtimeSinceStartup;
        }

        private void RevealDisabledAncestorChain(string settingId)
        {
            SettingDefinition setting = _hierarchy.GetById(settingId);
            if (setting == null)
            {
                return;
            }

            foreach (SettingDefinition ancestor in _hierarchy.GetAncestors(setting))
            {
                if (!ancestor.ControlsChildVisibility || ancestor.Type != SettingType.Bool)
                {
                    continue;
                }

                _forceVisibleDisabledAncestorIds.Add(ancestor.Id);
            }
        }

        private bool IsForcedVisibleThroughDisabledAncestor(SettingDefinition setting)
        {
            if (setting == null || _forceVisibleDisabledAncestorIds.Count == 0)
            {
                return false;
            }

            foreach (SettingDefinition ancestor in _hierarchy.GetAncestors(setting))
            {
                if (_forceVisibleDisabledAncestorIds.Contains(ancestor.Id))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsFocusedForcedVisibleSetting(SettingDefinition setting)
        {
            return setting != null &&
                !string.IsNullOrEmpty(_highlightedSettingId) &&
                string.Equals(setting.Id, _highlightedSettingId, StringComparison.OrdinalIgnoreCase) &&
                IsForcedVisibleThroughDisabledAncestor(setting);
        }

        private void HandleSettingChanged(
            SettingDefinition changedSetting,
            object settingsObject,
            Action onSettingsChanged)
        {
            ClearForcedVisibilityForControlledChildren(changedSetting);
            bool ancestorsChanged = EnableControllingAncestors(changedSetting, settingsObject);
            changedSetting?.OnChanged?.Invoke(settingsObject);
            onSettingsChanged?.Invoke();

            if (ancestorsChanged)
            {
                _forceVisibleDisabledAncestorIds.Clear();
            }
        }

        private void DrawImportExportFooter(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
            Rect contentRect = rect.ContractedBy(2f);
            contentRect.y += 4f;
            contentRect.height -= 4f;

            if (_transferMode == TransferMode.None)
            {
                const float buttonWidth = 110f;
                Rect exportRect = new Rect(contentRect.x, contentRect.y, buttonWidth, contentRect.height);
                Rect importRect = new Rect(exportRect.xMax + 6f, contentRect.y, buttonWidth, contentRect.height);
                if ((ImportExportActions.ExportToFile != null || ImportExportActions.ExportToClipboard != null) &&
                    Widgets.ButtonText(exportRect, ImportExportActions.ExportLabel))
                {
                    _transferMode = TransferMode.Export;
                }

                if ((ImportExportActions.ImportFromFile != null || ImportExportActions.ImportFromClipboard != null) &&
                    Widgets.ButtonText(importRect, ImportExportActions.ImportLabel))
                {
                    _transferMode = TransferMode.Import;
                }

                return;
            }

            bool exporting = _transferMode == TransferMode.Export;
            Rect labelRect = new Rect(contentRect.x, contentRect.y + 5f, 80f, contentRect.height);
            Widgets.Label(labelRect, (exporting ? ImportExportActions.ExportLabel : ImportExportActions.ImportLabel) + ":");
            const float optionWidth = 110f;
            Rect fileRect = new Rect(labelRect.xMax + 4f, contentRect.y, optionWidth, contentRect.height);
            Rect clipboardRect = new Rect(fileRect.xMax + 6f, contentRect.y, optionWidth, contentRect.height);
            Rect cancelRect = new Rect(clipboardRect.xMax + 6f, contentRect.y, optionWidth, contentRect.height);
            Action fileAction = exporting ? ImportExportActions.ExportToFile : ImportExportActions.ImportFromFile;
            Action clipboardAction = exporting ? ImportExportActions.ExportToClipboard : ImportExportActions.ImportFromClipboard;

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

        private bool EnableControllingAncestors(SettingDefinition setting, object settingsObject)
        {
            if (setting == null || settingsObject == null)
            {
                return false;
            }

            bool changed = false;
            foreach (SettingDefinition ancestor in _hierarchy.GetAncestors(setting))
            {
                if (!ancestor.ControlsChildVisibility ||
                    ancestor.Type != SettingType.Bool ||
                    string.IsNullOrEmpty(ancestor.FieldName))
                {
                    continue;
                }

                FieldInfo field = settingsObject.GetType().GetField(
                    ancestor.FieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool) || (bool)field.GetValue(settingsObject))
                {
                    continue;
                }

                field.SetValue(settingsObject, true);
                ancestor.OnChanged?.Invoke(settingsObject);
                changed = true;
            }

            return changed;
        }

        private void ClearForcedVisibilityForControlledChildren(SettingDefinition setting)
        {
            if (setting == null || !setting.ControlsChildVisibility || setting.Type != SettingType.Bool)
            {
                return;
            }

            _forceVisibleDisabledAncestorIds.Remove(setting.Id);
            ClearForcedVisibilityForDescendants(setting.Id);
        }

        private void ClearForcedVisibilityForDescendants(string parentId)
        {
            var children = _hierarchy.GetChildren(parentId);
            for (int i = 0; i < children.Count; i++)
            {
                SettingDefinition child = children[i];
                if (child == null)
                {
                    continue;
                }

                _forceVisibleDisabledAncestorIds.Remove(child.Id);
                ClearForcedVisibilityForDescendants(child.Id);
            }
        }

        private void DrawFocusedSettingHighlight(Rect rowRect, SettingDefinition def, int depth)
        {
            if (def == null ||
                string.IsNullOrEmpty(_highlightedSettingId) ||
                !string.Equals(def.Id, _highlightedSettingId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            float age = Time.realtimeSinceStartup - _highlightStartedAt;
            if (!ContextualPresentationMath.IsHighlightActive(
                Time.realtimeSinceStartup,
                _highlightStartedAt,
                FocusHighlightSeconds))
            {
                _highlightedSettingId = null;
                return;
            }

            float fade = 1f - Mathf.Clamp01(age / FocusHighlightSeconds);
            float pulse = 0.5f + (0.5f * Mathf.Sin(age * 16f));
            Color focusColor = FocusHighlightColor;
            Color oldColor = GUI.color;
            GUI.color = new Color(focusColor.r, focusColor.g, focusColor.b, Mathf.Lerp(0.18f, 0.36f, pulse) * fade);
            Rect highlightRect = GetPanelRowRect(rowRect, def, depth);
            Widgets.DrawBoxSolid(highlightRect, GUI.color);
            GUI.color = new Color(focusColor.r, focusColor.g, focusColor.b, 0.85f * fade);
            Widgets.DrawBox(highlightRect, 2);
            GUI.color = oldColor;
        }

        private Rect GetPanelRowRect(Rect rowRect, SettingDefinition def, int depth)
        {
            int panelDepth = def.Type == SettingType.Header
                ? depth
                : Mathf.Max(0, depth - 1);
            float inset = panelDepth * IndentPerLevel;
            return new Rect(
                rowRect.x + inset + 1f,
                rowRect.y,
                Mathf.Max(0f, rowRect.width - inset - 2f),
                rowRect.height);
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
            ScrollToIndex(fullList, settingsObject, index, listHeight);
        }

        private void CenterOnSettingId(
            string settingId,
            List<SettingDefinition> visibleSettings,
            object settingsObject,
            float listHeight)
        {
            if (string.IsNullOrEmpty(settingId) || visibleSettings == null)
            {
                return;
            }

            int index = visibleSettings.FindIndex(def =>
                string.Equals(def.Id, settingId, StringComparison.OrdinalIgnoreCase));
            ScrollToIndex(visibleSettings, settingsObject, index, listHeight);
        }

        /// <summary>
        /// Scrolls so the row at <paramref name="index"/> sits in the middle of the viewport.
        /// Rows are not uniform height, so offsets are accumulated rather than multiplied.
        /// </summary>
        private void ScrollToIndex(
            List<SettingDefinition> settings,
            object settingsObject,
            int index,
            float listHeight)
        {
            if (settings == null || index < 0 || index >= settings.Count)
            {
                return;
            }

            float targetY = 0f;
            float viewHeight = 0f;
            for (int i = 0; i < settings.Count; i++)
            {
                float rowHeight = MeasureRowHeight(settings[i], settingsObject);
                if (i < index)
                {
                    targetY += rowHeight;
                }

                viewHeight += rowHeight;
            }

            _scrollPosition.y = ContextualPresentationMath.CenteredScroll(
                targetY,
                listHeight,
                RowHeight,
                viewHeight);
            _scrollPosition.x = 0f;
        }

        private bool IsResettable(SettingDefinition def, FieldInfo field)
        {
            if (def == null)
            {
                return false;
            }

            if (def.Type == SettingType.Custom)
            {
                return def.CustomReset != null && def.CustomHasNonDefaultValue != null;
            }

            if (def.ValueGetter != null && def.ValueSetter != null)
            {
                return def.DefaultValue != null && def.Type == SettingType.Enum;
            }

            if (field == null || def.DefaultValue == null)
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
                case SettingType.Slider:
                    return true;
                default:
                    return false;
            }
        }

        private bool HasNonDefaultValue(FieldInfo field, object settingsObject, SettingDefinition def)
        {
            if (settingsObject == null || def == null)
            {
                return false;
            }

            if (def.Type == SettingType.Custom)
            {
                return def.CustomHasNonDefaultValue?.Invoke(settingsObject) ?? false;
            }

            if (def.ValueGetter != null)
            {
                return !ValuesEqual(def.ValueGetter(settingsObject), def.DefaultValue);
            }

            if (field == null)
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

            bool clicked = Widgets.ButtonImage(rect, ResetIcon);

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
            if (settingsObject == null || def == null)
            {
                return false;
            }

            if (def.Type == SettingType.Custom)
            {
                if (def.CustomReset == null)
                {
                    return false;
                }

                def.CustomReset(settingsObject);
                return true;
            }

            if (def.ValueSetter != null && def.DefaultValue != null)
            {
                def.ValueSetter(settingsObject, def.DefaultValue);
                return true;
            }

            if (field == null)
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


        private sealed class EmptyStateAction
        {
            internal readonly string Label;
            internal readonly Action Action;
            internal readonly SettingsViewMode? SwitchToViewMode;

            internal EmptyStateAction(string label, Action action)
            {
                Label = label;
                Action = action;
            }

            private EmptyStateAction(string label, SettingsViewMode switchToViewMode)
            {
                Label = label;
                SwitchToViewMode = switchToViewMode;
            }

            internal static EmptyStateAction SwitchView(string label, SettingsViewMode viewMode)
            {
                return new EmptyStateAction(label, viewMode);
            }
        }
    }

    /// <summary>
    /// Small local search control so the settings framework does not depend on
    /// RimWorld's version-specific QuickSearchWidget API.
    /// </summary>
    internal sealed class SettingsSearchWidget
    {
        internal readonly SettingsSearchFilter filter = new SettingsSearchFilter();

        internal void OnGUI(Rect rect, Action onChanged)
        {
            string value = Widgets.TextField(rect, filter.Text ?? string.Empty);
            if (!string.Equals(value, filter.Text, StringComparison.Ordinal))
            {
                filter.Text = value;
                onChanged?.Invoke();
            }
        }

        internal void Reset()
        {
            filter.Text = string.Empty;
        }

        internal void Unfocus()
        {
        }
    }

    internal enum TransferMode
    {
        None,
        Export,
        Import
    }

    internal sealed class SettingsSearchFilter
    {
        internal string Text { get; set; } = string.Empty;
    }
}
