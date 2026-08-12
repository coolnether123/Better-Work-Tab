using System;
using System.Collections.Generic;
using Spine.UI.SettingsFramework;

namespace Better_Work_Tab.UI.Settings
{
    internal static class BWTSettingsContextFocus
    {
        private static BWTSettingsFocusRequest _pendingRequest;

        internal static void Request(BWTSettingsFocusRequest request)
        {
            if (request == null || request.SettingIds.Count == 0)
            {
                return;
            }

            _pendingRequest = request;
        }

        internal static bool TryConsume(out BWTSettingsFocusRequest request)
        {
            request = _pendingRequest;
            _pendingRequest = null;
            return request != null;
        }

        internal static SettingsFilterDefinition CreateFilter(BWTSettingsFocusRequest request)
        {
            if (request == null)
            {
                return null;
            }

            var ids = new HashSet<string>(request.SettingIds, StringComparer.OrdinalIgnoreCase);
            return new SettingsFilterDefinition
            {
                Id = "context." + SanitizeId(request.Label),
                Label = request.Label,
                Category = "context",
                CategoryLabel = "Context",
                Tooltip = request.Tooltip,
                Predicate = (def, _) => def != null && !string.IsNullOrEmpty(def.Id) && ids.Contains(def.Id),
                IncludeChildrenOfMatches = true
            };
        }

        private static string SanitizeId(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return "worktab";
            }

            char[] chars = label.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                {
                    chars[i] = '.';
                }
            }

            return new string(chars).Trim('.');
        }
    }
}
