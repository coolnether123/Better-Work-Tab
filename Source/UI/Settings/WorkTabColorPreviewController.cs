using System.Reflection;
using Spine.UI.SettingsFramework;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using UnityEngine;

namespace Better_Work_Tab.UI.Settings
{
    internal enum WorkTabColorPreviewTarget
    {
        Cell,
        SkillNumber,
        BestPawn,
        Row,
        Column,
        RowAndColumn,
        HeaderText,
        HeaderUnderline,
        Divider,
        LegendOnly
    }

    internal readonly struct WorkTabColorPreview
    {
        internal WorkTabColorPreview(
            string label,
            Color color,
            WorkTabColorPreviewTarget target,
            string fieldName)
        {
            Label = label;
            Color = color;
            Target = target;
            FieldName = fieldName;
        }

        internal string Label { get; }
        internal Color Color { get; }
        internal WorkTabColorPreviewTarget Target { get; }
        internal string FieldName { get; }

        internal bool IncludesRow
        {
            get
            {
                return Target == WorkTabColorPreviewTarget.Row ||
                       Target == WorkTabColorPreviewTarget.RowAndColumn;
            }
        }

        internal bool IncludesColumn
        {
            get
            {
                return Target == WorkTabColorPreviewTarget.Column ||
                       Target == WorkTabColorPreviewTarget.RowAndColumn;
            }
        }
    }

    /// <summary>
    /// Bridges generic settings color-preview events to a short-lived Work-tab preview state.
    /// Picker activity takes precedence over row hover so moving inside the dialog does not
    /// make the preview disappear.
    /// </summary>
    internal sealed class WorkTabColorPreviewController :
        ISettingColorPreviewSink,
        ISettingColorPreviewTransactionSink
    {
        private const string MasterHighlightSettingId = "highlights.masterColor";
        private const string MasterHighlightPreviewField = "preview.masterHighlight";

        internal static readonly WorkTabColorPreviewController Instance = new WorkTabColorPreviewController();

        private SettingDefinition _hoveredDefinition;
        private Color _hoveredColor;
        private int _hoveredFrame = -100;
        private SettingDefinition _pickerDefinition;
        private Color _pickerColor;
        private SettingDefinition _settingPreviewDefinition;
        private int _settingPreviewFrame = -100;
        private Color _masterHoverColor;
        private int _masterHoverFrame = -100;
        private bool _masterPickerActive;
        private Color _masterPickerColor;

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

        public void Begin(SettingDefinition definition, object settingsObject, Color originalColor)
        {
            ApplyTemporaryColor(definition, settingsObject, originalColor);
        }

        public void Preview(SettingDefinition definition, object settingsObject, Color color)
        {
            ApplyTemporaryColor(definition, settingsObject, color);
        }

        public void Commit(SettingDefinition definition, object settingsObject, Color color)
        {
            ApplyTemporaryColor(definition, settingsObject, color);
        }

        public void Restore(SettingDefinition definition, object settingsObject, Color originalColor)
        {
            ApplyTemporaryColor(definition, settingsObject, originalColor);
        }

        internal void BeginMasterPicker(Color color)
        {
            _masterPickerActive = true;
            _masterPickerColor = color;
        }

        internal void PreviewMasterPicker(Color color)
        {
            if (_masterPickerActive)
            {
                _masterPickerColor = color;
            }
        }

        internal void EndMasterPicker()
        {
            _masterPickerActive = false;
        }

        internal bool TryGetPreview(out WorkTabColorPreview preview)
        {
            if (_masterPickerActive)
            {
                preview = CreateMasterHighlightPreview(_masterPickerColor);
                return true;
            }

            if (_pickerDefinition != null)
            {
                preview = CreatePreview(_pickerDefinition, _pickerColor);
                return true;
            }

            if (Time.frameCount - _masterHoverFrame <= 1)
            {
                preview = CreateMasterHighlightPreview(_masterHoverColor);
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

        internal bool IsSkillPreviewActive
        {
            get
            {
                return TryGetPreview(out WorkTabColorPreview preview) &&
                       preview.Target == WorkTabColorPreviewTarget.SkillNumber;
            }
        }

        /// <summary>
        /// Receives generic non-color preview notifications from newer settings-framework
        /// builds. The framework owns the live field value; this controller only remembers
        /// which real renderer should be made visible for the current row.
        /// </summary>
        public void PreviewSetting(SettingDefinition definition, object settingsObject, object value)
        {
            _settingPreviewDefinition = definition;
            _settingPreviewFrame = Time.frameCount;

            if (definition?.Id == MasterHighlightSettingId &&
                settingsObject is BetterWorkTabSettings settings)
            {
                _masterHoverColor = settings.Color_CursorHighlight;
                _masterHoverFrame = Time.frameCount;
            }
        }

        internal bool IsBestPawnPreviewActive
        {
            get
            {
                return TryGetPreview(out WorkTabColorPreview preview) &&
                       preview.Target == WorkTabColorPreviewTarget.BestPawn;
            }
        }

        internal bool IsBestPawnThicknessPreviewActive
        {
            get
            {
                return _settingPreviewDefinition != null &&
                       Time.frameCount - _settingPreviewFrame <= 1 &&
                       _settingPreviewDefinition.FieldName == "bestPawnHighlightThickness";
            }
        }

        internal bool IsMasterHighlightPreviewActive
        {
            get
            {
                return TryGetPreview(out WorkTabColorPreview preview) &&
                       preview.FieldName == MasterHighlightPreviewField;
            }
        }

        internal bool TryGetMasterHighlightColor(out Color color)
        {
            color = default;
            if (!IsMasterHighlightPreviewActive ||
                !TryGetPreview(out WorkTabColorPreview preview))
            {
                return false;
            }

            color = preview.Color;
            return true;
        }

        internal bool TryGetHighlightColor(bool column, out Color color)
        {
            color = default;
            if (!TryGetPreview(out WorkTabColorPreview preview) ||
                (column ? !preview.IncludesColumn : !preview.IncludesRow))
            {
                return false;
            }

            color = preview.Color;
            return true;
        }

        internal bool TryGetSkillColor(int level, out Color color)
        {
            color = default;
            if (!TryGetPreview(out WorkTabColorPreview preview) ||
                preview.Target != WorkTabColorPreviewTarget.SkillNumber)
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
                preview.Target != WorkTabColorPreviewTarget.BestPawn)
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

        private static WorkTabColorPreview CreateMasterHighlightPreview(Color color)
        {
            return new WorkTabColorPreview(
                "Master highlight color",
                color,
                WorkTabColorPreviewTarget.Column,
                MasterHighlightPreviewField);
        }

        private static void ApplyTemporaryColor(
            SettingDefinition definition,
            object settingsObject,
            Color color)
        {
            if (definition == null ||
                settingsObject == null ||
                string.IsNullOrEmpty(definition.FieldName))
            {
                return;
            }

            FieldInfo field = settingsObject.GetType().GetField(definition.FieldName);
            if (field == null || field.FieldType != typeof(Color))
            {
                return;
            }

            field.SetValue(settingsObject, color);
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.SettingsThemeLanguageScale |
                WorkTabDirtyFlags.HeaderText |
                WorkTabDirtyFlags.RenderResources |
                WorkTabDirtyFlags.Presentation |
                WorkTabDirtyFlags.HoverInteraction);
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
                case "movedMarkerColor":
                    // Moved-marker color changes the header glyphs themselves in both
                    // angled and vanilla header renderers; preview that exact semantic.
                    return WorkTabColorPreviewTarget.HeaderText;
                case "headerUnderlineColor":
                    return WorkTabColorPreviewTarget.HeaderUnderline;
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
                case "Color_SettingFocusHighlight":
                    return WorkTabColorPreviewTarget.LegendOnly;
                default:
                    return WorkTabColorPreviewTarget.Cell;
            }
        }
    }
}
