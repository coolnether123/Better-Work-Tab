using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using RimWorld;
using Spine.UI.ColourPicker;
using UnityEngine;
using Verse;
using Verse.Sound;

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
        private readonly Func<bool> _canEditDividers;

        internal WorkGridContextActionController(
            WorkTabBodyRenderer bodyRenderer,
            Action markWindowDirty,
            Action resizeWindowIfRequestedSizeChanged,
            Func<bool> canEditDividers)
        {
            _bodyRenderer = bodyRenderer ?? throw new ArgumentNullException(nameof(bodyRenderer));
            _markWindowDirty = markWindowDirty ?? throw new ArgumentNullException(nameof(markWindowDirty));
            _resizeWindowIfRequestedSizeChanged = resizeWindowIfRequestedSizeChanged ??
                throw new ArgumentNullException(nameof(resizeWindowIfRequestedSizeChanged));
            _canEditDividers = canEditDividers ?? throw new ArgumentNullException(nameof(canEditDividers));
        }

        internal void ProcessRightClicks(in WorkTabView view)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableContextMenuOnRightClick ?? true))
            {
                return;
            }

            if (!view.HasMatchingLayoutRevision)
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

            if (!_bodyRenderer.TryGetRowAt(in view, evt.mousePosition, out var row))
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

            bool overWorkPriorityColumn = _bodyRenderer.TryGetBodyColumnAt(in view, evt.mousePosition, out var column) &&
                column.Column?.Worker is PawnColumnWorker_WorkPriority;
            if (!overWorkPriorityColumn)
            {
                if (rightMouseDown)
                {
                    ShowPawnContextMenu(row.Pawn, view.Preview);
                }

                evt.Use();
            }
        }

        private void ShowPawnContextMenu(Pawn pawn, IWorkGridPreviewPort preview)
        {
            var options = new List<FloatMenuOption>
            {
                new BWTTutorialFloatMenuOption(
                    "BWT_Context_InsertDividerAbove".Translate(),
                    () => InsertDividerAbove(pawn),
                    BWTGeneralTutorial.PawnDividerLesson,
                    1,
                    "pawn-divider-insert"),
                new FloatMenuOption("BWT_Context_InsertDividerBelow".Translate(), () => InsertDividerBelow(pawn)),
                // Instrumented like "Change title...": the appearance lesson
                // teaches both, and this is the one option that is always here.
                // A pawn whose title cannot be edited would otherwise leave the
                // lesson pointing at a menu entry that does not exist.
                new BWTTutorialFloatMenuOption(
                    "BWT_Context_SetBackgroundColor".Translate(),
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
                    "BWT_Context_ChangeTitle".Translate(),
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
                options.Add(new FloatMenuOption("BWT_Context_ClearBackgroundColor".Translate(), () =>
                {
                    PawnOrganizer.API.PawnColorDatabase.ClearColor(pawn);
                }));
            }

            AddWorkloadPreviewMembershipOption(options, pawn, preview);

            // Multiplayer follow mode: Copy this pawn row
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    "BWT_Context_CopyPawnRow".Translate(pawn.NameShortColored),
                    () => LayoutSharingManager.CopyPawnRowToLocalAndStop(pawn)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void AddWorkloadPreviewMembershipOption(
            List<FloatMenuOption> options,
            Pawn pawn,
            IWorkGridPreviewPort preview)
        {
            if (preview?.IsActive != true)
            {
                return;
            }

            switch (preview.GetMembershipAction(pawn))
            {
                case WorkGridPreviewMembershipAction.ExplicitlyExcluded:
                    options.Add(new FloatMenuOption(
                        "BWT_Context_MembershipExcluded".Translate(),
                        null));
                    return;
                case WorkGridPreviewMembershipAction.PawnUnavailable:
                    options.Add(new FloatMenuOption(
                        "BWT_Context_MembershipPawnUnavailable".Translate(),
                        null));
                    return;
                case WorkGridPreviewMembershipAction.Include:
                    options.Add(new FloatMenuOption(
                        "BWT_Context_IncludeInApplication".Translate(),
                        () => TogglePreviewMembership(preview, pawn)));
                    return;
                case WorkGridPreviewMembershipAction.Exclude:
                    options.Add(new FloatMenuOption(
                        "BWT_Context_ExcludeFromApplication".Translate(),
                        () => TogglePreviewMembership(preview, pawn)));
                    return;
                case WorkGridPreviewMembershipAction.OutsideScope:
                    options.Add(new FloatMenuOption(
                        "BWT_Context_MembershipOutsideScope".Translate(),
                        null));
                    return;
                case WorkGridPreviewMembershipAction.Unavailable:
                    options.Add(new FloatMenuOption(
                        "BWT_Context_MembershipUnavailable".Translate(),
                        null));
                    return;
            }
        }

        private void TogglePreviewMembership(
            IWorkGridPreviewPort preview,
            Pawn pawn)
        {
            if (preview.ToggleMembership(pawn))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                _markWindowDirty();
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            Messages.Message(
                preview.LastMessage,
                MessageTypeDefOf.RejectInput,
                false);
        }

        private void ShowDividerContextMenu(PawnDivider divider)
        {
            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesDividers))
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("BWT_Context_Edit".Translate(), () =>
                {
                    Find.WindowStack.Add(new Dialog_EditDivider(divider));
                }),
                new FloatMenuOption("Delete".Translate(), () =>
                {
                    PawnOrganizerSystem.Instance?.Layout.RemoveDivider(divider);
                    NotifyDividerLayoutChanged();
                })
            };

            // Multiplayer follow mode: Copy this divider
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    "BWT_Context_CopyDivider".Translate(),
                    () => LayoutSharingManager.CopyDividerToLocalAndStop(divider)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void InsertDividerAbove(Pawn pawn)
        {
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || !_canEditDividers())
            {
                return;
            }

            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesDividers))
            {
                return;
            }

            if (layout.AddDividerBeforePawn(pawn, "BWT_Dialog_Divider_DefaultName".Translate(), Color.gray) != null)
            {
                NotifyDividerLayoutChanged();
            }
        }

        private void InsertDividerBelow(Pawn pawn)
        {
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || !_canEditDividers())
            {
                return;
            }

            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesDividers))
            {
                return;
            }

            if (layout.AddDividerAfterPawn(pawn, "BWT_Dialog_Divider_DefaultName".Translate(), Color.gray) != null)
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
