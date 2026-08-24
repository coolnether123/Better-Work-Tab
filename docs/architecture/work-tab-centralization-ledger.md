# Work tab centralization ledger

## Source baseline

| Item | Value |
| --- | --- |
| Remote branch | `origin/Dev` |
| Remote and local base | `55700b5dc35c1878ed3e7fdd02c2f42fd49459b5` |
| Integration branch | `refactor/schedule-domain-centralization` |
| Consolidated source change | `ad01eed42a8194e8774b649a8b00975b5c323bc5` |
| Integration worktree | `A:\Dev\RimWorld\Worktrees\Better-Work-Tab\schedule-domain-migration` |
| Standalone Spine source | `14fd0633ea6a67ea5dcc5004c76c26b030ebe883` |
| Mission production C# baseline | 443 files, 152,346 physical lines |
| Pre-batch `Dev` baseline | 447 files, 151,788 physical and 134,772 nonblank lines |

The source archive named in the task was not present in the supplied attachment directory, common user folders, or `A:\Dev`. The `Dev` working copy contained 17 modified source and language files with 1,000 insertions and 302 deletions. Those changes contain the named priority-cycle, unavailable-cell, footer, projection, and preview behavior. Commit `2f195ca8` preserves that tree without changing the original working copy.

The preserved tree was committed directly to `Dev` as `ad01eed4` and pushed to `origin/Dev`. Three outstanding feature branches were replayed onto that base. Their focused changes were already present in the consolidated source, so each branch resolved to `ad01eed4`; merging them into `Dev` was then a no-op. `Nether/infrastructure-streamline` is an ancestor of `Dev`, and no active remote feature branch contains a commit outside `Dev`. Historical support and mainline branches remain separate product histories.

Before this batch, `Dev` and `origin/Dev` were reconciled at `55700b5d`. The preceding integrated slices are `b61470be` (architecture boundaries), `755b2e65` (workload capture and schema migration), `805f75c9` (parent priority commands), `b89c0e70` (presentation state and settings writes), and `55700b5d` (parent priority reads). No active feature branch contained unmerged production commits.

The current batch is isolated in the worktree above. After its merge, subsequent architecture work continues from the canonical `Dev` checkout at `A:\Dev\RimWorld\Mods\Better-Work-Tab`.

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

## Current batch verification

| Check | Result |
| --- | --- |
| Production 1.6 build | Passed with MSBuild |
| In-repository deterministic suite | 14 passed, 0 failed |
| External deterministic and contract suite | 258 passed, 0 failed |
| Service contract suite | 13 passed, 0 failed |
| Blocker/high review | Passed after import, multiplayer replay, actionability, and migration-retry corrections |

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
| Schedule and application centralization | `schedule-domain-migration` | `refactor/schedule-domain-centralization` | `55700b5d` | Application, schedules, parent/specific actionability, import and workload adapters | Parent read and presentation slices | Build, 14 deterministic, 258 external, 13 service contracts | Blocker/high gate passed | Ready to integrate | None | Trusted import and workload schedule adapters are narrow permanent boundaries |

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
| Compatibility schedule import fails closed in active multiplayer | Bulk import has no deterministic synchronized transaction protocol | Accepted after merge review |
| Workload rollback owns exact advancing schedule revisions | Partial rollback retries must reject drift without losing ownership of unrestored targets | Accepted after transaction review |
| The existing snapshot evolves into the finished pass view | A parallel view graph would add code and duplicate geometry/state | Accepted after frame-order review |
| Per-game composition belongs to the world game component | Mod startup is not the lifetime owner for mutable game state | Accepted after lifetime review |
| Production centralization is net subtractive | The old path must be removed as each shared owner becomes authoritative | Accepted |

## Simplification scorecard

| Measure | Baseline | Current | Exit condition |
| --- | --- | --- | --- |
| Production C# files | 443 | 454 | New boundaries must continue replacing broader owners; no empty file splitting |
| Production physical lines | 152,346 mission; 151,788 pre-batch | 151,629 | Final count remains below both baselines |
| Production nonblank lines | 134,772 pre-batch | 134,681 | Each implementation batch remains net subtractive |
| Direct settings singleton references | 346 in 94 files | 296 in 87 files | Normal domain and rendering callers use owned ports or snapshots |
| Ambient effective-state runtime references | 266 in 42 files | 180 in 41 files | Retained only at documented Harmony/native edges |
| Direct WorkGrid invalidation calls | 41 in 26 files | 33 in 23 files | Domain changes return state-change results; only UI-local calls remain |
| WorkGrid files with Workload text coupling | 13 files, 685 matches | 13 files, 592 matches | No shared WorkGrid contract depends on Workload types |
| Effective presentation call surface | 120 calls across 31 files and 389 physical lines | 119 fallback-free calls | Continue reducing the mechanism and its caller spans |
| Workload settings transaction writer | About 445 lines including interface and snapshot shell | Unchanged | At or below 360 lines with one receipt-bearing persistence and compensation path |
| Temporary adapters | 0 architecture adapters | 0 | Every introduced adapter has a recorded removal phase and no obsolete adapter remains |

## Remaining work

- Move ordinary specific-job and execution-order writes behind typed application commands without retaining manager-owned UI sequences.
- Compile classic rules and Rule Builder 2 into one canonical command vocabulary and remove their direct mutation loops.
- Finish WorkGrid pass-view decoupling from workload and concrete domain internals.
- Finish global settings/preference ownership and route remaining direct singleton writes through owned operations.
- Shrink the workload backend by moving normal domain mutation, invalidation, and transaction coordination to the application boundary.
- Complete compatibility, multiplayer-runtime, and paired-performance matrices after the remaining mutation paths stabilize.

## Update rule

Update this ledger after each integrated commit. Record the exact command, result, review disposition, adapter state, and next dependency. Remove completed mapping branches and worktrees only after their evidence has been integrated and the source owner no longer needs them.
