using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Keeps the stable pawn-label text path independent from RimWorld's
    /// mutable TaggedString/rich-text caches. Markup is measured as visible
    /// text and is only copied when a label actually needs truncation.
    /// </summary>
    internal static class PreparedPawnLabelText
    {
        private const string Ellipsis = "...";

        // These patterns intentionally mirror RimWorld's
        // native rich-text tag stripper. The immutable Regex instances do not
        // enter its resolver or grow the process-wide color cache.
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

        internal static string Truncate(
            string richText,
            float width,
            Func<string, float> measureVisibleText)
        {
            if (richText == null)
            {
                throw new ArgumentNullException(nameof(richText));
            }
            if (measureVisibleText == null)
            {
                throw new ArgumentNullException(nameof(measureVisibleText));
            }

            // A NaN width cannot describe a meaningful cell. Keep the source
            // unchanged, matching the existing strict-width guard's behavior.
            if (float.IsNaN(width))
            {
                return richText;
            }

            string visibleText = StripMarkup(richText);
            // Preserve the old strict comparison: an exact fit is not a
            // truncation, so the original rich string and its tags survive.
            if (measureVisibleText(visibleText) <= width)
            {
                return richText;
            }

            if (measureVisibleText(Ellipsis) > width)
            {
                return Ellipsis;
            }

            int textElementCount = CountTextElements(visibleText);
            int low = 0;
            int high = textElementCount;
            int bestEnd = 0;
            while (low <= high)
            {
                int candidateElements = low + ((high - low + 1) / 2);
                int candidateEnd = TextElementBoundary(visibleText, candidateElements);
                string candidate = visibleText.Substring(0, candidateEnd) + Ellipsis;
                if (measureVisibleText(candidate) <= width)
                {
                    bestEnd = candidateEnd;
                    low = candidateElements + 1;
                }
                else
                {
                    high = candidateElements - 1;
                }
            }

            return BuildTruncatedMarkup(richText, bestEnd);
        }

        private static string BuildTruncatedMarkup(string source, int visibleEnd)
        {
            if (visibleEnd <= 0)
            {
                return Ellipsis;
            }

            var output = new StringBuilder(source.Length + Ellipsis.Length);
            var openTags = new List<string>(4);
            int visiblePosition = 0;
            bool stripControlTokens =
                source.IndexOf("(*", StringComparison.Ordinal) >= 0 ||
                source.IndexOf('<') >= 0;
            for (int index = 0; index < source.Length;)
            {
                int nextIndex;
                string tagName;
                bool closingTag;
                bool selfClosingTag;
                if (TryReadMarkup(
                        source,
                        index,
                        stripControlTokens,
                        out nextIndex,
                        out tagName,
                        out closingTag,
                        out selfClosingTag))
                {
                    if (visiblePosition >= visibleEnd)
                    {
                        break;
                    }

                    // Complete tokens are removed by StripMarkup. A
                    // conventional XML name is retained only so the
                    // generated prefix can close the active formatting scope.
                    if (tagName.Length == 0)
                    {
                        index = nextIndex;
                        continue;
                    }

                    if (closingTag)
                    {
                        if (openTags.Count > 0 &&
                            string.Equals(
                                openTags[openTags.Count - 1],
                                tagName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            openTags.RemoveAt(openTags.Count - 1);
                            output.Append(source, index, nextIndex - index);
                        }
                    }
                    else
                    {
                        output.Append(source, index, nextIndex - index);
                        if (!selfClosingTag)
                        {
                            openTags.Add(tagName);
                        }
                    }

                    index = nextIndex;
                    continue;
                }

                if (visiblePosition >= visibleEnd)
                {
                    break;
                }

                // visibleEnd is always a text-element boundary in the
                // stripped string. Copying one UTF-16 unit at a time keeps
                // source markup positions independent from visible positions;
                // the boundary check prevents a surrogate, combining sequence,
                // or ZWJ sequence from being cut at the truncation point.
                if (visiblePosition + 1 > visibleEnd)
                {
                    break;
                }
                output.Append(source[index]);
                visiblePosition++;
                index++;
            }

            // The ellipsis stays in the current formatting scope. Closing all
            // active tags afterwards makes the generated value balanced while
            // preserving the role color on the visible end of the label.
            output.Append(Ellipsis);
            for (int index = openTags.Count - 1; index >= 0; index--)
            {
                output.Append("</").Append(openTags[index]).Append('>');
            }
            return output.ToString();
        }

        private static bool TryReadMarkup(
            string text,
            int index,
            bool stripControlTokens,
            out int nextIndex,
            out string tagName,
            out bool closingTag,
            out bool selfClosingTag)
        {
            nextIndex = index;
            tagName = string.Empty;
            closingTag = false;
            selfClosingTag = false;

            if (index < 0 || index >= text.Length)
            {
                return false;
            }

            if (stripControlTokens &&
                text[index] == '(' &&
                index + 1 < text.Length &&
                (text[index + 1] == '*' || text[index + 1] == '/'))
            {
                int end = text.IndexOf(')', index + 2);
                if (end < 0)
                {
                    // The native control-token regex requires a closing
                    // parenthesis; unfinished control-shaped text remains
                    // visible.
                    return false;
                }

                nextIndex = end + 1;
                return true;
            }

            if (text[index] != '<')
            {
                return false;
            }

            int endIndex = text.IndexOf('>', index + 1);
            if (endIndex < 0)
            {
                // The native XML regex requires a closing angle bracket;
                // unfinished tag-shaped text remains visible, including an
                // unfinished color tag.
                return false;
            }

            // The authoritative regex strips every complete <...> token,
            // including empty, spaced, numeric, nested, and otherwise odd
            // contents. Only conventional names are tracked for balancing.
            int nameStart = index + 1;
            if (nameStart >= endIndex)
            {
                nextIndex = endIndex + 1;
                return true;
            }

            closingTag = text[nameStart] == '/';
            if (closingTag)
            {
                nameStart++;
            }

            if (nameStart >= endIndex || !char.IsLetter(text[nameStart]))
            {
                nextIndex = endIndex + 1;
                return true;
            }

            int nameEnd = nameStart;
            while (nameEnd < endIndex &&
                   !char.IsWhiteSpace(text[nameEnd]) &&
                   text[nameEnd] != '=' &&
                   text[nameEnd] != '/')
            {
                if (!IsTagNameCharacter(text[nameEnd]))
                {
                    // It is still a complete token for StripMarkup, but it
                    // is not safe to copy or synthesize as a rich-text tag.
                    tagName = string.Empty;
                    nextIndex = endIndex + 1;
                    return true;
                }
                nameEnd++;
            }

            tagName = text.Substring(nameStart, nameEnd - nameStart);
            int contentEnd = endIndex - 1;
            while (contentEnd > index && char.IsWhiteSpace(text[contentEnd]))
            {
                contentEnd--;
            }
            selfClosingTag = !closingTag && text[contentEnd] == '/';
            nextIndex = endIndex + 1;
            return true;
        }

        private static bool IsTagNameCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_' || value == '-';
        }

        private static int CountTextElements(string text)
        {
            int count = 0;
            for (int index = 0; index < text.Length; count++)
            {
                index = NextTextElementBoundary(text, index);
            }
            return count;
        }

        private static int TextElementBoundary(string text, int elementCount)
        {
            int count = 0;
            int index = 0;
            while (index < text.Length && count < elementCount)
            {
                index = NextTextElementBoundary(text, index);
                count++;
            }
            return index;
        }

        private static int NextTextElementBoundary(string text, int index)
        {
            if (index < 0 || index >= text.Length)
            {
                return index;
            }

            int current = index;
            int codePoint = ReadCodePoint(text, current, out int codePointLength);
            current += codePointLength;

            // CRLF is one text element under the Unicode text-element rules.
            if (codePoint == '\r' && current < text.Length && text[current] == '\n')
            {
                return current + 1;
            }

            // Regional indicators form flags in pairs.
            if (IsRegionalIndicator(codePoint) && current < text.Length)
            {
                int nextCodePoint = ReadCodePoint(text, current, out int nextLength);
                if (IsRegionalIndicator(nextCodePoint))
                {
                    current += nextLength;
                }
                return current;
            }

            ConsumeExtenders(text, ref current);
            while (current < text.Length)
            {
                int joiner = ReadCodePoint(text, current, out int joinerLength);
                if (joiner != 0x200D)
                {
                    break;
                }

                // Keep a ZWJ and its following code point in the same text
                // element. This is the important distinction from the .NET
                // 4.x StringInfo implementation used by the game runtime.
                current += joinerLength;
                if (current >= text.Length)
                {
                    break;
                }
                current += CodePointLength(text, current);
                ConsumeExtenders(text, ref current);
            }
            return current;
        }

        private static void ConsumeExtenders(string text, ref int index)
        {
            while (index < text.Length)
            {
                int codePoint = ReadCodePoint(text, index, out int codePointLength);
                if (!IsExtender(text, index, codePoint))
                {
                    return;
                }
                index += codePointLength;
            }
        }

        private static bool IsExtender(string text, int index, int codePoint)
        {
            if ((codePoint >= 0x1F3FB && codePoint <= 0x1F3FF) ||
                (codePoint >= 0xE0020 && codePoint <= 0xE007F) ||
                (codePoint >= 0xE0100 && codePoint <= 0xE01EF) ||
                codePoint == 0xFE0E ||
                codePoint == 0xFE0F)
            {
                return true;
            }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, index);
            return category == UnicodeCategory.NonSpacingMark ||
                category == UnicodeCategory.SpacingCombiningMark ||
                category == UnicodeCategory.EnclosingMark;
        }

        private static bool IsRegionalIndicator(int codePoint)
        {
            return codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;
        }

        private static int ReadCodePoint(string text, int index, out int length)
        {
            length = CodePointLength(text, index);
            if (length == 2)
            {
                return char.ConvertToUtf32(text[index], text[index + 1]);
            }
            return text[index];
        }

        private static int CodePointLength(string text, int index)
        {
            return index + 1 < text.Length &&
                   char.IsHighSurrogate(text[index]) &&
                   char.IsLowSurrogate(text[index + 1])
                ? 2
                : 1;
        }
    }
}
