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

        public Dialog_RenameWorkload(Worklist workload)
        {
            this.workload = workload;
            curName = workload.RenamableLabel;
            closeOnAccept = true;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            GUI.BeginGroup(rect);
            float curY = 0f;
            Widgets.Label(new Rect(0f, curY, rect.width, 35f), "Rename workload");
            curY += 35f;

            string tempName = curName;
            string text = Widgets.TextField(rect.AtZero(), workload.RenamableLabel);
            curName = tempName;
            curY += 45f;

            if (Widgets.ButtonText(new Rect((rect.width - 120f) / 2f, curY, 120f, 40f), "Accept"))
            {
                workload.RenamableLabel = curName;
                Close();
            }
            GUI.EndGroup();
        }
    }
}
