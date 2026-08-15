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
            while (uppercasePrefixLength < fieldName.Length && char.IsUpper(fieldName[uppercasePrefixLength]))
            {
                uppercasePrefixLength++;
            }

            if (uppercasePrefixLength == fieldName.Length)
            {
                return fieldName.ToLowerInvariant();
            }

            int charactersToLower = uppercasePrefixLength == 1 ? 1 : uppercasePrefixLength - 1;
            return fieldName.Substring(0, charactersToLower).ToLowerInvariant() + fieldName.Substring(charactersToLower);
        }
    }

    /// <summary>
    /// Ordered settings-definition builder for one settings type.
    /// </summary>
    public sealed class SettingsSchema<TSettings>
    {
        private readonly List<SettingDefinition> definitions = new List<SettingDefinition>();
        private readonly IReadOnlyList<SettingDefinition> readOnlyDefinitions;
        private readonly Func<string, string> scribeKeyConvention;
        private readonly Action<SettingDefinition> onAdd;

        public SettingsSchema(Func<string, string> scribeKeyConvention = null) : this(scribeKeyConvention, onAdd: null)
        {
        }

        public SettingsSchema(Func<string, string> scribeKeyConvention, Action<SettingDefinition> onAdd)
        {
            this.scribeKeyConvention = scribeKeyConvention;
            this.onAdd = onAdd;
            readOnlyDefinitions = LegacyReadOnlyCollections.WrapList(definitions);
            Root = new SettingsScope<TSettings>(definitions, parentId: null, scribeKeyConvention, onAdd);
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
        public IReadOnlyCollection<string> ApplyPreferenceDefaults(TSettings settings)
        {
#if RWT_EMBEDDED_SPINE_SETTINGS
            return SettingsScribe.ApplyPreferenceDefaults(
#else
            return Spine.Api.SpineApi.Settings.ApplyPreferenceDefaults(
#endif
                settings, readOnlyDefinitions);
        }

        /// <summary>Runs registered reactions after a bulk preference update.</summary>
        public void NotifyPreferenceChanges(TSettings settings, IEnumerable<string> changedFields = null)
        {
#if RWT_EMBEDDED_SPINE_SETTINGS
            SettingsScribe.NotifyPreferenceChanges(
#else
            Spine.Api.SpineApi.Settings.NotifyPreferenceChanges(
#endif
                settings, readOnlyDefinitions, changedFields);
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

        public SettingsScope<TSettings> Section(string id, string label, string labelKey = null)
        {
            return Section(id, label, labelKey, configure: null);
        }

        /// <summary>
        /// Adds a header and returns a scope beneath it. The configuration
        /// callback is an escape hatch for presentation metadata that is not
        /// part of the section's common label/key pair.
        /// </summary>
        public SettingsScope<TSettings> Section(string id, string label, string labelKey, Action<SettingDefinition> configure)
        {
            SettingDefinition header = SettingDefinitionBuilder.Create(RequireId(id), SettingType.Header, label, labelKey: labelKey);
            configure?.Invoke(header);
            Root.Add(header);
            return new SettingsScope<TSettings>(definitions, header.Id, scribeKeyConvention, onAdd);
        }

        public SettingsScope<TSettings> Under(string parentId)
        {
            return new SettingsScope<TSettings>(definitions, RequireId(parentId), scribeKeyConvention, onAdd);
        }

        private static string RequireId(string id)
        {
            if (LegacyBcl.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A settings definition identifier is required.", nameof(id));
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
        private readonly Action<SettingDefinition> onAdd;

        internal SettingsScope(List<SettingDefinition> definitions, string parentId, Func<string, string> scribeKeyConvention, Action<SettingDefinition> onAdd)
        {
            this.definitions = definitions;
            this.parentId = parentId;
            this.scribeKeyConvention = scribeKeyConvention;
            this.onAdd = onAdd;
        }

        /// <summary>
        /// Returns a scope beneath an existing definition while preserving the
        /// schema's shared definition collection and scribe-key convention.
        /// </summary>
        public SettingsScope<TSettings> Under(string nestedParentId)
        {
            return new SettingsScope<TSettings>(definitions, RequireId(nestedParentId), scribeKeyConvention, onAdd);
        }

        /// <summary>
        /// Adds a non-field definition to this scope. Consumers use the
        /// configuration callback for consumer-owned metadata and callbacks;
        /// Spine only supplies the shared definition plumbing.
        /// </summary>
        public SettingDefinition Define(string id, SettingType type, string label = null, string tooltip = null, Action<SettingDefinition> configure = null)
        {
            SettingDefinition definition = SettingDefinitionBuilder.Create(RequireId(id), type, label, tooltip: tooltip, parentId: parentId);
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

            if (!string.IsNullOrEmpty(definition.FieldName) && definition.ScribeKey == null)
            {
                definition.ScribeKey = ScribeKey(definition.FieldName);
            }

            definitions.Add(definition);
            onAdd?.Invoke(definition);
            return definition;
        }

        /// <summary>
        /// Adds an arbitrary field-backed definition while retaining typed
        /// direct-field selection and the schema's scribe-key convention.
        /// </summary>
        public SettingDefinition Field<TValue>(string id, Expression<Func<TSettings, TValue>> field, SettingType type, string label, string tooltip = null,
                                               Action<TSettings> onChanged = null, Action<SettingDefinition> configure = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition = SettingDefinitionBuilder.Create(RequireId(id), type, label, tooltip: tooltip, parentId: parentId, scribeKey: ScribeKey(fieldName));
            definition.FieldName = fieldName;
            definition.OnChanged = Adapt(onChanged);
            if (type == SettingType.Enum && typeof(TValue).IsEnum)
            {
                definition.EnumType = typeof(TValue);
            }

            configure?.Invoke(definition);
            return Add(definition);
        }

        public SettingDefinition Toggle(string id, Expression<Func<TSettings, bool>> field, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition = SettingDefinitionBuilder.Create(RequireId(id), SettingType.Bool, label, tooltip: tooltip, parentId: parentId, fieldName: fieldName,
                                                                           scribeKey: ScribeKey(fieldName), onChanged: Adapt(onChanged));
            definition.ControlsChildVisibility = false;
            return Add(definition);
        }

        public SettingDefinition Int(string id, Expression<Func<TSettings, int>> field, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            return Field(id, field, SettingType.Int, label, tooltip, onChanged);
        }

        public SettingDefinition Int(string id, string fieldName, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            return Add(SettingDefinitionBuilder.Create(RequireId(id), SettingType.Int, label, tooltip: tooltip, parentId: parentId, fieldName: RequireFieldName(fieldName),
                                                       scribeKey: ScribeKey(fieldName), onChanged: Adapt(onChanged)));
        }

        public SettingDefinition Float(string id, Expression<Func<TSettings, float>> field, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            return Field(id, field, SettingType.Float, label, tooltip, onChanged);
        }

        public SettingDefinition Float(string id, string fieldName, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            return Add(SettingDefinitionBuilder.Create(RequireId(id), SettingType.Float, label, tooltip: tooltip, parentId: parentId, fieldName: RequireFieldName(fieldName),
                                                       scribeKey: ScribeKey(fieldName), onChanged: Adapt(onChanged)));
        }

        public SettingDefinition NumericInt(string id, Expression<Func<TSettings, int>> field, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            return Field(id, field, SettingType.NumericInt, label, tooltip, onChanged);
        }

        public SettingDefinition NumericInt(string id, string fieldName, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            return Add(SettingDefinitionBuilder.Create(RequireId(id), SettingType.NumericInt, label, tooltip: tooltip, parentId: parentId, fieldName: RequireFieldName(fieldName),
                                                       scribeKey: ScribeKey(fieldName), onChanged: Adapt(onChanged)));
        }

        public SettingDefinition Slider(string id, Expression<Func<TSettings, float>> field, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            return Slider(id, fieldName, label, tooltip, onChanged);
        }

        public SettingDefinition Slider(string id, string fieldName, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            fieldName = RequireFieldName(fieldName);
            SettingDefinition definition = SettingDefinitionBuilder.Create(RequireId(id), SettingType.Slider, label, tooltip: tooltip, parentId: parentId, fieldName: fieldName,
                                                                           scribeKey: ScribeKey(fieldName), onChanged: Adapt(onChanged));
            return Add(definition);
        }

        public SettingDefinition Colour(string id, Expression<Func<TSettings, Color>> field, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition = SettingDefinitionBuilder.Create(RequireId(id), SettingType.Color, label, tooltip: tooltip, parentId: parentId, fieldName: fieldName,
                                                                           scribeKey: ScribeKey(fieldName));
            definition.OnChanged = Adapt(onChanged);
            return Add(definition);
        }

        public SettingDefinition Enum<TEnum>(string id, Expression<Func<TSettings, TEnum>> field, string label, string tooltip = null, Func<TEnum, string> labelProvider = null,
                                             Func<TEnum, string> descriptionProvider = null, Action<TSettings> onChanged = null)
            where TEnum : struct
        {
            if (!typeof(TEnum).IsEnum)
            {
                throw new ArgumentException("The typed settings enum must be an enum type.", nameof(field));
            }

            string fieldName = SettingSelector.FieldName(field);
            SettingDefinition definition =
                SettingDefinitionBuilder.Create(RequireId(id), SettingType.Enum, label, tooltip: tooltip, parentId: parentId, fieldName: fieldName, scribeKey: ScribeKey(fieldName),
                                                enumType: typeof(TEnum), labelProvider: Adapt(labelProvider), descriptionProvider: Adapt(descriptionProvider));
            definition.OnChanged = Adapt(onChanged);
            return Add(definition);
        }

        public SettingDefinition DerivedEnum<TEnum>(
            string id,
            Func<TSettings, TEnum> getValue,
            Action<TSettings, TEnum> setValue,
            string label,
            string tooltip = null,
            Func<TEnum, string> labelProvider = null,
            Func<TEnum, string> descriptionProvider = null,
            Action<TSettings> onChanged = null)
            where TEnum : struct
        {
            if (!typeof(TEnum).IsEnum)
            {
                throw new ArgumentException("The typed settings enum must be an enum type.", nameof(TEnum));
            }
            if (getValue == null) throw new ArgumentNullException(nameof(getValue));
            if (setValue == null) throw new ArgumentNullException(nameof(setValue));

            SettingDefinition definition = SettingDefinitionBuilder.Create(
                RequireId(id),
                SettingType.Enum,
                label,
                tooltip: tooltip,
                parentId: parentId,
                enumType: typeof(TEnum),
                labelProvider: Adapt(labelProvider),
                descriptionProvider: Adapt(descriptionProvider),
                onChanged: Adapt(onChanged));
            definition.ValueGetter = settings => getValue((TSettings)settings);
            definition.ValueSetter = (settings, value) => setValue((TSettings)settings, (TEnum)value);
            return Add(definition);
        }

        public SettingDefinition ReadOnly(
            string id,
            Func<TSettings, string> valueProvider,
            string label,
            string tooltip = null)
        {
            if (valueProvider == null) throw new ArgumentNullException(nameof(valueProvider));

            SettingDefinition definition = Define(id, SettingType.ReadOnly, label, tooltip);
            definition.ReadOnlyValueProvider = settings => valueProvider((TSettings)settings);
            return definition;
        }

        public SettingDefinition Color(string id, Expression<Func<TSettings, Color>> field, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            return Colour(id, field, label, tooltip, onChanged);
        }

        public SettingDefinition Button(string id, string label, string tooltip = null, Action<TSettings> onChanged = null)
        {
            SettingDefinition definition =
                SettingDefinitionBuilder.Create(RequireId(id), SettingType.Button, label, tooltip: tooltip, parentId: parentId, onChanged: Adapt(onChanged));
            return Add(definition);
        }

        public SettingDefinition Spacer(string id, string label = "", string tooltip = null)
        {
            return Define(id, SettingType.Spacer, label, tooltip);
        }

        public SettingDefinition DropdownListAdder(string id, string label, Func<IEnumerable<string>> optionsProvider, Action<string> onOptionAdded, string tooltip = null)
        {
            SettingDefinition definition = Define(id, SettingType.DropdownListAdder, label, tooltip);
            definition.DropdownOptionsProvider = optionsProvider;
            definition.OnOptionAdded = onOptionAdded;
            return definition;
        }

#if RWT_LEGACY_BCL
        public SettingDefinition Custom(string id, LegacyFunc6<Rect, string, string, TSettings, bool, bool> drawer, string label = "", string tooltip = null,
#else
        public SettingDefinition Custom(string id, Func<Rect, string, string, TSettings, bool, bool> drawer, string label = "", string tooltip = null,
#endif
                                        Action<TSettings> onChanged = null)
        {
            SettingDefinition definition = Define(id, SettingType.Custom, label, tooltip);
            definition.CustomDrawer = drawer == null ? null : (rect, rowLabel, rowTooltip, settings, disabled) => drawer(rect, rowLabel, rowTooltip, (TSettings)settings, disabled);
            definition.OnChanged = Adapt(onChanged);
            return definition;
        }

        private string ScribeKey(string fieldName)
        {
            return scribeKeyConvention == null ? null : scribeKeyConvention(fieldName);
        }

        private static Action<object> Adapt(Action<TSettings> callback)
        {
            return callback == null ? null : settings => callback((TSettings)settings);
        }

        private static Func<object, string> Adapt<TValue>(Func<TValue, string> callback)
        {
            return callback == null ? null : value => callback((TValue)value);
        }

        private static string RequireId(string id)
        {
            if (LegacyBcl.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A settings definition identifier is required.", nameof(id));
            }

            return id;
        }

        private static string RequireFieldName(string fieldName)
        {
            if (LegacyBcl.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("A settings field name is required.", nameof(fieldName));
            }

            return fieldName;
        }
    }

    /// <summary>
    /// Internal construction seam for the typed schema. Keeping the common
    /// metadata defaults here lets the public schema stay compact without
    /// exposing a second, legacy factory surface.
    /// </summary>
    internal static class SettingDefinitionBuilder
    {
        internal static SettingDefinition Create(string id, SettingType type, string label = null, string labelKey = null, string tooltip = null, string tooltipKey = null,
                                                 string parentId = null, string fieldName = null, string scribeKey = null, bool showInSimpleView = true, Type enumType = null,
                                                 Func<object, string> labelProvider = null, Func<object, string> descriptionProvider = null, Action<object> onChanged = null)
        {
            return new SettingDefinition { Id = id,
                                           Type = type,
                                           Label = label,
                                           LabelKey = labelKey,
                                           Tooltip = tooltip,
                                           TooltipKey = tooltipKey,
                                           ParentId = parentId,
                                           FieldName = fieldName,
                                           ScribeKey = scribeKey,
                                           ShowInSimpleView = showInSimpleView,
                                           ShowInAdvancedView = true,
                                           EnumType = enumType,
                                           EnumLabelProvider = labelProvider,
                                           EnumDescriptionProvider = descriptionProvider,
                                           OnChanged = onChanged };
        }
    }

    internal static class SettingSelector
    {
        private const string DirectFieldMessage =
            "A settings selector must be a direct field access such as " + "settings => settings.Enabled; properties, nested members, and " + "method calls are not supported.";

        internal static string FieldName<TSettings, TValue>(Expression<Func<TSettings, TValue>> selector)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            MemberExpression member = selector.Body as MemberExpression;
            FieldInfo field = member?.Member as FieldInfo;
            if (LegacyBcl.IsNull(field) || member.Expression != selector.Parameters[0])
            {
                throw new ArgumentException(DirectFieldMessage, nameof(selector));
            }

            if (!object.Equals(field.FieldType, typeof(TValue)))
            {
                throw new ArgumentException("The settings selector field '" + field.Name + "' has type " + field.FieldType.FullName + ", not " + typeof(TValue).FullName + ".",
                                            nameof(selector));
            }

            return field.Name;
        }
    }

    /// <summary>
    /// Legacy constructors retained so consumers compiled against SpineLib 1.0
    /// continue to load on 1.1. New consumers should use
    /// <see cref="SettingsSchema{TSettings}"/>.
    /// </summary>
    [Obsolete("Use SettingsSchema<TSettings> and SettingsScope<TSettings>.", false)]
    public static class SettingDefinitions
    {
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

    public static SettingDefinition Custom(
            string id,
#if RWT_LEGACY_BCL
            LegacyFunc6<Rect, string, string, object, bool, bool> drawer,
#else
            Func<Rect, string, string, object, bool, bool> drawer,
#endif
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

        private static SettingDefinition Base(
            string id,
            SettingType type,
            string label,
            string labelKey,
            string tooltip = null,
            string tooltipKey = null) =>
            SettingDefinitionBuilder.Create(
                id,
                type,
                label,
                labelKey,
                tooltip,
                tooltipKey);
    }

    /// <summary>
    /// Prepares definition metadata that depends on the settings object or on
    /// declaration order. This is intentionally internal; public consumers
    /// reach it through the schema/page lifecycle rather than a legacy factory
    /// class.
    /// </summary>
    internal static class SettingsPreparation
    {
        private static readonly HashSet<string> AuditedSettingsTypes = new HashSet<string>(StringComparer.Ordinal);

        internal static void Prepare(object settings, IReadOnlyList<SettingDefinition> definitions)
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

                if (definition.DefaultValue != null || string.IsNullOrEmpty(definition.FieldName))
                {
                    continue;
                }

                if (pristine == null)
                {
                    pristine = Activator.CreateInstance(settingsType);
                }

                FieldInfo field = settingsType.GetField(definition.FieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (LegacyBcl.IsNotNull(field))
                {
                    definition.DefaultValue = field.GetValue(pristine);
                }
            }

            ValidatePresentation(settingsType, definitions);
        }

        /// <summary>
        /// Development-time audit of a consumer's setting definitions. It is
        /// deferred until language data exists because this method also runs
        /// during early settings scribing.
        /// </summary>
        private static void ValidatePresentation(Type settingsType, IReadOnlyList<SettingDefinition> definitions)
        {
            if (LegacyBcl.IsNull(settingsType) || !Prefs.DevMode)
            {
                return;
            }

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

                string id = string.IsNullOrEmpty(definition.Id) ? "(no id)" : definition.Id;

                if (!string.IsNullOrEmpty(definition.LabelKey) && !definition.LabelKey.CanTranslate())
                {
                    problems.Add("  " + id + ": LabelKey '" + definition.LabelKey + "' has no translation entry.");
                }

                if (!string.IsNullOrEmpty(definition.TooltipKey) && !definition.TooltipKey.CanTranslate())
                {
                    problems.Add("  " + id + ": TooltipKey '" + definition.TooltipKey + "' has no translation entry.");
                }

                if (WantsExplanation(definition.Type) && string.IsNullOrEmpty(definition.TooltipKey) && string.IsNullOrEmpty(definition.Tooltip))
                {
                    problems.Add("  " + id + ": no tooltip. A player who hovers this " + "setting has nothing to read.");
                }
            }

            if (problems.Count == 0)
            {
                return;
            }

            Log.Warning("[Spine] Settings audit for " + settingsType.Name + " found " + problems.Count + " issue(s):\n" + string.Join("\n", problems.ToArray()));
        }

        private static bool WantsExplanation(SettingType type)
        {
            return type == SettingType.Bool || type == SettingType.Color || type == SettingType.Enum || type == SettingType.Button || type == SettingType.Slider ||
                   type == SettingType.Int || type == SettingType.Float || type == SettingType.NumericInt || type == SettingType.DropdownListAdder;
        }
    }

    /// <summary>
    /// These exist so the typed schema signatures above never have to change again.
    /// Adding a parameter to an existing public method is a BINARY-BREAKING
    /// change in C#: a caller bakes the whole argument list into its IL, so a
    /// consumer compiled against the old signature throws MissingMethodException
    /// against the new assembly even though its source would still compile
    /// unchanged. That once broke every mod built on Spine, from a change that
    /// looked additive.
    ///
    /// So: do not add parameters to shipped schema methods. Add a refinement
    /// here. A new method is binary-safe, and consumers opt in by recompiling.
    /// </summary>
    public static class SettingRefinements
    {
        /// <summary>Sets the reset and absent-key default for this definition.</summary>
        public static SettingDefinition DefaultTo(this SettingDefinition definition, object value)
        {
            if (definition != null)
            {
                definition.DefaultValue = value;
            }

            return definition;
        }

        /// <summary>Marks this setting as controlling the visibility of its children.</summary>
        public static SettingDefinition ControlsChildren(this SettingDefinition definition)
        {
            if (definition != null)
            {
                definition.ControlsChildVisibility = true;
            }

            return definition;
        }

        /// <summary>Uses a specific persisted key for this definition.</summary>
        public static SettingDefinition ScribeAs(this SettingDefinition definition, string key)
        {
            if (definition != null)
            {
                definition.ScribeKey = key;
            }

            return definition;
        }

        /// <summary>Sets the localized label and tooltip keys for this definition.</summary>
        public static SettingDefinition Localized(this SettingDefinition definition, string labelKey, string tooltipKey)
        {
            if (definition != null)
            {
                definition.LabelKey = labelKey;
                definition.TooltipKey = tooltipKey;
            }

            return definition;
        }

        public static SettingDefinition WithScribeDefault(this SettingDefinition definition, object value)
        {
            if (definition != null)
            {
                definition.ScribeDefaultOverride = value;
            }

            return definition;
        }

        public static SettingDefinition WithoutAutoScribe(this SettingDefinition definition)
        {
            if (definition != null)
            {
                definition.DisableAutoScribe = true;
            }

            return definition;
        }

        public static SettingDefinition ClassifiedAs(this SettingDefinition definition, SettingClassification classification)
        {
            if (definition != null)
            {
                definition.Classification = classification;
            }

            return definition;
        }

        public static SettingDefinition Under(this SettingDefinition definition, string parentId)
        {
            if (definition != null)
            {
                definition.ParentId = parentId;
            }

            return definition;
        }

        public static SettingDefinition Ordered(this SettingDefinition definition, int sortOrder)
        {
            if (definition != null)
            {
                definition.SortOrder = sortOrder;
            }

            return definition;
        }

        public static SettingDefinition SearchableBy(this SettingDefinition definition, params string[] keywords)
        {
            if (definition != null)
            {
                definition.SearchKeywords = keywords;
            }

            return definition;
        }

        public static SettingDefinition ShownIn(this SettingDefinition definition, bool simple, bool advanced = true)
        {
            if (definition != null)
            {
                definition.ShowInSimpleView = simple;
                definition.ShowInAdvancedView = advanced;
            }

            return definition;
        }

        public static SettingDefinition Accented(this SettingDefinition definition, Color color)
        {
            if (definition != null)
            {
                definition.HeaderColor = color;
            }

            return definition;
        }

        public static SettingDefinition RestartRequired(this SettingDefinition definition)
        {
            if (definition != null)
            {
                definition.RequiresRestart = true;
            }

            return definition;
        }

        /// <summary>Holds this entry outside the scrolling region.</summary>
        public static SettingDefinition Pinned(this SettingDefinition definition, SettingPin pin)
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
        public static SettingDefinition ShownWhen(this SettingDefinition definition, Func<object, bool> predicate)
        {
            if (definition != null)
            {
                definition.VisibleWhen = predicate;
            }

            return definition;
        }

        /// <summary>Hides this entry from the Simple view.</summary>
        public static SettingDefinition AdvancedOnly(this SettingDefinition definition)
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
        public static SettingDefinition Range(this SettingDefinition definition, float min, float max)
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
        public static SettingDefinition Step(this SettingDefinition definition, float step)
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
        public static SettingDefinition ShowsPercent(this SettingDefinition definition)
        {
            return definition.ShowsValue(value => Mathf.RoundToInt(value * 100f) + "%");
        }

        /// <summary>
        /// Replaces the numeric readout beside a slider. Use for units, counts,
        /// or a word standing in for a band of values.
        /// </summary>
        public static SettingDefinition ShowsValue(this SettingDefinition definition, Func<float, string> formatter)
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
        public static SettingDefinition Configure(this SettingDefinition definition, Action<SettingDefinition> configure)
        {
            if (definition != null)
            {
                configure?.Invoke(definition);
            }

            return definition;
        }

        /// <summary>Sets the legacy numeric bounds used by a consumer renderer.</summary>
        public static SettingDefinition ValueRange(this SettingDefinition definition, float min, float max)
        {
            if (definition != null)
            {
                definition.MinValue = Mathf.Min(min, max);
                definition.MaxValue = Mathf.Max(min, max);
            }

            return definition;
        }

        /// <summary>Sets the optional labels at the ends of a numeric control.</summary>
        public static SettingDefinition ValueLabels(this SettingDefinition definition, string minLabel, string maxLabel)
        {
            if (definition != null)
            {
                definition.MinLabel = minLabel;
                definition.MaxLabel = maxLabel;
            }

            return definition;
        }

        /// <summary>Sets the format string used for a numeric readout.</summary>
        public static SettingDefinition FormattedAs(this SettingDefinition definition, string format)
        {
            if (definition != null)
            {
                definition.ValueFormat = format;
            }

            return definition;
        }

        /// <summary>Marks a boolean definition for consumer-provided emphasis.</summary>
        public static SettingDefinition Emphasized(this SettingDefinition definition, bool value = true)
        {
            if (definition != null)
            {
                definition.EmphasizeAsHeader = value;
            }

            return definition;
        }

        /// <summary>Sets the dynamic options and selection callback for a dropdown row.</summary>
        public static SettingDefinition OptionsFrom(this SettingDefinition definition, Func<IEnumerable<string>> optionsProvider, Action<string> onOptionAdded)
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
        public static SettingDefinition SuppressedWhen<TSettings>(this SettingDefinition definition, Func<TSettings, bool> when, Func<TSettings, string> reason,
                                                                  string suppressorSettingId = null, string linkLabel = null, string externalActionUrl = null,
                                                                  string externalActionLabel = null, string externalActionTooltip = null)
        {
            if (definition != null)
            {
                if (definition.Suppressions == null)
                {
                    definition.Suppressions = new List<SettingSuppression>();
                }

                definition.Suppressions.Add(new SettingSuppression { When = when == null ? null : settings => when ((TSettings)settings),
                                                                     Reason = reason == null ? null : settings => reason((TSettings)settings),
                                                                     SuppressorSettingId = suppressorSettingId, LinkLabel = linkLabel, ExternalActionUrl = externalActionUrl,
                                                                     ExternalActionLabel = externalActionLabel, ExternalActionTooltip = externalActionTooltip });
            }

            return definition;
        }

        /// <summary>Sets the custom-row reset hooks used by a consumer renderer.</summary>
        public static SettingDefinition WithCustomReset(this SettingDefinition definition, Func<object, bool> hasNonDefaultValue, Action<object> reset)
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
