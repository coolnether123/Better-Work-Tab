using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal readonly struct RuleBuilder2TargetCatalogEntry
    {
        internal RuleBuilder2TargetCatalogEntry(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            string label,
            bool hasSubWork)
        {
            WorkTypeDef = workType;
            WorkGiverDef = workGiver;
            Label = label;
            HasSubWork = hasSubWork;
        }

        internal WorkTypeDef WorkTypeDef { get; }
        internal WorkGiverDef WorkGiverDef { get; }
        internal string Label { get; }
        internal bool HasSubWork { get; }
        internal bool IsSubWork => WorkGiverDef != null;
        internal string WorkTypeKey => WorkTypeDef?.defName ?? "";

        internal bool Matches(RuleBuilder2Target target)
        {
            if (target == null || WorkTypeDef == null)
            {
                return false;
            }

            return target.WorkTypeDefName == WorkTypeDef.defName &&
                   (target.WorkGiverDefName ?? "") == (WorkGiverDef?.defName ?? "");
        }
    }

    internal static class RuleBuilder2TargetCatalog
    {
        internal static List<RuleBuilder2TargetCatalogEntry> BuildAll()
        {
            var entries = new List<RuleBuilder2TargetCatalogEntry>();

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading
                         .Where(workType => workType != null)
                         .OrderByDescending(workType => workType.naturalPriority)
                         .ThenBy(workType => GetWorkTypeLabel(workType)))
            {
                List<WorkGiverDef> subWork = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType)
                    .Where(workGiver => workGiver?.def != null)
                    .Select(workGiver => workGiver.def)
                    .Distinct()
                    .OrderBy(GetWorkGiverLabel)
                    .ToList();

                entries.Add(new RuleBuilder2TargetCatalogEntry(workType, null, GetWorkTypeLabel(workType), subWork.Count > 0));

                foreach (WorkGiverDef workGiver in subWork)
                {
                    entries.Add(new RuleBuilder2TargetCatalogEntry(workType, workGiver, GetWorkGiverLabel(workGiver), false));
                }
            }

            return entries;
        }

        internal static List<RuleBuilder2TargetCatalogEntry> BuildVisible(
            List<RuleBuilder2TargetCatalogEntry> allEntries,
            string search,
            ISet<string> expandedWorkTypes)
        {
            string normalizedSearch = (search ?? "").Trim().ToLowerInvariant();
            bool searching = normalizedSearch.Length > 0;
            List<RuleBuilder2TargetCatalogEntry> entries = allEntries ?? new List<RuleBuilder2TargetCatalogEntry>();
            var visibleEntries = new List<RuleBuilder2TargetCatalogEntry>();
            var childrenByWorkType = entries
                .Where(entry => entry.IsSubWork)
                .GroupBy(entry => entry.WorkTypeKey)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (RuleBuilder2TargetCatalogEntry parent in entries.Where(entry => !entry.IsSubWork))
            {
                childrenByWorkType.TryGetValue(parent.WorkTypeKey, out List<RuleBuilder2TargetCatalogEntry> children);
                children ??= new List<RuleBuilder2TargetCatalogEntry>();
                bool parentMatches = MatchesSearch(parent, normalizedSearch);
                bool anyChildMatches = searching && children.Any(child => MatchesSearch(child, normalizedSearch));

                if (!searching || parentMatches || anyChildMatches)
                {
                    visibleEntries.Add(parent);
                }

                bool showChildren = (!searching && expandedWorkTypes != null && expandedWorkTypes.Contains(parent.WorkTypeKey)) ||
                                    (searching && (parentMatches || anyChildMatches));
                if (!showChildren)
                {
                    continue;
                }

                foreach (RuleBuilder2TargetCatalogEntry child in children)
                {
                    if (!searching || parentMatches || MatchesSearch(child, normalizedSearch))
                    {
                        visibleEntries.Add(child);
                    }
                }
            }

            return visibleEntries;
        }

        private static bool MatchesSearch(RuleBuilder2TargetCatalogEntry entry, string search)
        {
            return search.Length == 0 ||
                   (entry.WorkTypeDef?.defName ?? "").ToLowerInvariant().Contains(search) ||
                   (entry.WorkGiverDef?.defName ?? "").ToLowerInvariant().Contains(search) ||
                   (entry.Label ?? "").ToLowerInvariant().Contains(search);
        }

        private static string GetWorkTypeLabel(WorkTypeDef workType)
        {
            string label = workType?.LabelCap.ToString();
            return label.NullOrEmpty() ? workType?.defName ?? "" : label;
        }

        private static string GetWorkGiverLabel(WorkGiverDef workGiver)
        {
            string label = workGiver?.LabelCap.ToString();
            return label.NullOrEmpty() ? workGiver?.defName ?? "" : label;
        }
    }
}
