using System;
using System.Collections.Generic;
using Spine.UI.WidgetExtensions;
using UnityEngine;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// A standard RimWorld choice menu with a non-overlapping setting description panel.
    /// </summary>
    public sealed class DescribedFloatMenu : FloatMenu
    {
        private const float MinimumOptionWidth = 140f;
        private const float MaximumOptionWidth = 300f;
        private const float HelpPanelWidth = 300f;
        private const float PanelGap = 10f;
        private const float PanelPadding = 8f;
        private const float SectionGap = 8f;
        private const float MinimumHelpHeight = 68f;
        private const float OptionSpacing = -1f;
        private const float MaxScreenHeightPercent = 0.9f;

        private static readonly Color WindowBorderColor = new Color(0.38f, 0.42f, 0.48f, 0.95f);
        private static readonly Color WindowFillColor = new Color(0.082f, 0.098f, 0.114f, 0.98f);
        private static readonly Color TipTitleColor = new Color(1f, 0.86f, 0.55f);

        private readonly FloatMenuOption selectedOption;
        private readonly string settingLabel;
        private readonly string description;
        private readonly IDictionary<FloatMenuOption, string> optionDescriptions;
        private Vector2 scrollPosition;
        private FloatMenuOption focusedOption;

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

        public override Vector2 InitialSize => new Vector2(
            OptionWidth + PanelGap + HelpPanelWidth,
            Mathf.Max(VisibleOptionHeight, RequiredHelpHeight));

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            float optionOffsetY = (size.y - VisibleOptionHeight) * 0.5f;
            Vector2 position = Verse.UI.MousePositionOnUIInverted + new Vector2(4f, -optionOffsetY);

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
                Rect optionRect = new Rect(
                    0f,
                    (rect.height - VisibleOptionHeight) * 0.5f,
                    optionWidth,
                    VisibleOptionHeight);
                float focusedCenterY = DrawOptions(optionRect);

                float helpHeight = RequiredHelpHeight;
                Rect helpRect = new Rect(
                    optionWidth + PanelGap,
                    (rect.height - helpHeight) * 0.5f,
                    HelpPanelWidth,
                    helpHeight);
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
                Rect rowRect = useScrollbar
                    ? new Rect(0f, y, contentWidth, option.RequiredHeight)
                    : new Rect(rect.x, rect.y + y, contentWidth, option.RequiredHeight);
                bool isSelected = ReferenceEquals(option, selectedOption);
                bool isHovered = Mouse.IsOver(rowRect);

#if RWT_FLOATMENU_DOGUI_CONTEXT
                if (option.DoGUI(rowRect, givesColonistOrders, this))
#else
                if (option.DoGUI(rowRect, givesColonistOrders))
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
                    Widgets.DrawHighlightSelected(rowRect.ContractedBy(1f));
                    selectedCenterY = useScrollbar
                        ? rect.y + rowRect.center.y - scrollPosition.y
                        : rowRect.center.y;
                }

                if (isHovered)
                {
                    hoveredOption = option;
                    hoveredCenterY = useScrollbar
                        ? rect.y + rowRect.center.y - scrollPosition.y
                        : rowRect.center.y;
                }

                y += option.RequiredHeight + OptionSpacing;
            }

            if (useScrollbar)
            {
                Widgets.EndScrollView();
            }

            focusedOption = hoveredOption ?? selectedOption;
            return hoveredOption != null ? hoveredCenterY : selectedCenterY;
        }

        private void DrawHelpPanel(Rect rect)
        {
            Widgets.DrawShadowAround(rect);
            Widgets.DrawWindowBackground(rect);
            Rect content = rect.ContractedBy(PanelPadding);

            Text.Font = GameFont.Small;
            string header = focusedOption == null
                ? settingLabel
                : settingLabel + ": " + focusedOption.Label;
            float headerHeight = Text.CalcHeight(header, content.width);
            GUI.color = TipTitleColor;
            Widgets.Label(new Rect(content.x, content.y, content.width, headerHeight), header);
            GUI.color = Color.white;
            float y = content.y + headerHeight;

            string focusedDescription = GetFocusedDescription();
            if (!string.IsNullOrEmpty(focusedDescription))
            {
                y += SectionGap;
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(content.x, y, content.width, content.yMax - y), focusedDescription);
            }
        }

        private static void DrawSelectionPointer(float optionEdgeX, float panelEdgeX, float y)
        {
            const float bridgeHalfHeight = 9f;
            const float arrowDepth = 9f;
            float bridgeStartX = optionEdgeX + arrowDepth;
            float panelJoinX = panelEdgeX + 1f;
            var pointer = new[]
            {
                new Vector2(optionEdgeX, y),
                new Vector2(bridgeStartX, y - bridgeHalfHeight),
                new Vector2(panelJoinX, y - bridgeHalfHeight),
                new Vector2(panelJoinX, y + bridgeHalfHeight),
                new Vector2(bridgeStartX, y + bridgeHalfHeight)
            };

            // Use the same miter-joined outline geometry as Rule Builder 2.0's
            // connected column/header selection so the arrow and bridge are seamless.
            ConnectedOutlineDrawer.DrawClosed(
                pointer,
                WindowBorderColor,
                1f,
                WindowFillColor);
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
                    Text.Font = GameFont.Small;
                    float headerHeight = Text.CalcHeight(settingLabel, contentWidth);

                    if (options.Count > 0)
                    {
                        for (int index = 0; index < options.Count; index++)
                        {
                            headerHeight = Mathf.Max(
                                headerHeight,
                                Text.CalcHeight(
                                    settingLabel + ": " + options[index].Label,
                                    contentWidth));
                        }
                    }

                    float height = PanelPadding + headerHeight;

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

                    if (descriptionHeight > 0f)
                    {
                        height += SectionGap + descriptionHeight;
                    }

                    height += PanelPadding;

                    return Mathf.Min(
                        Verse.UI.screenHeight * MaxScreenHeightPercent,
                        Mathf.Max(MinimumHelpHeight, height));
                }
                finally
                {
                    Text.Font = previousFont;
                }
            }
        }
    }
}
