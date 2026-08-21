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
                ExportLabel = "BWT_Settings_ImportExport_Export".Translate(),
                ImportLabel = "BWT_Settings_ImportExport_Import".Translate(),
                FileLabel = "BWT_Settings_ImportExport_File".Translate(),
                ClipboardLabel = "BWT_Settings_ImportExport_Clipboard".Translate(),
                CancelLabel = "Cancel".Translate(),
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
                Messages.Message("BWT_Settings_ImportExport_Copied".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Log.Error($"[Better Work Tab] Failed to export settings to the clipboard: {ex}");
                Messages.Message("BWT_Settings_ImportExport_ExportFailed".Translate(), MessageTypeDefOf.RejectInput, false);
            }
        }

        private static void ShowExportPathDialog(BetterWorkTabSettings settings)
        {
            Find.WindowStack.Add(new Dialog_BWTSettingsJsonPath(
                "BWT_Settings_ImportExport_ExportTitle".Translate(),
                DefaultPath,
                "BWT_Settings_ImportExport_Export".Translate(),
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
                        Messages.Message("BWT_Settings_ImportExport_Exported".Translate(path), MessageTypeDefOf.PositiveEvent, false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Better Work Tab] Failed to export settings to {path}: {ex}");
                        Messages.Message("BWT_Settings_ImportExport_ExportFailed".Translate(), MessageTypeDefOf.RejectInput, false);
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
                "BWT_Settings_ImportExport_ImportTitle".Translate(),
                DefaultPath,
                "BWT_Settings_ImportExport_Import".Translate(),
                path =>
                {
                    try
                    {
                        if (!File.Exists(path))
                        {
                            Messages.Message("BWT_Settings_ImportExport_FileMissing".Translate(), MessageTypeDefOf.RejectInput, false);
                            return;
                        }

                        TryImport(settings, File.ReadAllText(path), afterImport);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Better Work Tab] Failed to import settings from {path}: {ex}");
                        Messages.Message("BWT_Settings_ImportExport_ImportFailed".Translate(), MessageTypeDefOf.RejectInput, false);
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
                "BWT_Settings_ImportExport_ConfirmImport".Translate(),
                action,
                destructive: true,
                title: "BWT_Settings_ImportExport_ImportTitle".Translate()));
        }

        private static string DefaultPath => Path.Combine(GenFilePaths.ConfigFolderPath, DefaultFileName);
    }
}
