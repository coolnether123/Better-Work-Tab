using System;
using System.Collections.Generic;
using System.Linq;

namespace Better_Work_Tab.UI.Settings
{
    internal enum BWTSettingsSearchView
    {
        Simple,
        Advanced
    }

    /// <summary>
    /// Runtime-neutral search metadata for one BWT setting. The UI adapter
    /// supplies translated labels and the hierarchy context; this type keeps
    /// search behavior deterministic and testable without RimWorld.
    /// </summary>
    internal sealed class BWTSettingsSearchDocument
    {
        internal BWTSettingsSearchDocument(
            string id,
            string label,
            string tooltip,
            IEnumerable<string> keywords,
            string context,
            bool showInSimpleView,
            bool showInAdvancedView,
            int order)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            Tooltip = tooltip ?? string.Empty;
            Keywords = CopyStrings(keywords);
            Context = context ?? string.Empty;
            ShowInSimpleView = showInSimpleView;
            ShowInAdvancedView = showInAdvancedView;
            Order = order;
        }

        internal string Id { get; }
        internal string Label { get; }
        internal string Tooltip { get; }
        internal IReadOnlyList<string> Keywords { get; }
        internal string Context { get; }
        internal bool ShowInSimpleView { get; }
        internal bool ShowInAdvancedView { get; }
        internal int Order { get; }

        private static IReadOnlyList<string> CopyStrings(IEnumerable<string> values)
        {
            var copy = new List<string>();
            foreach (string value in values ?? new string[0])
            {
                if (!string.IsNullOrEmpty(value))
                {
                    copy.Add(value);
                }
            }

            return copy;
        }
    }

    internal sealed class BWTSettingsSearchMatch
    {
        internal BWTSettingsSearchMatch(BWTSettingsSearchDocument document)
        {
            Document = document;
        }

        internal BWTSettingsSearchDocument Document { get; }
        internal string SettingId => Document.Id;
        internal string Label => Document.Label;
        internal string Context => Document.Context;
        internal bool IsAdvancedOnly =>
            !Document.ShowInSimpleView && Document.ShowInAdvancedView;
    }

    internal static class BWTSettingsSearchText
    {
        internal static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            char[] buffer = new char[text.Length];
            int length = 0;
            foreach (char character in text)
            {
                if (char.IsWhiteSpace(character) ||
                    character == '-' ||
                    character == '_' ||
                    character == '\u2010' ||
                    character == '\u2011' ||
                    character == '\u2012' ||
                    character == '\u2013' ||
                    character == '\u2014')
                {
                    continue;
                }

                buffer[length++] = char.ToLowerInvariant(character);
            }

            return new string(buffer, 0, length);
        }

        internal static bool IsMeaningful(string normalizedText)
        {
            if (string.IsNullOrEmpty(normalizedText) || normalizedText.Length < 3)
            {
                return false;
            }

            for (int index = 0; index < normalizedText.Length; index++)
            {
                if (char.IsLetterOrDigit(normalizedText[index]))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool Matches(string text, string rawNeedle, string normalizedNeedle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string haystack = text.ToLowerInvariant();
            return haystack.Contains(rawNeedle) ||
                !string.IsNullOrEmpty(normalizedNeedle) &&
                Normalize(text).Contains(normalizedNeedle);
        }
    }

    /// <summary>
    /// Indexes every reachable BWT setting, including Advanced-only entries.
    /// Advanced is a superset of Simple, matching the shared drawer contract.
    /// </summary>
    internal sealed class BWTSettingsSearchIndex
    {
        private readonly List<BWTSettingsSearchDocument> _documents;

        internal BWTSettingsSearchIndex(IEnumerable<BWTSettingsSearchDocument> documents)
        {
            _documents = new List<BWTSettingsSearchDocument>();
            foreach (BWTSettingsSearchDocument document in documents ?? new BWTSettingsSearchDocument[0])
            {
                if (document != null &&
                    !string.IsNullOrEmpty(document.Id) &&
                    (document.ShowInSimpleView || document.ShowInAdvancedView))
                {
                    _documents.Add(document);
                }
            }

            _documents.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
            });
        }

        internal IReadOnlyList<BWTSettingsSearchMatch> Search(
            string query,
            BWTSettingsSearchView view)
        {
            string rawNeedle = (query ?? string.Empty).Trim().ToLowerInvariant();
            string normalizedNeedle = BWTSettingsSearchText.Normalize(rawNeedle);
            var matches = new List<BWTSettingsSearchMatch>();

            foreach (BWTSettingsSearchDocument document in _documents)
            {
                if (!IsVisibleInView(document, view))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(rawNeedle) ||
                    MatchesDocument(document, rawNeedle, normalizedNeedle))
                {
                    matches.Add(new BWTSettingsSearchMatch(document));
                }
            }

            return matches;
        }

        internal IReadOnlyList<BWTSettingsSearchMatch> SearchAdvancedOnly(string query)
        {
            var matches = new List<BWTSettingsSearchMatch>();
            foreach (BWTSettingsSearchMatch match in Search(query, BWTSettingsSearchView.Advanced))
            {
                if (match.IsAdvancedOnly)
                {
                    matches.Add(match);
                }
            }

            return matches;
        }

        private static bool IsVisibleInView(
            BWTSettingsSearchDocument document,
            BWTSettingsSearchView view)
        {
            return view == BWTSettingsSearchView.Simple
                ? document.ShowInSimpleView
                : document.ShowInSimpleView || document.ShowInAdvancedView;
        }

        private static bool MatchesDocument(
            BWTSettingsSearchDocument document,
            string rawNeedle,
            string normalizedNeedle)
        {
            if (BWTSettingsSearchText.Matches(document.Id, rawNeedle, normalizedNeedle) ||
                BWTSettingsSearchText.Matches(document.Label, rawNeedle, normalizedNeedle) ||
                BWTSettingsSearchText.Matches(document.Tooltip, rawNeedle, normalizedNeedle) ||
                BWTSettingsSearchText.Matches(document.Context, rawNeedle, normalizedNeedle))
            {
                return true;
            }

            foreach (string keyword in document.Keywords)
            {
                if (BWTSettingsSearchText.Matches(keyword, rawNeedle, normalizedNeedle))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Persistent-state-free model for adaptive search aliases. The runtime
    /// wrapper owns the local file; this model owns validation, threshold,
    /// deterministic ordering, and bounds.
    /// </summary>
    internal sealed class BWTSettingsAdaptiveAliasModel
    {
        internal const int RequiredConfirmations = 3;
        internal const int MaxEntries = 64;
        internal const int MaxSettingIdsPerEntry = 8;
        internal const int MaxQueryLength = 96;
        internal const int MaxSettingIdLength = 160;

        private readonly SortedDictionary<string, AliasEntry> _entries =
            new SortedDictionary<string, AliasEntry>(StringComparer.Ordinal);

        internal int EntryCount => _entries.Count;
        internal int ActiveCount => CountEntries(active: true);
        internal int PendingCount => CountEntries(active: false);
        internal int Revision { get; private set; }

        internal bool RegisterTransition(
            string earlierWording,
            string replacementQuery,
            IEnumerable<string> settingIds)
        {
            string source = BWTSettingsSearchText.Normalize(earlierWording);
            string target = BWTSettingsSearchText.Normalize(replacementQuery);
            List<string> ids = NormalizeIds(settingIds);
            if (!IsValidQuery(source) ||
                !IsValidQuery(target) ||
                string.Equals(source, target, StringComparison.Ordinal) ||
                ids.Count == 0)
            {
                return false;
            }

            if (_entries.TryGetValue(source, out AliasEntry existing) &&
                existing.IsActive)
            {
                return false;
            }

            if (existing != null &&
                string.Equals(existing.TargetQuery, target, StringComparison.Ordinal) &&
                SameIds(existing.SettingIds, ids))
            {
                // The same failed-to-corrected rewrite can be confirmed across
                // separate deliberate interactions. Preserve its progress, but
                // open a new confirmation token for the new transition.
                existing.LastConfirmationToken = null;
                Revision++;
                return true;
            }

            if (existing == null)
            {
                if (_entries.Count >= MaxEntries && !EvictPendingForCapacity())
                {
                    return false;
                }

                existing = new AliasEntry(source);
                _entries[source] = existing;
            }

            existing.TargetQuery = target;
            existing.SettingIds = ids;
            existing.Confirmations = 0;
            existing.LastConfirmationToken = null;
            Revision++;
            return true;
        }

        internal bool Confirm(
            string earlierWording,
            string replacementQuery,
            string settingId,
            string confirmationToken)
        {
            string source = BWTSettingsSearchText.Normalize(earlierWording);
            string target = BWTSettingsSearchText.Normalize(replacementQuery);
            string normalizedId = NormalizeSettingId(settingId);
            if (!IsValidQuery(source) ||
                !IsValidQuery(target) ||
                string.IsNullOrEmpty(normalizedId) ||
                !_entries.TryGetValue(source, out AliasEntry entry) ||
                entry.IsActive ||
                !string.Equals(entry.TargetQuery, target, StringComparison.Ordinal) ||
                !entry.SettingIds.Contains(normalizedId, StringComparer.Ordinal) ||
                string.IsNullOrEmpty(confirmationToken) ||
                string.Equals(entry.LastConfirmationToken, confirmationToken, StringComparison.Ordinal))
            {
                return false;
            }

            entry.LastConfirmationToken = confirmationToken;
            entry.Confirmations = Math.Min(RequiredConfirmations, entry.Confirmations + 1);
            Revision++;
            return true;
        }

        internal bool IsActiveAliasFor(string earlierWording, string settingId)
        {
            string source = BWTSettingsSearchText.Normalize(earlierWording);
            string normalizedId = NormalizeSettingId(settingId);
            return IsValidQuery(source) &&
                !string.IsNullOrEmpty(normalizedId) &&
                _entries.TryGetValue(source, out AliasEntry entry) &&
                entry.IsActive &&
                entry.SettingIds.Contains(normalizedId, StringComparer.Ordinal);
        }

        internal IReadOnlyList<AliasSnapshot> GetSnapshots()
        {
            var snapshots = new List<AliasSnapshot>();
            foreach (AliasEntry entry in _entries.Values)
            {
                snapshots.Add(new AliasSnapshot(
                    entry.SourceQuery,
                    entry.TargetQuery,
                    entry.SettingIds,
                    entry.Confirmations));
            }

            return snapshots;
        }

        internal IReadOnlyList<AliasSnapshot> GetActiveSnapshots()
        {
            var snapshots = new List<AliasSnapshot>();
            foreach (AliasEntry entry in _entries.Values)
            {
                if (entry.IsActive)
                {
                    snapshots.Add(new AliasSnapshot(
                        entry.SourceQuery,
                        entry.TargetQuery,
                        entry.SettingIds,
                        entry.Confirmations));
                }
            }

            return snapshots;
        }

        internal IReadOnlyList<string> ExportLines()
        {
            var lines = new List<string> { "BWT_SETTINGS_SEARCH_ALIASES_V1" };
            foreach (AliasEntry entry in _entries.Values)
            {
                var encodedIds = new List<string>();
                foreach (string settingId in entry.SettingIds)
                {
                    encodedIds.Add(Encode(settingId));
                }

                lines.Add(string.Join(
                    "\t",
                    "A",
                    Encode(entry.SourceQuery),
                    entry.Confirmations.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Encode(entry.TargetQuery),
                    string.Join(",", encodedIds.ToArray())));
            }

            return lines;
        }

        internal void Load(IEnumerable<string> lines)
        {
            Reset();
            bool headerSeen = false;
            foreach (string line in lines ?? new string[0])
            {
                if (!headerSeen)
                {
                    headerSeen = string.Equals(
                        line,
                        "BWT_SETTINGS_SEARCH_ALIASES_V1",
                        StringComparison.Ordinal);
                    continue;
                }

                string[] parts = (line ?? string.Empty).Split('\t');
                if (parts.Length != 5 || !string.Equals(parts[0], "A", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!int.TryParse(
                        parts[2],
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int confirmations))
                {
                    continue;
                }

                string source = Decode(parts[1]);
                string target = Decode(parts[3]);
                var ids = new List<string>();
                foreach (string encodedId in parts[4].Split(','))
                {
                    string id = Decode(encodedId);
                    if (!string.IsNullOrEmpty(id))
                    {
                        ids.Add(id);
                    }
                }

                if (!TryImportEntry(source, target, ids, confirmations))
                {
                    continue;
                }
            }

            Revision++;
        }

        internal void Reset()
        {
            _entries.Clear();
            Revision++;
        }

        private int CountEntries(bool active)
        {
            int count = 0;
            foreach (AliasEntry entry in _entries.Values)
            {
                if (entry.IsActive == active)
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryImportEntry(
            string sourceText,
            string targetText,
            IEnumerable<string> settingIds,
            int confirmations)
        {
            string source = BWTSettingsSearchText.Normalize(sourceText);
            string target = BWTSettingsSearchText.Normalize(targetText);
            List<string> ids = NormalizeIds(settingIds);
            if (!IsValidQuery(source) ||
                !IsValidQuery(target) ||
                string.Equals(source, target, StringComparison.Ordinal) ||
                ids.Count == 0 ||
                !_entries.ContainsKey(source) && _entries.Count >= MaxEntries)
            {
                return false;
            }

            var entry = new AliasEntry(source)
            {
                TargetQuery = target,
                SettingIds = ids,
                Confirmations = Math.Max(0, Math.Min(RequiredConfirmations, confirmations))
            };
            _entries[source] = entry;
            return true;
        }

        private bool EvictPendingForCapacity()
        {
            string candidate = null;
            foreach (KeyValuePair<string, AliasEntry> pair in _entries)
            {
                if (!pair.Value.IsActive &&
                    (candidate == null || string.Compare(pair.Key, candidate, StringComparison.Ordinal) > 0))
                {
                    candidate = pair.Key;
                }
            }

            if (candidate == null)
            {
                return false;
            }

            _entries.Remove(candidate);
            return true;
        }

        private static List<string> NormalizeIds(IEnumerable<string> settingIds)
        {
            var unique = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string settingId in settingIds ?? new string[0])
            {
                string normalized = NormalizeSettingId(settingId);
                if (!string.IsNullOrEmpty(normalized))
                {
                    unique.Add(normalized);
                }

                if (unique.Count >= MaxSettingIdsPerEntry)
                {
                    break;
                }
            }

            return new List<string>(unique);
        }

        private static string NormalizeSettingId(string settingId)
        {
            if (string.IsNullOrEmpty(settingId))
            {
                return string.Empty;
            }

            string trimmed = settingId.Trim();
            return trimmed.Length > MaxSettingIdLength ? string.Empty : trimmed;
        }

        private static bool IsValidQuery(string normalizedQuery)
        {
            return BWTSettingsSearchText.IsMeaningful(normalizedQuery) &&
                normalizedQuery.Length <= MaxQueryLength;
        }

        private static bool SameIds(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class AliasEntry
        {
            internal AliasEntry(string sourceQuery)
            {
                SourceQuery = sourceQuery;
                SettingIds = new List<string>();
            }

            internal string SourceQuery { get; }
            internal string TargetQuery { get; set; }
            internal List<string> SettingIds { get; set; }
            internal int Confirmations { get; set; }
            internal string LastConfirmationToken { get; set; }
            internal bool IsActive => Confirmations >= RequiredConfirmations;
        }
    }

    internal sealed class AliasSnapshot
    {
        internal AliasSnapshot(
            string sourceQuery,
            string targetQuery,
            IEnumerable<string> settingIds,
            int confirmations)
        {
            SourceQuery = sourceQuery;
            TargetQuery = targetQuery;
            SettingIds = new List<string>(settingIds ?? new string[0]);
            Confirmations = confirmations;
        }

        internal string SourceQuery { get; }
        internal string TargetQuery { get; }
        internal IReadOnlyList<string> SettingIds { get; }
        internal int Confirmations { get; }
        internal bool IsActive => Confirmations >= BWTSettingsAdaptiveAliasModel.RequiredConfirmations;
    }
}
