using Spine.UI.SettingsFramework;
using UnityEngine;

namespace Better_Work_Tab.UI.Settings
{
    internal enum WorkTabColorPreviewTarget
    {
        Cell,
        CellText,
        CellIndicator,
        Row,
        Column,
        RowAndColumn,
        Header,
        HeaderText,
        Divider,
        LegendOnly
    }

    internal readonly struct WorkTabColorPreview
    {
        internal WorkTabColorPreview(string label, Color color, WorkTabColorPreviewTarget target)
        {
            Label = label;
            Color = color;
            Target = target;
        }

        internal string Label { get; }
        internal Color Color { get; }
        internal WorkTabColorPreviewTarget Target { get; }
    }

    /// <summary>
    /// Bridges generic settings color-preview events to a short-lived Work-tab preview state.
    /// Picker activity takes precedence over row hover so moving inside the dialog does not
    /// make the preview disappear.
    /// </summary>
    internal sealed class WorkTabColorPreviewController : ISettingColorPreviewSink
    {
        internal static readonly WorkTabColorPreviewController Instance = new WorkTabColorPreviewController();

        private SettingDefinition _hoveredDefinition;
        private Color _hoveredColor;
        private int _hoveredFrame = -100;
        private SettingDefinition _pickerDefinition;
        private Color _pickerColor;

        private WorkTabColorPreviewController()
        {
        }

        public void PreviewHover(SettingDefinition definition, Color color)
        {
            _hoveredDefinition = definition;
            _hoveredColor = color;
            _hoveredFrame = Time.frameCount;
        }

        public void BeginPicker(SettingDefinition definition, Color color)
        {
            _pickerDefinition = definition;
            _pickerColor = color;
        }

        public void PreviewPicker(SettingDefinition definition, Color color)
        {
            if (ReferenceEquals(_pickerDefinition, definition))
            {
                _pickerColor = color;
            }
        }

        public void EndPicker(SettingDefinition definition)
        {
            if (ReferenceEquals(_pickerDefinition, definition))
            {
                _pickerDefinition = null;
            }
        }

        internal bool TryGetPreview(out WorkTabColorPreview preview)
        {
            if (_pickerDefinition != null)
            {
                preview = CreatePreview(_pickerDefinition, _pickerColor);
                return true;
            }

            // The Work tab normally draws before the settings window. Retaining the hover for
            // one additional frame lets it consume the state produced by the previous OnGUI pass.
            if (_hoveredDefinition != null && Time.frameCount - _hoveredFrame <= 1)
            {
                preview = CreatePreview(_hoveredDefinition, _hoveredColor);
                return true;
            }

            preview = default;
            return false;
        }

        private static WorkTabColorPreview CreatePreview(SettingDefinition definition, Color color)
        {
            return new WorkTabColorPreview(
                definition.Label ?? definition.Id ?? "Color",
                color,
                ResolveTarget(definition.FieldName));
        }

        private static WorkTabColorPreviewTarget ResolveTarget(string fieldName)
        {
            switch (fieldName)
            {
                case "Color_CursorHighlight":
                case "Color_FloatMenuHighlight":
                    return WorkTabColorPreviewTarget.RowAndColumn;
                case "Color_RowHoverHighlight":
                case "Color_SelectedPawnHighlight":
                    return WorkTabColorPreviewTarget.Row;
                case "Color_ColumnHoverHighlight":
                case "Color_CustomMouseHighlight":
                case "Color_CustomSimilarWorktypeHighlight":
                    return WorkTabColorPreviewTarget.Column;
                case "angledHeaderColor":
                case "Color_HeaderText":
                case "headerUnderlineColor":
                    return WorkTabColorPreviewTarget.Header;
                case "movedMarkerColor":
                    // Moved-marker color changes the header glyphs themselves in both
                    // angled and vanilla header renderers; preview that exact semantic.
                    return WorkTabColorPreviewTarget.HeaderText;
                case "Color_DividerText":
                case "Color_Borders":
                    return WorkTabColorPreviewTarget.Divider;
                case "Color_VeryLowSkill":
                case "Color_LowSkill":
                case "Color_GoodLowSkill":
                case "Color_ExcellentSkill":
                    return WorkTabColorPreviewTarget.CellText;
                case "Color_BestPawnForSkillSquare":
                    return WorkTabColorPreviewTarget.CellIndicator;
                case "Color_SettingFocusHighlight":
                    return WorkTabColorPreviewTarget.LegendOnly;
                default:
                    return WorkTabColorPreviewTarget.Cell;
            }
        }
    }
}
