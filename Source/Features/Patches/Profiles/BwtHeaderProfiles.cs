using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Better_Work_Tab;
using Better_Work_Tab.UI.Headers;

namespace Better_Work_Tab.Features.Patches.Profiles
{
    internal sealed class BwtHeaderProfile
    {
        internal BwtHeaderProfile(
            string id, BwtTargetIdentity target, MethodInfo highlightCall,
            MethodInfo isWorkTabCall, FieldInfo settingsField, FieldInfo angledHeadersField,
            Type workPriorityWorkerType)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("A profile ID is required.", "id");
            if (target == null) throw new ArgumentNullException("target");
            Id = id.Trim();
            Target = target;
            HighlightCall = highlightCall;
            IsWorkTabCall = isWorkTabCall;
            SettingsField = settingsField;
            AngledHeadersField = angledHeadersField;
            WorkPriorityWorkerType = workPriorityWorkerType;
        }

        internal string Id { get; private set; }
        internal BwtTargetIdentity Target { get; private set; }
        internal MethodInfo HighlightCall { get; private set; }
        internal MethodInfo IsWorkTabCall { get; private set; }
        internal FieldInfo SettingsField { get; private set; }
        internal FieldInfo AngledHeadersField { get; private set; }
        internal Type WorkPriorityWorkerType { get; private set; }

        internal string Validate()
        {
            if (HighlightCall == null || IsWorkTabCall == null || SettingsField == null ||
                AngledHeadersField == null || WorkPriorityWorkerType == null)
                return "a header binding is missing";
            return null;
        }
    }

    internal static class BwtHeaderProfiles
    {
        internal static readonly BwtHeaderProfile DisableHighlight = CreateDisableHighlight();

        private static BwtHeaderProfile CreateDisableHighlight()
        {
            MethodInfo target = AccessTools.Method(
                typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader),
                new[] { typeof(UnityEngine.Rect), typeof(PawnTable) });
            return new BwtHeaderProfile(
                "BWT.PawnColumnWorker.DoHeader.DisableHighlight",
                BwtTargetIdentity.ForMethod(target, BwtBuildIdentity.RimWorld16),
                AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawHighlight), new[] { typeof(UnityEngine.Rect) }),
                AccessTools.Method(
                    typeof(PawnColumnWorker_WorkPriority_DoHeader_Patch),
                    nameof(PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab)),
                AccessTools.Field(typeof(BetterWorkTabMod), nameof(BetterWorkTabMod.Settings)),
                AccessTools.Field(
                    typeof(BetterWorkTabSettings), nameof(BetterWorkTabSettings.enableAngledHeaders)),
                typeof(PawnColumnWorker_WorkPriority));
        }
    }
}
