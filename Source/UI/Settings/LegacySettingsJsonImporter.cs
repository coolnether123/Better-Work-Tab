using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Spine.UI.SettingsFramework;
using UnityEngine;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Compatibility reader for version 1 preference-only JSON exports.
    /// Version 2 uses BWTSettingsJsonService's complete Scribe-backed payload.
    /// </summary>
    internal static class LegacySettingsJsonImporter
    {
        internal static bool TryImport(
            BetterWorkTabSettings settings,
            string json,
            out string report)
        {
            if (!MiniJson.TryReadSettingsObject(json, out var values, out string error))
            {
                report = error;
                return false;
            }

            BWTSettingsRegistry.EnsureInitialized();
            var definitionsById = new Dictionary<string, SettingDefinition>();
            foreach (SettingDefinition definition in BWTSettingsRegistry.Definitions)
            {
                if (definition != null &&
                    !string.IsNullOrEmpty(definition.Id) &&
                    !definitionsById.ContainsKey(definition.Id))
                {
                    definitionsById.Add(definition.Id, definition);
                }
            }

            int imported = 0;
            int skipped = 0;
            foreach (KeyValuePair<string, object> pair in values)
            {
                if (!definitionsById.TryGetValue(pair.Key, out SettingDefinition definition) ||
                    !TryGetSupportedField(settings, definition, out FieldInfo field) ||
                    !TryConvertValue(pair.Value, field.FieldType, out object converted))
                {
                    skipped++;
                    continue;
                }

                field.SetValue(settings, converted);
                definition.OnChanged?.Invoke(settings);
                imported++;
            }

            settings.NormalizePrioritySettings();
            settings.Write();
            report = skipped > 0
                ? $"Imported {imported} legacy settings. Skipped {skipped} unsupported or invalid entries."
                : $"Imported {imported} legacy settings.";
            return imported > 0;
        }

        private static bool TryGetSupportedField(
            BetterWorkTabSettings settings,
            SettingDefinition definition,
            out FieldInfo field)
        {
            field = null;
            if (settings == null || definition == null || string.IsNullOrEmpty(definition.FieldName))
            {
                return false;
            }

            field = settings.GetType().GetField(
                definition.FieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null && IsSupportedType(field.FieldType);
        }

        private static bool IsSupportedType(Type type)
        {
            return type == typeof(bool) ||
                type == typeof(int) ||
                type == typeof(float) ||
                type == typeof(double) ||
                type == typeof(string) ||
                type == typeof(Color) ||
                type.IsEnum;
        }

        private static bool TryConvertValue(object raw, Type targetType, out object value)
        {
            value = null;
            try
            {
                if (targetType == typeof(bool))
                {
                    if (raw is bool boolValue)
                    {
                        value = boolValue;
                        return true;
                    }

                    if (raw is string boolText && bool.TryParse(boolText, out bool parsedBool))
                    {
                        value = parsedBool;
                        return true;
                    }

                    return false;
                }

                if (targetType == typeof(int))
                {
                    value = raw is double doubleValue
                        ? Mathf.RoundToInt((float)doubleValue)
                        : int.Parse(raw.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(float))
                {
                    value = raw is double doubleValue
                        ? (float)doubleValue
                        : float.Parse(raw.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(double))
                {
                    value = raw is double doubleValue
                        ? doubleValue
                        : double.Parse(raw.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(string))
                {
                    value = raw?.ToString() ?? string.Empty;
                    return true;
                }

                if (targetType == typeof(Color))
                {
                    if (raw is string colorText && ColorUtility.TryParseHtmlString(colorText, out Color color))
                    {
                        value = color;
                        return true;
                    }

                    return false;
                }

                if (targetType.IsEnum)
                {
                    value = raw is double enumNumber
                        ? Enum.ToObject(targetType, Convert.ToInt32(enumNumber))
                        : Enum.Parse(targetType, raw.ToString(), ignoreCase: true);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static class MiniJson
        {
            internal static bool TryReadSettingsObject(
                string json,
                out Dictionary<string, object> values,
                out string error)
            {
                values = new Dictionary<string, object>();
                error = string.Empty;
                int settingsIndex = json.IndexOf("\"settings\"", StringComparison.OrdinalIgnoreCase);
                if (settingsIndex < 0)
                {
                    error = "The JSON does not contain complete data or a legacy settings object.";
                    return false;
                }

                int objectStart = json.IndexOf('{', settingsIndex);
                if (objectStart < 0)
                {
                    error = "The legacy settings object is malformed.";
                    return false;
                }

                int index = objectStart + 1;
                while (index < json.Length)
                {
                    SkipWhitespace(json, ref index);
                    if (index < json.Length && json[index] == '}')
                    {
                        return true;
                    }

                    if (!TryReadString(json, ref index, out string key, out error))
                    {
                        return false;
                    }

                    SkipWhitespace(json, ref index);
                    if (index >= json.Length || json[index] != ':')
                    {
                        error = $"Expected ':' after setting '{key}'.";
                        return false;
                    }

                    index++;
                    SkipWhitespace(json, ref index);
                    if (!TryReadValue(json, ref index, out object value, out error))
                    {
                        return false;
                    }

                    values[key] = value;
                    SkipWhitespace(json, ref index);
                    if (index < json.Length && json[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    if (index < json.Length && json[index] == '}')
                    {
                        return true;
                    }
                }

                error = "The legacy settings object was not closed.";
                return false;
            }

            private static bool TryReadValue(
                string json,
                ref int index,
                out object value,
                out string error)
            {
                value = null;
                error = string.Empty;
                if (index >= json.Length)
                {
                    error = "Unexpected end of JSON.";
                    return false;
                }

                if (json[index] == '"')
                {
                    if (!TryReadString(json, ref index, out string text, out error))
                    {
                        return false;
                    }

                    value = text;
                    return true;
                }

                if (StartsWith(json, index, "true"))
                {
                    index += 4;
                    value = true;
                    return true;
                }

                if (StartsWith(json, index, "false"))
                {
                    index += 5;
                    value = false;
                    return true;
                }

                if (StartsWith(json, index, "null"))
                {
                    index += 4;
                    return true;
                }

                int start = index;
                while (index < json.Length && "-+0123456789.eE".IndexOf(json[index]) >= 0)
                {
                    index++;
                }

                if (start == index ||
                    !double.TryParse(
                        json.Substring(start, index - start),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double number))
                {
                    error = "Unsupported JSON value in legacy settings object.";
                    return false;
                }

                value = number;
                return true;
            }

            private static bool TryReadString(
                string json,
                ref int index,
                out string value,
                out string error)
            {
                value = string.Empty;
                error = string.Empty;
                if (index >= json.Length || json[index] != '"')
                {
                    error = "Expected a JSON string.";
                    return false;
                }

                index++;
                var builder = new StringBuilder();
                while (index < json.Length)
                {
                    char character = json[index++];
                    if (character == '"')
                    {
                        value = builder.ToString();
                        return true;
                    }

                    if (character == '\\')
                    {
                        if (index >= json.Length)
                        {
                            error = "Invalid JSON string escape.";
                            return false;
                        }

                        char escaped = json[index++];
                        switch (escaped)
                        {
                            case '"':
                            case '\\':
                            case '/':
                                builder.Append(escaped);
                                break;
                            case 'n':
                                builder.Append('\n');
                                break;
                            case 'r':
                                builder.Append('\r');
                                break;
                            case 't':
                                builder.Append('\t');
                                break;
                            default:
                                builder.Append(escaped);
                                break;
                        }

                        continue;
                    }

                    builder.Append(character);
                }

                error = "Unclosed JSON string.";
                return false;
            }

            private static void SkipWhitespace(string json, ref int index)
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }

            private static bool StartsWith(string text, int index, string value)
            {
                return index + value.Length <= text.Length &&
                    string.Compare(
                        text,
                        index,
                        value,
                        0,
                        value.Length,
                        StringComparison.OrdinalIgnoreCase) == 0;
            }
        }
    }
}
