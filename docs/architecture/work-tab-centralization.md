# Work tab centralization

## Purpose

This record defines the ownership and dependency rules for Better Work Tab state. It tracks the migration until each major system has one state owner, one normal read path, and one normal command path.

The application boundary coordinates domain services. It does not own every domain rule or store all mutable state in one object.

## Required dependency direction

The target dependency direction is:

```text
UI, rules, workloads, APIs, multiplayer replay, compatibility adapters
  -> application commands and canonical reads
  -> domain services
  -> domain-owned persistence and external ports

domain change results
  -> work-tab view construction
  -> layout, rendering, hit testing, and tooltips
```

Harmony patches may use a small patch-safe facade when RimWorld does not provide an injection point. BWT-owned call chains receive explicit services or state views.

## Ownership matrix

| System | Current owner or convergence point | Target owner | Target read port | Target command owner | Persistence owner | Migration state |
| --- | --- | --- | --- | --- | --- | --- |
| Application core | Several gateways and runtime facades | Per-game composition and small application coordinator | Canonical effective-state reader | Explicit typed operations and domain plans | Domain repositories coordinated by the game component | Mapped; migration pending |
| Priority and authority | `WorkPrioritySystem`, `PriorityAuthorityBroker`, provider and mirror services | Authority-aware priority domain | Priority reads through the canonical state view | Priority command handler | Priority owner or external adapter | Mapped; migration pending |
| Schedules | `TimePriorityService`, editor, API, and integration paths | Schedule domain | Schedule read service | Schedule command handler | Schedule record owner | Mapped; migration pending |
| Specific jobs | `WorkGiverReassignmentManager` and UI collaborators | Specific-job domain | Override and inheritance reader | Specific-job command handler | Specific-job record owner | Mapped; migration pending |
| Execution order | Reassignment, layout, and patch paths | Execution-order service | Execution-order reader | Explicit display, execution, or coupled reorder commands | Execution-order record owner | Mapped; migration pending |
| Settings and presentation | Settings singleton, registry, contextual router, and workload projection | Global preference store plus presentation domain | Concrete pass snapshot through the existing effective-settings facade | Existing workload settings writer with result receipts | Each store owns its record | Mapped; migration pending |
| WorkGrid and layout | Snapshot, projection, invalidation, renderer, and layout services | Existing snapshot evolved into a pass-stable finished view plus UI-only frame state | Immutable pass view | Application operations for game state, UI commands for frame state | No game-state persistence | Mapped; migration pending |
| Rules | Classic apply paths and Rule Builder 2.0 apply service | Pure evaluators and command compiler | Canonical state view | Canonical command batches | Rule format owners and import adapters | Mapped; migration pending |
| Workloads | Backend, session, gateway, converter, and multiplayer callbacks | Template repository, capture service, planner, and patch projection | Canonical live or projected state view | Canonical command batches plus repository actions | Workload repository and converters | Mapped; migration pending |
| Persistence | Game component plus feature converters | Per-domain record owners | Current canonical models | Domain migration entry points | Game component coordinates Scribe only | Mapped; migration pending |
| Multiplayer | `MultiplayerBridge` and workload protocol paths | Command transport and transaction coordinator | Canonical state fingerprints and revisions | Local domain handlers through synchronized transport | Transport records only | Mapped; migration pending |
| External compatibility | Registry, mirror, and per-mod gateways | Small detection, authority, import, mirror, and coexistence ports | Core-facing compatibility reads | Canonical commands or narrow authority adapters | Integration-owned migration records | Mapped; migration pending |
| Harmony and Spine | Patch entry points and mirrored Spine code | Integration edge and standalone Spine owner | Patch-safe facade only where required | Normal application services when BWT owns the call chain | Existing owners | Mapped; migration pending |

## Application operations

The normal game-state boundary exposes explicit typed methods. It does not accept a marker interface, arbitrary object payload, or a heterogeneous command bus. Cross-domain work uses a typed aggregate plan only when a real caller needs atomic coordination.

Preview editing is not a game-state application operation. A projection editor applies the same domain intent vocabulary to session-local overlays without transport, persistence, mirroring, or live invalidation.

The first priority operations distinguish intent:

- set the priority displayed by a root cell, which may mean the current schedule hour;
- set the stored parent priority, which never implies schedule editing;
- apply a deterministic stored-parent batch.

Transport submission and local application are distinct results. A multiplayer client may return `Submitted`; only synchronized local replay can return `Applied`.

## Command transaction

An accepted game-state command follows this order unless a documented RimWorld boundary requires an exception:

1. Normalize and reject malformed or duplicate targets.
2. Capture the game/session epoch, expected domain revisions, authority, range, schedule, and reassignment state needed by the plan.
3. Validate and authorize the complete plan.
4. Enter a per-game non-reentrant execution guard.
5. Capture inverse values or domain journals.
6. Apply local mutations through non-synchronizing, non-publishing domain ports.
7. Revalidate the captured authority and relevant revisions.
8. Deliver external mirrors under the command's explicit failure policy.
9. Mark persistence dirty or write the repository action.
10. On atomic failure, roll back in reverse order before any normal revision or change publication.
11. If rollback fails, return recovery-required state and publish broad recovery invalidation.
12. On success, advance each affected domain revision once, advance the application revision once, publish one state change, and return the applied receipt.

Rejected, unchanged, submitted, and successfully rolled-back operations do not advance normal state revisions. Reentrant execution is rejected and never interleaves partial mutations. Until an external mirror provides reversible, idempotent, result-bearing delivery, mirror failure is applied-with-warning rather than a false atomic rollback claim.

## Read precedence

The production call paths establish the following precedence. Migration tests must preserve it unless a recorded behavior decision changes the rule.

| Read | Precedence |
| --- | --- |
| Parent priority | A projected `Set` or `Clear` is resolved before live state. External priority authority supplies its effective value directly. Under BWT authority, the pawn's stored parent priority is the fallback and a pinned current-hour parent schedule overrides it. A zero stored parent priority cannot be re-enabled by a schedule. |
| Specific-job priority | Pawn override, then global override unless a global tombstone suppresses it, then the supplied parent priority. The pawn-specific current-hour schedule overrides the resulting child priority, followed by the global child schedule. A zero parent or child base cannot be re-enabled by a child schedule. |
| Parent schedule | Projected clear suppresses the live schedule; projected pins override live pins. A pinned hour overrides fallback and a linked hour inherits it. An explicit pin equal to fallback remains distinct from a linked hour. |
| Specific-job schedule | Pawn-specific pinned hour, global pinned hour, then the already-resolved child fallback. Work-giver identity must use its current reassigned WorkType rather than the native or formerly assigned WorkType. |
| Presentation | Workload-owned projected intent, then the live presentation owner, then the global preference. `Clear` releases workload ownership and restores the lower-precedence live value; it is not a default value. |
| Display order | Pawn-local order, then global order unless an explicit global clear applies, then remaining valid WorkGivers in native priority order. |
| Execution order | Effective parent priority, explicit execution tie-break order, and native priority. The current saved display sequence is also the execution tie-break; migration must expose this as an explicitly coupled legacy command before the two policies can be separated. |

Reads used by previews are observational. They must not trigger authority handoff, persistence, invalidation, or simulation cache mutation.

## Canonical identity vocabulary

Shared application and read contracts use workload-neutral identities:

- pawn identity;
- WorkType identity;
- WorkGiver identity;
- parent-priority target;
- schedule target with local or global scope and parent or specific-job kind;
- specific-job target;
- presentation key.

Workload keys remain inside the Workloads boundary and translate to these identities. RimWorld objects remain inside runtime adapters and domain handlers. Identity conversion must use the reassignment domain for a WorkGiver's current WorkType. A pawn identity is world-stable and must not gain a map component unless runtime evidence proves that RimWorld pawn IDs can collide across maps.

## State categories

The application distinguishes four kinds of state:

| Category | Examples | Revision policy |
| --- | --- | --- |
| Durable game state | priorities, schedules, specific-job overrides, execution order, workload templates | Advances the owning domain and application revisions after an accepted transaction |
| Durable user preference | global rendering and interaction preferences | Advances the preference revision; persists through the settings owner |
| Session projection | workload preview intents and baselines | Advances only the projection/session revision until committed |
| Transient UI state | hover, drag, animation, tutorial focus, open menu, viewport | Never advances durable application or simulation revisions |

Renderer invalidation counters are consumers of state changes, not authoritative state revisions.

## Result and change vocabulary

Operation results distinguish `Rejected`, `NoChange`, `Submitted`, `Applied`, `FailedRolledBack`, and `RecoveryRequired`. Only `Applied` publishes the normal committed change.

A state change carries:

- the before and after revision vectors;
- changed dimensions;
- a sorted, distinct list of dirty cell identities;
- explicit execution-recache, layout, presentation, persistence, and mirror effects;
- a broad-scope marker for recovery or unknown native and foreign writes.

The target runtime is .NET Framework 4.7.2, so contracts use deterministic read-only lists rather than `IReadOnlySet<T>`.

## Composition lifetime

`BetterWorkTabMod` owns boot factories, Harmony installation, and compatibility registration. Per-game application state, revisions, persistence coordination, and disposal belong to `GameComponent_BWTWorldSettings`. BWT-controlled windows and render/input components receive explicit dependencies from that scope.

One narrowly named static bridge may resolve the current per-game priority operation for Harmony or native callbacks that provide no injection point. No generic `Get<T>`, mutable application singleton, or second ambient service locator is allowed.

No extraction may change precedence without a behavior change decision and regression coverage.

## Persistence policy

Each stateful domain owns its record meaning and migration. Load converts every supported old record into the current canonical model before runtime use. Runtime code does not infer a schema generation from a null or empty collection.

The top-level game component may coordinate Scribe calls. It does not own domain behavior. Unknown future schemas fail closed with a specific diagnostic when safe preservation is impossible.

## Temporary adapter policy

A temporary adapter must name:

- the old caller;
- the canonical capability it forwards to;
- the behavior test that proves parity;
- the phase that removes it.

An adapter is not complete until every old caller is inventoried. A permanent facade that exposes all previous paths is rejected.

## Net simplification gate

Centralization must reduce production code and ownership paths. It is not permission to keep the old implementation and add a second framework over it.

At the `ad01eed4` baseline, `Source` contains 443 C# files and 152,346 physical lines. The highest-cost areas are Workloads at 21,804 lines, WorkGrid at 12,816 lines, settings UI at 10,356 lines, Time Priority at 6,735 lines, specific-job reassignment at 5,704 lines, and rules at 4,796 lines.

For settings and presentation, the first accepted target is at least 337 fewer production lines: at least 252 from the effective presentation mechanism and its 120 fallback call spans, and at least 85 from the existing workload settings writer's duplicated apply, rollback, persistence, and invalidation scaffolding. This slice reuses the existing effective-settings facade and writer interface; it does not add a presentation-reader interface, a generic settings command service, or a second workload writer.

Every implementation batch must report:

- production files and physical lines added and removed;
- old mutation, read, invalidation, or ownership paths removed;
- temporary adapters added and retired;
- dependency-count movement for the affected boundary.

A migration batch does not merge when it only adds contracts, forwarding classes, or split files while leaving the replaced path intact. Shared types must have at least two real callers or be the smallest required boundary for the next deletion in the same coherent batch. Tests and evidence may grow; the completed production `Source` tree must be smaller than this baseline and materially simpler by call-path and dependency counts.

## Pass-stable view

The existing `WorkGridSnapshot` and geometry/context types evolve into the finished pass view. The migration does not add a parallel `WorkTabView` object graph.

For each IMGUI pass:

1. prepare layout and the input view;
2. route input against that view;
3. if a local operation is applied, patch or refresh the affected cells once;
4. render from the successor view;
5. if an operation is only submitted, retain the original view.

This preserves same-event local feedback without treating a multiplayer submission as an applied mutation.

## Performance boundaries

The migration must not add per-cell allocations, repeated reflection, repeated authority or schedule resolution, full-grid rebuilds for sparse changes, or repeated workload fingerprints during one immediate-mode event.

The finished view is frame-stable. Drawing and hit testing consume the same geometry and effective state. Hover, drag, animation, and tutorial activity do not advance persistent state revisions.

## Rejected designs

- One mutable application-state singleton.
- A global service locator for normal BWT code.
- An untyped command bus with object payloads.
- A marker-command dispatcher that grows a type switch for every feature.
- Shared application contracts that use workload-specific keys or intents.
- Duplicate live and projected policy implementations.
- Separate mutation handlers for single-player and multiplayer.
- Compatibility types in normal domain or WorkGrid code.
- File splits that preserve the same dependency graph.
- Parallel frameworks that leave the old implementation as the real owner.

## Verification contract

The final branch must pass the repository deterministic suites, both 1.6 build forms, package and production-surface checks, service and API contracts, Spine mirror verification, focused runtime scenarios, compatibility and multiplayer checks supported by the current harness, and the repository performance protocol for changed hot paths.
