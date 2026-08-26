using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Composes one prepared text cell into the render target owned by
    /// <see cref="RetainedWorkBoxRowCache"/>. This is a one-shot cache-miss leaf:
    /// retained cache hits never enter the composer, and a failed composition is
    /// reported so the caller can use the native label path.
    /// </summary>
    internal static class RetainedTextComposer
    {
        /// <summary>
        /// Prepares the font glyphs and emits the label in one pass. The retained
        /// label path supports the same markup subset as the previous renderer:
        /// well-formed tags are omitted, while <c>&lt;color=#...&gt;</c> and
        /// <c>&lt;/color&gt;</c> change the color of subsequent glyphs. An
        /// unterminated tag remains text, matching the previous retained leaf.
        /// </summary>
        internal static bool Draw(
            Rect rect,
            string richText,
            Color baseColor,
            GameFont gameFont,
            TextAnchor anchor)
        {
            GameFont previousFont = Text.Font;
            Text.Font = gameFont;
            try
            {
                if (!TryPrepare(richText, out PreparedGlyphRun glyphRun))
                {
                    return false;
                }

                return Emit(rect, richText, baseColor, anchor, in glyphRun);
            }
            finally
            {
                Text.Font = previousFont;
            }
        }

        private static bool TryPrepare(
            string richText,
            out PreparedGlyphRun glyphRun)
        {
            GUIStyle style = Text.CurFontStyle;
            Font font = style?.font;
            if (font == null || string.IsNullOrEmpty(richText))
            {
                glyphRun = default(PreparedGlyphRun);
                return false;
            }

            // The plain string supplies the glyph sequence; the original rich
            // string is retained for the emission pass so color tags can be
            // applied without allocating a per-glyph command list.
            string text = richText.StripTags();
            int fontSize = style.fontSize > 0 ? style.fontSize : font.fontSize;
            FontStyle fontStyle = style.fontStyle;
            font.RequestCharactersInTexture(text, fontSize, fontStyle);
            CharacterInfo[] glyphs = new CharacterInfo[text.Length];
            float width = 0f;
            for (int index = 0; index < text.Length; index++)
            {
                if (!font.GetCharacterInfo(text[index], out glyphs[index], fontSize, fontStyle))
                {
                    glyphRun = default(PreparedGlyphRun);
                    return false;
                }

                width += glyphs[index].advance;
            }

            // Font material/pass setup is a compatibility boundary. If the
            // active RimWorld font cannot provide it, the row cache must fall
            // back to the native label worker instead of drawing partial text.
            Material material = font.material;
            if (material == null || !material.SetPass(0))
            {
                glyphRun = default(PreparedGlyphRun);
                return false;
            }

            glyphRun = new PreparedGlyphRun(glyphs, width);
            return true;
        }

        private static bool Emit(
            Rect rect,
            string richText,
            Color baseColor,
            TextAnchor anchor,
            in PreparedGlyphRun glyphRun)
        {
            float xPosition = anchor == TextAnchor.MiddleCenter
                ? rect.center.x - (glyphRun.Width * 0.5f)
                : rect.xMin;
            float yCenter = rect.center.y;
            GL.Begin(GL.QUADS);
            try
            {
                int plainIndex = 0;
                Color color = baseColor;
                for (int sourceIndex = 0; sourceIndex < richText.Length; sourceIndex++)
                {
                    if (TryConsumeColorTag(richText, ref sourceIndex, baseColor, ref color))
                    {
                        continue;
                    }
                    if (richText[sourceIndex] == '<')
                    {
                        int close = richText.IndexOf('>', sourceIndex);
                        if (close >= 0)
                        {
                            sourceIndex = close;
                            continue;
                        }
                    }
                    if (plainIndex >= glyphRun.Glyphs.Length)
                    {
                        break;
                    }

                    CharacterInfo glyph = glyphRun.Glyphs[plainIndex++];
                    float verticalCenter = (glyph.maxY + glyph.minY) * 0.5f;
                    float baseline = yCenter + verticalCenter;
                    GL.Color(color);
                    GL.TexCoord(glyph.uvTopLeft);
                    GL.Vertex3(xPosition + glyph.minX, baseline - glyph.maxY, 0f);
                    GL.TexCoord(glyph.uvTopRight);
                    GL.Vertex3(xPosition + glyph.maxX, baseline - glyph.maxY, 0f);
                    GL.TexCoord(glyph.uvBottomRight);
                    GL.Vertex3(xPosition + glyph.maxX, baseline - glyph.minY, 0f);
                    GL.TexCoord(glyph.uvBottomLeft);
                    GL.Vertex3(xPosition + glyph.minX, baseline - glyph.minY, 0f);
                    xPosition += glyph.advance;
                }
            }
            finally
            {
                GL.End();
            }
            return true;
        }

        private static bool TryConsumeColorTag(
            string text,
            ref int index,
            Color baseColor,
            ref Color currentColor)
        {
            if (text[index] != '<')
            {
                return false;
            }
            int close = text.IndexOf('>', index);
            if (close < 0)
            {
                return false;
            }

            string tag = text.Substring(index + 1, close - index - 1);
            if (string.Equals(tag, "/color", StringComparison.OrdinalIgnoreCase))
            {
                currentColor = baseColor;
                index = close;
                return true;
            }
            if (!tag.StartsWith("color=#", StringComparison.OrdinalIgnoreCase) ||
                !ColorUtility.TryParseHtmlString("#" + tag.Substring(7), out Color parsed))
            {
                return false;
            }

            parsed.a *= baseColor.a;
            currentColor = parsed;
            index = close;
            return true;
        }

        private readonly struct PreparedGlyphRun
        {
            internal PreparedGlyphRun(CharacterInfo[] glyphs, float width)
            {
                Glyphs = glyphs;
                Width = width;
            }

            internal CharacterInfo[] Glyphs { get; }
            internal float Width { get; }
        }
    }
}
