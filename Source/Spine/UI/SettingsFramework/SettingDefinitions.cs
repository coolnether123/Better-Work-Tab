using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Convention helpers for the low-boilerplate settings schema API.
    /// </summary>
    public static class SettingsSchemaConventions
    {
        /// <summary>
        /// Converts a field name such as <c>ShowPreview</c> to
        /// <c>showPreview</c>. A null or empty name is preserved.
        /// </summary>
        public static string LowerCamelCase(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName) || !char.IsUpper(fieldName[0]))
            {
                return fieldName;
            }

            if (fieldName.Length == 1)
            {
                return fieldName.ToLowerInvariant();
            }

            int uppercasePrefixLength = 1;
            while (uppercasePrefixLength < fieldName.Length &&
                   char.IsUpper(fieldName[uppercasePrefixLength]))
            {
                uppercasePrefixLength++;
            }

            if (uppercasePrefixLength == fieldName.Length)
            {
                return fieldName.ToLowerInvariant();
            }

            int charactersToLower = uppercasePrefixLength == 1
                ? 1
                : uppercasePrefixLength - 1;
            return fieldName.Substring(0, charactersToLower).ToLowerInvariant() +
                fieldName.Substring(charactersToLower);
        }
    }

    /// <summary>
    /// Ordered settings-definition builder for one settings type.
    /// </summary>
    public sealed class SettingsSchema<TSettings>
    {
        private readonly List<SettingDefinition> definitions =
            new List<SettingDefinition>();
        private readonly IReadOnlyList<SettingDefinition> readOnlyDefinitions;
        private readonly Func<string, string> scribeKeyConvention;

        public SettingsSchema(Func<string, string> scribeKeyConvention = null)
        {
            this.scribeKeyConvention = scribeKeyConvention;
            readOnlyDefinitions = definitions.AsReadOnly();
            Root = new SettingsScope<TSettings>(
                definitions,
                parentId: null,
                scribeKeyConvention);
        }

        public IReadOnlyList<SettingDefinition> Definitions => readOnlyDefinitions;

        public SettingsScope<TSettings> Root { get; }

        /// <summary>
        /// Scribes this schema through Spine's existing settings facade.
        /// Consumer-owned settings types retain control over when this is called.
        /// </summary>
        public void Scribe(TSettings settings)
        {
#if RWT_EMBEDDED_SPINE_SETTINGS
            SettingsScribe.ScribeAll(settings, readOnlyDefinitions);
#else
            Spine.Api.SpineApi.Settings.Scribe(settings, readOnlyDefinitions);
#endif
        }

        /// <summary>Applies registered preference defaults.</summary>
        public IReadOnlyCollection<string> ApplyPreferenceDefaults(
            TSettings settings)
        {
#if RWT_EMBEDDED_SPINE_SETTINGS
            return SettingsScribe.ApplyPreferenceDefaults(
#else
            return Spine.Api.SpineApi.Settings.ApplyPreferenceDefaults(
#endif
                settings,
                readOnlyDefinitions);
        }

        /// <summary>Runs registered reactions after a bulk preference update.</summary>
        public void NotifyPreferenceChanges(
            TSettings settings,
            IEnumerable<string> changedFields = null)
        {
#if RWT_EMBEDDED_SPINE_SETTINGS
            SettingsScribe.NotifyPreferenceChanges(
#else
            Spine.Api.SpineApi.Settings.NotifyPreferenceChanges(
#endif
                settings,
                readOnlyDefinitions,
                changedFields);
        }

        /// <summary>Resolves a definition's persisted key.</summary>
        public string EffectiveScribeKey(SettingDefinition definition)
        {
#if RWT_EMBEDDED_SPINE_SETTINGS
            return SettingsScribe.EffectiveScribeKey(definition);
#else
            return Spine.Api.SpineApi.Settings.EffectiveScribeKey(definition);
#endif
        }

        public SettingsScope<TSettings> Section(
            string id,
            string label,
            string labelKey = null)
        {
            return Section(id, label, labelKey, configure: null);
        }

        /// <summary>
        /// Adds a header and returns a scope beneath it. The configuration
        /// callback is an escape hatch for presentation metadata that is not
        /// part of the section's common label/key pair.
        /// </summary>
        public SettingsScope<TSettings> Section(
            string id,
            string label,
            string labelKey,
            Action<SettingDefinition> configure)
        {
            SettingDefinition header = SettingDefinitions.Header(
                RequireId(id),
                label,
                labelKey);
            configure?.Invoke(header);
            definitions.Add(header);
            return new SettingsScope<TSettings>(
                definitions,
                header.Id,
                scribeKeyConvention);
        }

        public SettingsScope<TSettings> Under(string parentId)
        {
            return new SettingsScope<TSettings>(
                definitions,
                RequireId(parentId),
                scribeKeyConvention);
        }

        private static string RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A settings definition identifier is required.",
                    nameof(id));
            }

            return id;
        }
    }

    /// <summary>
    /// Adds typed field-backed definitions to a <see cref="SettingsSchema{TSettings}"/>.
    /// </summary>
    public sealed class SettingsScope<TSettings>
    {
        private readonly List<SettingDefinition> definitions;
        private readonly string parentId;
        private readonly Func<string, string> scribeKeyConvention;

        internal SettingsScope(
            List<SettingDefinition> definitions,
            string parentId,
            Func<string, string> scribeKeyConvention)
        {
            this.definitions = definitions;
            this.parentId = parentId;
            this.scribeKeyConvention = scribeKeyConvention;
        }

        /// <summary>
        /// Returns a scope beneath an existing definition while preserving the
        /// schema's shared definition collection and scribe-key convention.
        /// </summary>
        public SettingsScope<TSettings> Under(string nestedParentId)
        {
            return new SettingsScope<TSettings>(
                definitions,
                RequireId(nestedParentId),
                scribeKeyConvention);
        }

        /// <summary>
        /// Adds a non-field definition to this scope. Consumers use the
        /// configuration callback for consumer-owned metadata and callbacks;
        /// Spine only supplies the shared definition plumbing.
        /// </summary>
        public SettingDefinition Define(
            string id,
            SettingType type,
            string label = null,
            string tooltip = null,
            Action<SettingDefinition> configure = null)
        {
            SettingDefinition definition = SettingDefinitions.Define(
                RequireId(id),
                type,
                label,
                tooltip: tooltip,
                parentId: parentId);
            configure?.Invoke(definition);
            return Add(definition);
        }

        /// <summary>
        /// Adds an existing definition to this scope while applying the
        /// schema's parent and scribe-key conventions when the definition has
        /// left those values unspecified.
        /// </summary>
        public SettingDefinition Add(SettingDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (string.IsNullOrEmpty(definition.ParentId) && parentId != null)
            {
                definition.ParentId = parentId;
            }

            if (!string.IsNullOrEmpty(definition.FieldName) &&
                definition.ScribeKey == null)
            {
                definition.ScribeKey = ScribeKey(definition.FieldName);
            }

            definitions.Add(definition);
            return definition;
        }

        /// <summary>
        /// Adds an arbitrary field-backed definition while retaining typed
        /// direct-field selection and the schema's scribe-key convention.
        /// </summary>
        public SettingDefinition Field<TValue>(
            string id,
            Expression<Func<TSettings, TValue>> field,
            SettingType type,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null,
            Action<SettingDefinition> configure = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition = SettingDefinitions.Define(
                RequireId(id),
                type,
                label,
                tooltip: tooltip,
                parentId: parentId,
                scribeKey: ScribeKey(fieldName));
            definition.FieldName = fieldName;
            definition.OnChanged = Adapt(onChanged);
            if (type == SettingType.Enum && typeof(TValue).IsEnum)
            {
                definition.EnumType = typeof(TValue);
            }

            configure?.Invoke(definition);
            return Add(definition);
        }

        public SettingDefinition Toggle(
            string id,
            Expression<Func<TSettings, bool>> field,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition = SettingDefinitions.Toggle(
                RequireId(id),
                fieldName,
                label,
                tooltip: tooltip,
                parentId: parentId,
                scribeKey: ScribeKey(fieldName),
                onChanged: Adapt(onChanged));
            return Add(definition);
        }

        public SettingDefinition Int(
            string id,
            Expression<Func<TSettings, int>> field,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            return Field(id, field, SettingType.Int, label, tooltip, onChanged);
        }

        public SettingDefinition Int(
            string id,
            string fieldName,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            return Add(SettingDefinitions.Int(
                RequireId(id),
                RequireFieldName(fieldName),
                label,
                tooltip: tooltip,
                parentId: parentId,
                scribeKey: ScribeKey(fieldName),
                onChanged: Adapt(onChanged)));
        }

        public SettingDefinition Float(
            string id,
            Expression<Func<TSettings, float>> field,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            return Field(id, field, SettingType.Float, label, tooltip, onChanged);
        }

        public SettingDefinition Float(
            string id,
            string fieldName,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            return Add(SettingDefinitions.Float(
                RequireId(id),
                RequireFieldName(fieldName),
                label,
                tooltip: tooltip,
                parentId: parentId,
                scribeKey: ScribeKey(fieldName),
                onChanged: Adapt(onChanged)));
        }

        public SettingDefinition NumericInt(
            string id,
            Expression<Func<TSettings, int>> field,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            return Field(id, field, SettingType.NumericInt, label, tooltip, onChanged);
        }

        public SettingDefinition NumericInt(
            string id,
            string fieldName,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            return Add(SettingDefinitions.NumericInt(
                RequireId(id),
                RequireFieldName(fieldName),
                label,
                tooltip: tooltip,
                parentId: parentId,
                scribeKey: ScribeKey(fieldName),
                onChanged: Adapt(onChanged)));
        }

        public SettingDefinition Slider(
            string id,
            Expression<Func<TSettings, float>> field,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            return Slider(id, fieldName, label, tooltip, onChanged);
        }

        public SettingDefinition Slider(
            string id,
            string fieldName,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            fieldName = RequireFieldName(fieldName);
            SettingDefinition definition = SettingDefinitions.Slider(
                RequireId(id),
                fieldName,
                label,
                tooltip: tooltip,
                parentId: parentId,
                scribeKey: ScribeKey(fieldName),
                onChanged: Adapt(onChanged));
            return Add(definition);
        }

        public SettingDefinition Colour(
            string id,
            Expression<Func<TSettings, Color>> field,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition = SettingDefinitions.Colour(
                RequireId(id),
                fieldName,
                label,
                scribeKey: ScribeKey(fieldName));
            definition.Tooltip = tooltip;
            definition.ParentId = parentId;
            definition.OnChanged = Adapt(onChanged);
            return Add(definition);
        }

        public SettingDefinition Enum<TEnum>(
            string id,
            Expression<Func<TSettings, TEnum>> field,
            string label,
            string tooltip = null,
            Func<TEnum, string> labelProvider = null,
            Func<TEnum, string> descriptionProvider = null,
            Action<TSettings> onChanged = null)
            where TEnum : struct
        {
            if (!typeof(TEnum).IsEnum)
            {
                throw new ArgumentException(
                    "The typed settings enum must be an enum type.",
                    nameof(field));
            }

            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition = SettingDefinitions.Enum(
                RequireId(id),
                fieldName,
                typeof(TEnum),
                label,
                tooltip: tooltip,
                parentId: parentId,
                scribeKey: ScribeKey(fieldName),
                labelProvider: Adapt(labelProvider),
                descriptionProvider: Adapt(descriptionProvider));
            definition.OnChanged = Adapt(onChanged);
            return Add(definition);
        }

        public SettingDefinition Color(
            string id,
            Expression<Func<TSettings, Color>> field,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            return Colour(id, field, label, tooltip, onChanged);
        }

        public SettingDefinition Button(
            string id,
            string label,
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            SettingDefinition definition = SettingDefinitions.Button(
                RequireId(id),
                label,
                Adapt(onChanged));
            definition.Tooltip = tooltip;
            definition.ParentId = parentId;
            return Add(definition);
        }

        public SettingDefinition Spacer(
            string id,
            string label = "",
            string tooltip = null)
        {
            return Define(id, SettingType.Spacer, label, tooltip);
        }

        public SettingDefinition DropdownListAdder(
            string id,
            string label,
            Func<IEnumerable<string>> optionsProvider,
            Action<string> onOptionAdded,
            string tooltip = null)
        {
            SettingDefinition definition = Define(
                id,
                SettingType.DropdownListAdder,
                label,
                tooltip);
            definition.DropdownOptionsProvider = optionsProvider;
            definition.OnOptionAdded = onOptionAdded;
            return definition;
        }

        public SettingDefinition Custom(
            string id,
            Func<Rect, string, string, TSettings, bool, bool> drawer,
            string label = "",
            string tooltip = null,
            Action<TSettings> onChanged = null)
        {
            SettingDefinition definition = Define(
                id,
                SettingType.Custom,
                label,
                tooltip);
            definition.CustomDrawer = drawer == null
                ? null
                : (rect, rowLabel, rowTooltip, settings, disabled) =>
                    drawer(rect, rowLabel, rowTooltip, (TSettings)settings, disabled);
            definition.OnChanged = Adapt(onChanged);
            return definition;
        }

        private string ScribeKey(string fieldName)
        {
            return scribeKeyConvention == null
                ? null
                : scribeKeyConvention(fieldName);
        }

        private static Action<object> Adapt(Action<TSettings> callback)
        {
            return callback == null
                ? null
                : settings => callback((TSettings)settings);
        }

        private static Func<object, string> Adapt<TValue>(
            Func<TValue, string> callback)
        {
            return callback == null
                ? null
                : value => callback((TValue)value);
        }

        private static string RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A settings definition identifier is required.",
                    nameof(id));
            }

            return id;
        }

        private static string RequireFieldName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException(
                    "A settings field name is required.",
                    nameof(fieldName));
            }

            return fieldName;
        }
    }

    internal static class SettingSelector
    {
        private const string DirectFieldMessage =
            "A settings selector must be a direct field access such as " +
            "settings => settings.Enabled; properties, nested members, and " +
            "method calls are not supported.";

        internal static string FieldName<TSettings, TValue>(
            Expression<Func<TSettings, TValue>> selector)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            MemberExpression member = selector.Body as MemberExpression;
            FieldInfo field = member?.Member as FieldInfo;
            if (field == null || member.Expression != selector.Parameters[0])
            {
                throw new ArgumentException(DirectFieldMessage, nameof(selector));
            }

            if (field.FieldType != typeof(TValue))
            {
                throw new ArgumentException(
                    "The settings selector field '" + field.Name +
                    "' has type " + field.FieldType.FullName +
                    ", not " + typeof(TValue).FullName + ".",
                    nameof(selector));
            }

            return field.Name;
        }
    }

    /// <summary>
    /// Compact constructors for the standard settings rows shared by consumer
    /// mods. Gameplay meaning and callbacks remain consumer-owned.
    /// </summary>
    public static class SettingDefinitions
    {
        private static readonly HashSet<string> AuditedSettingsTypes =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Creates a definition with the common presentation metadata. This is
        /// the low-level escape hatch for widget types and consumer-owned
        /// callbacks that do not need a dedicated factory.
        /// </summary>
        public static SettingDefinition Define(
            string id,
            SettingType type,
            string label = null,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            string scribeKey = null)
        {
            SettingDefinition definition = Base(
                id,
                type,
                label,
                labelKey,
                tooltip,
                tooltipKey);
            definition.FieldName = null;
            definition.ScribeKey = scribeKey;
            definition.ParentId = parentId;
            definition.ShowInSimpleView = simple;
            return definition;
        }

        public static SettingDefinition Header(
            string id,
            string label,
            string labelKey = null) =>
            Base(id, SettingType.Header, label, labelKey);

        public static SettingDefinition Toggle(
            string id,
            string fieldName,
            string label,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            bool controlsChildren = false,
            string scribeKey = null,
            Action<object> onChanged = null)
        {
            SettingDefinition definition = Base(
                id,
                SettingType.Bool,
                label,
                labelKey,
                tooltip,
                tooltipKey);
            definition.FieldName = fieldName;
            definition.ScribeKey = scribeKey;
            definition.ParentId = parentId;
            definition.ShowInSimpleView = simple;
            definition.ControlsChildVisibility = controlsChildren;
            definition.OnChanged = onChanged;
            return definition;
        }

        public static SettingDefinition Int(
            string id,
            string fieldName,
            string label,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            string scribeKey = null,
            Action<object> onChanged = null)
        {
            return Numeric(
                id,
                SettingType.Int,
                fieldName,
                label,
                labelKey,
                tooltip,
                tooltipKey,
                parentId,
                simple,
                scribeKey,
                onChanged);
        }

        public static SettingDefinition Float(
            string id,
            string fieldName,
            string label,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            string scribeKey = null,
            Action<object> onChanged = null)
        {
            return Numeric(
                id,
                SettingType.Float,
                fieldName,
                label,
                labelKey,
                tooltip,
                tooltipKey,
                parentId,
                simple,
                scribeKey,
                onChanged);
        }

        public static SettingDefinition NumericInt(
            string id,
            string fieldName,
            string label,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            string scribeKey = null,
            Action<object> onChanged = null)
        {
            return Numeric(
                id,
                SettingType.NumericInt,
                fieldName,
                label,
                labelKey,
                tooltip,
                tooltipKey,
                parentId,
                simple,
                scribeKey,
                onChanged);
        }

        private static SettingDefinition Numeric(
            string id,
            SettingType type,
            string fieldName,
            string label,
            string labelKey,
            string tooltip,
            string tooltipKey,
            string parentId,
            bool simple,
            string scribeKey,
            Action<object> onChanged)
        {
            SettingDefinition definition = Define(
                id,
                type,
                label,
                labelKey,
                tooltip,
                tooltipKey,
                parentId,
                simple,
                scribeKey);
            definition.FieldName = fieldName;
            definition.OnChanged = onChanged;
            return definition;
        }

        /// <summary>
        /// A float dragged along a range. The bound field must be a float.
        /// Defaults to 0..1 with a two-decimal readout; use
        /// <see cref="SettingRefinements.Range"/>,
        /// <see cref="SettingRefinements.Step"/>, and
        /// <see cref="SettingRefinements.ShowsPercent"/> to shape it.
        /// </summary>
        /// <remarks>
        /// The parameters here deliberately mirror <see cref="Toggle"/>, so the
        /// leading arguments mean the same thing in every factory and a reader
        /// never has to check which one they are looking at. Everything a
        /// slider alone needs is a refinement instead, because a bare
        /// <c>0.35f, 1f</c> in the middle of a call tells a reader nothing.
        /// </remarks>
        public static SettingDefinition Slider(
            string id,
            string fieldName,
            string label,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            string scribeKey = null,
            Action<object> onChanged = null)
        {
            SettingDefinition definition = Base(
                id,
                SettingType.Slider,
                label,
                labelKey,
                tooltip,
                tooltipKey);
            definition.FieldName = fieldName;
            definition.ScribeKey = scribeKey;
            definition.ParentId = parentId;
            definition.ShowInSimpleView = simple;
            definition.OnChanged = onChanged;
            return definition;
        }

        public static SettingDefinition Enum(
            string id,
            string fieldName,
            Type enumType,
            string label,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            string scribeKey = null,
            Func<object, string> labelProvider = null,
            Func<object, string> descriptionProvider = null)
        {
            SettingDefinition definition = Base(
                id,
                SettingType.Enum,
                label,
                labelKey,
                tooltip,
                tooltipKey);
            definition.FieldName = fieldName;
            definition.ScribeKey = scribeKey;
            definition.ParentId = parentId;
            definition.ShowInSimpleView = simple;
            definition.EnumType = enumType;
            definition.EnumLabelProvider = labelProvider;
            definition.EnumDescriptionProvider = descriptionProvider;
            return definition;
        }

        public static SettingDefinition Colour(
            string id,
            string fieldName,
            string label,
            string labelKey = null,
            string tooltipKey = null,
            string scribeKey = null)
        {
            SettingDefinition definition = Base(
                id,
                SettingType.Color,
                label,
                labelKey,
                tooltipKey: tooltipKey);
            definition.FieldName = fieldName;
            definition.ScribeKey = scribeKey;
            return definition;
        }

        /// <summary>American-spelling alias for <see cref="Colour"/>.</summary>
        public static SettingDefinition Color(
            string id,
            string fieldName,
            string label,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true,
            string scribeKey = null,
            Action<object> onChanged = null)
        {
            SettingDefinition definition = Colour(
                id,
                fieldName,
                label,
                labelKey,
                tooltipKey,
                scribeKey);
            definition.Tooltip = tooltip;
            definition.ParentId = parentId;
            definition.ShowInSimpleView = simple;
            definition.OnChanged = onChanged;
            return definition;
        }

        public static SettingDefinition Button(
            string id,
            string label,
            Action<object> action,
            string labelKey = null,
            string tooltipKey = null)
        {
            SettingDefinition definition = Base(
                id,
                SettingType.Button,
                label,
                labelKey,
                tooltipKey: tooltipKey);
            definition.OnChanged = action;
            return definition;
        }

        public static SettingDefinition Spacer(
            string id,
            string label = "",
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true)
        {
            return Define(
                id,
                SettingType.Spacer,
                label,
                labelKey,
                tooltip,
                tooltipKey,
                parentId,
                simple);
        }

        public static SettingDefinition DropdownListAdder(
            string id,
            string label,
            Func<IEnumerable<string>> optionsProvider,
            Action<string> onOptionAdded,
            string labelKey = null,
            string tooltip = null,
            string tooltipKey = null,
            string parentId = null,
            bool simple = true)
        {
            SettingDefinition definition = Define(
                id,
                SettingType.DropdownListAdder,
                label,
                labelKey,
                tooltip,
                tooltipKey,
                parentId,
                simple);
            definition.DropdownOptionsProvider = optionsProvider;
            definition.OnOptionAdded = onOptionAdded;
            return definition;
        }

        public static SettingDefinition Custom(
            string id,
            Func<Rect, string, string, object, bool, bool> drawer,
            string label = "",
            string labelKey = "")
        {
            SettingDefinition definition = Base(
                id,
                SettingType.Custom,
                label,
                labelKey);
            definition.CustomDrawer = drawer;
            return definition;
        }

        internal static void Prepare(
            object settings,
            IReadOnlyList<SettingDefinition> definitions)
        {
            if (settings == null || definitions == null)
            {
                return;
            }

            Type settingsType = settings.GetType();
            object pristine = null;
            for (int index = 0; index < definitions.Count; index++)
            {
                SettingDefinition definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                if (definition.SortOrder == int.MinValue)
                {
                    definition.SortOrder = index;
                }

                if (definition.DefaultValue != null ||
                    string.IsNullOrEmpty(definition.FieldName))
                {
                    continue;
                }

                if (pristine == null)
                {
                    pristine = Activator.CreateInstance(settingsType);
                }

                FieldInfo field = settingsType.GetField(
                    definition.FieldName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                if (field != null)
                {
                    definition.DefaultValue = field.GetValue(pristine);
                }
            }

            ValidatePresentation(settingsType, definitions);
        }

        /// <summary>
        /// Development-time audit of a consumer's setting definitions. Reports two
        /// things a player would otherwise discover the hard way: an interactive
        /// setting with nothing to read when they hover it, and a translation key
        /// that will not resolve at runtime.
        /// Runs once per settings type and only under dev mode, so a shipped game
        /// pays nothing and players never see it.
        /// </summary>
        /// <remarks>
        /// This used to assume Prepare was only reached after language data had
        /// loaded. It is not: Prepare also runs from the scribe path, which
        /// RimWorld executes while constructing mods, before LanguageDatabase has
        /// an active language. CanTranslate dereferences that language, so the
        /// audit threw a NullReferenceException straight out of ExposeData and
        /// took the entire settings load with it - every setting silently
        /// reverted to its default on every launch, for every consumer, whenever
        /// dev mode was on. The language check below is load-bearing, not
        /// defensive.
        /// </remarks>
        private static void ValidatePresentation(
            Type settingsType,
            IReadOnlyList<SettingDefinition> definitions)
        {
            if (settingsType == null || !Prefs.DevMode)
            {
                return;
            }

            // Bail before claiming the audit slot, so the audit still runs once
            // the settings page draws and the keys can actually be resolved.
            if (LanguageDatabase.activeLanguage == null)
            {
                return;
            }

            if (!AuditedSettingsTypes.Add(settingsType.FullName))
            {
                return;
            }

            List<string> problems = new List<string>();
            for (int index = 0; index < definitions.Count; index++)
            {
                SettingDefinition definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                string id = string.IsNullOrEmpty(definition.Id)
                    ? "(no id)"
                    : definition.Id;

                if (!string.IsNullOrEmpty(definition.LabelKey) &&
                    !definition.LabelKey.CanTranslate())
                {
                    problems.Add(
                        "  " + id + ": LabelKey '" + definition.LabelKey +
                        "' has no translation entry.");
                }

                if (!string.IsNullOrEmpty(definition.TooltipKey) &&
                    !definition.TooltipKey.CanTranslate())
                {
                    problems.Add(
                        "  " + id + ": TooltipKey '" + definition.TooltipKey +
                        "' has no translation entry.");
                }

                if (WantsExplanation(definition.Type) &&
                    string.IsNullOrEmpty(definition.TooltipKey) &&
                    string.IsNullOrEmpty(definition.Tooltip))
                {
                    problems.Add(
                        "  " + id + ": no tooltip. A player who hovers this " +
                        "setting has nothing to read.");
                }
            }

            if (problems.Count == 0)
            {
                return;
            }

            Log.Warning(
                "[Spine] Settings audit for " + settingsType.Name +
                " found " + problems.Count + " issue(s):\n" +
                string.Join("\n", problems.ToArray()));
        }

        private static bool WantsExplanation(SettingType type)
        {
            return type == SettingType.Bool ||
                type == SettingType.Color ||
                type == SettingType.Enum ||
                type == SettingType.Button ||
                type == SettingType.Slider ||
                type == SettingType.Int ||
                type == SettingType.Float ||
                type == SettingType.NumericInt ||
                type == SettingType.DropdownListAdder;
        }

        private static SettingDefinition Base(
            string id,
            SettingType type,
            string label,
            string labelKey,
            string tooltip = null,
            string tooltipKey = null) =>
            new SettingDefinition
            {
                Id = id,
                Type = type,
                Label = label,
                LabelKey = labelKey,
                Tooltip = tooltip,
                TooltipKey = tooltipKey,
                ShowInSimpleView = true,
                ShowInAdvancedView = true
            };
    }

    /// <summary>
    /// Post-construction refinements for setting definitions.
    ///
    /// These exist so the factory signatures above never have to change again.
    /// Adding a parameter to an existing public method is a BINARY-BREAKING
    /// change in C#: a caller bakes the whole argument list into its IL, so a
    /// consumer compiled against the old signature throws MissingMethodException
    /// against the new assembly even though its source would still compile
    /// unchanged. That once broke every mod built on Spine, from a change that
    /// looked additive.
    ///
    /// So: DO NOT add parameters to the factories. Add a refinement here. A new
    /// method is always binary-safe, and consumers opt in by recompiling rather
    /// than by breaking.
    /// </summary>
    public static class SettingRefinements
    {
        /// <summary>Sets the reset and absent-key default for this definition.</summary>
        public static SettingDefinition DefaultTo(
            this SettingDefinition definition,
            object value)
        {
            if (definition != null)
            {
                definition.DefaultValue = value;
            }

            return definition;
        }

        /// <summary>Marks this setting as controlling the visibility of its children.</summary>
        public static SettingDefinition ControlsChildren(
            this SettingDefinition definition)
        {
            if (definition != null)
            {
                definition.ControlsChildVisibility = true;
            }

            return definition;
        }

        /// <summary>Uses a specific persisted key for this definition.</summary>
        public static SettingDefinition ScribeAs(
            this SettingDefinition definition,
            string key)
        {
            if (definition != null)
            {
                definition.ScribeKey = key;
            }

            return definition;
        }

        /// <summary>Sets the localized label and tooltip keys for this definition.</summary>
        public static SettingDefinition Localized(
            this SettingDefinition definition,
            string labelKey,
            string tooltipKey)
        {
            if (definition != null)
            {
                definition.LabelKey = labelKey;
                definition.TooltipKey = tooltipKey;
            }

            return definition;
        }

        public static SettingDefinition WithScribeDefault(
            this SettingDefinition definition,
            object value)
        {
            if (definition != null)
            {
                definition.ScribeDefaultOverride = value;
            }

            return definition;
        }

        public static SettingDefinition WithoutAutoScribe(
            this SettingDefinition definition)
        {
            if (definition != null)
            {
                definition.DisableAutoScribe = true;
            }

            return definition;
        }

        public static SettingDefinition ClassifiedAs(
            this SettingDefinition definition,
            SettingClassification classification)
        {
            if (definition != null)
            {
                definition.Classification = classification;
            }

            return definition;
        }

        public static SettingDefinition Under(
            this SettingDefinition definition,
            string parentId)
        {
            if (definition != null)
            {
                definition.ParentId = parentId;
            }

            return definition;
        }

        public static SettingDefinition Ordered(
            this SettingDefinition definition,
            int sortOrder)
        {
            if (definition != null)
            {
                definition.SortOrder = sortOrder;
            }

            return definition;
        }

        public static SettingDefinition SearchableBy(
            this SettingDefinition definition,
            params string[] keywords)
        {
            if (definition != null)
            {
                definition.SearchKeywords = keywords;
            }

            return definition;
        }

        public static SettingDefinition ShownIn(
            this SettingDefinition definition,
            bool simple,
            bool advanced = true)
        {
            if (definition != null)
            {
                definition.ShowInSimpleView = simple;
                definition.ShowInAdvancedView = advanced;
            }

            return definition;
        }

        public static SettingDefinition Accented(
            this SettingDefinition definition,
            Color color)
        {
            if (definition != null)
            {
                definition.HeaderColor = color;
            }

            return definition;
        }

        public static SettingDefinition RestartRequired(
            this SettingDefinition definition)
        {
            if (definition != null)
            {
                definition.RequiresRestart = true;
            }

            return definition;
        }

        /// <summary>Holds this entry outside the scrolling region.</summary>
        public static SettingDefinition Pinned(
            this SettingDefinition definition,
            SettingPin pin)
        {
            if (definition != null)
            {
                definition.Pin = pin;
            }

            return definition;
        }

        /// <summary>
        /// Shows this entry only while the predicate holds for the settings
        /// object. Named ShownWhen rather than VisibleWhen so it cannot be
        /// confused with the field of that name, which is a delegate.
        /// </summary>
        public static SettingDefinition ShownWhen(
            this SettingDefinition definition,
            Func<object, bool> predicate)
        {
            if (definition != null)
            {
                definition.VisibleWhen = predicate;
            }

            return definition;
        }

        /// <summary>Hides this entry from the Simple view.</summary>
        public static SettingDefinition AdvancedOnly(
            this SettingDefinition definition)
        {
            if (definition != null)
            {
                definition.ShowInSimpleView = false;
            }

            return definition;
        }

        /// <summary>
        /// Bounds a slider. A reversed pair is treated as the range the caller
        /// meant rather than as an empty one, because an unusable slider is a
        /// worse outcome than a silently reordered pair.
        /// </summary>
        public static SettingDefinition Range(
            this SettingDefinition definition,
            float min,
            float max)
        {
            if (definition != null)
            {
                definition.SliderMin = Mathf.Min(min, max);
                definition.SliderMax = Mathf.Max(min, max);
            }

            return definition;
        }

        /// <summary>
        /// Quantises a slider to multiples of <paramref name="step"/>, so a
        /// player lands on 0.75 rather than 0.7431. Zero or less leaves it
        /// continuous.
        /// </summary>
        public static SettingDefinition Step(
            this SettingDefinition definition,
            float step)
        {
            if (definition != null)
            {
                definition.SliderStep = step;
            }

            return definition;
        }

        /// <summary>
        /// Reads a slider out as a whole percentage. Intended for a normalised
        /// 0..1 range; on any other range the number shown will not match the
        /// value stored.
        /// </summary>
        public static SettingDefinition ShowsPercent(
            this SettingDefinition definition)
        {
            return definition.ShowsValue(
                value => Mathf.RoundToInt(value * 100f) + "%");
        }

        /// <summary>
        /// Replaces the numeric readout beside a slider. Use for units, counts,
        /// or a word standing in for a band of values.
        /// </summary>
        public static SettingDefinition ShowsValue(
            this SettingDefinition definition,
            Func<float, string> formatter)
        {
            if (definition != null)
            {
                definition.SliderValueFormatter = formatter;
            }

            return definition;
        }

        /// <summary>
        /// Applies consumer-owned metadata to a definition and returns it so a
        /// schema declaration remains chainable. This keeps the shared API
        /// feature-neutral as consumers add presentation callbacks of their own.
        /// </summary>
        public static SettingDefinition Configure(
            this SettingDefinition definition,
            Action<SettingDefinition> configure)
        {
            if (definition != null)
            {
                configure?.Invoke(definition);
            }

            return definition;
        }

        /// <summary>Sets the legacy numeric bounds used by a consumer renderer.</summary>
        public static SettingDefinition ValueRange(
            this SettingDefinition definition,
            float min,
            float max)
        {
            if (definition != null)
            {
                definition.MinValue = Mathf.Min(min, max);
                definition.MaxValue = Mathf.Max(min, max);
            }

            return definition;
        }

        /// <summary>Sets the optional labels at the ends of a numeric control.</summary>
        public static SettingDefinition ValueLabels(
            this SettingDefinition definition,
            string minLabel,
            string maxLabel)
        {
            if (definition != null)
            {
                definition.MinLabel = minLabel;
                definition.MaxLabel = maxLabel;
            }

            return definition;
        }

        /// <summary>Sets the format string used for a numeric readout.</summary>
        public static SettingDefinition FormattedAs(
            this SettingDefinition definition,
            string format)
        {
            if (definition != null)
            {
                definition.ValueFormat = format;
            }

            return definition;
        }

        /// <summary>Marks a boolean definition for consumer-provided emphasis.</summary>
        public static SettingDefinition Emphasized(
            this SettingDefinition definition,
            bool value = true)
        {
            if (definition != null)
            {
                definition.EmphasizeAsHeader = value;
            }

            return definition;
        }

        /// <summary>Sets the dynamic options and selection callback for a dropdown row.</summary>
        public static SettingDefinition OptionsFrom(
            this SettingDefinition definition,
            Func<IEnumerable<string>> optionsProvider,
            Action<string> onOptionAdded)
        {
            if (definition != null)
            {
                definition.DropdownOptionsProvider = optionsProvider;
                definition.OnOptionAdded = onOptionAdded;
            }

            return definition;
        }

        /// <summary>
        /// Adds a typed suppression rule while leaving its condition and reason
        /// in consumer code.
        /// </summary>
        public static SettingDefinition SuppressedWhen<TSettings>(
            this SettingDefinition definition,
            Func<TSettings, bool> when,
            Func<TSettings, string> reason,
            string suppressorSettingId = null,
            string linkLabel = null,
            string externalActionUrl = null,
            string externalActionLabel = null,
            string externalActionTooltip = null)
        {
            if (definition != null)
            {
                if (definition.Suppressions == null)
                {
                    definition.Suppressions = new List<SettingSuppression>();
                }

                definition.Suppressions.Add(new SettingSuppression
                {
                    When = when == null ? null : settings => when((TSettings)settings),
                    Reason = reason == null ? null : settings => reason((TSettings)settings),
                    SuppressorSettingId = suppressorSettingId,
                    LinkLabel = linkLabel,
                    ExternalActionUrl = externalActionUrl,
                    ExternalActionLabel = externalActionLabel,
                    ExternalActionTooltip = externalActionTooltip
                });
            }

            return definition;
        }

        /// <summary>Sets the custom-row reset hooks used by a consumer renderer.</summary>
        public static SettingDefinition WithCustomReset(
            this SettingDefinition definition,
            Func<object, bool> hasNonDefaultValue,
            Action<object> reset)
        {
            if (definition != null)
            {
                definition.CustomHasNonDefaultValue = hasNonDefaultValue;
                definition.CustomReset = reset;
            }

            return definition;
        }
    }

}
