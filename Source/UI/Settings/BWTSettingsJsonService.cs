using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Better_Work_Tab.Features.Tutorial;
using System.Xml;
using Spine.RimWorld.Serialization;
using Spine.UI.ColourPicker;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Serializes all Better Work Tab preferences and config-backed state using
    /// RimWorld's serializer inside a small JSON envelope.
    /// </summary>
    public static class BWTSettingsJsonService
    {
        private const string FormatName = "BetterWorkTabSettings";
        private const int FormatVersion = 2;
        private const string CompleteDataProperty = "data";
        private const string CompleteDataRootName = "BetterWorkTabExport";
        private const string CompleteDataNodeName = "Data";
        private const int SettingsExportBlockedWarningKey = 154927303;
        private const int SettingsImportBlockedWarningKey = 154927304;

        public static string Export(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var builder = new StringBuilder();
            builder.AppendLine("{");
            AppendProperty(builder, "format", FormatName, trailingComma: true);
            AppendProperty(builder, "formatVersion", FormatVersion, trailingComma: true);
            AppendProperty(
                builder,
                "exportedAtUtc",
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                trailingComma: true);
            AppendProperty(builder, CompleteDataProperty, ExportCompleteData(settings), trailingComma: false);
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

            // Import replaces the live settings object in place. A workload
            // preview owns a projected presentation state, so accepting an
            // import here would either leak global values into the preview or
            // leave the preview's ownership snapshot stale. Reject before any
            // parsing or field mutation; the UI uses the same policy to hide
            // the import affordance, but this is the authoritative boundary.
            if (BWTWorkloadSettingsOwnershipPolicy.IsBulkSettingsOperationBlocked(
                    out string blockedReason))
            {
                report = blockedReason;
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                report = "The import data is empty.";
                return false;
            }

            if (settings.IsPersistenceReadOnly)
            {
                report = string.IsNullOrEmpty(settings.PersistenceDiagnostic)
                    ? "The current settings document is read-only for diagnostics and cannot be rewritten."
                    : settings.PersistenceDiagnostic;
                return false;
            }

            bool hasCompleteData = HasJsonProperty(json, CompleteDataProperty);
            if (!hasCompleteData)
            {
                // A versioned envelope without the complete payload is not a
                // legacy export. Do not silently reinterpret a future/unknown
                // document as the old preference-only shape.
                if (HasJsonProperty(json, "format") || HasJsonProperty(json, "formatVersion"))
                {
                    report = "The settings envelope is incomplete or uses an unsupported format.";
                    return false;
                }

                // Version 1 exports contained registry-backed scalar preferences under a
                // "settings" object. Keep them importable after version 2 expanded the payload
                // to all config-backed state.
                return LegacySettingsJsonImporter.TryImport(settings, json, out report);
            }

            if (!TryValidateJsonEnvelope(json, out report) ||
                !TryReadStringProperty(json, CompleteDataProperty, out string encodedData))
            {
                if (string.IsNullOrEmpty(report))
                {
                    report = "The settings envelope does not contain a valid complete payload.";
                }

                return false;
            }

            return TryImportCompleteData(settings, encodedData, out report);
        }

        private static string ExportCompleteData(BetterWorkTabSettings settings)
        {
            if (!ScribeIsolationGuard.CanStart(
                "BWT",
                "settings export",
                SettingsExportBlockedWarningKey))
            {
                throw new InvalidOperationException(
                    "Settings cannot be exported while RimWorld or Multiplayer is serializing.");
            }

            string path = TemporaryExportPath("Export");
            bool ownsSaver = false;
            try
            {
                var data = new CompleteSettingsData
                {
                    Settings = settings,
                    RecentColors = RecentColours.CopyRecentColors(),
                    PinnedColors = RecentColours.CopyPinnedColors()
                };

                Scribe.saver.InitSaving(path, "BetterWorkTabExport");
                ownsSaver = true;
                Scribe_Deep.Look(ref data, "Data");
                Scribe.saver.FinalizeSaving();
                ownsSaver = false;
                return Convert.ToBase64String(File.ReadAllBytes(path));
            }
            finally
            {
                // FinalizeSaving is not cleanup. Abort only an operation this method started;
                // never finalize or stop serialization owned by the game or another mod.
                if (ownsSaver && Scribe.mode == LoadSaveMode.Saving)
                {
                    Scribe.saver.ForceStop();
                }

                TryDelete(path);
            }
        }

        private static bool TryImportCompleteData(
            BetterWorkTabSettings destination,
            string encodedData,
            out string report)
        {
            // Keep the private complete-data path fail-closed as well. This
            // protects future callers that may bypass TryImport's legacy
            // envelope dispatch.
            if (BWTWorkloadSettingsOwnershipPolicy.IsBulkSettingsOperationBlocked(
                    out string blockedReason))
            {
                report = blockedReason;
                return false;
            }

            if (!ScribeIsolationGuard.CanStart(
                "BWT",
                "settings import",
                SettingsImportBlockedWarningKey))
            {
                report = "Settings cannot be imported while RimWorld or Multiplayer is serializing.";
                return false;
            }

            string path = TemporaryExportPath("Import");
            bool ownsLoader = false;
            bool workloadModeChanged = false;
            bool settingsWriteSucceeded = false;
            bool previousLegacyMode = false;
            try
            {
                File.WriteAllBytes(path, Convert.FromBase64String(encodedData));
                if (!TryValidateCompleteDataDocument(path, out string documentError))
                {
                    report = documentError;
                    return false;
                }

                CompleteSettingsData data = null;
                Scribe.loader.InitLoading(path);
                ownsLoader = true;
                Scribe_Deep.Look(ref data, "Data");
                Scribe.loader.FinalizeLoading();
                ownsLoader = false;

                if (data?.Settings == null)
                {
                    report = "The settings export is empty.";
                    return false;
                }

                if (data.Settings.IsPersistenceReadOnly)
                {
                    report = string.IsNullOrEmpty(data.Settings.PersistenceDiagnostic)
                        ? "The settings export contains fields or a schema this build cannot preserve."
                        : data.Settings.PersistenceDiagnostic;
                    return false;
                }

                previousLegacyMode = destination.useLegacyWorkloads;
                var previousPreferenceValues = new Dictionary<string, object>();
                foreach (SettingDefinition definition in BWTSettingsRegistry.Definitions)
                {
                    if (definition == null ||
                        string.IsNullOrEmpty(definition.FieldName) ||
                        previousPreferenceValues.ContainsKey(definition.FieldName))
                    {
                        continue;
                    }

                    FieldInfo field = typeof(BetterWorkTabSettings).GetField(
                        definition.FieldName,
                        BindingFlags.Instance | BindingFlags.Public);
                    if (field == null || field.IsInitOnly || field.IsLiteral)
                    {
                        continue;
                    }

                    previousPreferenceValues.Add(definition.FieldName, field.GetValue(destination));
                }

                if (data.Settings.useLegacyWorkloads != destination.useLegacyWorkloads)
                {
                    if (!BWTWorkloadSettingsOwnershipPolicy.TryTransitionMode(
                            data.Settings.useLegacyWorkloads,
                            persist: false,
                            out string transitionReason))
                    {
                        report = string.IsNullOrEmpty(transitionReason)
                            ? "Couldn't change workload mode."
                            : transitionReason;
                        return false;
                    }

                    workloadModeChanged = true;
                }

                TransferLoadedSettingsState(data.Settings, destination);
                if (destination.viewedSettingIds != null)
                {
                    for (int i = 0; i < destination.viewedSettingIds.Count; i++)
                    {
                        if (destination.viewedSettingIds[i] == "layout.workTabMaxVisiblePawns")
                        {
                            destination.viewedSettingIds[i] = SettingIDs.LayoutWorkTabMaxVisiblePawns;
                        }
                    }
                }

                // These existing owners repair derived references and persisted
                // preference invariants after the staged graph is adopted.
                destination.NormalizePrioritySettings();
                destination.InitializeRulesets();

                var changedPreferenceFields = new HashSet<string>();
                foreach (KeyValuePair<string, object> previousPreference in previousPreferenceValues)
                {
                    FieldInfo field = typeof(BetterWorkTabSettings).GetField(
                        previousPreference.Key,
                        BindingFlags.Instance | BindingFlags.Public);
                    if (field != null &&
                        !Equals(previousPreference.Value, field.GetValue(destination)))
                    {
                        changedPreferenceFields.Add(previousPreference.Key);
                    }
                }

                // The gateway owns this field's transition; never invoke its
                // registered reaction a second time during the import.
                changedPreferenceFields.Remove(nameof(BetterWorkTabSettings.useLegacyWorkloads));
                BWTSettingsRegistry.Schema.NotifyPreferenceChanges(
                    destination,
                    changedPreferenceFields);
                RecentColours.ReplaceAll(data.RecentColors, data.PinnedColors);
                destination.Write();
                settingsWriteSucceeded = true;
                BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
                if (workloadModeChanged)
                {
                    // The settings commit is complete before the ownership
                    // observer and Work-tab presentation are synchronized.
                    BWTWorkloadSettingsOwnershipPolicy.Refresh();
                    WorkTabInvalidationHub.Invalidate(
                        WorkTabDirtyFlags.SettingsThemeLanguageScale);
                }

                report = "Imported all settings data, including settings history, rulesets, layout state, and recent colors.";
                return true;
            }
            catch (Exception ex)
            {
                if (workloadModeChanged && !settingsWriteSucceeded)
                {
                    destination.useLegacyWorkloads = previousLegacyMode;
                }

                report = "The settings export could not be imported: " + ex.Message;
                return false;
            }
            finally
            {
                // FinalizeLoading runs callbacks and is not a recovery API. Force-stop only the
                // standalone loader this method successfully started and still owns.
                if (ownsLoader &&
                    (Scribe.mode == LoadSaveMode.LoadingVars ||
                     Scribe.mode == LoadSaveMode.ResolvingCrossRefs ||
                     Scribe.mode == LoadSaveMode.PostLoadInit))
                {
                    Scribe.loader.ForceStop();
                }

                TryDelete(path);
            }
        }

        private static void TransferLoadedSettingsState(
            BetterWorkTabSettings source,
            BetterWorkTabSettings destination)
        {
            // Keep compatibility transfer broad across the public settings surface,
            // but do not reflect all non-public fields: persistence diagnostics and
            // future transient implementation state must not cross this boundary.
            foreach (FieldInfo field in typeof(BetterWorkTabSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.IsInitOnly && !field.IsLiteral)
                {
                    field.SetValue(destination, field.GetValue(source));
                }
            }

            // Every non-public field written by BetterWorkTabSettings.ExposeData is
            // intentionally explicit here. These assignments adopt the Scribe-loaded
            // graph and normalize only the nullable stores whose owners require them.
            destination.tutorialProgressSchemaVersion = source.tutorialProgressSchemaVersion;
            destination.selectedTutorialCourse = source.selectedTutorialCourse;
            destination.tutorialMigratedFromPublic105 = source.tutorialMigratedFromPublic105;
            destination.skippedTutorialLessonIds =
                source.skippedTutorialLessonIds ?? new List<string>();
            destination.tutorialLessonIdsAlreadyUsed =
                source.tutorialLessonIdsAlreadyUsed ?? new List<string>();
            destination.tutorialDiscoveryOfferAcknowledged =
                source.tutorialDiscoveryOfferAcknowledged;
        }

        private static string TemporaryExportPath(string operation)
        {
            return Path.Combine(
                Path.GetTempPath(),
                "BetterWorkTab" + operation + "_" + Guid.NewGuid().ToString("N") + ".xml");
        }

        private static void AppendProperty(StringBuilder builder, string name, object value, bool trailingComma)
        {
            builder.Append("  ");
            AppendJsonString(builder, name);
            builder.Append(": ");

            if (value is int integer)
            {
                builder.Append(integer.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                AppendJsonString(builder, value?.ToString() ?? string.Empty);
            }

            if (trailingComma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
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
                        builder.Append(character);
                        break;
                }
            }

            builder.Append('"');
        }

        private static bool TryReadStringProperty(string json, string propertyName, out string value)
        {
            value = null;
            string token = "\"" + propertyName + "\"";
            int propertyIndex = json.IndexOf(token, StringComparison.Ordinal);
            if (propertyIndex < 0)
            {
                return false;
            }

            int colonIndex = json.IndexOf(':', propertyIndex + token.Length);
            if (colonIndex < 0)
            {
                return false;
            }

            int index = colonIndex + 1;
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }

            if (index >= json.Length || json[index] != '"')
            {
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

                if (character == '\\' && index < json.Length)
                {
                    char escaped = json[index++];
                    switch (escaped)
                    {
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
                }
                else
                {
                    builder.Append(character);
                }
            }

            return false;
        }

        private static bool TryValidateJsonEnvelope(string json, out string report)
        {
            report = string.Empty;
            if (!TryReadTopLevelPropertyNames(json, out HashSet<string> propertyNames, out report))
            {
                return false;
            }

            string[] knownProperties = { "format", "formatVersion", "exportedAtUtc", CompleteDataProperty };
            foreach (string propertyName in propertyNames)
            {
                bool known = false;
                for (int i = 0; i < knownProperties.Length; i++)
                {
                    if (string.Equals(propertyName, knownProperties[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    report = "The settings envelope contains an unknown member and cannot be safely imported.";
                    return false;
                }
            }

            if (!TryReadStringProperty(json, "format", out string format) ||
                !string.Equals(format, FormatName, StringComparison.Ordinal))
            {
                report = "The settings envelope format is not recognized.";
                return false;
            }

            if (!TryReadIntegerProperty(json, "formatVersion", out int version))
            {
                report = "The settings envelope has no valid format version.";
                return false;
            }

            if (version != FormatVersion)
            {
                report = version > FormatVersion
                    ? "The settings export was created by a newer Better Work Tab and cannot be imported by this build."
                    : "The settings export uses an unsupported older format.";
                return false;
            }

            return true;
        }

        private static bool TryReadTopLevelPropertyNames(
            string json,
            out HashSet<string> propertyNames,
            out string error)
        {
            propertyNames = new HashSet<string>(StringComparer.Ordinal);
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The settings envelope is empty.";
                return false;
            }

            int index = 0;
            SkipJsonWhitespace(json, ref index);
            if (index >= json.Length || json[index++] != '{')
            {
                error = "The settings envelope is not a JSON object.";
                return false;
            }

            while (true)
            {
                SkipJsonWhitespace(json, ref index);
                if (index >= json.Length)
                {
                    error = "The settings envelope is truncated.";
                    return false;
                }

                if (json[index] == '}')
                {
                    index++;
                    SkipJsonWhitespace(json, ref index);
                    if (index != json.Length)
                    {
                        error = "The settings envelope contains trailing data.";
                        return false;
                    }

                    return true;
                }

                if (!TryReadJsonStringAt(json, ref index, out string propertyName))
                {
                    error = "The settings envelope contains an invalid property name.";
                    return false;
                }

                if (!propertyNames.Add(propertyName))
                {
                    error = "The settings envelope contains a duplicate property.";
                    return false;
                }

                SkipJsonWhitespace(json, ref index);
                if (index >= json.Length || json[index++] != ':')
                {
                    error = "The settings envelope contains a property without a value.";
                    return false;
                }

                if (!TrySkipJsonValue(json, ref index))
                {
                    error = "The settings envelope contains an invalid property value.";
                    return false;
                }

                SkipJsonWhitespace(json, ref index);
                if (index >= json.Length)
                {
                    error = "The settings envelope is truncated.";
                    return false;
                }

                if (json[index] == ',')
                {
                    index++;
                    continue;
                }

                if (json[index] != '}')
                {
                    error = "The settings envelope contains an invalid member separator.";
                    return false;
                }
            }
        }

        private static bool TryReadJsonStringAt(string json, ref int index, out string value)
        {
            value = null;
            if (index >= json.Length || json[index++] != '"')
            {
                return false;
            }

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
                        return false;
                    }

                    char escaped = json[index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > json.Length ||
                                !int.TryParse(
                                    json.Substring(index, 4),
                                    NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture,
                                    out int codePoint))
                            {
                                return false;
                            }

                            builder.Append((char)codePoint);
                            index += 4;
                            break;
                        default:
                            return false;
                    }

                    continue;
                }

                if (character < ' ')
                {
                    return false;
                }

                builder.Append(character);
            }

            return false;
        }

        private static bool TrySkipJsonValue(string json, ref int index)
        {
            SkipJsonWhitespace(json, ref index);
            if (index >= json.Length)
            {
                return false;
            }

            if (json[index] == '"')
            {
                return TryReadJsonStringAt(json, ref index, out _);
            }

            if (json[index] == '{' || json[index] == '[')
            {
                char open = json[index++];
                char close = open == '{' ? '}' : ']';
                int depth = 1;
                while (index < json.Length && depth > 0)
                {
                    if (json[index] == '"')
                    {
                        if (!TryReadJsonStringAt(json, ref index, out _)) return false;
                        continue;
                    }

                    if (json[index] == open) depth++;
                    if (json[index] == close) depth--;
                    index++;
                }

                return depth == 0;
            }

            int start = index;
            while (index < json.Length && json[index] != ',' && json[index] != '}')
            {
                index++;
            }

            return index > start && !string.IsNullOrWhiteSpace(json.Substring(start, index - start));
        }

        private static void SkipJsonWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }

        private static bool TryReadIntegerProperty(string json, string propertyName, out int value)
        {
            value = 0;
            string token = "\"" + propertyName + "\"";
            int propertyIndex = json.IndexOf(token, StringComparison.Ordinal);
            if (propertyIndex < 0)
            {
                return false;
            }

            int colonIndex = json.IndexOf(':', propertyIndex + token.Length);
            if (colonIndex < 0)
            {
                return false;
            }

            int index = colonIndex + 1;
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }

            int start = index;
            if (index < json.Length && (json[index] == '-' || json[index] == '+'))
            {
                index++;
            }

            while (index < json.Length && char.IsDigit(json[index]))
            {
                index++;
            }

            return index > start &&
                   int.TryParse(
                       json.Substring(start, index - start),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value);
        }

        private static bool HasJsonProperty(string json, string propertyName)
        {
            return !string.IsNullOrEmpty(json) &&
                   json.IndexOf("\"" + propertyName + "\"", StringComparison.Ordinal) >= 0;
        }

        private static bool TryValidateCompleteDataDocument(string path, out string error)
        {
            error = string.Empty;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = false,
                    IgnoreWhitespace = false
                };

                var document = new XmlDocument
                {
                    XmlResolver = null
                };
                using (XmlReader reader = XmlReader.Create(path, settings))
                {
                    document.Load(reader);
                }

                XmlElement root = document.DocumentElement;
                if (root == null || !string.Equals(root.Name, CompleteDataRootName, StringComparison.Ordinal))
                {
                    error = "The settings export has an unrecognized XML root.";
                    return false;
                }

                if (HasUnknownEnvelopeAttributes(root))
                {
                    error = "The settings export root contains an unknown attribute.";
                    return false;
                }

                XmlElement data = null;
                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (!string.Equals(child.Name, CompleteDataNodeName, StringComparison.Ordinal) || data != null)
                    {
                        error = "The settings export contains an unknown or duplicate envelope member.";
                        return false;
                    }

                    data = (XmlElement)child;
                }

                if (data == null)
                {
                    error = "The settings export has no complete data node.";
                    return false;
                }

                if (HasUnknownEnvelopeAttributes(data))
                {
                    error = "The settings export data node contains an unknown attribute.";
                    return false;
                }

                foreach (XmlNode child in data.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (!string.Equals(child.Name, "Settings", StringComparison.Ordinal) &&
                        !string.Equals(child.Name, "RecentColors", StringComparison.Ordinal) &&
                        !string.Equals(child.Name, "PinnedColors", StringComparison.Ordinal))
                    {
                        error = "The settings export contains an unknown complete-data member.";
                        return false;
                    }

                    if (HasUnknownEnvelopeAttributes((XmlElement)child))
                    {
                        error = "The settings export contains an unknown envelope attribute.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "The settings export is not a valid XML document: " + exception.Message;
                return false;
            }
        }

        private static bool HasUnknownEnvelopeAttributes(XmlElement element)
        {
            if (element?.Attributes == null)
            {
                return false;
            }

            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (attribute == null ||
                    string.Equals(attribute.Name, "Class", StringComparison.Ordinal) ||
                    string.Equals(attribute.Name, "IsNull", StringComparison.Ordinal) ||
                    string.Equals(attribute.Name, "MayRequire", StringComparison.Ordinal) ||
                    string.Equals(attribute.Name, "MayRequireAnyOf", StringComparison.Ordinal) ||
                    string.Equals(attribute.Name, "MayRequireAllOf", StringComparison.Ordinal))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public sealed class CompleteSettingsData : IExposable
        {
            public BetterWorkTabSettings Settings;
            public List<Color> RecentColors = new List<Color>();
            public List<Color> PinnedColors = new List<Color>();

            public void ExposeData()
            {
                Scribe_Deep.Look(ref Settings, "Settings");
                Scribe_Collections.Look(ref RecentColors, "RecentColors", LookMode.Value);
                Scribe_Collections.Look(ref PinnedColors, "PinnedColors", LookMode.Value);

                if (RecentColors == null)
                {
                    RecentColors = new List<Color>();
                }

                if (PinnedColors == null)
                {
                    PinnedColors = new List<Color>();
                }
            }
        }
    }
}
