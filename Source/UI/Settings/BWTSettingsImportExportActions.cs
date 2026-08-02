using System;
using System.IO;
using RimWorld;
using Better_Work_Tab.UI.SettingsFramework;
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
            try
            {
                GUIUtility.systemCopyBuffer = BWTSettingsJsonService.Export(settings);
                Messages.Message("All Better Work Tab settings data copied to clipboard.", MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Log.Error($"[Better Work Tab] Failed to export settings to the clipboard: {ex}");
                Messages.Message("Failed to export Better Work Tab settings. Check the log for details.", MessageTypeDefOf.RejectInput, false);
            }
        }

        private static void ShowExportPathDialog(BetterWorkTabSettings settings)
        {
            Find.WindowStack.Add(new Dialog_BWTSettingsJsonPath(
                "Export All Better Work Tab Settings Data",
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
                        Messages.Message($"All Better Work Tab settings data exported to {path}.", MessageTypeDefOf.PositiveEvent, false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Better Work Tab] Failed to export settings to {path}: {ex}");
                        Messages.Message("Failed to export Better Work Tab settings. Check the log for details.", MessageTypeDefOf.RejectInput, false);
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
                            Messages.Message("That Better Work Tab settings file does not exist.", MessageTypeDefOf.RejectInput, false);
                            return;
                        }

                        TryImport(settings, File.ReadAllText(path), afterImport);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Better Work Tab] Failed to import settings from {path}: {ex}");
                        Messages.Message("Failed to import Better Work Tab settings. Check the log for details.", MessageTypeDefOf.RejectInput, false);
                    }
                }));
        }

        private static void TryImport(BetterWorkTabSettings settings, string json, Action afterImport)
        {
            if (BWTSettingsJsonService.TryImport(settings, json, out string report))
            {
                afterImport?.Invoke();
                Messages.Message(report, MessageTypeDefOf.PositiveEvent, false);
                return;
            }

            Messages.Message(report, MessageTypeDefOf.RejectInput, false);
        }

        private static void ConfirmImport(Action action)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Importing will overwrite all Better Work Tab settings data, including rulesets, layout state, viewed-setting history, and recent colors. Continue?",
                action,
                destructive: true,
                title: "Import Better Work Tab Settings"));
        }

        private static string DefaultPath => Path.Combine(GenFilePaths.ConfigFolderPath, DefaultFileName);
    }
}
