using RimWorld;
using UnityEngine;
using Verse;

using Better_Work_Tab.Features.Workloads;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Dialog window for renaming an existing Worklist.
    /// Displays a text entry field pre-filled with the current label, updates on accept.
    /// </summary>
    public class Dialog_RenameWorkload : Window
    {
        private Worklist workload;
        private string curName = "";

        public override Vector2 InitialSize => new Vector2(300f, 140f);

        public Dialog_RenameWorkload(Worklist workload)
        {
            this.workload = workload;
            curName = workload?.RenamableLabel ?? "Workload";
#if !(v0_18 || v0_17 || v0_16)
            closeOnAccept = true;
#endif
            closeOnClickedOutside = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Small;

            // Label
            Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "Rename workload");

            // Text input field
            curName = Widgets.TextField(new Rect(0f, 35f, rect.width, 35f), curName);

            // Buttons
            float buttonY = rect.height - 30f;
            Rect acceptRect = new Rect(0f, buttonY, (rect.width - 10f) / 2f, 35f);
            Rect cancelRect = new Rect(acceptRect.xMax + 10f, buttonY, (rect.width - 10f) / 2f, 35f);

            if (Widgets.ButtonText(acceptRect, "Accept"))
            {
                if (ApplyRename())
                {
                    Close();
                }
            }

            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }
        }

        private bool ApplyRename()
        {
            if (workload == null)
            {
                return false;
            }

            string trimmed = (curName ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                MessageCompat.Message("NameCannotBeEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            workload.RenamableLabel = trimmed;
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            BetterWorkTabMod.Settings?.Write();

            return true;
        }
    }
}
