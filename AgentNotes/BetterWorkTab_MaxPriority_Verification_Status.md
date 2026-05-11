# Better Work Tab Max Priority Verification Status

Date: 2026-05-10

## Branch Status

- Current checkout: `feat/max-priority-api-and-transpiler-cleanup` at `4d5d0aa96`.
- No branch switch, merge, or rebase was performed.
- `Dev` is at `7669869bd`.
- `Main` is at `4eeca7180`.
- `origin/TTDG/MaxPriorityIncrease` is at `0d9217eef`.
- `origin/feat/max-priority-api-and-transpiler-cleanup` is at `6981355fe`.

## Branch Comparison Result

- `origin/TTDG/MaxPriorityIncrease` is contained in `Dev`; the old max-priority branch was folded into `Dev` through PR #8 and later cleanup.
- `origin/feat/max-priority-api-and-transpiler-cleanup` is one commit ahead of `Dev`: `6981355fe Improve max priority compatibility and transpiler diagnostics`.
- The local feature branch is one commit ahead of origin: `4d5d0aa96 Refine max priority rule builder flow`.
- The feature branch should be reconciled into `Dev` after review. It was not merged here because the task explicitly prohibited git merges and branch changes.

## Feature Branch Disposition

Status: fixed in the current checkout, not merged.

The feature branch was not ready as-is because:

- `Source/API/PriorityApi.cs` existed but was not included in the old-style `.csproj`, so the public API was not compiled.
- Direct max-priority transpilers still threw when IL patterns were missing.
- Priority-order initialization still defaulted to vanilla `4` in `WorkAssignmentRuleset`.
- Rule-builder selected priorities with no rules could disappear from the selector.

## Behavior Verified

- Priority display: `WidgetsWork.ColorOfPriority` uses extended priority color ranges; skill-overlay compact labels handle multi-digit priorities.
- Tooltip display: work-box tooltips remap extended priorities to vanilla `1..4` wording through `GetTooltipPriority`.
- Priority editing: `Pawn_WorkSettings.SetPriority` upper-bound IL is patched to use the configured max priority.
- Scroll priority changes: work-cell scroll changes route through `PriorityAuthority.GetNextManualPriority` and `PriorityCommandRouter`.
- Manual priorities: auto-assign and workload application now use the priority router, so values are clamped and multiplayer sync paths are respected.
- Auto-enable manual priorities: Work tab auto-enable now calls `PriorityCommandRouter.SetUseWorkPriorities(true)` instead of directly mutating play settings.
- Rule builder priority ordering: order uses effective max priority, selected unused priorities remain visible, and priority `0` can be selected from the add control.
- Save/load: `maxPriorityInt`, ruleset `PriorityOrder`, worklist priorities, and multiplayer `SharedMaxPriority` are covered by Scribe paths.
- Default priorities: enabled default remains priority `3`, clamped to the effective max priority.
- Drag/drop interactions: column dragging suppresses priority edits only while dragging; drag completion/cancel paths clear `IsHeaderDragging`, and pending header click state is now cleared after drag.

## Mod Compatibility Risks

- Harmony targets remain high-risk because they touch core Work tab paths: `WidgetsWork.DrawWorkBoxFor`, `WidgetsWork.TipForPawnWorker`, `PawnColumnWorker_WorkPriority.HeaderClicked`, and `Pawn_WorkSettings.SetPriority`.
- These are necessary for priorities above vanilla `4`; other work-tab overhaul mods can still conflict.
- Transpilers now fail safely with one-time warnings and return the original IL if expected patterns are missing.
- If the `SetPriority` transpiler is skipped, priorities above `4` will not persist; the warning is intentional because silently pretending max priority works would be worse.
- `About/About.xml` already declares incompatibility with `Fluffy.WorkTab` and `Mlie.CompactWorkTab`.
- Multiplayer paths are routed through `PriorityCommandRouter` and `PriorityMultiplayerSync`; all multiplayer clients should run the same build and max-priority branch state.

## Files Changed

- `Source/Better Work Tab.csproj`
- `Source/API/PriorityApi.cs`
- `Source/API/PriorityApi.md`
- `Source/Features/Patches/MaxPriorityPatches.cs`
- `Source/Features/Rules/WorkAssignmentRuleset.cs`
- `Source/UI/RuleBuilder/State/RuleBuilderState.cs`
- `Source/UI/RuleBuilder/Panels/PrioritySelectorPanel.cs`
- `Source/UI/MainTabWindow_BetterWork.cs`
- `Source/UI/Headers/Angled/AngledHeaderInteraction.cs`
- `AgentNotes/BetterWorkTab_MaxPriority_Verification_Status.md`
- `1.6/Assemblies/Better Work Tab.dll`
- `1.6/Assemblies/Better Work Tab.pdb`

The checkout also already contained related uncommitted max-priority/router changes before this pass, including `Source/Features/Patches/PriorityCommandService.cs`.

## Verification Notes

- Ran `git fetch --all --prune`.
- Confirmed `origin/TTDG/MaxPriorityIncrease` is an ancestor of `Dev`.
- Reviewed `README.md`, `About/About.xml`, priority API files, max-priority patches, rule-builder priority UI, settings, workload/rules priority application, multiplayer bridge, and relevant Harmony transpiler infrastructure.
- Ran `git diff --check`: no whitespace errors.
- Built with Visual Studio MSBuild:
  `& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" "Source\Better Work Tab.sln" /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /v:minimal`
- Build succeeded and wrote `1.6/Assemblies/Better Work Tab.dll`.
- Remaining build output was limited to existing unused-field warnings.
- No in-game manual test was run in this environment.
