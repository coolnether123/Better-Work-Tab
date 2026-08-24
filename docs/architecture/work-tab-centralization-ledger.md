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
| Application | Per-game domain services | Canonical effective-state providers | `WorkTabApplication` and typed atomic plans | Game component composes domain owners | Complete |
| Priority | Authority broker and priority policy | `ParentPriorityRead` and finished view | Displayed or stored priority batches | External providers remain narrow authority ports | Complete |
| Schedules | `TimePriorityScheduleRuntime` and service | Immutable schedule values | Application schedule commands | Game component records; mirror and synchronized replay use commands | Complete |
| Specific jobs | Reassignment domain | Canonical override and inheritance reader | One specific-job batch in `WorkTabMutationScope` | Exact rollback and stale-data cleanup remain domain-owned | Complete |
| Display and execution order | Column-order and reassignment domains | Effective order readers | Explicit reorder or staged layout command | Layout retarget includes schedule compensation | Complete |
| Settings and presentation | Global preference owner and workload projection | Pass-stable settings snapshot | Context router or receipt-bearing workload writer | Preview port is session-local; accepted writes persist once | Complete |
| WorkGrid | Snapshot provider and `WorkTabView` | Finished immutable view | Input routes application commands | Renderer owns no game state | Complete |
| Rules | Classic and V2 evaluators | Canonical state view | `RuleApplicationPlanningScope` compiles one atomic plan | Old rule records translate at the boundary | Complete |
| Workloads | Repository, session projection, and planner | Canonical live or projected state | One atomic plan plus one repository action | Legacy and V2 backends share the same application transaction | Complete |
| Multiplayer | Transport layer | Fingerprints and expected revisions | Synchronized entry points call the same local handlers | Submission remains distinct from application | Complete at repository-harness level |
| Compatibility | Per-integration narrow gateways | Core-facing authority and state reads | Canonical command or trusted import ports | Fail-closed behavior retained | Complete at repository-contract level |
| Harmony and Spine | Patch and standalone integration edges | Patch-safe facade only when injection is unavailable | BWT-owned chains use explicit state views | Embedded and external Spine remain separate owners | Complete |

## Central transaction and revision result

`WorkTabAtomicMutationPlan` is the shared plan vocabulary for priorities, schedules, specific jobs, execution order, and presentation-affecting workload operations. `WorkTabMutationScope` validates the whole request, captures rollback state, applies each participating domain without publishing intermediate changes, restores in reverse order on failure, and commits one coherent application result. Rejected, unchanged, submitted, and successfully rolled-back requests do not publish a normal committed revision.

The legacy workload path now compiles the same atomic plan as the V2 path. Classic rules and Rule Builder 2 compile the same plan vocabulary. Root-header priority changes use one displayed-priority batch. Full-day schedules and specific-job changes use one batch instead of per-cell or per-job loops. Layout move, undo, and redo stage order and reassignment data, own their schedule retarget batch, and advance history only after the application transaction commits.

## Finished view and input

`WorkTabView` is the pass envelope for geometry, effective state, settings, and prepared snapshot data. Input and drawing use the same view. The optimized BWT path receives prepared specific-job `CellPresentation` values from its snapshot. No concrete workload type is part of the shared WorkGrid contracts, and the BWT-owned body renderer does not resolve live workload state while drawing. Native and Harmony fallback drawing retains one documented live edge until those call sites can receive a finished BWT view.

Workload drafts use a shared bounded history implementation. `Ctrl+Z` and `Ctrl+Y` or `Ctrl+Shift+Z` undo and redo accepted draft edits. Canceling a dirty preview retains its projected state and both history stacks; the next `Ctrl+Z` restores the canceled workload with the unsaved changes. Restoration remains preview-only and does not mutate live game state.

## Removed duplicate paths

- Manager-owned single priority and specific-job write sequences.
- Per-rule and per-workload mutation, invalidation, and rollback loops.
- The separate multiplayer column-order synchronizer.
- Duplicate WorkGrid inspection semantics and effective-state revision shim.
- Header-positioning, tutorial-geometry, settings-visibility, and naming shells whose behavior already had another owner.
- Dead diagnostic formatting in production; the external probe formats its own diagnostics through the supported read API.
- Backend contracts and authorization forwarding that duplicated the application boundary.

No temporary mutation adapter remains. The two one-caller ports are intentional edges: the snapshot presentation layer separates prepared data from rendering, and the presentation preview port prevents session edits from reaching persistent settings. Remove either only when its edge disappears; do not add a second implementation merely to justify the abstraction.

## Simplification scorecard

| Measure | Mission baseline | Final working tree | Change |
| --- | ---: | ---: | ---: |
| Production C# files | 443 | 459 | +16 focused boundary files |
| Production physical lines | 152,346 | 151,998 | -348 |
| Production nonblank lines | Not recorded at mission start | 135,229 | Current measurement |

Relative to completion base `e5987800`, tracked production edits add 5,351 and remove 6,663 lines; new production files add 1,693 lines. The full mission remains physically smaller than its baseline while adding atomic rollback, finished views, canceled-draft recovery, and workload undo and redo. The final physical count is the controlling net-simplification measure because the completion base already contains the earlier centralized priority and schedule deletions.

## Final verification

| Check | Result | Evidence |
| --- | --- | --- |
| Production 1.6 build with embedded Spine | Passed, 0 errors | Existing duplicate Publicizer source warning only |
| In-repository deterministic suite | 15 passed, 0 failed | Local production suite |
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
| Live isolation after test | Passed | Session stopped cleanly; harness reported no blockers before launch |

The focused visible-column test is the repository's available hot-path gate for this change. It confirms prepared presentation, visible-column culling, and the absence of repeated live resolution in the BWT body path. A paired save benchmark was not available because the workspace has no compatible benchmark manifest and seed lane for this mod. No performance claim beyond the passing source gate and live geometry evidence is made.

## Migration and compatibility policy

Each stateful domain owns current-record meaning and conversion. The game component coordinates Scribe without becoming the behavioral owner. Supported old workload shapes normalize into the current projection and ownership model before runtime planning. Empty collections do not select a schema generation. Compatibility imports use the canonical application plan and are unavailable in active multiplayer when no deterministic transaction protocol exists.

## Remaining condition

There is no remaining central-architecture source migration. The native/Harmony live-render fallback is the only documented edge condition: retire it when those non-BWT call sites can receive the same finished `WorkTabView`. A future paired runtime benchmark needs a repository-owned manifest and deterministic save fixture; that is test infrastructure, not a second state or mutation path.
