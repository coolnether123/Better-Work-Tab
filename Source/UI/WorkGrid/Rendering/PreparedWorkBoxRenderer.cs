using System;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Captures and paints the common visual portion of a pawn work box. The
    /// specialized parent and sub-work renderers add only their own interaction
    /// and composition behavior around this prepared primitive.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class PreparedWorkBoxRenderer
    {
        private static Material _retainedMaterial;

        internal static WorkBoxVisualState Capture(
            Pawn pawn,
            WorkTypeDef workType,
            int priority,
            bool disabled,
            bool incapable,
            bool ageDisabled,
            bool overrideRing,
            bool workActive,
            int bestPawnId)
        {
            float skill = Mathf.Clamp(pawn.skills.AverageOfRelevantSkillsFor(workType), 0f, 20f);
            byte skillBand;
            float skillBlend;
            if (skill < 4f)
            {
                skillBand = 0;
                skillBlend = skill / 4f;
            }
            else if (skill <= 14f)
            {
                skillBand = 1;
                skillBlend = (skill - 4f) / 10f;
            }
            else
            {
                skillBand = 2;
                skillBlend = (skill - 14f) / 6f;
            }

            byte passion = (byte)Mathf.Clamp(
                (int)pawn.skills.MaxPassionOfRelevantSkillsFor(workType),
                0,
                byte.MaxValue);
            WorkCellVisualFlags flags = WorkCellVisualFlags.None;
            if (disabled) flags |= WorkCellVisualFlags.Disabled;
            if (incapable) flags |= WorkCellVisualFlags.Incapable;
            if (ageDisabled) flags |= WorkCellVisualFlags.AgeDisabled;
            if (overrideRing) flags |= WorkCellVisualFlags.OverrideRing;
            if (passion > 0) flags |= WorkCellVisualFlags.HasPassion;
            if (ParentPriorityRead.GetObservedManualMode(pawn, workType, true))
                flags |= WorkCellVisualFlags.ManualPriorityMode;
            if (pawn.thingIDNumber == bestPawnId) flags |= WorkCellVisualFlags.BestPawn;
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                flags |= WorkCellVisualFlags.IdeologyWarning;
            if (workType.relevantSkills != null &&
                workType.relevantSkills.Count > 0 &&
                skill <= 2f &&
                workActive)
                flags |= WorkCellVisualFlags.LowSkillWarning;

            return new WorkBoxVisualState(
                (byte)Mathf.Clamp(priority, 0, byte.MaxValue),
                skillBand,
                skillBlend,
                passion,
                PackColor(WorkPrioritySystem.GetPriorityColor(priority)),
                flags);
        }

        internal static bool Draw(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            float visualAlpha)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.WordWrap = false;
                return DrawCore(
                    boxRect,
                    visual,
                    displayPriority,
                    visualAlpha,
                    oldColor,
                    prepareTextStyle: true);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

        /// <summary>
        /// Draws inside a caller-owned GUI state batch. The caller prepares the
        /// font, center alignment, and disabled word wrapping once for the batch.
        /// Color is restored for the next cell, while the outer scope restores the
        /// complete state when the row finishes.
        /// </summary>
        internal static bool DrawInBatch(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            float visualAlpha,
            Color batchColor)
        {
            try
            {
                return DrawCore(
                    boxRect,
                    visual,
                    displayPriority,
                    visualAlpha,
                    batchColor,
                    prepareTextStyle: false);
            }
            finally
            {
                GUI.color = batchColor;
            }
        }

        /// <summary>
        /// Composes the stable portion of a prepared work box into the currently
        /// active render target. The retained row cache owns target setup and later
        /// presents that target through IMGUI so RimWorld's scroll clipping remains
        /// authoritative.
        /// </summary>
        internal static bool DrawRetained(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            Color baseColor)
        {
            Material material = RetainedMaterial;
            if (material == null)
            {
                return false;
            }

            bool ageDisabled = (visual.Flags & WorkCellVisualFlags.AgeDisabled) != 0;
            if ((visual.Flags & WorkCellVisualFlags.Disabled) != 0)
            {
                if (ageDisabled)
                {
                    DrawRetainedTexture(boxRect, WidgetsWork.WorkBoxBGTex_AgeDisabled, baseColor, material);
                }
                return true;
            }

            Color cellColor = (visual.Flags & WorkCellVisualFlags.Incapable) != 0
                ? new Color(1f, 0.3f, 0.3f, baseColor.a)
                : baseColor;
            Texture2D baseTexture;
            Texture2D blendTexture;
            switch (visual.SkillBand)
            {
                case 0:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Awful;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    break;
                case 1:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    break;
                default:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Excellent;
                    break;
            }

            DrawRetainedTexture(boxRect, baseTexture, cellColor, material);
            if (visual.SkillBlend > 0.001f)
            {
                Color blendColor = cellColor;
                blendColor.a *= visual.SkillBlend;
                DrawRetainedTexture(boxRect, blendTexture, blendColor, material);
            }
            if ((visual.Flags & WorkCellVisualFlags.IdeologyWarning) != 0)
            {
                DrawRetainedTexture(
                    boxRect,
                    WidgetsWork.WorkBoxOverlay_PreceptWarning,
                    Color.white,
                    material);
            }
            if ((visual.Flags & WorkCellVisualFlags.LowSkillWarning) != 0)
            {
                DrawRetainedTexture(
                    boxRect.ContractedBy(-2f),
                    WidgetsWork.WorkBoxOverlay_Warning,
                    Color.white,
                    material);
            }
            if (visual.Passion > 0)
            {
                Rect passionRect = boxRect;
                passionRect.xMin = boxRect.center.x;
                passionRect.yMin = boxRect.center.y;
                DrawRetainedTexture(
                    passionRect,
                    visual.Passion == 1
                        ? WidgetsWork.PassionWorkboxMinorIcon
                        : WidgetsWork.PassionWorkboxMajorIcon,
                    new Color(1f, 1f, 1f, 0.4f),
                    material);
            }

            if ((visual.Flags & WorkCellVisualFlags.ManualPriorityMode) != 0)
            {
                if (displayPriority > WorkPrioritySystem.DisabledPriority)
                {
                    Color priorityColor = displayPriority == visual.Priority
                        ? UnpackColor(visual.PriorityColor)
                        : WorkPrioritySystem.GetPriorityColor(displayPriority);
                    priorityColor.a *= baseColor.a;
                    if (!DrawRetainedPriorityLabel(boxRect, displayPriority, priorityColor))
                    {
                        return false;
                    }
                }
            }
            else if (displayPriority > WorkPrioritySystem.DisabledPriority)
            {
                DrawRetainedTexture(boxRect, WidgetsWork.WorkBoxCheckTex, baseColor, material);
            }

            return true;
        }

        internal static void DrawDynamicOverlays(
            Rect boxRect,
            WorkBoxVisualState visual,
            Color baseColor)
        {
            Color oldColor = GUI.color;
            try
            {
                DrawStaticFeatureOverlays(boxRect, visual.Flags, baseColor.a);
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static Material RetainedMaterial
        {
            get
            {
                if (_retainedMaterial == null)
                {
                    Shader shader = Shader.Find("UI/Default") ?? ShaderDatabase.Transparent;
                    if (shader == null)
                    {
                        return null;
                    }

                    _retainedMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    _retainedMaterial.SetInt("_SrcBlend", 5);
                    _retainedMaterial.SetInt("_DstBlend", 10);
                    _retainedMaterial.SetInt("_Cull", 0);
                    _retainedMaterial.SetInt("_ZWrite", 0);
                }
                return _retainedMaterial;
            }
        }

        private static void DrawRetainedTexture(
            Rect rect,
            Texture texture,
            Color color,
            Material material)
        {
            if (texture == null)
            {
                return;
            }

            Graphics.DrawTexture(
                rect,
                texture,
                new Rect(0f, 0f, 1f, 1f),
                0,
                0,
                0,
                0,
                color,
                material);
        }

        private static bool DrawRetainedPriorityLabel(Rect boxRect, int priority, Color color)
        {
            GUIStyle style = Text.CurFontStyle;
            Font font = style?.font;
            if (font == null)
            {
                return false;
            }

            int fontSize = style.fontSize > 0 ? style.fontSize : font.fontSize;
            FontStyle fontStyle = style.fontStyle;
            string text = priority.ToStringCached();
            font.RequestCharactersInTexture(text, fontSize, fontStyle);

            float width = 0f;
            var glyphs = new CharacterInfo[text.Length];
            for (int index = 0; index < text.Length; index++)
            {
                if (!font.GetCharacterInfo(text[index], out glyphs[index], fontSize, fontStyle))
                {
                    return false;
                }
                width += glyphs[index].advance;
            }

            Material material = font.material;
            if (material == null || !material.SetPass(0))
            {
                return false;
            }

            float xPosition = boxRect.center.x - (width * 0.5f);
            GL.Begin(GL.QUADS);
            try
            {
                GL.Color(color);
                for (int index = 0; index < glyphs.Length; index++)
                {
                    CharacterInfo glyph = glyphs[index];
                    float verticalCenter = (glyph.maxY + glyph.minY) * 0.5f;
                    float baseline = boxRect.center.y + verticalCenter;
                    float xMin = xPosition + glyph.minX;
                    float xMax = xPosition + glyph.maxX;
                    float yMin = baseline - glyph.maxY;
                    float yMax = baseline - glyph.minY;

                    GL.TexCoord(glyph.uvTopLeft);
                    GL.Vertex3(xMin, yMin, 0f);
                    GL.TexCoord(glyph.uvTopRight);
                    GL.Vertex3(xMax, yMin, 0f);
                    GL.TexCoord(glyph.uvBottomRight);
                    GL.Vertex3(xMax, yMax, 0f);
                    GL.TexCoord(glyph.uvBottomLeft);
                    GL.Vertex3(xMin, yMax, 0f);
                    xPosition += glyph.advance;
                }
            }
            finally
            {
                GL.End();
            }
            return true;
        }

        internal static bool DrawRetainedText(
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
                GUIStyle style = Text.CurFontStyle;
                Font font = style?.font;
                if (font == null || string.IsNullOrEmpty(richText))
                {
                    return false;
                }

                string text = richText.StripTags();
                int fontSize = style.fontSize > 0 ? style.fontSize : font.fontSize;
                FontStyle fontStyle = style.fontStyle;
                font.RequestCharactersInTexture(text, fontSize, fontStyle);
                var glyphs = new CharacterInfo[text.Length];
                float width = 0f;
                for (int index = 0; index < text.Length; index++)
                {
                    if (!font.GetCharacterInfo(text[index], out glyphs[index], fontSize, fontStyle))
                    {
                        return false;
                    }
                    width += glyphs[index].advance;
                }

                Material material = font.material;
                if (material == null || !material.SetPass(0))
                {
                    return false;
                }

                float xPosition = anchor == TextAnchor.MiddleCenter
                    ? rect.center.x - (width * 0.5f)
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
                        if (plainIndex >= glyphs.Length)
                        {
                            break;
                        }

                        CharacterInfo glyph = glyphs[plainIndex++];
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
            finally
            {
                Text.Font = previousFont;
            }
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

        private static bool DrawCore(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            float visualAlpha,
            Color baseColor,
            bool prepareTextStyle)
        {
            if (visualAlpha <= 0.001f)
            {
                return true;
            }

            bool ageDisabled = (visual.Flags & WorkCellVisualFlags.AgeDisabled) != 0;
            if ((visual.Flags & WorkCellVisualFlags.Disabled) != 0)
            {
                if (ageDisabled)
                {
                    GUI.color = WithAlpha(baseColor, visualAlpha);
                    GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxBGTex_AgeDisabled);
                }
                return false;
            }

            GUI.color = (visual.Flags & WorkCellVisualFlags.Incapable) != 0
                ? new Color(1f, 0.3f, 0.3f, baseColor.a * visualAlpha)
                : WithAlpha(baseColor, visualAlpha);
            DrawBackground(boxRect, visual, visualAlpha);
            DrawForeground(
                boxRect,
                visual,
                displayPriority,
                visualAlpha,
                baseColor,
                prepareTextStyle);
            return true;
        }

        private static void DrawForeground(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            float visualAlpha,
            Color baseColor,
            bool prepareTextStyle)
        {
            if ((visual.Flags & WorkCellVisualFlags.ManualPriorityMode) != 0)
            {
                if (displayPriority > WorkPrioritySystem.DisabledPriority)
                {
                    if (prepareTextStyle)
                    {
                        Text.Font = boxRect.width <= WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f
                            ? GameFont.Tiny
                            : GameFont.Medium;
                        Text.Anchor = TextAnchor.MiddleCenter;
                    }
                    Color color = displayPriority == visual.Priority
                        ? UnpackColor(visual.PriorityColor)
                        : WorkPrioritySystem.GetPriorityColor(displayPriority);
                    color.a *= baseColor.a * visualAlpha;
                    GUI.color = color;
                    Widgets.Label(boxRect.ContractedBy(-3f), displayPriority.ToStringCached());
                }
            }
            else if (displayPriority > WorkPrioritySystem.DisabledPriority)
            {
                GUI.color = WithAlpha(baseColor, visualAlpha);
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
            }

            if ((visual.Flags & (WorkCellVisualFlags.BestPawn | WorkCellVisualFlags.OverrideRing)) != 0)
            {
                DrawStaticFeatureOverlays(boxRect, visual.Flags, baseColor.a * visualAlpha);
            }
        }

        private static void DrawBackground(
            Rect boxRect,
            WorkBoxVisualState visual,
            float visualAlpha)
        {
            Texture2D baseTexture;
            Texture2D blendTexture;
            switch (visual.SkillBand)
            {
                case 0:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Awful;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    break;
                case 1:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Bad;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    break;
                default:
                    baseTexture = WidgetsWork.WorkBoxBGTex_Mid;
                    blendTexture = WidgetsWork.WorkBoxBGTex_Excellent;
                    break;
            }

            Color baseColor = GUI.color;
            GUI.DrawTexture(boxRect, baseTexture);
            GUI.color = new Color(
                baseColor.r,
                baseColor.g,
                baseColor.b,
                baseColor.a * visual.SkillBlend);
            GUI.DrawTexture(boxRect, blendTexture);

            if ((visual.Flags & WorkCellVisualFlags.IdeologyWarning) != 0)
            {
                GUI.color = new Color(1f, 1f, 1f, visualAlpha);
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxOverlay_PreceptWarning);
            }
            if ((visual.Flags & WorkCellVisualFlags.LowSkillWarning) != 0)
            {
                GUI.color = new Color(1f, 1f, 1f, visualAlpha);
                GUI.DrawTexture(boxRect.ContractedBy(-2f), WidgetsWork.WorkBoxOverlay_Warning);
            }
            if (visual.Passion > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f * visualAlpha);
                Rect passionRect = boxRect;
                passionRect.xMin = boxRect.center.x;
                passionRect.yMin = boxRect.center.y;
                GUI.DrawTexture(
                    passionRect,
                    visual.Passion == 1
                        ? WidgetsWork.PassionWorkboxMinorIcon
                        : WidgetsWork.PassionWorkboxMajorIcon);
            }
        }

        private static void DrawStaticFeatureOverlays(
            Rect boxRect,
            WorkCellVisualFlags flags,
            float visualAlpha)
        {
            if ((flags & (WorkCellVisualFlags.Disabled | WorkCellVisualFlags.Incapable)) != 0)
            {
                return;
            }

            if ((flags & WorkCellVisualFlags.BestPawn) != 0)
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                if (settings != null)
                {
                    BetterWorkTabSettings.ShowUIMode mode = settings.ShowUIMode_ShowPawnForSkillSquare;
                    if (mode == BetterWorkTabSettings.ShowUIMode.Always || mode == ShiftHelper.State)
                    {
                        Color outlineColor = settings.Color_BestPawnForSkillSquare;
                        outlineColor.a *= visualAlpha;
                        Widgets.DrawBoxSolidWithOutline(
                            boxRect.ExpandedBy(1f),
                            Color.clear,
                            outlineColor,
                            BWTWorkTabEffectiveSettings.GetInt(SettingIDs.HighlightsBestPawnBackground));
                    }
                }
            }

            if (visualAlpha > 0.999f && (flags & WorkCellVisualFlags.OverrideRing) != 0)
            {
                PriorityOverrideRing.Draw(boxRect);
            }
        }

        private static uint PackColor(Color color)
        {
            Color32 value = color;
            return (uint)(value.r | (value.g << 8) | (value.b << 16) | (value.a << 24));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        private static Color UnpackColor(uint packed)
        {
            return new Color32(
                (byte)(packed & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)((packed >> 16) & 0xFF),
                (byte)((packed >> 24) & 0xFF));
        }
    }
}
