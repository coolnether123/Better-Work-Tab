using RimWorld;
using UnityEngine;
using Verse;

using Better_Work_Tab.Features.Workloads;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Dialog window for naming a new Worklist.
    /// Displays a text entry field for the new label, sets on accept, and closes on escape/outside click.
    /// </summary>
    public class Dialog_NameNewWorklist : Window
    {
        private Worklist workload;
        private string curName = "";

        public Dialog_NameNewWorklist(Worklist workload)
        {
            this.workload = workload;
            closeOnAccept = true;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            GUI.BeginGroup(rect);
            float curY = 0f;
            Widgets.Label(new Rect(0f, curY, rect.width, 35f), "Name new workload");
            curY += 35f;

            string tempName = curName;
            Widgets.TextEntry(new Rect(0f, curY, rect.width, 35f), ref tempName, 50);
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
