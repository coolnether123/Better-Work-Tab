using System;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal enum RetainedWorkBoxDrawFailure : byte
    {
        None,
        ResourceUnavailable,
        Unsupported
    }

    /// <summary>
    /// Captures and paints the common visual portion of a pawn work box. The
    /// specialized parent and sub-work renderers add only their own interaction
    /// and composition behavior around this prepared primitive.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class PreparedWorkBoxRenderer
    {
        internal const float LowSkillWarningOutset = 2f;
        internal const float PriorityLabelOutset = 3f;

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
            Color baseColor,
            out RetainedWorkBoxDrawFailure failure)
        {
            failure = RetainedWorkBoxDrawFailure.None;
            bool ageDisabled = (visual.Flags & WorkCellVisualFlags.AgeDisabled) != 0;
            if ((visual.Flags & WorkCellVisualFlags.Disabled) != 0)
            {
                if (ageDisabled)
                {
                    if (!DrawRetainedTexture(boxRect, WidgetsWork.WorkBoxBGTex_AgeDisabled, baseColor))
                    {
                        failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                        return false;
                    }
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

            if (!DrawRetainedTexture(boxRect, baseTexture, cellColor))
            {
                failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                return false;
            }
            if (visual.SkillBlend > 0.001f)
            {
                Color blendColor = cellColor;
                blendColor.a *= visual.SkillBlend;
                if (!DrawRetainedTexture(boxRect, blendTexture, blendColor))
                {
                    failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                    return false;
                }
            }
            if ((visual.Flags & WorkCellVisualFlags.IdeologyWarning) != 0)
            {
                if (!DrawRetainedTexture(
                        boxRect,
                        WidgetsWork.WorkBoxOverlay_PreceptWarning,
                        Color.white))
                {
                    failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                    return false;
                }
            }
            // Semi-transparent foreground pixels stay on the live IMGUI pass.
            // Composing them into this transparent surface would blend them a
            // second time when the surface is presented. The retained surface
            // still owns opaque box textures and the checkbox texture.
            if (!HasPriorityLabel(visual, displayPriority) &&
                displayPriority > WorkPrioritySystem.DisabledPriority)
            {
                if (!DrawRetainedTexture(boxRect, WidgetsWork.WorkBoxCheckTex, baseColor))
                {
                    failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Proves once per render-resource generation that the immediate
        /// texture path can write a known pixel to a render texture. The row
        /// cache owns the generation latch; this renderer owns the material and
        /// the target state used by the probe.
        /// </summary>
        internal static bool TryValidateRetainedComposition(
            out RetainedWorkBoxDrawFailure failure)
        {
            failure = RetainedWorkBoxDrawFailure.None;
            RenderTexture previous = RenderTexture.active;
            int previousViewportWidth = previous == null ? Screen.width : previous.width;
            int previousViewportHeight = previous == null ? Screen.height : previous.height;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture surface = null;
            Texture2D readback = null;
            bool matrixPushed = false;
            try
            {
                surface = new RenderTexture(
                    1,
                    1,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB)
                {
                    name = "BWT retained work capability sentinel",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!surface.Create())
                {
                    failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                    return false;
                }

                readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                RenderTexture.active = surface;
                GL.InvalidateState();
                ConfigureSrgbWriteForSrgbTarget();
                GL.Viewport(new Rect(0f, 0f, surface.width, surface.height));
                GUI.matrix = Matrix4x4.identity;
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadPixelMatrix(0f, 1f, 1f, 0f);
                GL.Clear(true, true, Color.clear);
                if (!DrawRetainedTexture(
                        new Rect(0f, 0f, 1f, 1f),
                        Texture2D.whiteTexture,
                        Color.red))
                {
                    failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                    return false;
                }

                GL.PopMatrix();
                matrixPushed = false;
                readback.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, false);
                readback.Apply(false, false);
                Color pixel = readback.GetPixel(0, 0);
                if (pixel.r < 0.5f || pixel.a < 0.5f ||
                    pixel.g > 0.5f || pixel.b > 0.5f)
                {
                    failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                    return false;
                }

                return true;
            }
            catch (NotSupportedException)
            {
                failure = RetainedWorkBoxDrawFailure.Unsupported;
                return false;
            }
            catch (Exception)
            {
                failure = RetainedWorkBoxDrawFailure.ResourceUnavailable;
                return false;
            }
            finally
            {
                if (matrixPushed)
                {
                    GL.PopMatrix();
                }

                RenderTexture.active = previous;
                if (previousViewportWidth > 0 && previousViewportHeight > 0)
                {
                    GL.Viewport(new Rect(
                        0f,
                        0f,
                        previousViewportWidth,
                        previousViewportHeight));
                }
                GL.sRGBWrite = previousSrgbWrite;
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GL.InvalidateState();

                if (readback != null)
                {
                    UnityEngine.Object.Destroy(readback);
                }
                if (surface != null)
                {
                    if (surface.IsCreated())
                    {
                        surface.Release();
                    }
                    UnityEngine.Object.Destroy(surface);
                }
            }
        }

        internal static void ReleaseRetainedResources()
        {
            Material retainedMaterial = _retainedMaterial;
            _retainedMaterial = null;
            if (retainedMaterial != null)
            {
                UnityEngine.Object.Destroy(retainedMaterial);
            }
        }

        // RenderTextureReadWrite.sRGB does not set GL.sRGBWrite. Derive it from
        // project color space, never the preceding IMGUI draw.
        internal static void ConfigureSrgbWriteForSrgbTarget()
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
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
                    Shader shader = ShaderDatabase.Transparent ?? Shader.Find("UI/Default");
                    if (shader == null)
                    {
                        return null;
                    }

                    _retainedMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    _retainedMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    _retainedMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    _retainedMaterial.SetInt("_Cull", (int)CullMode.Off);
                    _retainedMaterial.SetInt("_ZWrite", 0);
                }
                return _retainedMaterial;
            }
        }

        internal static bool HasLiveLowSkillWarning(WorkBoxVisualState visual)
        {
            return (visual.Flags & WorkCellVisualFlags.LowSkillWarning) != 0 &&
                   (visual.Flags & WorkCellVisualFlags.Disabled) == 0;
        }

        internal static bool HasLivePassionIcon(WorkBoxVisualState visual)
        {
            return visual.Passion > 0 &&
                   (visual.Flags & WorkCellVisualFlags.Disabled) == 0;
        }

        internal static bool HasLiveForeground(
            WorkBoxVisualState visual,
            int displayPriority)
        {
            return HasLiveLowSkillWarning(visual) ||
                   HasLivePassionIcon(visual) ||
                   HasPriorityLabel(visual, displayPriority);
        }

        /// <summary>
        /// Draws the low-skill warning on the live IMGUI target. Its transparent
        /// border must be composed once against the final work-tab background.
        /// </summary>
        internal static void DrawLiveLowSkillWarning(
            Rect boxRect,
            WorkBoxVisualState visual,
            float visualAlpha)
        {
            if (!HasLiveLowSkillWarning(visual) || visualAlpha <= 0.001f)
            {
                return;
            }

            Color previousColor = GUI.color;
            try
            {
                GUI.color = new Color(1f, 1f, 1f, visualAlpha);
                GUI.DrawTexture(
                    boxRect.ContractedBy(-LowSkillWarningOutset),
                    WidgetsWork.WorkBoxOverlay_Warning);
            }
            finally
            {
                GUI.color = previousColor;
            }
        }

        internal static void DrawLivePassionIcon(
            Rect boxRect,
            WorkBoxVisualState visual,
            float visualAlpha)
        {
            if (!HasLivePassionIcon(visual) || visualAlpha <= 0.001f)
            {
                return;
            }

            Color previousColor = GUI.color;
            try
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f * visualAlpha);
                DrawPassionIcon(boxRect, visual);
            }
            finally
            {
                GUI.color = previousColor;
            }
        }

        private static bool DrawRetainedTexture(
            Rect rect,
            Texture texture,
            Color color)
        {
            if (texture == null || rect.width <= 0f || rect.height <= 0f)
            {
                return false;
            }

            Material material = RetainedMaterial;
            if (material == null)
            {
                return false;
            }

            // GUI.DrawTexture and Graphics.DrawTexture queue IMGUI/graphics
            // work against the caller's target. Emit the quad directly while
            // the row cache owns RenderTexture.active, so the target cannot be
            // restored before the stable pixels are written. The explicit UV
            // flip keeps this top-left pixel matrix oriented like IMGUI.
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", color);
            if (!material.SetPass(0))
            {
                return false;
            }
            GL.Begin(GL.QUADS);
            try
            {
                GL.Color(Color.white);
                GL.TexCoord2(0f, 1f);
                GL.Vertex3(rect.xMin, rect.yMin, 0f);
                GL.TexCoord2(1f, 1f);
                GL.Vertex3(rect.xMax, rect.yMin, 0f);
                GL.TexCoord2(1f, 0f);
                GL.Vertex3(rect.xMax, rect.yMax, 0f);
                GL.TexCoord2(0f, 0f);
                GL.Vertex3(rect.xMin, rect.yMax, 0f);
                return true;
            }
            finally
            {
                GL.End();
            }
        }

        internal static bool HasPriorityLabel(
            WorkBoxVisualState visual,
            int displayPriority)
        {
            return (visual.Flags & WorkCellVisualFlags.Disabled) == 0 &&
                   (visual.Flags & WorkCellVisualFlags.ManualPriorityMode) != 0 &&
                   displayPriority > WorkPrioritySystem.DisabledPriority;
        }

        /// <summary>
        /// Draws one manual priority numeral on the live screen pass. The caller
        /// owns the surrounding GUI-state scope when drawing a prepared run;
        /// keeping this method state-light avoids a capture/restore per cell.
        /// </summary>
        internal static void DrawLivePriorityLabel(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            Color baseColor,
            float visualAlpha,
            bool compactText)
        {
            if (!HasPriorityLabel(visual, displayPriority))
            {
                return;
            }

            GameFont font = compactText ? GameFont.Tiny : GameFont.Medium;
            if (Text.Font != font)
            {
                Text.Font = font;
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            DrawPriorityLabel(
                boxRect,
                visual,
                displayPriority,
                baseColor,
                visualAlpha,
                prepareTextStyle: false);
        }

        /// <summary>
        /// Replays the transparent foreground in direct-draw order after a
        /// retained work-box surface has been presented.
        /// </summary>
        internal static void DrawLiveForeground(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            Color baseColor,
            float visualAlpha,
            bool compactText)
        {
            DrawLiveLowSkillWarning(boxRect, visual, visualAlpha);
            DrawLivePassionIcon(boxRect, visual, visualAlpha);
            DrawLivePriorityLabel(
                boxRect,
                visual,
                displayPriority,
                baseColor,
                visualAlpha,
                compactText);
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
                if (HasPriorityLabel(visual, displayPriority))
                {
                    DrawPriorityLabel(
                        boxRect,
                        visual,
                        displayPriority,
                        baseColor,
                        visualAlpha,
                        prepareTextStyle);
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

        private static void DrawPriorityLabel(
            Rect boxRect,
            WorkBoxVisualState visual,
            int displayPriority,
            Color baseColor,
            float visualAlpha,
            bool prepareTextStyle)
        {
            if (!HasPriorityLabel(visual, displayPriority))
            {
                return;
            }

            if (prepareTextStyle)
            {
                Text.Font = boxRect.width <= WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f
                    ? GameFont.Tiny
                    : GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
            }

            Color color = displayPriority == visual.Priority
                ? UnpackColor(visual.PriorityColor)
                : WorkPrioritySystem.GetPriorityColor(displayPriority);
            color.a *= baseColor.a * visualAlpha;
            GUI.color = color;
            Widgets.Label(
                boxRect.ContractedBy(-PriorityLabelOutset),
                displayPriority.ToStringCached());
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
                GUI.DrawTexture(
                    boxRect.ContractedBy(-LowSkillWarningOutset),
                    WidgetsWork.WorkBoxOverlay_Warning);
            }
            if (visual.Passion > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f * visualAlpha);
                DrawPassionIcon(boxRect, visual);
            }
        }

        private static void DrawPassionIcon(Rect boxRect, WorkBoxVisualState visual)
        {
            Rect passionRect = boxRect;
            passionRect.xMin = boxRect.center.x;
            passionRect.yMin = boxRect.center.y;
            GUI.DrawTexture(
                passionRect,
                visual.Passion == 1
                    ? WidgetsWork.PassionWorkboxMinorIcon
                    : WidgetsWork.PassionWorkboxMajorIcon);
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
