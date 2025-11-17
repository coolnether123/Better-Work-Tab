using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.PawnOrganizer.API;
using Spine.UI;               // for TextColorHelper
using System.Collections.Generic;

namespace Better_Work_Tab.Patches
{
    [HarmonyPatch(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell))]
    public static class Patch_PawnColumnWorker_Label_DoCell
    {
        // Mirror vanilla statics for the truncation cache:
        private static readonly Dictionary<string, TaggedString> labelCache = new Dictionary<string, TaggedString>();
        private static float labelCacheForWidth = -1f;
        private const int LeftMargin = 3;

        public static bool Prefix(
            PawnColumnWorker_Label __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            // If no game/pawn or no custom background set, let vanilla run unmodified:
            if (Current.Game == null
                || pawn == null
                || !PawnColorDatabase.TryGetColor(pawn, out var bg)
                || bg.a <= 0f)
            {
                return true; // run vanilla
            }

            // Otherwise, draw our copy with contrast‐aware text
            DoCell_Contrast(__instance, rect, pawn, table, bg);
            return false;    // skip vanilla
        }


        private static void DoCell_Contrast(
    PawnColumnWorker_Label worker,
    Rect rect,
    Pawn pawn,
    PawnTable table,
    Color backgroundColor)
        {
            // 1) Compute our drawing rects (exact vanilla)
            Rect rect1 = new Rect(
                rect.x,
                rect.y,
                rect.width,
                Mathf.Min(rect.height,
                    worker.def.groupable
                        ? rect.height
                        : worker.GetMinCellHeight(pawn))
            );

            Rect rect2 = rect1;
            rect2.xMin += 3f;

            // 2) Icon & selection overlay (exact vanilla)
            if (worker.def.showIcon)
            {
                rect2.xMin += rect1.height;
                Rect iconRect = new Rect(rect1.x, rect1.y, rect1.height, rect1.height);

                if (Find.Selector.IsSelected(pawn))
                    SelectionDrawerUtility.DrawSelectionOverlayWholeGUI(iconRect.ContractedBy(2f));

                Widgets.ThingIcon(iconRect, pawn);
            }

            // 3) Health bar (exact vanilla)
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

            // 4) Mouse-over highlight (exact vanilla)
            if (Mouse.IsOver(rect1))
                GUI.DrawTexture(rect1, TexUI.HighlightTex);

            // ===================================================================
            // 5) GET EXACT VANILLA LABEL TEXT (Name + Title)
            // ===================================================================
            // Call vanilla's exact GetLabel() to get precise text - e.g., "Morrison, Colonist" or "Lewwis, Bodyguard"
            var getLabelMI = AccessTools.Method(typeof(PawnColumnWorker_Label), "GetLabel");
            TaggedString vanillaLabel = (TaggedString)getLabelMI.Invoke(worker, new object[] { pawn });

            Log.Message($"[BWT DEBUG] Pawn '{pawn.LabelShort}' vanilla label returned: '{vanillaLabel}' (KindLabel: '{pawn.KindLabel}')");

            // REMOVE all vanilla markup (especially <color=#999...>)
            string finalLabel = vanillaLabel.Resolve().StripTags();

            // Truncate if needed
            if (Mathf.Abs(rect2.width - labelCacheForWidth) > 0.001f)
            {
                labelCacheForWidth = rect2.width;
                labelCache.Clear();
            }
            if (Text.CalcSize(finalLabel).x > rect2.width)
                finalLabel = finalLabel.Truncate(rect2.width);

            // ===================================================================
            // 7) SINGLE CONTRAST COLOR FOR ENTIRE LABEL (Name + Title)
            // ===================================================================
            Color textCol = TextColorHelper.GetContrastingTextColor(
                backgroundColor,
                darkTextColor: Color.black,
                lightTextColor: Color.white);

            // 8) Draw with uniform contrast color
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldWrap = Text.WordWrap;
            var oldGuiColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;  // Fixed: Always MiddleLeft (vanilla default)
                Text.WordWrap = false;
                GUI.color = textCol;  // ONE color for "(Name), (Title)"

                Widgets.Label(rect2, finalLabel);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldGuiColor;
            }

            // 9) Invisible button + tooltip (exact vanilla)
            if (Widgets.ButtonInvisible(rect1))
            {
                CameraJumper.TryJumpAndSelect(pawn);
                if (Current.ProgramState == ProgramState.Playing && Event.current.button == 0)
                    Find.MainTabsRoot.EscapeCurrentTab(false);
            }
            else if (Mouse.IsOver(rect1))
            {
                TipSignal tooltip = pawn.GetTooltip();
                tooltip.text = "ClickToJumpTo".Translate() + "\n\n" + tooltip.text;
                TooltipHandler.TipRegion(rect1, tooltip);
            }
        }
    }
}