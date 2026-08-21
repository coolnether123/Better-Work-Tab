using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Better_Work_Tab;
using HarmonyLib;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Adapts the pure BWT search index to the shared settings drawer without
    /// changing the Spine public ABI. The drawer keeps its native search
    /// rendering; this adapter supplies the all-settings context and local
    /// correction learning around it.
    /// </summary>
    internal static class BWTSettingsAdaptiveSearchAliases
    {
        private const string AliasFileName = "BetterWorkTab.settings-search-aliases.v1";
        private const float AdvancedNoticeHeight = 32f;
        private const float AdvancedNoticeGap = 6f;

        private static readonly FieldInfo SearchQueryField =
            typeof(SettingsListDrawer).GetField(
                "_searchQuery",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ActiveFilterField =
            typeof(SettingsListDrawer).GetField(
                "_activeFilter",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<SettingsListDrawer, Observation> Observations =
            new Dictionary<SettingsListDrawer, Observation>();
        private static readonly Dictionary<SettingDefinition, string[]> BaselineKeywords =
            new Dictionary<SettingDefinition, string[]>();

        private static BWTSettingsAdaptiveAliasModel _model =
            new BWTSettingsAdaptiveAliasModel();
        private static IReadOnlyList<SettingDefinition> _appliedDefinitions;
        private static BWTSettingsSearchIndex _cachedIndex;
        private static IReadOnlyList<SettingDefinition> _cachedIndexDefinitions;
        private static int _cachedIndexRevision = -1;
        private static bool _loaded;
        private static bool _reflectionWarningShown;

        internal static int ActiveAliasCount => _model.ActiveCount;
        internal static int PendingAliasCount => _model.PendingCount;

        internal static void Initialize(IReadOnlyList<SettingDefinition> definitions)
        {
            if (!_loaded)
            {
                Load();
                _loaded = true;
            }

            ApplyToDefinitions(definitions);
        }

        internal static void Observe(SettingsListDrawer drawer, object settingsObject)
        {
            if (drawer == null || settingsObject == null)
            {
                return;
            }

            Initialize(BWTSettingsRegistry.Definitions);
            if (!TryReadSearchQuery(drawer, out string query))
            {
                return;
            }

            SettingsViewMode viewMode = ReadViewMode(settingsObject);
            BWTSettingsSearchIndex index = GetIndex();
            SettingsFilterDefinition filter = ReadActiveFilter(drawer);
            IReadOnlyList<BWTSettingsSearchMatch> matches = GetFilteredMatches(
                index,
                query,
                viewMode,
                settingsObject,
                filter);

            if (!Observations.TryGetValue(drawer, out Observation observation))
            {
                observation = new Observation();
                Observations[drawer] = observation;
            }

            bool queryChanged = !string.Equals(observation.Query, query, StringComparison.Ordinal);
            if (queryChanged &&
                BWTSettingsSearchText.IsMeaningful(BWTSettingsSearchText.Normalize(observation.Query)) &&
                observation.MatchIds.Count == 0 &&
                matches.Count > 0)
            {
                string source = observation.Query;
                string token = (++observation.TransitionNumber).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                if (_model.RegisterTransition(
                        source,
                        query,
                        matches.Select(match => match.SettingId)))
                {
                    observation.PendingSourceQuery = BWTSettingsSearchText.Normalize(source);
                    observation.PendingTargetQuery = BWTSettingsSearchText.Normalize(query);
                    observation.PendingSettingIds = matches
                        .Select(match => match.SettingId)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    observation.PendingConfirmationToken = token;
                    Persist();
                }
            }

            observation.Query = query;
            observation.ViewMode = viewMode;
            observation.MatchIds = matches
                .Select(match => match.SettingId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        internal static void ConfirmInteraction(
            SettingsListDrawer drawer,
            SettingDefinition definition)
        {
            if (drawer == null || definition == null ||
                !Observations.TryGetValue(drawer, out Observation observation) ||
                string.IsNullOrEmpty(observation.PendingSourceQuery) ||
                !string.Equals(
                    BWTSettingsSearchText.Normalize(observation.Query),
                    observation.PendingTargetQuery,
                    StringComparison.Ordinal) ||
                !observation.PendingSettingIds.Contains(definition.Id, StringComparer.Ordinal))
            {
                return;
            }

            if (_model.Confirm(
                    observation.PendingSourceQuery,
                    observation.PendingTargetQuery,
                    definition.Id,
                    observation.PendingConfirmationToken))
            {
                Persist();
                ApplyToDefinitions(BWTSettingsRegistry.Definitions);
                _cachedIndex = null;
            }
        }

        internal static bool TryGetAdvancedOnlyNotice(
            SettingsListDrawer drawer,
            object settingsObject,
            SettingsViewMode viewMode,
            out BWTSettingsAdvancedSearchNotice notice)
        {
            notice = null;
            if (drawer == null || settingsObject == null || viewMode != SettingsViewMode.Simple ||
                !TryReadSearchQuery(drawer, out string query) ||
                string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            BWTSettingsSearchIndex index = GetIndex();
            SettingsFilterDefinition filter = ReadActiveFilter(drawer);
            IReadOnlyList<BWTSettingsSearchMatch> simpleMatches = GetFilteredMatches(
                index,
                query,
                BWTSettingsSearchView.Simple,
                settingsObject,
                filter);
            IReadOnlyList<BWTSettingsSearchMatch> advancedMatches = GetFilteredAdvancedOnlyMatches(
                index,
                query,
                settingsObject,
                filter);
            if (advancedMatches.Count == 0)
            {
                return false;
            }

            BWTSettingsSearchMatch first = advancedMatches[0];
            notice = new BWTSettingsAdvancedSearchNotice(
                simpleMatches.Count,
                advancedMatches.Count,
                first.Label,
                first.Context);
            return true;
        }

        internal static bool DrawAdvancedOnlyNotice(
            Rect rect,
            BWTSettingsAdvancedSearchNotice notice)
        {
            if (notice == null || rect.height <= 0f)
            {
                return false;
            }

            string first = string.IsNullOrEmpty(notice.FirstLabel)
                ? "BWT_Settings_Search_AdvancedSetting".Translate()
                : notice.FirstLabel;
            string suffix = notice.MatchCount > 1
                ? "BWT_Settings_Search_MatchCount".Translate(notice.MatchCount)
                : string.Empty;
            string label = string.Format(
                BWTSettingsTranslation.AdvancedSearchNotice,
                first + suffix);
            Rect buttonRect = new Rect(
                rect.x,
                rect.y,
                rect.width,
                Mathf.Min(AdvancedNoticeHeight, rect.height));
            bool clicked = Widgets.ButtonText(buttonRect, label);
            string tooltip = string.IsNullOrEmpty(notice.Context)
                ? "BWT_Settings_Search_HiddenInSimple".Translate()
                : "BWT_Settings_Search_AdvancedContext".Translate(notice.Context);
            TooltipHandler.TipRegion(buttonRect, tooltip);
            return clicked;
        }

        internal static float AdvancedNoticeReservedHeight => AdvancedNoticeHeight + AdvancedNoticeGap;

        internal static bool DrawAliasStatus(
            Rect rect,
            string label,
            string tooltip,
            object settingsObject,
            bool disabled)
        {
            Color oldColor = GUI.color;
            if (disabled)
            {
                GUI.color = Color.gray;
            }

            const float buttonWidth = 86f;
            Rect buttonRect = new Rect(
                rect.xMax - buttonWidth,
                rect.y + 2f,
                buttonWidth,
                Mathf.Max(0f, rect.height - 4f));
            Rect valueRect = new Rect(
                buttonRect.x - 174f,
                rect.y,
                168f,
                rect.height);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, valueRect.x - rect.x - 6f),
                rect.height);
            Widgets.Label(labelRect, label ?? string.Empty);
            string status = ActiveAliasCount == 0 && PendingAliasCount == 0
                ? "BWT_Settings_Search_None".Translate()
                : "BWT_Settings_Search_AliasStatus".Translate(ActiveAliasCount, PendingAliasCount);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, status);
            Text.Anchor = oldAnchor;

            bool clicked = !disabled && Widgets.ButtonText(buttonRect, "BWT_Settings_Search_Reset".Translate());
            if (clicked)
            {
                Reset();
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(
                    rect,
                    tooltip + " " + "BWT_Settings_Search_AliasPrivacy".Translate());
            }

            GUI.color = oldColor;
            return clicked;
        }

        internal static void Reset()
        {
            _model.Reset();
            Persist();
            ApplyToDefinitions(BWTSettingsRegistry.Definitions);
            _cachedIndex = null;
            Observations.Clear();
        }

        private static BWTSettingsSearchIndex GetIndex()
        {
            IReadOnlyList<SettingDefinition> definitions = BWTSettingsRegistry.Definitions;
            if (_cachedIndex != null &&
                ReferenceEquals(_cachedIndexDefinitions, definitions) &&
                _cachedIndexRevision == _model.Revision)
            {
                return _cachedIndex;
            }

            SettingsHierarchy hierarchy = BWTSettingsRegistry.Hierarchy;
            var documents = new List<BWTSettingsSearchDocument>();
            foreach (SettingDefinition definition in definitions)
            {
                if (definition == null ||
                    (!definition.ShowInSimpleView && !definition.ShowInAdvancedView))
                {
                    continue;
                }

                documents.Add(new BWTSettingsSearchDocument(
                    definition.Id,
                    BWTSettingsTranslation.GetLabel(definition),
                    BWTSettingsTranslation.GetTooltip(definition),
                    definition.SearchKeywords,
                    BuildContext(hierarchy, definition),
                    definition.ShowInSimpleView,
                    definition.ShowInAdvancedView,
                    definition.SortOrder));
            }

            _cachedIndex = new BWTSettingsSearchIndex(documents);
            _cachedIndexDefinitions = definitions;
            _cachedIndexRevision = _model.Revision;
            return _cachedIndex;
        }

        private static string BuildContext(
            SettingsHierarchy hierarchy,
            SettingDefinition definition)
        {
            var parts = new List<string>();
            foreach (SettingDefinition ancestor in hierarchy.GetAncestors(definition))
            {
                parts.Insert(
                    0,
                    (BWTSettingsTranslation.GetLabel(ancestor) ?? string.Empty) +
                    " " + ancestor.Id);
            }

            return string.Join(" > ", parts.ToArray());
        }

        private static IReadOnlyList<BWTSettingsSearchMatch> GetFilteredMatches(
            BWTSettingsSearchIndex index,
            string query,
            SettingsViewMode viewMode,
            object settingsObject,
            SettingsFilterDefinition filter)
        {
            BWTSettingsSearchView searchView = viewMode == SettingsViewMode.Simple
                ? BWTSettingsSearchView.Simple
                : BWTSettingsSearchView.Advanced;
            return GetFilteredMatches(index, query, searchView, settingsObject, filter);
        }

        private static IReadOnlyList<BWTSettingsSearchMatch> GetFilteredMatches(
            BWTSettingsSearchIndex index,
            string query,
            BWTSettingsSearchView view,
            object settingsObject,
            SettingsFilterDefinition filter)
        {
            var matches = new List<BWTSettingsSearchMatch>();
            foreach (BWTSettingsSearchMatch match in index.Search(query, view))
            {
                SettingDefinition definition = BWTSettingsRegistry.Hierarchy.GetById(match.SettingId);
                if (definition != null &&
                    (definition.VisibleWhen == null || definition.VisibleWhen(settingsObject)) &&
                    MatchesFilter(definition, settingsObject, filter))
                {
                    matches.Add(match);
                }
            }

            return matches;
        }

        private static IReadOnlyList<BWTSettingsSearchMatch> GetFilteredAdvancedOnlyMatches(
            BWTSettingsSearchIndex index,
            string query,
            object settingsObject,
            SettingsFilterDefinition filter)
        {
            var matches = new List<BWTSettingsSearchMatch>();
            foreach (BWTSettingsSearchMatch match in index.SearchAdvancedOnly(query))
            {
                SettingDefinition definition = BWTSettingsRegistry.Hierarchy.GetById(match.SettingId);
                if (definition != null &&
                    (definition.VisibleWhen == null || definition.VisibleWhen(settingsObject)) &&
                    MatchesFilter(definition, settingsObject, filter))
                {
                    matches.Add(match);
                }
            }

            return matches;
        }

        private static bool MatchesFilter(
            SettingDefinition definition,
            object settingsObject,
            SettingsFilterDefinition filter)
        {
            if (filter == null || filter.Matches(definition, settingsObject))
            {
                return true;
            }

            if (!filter.IncludeChildrenOfMatches)
            {
                return false;
            }

            SettingDefinition parent = BWTSettingsRegistry.Hierarchy.GetParent(definition);
            while (parent != null)
            {
                if (filter.Matches(parent, settingsObject))
                {
                    return true;
                }

                parent = BWTSettingsRegistry.Hierarchy.GetParent(parent);
            }

            return false;
        }

        private static SettingsViewMode ReadViewMode(object settingsObject)
        {
            if (settingsObject is BetterWorkTabSettings settings &&
                settings.settingsViewMode == BetterWorkTabSettings.SettingsViewMode.Simple)
            {
                return SettingsViewMode.Simple;
            }

            return SettingsViewMode.Advanced;
        }

        private static bool TryReadSearchQuery(
            SettingsListDrawer drawer,
            out string query)
        {
            query = string.Empty;
            if (SearchQueryField == null)
            {
                WarnReflectionUnavailable("search query");
                return false;
            }

            try
            {
                query = SearchQueryField.GetValue(drawer) as string ?? string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                WarnReflectionUnavailable("search query value: " + ex.GetType().Name);
                return false;
            }
        }

        private static SettingsFilterDefinition ReadActiveFilter(SettingsListDrawer drawer)
        {
            if (ActiveFilterField == null)
            {
                return null;
            }

            try
            {
                return ActiveFilterField.GetValue(drawer) as SettingsFilterDefinition;
            }
            catch (Exception ex)
            {
                WarnReflectionUnavailable("active filter value: " + ex.GetType().Name);
                return null;
            }
        }

        private static void ApplyToDefinitions(IReadOnlyList<SettingDefinition> definitions)
        {
            if (definitions == null)
            {
                return;
            }

            _appliedDefinitions = definitions;
            var current = new HashSet<SettingDefinition>();
            var byId = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
            foreach (SettingDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id))
                {
                    continue;
                }

                current.Add(definition);
                byId[definition.Id] = definition;
                if (!BaselineKeywords.ContainsKey(definition))
                {
                    BaselineKeywords[definition] = CopyKeywords(definition.SearchKeywords);
                }

                definition.SearchKeywords = CopyKeywords(BaselineKeywords[definition]);
            }

            var stale = BaselineKeywords.Keys
                .Where(definition => !current.Contains(definition))
                .ToList();
            foreach (SettingDefinition definition in stale)
            {
                BaselineKeywords.Remove(definition);
            }

            foreach (AliasSnapshot alias in _model.GetActiveSnapshots())
            {
                foreach (string settingId in alias.SettingIds)
                {
                    if (!byId.TryGetValue(settingId, out SettingDefinition definition))
                    {
                        continue;
                    }

                    var keywords = new List<string>(definition.SearchKeywords ?? new string[0]);
                    if (!keywords.Contains(alias.SourceQuery, StringComparer.Ordinal))
                    {
                        keywords.Add(alias.SourceQuery);
                        keywords.Sort(StringComparer.Ordinal);
                        definition.SearchKeywords = keywords.ToArray();
                    }
                }
            }
        }

        private static string[] CopyKeywords(IEnumerable<string> keywords)
        {
            return (keywords ?? new string[0])
                .Where(keyword => !string.IsNullOrEmpty(keyword))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(keyword => keyword, StringComparer.Ordinal)
                .ToArray();
        }

        private static void Load()
        {
            string path = GetAliasFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                _model.Load(File.ReadAllLines(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log.Warning("[Better Work Tab] Settings search aliases could not be loaded: " + ex.GetType().Name);
                _model.Reset();
            }
        }

        private static void Persist()
        {
            string path = GetAliasFilePath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory))
                {
                    return;
                }

                Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                File.WriteAllLines(temporary, _model.ExportLines(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Better Work Tab] Settings search aliases could not be saved: " + ex.GetType().Name);
            }
        }

        private static string GetAliasFilePath()
        {
            try
            {
                string config = GenFilePaths.ConfigFolderPath;
                return string.IsNullOrEmpty(config)
                    ? null
                    : Path.Combine(config, AliasFileName);
            }
            catch
            {
                return null;
            }
        }

        private static void WarnReflectionUnavailable(string detail)
        {
            if (_reflectionWarningShown)
            {
                return;
            }

            _reflectionWarningShown = true;
            Log.Warning("[Better Work Tab] Settings search context disabled because the shared drawer shape changed (" + detail + ").");
        }

        private sealed class Observation
        {
            internal string Query = string.Empty;
            internal SettingsViewMode ViewMode = SettingsViewMode.Simple;
            internal List<string> MatchIds = new List<string>();
            internal string PendingSourceQuery;
            internal string PendingTargetQuery;
            internal List<string> PendingSettingIds = new List<string>();
            internal string PendingConfirmationToken;
            internal int TransitionNumber;
        }
    }

    internal sealed class BWTSettingsAdvancedSearchNotice
    {
        internal BWTSettingsAdvancedSearchNotice(
            int simpleMatchCount,
            int matchCount,
            string firstLabel,
            string context)
        {
            SimpleMatchCount = simpleMatchCount;
            MatchCount = matchCount;
            FirstLabel = firstLabel;
            Context = context;
        }

        internal int SimpleMatchCount { get; }
        internal int MatchCount { get; }
        internal string FirstLabel { get; }
        internal string Context { get; }
    }

    [HarmonyPatch]
    internal static class Patch_SettingsListDrawer_DrawSettingsList_BWTSearch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(SettingsListDrawer),
                "DrawSettingsList");
        }

        [HarmonyPrefix]
        private static bool Prefix(
            SettingsListDrawer __instance,
            ref Rect rect,
            object settingsObject,
            ref SettingsViewMode viewMode)
        {
            if (!BWTSettingsAdaptiveSearchAliases.TryGetAdvancedOnlyNotice(
                    __instance,
                    settingsObject,
                    viewMode,
                    out BWTSettingsAdvancedSearchNotice notice))
            {
                return true;
            }

            bool clicked = BWTSettingsAdaptiveSearchAliases.DrawAdvancedOnlyNotice(rect, notice);
            if (clicked)
            {
                viewMode = SettingsViewMode.Advanced;
                return true;
            }

            if (notice.SimpleMatchCount == 0)
            {
                // The notice is the complete empty-state surface. The native
                // drawer's generic filter suggestion would otherwise obscure
                // the fact that the result exists in Advanced.
                return false;
            }

            rect.yMin += BWTSettingsAdaptiveSearchAliases.AdvancedNoticeReservedHeight;
            return true;
        }
    }
}
