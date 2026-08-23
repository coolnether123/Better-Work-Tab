# Work tab centralization ledger

## Source baseline

| Item | Value |
| --- | --- |
| Remote branch | `origin/Dev` |
| Remote and local base | `a41a9931ca350ac944f89094a674f0da5a773685` |
| Integration branch | `refactor/central-work-tab-architecture` |
| Consolidated source change | `ad01eed42a8194e8774b649a8b00975b5c323bc5` |
| Integration worktree | `A:\Dev\RimWorld\Worktrees\Better-Work-Tab\central-architecture` |
| Standalone Spine source | `14fd0633ea6a67ea5dcc5004c76c26b030ebe883` |
| Production C# baseline | 443 files, 152,346 physical lines |

The source archive named in the task was not present in the supplied attachment directory, common user folders, or `A:\Dev`. The `Dev` working copy contained 17 modified source and language files with 1,000 insertions and 302 deletions. Those changes contain the named priority-cycle, unavailable-cell, footer, projection, and preview behavior. Commit `2f195ca8` preserves that tree without changing the original working copy.

The preserved tree was committed directly to `Dev` as `ad01eed4` and pushed to `origin/Dev`. Three outstanding feature branches were replayed onto that base. Their focused changes were already present in the consolidated source, so each branch resolved to `ad01eed4`; merging them into `Dev` was then a no-op. `Nether/infrastructure-streamline` is an ancestor of `Dev`, and no active remote feature branch contains a commit outside `Dev`. Historical support and mainline branches remain separate product histories.

Commit `a41a9931` subsequently updated player-facing text in `About/About.xml` and `README.md`. It is now the local and remote `Dev` head. It does not change the measured production C# baseline, and the architecture branch was rebased onto it before integration.

The concurrent `About/About.xml` and `README.md` edits were preserved in `a41a9931` before architecture work resumed. The migration did not alter or fold those edits into its own batch.

## Baseline verification

| Check | Result | Evidence |
| --- | --- | --- |
| Deterministic source inclusion | Passed | `bwt-source-inclusion-contract/v1` |
| Deterministic tests | 231 passed, 0 failed | `A:\Dev\RimWorld\Runtime\Evidence\BWT-Central-Architecture\baseline-deterministic` |
| Full non-runtime gate | 18 steps passed, 0 failed | `A:\Dev\RimWorld\Runtime\Evidence\BWT-Central-Architecture\baseline-full\run-bc9035d1407843c083f4e04df00d4f0a\evidence\verification.json` |
| Embedded 1.6 build and package | Passed | Full gate evidence |
| External Spine 1.6 build and package | Passed | Full gate evidence |
| Spine mirror contract | Passed, 28 shared and 28 retired files | Full gate evidence |
| Service contracts | Passed | Full gate evidence |
| Paired API contracts | Passed | Full gate evidence |
| Production-surface checks | Passed for both candidates | Full gate evidence |
| TestProbe build and staging | Passed | Full gate evidence |
| Workloads V2 deterministic suites | 12 passed, 1 failed | `gateway-lifecycle-contracts` expects the retired footer action-in-draw source shape; production separates draw and input dispatch |
| Runtime smoke behavior | Passed with contaminated log scan | Work tab opened, status and priority smoke passed, screenshot captured, and the session stopped cleanly; matrix failure came from an unrelated historical FactionLens error in `Player-prev.log` |
| Runtime isolation after stop | Passed | Harness doctor reported no resource leases, unregistered processes, or blockers |
| Compatibility matrix | Pending | Scheduled after domain and adapter migration |
| Multiplayer runtime | Pending | Scheduled after command transport migration |
| Paired benchmark | Pending | Scheduled for changed hot paths |

Two earlier full-gate invocations failed before BWT build steps because the active tooling worktree does not contain the externalized BWT build contract or Spine mirror script. The passing invocation used the clean tooling branch that owns those files and the active workspace and version manifests.

## Integration lanes

| System | Worktree | Branch | Base | Owned paths | Shared dependency | Tests | Review | Integration state | Blockers | Temporary adapters | Removal phase |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Application and mutation map | `central-application-map` | `refactor/work-tab-application-map` | `ad01eed4` | Read-only repository map | None | Inventory only | Compared | Complete | None | None | Not applicable |
| Priority and schedule map | `priority-schedule-map` | `refactor/priority-schedule-map` | `ad01eed4` | Read-only priority and schedule paths | Application vocabulary | Inventory and test gap analysis | Compared | Complete | None | None | Not applicable |
| Specific-job and execution map | `specific-job-map` | `refactor/specific-job-map` | `ad01eed4` | Read-only specific-job and execution paths | Application vocabulary | Inventory and test gap analysis | Compared | Complete | None | None | Not applicable |
| WorkGrid, layout, rendering, and input | `work-grid-map` | `refactor/work-grid-state-map` | `ad01eed4` | Read-only WorkGrid map | Canonical read and change result | Inventory and performance analysis | Compared | Complete | None | None | Not applicable |
| Settings, presentation, and rules | `settings-rules-map` | `refactor/settings-rules-map` | `ad01eed4` | Read-only settings and rules map | Canonical read and command types | Inventory and transaction analysis | Compared | Complete | None | None | Not applicable |
| Workloads, persistence, multiplayer, and compatibility map | `workload-boundaries-map` | `refactor/workload-boundaries-map` | `ad01eed4` | Read-only boundary map | All domain contracts | Inventory, migration, and protocol analysis | Compared | Complete | None | None | Not applicable |
| Shared seam and transaction review | `central-architecture` | `refactor/central-work-tab-architecture` | `ad01eed4` | Architecture records only | All six maps | Call-graph, transaction, lifecycle, and subtraction review | Blocking findings incorporated | Complete | Additive first draft rejected | None | Not applicable |

## Shared decisions

| Decision | Evidence | State |
| --- | --- | --- |
| Application types remain independent from workload keys and intents | Task contract and current multi-caller requirement | Accepted |
| Domain services retain separate state and policy | Current owners have different invariants and persistence | Accepted |
| The application exposes explicit typed operations, not a marker-command bus | Prevents a growing dispatcher and preserves distinct displayed, stored, projected, and submitted intent | Accepted after call-graph review |
| Preview uses a projection editor outside game-state application | Preview must not acquire live transport, persistence, mirror, or invalidation effects | Accepted after call-graph review |
| The application returns one coherent committed state change | Required for revisions, rollback, execution recache, and UI invalidation | Accepted after transaction-order correction |
| Live and projected reads use one resolver with an optional overlay | Sharing an interface alone would preserve duplicate policy | Accepted after call-graph review |
| Multiplayer submission and synchronized local application are distinct results | A dispatched call is not yet an applied mutation | Accepted after protocol review |
| The existing snapshot evolves into the finished pass view | A parallel view graph would add code and duplicate geometry/state | Accepted after frame-order review |
| Per-game composition belongs to the world game component | Mod startup is not the lifetime owner for mutable game state | Accepted after lifetime review |
| Production centralization is net subtractive | The old path must be removed as each shared owner becomes authoritative | Accepted |

## Simplification scorecard

| Measure | Baseline | Current | Exit condition |
| --- | --- | --- | --- |
| Production C# files | 443 | 443 | No increase caused by empty file splitting; every new boundary replaces broader code |
| Production physical lines | 152,346 | 152,346 | Final count is below baseline |
| Direct settings singleton references | 346 in 94 files | 346 in 94 files | Normal domain and rendering callers use owned ports or snapshots |
| Ambient effective-state runtime references | 266 in 42 files | 266 in 42 files | Retained only at documented Harmony/native edges |
| Direct WorkGrid invalidation calls | 41 in 26 files | 41 in 26 files | Domain changes return state-change results; only UI-local calls remain |
| WorkGrid files with Workload text coupling | 13 files, 685 matches | 13 files, 685 matches | No shared WorkGrid contract depends on Workload types |
| Effective presentation call surface | 120 calls across 31 files and 389 physical lines | Unchanged | Fallback-free call spans at or below 220 lines; at least 252 mechanism and caller lines deleted |
| Workload settings transaction writer | About 445 lines including interface and snapshot shell | Unchanged | At or below 360 lines with one receipt-bearing persistence and compensation path |
| Temporary adapters | 0 architecture adapters | 0 | Every introduced adapter has a recorded removal phase and no obsolete adapter remains |

## Current blockers

- The named source archive is unavailable. Consolidated `Dev` is the only supplied newer source snapshot.
- A Workloads V2 source-contract assertion is stale relative to the intentional footer draw/input split and must be replaced with a behavior-preserving contract.
- Schema-2 scalar-only workload presentation data can be lost because absent and explicitly empty typed members are currently conflated.
- Multiplayer workload prepare assumes matching per-player local template identity and fingerprint without an explicit distribution or parity contract.
- Runtime behavior passed, but the baseline matrix log result is contaminated by a historical error in a global previous-log file.
- Performance comparison evidence remains pending until the affected production paths stabilize.
- The active tooling worktree is mid-migration. BWT verification currently needs the externalized build contract and mirror script from its clean owning branch.

## Update rule

Update this ledger after each integrated commit. Record the exact command, result, review disposition, adapter state, and next dependency. Remove completed mapping branches and worktrees only after their evidence has been integrated and the source owner no longer needs them.
