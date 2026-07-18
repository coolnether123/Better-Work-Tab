using System;
using Better_Work_Tab.Features.Testing;
using Better_Work_Tab.Features.Tutorial;
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
        private static readonly MethodInfo GetLabelMethod =
            AccessTools.Method(typeof(PawnColumnWorker_Label), "GetLabel");
        private static readonly Func<PawnColumnWorker_Label, Pawn, TaggedString> GetLabel =
            AccessTools.MethodDelegate<Func<PawnColumnWorker_Label, Pawn, TaggedString>>(GetLabelMethod);

        // NOTE: Prefix and Transpiler are mutually exclusive execution paths:
        // - If contrast mode (Prefix returns false): DoCell_Contrast gates close directly.
        // - If vanilla mode (Prefix returns true): Transpiler intercepts EscapeCurrentTab.
        // Both honor disableLeftClickClose via ShouldCloseWorkTab().
        // Postfix ensures overlays draw after vanilla rendering when Prefix returns true (e.g., no contrast mode)
        public static void Postfix(PawnColumnWorker_Label __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            if (pawn == null || !__instance.def.showIcon)
                return;

            Rect rect1 = new Rect(
                rect.x,
                rect.y,
                rect.width,
                Mathf.Min(
                    rect.height,
                    __instance.def.groupable ? rect.height : __instance.GetMinCellHeight(pawn)));

            Rect iconRect = new Rect(rect1.x, rect1.y, rect1.height, rect1.height);
            WorkTabGeometryDiagnostics.RecordPawnLabelIconRect(pawn, iconRect);
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
                    worker.def.groupable ? rect.height : worker.GetMinCellHeight(pawn)));

            Rect rect2 = rect1;
            rect2.xMin += 3f;

            if (worker.def.showIcon)
            {
                rect2.xMin += rect1.height;
                Rect iconRect = new Rect(rect1.x, rect1.y, rect1.height, rect1.height);

                if (Find.Selector.IsSelected(pawn))
                    SelectionDrawerUtility.DrawSelectionOverlayWholeGUI(iconRect.ContractedBy(2f));

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
                    GenMapUI.OverlayHealthTex,
                    BaseContent.ClearTex,
                    doBorder: false);
            }

            if (!BWTWorkTabTutorial.OwnsCurrentPointer && Mouse.IsOver(rect1))
                GUI.DrawTexture(rect1, TexUI.HighlightTex);

            TaggedString vanillaLabel = GetLabel(worker, pawn);

            string finalLabel = vanillaLabel.Resolve().StripTags();

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
                CameraJumper.TryJumpAndSelect(pawn);
                if (Current.ProgramState == ProgramState.Playing && Event.current.button == 0)
                {
                    // Keep the Work tab open when the user opts into the setting; otherwise mimic vanilla.
                    if (ShouldCloseWorkTab())
                    {
                        Find.MainTabsRoot.EscapeCurrentTab(false);
                    }
                }
            }
            else if (!BWTWorkTabTutorial.OwnsCurrentPointer && Mouse.IsOver(rect1))
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
