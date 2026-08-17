using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using RimWorld;
using Spine.UI.ColourPicker;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    /// <summary>
    /// Owns right-click dispatch and context actions for pawn and divider rows.
    /// </summary>
    internal sealed class WorkGridContextActionController
    {
        private readonly WorkTabBodyRenderer _bodyRenderer;
        private readonly Action _markWindowDirty;
        private readonly Action _resizeWindowIfRequestedSizeChanged;

        internal WorkGridContextActionController(
            WorkTabBodyRenderer bodyRenderer,
            Action markWindowDirty,
            Action resizeWindowIfRequestedSizeChanged)
        {
            _bodyRenderer = bodyRenderer ?? throw new ArgumentNullException(nameof(bodyRenderer));
            _markWindowDirty = markWindowDirty ?? throw new ArgumentNullException(nameof(markWindowDirty));
            _resizeWindowIfRequestedSizeChanged = resizeWindowIfRequestedSizeChanged ??
                throw new ArgumentNullException(nameof(resizeWindowIfRequestedSizeChanged));
        }

        internal void ProcessRightClicks(IWorkTabLayoutController layout)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableContextMenuOnRightClick ?? true))
            {
                return;
            }

            if (layout == null)
            {
                return;
            }

            Event evt = Event.current;
            bool rightMouseDown = evt.type == EventType.MouseDown && evt.button == 1;
            bool rightMouseUp = evt.type == EventType.MouseUp && evt.button == 1;
            if (!rightMouseDown && !rightMouseUp)
            {
                return;
            }

            if (rightMouseDown)
            {
                PawnOrganizerSystem.Instance?.CancelActiveDrag();
            }

            if (!_bodyRenderer.TryGetRowAt(layout, evt.mousePosition, out var row))
            {
                return;
            }

            if (row.Divider != null)
            {
                if (rightMouseDown)
                {
                    ShowDividerContextMenu(row.Divider);
                }

                evt.Use();
                return;
            }

            if (row.Pawn == null)
            {
                return;
            }

            bool overWorkPriorityColumn = _bodyRenderer.TryGetBodyColumnAt(layout, evt.mousePosition, out var column) &&
                column.Column?.Worker is PawnColumnWorker_WorkPriority;
            if (!overWorkPriorityColumn)
            {
                if (rightMouseDown)
                {
                    ShowPawnContextMenu(row.Pawn);
                }

                evt.Use();
            }
        }

        private void ShowPawnContextMenu(Pawn pawn)
        {
            var options = new List<FloatMenuOption>
            {
                new BWTTutorialFloatMenuOption(
                    "Insert divider above",
                    () => InsertDividerAbove(pawn),
                    BWTGeneralTutorial.PawnDividerLesson,
                    1,
                    "pawn-divider-insert"),
                new FloatMenuOption("Insert divider below", () => InsertDividerBelow(pawn)),
                // Instrumented like "Change title...": the appearance lesson
                // teaches both, and this is the one option that is always here.
                // A pawn whose title cannot be edited would otherwise leave the
                // lesson pointing at a menu entry that does not exist.
                new BWTTutorialFloatMenuOption(
                    "Set background color...",
                    () =>
                    {
                        BWTGeneralTutorial.NotifyPawnAppearanceMenuOptionChosen(editingTitle: false);
                        ShowBackgroundColorPicker(pawn);
                    },
                    BWTGeneralTutorial.PawnAppearanceLesson,
                    1,
                    "pawn-appearance-color")
            };
            if (PawnTitleUtility.CanEditTitle(pawn))
            {
                options.Insert(2, new BWTTutorialFloatMenuOption(
                    "Change title...",
                    () =>
                {
                    BWTGeneralTutorial.NotifyPawnAppearanceMenuOptionChosen(editingTitle: true);
                    Find.WindowStack.Add(new Dialog_ChangePawnTitle(pawn));
                },
                    BWTGeneralTutorial.PawnAppearanceLesson,
                    1,
                    "pawn-appearance-title"));
            }

            if (PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out _))
            {
                options.Add(new FloatMenuOption("Clear background color", () =>
                {
                    PawnOrganizer.API.PawnColorDatabase.ClearColor(pawn);
                }));
            }

            // Multiplayer follow mode: Copy this pawn row
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    $"Copy {pawn.NameShortColored} row position to my layout (stop following)",
                    () => LayoutSharingManager.CopyPawnRowToLocalAndStop(pawn)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowDividerContextMenu(PawnDivider divider)
        {
            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FeaturesDividers,
                    BetterWorkTabMod.Settings?.enableDividers ?? DefaultSettings.enableDividers))
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit...", () =>
                {
                    Find.WindowStack.Add(new Dialog_EditDivider(divider));
                }),
                new FloatMenuOption("Delete", () =>
                {
                    PawnOrganizerSystem.Instance?.Layout.RemoveDivider(divider);
                    NotifyDividerLayoutChanged();
                })
            };

            // Multiplayer follow mode: Copy this divider
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    "Copy this divider to my layout (stop following)",
                    () => LayoutSharingManager.CopyDividerToLocalAndStop(divider)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void InsertDividerAbove(Pawn pawn)
        {
            var worklist = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || worklist == null)
            {
                return;
            }

            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FeaturesDividers,
                    BetterWorkTabMod.Settings?.enableDividers ?? DefaultSettings.enableDividers))
            {
                return;
            }

            if (layout.AddDividerBeforePawn(pawn, "New Divider", Color.gray) != null)
            {
                NotifyDividerLayoutChanged();
            }
        }

        private void InsertDividerBelow(Pawn pawn)
        {
            var worklist = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || worklist == null)
            {
                return;
            }

            if (!BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.FeaturesDividers,
                    BetterWorkTabMod.Settings?.enableDividers ?? DefaultSettings.enableDividers))
            {
                return;
            }

            if (layout.AddDividerAfterPawn(pawn, "New Divider", Color.gray) != null)
            {
                NotifyDividerLayoutChanged();
            }
        }

        private void NotifyDividerLayoutChanged()
        {
            PawnOrganizerSystem.Instance?.CancelActiveDrag();
            PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
            _markWindowDirty();

            _resizeWindowIfRequestedSizeChanged();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

            if (MultiplayerBridge.Active)
                LayoutSharingManager.NotifyLayoutChanged();
        }

        private void ShowBackgroundColorPicker(Pawn pawn)
        {
            Color current = PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out var stored) ? stored : Color.white;
            Find.WindowStack.Add(new Dialog_ColourPicker(current, (newColor, _) =>
            {
                PawnOrganizerSystem.Instance?.SetPawnBackgroundColor(pawn, newColor);
            }));
        }
    }
}
