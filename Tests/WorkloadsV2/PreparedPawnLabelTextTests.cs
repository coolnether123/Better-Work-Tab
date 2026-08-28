using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Better_Work_Tab.UI.WorkGrid.Rendering;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PreparedPawnLabelTextTests
    {
        public static void Run()
        {
            ExactFitPreservesSourceMarkup();
            StripMarkupMatchesRimWorldRegexCorpus();
            ExactFitUsesAuthoritativeMarkupVisibility();
            EmmaNarrowWidthKeepsRoleTagBalanced();
            HinesNestedTagsStayBalanced();
            PhoenixLocalizedMultipleTagsStayBalanced();
            ControlTokensAreNotRendered();
            TooNarrowForEllipsisReturnsPlainEllipsis();
            GraphemeBoundariesRemainWhole();
            AdditionalGraphemeBoundariesRemainWhole();
            MalformedCompleteTagsAreNotCopied();
            CapturePresentationColorPathsStayBalanced();
            WidthSweepNeverCutsMarkup();
            MeasurementIsBoundedAndHasNoGlobalCache();
        }

        private static void ExactFitPreservesSourceMarkup()
        {
            const string label = "Emma, <color=#999999>Quarry worker</color>";
            int measureCalls = 0;
            string prepared = PreparedPawnLabelText.Truncate(
                label,
                19f,
                delegate(string value)
                {
                    measureCalls++;
                    return value.Length;
                });

            TestAssert.True(object.ReferenceEquals(label, prepared),
                "an exact-fit pawn label must retain its original string");
            TestAssert.Equal(label, prepared,
                "an exact-fit pawn label must retain its original rich text");
            TestAssert.Equal(1, measureCalls,
                "an exact-fit label should require one visible-width measurement");
        }

        private static void EmmaNarrowWidthKeepsRoleTagBalanced()
        {
            const string label = "Emma, <color=#999999>Quarry worker</color>";
            string prepared = PreparedPawnLabelText.Truncate(label, 10f, MeasureByCharacter);

            TestAssert.Equal("Emma, Q...", PreparedPawnLabelText.StripMarkup(prepared),
                "Emma's visible prefix must use the strict width budget");
            TestAssert.True(IsBalancedMarkup(prepared),
                "Emma's truncated role color must remain balanced");
            TestAssert.True(prepared.IndexOf("<color=#99999", StringComparison.Ordinal) >= 0,
                "Emma's retained role segment must stay colorized");
            TestAssert.True(prepared.EndsWith("</color>", StringComparison.Ordinal),
                "Emma's role tag must close after the ellipsis");
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

        private static void ExactFitUsesAuthoritativeMarkupVisibility()
        {
            const string completeNumericTag = "A<3>";
            string prepared = PreparedPawnLabelText.Truncate(
                completeNumericTag,
                1f,
                MeasureByCharacter);
            TestAssert.True(object.ReferenceEquals(completeNumericTag, prepared),
                "an exact-fit complete numeric token must preserve the original source");

            const string unfinishedTag = "A<color=#99999";
            prepared = PreparedPawnLabelText.Truncate(
                unfinishedTag,
                14f,
                MeasureByCharacter);
            TestAssert.True(object.ReferenceEquals(unfinishedTag, prepared),
                "an exact-fit unfinished token must remain visible per ColoredText.StripTags");
            TestAssert.Equal(ReferenceStripMarkup(unfinishedTag),
                PreparedPawnLabelText.StripMarkup(prepared),
                "exact-fit unfinished token visibility must match ColoredText.StripTags");
        }

        private static void HinesNestedTagsStayBalanced()
        {
            const string label =
                "Hines, <color=#999999><b>Quarry worker</b></color>";
            string prepared = PreparedPawnLabelText.Truncate(label, 11f, MeasureByCharacter);

            TestAssert.Equal("Hines, Q...", PreparedPawnLabelText.StripMarkup(prepared),
                "Hines's visible prefix must be measured without nested tags");
            TestAssert.True(IsBalancedMarkup(prepared),
                "Hines's nested role tags must remain balanced");
            TestAssert.Contains(prepared, "<color=#999999><b>Q...</b></color>",
                "Hines's nested formatting must surround only the retained text");
        }

        private static void PhoenixLocalizedMultipleTagsStayBalanced()
        {
            const string label =
                "Phoenix, <color=#999999>Animal</color> <i>farmer</i> " +
                "<b>Élite</b> — <size=10>colonist</size>";
            string prepared = PreparedPawnLabelText.Truncate(label, 34f, MeasureByCharacter);

            TestAssert.Equal(
                "Phoenix, Animal farmer Élite — ...",
                PreparedPawnLabelText.StripMarkup(prepared),
                "localized punctuation and accented text must use visible width");
            TestAssert.True(IsBalancedMarkup(prepared),
                "Phoenix's multiple formatting spans must remain balanced");
            TestAssert.Contains(prepared, "<color=#999999>Animal</color>",
                "Phoenix's role color must survive truncation");
            TestAssert.Contains(prepared, "<i>farmer</i>",
                "Phoenix's second formatting span must survive truncation");
            TestAssert.Contains(prepared, "<b>Élite</b>",
                "Phoenix's localized formatting span must survive truncation");
            TestAssert.False(prepared.IndexOf("<size=10>", StringComparison.Ordinal) >= 0,
                "Phoenix must not copy a tag that begins after the retained prefix");
        }

        private static void ControlTokensAreNotRendered()
        {
            const string label = "Emma, <color=#999999>Quarry (*role) worker</color>";
            string prepared = PreparedPawnLabelText.Truncate(label, 16f, MeasureByCharacter);

            TestAssert.Equal("Emma, Quarry ...", PreparedPawnLabelText.StripMarkup(prepared),
                "control tokens must have zero visible width");
            TestAssert.False(prepared.IndexOf("(*role)", StringComparison.Ordinal) >= 0,
                "control tokens must never reach the prepared renderer");
            TestAssert.True(IsBalancedMarkup(prepared),
                "control-token removal must not unbalance role markup");
        }

        private static void TooNarrowForEllipsisReturnsPlainEllipsis()
        {
            const string label = "Phoenix, <color=#999999>Animal farmer</color>";
            string prepared = PreparedPawnLabelText.Truncate(label, 2f, MeasureByCharacter);

            TestAssert.Equal("...", prepared,
                "a cell narrower than the ellipsis must use the native fallback value");
            TestAssert.Equal("...", PreparedPawnLabelText.StripMarkup(prepared),
                "the narrow fallback must contain no literal markup");
            TestAssert.True(IsBalancedMarkup(prepared),
                "the narrow fallback must still be valid rich text");
        }

        private static void GraphemeBoundariesRemainWhole()
        {
            const string emojiLabel =
                "<color=#999999>👩‍💻 éxy</color>";
            string prepared = PreparedPawnLabelText.Truncate(
                emojiLabel,
                9f,
                MeasureByCharacter);
            TestAssert.Equal("👩‍💻 ...", PreparedPawnLabelText.StripMarkup(prepared),
                "a ZWJ emoji must remain one text element at the truncation boundary");
            TestAssert.Contains(prepared, "👩‍💻",
                "the prepared label must retain the complete ZWJ emoji");
            TestAssert.True(IsBalancedMarkup(prepared),
                "the ZWJ label must retain balanced role markup");

            const string combiningLabel =
                "<color=#999999>éxyzw</color>";
            prepared = PreparedPawnLabelText.Truncate(
                combiningLabel,
                5f,
                MeasureByCharacter);
            TestAssert.Equal("é...", PreparedPawnLabelText.StripMarkup(prepared),
                "a combining mark must remain attached to its base character");
            TestAssert.False(prepared.IndexOf("e...", StringComparison.Ordinal) >= 0,
                "the prepared label must not split a combining sequence");
            TestAssert.True(IsBalancedMarkup(prepared),
                "the combining-mark label must retain balanced role markup");
        }

        private static void CapturePresentationColorPathsStayBalanced()
        {
            const string pawnLabel =
                "Emma, <color=#999999>Quarry worker</color>";
            const string mechLabel =
                "Lancer, <color=#999999>Construction</color>";
            const string slaveLabel =
                "Hines, <color=#999999>Quarry worker</color>";

            string pawnText = PreparedPawnLabelText.Truncate(
                pawnLabel,
                10f,
                MeasureByCharacter);
            TestAssert.Equal("Emma, Q...", PreparedPawnLabelText.StripMarkup(pawnText),
                "the ordinary pawn capture path must retain its visible label prefix");
            TestAssert.True(IsBalancedMarkup(pawnText),
                "the ordinary pawn capture path must retain balanced role markup");

            string mechText = PreparedPawnLabelText.Truncate(
                mechLabel,
                12f,
                MeasureByCharacter);
            string mechPresented = "<color=#00FFFF>" + mechText + "</color>";
            TestAssert.Equal("Lancer, C...", PreparedPawnLabelText.StripMarkup(mechPresented),
                "the colony-mech capture path must retain its visible label prefix");
            TestAssert.True(IsBalancedMarkup(mechPresented),
                "the colony-mech name-color wrapper must remain balanced around role markup");

            string slaveText = PreparedPawnLabelText.Truncate(
                slaveLabel,
                11f,
                MeasureByCharacter);
            string slavePresented = "<color=#FFAA00>" + slaveText + "</color>";
            TestAssert.Equal("Hines, Q...", PreparedPawnLabelText.StripMarkup(slavePresented),
                "the slave capture path must retain its visible label prefix");
            TestAssert.True(IsBalancedMarkup(slavePresented),
                "the slave name-color wrapper must remain balanced around role markup");

            string contrastText = PreparedPawnLabelText.StripMarkup(
                PreparedPawnLabelText.Truncate(
                    mechLabel,
                    12f,
                    MeasureByCharacter));
            TestAssert.Equal("Lancer, C...", contrastText,
                "the contrast capture path must remove role markup after truncation");
        }

        private static void AdditionalGraphemeBoundariesRemainWhole()
        {
            const string modifierLabel =
                "<color=#999999>👍🏽 xyz</color>";
            string prepared = PreparedPawnLabelText.Truncate(
                modifierLabel,
                7f,
                MeasureByCharacter);
            TestAssert.Equal("👍🏽...", PreparedPawnLabelText.StripMarkup(prepared),
                "an emoji modifier must remain attached to its base emoji");
            TestAssert.True(IsBalancedMarkup(prepared),
                "the emoji modifier label must retain balanced role markup");

            const string flagLabel =
                "<color=#999999>🇺🇸 xyz</color>";
            prepared = PreparedPawnLabelText.Truncate(
                flagLabel,
                7f,
                MeasureByCharacter);
            TestAssert.Equal("🇺🇸...", PreparedPawnLabelText.StripMarkup(prepared),
                "a regional-indicator flag must remain one text element");
            TestAssert.True(IsBalancedMarkup(prepared),
                "the regional-indicator label must retain balanced role markup");

            const string crlfLabel =
                "<color=#999999>Line\r\nBreak</color>";
            prepared = PreparedPawnLabelText.Truncate(
                crlfLabel,
                9f,
                MeasureByCharacter);
            TestAssert.Equal("Line\r\n...", PreparedPawnLabelText.StripMarkup(prepared),
                "CRLF must remain one text element at the truncation boundary");
            TestAssert.True(IsBalancedMarkup(prepared),
                "the CRLF label must retain balanced role markup");
        }

        private static void MalformedCompleteTagsAreNotCopied()
        {
            const string label = "Emma, <a<b>Quarry worker</a<b>";
            string prepared = PreparedPawnLabelText.Truncate(
                label,
                10f,
                MeasureByCharacter);

            TestAssert.Equal("Emma, Q...", PreparedPawnLabelText.StripMarkup(prepared),
                "a complete but malformed token must not change visible-width truncation");
            TestAssert.False(prepared.IndexOf("<a<b>", StringComparison.Ordinal) >= 0,
                "a malformed complete token must not be copied into prepared rich text");
            TestAssert.True(IsBalancedMarkup(prepared),
                "malformed complete tokens must not leave generated markup unbalanced");
        }

        private static void WidthSweepNeverCutsMarkup()
        {
            string[] labels =
            {
                "Emma, <color=#999999>Quarry worker</color>",
                "Hines, <color=#999999><b>Quarry worker</b></color>",
                "Phoenix, <color=#999999>Animal</color> <i>farmer</i> " +
                "<b>Élite</b> — <size=10>colonist</size>"
            };

            for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
            {
                string plain = PreparedPawnLabelText.StripMarkup(labels[labelIndex]);
                for (int width = 0; width <= plain.Length + 2; width++)
                {
                    string prepared = PreparedPawnLabelText.Truncate(
                        labels[labelIndex],
                        width,
                        MeasureByCharacter);
                    TestAssert.True(IsBalancedMarkup(prepared),
                        "width sweep produced unbalanced markup for label " + labelIndex +
                        " at width " + width);
                    TestAssert.False(prepared.IndexOf("<color=#", StringComparison.Ordinal) >= 0 &&
                                     prepared.IndexOf("</color>", StringComparison.Ordinal) < 0,
                        "width sweep produced an unterminated color tag for label " + labelIndex +
                        " at width " + width);
                }
            }
        }

        private static void MeasurementIsBoundedAndHasNoGlobalCache()
        {
            int measureCalls = 0;
            string source = "Hines, <color=#999999>Quarry worker with a long title</color>";
            string prepared = PreparedPawnLabelText.Truncate(
                source,
                14f,
                delegate(string value)
                {
                    measureCalls++;
                    return value.Length;
                });

            TestAssert.True(measureCalls <= 10,
                "markup-aware truncation must use bounded binary-search measurement");
            TestAssert.True(IsBalancedMarkup(prepared),
                "bounded truncation must not trade performance for malformed markup");

            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "WorkGrid", "Rendering", "PreparedPawnLabelText.cs"),
                "prepared pawn-label text implementation");
            string helper = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Rendering",
                "PreparedPawnLabelText.cs"));
            TestAssert.False(helper.IndexOf("Dictionary<", StringComparison.Ordinal) >= 0,
                "prepared label text must not add a process-wide truncation cache");
            TestAssert.False(helper.IndexOf("ColoredText.", StringComparison.Ordinal) >= 0,
                "prepared label text must not grow RimWorld's global color cache");
            TestAssert.False(helper.IndexOf("GenText.Truncate", StringComparison.Ordinal) >= 0,
                "prepared label text must not delegate to TaggedString truncation");
            TestAssert.False(helper.IndexOf("Resolve(", StringComparison.Ordinal) >= 0,
                "prepared label text must not resolve generated variants");
        }

        private static float MeasureByCharacter(string value)
        {
            return value.Length;
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

        private static bool IsBalancedMarkup(string text)
        {
            var openTags = new List<string>();
            for (int index = 0; index < text.Length;)
            {
                if (text[index] != '<')
                {
                    index++;
                    continue;
                }

                int close = text.IndexOf('>', index + 1);
                if (close < 0)
                {
                    return false;
                }

                string content = text.Substring(index + 1, close - index - 1).Trim();
                if (content.Length == 0)
                {
                    return false;
                }

                bool closing = content[0] == '/';
                bool selfClosing = !closing && content.EndsWith("/", StringComparison.Ordinal);
                string name = content.TrimStart('/');
                int nameEnd = 0;
                while (nameEnd < name.Length &&
                       !char.IsWhiteSpace(name[nameEnd]) &&
                       name[nameEnd] != '=' &&
                       name[nameEnd] != '/')
                {
                    nameEnd++;
                }
                if (nameEnd == 0)
                {
                    return false;
                }
                name = name.Substring(0, nameEnd);
                if (closing)
                {
                    if (openTags.Count == 0 ||
                        !string.Equals(
                            openTags[openTags.Count - 1],
                            name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    openTags.RemoveAt(openTags.Count - 1);
                }
                else if (!selfClosing)
                {
                    openTags.Add(name);
                }

                index = close + 1;
            }
            return openTags.Count == 0;
        }
    }
}
