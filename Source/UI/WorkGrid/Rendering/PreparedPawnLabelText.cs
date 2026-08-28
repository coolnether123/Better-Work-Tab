using System;
using System.Text.RegularExpressions;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Keeps the prepared contrast-label path independent from RimWorld's
    /// mutable rich-text caches while preserving its markup visibility rules.
    /// </summary>
    internal static class PreparedPawnLabelText
    {
        // These patterns intentionally mirror RimWorld's native rich-text tag
        // stripper. The immutable Regex instances do not enter its resolver or
        // grow the process-wide color cache.
        private static readonly Regex XmlTagRegex = new Regex("<[^>]*>");
        private static readonly Regex ControlTagRegex = new Regex("\\([\\*\\/][^\\)]*\\)");

        internal static string StripMarkup(string richText)
        {
            if (richText == null)
            {
                throw new ArgumentNullException(nameof(richText));
            }

            // The native tag stripper has this fast path: a control close
            // token is only special when the source contains a control opener
            // or an XML opener somewhere else in the same value.
            if (richText.Length == 0 ||
                (richText.IndexOf("(*", StringComparison.Ordinal) < 0 &&
                 richText.IndexOf('<') < 0))
            {
                return richText;
            }

            string withoutXml = XmlTagRegex.Replace(richText, string.Empty);
            return ControlTagRegex.Replace(withoutXml, string.Empty);
        }
    }
}
