using System;
using System.IO;
using RimWorld;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Creates concrete import/export callbacks for the generic settings drawer footer.
    /// </summary>
    public static class BWTSettingsImportExportActions
    {
        private const string DefaultFileName = "BetterWorkTabSettings.json";

        public static SettingsImportExportActions Create(BetterWorkTabSettings settings, Action afterImport)
        {
            return new SettingsImportExportActions
            {
                ExportLabel = "Export",
                ImportLabel = "Import",
                FileLabel = "File",
                ClipboardLabel = "Clipboard",
                CancelLabel = "Cancel",
                ExportToClipboard = () => ExportToClipboard(settings),
                ExportToFile = () => ShowExportPathDialog(settings),
                ImportFromClipboard = () => ConfirmImport(() => ImportFromClipboard(settings, afterImport)),
                ImportFromFile = () => ConfirmImport(() => ShowImportPathDialog(settings, afterImport))
            };
        }

        private static void ExportToClipboard(BetterWorkTabSettings settings)
        {
            GUIUtility.systemCopyBuffer = BWTSettingsJsonService.Export(settings);
            MessageCompat.Message("Better Work Tab settings copied to clipboard.", MessageTypeDefOf.PositiveEvent, false);
        }

        private static void ShowExportPathDialog(BetterWorkTabSettings settings)
        {
            Find.WindowStack.Add(new Dialog_BWTSettingsJsonPath(
                "Export Better Work Tab Settings",
                DefaultPath,
                "Export",
                path =>
                {
                    try
                    {
                        string directory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllText(path, BWTSettingsJsonService.Export(settings));
                        MessageCompat.Message($"Better Work Tab settings exported to {path}.", MessageTypeDefOf.PositiveEvent, false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Better Work Tab] Failed to export settings to {path}: {ex}");
                        MessageCompat.Message("Failed to export Better Work Tab settings. Check the log for details.", MessageTypeDefOf.RejectInput, false);
                    }
                }));
        }

        private static void ImportFromClipboard(BetterWorkTabSettings settings, Action afterImport)
        {
            TryImport(settings, GUIUtility.systemCopyBuffer, afterImport);
        }

        private static void ShowImportPathDialog(BetterWorkTabSettings settings, Action afterImport)
        {
            Find.WindowStack.Add(new Dialog_BWTSettingsJsonPath(
                "Import Better Work Tab Settings",
                DefaultPath,
                "Import",
                path =>
                {
                    try
                    {
                        if (!File.Exists(path))
                        {
                            MessageCompat.Message("That Better Work Tab settings file does not exist.", MessageTypeDefOf.RejectInput, false);
                            return;
                        }

                        TryImport(settings, File.ReadAllText(path), afterImport);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Better Work Tab] Failed to import settings from {path}: {ex}");
                        MessageCompat.Message("Failed to import Better Work Tab settings. Check the log for details.", MessageTypeDefOf.RejectInput, false);
                    }
                }));
        }

        private static void TryImport(BetterWorkTabSettings settings, string json, Action afterImport)
        {
            if (BWTSettingsJsonService.TryImport(settings, json, out string report))
            {
                afterImport?.Invoke();
                MessageCompat.Message(report, MessageTypeDefOf.PositiveEvent, false);
                return;
            }

            MessageCompat.Message(report, MessageTypeDefOf.RejectInput, false);
        }

        private static void ConfirmImport(Action action)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Importing Better Work Tab settings will overwrite current configurable settings. Continue?",
                action,
                destructive: true,
                title: "Import Better Work Tab Settings"));
        }

        private static string DefaultPath => Path.Combine(Better_Work_Tab.GenFilePathsCompat.ConfigFolderPath, DefaultFileName);
    }
}
