using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Serializes registry-backed Better Work Tab settings to a small JSON payload.
    /// Runtime data such as rulesets, workloads, dividers, and layouts stay in their
    /// existing save/config systems.
    /// </summary>
    public static class BWTSettingsJsonService
    {
        private const string FormatName = "BetterWorkTabSettings";

        public static string Export(BetterWorkTabSettings settings)
        {
            BWTSettingsRegistry.EnsureInitialized();

            var emittedFields = new HashSet<string>();
            var builder = new StringBuilder();
            builder.AppendLine("{");
            AppendProperty(builder, "format", FormatName, trailingComma: true, indent: 2);
            AppendProperty(builder, "formatVersion", 1, trailingComma: true, indent: 2);
            AppendProperty(builder, "exportedAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), trailingComma: true, indent: 2);
            builder.AppendLine("  \"settings\": {");

            bool wroteAny = false;
            foreach (var def in BWTSettingsRegistry.Definitions)
            {
                if (!TryGetSupportedField(settings, def, out FieldInfo field) ||
                    !emittedFields.Add(def.FieldName))
                {
                    continue;
                }

                if (wroteAny)
                {
                    builder.AppendLine(",");
                }

                builder.Append("    ");
                AppendJsonString(builder, def.Id);
                builder.Append(": ");
                AppendValue(builder, field.GetValue(settings), field.FieldType);
                wroteAny = true;
            }

            if (wroteAny)
            {
                builder.AppendLine();
            }

            builder.AppendLine("  }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        public static bool TryImport(
            BetterWorkTabSettings settings,
            string json,
            out string report)
        {
            report = string.Empty;
            if (settings == null)
            {
                report = "No settings object is available.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                report = "The import data is empty.";
                return false;
            }

            if (!MiniJson.TryReadSettingsObject(json, out var values, out string error))
            {
                report = error;
                return false;
            }

            BWTSettingsRegistry.EnsureInitialized();
            var definitionsById = new Dictionary<string, SettingDefinition>();
            foreach (var def in BWTSettingsRegistry.Definitions)
            {
                if (def != null && !string.IsNullOrEmpty(def.Id) && !definitionsById.ContainsKey(def.Id))
                {
                    definitionsById.Add(def.Id, def);
                }
            }

            int imported = 0;
            int skipped = 0;
            foreach (var pair in values)
            {
                if (!definitionsById.TryGetValue(pair.Key, out var def) ||
                    !TryGetSupportedField(settings, def, out FieldInfo field))
                {
                    skipped++;
                    continue;
                }

                if (!TryConvertValue(pair.Value, field.FieldType, out object converted))
                {
                    skipped++;
                    continue;
                }

                field.SetValue(settings, converted);
                def.OnChanged?.Invoke(settings);
                imported++;
            }

            settings.NormalizePrioritySettings();
            settings.Write();
            report = skipped > 0
                ? $"Imported {imported} settings. Skipped {skipped} unsupported or invalid entries."
                : $"Imported {imported} settings.";
            return imported > 0;
        }

        private static bool TryGetSupportedField(
            BetterWorkTabSettings settings,
            SettingDefinition def,
            out FieldInfo field)
        {
            field = null;
            if (settings == null || def == null || string.IsNullOrEmpty(def.FieldName))
            {
                return false;
            }

            field = settings.GetType().GetField(
                def.FieldName,
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

        private static void AppendProperty(StringBuilder builder, string name, object value, bool trailingComma, int indent)
        {
            builder.Append(' ', indent);
            AppendJsonString(builder, name);
            builder.Append(": ");
            AppendValue(builder, value, value?.GetType() ?? typeof(string));
            if (trailingComma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static void AppendValue(StringBuilder builder, object value, Type type)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            if (type == typeof(bool))
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }

            if (type == typeof(int))
            {
                builder.Append(((int)value).ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (type == typeof(float))
            {
                builder.Append(((float)value).ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }

            if (type == typeof(double))
            {
                builder.Append(((double)value).ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }

            if (type == typeof(Color))
            {
                AppendJsonString(builder, ColorToHex((Color)value));
                return;
            }

            AppendJsonString(builder, value.ToString());
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            builder.Append('"');
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
                    value = raw is double d
                        ? Mathf.RoundToInt((float)d)
                        : int.Parse(raw.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(float))
                {
                    value = raw is double d ? (float)d : float.Parse(raw.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(double))
                {
                    value = raw is double d ? d : double.Parse(raw.ToString(), CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(string))
                {
                    value = raw?.ToString() ?? string.Empty;
                    return true;
                }

                if (targetType == typeof(Color))
                {
                    return raw is string colorText && ColorUtility.TryParseHtmlString(colorText, out var color)
                        ? SetConverted(color, out value)
                        : false;
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

        private static bool SetConverted<T>(T converted, out object value)
        {
            value = converted;
            return true;
        }

        private static string ColorToHex(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }

        private static class MiniJson
        {
            public static bool TryReadSettingsObject(
                string json,
                out Dictionary<string, object> values,
                out string error)
            {
                values = new Dictionary<string, object>();
                error = string.Empty;
                int settingsIndex = json.IndexOf("\"settings\"", StringComparison.OrdinalIgnoreCase);
                if (settingsIndex < 0)
                {
                    error = "The JSON does not contain a settings object.";
                    return false;
                }

                int objectStart = json.IndexOf('{', settingsIndex);
                if (objectStart < 0)
                {
                    error = "The settings object is malformed.";
                    return false;
                }

                int i = objectStart + 1;
                while (i < json.Length)
                {
                    SkipWhitespace(json, ref i);
                    if (i < json.Length && json[i] == '}')
                    {
                        return true;
                    }

                    if (!TryReadString(json, ref i, out string key, out error))
                    {
                        return false;
                    }

                    SkipWhitespace(json, ref i);
                    if (i >= json.Length || json[i] != ':')
                    {
                        error = $"Expected ':' after setting '{key}'.";
                        return false;
                    }

                    i++;
                    SkipWhitespace(json, ref i);
                    if (!TryReadValue(json, ref i, out object value, out error))
                    {
                        return false;
                    }

                    values[key] = value;
                    SkipWhitespace(json, ref i);
                    if (i < json.Length && json[i] == ',')
                    {
                        i++;
                        continue;
                    }

                    if (i < json.Length && json[i] == '}')
                    {
                        return true;
                    }
                }

                error = "The settings object was not closed.";
                return false;
            }

            private static bool TryReadValue(string json, ref int i, out object value, out string error)
            {
                value = null;
                error = string.Empty;
                if (i >= json.Length)
                {
                    error = "Unexpected end of JSON.";
                    return false;
                }

                if (json[i] == '"')
                {
                    return TryReadString(json, ref i, out string text, out error)
                        ? SetConverted(text, out value)
                        : false;
                }

                if (StartsWith(json, i, "true"))
                {
                    i += 4;
                    value = true;
                    return true;
                }

                if (StartsWith(json, i, "false"))
                {
                    i += 5;
                    value = false;
                    return true;
                }

                if (StartsWith(json, i, "null"))
                {
                    i += 4;
                    value = null;
                    return true;
                }

                int start = i;
                while (i < json.Length && "-+0123456789.eE".IndexOf(json[i]) >= 0)
                {
                    i++;
                }

                if (start == i ||
                    !double.TryParse(json.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    error = "Unsupported JSON value in settings object.";
                    return false;
                }

                value = number;
                return true;
            }

            private static bool TryReadString(string json, ref int i, out string value, out string error)
            {
                value = string.Empty;
                error = string.Empty;
                if (i >= json.Length || json[i] != '"')
                {
                    error = "Expected a JSON string.";
                    return false;
                }

                i++;
                var builder = new StringBuilder();
                while (i < json.Length)
                {
                    char c = json[i++];
                    if (c == '"')
                    {
                        value = builder.ToString();
                        return true;
                    }

                    if (c == '\\')
                    {
                        if (i >= json.Length)
                        {
                            error = "Invalid JSON string escape.";
                            return false;
                        }

                        char escaped = json[i++];
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

                    builder.Append(c);
                }

                error = "Unclosed JSON string.";
                return false;
            }

            private static void SkipWhitespace(string json, ref int i)
            {
                while (i < json.Length && char.IsWhiteSpace(json[i]))
                {
                    i++;
                }
            }

            private static bool StartsWith(string text, int index, string value)
            {
                return index + value.Length <= text.Length &&
                    string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
            }
        }
    }
}
