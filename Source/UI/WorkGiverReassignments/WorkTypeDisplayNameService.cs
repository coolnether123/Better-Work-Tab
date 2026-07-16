using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Central display-name policy for Work type labels. Custom labels are per-save
    /// and only affect UI text; WorkTypeDef.defName is never changed.
    /// </summary>
    internal static class WorkTypeDisplayNameService
    {
        internal static string HeaderLabel(WorkTypeDef def)
        {
            if (CustomLabelStore.TryGetWorkTypeLabel(def, out string customLabel))
            {
                return customLabel;
            }

            return DefaultHeaderLabel(def);
        }

        internal static string FullLabel(WorkTypeDef def)
        {
            if (CustomLabelStore.TryGetWorkTypeLabel(def, out string customLabel))
            {
                return customLabel;
            }

            if (def == null)
            {
                return "Work";
            }

            string label = def.LabelCap.ToString();
            if (!label.NullOrEmpty())
            {
                return label;
            }

            return DefaultHeaderLabel(def);
        }

        internal static string GerundLabel(WorkTypeDef def)
        {
            if (CustomLabelStore.TryGetWorkTypeLabel(def, out string customLabel))
            {
                return customLabel;
            }

            if (def == null)
            {
                return "Work";
            }

            string label = def.gerundLabel;
            if (!label.NullOrEmpty())
            {
                return label.CapitalizeFirst();
            }

            return FullLabel(def);
        }

        internal static string RenameDialogLabel(WorkTypeDef def)
        {
            return CustomLabelStore.TryGetWorkTypeLabel(def, out string customLabel)
                ? customLabel
                : DefaultHeaderLabel(def);
        }

        private static string DefaultHeaderLabel(WorkTypeDef def)
        {
            if (def == null)
            {
                return "Work";
            }

            string baseText = def.labelShort;
            if (baseText.NullOrEmpty()) baseText = def.label;
            if (baseText.NullOrEmpty()) baseText = def.defName;

            return (baseText.NullOrEmpty() ? "Work" : baseText).CapitalizeFirst();
        }
    }
}
