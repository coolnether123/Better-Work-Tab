using System;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Optional callbacks for a settings page's import/export footer.
    /// </summary>
    public sealed class SettingsImportExportActions
    {
        public string ExportLabel { get; set; } = "Export";
        public string ImportLabel { get; set; } = "Import";
        public string FileLabel { get; set; } = "File";
        public string ClipboardLabel { get; set; } = "Clipboard";
        public string CancelLabel { get; set; } = "Cancel";

        public Action ExportToFile { get; set; }
        public Action ExportToClipboard { get; set; }
        public Action ImportFromFile { get; set; }
        public Action ImportFromClipboard { get; set; }

        internal bool HasAnyAction =>
            ExportToFile != null ||
            ExportToClipboard != null ||
            ImportFromFile != null ||
            ImportFromClipboard != null;
    }
}
