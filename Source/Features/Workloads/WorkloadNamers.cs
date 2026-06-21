using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Workloads
{
    /// <summary>
    /// Rename dialog compatible with the non-generic Dialog_Rename in RimWorld 1.4.
    /// </summary>
    public class Dialog_RenameWorklist : Dialog_Rename
    {
        private readonly Worklist worklist;

        public Dialog_RenameWorklist(Worklist worklist)
        {
            this.worklist = worklist;
            curName = worklist?.RenamableLabel ?? string.Empty;
        }

        protected override AcceptanceReport NameIsValid(string name)
        {
            var result = base.NameIsValid(name);
#if vAlpha4
            if (string.IsNullOrEmpty(name))
#else
            if (!result.Accepted)
#endif
            {
                return result;
            }

            var settings = Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (settings != null && settings.SavedWorklists.Any(wl => wl != null && wl != worklist && wl.RenamableLabel == name))
            {
                return "NameIsInUse".Translate();
            }

            return true;
        }

        protected override void SetName(string name)
        {
            if (worklist == null)
            {
                return;
            }

            worklist.RenamableLabel = name;
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
    }

    /// <summary>
    /// Dialog for naming a newly created worklist. Uses the same validation as the rename dialog.
    /// </summary>
    public class Dialog_NameNewWorklist : Dialog_RenameWorklist
    {
        public Dialog_NameNewWorklist(Worklist worklist) : base(worklist)
        {
        }

#if v0_13
        public override Vector2 InitialWindowSize => new Vector2(280f, 175f);
#else
        public override Vector2 InitialSize => new Vector2(280f, 175f);
#endif
    }
}
