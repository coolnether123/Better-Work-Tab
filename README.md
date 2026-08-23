<img width="640" height="360" alt="Better Work Tab 2.0 preview" src="https://github.com/user-attachments/assets/9d9f61c9-3ef6-470c-b751-5d01c508e701" />

# Better Work Tab 2.0

Better Work Tab adds drag-and-drop ordering, specific-job controls, reusable rules, time-based priorities, and layout options to RimWorld's Work tab.

## Transpiler architecture

BWT uses profile-based IL patches for RimWorld 1.6. Each profile identifies a
target method and the instruction patterns BWT expects. BWT verifies those
patterns before applying a patch and leaves the original code unchanged when a
profile does not match.

This implementation lives in `Source/Transpilers/BwtExactProfile` and is built
into BWT. It is not part of standalone Spine. The older Fluent Transpiler code
is no longer used by BWT.

## What 2.0 adds

- **Guided tutorial.** Choose the focused "What's new in 2.0" course or the complete Better Work Tab walkthrough. Lessons point to the relevant controls and actions.
- **Rule Builder 2.0.** Create card-based rulesets with work targets, conditions, priority actions, and map checks. Preview matches before applying. The classic rule builder remains available.
- **Specific-job view.** Open a Work type to manage its individual jobs. Set shared or pawn-specific priorities, rename and reorder jobs, and move them between Work columns.
- **Two specific-job layouts.** Use a focused full-tab view or Fluffy-inspired right-expanding columns.
- **Time-priority schedules.** Ctrl-click a priority cell to set priorities for all 24 hours, including individual specific jobs. Schedules support copy and paste.
- **Priority ranges.** Keep vanilla priorities, let BWT choose automatically, delegate to a compatible provider, or let BWT manage priorities up to 99.
- **Fluffy Work Tab coexistence.** Choose which mod owns the Work tab. When BWT owns it, compatible Fluffy columns and top controls remain available.

You can toggle each major 2.0 system independently in Mod Settings.

## Work-tab essentials

- Drag pawn rows and Work columns to reorder them. The layout is saved.
- Hold Shift for color-coded skill levels and best-pawn indicators.
- Save and restore whole-colony priority layouts as workloads.
- Organize pawns with named, colored, collapsible dividers.
- Highlight selected pawns, hovered rows and columns, and related Work types.
- Use angled or vanilla-style headers, including vertical CJK header stacking.
- Show colonist and bed counts, resize the tab, and customize colors and spacing.
- Right-click pawn names for quick actions and use sub-work-aware one-time work commands.
- Synchronize supported ruleset, workload, and layout actions in multiplayer.

## Fresh installs and 1.x upgrades

BWT handles new 2.0 installs and upgrades from the public 1.0.5 release differently:

- New 2.0 installs start with the specific-job view, Rule Builder 2.0, time-priority schedules, and the guided tutorial enabled.
- Upgrades keep the new 2.0 features disabled initially. The upgrade prompt can enable them through a tutorial course. You can also keep the 1.x interface and enable features later in settings.

Saved 1.x preferences and rulesets are preserved. BWT applies the settings update once. Importing settings later does not repeat it. Back up important saves before changing the mod list.

## Specific jobs and Fluffy-style coverage

Better Work Tab owns its specific-job priorities, ordering, schedules, and two layouts. Fluffy Work Tab is optional.

| Capability | Better Work Tab 2.0 behavior |
| --- | --- |
| Expand Work types into individual jobs | Focused full-tab view or right-expanding columns |
| Shared and pawn-specific job priorities | Stored and executed by BWT |
| Move jobs between Work columns | Drag supported specific jobs onto a different Work column |
| Priorities by hour | BWT 24-hour planner for Work types and specific jobs |
| More priority levels | Configurable BWT range up to 99 |
| Scroll priority editing | Optional for Work, specific-job, and scheduled priority cells |
| Compact top controls | Uses installed Fluffy icons. Optional BWT text substitutes work without Fluffy. |
| Mood, current job, favorite, and detailed-copy columns | Supplied by an installed compatible Fluffy Work Tab |

Shift-click remains reserved for BWT's grouped column dragging, so it does not duplicate Fluffy's whole-row and whole-column shortcut exactly.

## Compatibility

- Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).
- Supports RimWorld 0.13 through 1.6. Multiplayer support is limited to the versions identified in `About/About.xml`.
- Compatible with all DLC and designed for modded Work types.
- Supports [FSF] Complex Jobs ordering and Vanilla Skills Expanded passion conditions.
- Fluffy Work Tab and maintained forks can coexist, with one mod selected as the Work-tab owner.
- CompactWorkTab overlaps the same UI and should not be enabled together.
- Other Work-tab overhauls may conflict. Test before adding one to an existing save.

## Performance

The optimized Work-grid renderer uses caching, viewport culling, and targeted invalidation for large colonies. A vanilla-compatible renderer remains available in advanced settings.

## Credits

- Colour picker by Karel Kroeze (MIT License)
- Fluffy's Work Tab by Fluffy: UI inspiration and compatibility reference. Its software and documentation are MIT licensed. BWT does not bundle its CC BY-SA art or sounds.
- Harmony team: Patch framework
- Full notices: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Support

- [GitHub](https://github.com/coolnether123/Better-Work-Tab)
- [Discord](https://discord.gg/QYBBaRkKWs)
- [Buy me a coffee](https://buymeacoffee.com/coolnether123)
