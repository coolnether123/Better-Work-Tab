<img width="640" height="360" alt="Better Work Tab 2.0 preview" src="https://github.com/user-attachments/assets/9d9f61c9-3ef6-470c-b751-5d01c508e701" />

# Better Work Tab 2.0

Better Work Tab rebuilds RimWorld's Work tab around faster colony setup, detailed job control, reusable rules, and a layout you can shape around the way you play.

## Transpiler architecture

Active BWT IL patches use the BWT exact-profile transactional engine with exact
RimWorld 1.6 target profiles. Profiles describe the known method, call
signatures, locals, and IL anchors; the engine performs matching, verification,
atomic mutation, rollback, and diagnostics. The engine is BWT-owned under
`Source/Transpilers/BwtExactProfile` and is not part of the standalone Spine
mirror. This is the 2.0 exact-profile naming/path-isolation boundary; the older
Fluent transpiler remains compiled as a frozen legacy compatibility surface and
is not integrated into this engine.

## What 2.0 adds

- **Guided tutorial** — choose a focused “What’s new in 2.0” course or the complete Better Work Tab walkthrough. Lessons point at the real controls and ask you to perform the action.
- **Rule Builder 2.0** — create card-based rulesets from work targets, conditions, priority actions, and map checks; preview matches before applying. The classic rule builder remains available.
- **Specific-job drilldown** — open a Work type into its individual jobs, set shared or pawn-specific priorities, rename jobs, reorder them, and move them between Work columns.
- **Two specific-job layouts** — use a clean focused full-tab view or Fluffy-inspired right-expanding columns.
- **Time-priority schedules** — Ctrl-click a priority cell to plan different priorities across all 24 hours, including individual specific jobs. Schedules support copy and paste.
- **Priority ranges** — keep vanilla priorities, let BWT choose automatically, delegate to a compatible provider, or let BWT manage priorities up to 99.
- **Fluffy Work Tab coexistence** — choose which mod owns the Work tab. When BWT owns it, compatible Fluffy columns and familiar top controls can remain available.

Every major 2.0 system is independently toggleable in Mod Settings.

## Work-tab essentials

- Drag pawn rows and Work columns to reorder them; layouts persist.
- Hold Shift for color-coded skill levels and best-pawn indicators.
- Save and restore whole-colony priority layouts as workloads.
- Organize pawns with named, colored, collapsible dividers.
- Highlight selected pawns, hovered rows and columns, and related Work types.
- Use angled or vanilla-style headers, including vertical CJK header stacking.
- Show colonist and bed counts, resize the tab, and customize colors and spacing.
- Right-click pawn names for quick actions and use sub-work-aware one-time work commands.
- Synchronize supported ruleset, workload, and layout actions in multiplayer.

## Fresh installs and 1.x upgrades

Better Work Tab deliberately gives these cohorts different first launches:

- A **fresh 2.0 install** starts with specific-job drilldown, Rule Builder 2.0, time-priority schedules, and the guided tutorial enabled.
- A player **upgrading from public 1.0.5** keeps the new top-level feature gates off. An upgrade prompt explains the change; choosing a tutorial course enables the supported 2.0 feature set. Skipping the course preserves the familiar 1.x-style surface until the player enables features in settings.

The migration preserves saved 1.x preferences and rulesets, stamps the new settings schema once, and does not treat later imports as a startup migration. Back up important saves before changing any mod list.

## Specific jobs and Fluffy-style coverage

Better Work Tab owns its specific-job priorities, ordering, schedules, and two presentation styles. Fluffy Work Tab is optional.

| Capability | Better Work Tab 2.0 behavior |
| --- | --- |
| Expand Work types into individual jobs | Focused full-tab view or right-expanding columns |
| Shared and pawn-specific job priorities | Stored and executed by BWT |
| Move jobs between Work columns | Drag supported specific jobs onto a different Work column |
| Priorities by hour | BWT 24-hour planner for Work types and specific jobs |
| More priority levels | Configurable BWT range up to 99 |
| Scroll priority editing | Optional for Work, specific-job, and scheduled priority cells |
| Compact top controls | Uses installed Fluffy icons; optional BWT text substitutes work standalone |
| Mood, current job, favorite, and detailed-copy columns | Supplied by an installed compatible Fluffy Work Tab |

Shift-click remains reserved for BWT's grouped column dragging, so it does not duplicate Fluffy's whole-row and whole-column shortcut exactly.

## Compatibility

- Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).
- Supports RimWorld 0.13 through 1.6; multiplayer support is limited to the versions identified in `About/About.xml`.
- Compatible with all DLC and designed for modded Work types.
- Supports [FSF] Complex Jobs ordering and Vanilla Skills Expanded passion conditions.
- Fluffy Work Tab and maintained forks can coexist, with one mod selected as the Work-tab owner.
- CompactWorkTab overlaps the same UI and should not be enabled together.
- Other Work-tab overhauls may conflict; test before adding one to an existing save.

## Performance

The optimized Work-grid renderer uses caching, viewport culling, and targeted invalidation for large colonies. A vanilla-compatible renderer remains available as a fallback in advanced settings.

Source-level performance comparisons must use `Tools/Invoke-BwtPairedBenchmark.ps1`. The gate runs reference and candidate builds simultaneously through identical muted harness lanes, save/mod snapshots, and profiler intervals; independent historical captures are suitable for context, not regression attribution.

## Credits

- Colour picker by Karel Kroeze (MIT License)
- Fluffy's Work Tab by Fluffy — UI inspiration and compatibility reference (MIT software/documentation); BWT does not bundle Fluffy's CC BY-SA art or sounds
- Harmony team — patch framework
- Full notices: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Support

- [GitHub](https://github.com/coolnether123/Better-Work-Tab)
- [Discord](https://discord.gg/QYBBaRkKWs)
- [Buy me a coffee](https://buymeacoffee.com/coolnether123)
