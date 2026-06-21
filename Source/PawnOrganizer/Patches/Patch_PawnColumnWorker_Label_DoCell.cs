using System;
using Better_Work_Tab.PawnOrganizer.API;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Spine.UI; // for TextColorHelper
using UnityEngine;
using Verse;
using Better_Work_Tab.ModSupport;
using System.Reflection.Emit;
using System.Reflection;

namespace Better_Work_Tab.Patches
{
    [HarmonyPatch(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell))]
    public static class Patch_PawnColumnWorker_Label_DoCell
    {
        // NOTE: Prefix and Transpiler are mutually exclusive execution paths:
        // - If contrast mode (Prefix returns false): DoCell_Contrast gates close directly.
        // - If vanilla mode (Prefix returns true): Transpiler intercepts EscapeCurrentTab.
        // Both honor disableLeftClickClose via ShouldCloseWorkTab().
        // Postfix ensures overlays draw after vanilla rendering when Prefix returns true (e.g., no contrast mode)
        public static void Postfix(PawnColumnWorker_Label __instance, Rect rect, Pawn pawn, PawnTable table)
        {
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
            if (pawn == null)
#else
            if (pawn == null || !__instance.def.showIcon)
#endif
                return;

            Rect rect1 = new Rect(
                rect.x,
                rect.y,
                rect.width,
                Mathf.Min(
                    rect.height,
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
                    __instance.GetMinCellHeight(pawn)));
#else
                    __instance.def.groupable ? rect.height : __instance.GetMinCellHeight(pawn)));
#endif

            Rect iconRect = new Rect(rect1.x, rect1.y, rect1.height, rect1.height);
            ModSupportManager.OnPawnRowDrawn(pawn, iconRect);
        }

        public static bool Prefix(
            PawnColumnWorker_Label __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            if (pawn == null)
            {
                return true;
            }

            if (Current.Game == null
                || !PawnColorDatabase.TryGetColor(pawn, out var bg)
                || bg.a <= 0f)
            {
                return true;
            }

            DoCell_Contrast(__instance, rect, pawn, table, bg);
            return false;
        }

        private static void DoCell_Contrast(
            PawnColumnWorker_Label worker,
            Rect rect,
            Pawn pawn,
            PawnTable table,
            Color backgroundColor)
        {
            Rect rect1 = new Rect(
                rect.x,
                rect.y,
                rect.width,
                Mathf.Min(
                    rect.height,
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
                    worker.GetMinCellHeight(pawn)));
#else
                    worker.def.groupable ? rect.height : worker.GetMinCellHeight(pawn)));
#endif

            Rect rect2 = rect1;
            rect2.xMin += 3f;

#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
            if (true) // In 1.3 we always show icon for Label column? Or check worker type.
#else
            if (worker.def.showIcon)
#endif
            {
                rect2.xMin += rect1.height;
                Rect iconRect = new Rect(rect1.x, rect1.y, rect1.height, rect1.height);

#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
                if (Find.Selector.IsSelected(pawn))
                    SelectionDrawerUtility.DrawSelectionOverlayOnGUI(pawn, iconRect.ContractedBy(2f), 1f, 1f);
#endif

                Widgets.ThingIcon(iconRect, pawn);
                ModSupportManager.OnPawnRowDrawn(pawn, iconRect);
            }

            if (pawn.health.summaryHealth.SummaryHealthPercent < 0.99f)
            {
                Rect barRect = new Rect(rect2.x - 3f, rect2.y, rect2.width + 3f, rect2.height);
                barRect.yMin += 4f;
                barRect.yMax -= 6f;
                Widgets.FillableBar(
                    barRect,
                    pawn.health.summaryHealth.SummaryHealthPercent,
#if v0_15
                    GenWorldUI.OverlayHealthTex,
#else
                    GenMapUI.OverlayHealthTex,
#endif
                    BaseContent.ClearTex,
                    doBorder: false);
            }

            if (Mouse.IsOver(rect1))
                GUI.DrawTexture(rect1, TexUI.HighlightTex);

            string finalLabel = BuildVanillaLabel(worker, pawn);

            Color textCol = TextColorHelper.GetContrastingTextColor(
                backgroundColor,
                darkTextColor: Color.black,
                lightTextColor: Color.white);

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                using (new GUIColorScope(textCol))
                {
                    Widgets.Label(rect2, finalLabel);
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
            }

            if (Widgets.ButtonInvisible(rect1))
            {
#if v0_16
                JumpToTargetUtility.TryJumpAndSelect(pawn);
#else
                CameraJumper.TryJumpAndSelect(pawn);
#endif
                if (Current.ProgramState ==
#if v0_15
                    ProgramState.MapPlaying
#else
                    ProgramState.Playing
#endif
                    && Event.current.button == 0)
                {
                    // Keep the Work tab open when the user opts into the setting; otherwise mimic vanilla.
                    if (ShouldCloseWorkTab())
                    {
                        Find.MainTabsRoot.EscapeCurrentTab(false);
                    }
                }
            }
            else if (Mouse.IsOver(rect1))
            {
                TipSignal tooltip = pawn.GetTooltip();
                tooltip.text = "ClickToJumpTo".Translate() + "\n\n" + tooltip.text;
                TooltipHandler.TipRegion(rect1, tooltip);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var escape = AccessTools.Method(typeof(MainTabsRoot), nameof(MainTabsRoot.EscapeCurrentTab), new[] { typeof(bool) });
            var replacement = AccessTools.Method(typeof(Patch_PawnColumnWorker_Label_DoCell), nameof(MaybeCloseWorkTab));

            foreach (var inst in instructions)
            {
                if (inst.Calls(escape))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else
                {
                    yield return inst;
                }
            }
        }

        private static void MaybeCloseWorkTab(MainTabsRoot root, bool playSound)
        {
            // Transpiler covers the vanilla draw path; Prefix handles the contrast path.
            if (ShouldCloseWorkTab())
            {
                root?.EscapeCurrentTab(playSound);
            }
        }

        /// <summary>
        /// Rebuilds the vanilla label for a pawn row (copied from RW 1.4 PawnColumnWorker_Label.DoCell).
        /// </summary>
        private static string BuildVanillaLabel(PawnColumnWorker_Label worker, Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            string label;
            if (pawn.RaceProps.Humanlike || pawn.RaceProps.Animal || pawn.Name == null || pawn.Name.Numerical)
            {
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
                label = PawnCompat.LabelShortCap(pawn);
#else
                label = worker.def.useLabelShort ? PawnCompat.LabelShortCap(pawn) : pawn.LabelNoCount.CapitalizeFirst();
#endif
            }
            else
            {
#if v1_2 || v1_1 || (v1_0 || v0_19)
                label = pawn.Name.ToStringShort.CapitalizeFirst() + ", " + pawn.KindLabel.Colorize(ColoredTextCompat.SubtleGrayColor);
#else
                label = pawn.Name.ToStringShort.CapitalizeFirst() + ", " + pawn.KindLabel.Colorize(ColoredText.SubtleGrayColor);
#endif
            }

#if v1_2 || v1_1 || (v1_0 || v0_19)
            // IsSlave doesn't exist in 1.2, skip this coloring
            if (false)
#elif v1_3
            if (pawn.IsSlave)
#else
            if (pawn.IsSlave || pawn.IsColonyMech)
#endif
            {
                label = label.Colorize(PawnNameColorUtility.PawnNameColorOf(pawn));
            }

            return label.StripTags();
        }

        private static bool ShouldCloseWorkTab() =>
            !(BetterWorkTabMod.Settings?.disableLeftClickClose ?? false);

        private readonly struct GUIColorScope : IDisposable
        {
            private readonly Color _previous;

            public GUIColorScope(Color color)
            {
                _previous = GUI.color;
                GUI.color = color;
            }

            public void Dispose()
            {
                GUI.color = _previous;
            }
        }
    }
}
