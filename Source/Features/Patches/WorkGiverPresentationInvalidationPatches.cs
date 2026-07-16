using Better_Work_Tab.UI.WorkGiverReassignments;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Patches
{
#if !v1_0 && !v0_19
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Notify_DisabledWorkTypesChanged))]
    internal static class Patch_Pawn_NotifyDisabledWorkTypesChanged_Presentation
    {
        private static void Postfix(Pawn __instance)
        {
            WorkGiverPresentationInvalidation.NotifyPawnDynamicStateChanged(__instance);
        }
    }
#endif

    [HarmonyPatch(typeof(PawnCapacitiesHandler), MethodType.Constructor, new[] { typeof(Pawn) })]
    internal static class Patch_PawnCapacitiesHandler_Constructor_Presentation
    {
        private static void Postfix(PawnCapacitiesHandler __instance, Pawn pawn)
        {
            WorkGiverPresentationInvalidation.RegisterCapacityOwner(__instance, pawn);
        }
    }

    [HarmonyPatch(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.Notify_CapacityLevelsDirty))]
    internal static class Patch_PawnCapacitiesHandler_NotifyDirty_Presentation
    {
        private static void Postfix(PawnCapacitiesHandler __instance)
        {
            WorkGiverPresentationInvalidation.NotifyCapacityStateChanged(__instance);
        }
    }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
    [HarmonyPatch(typeof(Pawn_IdeoTracker), MethodType.Constructor, new[] { typeof(Pawn) })]
    internal static class Patch_PawnIdeoTracker_Constructor_Presentation
    {
        private static void Postfix(Pawn_IdeoTracker __instance, Pawn pawn)
        {
            WorkGiverPresentationInvalidation.RegisterIdeologyOwner(__instance, pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
    internal static class Patch_PawnIdeoTracker_SetIdeo_Presentation
    {
        private static void Postfix(Pawn_IdeoTracker __instance)
        {
            WorkGiverPresentationInvalidation.NotifyIdeologyChanged(__instance);
        }
    }
#endif

    [HarmonyPatch(
        typeof(SkillRecord),
        nameof(SkillRecord.Learn),
        new[] { typeof(float), typeof(bool), typeof(bool) })]
    internal static class Patch_SkillRecord_Learn_Presentation
    {
        private readonly struct DisplayedSkillState
        {
            internal DisplayedSkillState(int level, Passion passion)
            {
                Level = level;
                Passion = passion;
            }

            internal int Level { get; }
            internal Passion Passion { get; }
        }

        private static void Prefix(SkillRecord __instance, out DisplayedSkillState __state)
        {
            __state = new DisplayedSkillState(__instance.levelInt, __instance.passion);
        }

        private static void Postfix(SkillRecord __instance, DisplayedSkillState __state)
        {
            if (__instance.levelInt == __state.Level && __instance.passion == __state.Passion)
            {
                return;
            }

            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.InvalidateCategory(
                UI.WorkGrid.Invalidation.WorkGridInvalidationCategory.CapabilitySkill);
        }
    }
}
