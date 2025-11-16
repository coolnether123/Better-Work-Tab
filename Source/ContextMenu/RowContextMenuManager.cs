using Better_Work_Tab.Input;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using Spine.UI.ColourPicker;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ContextMenu
{
    /// <summary>
    /// Enhanced right-click context menu for pawn rows.
    /// Single responsibility: Build and display row context menus.
    /// </summary>
    public class RowContextMenuManager
    {
        public bool ShowMenu(InputState input, WorkTabLayoutRow row, IWorkTabLayoutController layout)
        {
            if (row.Pawn == null) return false;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // === DIVIDER MANAGEMENT ===
            options.Add(new FloatMenuOption("Insert divider above", () =>
            {
                InsertDividerAbove(row.Pawn, layout);
            }));

            options.Add(new FloatMenuOption("Insert divider below", () =>
            {
                InsertDividerBelow(row.Pawn, layout);
            }));

            // === COLOR CUSTOMIZATION ===
            options.Add(new FloatMenuOption("Set background color...", () =>
            {
                ShowColorPicker(row.Pawn, isBackground: true);
            }));

            options.Add(new FloatMenuOption("Set text color...", () =>
            {
                ShowColorPicker(row.Pawn, isBackground: false);
            }));

            if (PawnOrganizer.API.PawnColorDatabase.TryGetColor(row.Pawn, out _))
            {
                options.Add(new FloatMenuOption("Clear custom colors", () =>
                {
                    PawnOrganizer.API.PawnColorDatabase.ClearColor(row.Pawn);
                    ClearTextColor(row.Pawn);
                }));
            }

            // === WORK PRIORITY MANAGEMENT ===
            options.Add(new FloatMenuOption("Copy work priorities", () =>
            {
                CopyPriorities(row.Pawn);
            }));

            if (HasClipboardData())
            {
                options.Add(new FloatMenuOption("Paste work priorities", () =>
                {
                    PastePriorities(row.Pawn);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
            return true;
        }

        private void InsertDividerAbove(Pawn pawn, IWorkTabLayoutController layout)
        {
            if (pawn == null || layout == null) return;

            // Find pawn's current index
            int pawnIndex = -1;
            for (int i = 0; i < layout.Rows.Count; i++)
            {
                if (layout.Rows[i].Pawn == pawn)
                {
                    pawnIndex = i;
                    break;
                }
            }

            if (pawnIndex < 0) return;

            // Calculate appropriate displayOrder
            int newOrder = pawn.playerSettings?.displayOrder ?? 0;
            if (pawnIndex > 0)
            {
                var prevRow = layout.Rows[pawnIndex - 1];
                int prevOrder = prevRow.IsDivider 
                    ? prevRow.Divider.DisplayOrder 
                    : prevRow.Pawn.playerSettings?.displayOrder ?? 0;
                newOrder = (prevOrder + newOrder) / 2;
            }
            else
            {
                newOrder -= 100; // Place before first pawn
            }

            var worklist = Current.Game.GetComponent<Features.Workloads.GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            if (worklist == null) return;

            var divider = new PawnDivider
            {
                DividerName = "New Divider",
                DisplayOrder = newOrder
            };

            worklist.Dividers.Add(divider);
            layout.Rebuild(layout.Table, worklist, layout.TableOrigin);

            VisualFeedback.AudioManager.PlayClick();
        }

        private void InsertDividerBelow(Pawn pawn, IWorkTabLayoutController layout)
        {
            if (pawn == null || layout == null) return;

            // Find pawn's current index
            int pawnIndex = -1;
            for (int i = 0; i < layout.Rows.Count; i++)
            {
                if (layout.Rows[i].Pawn == pawn)
                {
                    pawnIndex = i;
                    break;
                }
            }

            if (pawnIndex < 0) return;

            // Calculate appropriate displayOrder
            int newOrder = pawn.playerSettings?.displayOrder ?? 0;
            if (pawnIndex < layout.Rows.Count - 1)
            {
                var nextRow = layout.Rows[pawnIndex + 1];
                int nextOrder = nextRow.IsDivider 
                    ? nextRow.Divider.DisplayOrder 
                    : nextRow.Pawn.playerSettings?.displayOrder ?? 0;
                newOrder = (newOrder + nextOrder) / 2;
            }
            else
            {
                newOrder += 100; // Place after last pawn
            }

            var worklist = Current.Game.GetComponent<Features.Workloads.GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            if (worklist == null) return;

            var divider = new PawnDivider
            {
                DividerName = "New Divider",
                DisplayOrder = newOrder
            };

            worklist.Dividers.Add(divider);
            layout.Rebuild(layout.Table, worklist, layout.TableOrigin);

            VisualFeedback.AudioManager.PlayClick();
        }

        private void ShowColorPicker(Pawn pawn, bool isBackground)
        {
            Color currentColor = isBackground 
                ? (PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out Color bgColor) 
                    ? bgColor 
                    : Color.clear)
                : GetTextColor(pawn);

            Find.WindowStack.Add(new Dialog_ColourPicker(currentColor, (newColor, closing) =>
            {
                if (isBackground)
                {
                    PawnOrganizer.API.PawnColorDatabase.SetColor(pawn, newColor);
                }
                else
                {
                    SetTextColor(pawn, newColor);
                }
            }));
        }

        // Text color storage (similar to PawnColorDatabase)
        private static Dictionary<string, Color> _textColors = new Dictionary<string, Color>();

        private Color GetTextColor(Pawn pawn)
        {
            return _textColors.TryGetValue(pawn.ThingID, out Color color) 
                ? color 
                : Color.white;
        }

        private void SetTextColor(Pawn pawn, Color color)
        {
            _textColors[pawn.ThingID] = color;
        }

        private void ClearTextColor(Pawn pawn)
        {
            _textColors.Remove(pawn.ThingID);
        }

        // Clipboard for copy/paste
        private static Dictionary<string, int> _clipboard = null;

        private void CopyPriorities(Pawn pawn)
        {
            if (pawn?.workSettings == null) return;

            _clipboard = new Dictionary<string, int>();
            foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                _clipboard[workType.defName] = pawn.workSettings.GetPriority(workType);
            }

            VisualFeedback.AudioManager.PlayClick();
        }

        private void PastePriorities(Pawn pawn)
        {
            if (pawn?.workSettings == null || _clipboard == null) return;

            foreach (var kvp in _clipboard)
            {
                var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(kvp.Key);
                if (workType != null && !pawn.WorkTypeIsDisabled(workType))
                {
                    pawn.workSettings.SetPriority(workType, kvp.Value);
                }
            }

            VisualFeedback.AudioManager.PlayClick();
        }

        private bool HasClipboardData()
        {
            return _clipboard != null && _clipboard.Count > 0;
        }
    }
}
