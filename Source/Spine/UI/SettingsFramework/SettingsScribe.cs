using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Spine.UI.SettingsFramework
{
    public static class SettingsScribe
    {
        private static readonly MethodInfo ScribeValuesLookMethod = typeof(Scribe_Values)
            .GetMethods()
            .First(method =>
            {
                if (method.Name != "Look" || !method.IsGenericMethodDefinition)
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length >= 3 &&
                    parameters[0].ParameterType.IsByRef &&
                    parameters[1].ParameterType == typeof(string);
            });

        public static void ScribeAll(object settings, IEnumerable<SettingDefinition> definitions)
        {
            if (settings == null || definitions == null)
            {
                return;
            }

            Type settingsType = settings.GetType();
            foreach (SettingDefinition def in definitions)
            {
                if (def == null ||
                    def.Classification == SettingClassification.State ||
                    def.DisableAutoScribe ||
                    string.IsNullOrEmpty(def.FieldName))
                {
                    continue;
                }

                FieldInfo field = settingsType.GetField(def.FieldName);
                if (field == null || typeof(IEnumerable).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(string))
                {
                    continue;
                }

                object defaultValue = def.ScribeDefaultOverride ?? def.DefaultValue;
                if (defaultValue == null)
                {
                    continue;
                }

                string scribeKey = string.IsNullOrEmpty(def.ScribeKey) ? def.FieldName : def.ScribeKey;
                object[] args = CreateScribeArgs(field.GetValue(settings), scribeKey, defaultValue);
                ScribeValuesLookMethod.MakeGenericMethod(field.FieldType).Invoke(null, args);
                field.SetValue(settings, args[0]);
            }
        }

        public static void ApplyPreferenceDefaults(object settings, IEnumerable<SettingDefinition> definitions)
        {
            if (settings == null || definitions == null)
            {
                return;
            }

            Type settingsType = settings.GetType();
            foreach (SettingDefinition def in definitions)
            {
                if (def == null ||
                    def.Classification == SettingClassification.State ||
                    def.DefaultValue == null ||
                    string.IsNullOrEmpty(def.FieldName))
                {
                    continue;
                }

                FieldInfo field = settingsType.GetField(def.FieldName);
                if (field == null || !field.FieldType.IsInstanceOfType(def.DefaultValue))
                {
                    continue;
                }

                field.SetValue(settings, def.DefaultValue);
            }
        }

        public static string EffectiveScribeKey(SettingDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.FieldName))
            {
                return null;
            }

            return string.IsNullOrEmpty(def.ScribeKey) ? def.FieldName : def.ScribeKey;
        }

        private static object[] CreateScribeArgs(object value, string scribeKey, object defaultValue)
        {
            ParameterInfo[] parameters = ScribeValuesLookMethod.GetParameters();
            object[] args = new object[parameters.Length];
            args[0] = value;
            args[1] = scribeKey;
            args[2] = defaultValue;

            for (int i = 3; i < args.Length; i++)
            {
                args[i] = parameters[i].DefaultValue == DBNull.Value ? Type.Missing : parameters[i].DefaultValue;
            }

            return args;
        }
    }
}
