using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// Central registry for all rule parameters marked with <see cref="RuleParameterAttribute"/>.
    /// Keeps the discovery logic in one place so both UIs share the same schema.
    /// </summary>
    public static class RuleParameterRegistry
    {
        private static readonly Lazy<IReadOnlyList<FieldInfo>> CachedFields = new Lazy<IReadOnlyList<FieldInfo>>(
            () => typeof(WorkAssignmentParameters)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.GetCustomAttribute<RuleParameterAttribute>() != null)
                .ToArray());

        private static readonly Lazy<HashSet<string>> CachedFieldNames =
            new Lazy<HashSet<string>>(() => CachedFields.Value
                .Select(f => f.Name)
                .ToHashSet(StringComparer.Ordinal));

        /// <summary>
        /// All fields on <see cref="WorkAssignmentParameters"/> that participate in rule evaluation.
        /// </summary>
        public static IReadOnlyList<FieldInfo> Fields => CachedFields.Value;

        /// <summary>
        /// All parameter field names for quick lookup.
        /// </summary>
        public static IReadOnlyCollection<string> FieldNames => CachedFieldNames.Value;

        /// <summary>
        /// Standard display name for a parameter based on the translation key pattern.
        /// </summary>
        public static string GetDisplayName(string fieldName) => $"BWT_{fieldName}".Translate();

        /// <summary>
        /// Standard tooltip/description for a parameter based on the translation key pattern.
        /// </summary>
        public static string GetDescription(string fieldName) => $"BWT_{fieldName}_Desc".Translate();
    }
}
