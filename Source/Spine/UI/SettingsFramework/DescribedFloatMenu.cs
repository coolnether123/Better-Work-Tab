using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// A standard RimWorld choice menu with a non-overlapping setting description panel.
    /// </summary>
 #if v0_13
    /// <summary>
    /// 0.13's FloatMenuOption does not expose the later public layout and draw
    /// members used by the described menu.  Retain every option and use the
    /// native menu rather than reflecting into private game UI internals.
    /// </summary>
    public sealed class DescribedFloatMenu : FloatMenu
    {
        public DescribedFloatMenu(
            List<FloatMenuOption> options,
            FloatMenuOption selectedOption,
            string settingLabel,
            string description,
            IDictionary<FloatMenuOption, string> optionDescriptions = null)
            : base(options)
        {
        }

        public static bool AnyOpen => Find.WindowStack != null && Find.WindowStack.IsOpen<DescribedFloatMenu>();
    }
#else
    public sealed class DescribedFloatMenu : FloatMenu
    {
        private const float MinimumOptionWidth = 140f;
        private const float MaximumOptionWidth = 300f;
        private const float HelpPanelWidth = 320f;
        private const float PanelGap = 20f;
        private const float PanelPadding = 16f;
        private const float OptionSpacing = -1f;
        private const float MaxScreenHeightPercent = 0.9f;

        private static readonly Color SelectionColor = new Color(0.9f, 0.78f, 0.45f, 0.95f);
        private static readonly Color ContextFillColor = new Color(0.105f, 0.12f, 0.135f, 0.98f);

        private readonly FloatMenuOption selectedOption;
        private readonly string settingLabel;
        private readonly string description;
        private readonly IDictionary<FloatMenuOption, string> optionDescriptions;
        private Vector2 scrollPosition;
        private FloatMenuOption focusedOption;
        private bool previewingOption;

        public DescribedFloatMenu(
            List<FloatMenuOption> options,
            FloatMenuOption selectedOption,
            string settingLabel,
            string description,
            IDictionary<FloatMenuOption, string> optionDescriptions = null)
            : base(options)
        {
            this.selectedOption = selectedOption;
            this.settingLabel = settingLabel ?? string.Empty;
            this.description = description ?? string.Empty;
            this.optionDescriptions = optionDescriptions;

            // The help panel is part of the menu, so distance-based fading would make it
            // disappear while the player moves from the choices to the explanation.
            vanishIfMouseDistant = false;
        }

        public static bool AnyOpen => Find.WindowStack != null && Find.WindowStack.IsOpen<DescribedFloatMenu>();

#if !v0_13
        public override Vector2 InitialSize => new Vector2(
            OptionWidth + PanelGap + HelpPanelWidth,
            Mathf.Max(VisibleOptionHeight, RequiredHelpHeight));

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            Vector2 position = Better_Work_Tab.UICompat.MousePosUIInvertedUseEventIfCan + new Vector2(4f, 0f);

            if (position.x + size.x > Verse.UI.screenWidth)
            {
                position.x = Verse.UI.screenWidth - size.x;
            }

            if (position.y + size.y > Verse.UI.screenHeight)
            {
                position.y = Verse.UI.screenHeight - size.y;
            }

            position.x = Mathf.Max(0f, position.x);
            position.y = Mathf.Max(0f, position.y);
            windowRect = new Rect(position.x, position.y, size.x, size.y);
        }
#endif

        public override void DoWindowContents(Rect rect)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWordWrap = Text.WordWrap;
            Color previousColor = GUI.color;

            try
            {
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;

                float optionWidth = OptionWidth;
                Rect optionRect = new Rect(0f, 0f, optionWidth, VisibleOptionHeight);
                float focusedCenterY = DrawOptions(optionRect);

                Rect helpRect = new Rect(optionWidth + PanelGap, 0f, HelpPanelWidth, rect.height);
                DrawHelpPanel(helpRect);

                if (focusedCenterY >= optionRect.y && focusedCenterY <= optionRect.yMax)
                {
                    DrawSelectionPointer(optionRect.xMax + 1f, helpRect.x, focusedCenterY);
                }

                if (Event.current.type == EventType.MouseDown)
                {
                    Event.current.Use();
                    Close();
                }
            }
            finally
            {
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
                Text.WordWrap = previousWordWrap;
                GUI.color = previousColor;
            }
        }

        private float DrawOptions(Rect rect)
        {
            bool useScrollbar = TotalOptionHeight > rect.height;
            float contentWidth = useScrollbar ? rect.width - 16f : rect.width;
            Rect viewRect = new Rect(0f, 0f, contentWidth, TotalOptionHeight);
            float y = 0f;
            float selectedCenterY = -1f;
            float hoveredCenterY = -1f;
            FloatMenuOption hoveredOption = null;

            if (useScrollbar)
            {
                Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            }

            for (int index = 0; index < options.Count; index++)
            {
                FloatMenuOption option = options[index];
                Rect rowRect = new Rect(0f, y, contentWidth, option.RequiredHeight);
                bool isSelected = ReferenceEquals(option, selectedOption);
                bool isHovered = Mouse.IsOver(rowRect);

                if (isSelected)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(SelectionColor.r, SelectionColor.g, SelectionColor.b, 0.16f);
                    Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rowRect.ContractedBy(1f), GUI.color);
                    GUI.color = oldColor;
                }

#if v0_18
                if (option.DoGUI(rowRect, givesColonistOrders))
#else
                if (option.DoGUI(rowRect, givesColonistOrders, this))
#endif
                {
                    Find.WindowStack.TryRemove(this);
                    if (useScrollbar)
                    {
                        Widgets.EndScrollView();
                    }

                    return -1f;
                }

                if (isSelected)
                {
                    Color oldColor = GUI.color;
                    GUI.color = SelectionColor;
                    Widgets.DrawBox(rowRect.ContractedBy(1f), 2);
                    GUI.color = oldColor;
                    selectedCenterY = rowRect.center.y - (useScrollbar ? scrollPosition.y : 0f);
                }

                if (isHovered)
                {
                    hoveredOption = option;
                    hoveredCenterY = rowRect.center.y - (useScrollbar ? scrollPosition.y : 0f);
                }

                y += option.RequiredHeight + OptionSpacing;
            }

            if (useScrollbar)
            {
                Widgets.EndScrollView();
            }

            focusedOption = hoveredOption ?? selectedOption;
            previewingOption = hoveredOption != null && !ReferenceEquals(hoveredOption, selectedOption);
            return hoveredOption != null ? hoveredCenterY : selectedCenterY;
        }

        private void DrawHelpPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect content = rect.ContractedBy(PanelPadding);

            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(settingLabel, content.width);
            Widgets.Label(new Rect(content.x, content.y, content.width, titleHeight), settingLabel);
            float y = content.y + titleHeight + 10f;

            Color previousColor = GUI.color;
            GUI.color = SelectionColor;
            Widgets.DrawLineHorizontal(content.x, y, content.width);
            GUI.color = previousColor;
            y += 13f;

            if (focusedOption != null)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                string contextLabel = previewingOption
                    ? "BWT_Settings_UI_PreviewingOption".Translate()
                    : "BWT_Settings_UI_CurrentSelection".Translate();
                Widgets.Label(new Rect(content.x, y, content.width, 18f), contextLabel);
                GUI.color = Color.white;
                y += 18f;

                // Medium is RimWorld's emphasized menu type and gives the active option a
                // clear visual hierarchy without introducing a separate font asset.
                Text.Font = GameFont.Medium;
                GUI.color = SelectionColor;
                float selectionHeight = Text.CalcHeight(focusedOption.Label, content.width);
                Widgets.Label(new Rect(content.x, y, content.width, selectionHeight), focusedOption.Label);
                GUI.color = Color.white;
                y += selectionHeight + 13f;
            }

            string focusedDescription = GetFocusedDescription();
            if (!string.IsNullOrEmpty(focusedDescription))
            {
                Text.Font = GameFont.Small;
                GUI.color = new Color(0.88f, 0.88f, 0.88f);
                Widgets.Label(new Rect(content.x, y, content.width, content.yMax - y), focusedDescription);
            }
        }

        private static void DrawSelectionPointer(float optionEdgeX, float panelEdgeX, float y)
        {
            const float bridgeHalfHeight = 9f;
            const float arrowDepth = 9f;
            Rect bridge = Rect.MinMaxRect(
                optionEdgeX + arrowDepth,
                y - bridgeHalfHeight,
                panelEdgeX + 1f,
                y + bridgeHalfHeight);

            // The filled bridge shares the panel's visual weight so the pointer reads as one
            // connected callout instead of a loose line floating between two windows.
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(bridge, ContextFillColor);
            Widgets.DrawLine(new Vector2(bridge.xMin, bridge.yMin), new Vector2(bridge.xMax, bridge.yMin), SelectionColor, 2f);
            Widgets.DrawLine(new Vector2(bridge.xMin, bridge.yMax), new Vector2(bridge.xMax, bridge.yMax), SelectionColor, 2f);

            Vector2 target = new Vector2(optionEdgeX, y);
            Widgets.DrawLine(target, new Vector2(bridge.xMin, bridge.yMin), SelectionColor, 2f);
            Widgets.DrawLine(target, new Vector2(bridge.xMin, bridge.yMax), SelectionColor, 2f);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(new Rect(panelEdgeX - 2f, y - bridgeHalfHeight, 4f, bridgeHalfHeight * 2f), SelectionColor);
        }

        private string GetFocusedDescription()
        {
            if (focusedOption != null &&
                optionDescriptions != null &&
                optionDescriptions.TryGetValue(focusedOption, out string optionDescription) &&
                !string.IsNullOrEmpty(optionDescription))
            {
                return optionDescription;
            }

            return description;
        }

        private float OptionWidth
        {
            get
            {
                float width = MinimumOptionWidth;
                for (int index = 0; index < options.Count; index++)
                {
                    width = Mathf.Max(width, options[index].RequiredWidth);
                }

                return Mathf.Min(MaximumOptionWidth, Mathf.Round(width));
            }
        }

        private float TotalOptionHeight
        {
            get
            {
                float height = 1f;
                for (int index = 0; index < options.Count; index++)
                {
                    height += options[index].RequiredHeight + OptionSpacing;
                }

                return Mathf.Max(1f, height);
            }
        }

        private float VisibleOptionHeight => Mathf.Min(TotalOptionHeight, Verse.UI.screenHeight * MaxScreenHeightPercent);

        private float RequiredHelpHeight
        {
            get
            {
                GameFont previousFont = Text.Font;
                try
                {
                    float contentWidth = HelpPanelWidth - (PanelPadding * 2f);
                    Text.Font = GameFont.Medium;
                    float height = PanelPadding + Text.CalcHeight(settingLabel, contentWidth) + 23f;

                    if (options.Count > 0)
                    {
                        Text.Font = GameFont.Medium;
                        float optionLabelHeight = 0f;
                        for (int index = 0; index < options.Count; index++)
                        {
                            optionLabelHeight = Mathf.Max(optionLabelHeight, Text.CalcHeight(options[index].Label, contentWidth));
                        }

                        height += 31f + optionLabelHeight;
                    }

                    Text.Font = GameFont.Small;
                    float descriptionHeight = string.IsNullOrEmpty(description)
                        ? 0f
                        : Text.CalcHeight(description, contentWidth);
                    if (optionDescriptions != null)
                    {
                        foreach (string optionDescription in optionDescriptions.Values)
                        {
                            if (!string.IsNullOrEmpty(optionDescription))
                            {
                                descriptionHeight = Mathf.Max(descriptionHeight, Text.CalcHeight(optionDescription, contentWidth));
                            }
                        }
                    }

                    height += descriptionHeight;

                    return Mathf.Min(Verse.UI.screenHeight * MaxScreenHeightPercent, Mathf.Max(140f, height + PanelPadding));
                }
                finally
                {
                    Text.Font = previousFont;
                }
            }
        }
    }
#endif
}
