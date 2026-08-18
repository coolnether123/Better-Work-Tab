using Spine.UI.SettingsFramework;
using UnityEngine;

namespace Better_Work_Tab.UI.Settings
{
    internal enum WorkTabColorPreviewTarget
    {
        Cell,
        SkillNumber,
        BestPawn,
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
        internal string FieldName { get; }

        internal bool IncludesRow =>
            Target == WorkTabColorPreviewTarget.Row ||
            Target == WorkTabColorPreviewTarget.RowAndColumn;

        internal bool IncludesColumn =>
            Target == WorkTabColorPreviewTarget.Column ||
            Target == WorkTabColorPreviewTarget.RowAndColumn;
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
        private SettingDefinition _settingPreviewDefinition;
        private int _settingPreviewFrame = -100;

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

        /// <summary>
        /// Remembers non-color row previews supplied by the shared drawer. This
        /// is observation-only; it never writes a temporary value into settings.
        /// </summary>
        public void PreviewSetting(
            SettingDefinition definition,
            object settingsObject,
            object value)
        {
            _settingPreviewDefinition = definition;
            _settingPreviewFrame = Time.frameCount;
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

        internal bool IsSkillPreviewActive =>
            TryGetPreview(out WorkTabColorPreview preview) &&
            (preview.Target == WorkTabColorPreviewTarget.SkillNumber ||
             preview.Target == WorkTabColorPreviewTarget.CellText);

        internal bool IsBestPawnPreviewActive =>
            TryGetPreview(out WorkTabColorPreview preview) &&
            (preview.Target == WorkTabColorPreviewTarget.BestPawn ||
             preview.Target == WorkTabColorPreviewTarget.CellIndicator);

        internal bool IsBestPawnThicknessPreviewActive =>
            _settingPreviewDefinition != null &&
            Time.frameCount - _settingPreviewFrame <= 1 &&
            _settingPreviewDefinition.FieldName == "bestPawnHighlightThickness";

        internal bool TryGetSkillColor(int level, out Color color)
        {
            color = default;
            if (!TryGetPreview(out WorkTabColorPreview preview) ||
                (preview.Target != WorkTabColorPreviewTarget.SkillNumber &&
                 preview.Target != WorkTabColorPreviewTarget.CellText))
            {
                return false;
            }

            bool appliesToLevel;
            switch (preview.FieldName)
            {
                case "Color_VeryLowSkill":
                    appliesToLevel = level <= 3;
                    break;
                case "Color_LowSkill":
                    appliesToLevel = level >= 4 && level <= 9;
                    break;
                case "Color_GoodLowSkill":
                    appliesToLevel = level >= 10 && level <= 15;
                    break;
                case "Color_ExcellentSkill":
                    appliesToLevel = level >= 16;
                    break;
                default:
                    appliesToLevel = false;
                    break;
            }

            if (!appliesToLevel)
            {
                return false;
            }

            color = preview.Color;
            return true;
        }

        internal bool TryGetBestPawnColor(out Color color)
        {
            color = default;
            if (!TryGetPreview(out WorkTabColorPreview preview) ||
                (preview.Target != WorkTabColorPreviewTarget.BestPawn &&
                 preview.Target != WorkTabColorPreviewTarget.CellIndicator))
            {
                return false;
            }

            color = preview.Color;
            return true;
        }

        internal bool TryGetHeaderTextColor(out Color color)
        {
            color = default;
            if (!TryGetPreview(out WorkTabColorPreview preview) ||
                preview.Target != WorkTabColorPreviewTarget.HeaderText)
            {
                return false;
            }

            color = preview.Color;
            return true;
        }

        internal bool TryGetHeaderUnderlineColor(out Color color)
        {
            color = default;
            if (!TryGetPreview(out WorkTabColorPreview preview) ||
                preview.Target != WorkTabColorPreviewTarget.HeaderUnderline)
            {
                return false;
            }

            color = preview.Color;
            return true;
        }

        private static WorkTabColorPreview CreatePreview(SettingDefinition definition, Color color)
        {
            return new WorkTabColorPreview(
                definition.Label ?? definition.Id ?? "Color",
                color,
                ResolveTarget(definition.FieldName),
                definition.FieldName);
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
                case "Color_CustomSimilarWorktypeHighlight":
                    return WorkTabColorPreviewTarget.Column;
                case "angledHeaderColor":
                case "Color_HeaderText":
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
                    return WorkTabColorPreviewTarget.SkillNumber;
                case "Color_BestPawnForSkillSquare":
                    return WorkTabColorPreviewTarget.BestPawn;
                case "headerUnderlineColor":
                    return WorkTabColorPreviewTarget.HeaderUnderline;
                case "Color_SettingFocusHighlight":
                    return WorkTabColorPreviewTarget.LegendOnly;
                default:
                    return WorkTabColorPreviewTarget.Cell;
            }
        }
    }
}
