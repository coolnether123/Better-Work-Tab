# Work tab centralization ledger

## Canonical source

| Item | Value |
| --- | --- |
| Production owner | `A:\Dev\RimWorld\Mods\Better-Work-Tab` |
| Production branch | `Dev` |
| Completion base | `e5987800e3b08c356f1bbcb61924f415d0bc5f70` |
| Test owner | `A:\Dev\RimWorld\Infrastructure\Better-Work-Tab-Tests` on `main` |
| Harness owner | `A:\Dev\RimWorld\Infrastructure\AgenticHarness\RimWorld-Agent` on `master` |
| Runtime evidence root | `A:\Dev\RimWorld\Runtime\BwtCentralArchitecture` |

The production checkout is the only current source owner. The supplied archive was separate input and is not a source or build authority. A final fetch found no active production feature commit outside `Dev`; only historical `Support/*` product lines remain non-ancestors. The TestProbe API-refresh branch was merged into test `main` after reconciling its older schedule API calls with the current public notification boundary.

## Final ownership map

| System | Authoritative state or policy | Normal read | Normal write or coordination | Persistence and edge role | State |
| --- | --- | --- | --- | --- | --- |
| Application | Per-game domain services | Canonical effective-state providers | `WorkTabApplication` lowers typed requests to `WorkTabStagedMutation` | `WorkTabGameRoot` composes domain owners and narrow persistence ports | Complete |
| Priority | Authority broker and priority policy | `ParentPriorityRead` and finished view | Displayed or stored priority batches | External providers remain narrow authority ports | Complete |
| Schedules | `TimePriorityScheduleRuntime` and service | Immutable schedule values | Application schedule commands | Game component records; mirror and synchronized replay use commands | Complete |
| Specific jobs | Reassignment domain | Canonical override and inheritance reader | One specific-job batch in `WorkTabMutationScope` | Exact rollback and stale-data cleanup remain domain-owned | Complete |
| Display and execution order | Column-order and reassignment domains | Effective order readers | Explicit reorder or staged layout command | Layout retarget includes schedule compensation | Complete |
| Settings and presentation | Global preference owner and workload projection | Pass-stable settings snapshot | Context router or receipt-bearing workload writer | Preview port is session-local; accepted writes persist once | Complete |
| WorkGrid | Snapshot provider and `WorkTabView` | Finished immutable view | Input routes application commands | Renderer owns no game state | Complete |
| Rules | Classic and V2 evaluators | Canonical state view | `RuleApplicationPlanningScope` compiles one atomic plan | Old rule records translate at the boundary | Complete |
| Workloads | Repository, session projection, and planner | Canonical live or projected state | Workload metadata plus one shared staged live mutation | V2 keeps repository/CAS policy but has no separate live write or rollback engine | Complete |
| Multiplayer | Transport layer | Fingerprints and expected revisions | Synchronized entry points call the same local handlers | Protocol v2 waits for final-delivery acknowledgements | Complete at deterministic-contract level |
| Compatibility | Per-integration narrow gateways | Core-facing authority and state reads | Canonical command or trusted import ports | Fail-closed behavior retained | Complete at repository-contract level |
| Harmony and Spine | Patch and standalone integration edges | Patch-safe facade only when injection is unavailable | BWT-owned chains use explicit state views | Embedded and external Spine remain separate owners | Complete |

## Central transaction and revision result

`WorkTabStagedMutation` is the shared live mutation vocabulary for priorities, schedules, specific jobs, configuration, and external-specific authority. `WorkTabAtomicMutationPlan` and Workloads V2 both lower into it. `WorkTabStagedMutationReceipt` captures rollback state, stages each participating domain without durable intermediate publication, validates revision ownership, commits one coherent application change, and compensates in reverse order on failure. Rejected, unchanged, submitted, and successfully rolled-back requests do not publish a normal committed revision.

Classic rules, Rule Builder 2, legacy worklists, and imports compile the atomic plan. V2 retains its ownership, membership, diagnostics, Apply/Update/Fork, repository compare-and-swap, and recovery metadata, but compiles the live portion into the same staged receipt. Its former parent-priority, specific-job, schedule, and inverse rollback loops were removed from `Workload2Backend`.

The publisher consumes an explicit change receipt with before/after revision vectors, dimensions, effects, and sorted affected targets. Exact parent changes use sparse `(pawn, WorkType)` invalidation; broad invalidation is reserved for broad or unknown changes. Renderer dirty flags, row/execution recache, pawn-table notification, and external mirroring no longer accumulate in the application executor.

## Finished view and input

`WorkTabView` is the pass envelope for geometry, effective state, settings, and prepared snapshot data. Input and drawing use the same view. The optimized BWT path receives immutable specific-job pixels and overlay flags from its snapshot. The snapshot contains no live RimWorld objects and no cache-owned presentation objects. The renderer uses a separate pass-captured layout lookup for hover, tooltips, selection, and native fallback. No concrete workload type is part of the shared WorkGrid contracts, and the BWT-owned body renderer does not resolve live workload state while drawing.

Workload drafts use a shared bounded history implementation. `Ctrl+Z` and `Ctrl+Y` or `Ctrl+Shift+Z` undo and redo accepted draft edits. Canceling a dirty preview retains its projected state and both history stacks; the next `Ctrl+Z` restores the canceled workload with the unsaved changes. Restoration remains preview-only and does not mutate live game state.

## Removed duplicate paths

- Manager-owned single priority and specific-job write sequences.
- Per-rule and per-workload mutation, invalidation, and rollback loops.
- The separate multiplayer column-order synchronizer.
- Duplicate WorkGrid inspection semantics and effective-state revision shim.
- Header-positioning, tutorial-geometry, settings-visibility, and naming shells whose behavior already had another owner.
- Dead diagnostic formatting in production; the external probe formats its own diagnostics through the supported read API.
- Backend contracts and authorization forwarding that duplicated the application boundary.

No temporary mutation adapter remains. The one-caller snapshot presentation and settings preview ports are intentional ownership edges. `RetainedWorkBoxRowCache` is also intentionally separate despite one logical owner: it owns render-resource allocation, bounded eviction, device-loss recovery, and direct-render fallback rather than domain state.

## Simplification scorecard

| Measure | Mission baseline | Final working tree | Change |
| --- | ---: | ---: | ---: |
| Production C# files | 443 | 471 | +28 focused boundary and later performance files |
| Production physical lines | 152,346 | 155,701 | +3,355 across the full mission and later feature work |
| Production nonblank lines | Not recorded at mission start | 138,679 | Current measurement |

Relative to the reviewed `5eb16fc6` tree, this follow-up adds 6,755 and removes 3,030 tracked production lines: a net increase of 3,725 lines. That comparison includes new architecture boundaries, performance infrastructure, tests' production-facing support, and later tutorial, roster, and interaction work. It does not support a shrink claim; the simplification result is responsibility consolidation and removal of duplicate execution paths, not fewer total source lines.

## Final verification

| Check | Result | Evidence |
| --- | --- | --- |
| Production 1.6 build with embedded Spine | Passed, 0 errors | Existing duplicate Publicizer source warning only |
| In-repository deterministic suite | 22 passed, 0 failed | Local production suite, including retained-renderer orientation, roster revision, sparse invalidation, and spawn-performance contracts |
| External deterministic and architecture contracts | 273 passed, 0 failed | Test `main` suite |
| Full isolated verifier | Passed | `A:\Dev\RimWorld\Runtime\BwtCentralArchitecture\final-verification\run-482989b9e97b43f89fbdc33ea99e32e5\evidence\verification.json` |
| External Spine build and mirror checks | Passed | Full verifier |
| API, service, package, and production-surface checks | Passed | Full verifier |
| TestProbe build and staging | Passed, 0 warnings and 0 errors | Full verifier |
| Main-window frame phases | 14 passed, 0 failed | Focused source contract |
| Finished-view and visible-column optimization | 46 passed, 0 failed | Focused source and performance contract |
| Interaction cleanup | 45 passed, 0 failed | Focused source contract |
| Input behavior | 33 passed, 0 failed | Focused source contract |
| Ordering | 8 passed, 0 failed | Focused source contract |
| Harness extension suite | 35 passed, 0 failed | Harness extension tests |
| Live Work tab ownership and ExpandBeside geometry | 231 checks, 0 failures for 3 pawns and 24 columns | Runtime profile `central-final-probe2` |
| Final 100-pawn normal geometry | 633 checks, 0 failures | Runtime session `runtime-candidate-8e7299d4ead348a8968cba99808b542b`, profile `final-normal` |
| Final 103-pawn Focus View geometry after spawn trials | 637 checks, 0 failures | Same runtime session, profile `final-focus`; the increased roster count was retained rather than discarded |
| Workload preview open, three repeated trials | 95.3619 ms average, 105.3098 ms maximum | Same 100-pawn save and candidate; previous implementation measured about 789.8 ms average |
| Workload preview steady frame | 2.6728 ms per frame for `DoWindowContents` | 53.9-second repeated open/close run; preview preparation averaged 0.0026 ms per call |
| Workload preview scrolling | 4.6339 ms per frame for `DoWindowContents`; 1.4707 ms per frame for row drawing | Forty injected wheel events over the 100-pawn projected presentation; earlier implementation measured 12.4434 ms per frame for `DoWindowContents` |
| Colonist spawn transition | BWT work-giver recache 0.2215 ms cold, then 0.0256 and 0.0215 ms; pawn-table notification 1.0268-1.0565 ms | Three consecutive spawns, with observed colonist counts 100→101→102→103; full action time varied with vanilla pawn generation from 9.0359 to 66.4109 ms |
| Retained priority glyph orientation | Passed visual runtime inspection | Direct3D retained rows now present the top-left-composed surface without a second UV flip |
| Live isolation after test | Passed | Session stopped cleanly; harness reported no blockers before launch |

The focused visible-column test confirms prepared presentation, visible-column culling, and the absence of repeated live resolution in the BWT body path. Runtime measurements used the same 100-pawn save, harness path, screen geometry, and projected workload before and after the fixes. Pawn generation varies by generated pawn and the live roster may change while the game runs, so spawn evidence reports each observed count and separates vanilla generation from BWT recache and table-notification costs.

## Migration and compatibility policy

Each stateful domain owns current-record meaning and conversion. The game component coordinates Scribe without becoming the behavioral owner. Supported old workload shapes normalize into the current projection and ownership model before runtime planning. Empty collections do not select a schema generation. Compatibility imports use the canonical application plan and are unavailable in active multiplayer when no deterministic transaction protocol exists.

## Remaining condition

There is no remaining central-architecture source migration. The native/Harmony live-render fallback is the only documented edge condition: retire it when those non-BWT call sites can receive the same finished `WorkTabView`. A future paired runtime benchmark needs a repository-owned manifest and deterministic save fixture; that is test infrastructure, not a second state or mutation path.
