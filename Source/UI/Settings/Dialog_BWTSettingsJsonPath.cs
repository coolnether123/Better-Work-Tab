using System;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Small cross-platform path entry dialog for BWT settings JSON import/export.
    /// </summary>
    public class Dialog_BWTSettingsJsonPath : Window
    {
        private readonly string _title;
        private readonly string _acceptLabel;
        private readonly Action<string> _onAccept;
        private string _path;

        public Dialog_BWTSettingsJsonPath(
            string title,
            string initialPath,
            string acceptLabel,
            Action<string> onAccept)
        {
            _title = title;
            _path = initialPath ?? string.Empty;
            _acceptLabel = acceptLabel;
            _onAccept = onAccept;
            forcePause = true;
            absorbInputAroundWindow = true;
#if !(v0_18 || v0_17 || v0_16)
            closeOnCancel = true;
            closeOnAccept = false;
#endif
        }

#if v0_13 || vAlpha4
        public override Vector2 InitialWindowSize => new Vector2(760f, 220f);
#else
        public override Vector2 InitialSize => new Vector2(760f, 220f);
#endif

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), _title);
            Text.Font = GameFont.Small;

            Rect labelRect = new Rect(0f, 46f, inRect.width, 24f);
            Widgets.Label(labelRect, "JSON file path");

            Rect pathRect = new Rect(0f, 72f, inRect.width, 32f);
            _path = Widgets.TextField(pathRect, _path ?? string.Empty);

            Rect hintRect = new Rect(0f, 108f, inRect.width, 28f);
            GUI.color = Color.gray;
            Widgets.Label(hintRect, "Default folder: " + Better_Work_Tab.GenFilePathsCompat.ConfigFolderPath);
            GUI.color = Color.white;

            float buttonWidth = 140f;
            Rect cancelRect = new Rect(0f, inRect.height - 38f, buttonWidth, 35f);
            Rect acceptRect = new Rect(inRect.width - buttonWidth, inRect.height - 38f, buttonWidth, 35f);
            Rect openFolderRect = new Rect(cancelRect.xMax + 8f, inRect.height - 38f, buttonWidth, 35f);

            if (Better_Work_Tab.WidgetsCompat.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }

            if (Better_Work_Tab.WidgetsCompat.ButtonText(openFolderRect, "Open Folder"))
            {
                Application.OpenURL(Better_Work_Tab.GenFilePathsCompat.ConfigFolderPath);
            }

            if (Better_Work_Tab.WidgetsCompat.ButtonText(acceptRect, _acceptLabel))
            {
                string resolved = ResolvePath(_path);
                if (IsNullOrWhiteSpace(resolved))
                {
                    MessageCompat.Message("Enter a valid JSON file path.", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                _onAccept?.Invoke(resolved);
                Close();
            }
        }

        private static string ResolvePath(string path)
        {
            if (IsNullOrWhiteSpace(path))
            {
                return null;
            }

            path = path.Trim().Trim('"');
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Better_Work_Tab.GenFilePathsCompat.ConfigFolderPath, path);
            }

            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                path += ".json";
            }

            return Path.GetFullPath(path);
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
        }
    }
}
