using System;
using System.Text.RegularExpressions;
using Better_Work_Tab.UI.WorkGrid.Rendering;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PreparedPawnLabelTextTests
    {
        public static void Run()
        {
            StripMarkupMatchesRimWorldRegexCorpus();
        }

        private static void StripMarkupMatchesRimWorldRegexCorpus()
        {
            string[] corpus =
            {
                "Emma<3>",
                "Emma< color=#999999>worker</ color>",
                "Emma<>worker",
                "Emma<color=#99999",
                "Emma(/role)",
                "Emma(*role)(/role)",
                "Emma<color=#99999(/role)",
                "<a<b>Emma</b>",
                "line\n<foo bar>worker</foo>",
                "ordinary < 3 > text (/role)",
                "unclosed (*role"
            };

            for (int index = 0; index < corpus.Length; index++)
            {
                string expected = ReferenceStripMarkup(corpus[index]);
                string actual = PreparedPawnLabelText.StripMarkup(corpus[index]);
                TestAssert.Equal(expected, actual,
                    "markup stripping diverged from ColoredText.StripTags corpus case " + index);
            }
        }

        private static string ReferenceStripMarkup(string richText)
        {
            if (richText.Length == 0 ||
                (richText.IndexOf("(*", StringComparison.Ordinal) < 0 &&
                 richText.IndexOf('<') < 0))
            {
                return richText;
            }

            string withoutXml = Regex.Replace(richText, "<[^>]*>", string.Empty);
            return Regex.Replace(withoutXml, "\\([\\*\\/][^\\)]*\\)", string.Empty);
        }
    }
}
