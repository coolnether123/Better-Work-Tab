using System;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Button labels and callbacks used by the settings drawer import/export footer.
    /// </summary>
    public class SettingsImportExportActions
    {
        public string ExportLabel = "Export";
        public string ImportLabel = "Import";
        public string FileLabel = "File";
        public string ClipboardLabel = "Clipboard";
        public string CancelLabel = "Cancel";

        public Action ExportToFile;
        public Action ExportToClipboard;
        public Action ImportFromFile;
        public Action ImportFromClipboard;

        public bool HasAnyAction =>
            ExportToFile != null ||
            ExportToClipboard != null ||
            ImportFromFile != null ||
            ImportFromClipboard != null;
    }
}
