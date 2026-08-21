using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;


namespace Better_Work_Tab.Features.Workloads
{
    public class Dialog_RenameWorklist : Dialog_Rename<Worklist>
    {
        public Dialog_RenameWorklist(Worklist renameable) : base(renameable)
        {
        }
    }

    public class Dialog_NameNewWorklist : Dialog_Rename<Worklist>
    {

        public Dialog_NameNewWorklist(Worklist renameable) : base(renameable)
        {
        }
        protected override AcceptanceReport NameIsValid(string name)
        {
            AcceptanceReport result = base.NameIsValid(name);
            if (!result.Accepted)
            {
                return result;
            }
            if (name != renaming.RenamableLabel && Current.Game.GetComponent<GameComponent_BWTWorldSettings>().SavedWorklists.Where(wl => name == wl.RenamableLabel).Any())
            {
                return "NameIsInUse".Translate();
            }
            return true;
        }


        private bool focusedRenameField;

        private int startAcceptingInputAtFrame;


        private bool AcceptsInput
        {
            get
            {
                return startAcceptingInputAtFrame <= Time.frameCount;
            }
        }

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(280f, 175f);
            }
        }

        //Absolute copy/paste of vanilla Dialog_Rename (Except for "Name New Worktype")
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            bool flag = false;
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                flag = true;
                Event.current.Use();
            }
            Rect rect = new Rect(inRect);
            Text.Font = GameFont.Medium;
            rect.height = Text.LineHeight + 10f;
            Widgets.Label(rect, "BWT_Dialog_Workload_NameTitle".Translate());
            Text.Font = GameFont.Small;
            GUI.SetNextControlName("RenameField");
            string text = Widgets.TextField(new Rect(0f, rect.height, inRect.width, 35f), curName);
            if (AcceptsInput && text.Length < MaxNameLength)
            {
                curName = text;
            }
            else if (!AcceptsInput)
            {
                ((TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl)).SelectAll();
            }
            if (!focusedRenameField)
            {

                Verse.UI.FocusControl("RenameField", this);
                focusedRenameField = true;
            }
            if (!(Widgets.ButtonText(new Rect(15f, inRect.height - 35f - 10f, inRect.width - 15f - 15f, 35f), "OK".Translate()) || flag))
            {
                return;
            }
            AcceptanceReport acceptanceReport = NameIsValid(curName);
            if (!acceptanceReport.Accepted)
            {
                if (acceptanceReport.Reason.NullOrEmpty())
                {
                    Messages.Message("NameIsInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    Messages.Message(acceptanceReport.Reason, MessageTypeDefOf.RejectInput, false);
                }
                return;
            }
            if (renaming != null)
            {
                renaming.RenamableLabel = curName;
            }
            OnRenamed(curName);
            Find.WindowStack.TryRemove(this);
        }

    }
}
