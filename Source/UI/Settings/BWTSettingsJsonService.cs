using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Spine.RimWorld.Serialization;
using Spine.UI.ColourPicker;
using Spine.UI.SettingsFramework;
using UnityEngine;
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

            if (string.IsNullOrWhiteSpace(json))
            {
                report = "The import data is empty.";
                return false;
            }

            if (!TryReadStringProperty(json, CompleteDataProperty, out string encodedData))
            {
                // Version 1 exports contained registry-backed scalar preferences under a
                // "settings" object. Keep them importable after version 2 expanded the payload
                // to all config-backed state.
                return LegacySettingsJsonImporter.TryImport(settings, json, out report);
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
            try
            {
                File.WriteAllBytes(path, Convert.FromBase64String(encodedData));
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

                CopyPublicSettingsFields(data.Settings, destination);
                RecentColours.ReplaceAll(data.RecentColors, data.PinnedColors);
                BWTSettingsRegistry.Schema.NotifyPreferenceChanges(destination);
                destination.NormalizePrioritySettings();
                destination.Write();
                report = "Imported all settings data, including settings history, rulesets, layout state, and recent colors.";
                return true;
            }
            catch (Exception ex)
            {
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

        private static void CopyPublicSettingsFields(BetterWorkTabSettings source, BetterWorkTabSettings destination)
        {
            foreach (FieldInfo field in typeof(BetterWorkTabSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.IsInitOnly && !field.IsLiteral)
                {
                    field.SetValue(destination, field.GetValue(source));
                }
            }
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
