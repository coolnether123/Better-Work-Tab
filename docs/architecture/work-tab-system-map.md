# Work tab system map

## Scope

This map records the production ownership, read paths, mutation paths, revisions, and migration boundaries at `Dev` commit `ad01eed4`. It is the evidence base for the centralization sequence; it is not a proposal to combine all state into one service.

The production tree contains 443 C# files. The shared effective-state surface is referenced throughout immediate-mode rendering and input, so changes to identity, reads, or invalidation must be staged behind adapters.

## Composition roots

Composition is currently distributed:

- `BetterWorkTabMod` loads settings and independently initializes priority providers, external integrations, rules, and column state.
- `MainTabWindow_BetterWork` constructs its workload preview controller, prepares each frame, and scopes the effective-state provider around the render and input pass.
- `WorkloadPreviewController` constructs the normal live effective-state adapter and provider even when no preview is active.
- `GameComponent_BWTWorldSettings` coordinates Scribe for columns, schedules, specific-job state, workload persistence, and migration.
- `MPCompat_BetterWorkTab` registers synchronized methods and the workload transaction protocol separately from the other composition paths.

The target composition root may coordinate these services, but each domain retains its policy and record ownership.

## System ownership

| Concern | Authoritative state | Current policy owner | Important consumers | Migration boundary |
| --- | --- | --- | --- | --- |
| Parent priority | `Pawn_WorkSettings.priorities` or the selected external priority store | `WorkPrioritySystem`, `PriorityAuthorityResolver`, `PriorityValueResolver` | WorkGrid, rules, schedules, execution, workloads, importers | Authority-aware priority read and command ports |
| Manual priority mode | `Find.PlaySettings.useWorkPriorities` | `WorkPrioritySystem` | WorkGrid, rules, workloads | Manual-mode command and state change |
| Priority range | Registered provider snapshot and selected provider | `PriorityRangePolicy`, provider registry and selector | Input cycle, validation, display | Observation separate from data authority |
| Parent and specific-job schedules | `GameComponent_BWTWorldSettings.TimePrioritySchedules` | `TimePriorityService` | WorkGrid, execution, rules, workloads, Fluffy adapter | Schedule store, reader, and commands |
| Specific-job overrides and reassignment | `WorkGiverReassignmentData` | `WorkGiverReassignmentManager` | WorkGrid, rules, execution, workloads, importers | Exact state reader and typed commands |
| Specific-job display order | Global and pawn-local order collections in `WorkGiverReassignmentData` | `WorkGiverReassignmentManager` | Drilldown, drag, rule target catalog | Display-order command |
| Specific-job execution tie-break | Currently the saved display sequence | `WorkExecutionOrder` plus reassignment manager | Pawn work selection | Explicit coupled legacy command, then separate execution policy |
| Parent column order | `GameComponent_BWTWorldSettings.ColumnCurrentOrder` and live `PawnTableDefOf.Work.columns` | `WorkColumnOrderManager` | Layout and execution order | Layout and execution-order commands with explicit effects |
| Global preferences | `BetterWorkTabSettings` | settings registry, settings UI, contextual router | Rendering, input, compatibility, workloads | Preference store and presentation reader |
| Workload-owned presentation | Workload state plus contextual settings transaction state | workload projection and `BWTWorkTabContextSettingsRouter` | WorkGrid, settings controls, commit | Presentation ownership/read/write port |
| Rules | Classic and Rule Builder 2 record stores | rule evaluators and apply services | WorkGrid UI and automation | Pure evaluation and command compilation |
| Workload templates and sessions | Workload V2 envelope, session, and local MP profile | workload backend, converter, repository paths | Footer, preview, commit, MP | Repository, capture, patch, diff, planner, and receipt services |
| Renderer invalidation | Static counters, category revisions, and sparse cells | `WorkTabInvalidationHub` | snapshot and renderer caches | Consumer of `WorkTabStateChange`; UI-only invalidation stays local |

## Mutation inventory

### Parent priority and manual mode

| Route | Current entry | Notes |
| --- | --- | --- |
| Root cell click and wheel | `Patch_WorkPriority_DoCell_Unified` to `WorkPriorityCommandGateway` | Gateway selects preview, scheduled-hour, or stored-parent mutation. Live synchronized calls currently report optimistic success. |
| Optimized input | WorkGrid interaction handlers to the same gateway or patch-safe route | Must retain identical gesture bounds and cell-availability policy. |
| Header and paint bulk | `AngledHeaderInteraction`, paint command | Header priority writes bypass the gateway and perform sequential partial application. |
| Classic rules | `WorkAssignmentRule`, `WorkAssignmentRuleset` | Direct priority writes; ruleset also changes manual mode. |
| Rule Builder 2 | `RuleBuilder2ApplyService` | Direct manual, parent, schedule, and specific-job writes. |
| Workloads | legacy workload objects and `Workload2Backend.ApplyLive` | V2 has the strongest baseline, inverse, receipt, and rollback behavior, but owns a parallel cross-domain transaction. |
| API and import | external priority import and schedule audit paths | Import uses mirror suspension and raw BWT-store writes; it is not all-or-nothing. |
| Compatibility | Fluffy and Sleek handoff, import, and mirror adapters | Authority transition and mirror reentrancy must remain isolated. |
| Multiplayer | `[SyncMethod]` priority and manual entry points | Normal synchronized methods are separate from the workload transaction protocol. |
| Harmony compatibility | `Patch_Pawn_WorkSettings_SetPriority` | Reactive catch-all clamps and invalidates after native or external writes; it is not a normal command boundary. |

### Schedules

Schedule writes originate in `TimePriorityScheduleEditor`, root scheduled-cell input, Fluffy schedule assignment, Rule Builder 2, Workloads V2, external import, synchronized methods, and load or audit reconciliation. The standard service owns cache and mirror behavior, but target availability and transaction guarantees differ by caller.

### Specific jobs and order

Specific-job writes originate in priority boxes, angled headers, sub-work drag handlers, layout history, Rule Builder 2, Workloads V2, external import, Sleek handoff, and synchronized methods. Ordinary UI writes and workload writes use different atomicity and tombstone contracts.

### Settings and presentation

Global settings are written by the settings UI, contextual settings, compatibility modes, import/reset actions, tutorials, layout services, rule editors, and workload commit. There are 346 direct `BetterWorkTabMod.Settings` references in 94 source files. The settings object has no monotonic semantic revision, change receipt, or complete change event; renderer invalidation currently doubles as the closest settings stamp.

The current settings object combines distinct categories:

- game-facing configuration, including priority policy, workload mode, scheduler enablement, compatibility ownership, and saved rule selection;
- presentation preferences, including layout, headers, columns, drag, dividers, colors, and display behavior;
- durable UX history, including tutorials, viewed settings, prompts, and player identity;
- migration-only and compatibility fields;
- runtime-only read-only diagnostics and static window state.

Workload-owned presentation uses a second snapshot/apply/rollback transaction inside `BWTWorkTabContextSettingsRouter`. Metadata names 80 presentation IDs, but only 14 scalar IDs are stageable. The other 66 are not silently workload-owned; the presentation contract must explicitly classify them as global-only or add a supported typed projection.

The first settings slice replaces the fallback-heavy preview mechanism with one concrete immutable presentation snapshot and keeps the current static effective-settings facade as the narrow caller surface. The 120 bool, integer, and color reads become fallback-free lookups for registry-backed IDs. The slice removes the old preview snapshot, controller interface, gateway adapter, duplicated before/after collections, and inline singleton fallbacks; it does not add a second reader abstraction.

The existing workload settings writer gains a concrete mutation receipt that reports success, persistence, compensation, changed keys, and failure reason. One execute-and-compensate path replaces duplicated apply, rollback, write, lease, and invalidation scaffolding. Ordinary settings pages, imports, migrations, and compatibility writes remain outside this 14-setting transaction until a later deletion justifies moving them.

Settings import currently reflectively copies every public instance field before writing. A write failure does not restore the full in-memory state. The import contract must declare its scope and journal every included field rather than inheriting scope from reflection.

### Rules

Classic rules and Rule Builder 2 share an authority/preview gate but retain separate live mutation loops. Both enable manual priorities and call priority, schedule, and specific-job writers directly. A mid-sequence failure can leave earlier world changes in place; Rule Builder 2 can return a zero changed count after a parent write succeeded and a later schedule write failed.

Rules migrate to pure evaluation and deterministic command compilation. The application receipt, not a changed-count sentinel, states whether the batch committed, rolled back, or completed partially under an explicitly retained compatibility policy. The existing classic and V2 models, editors, and lossy translators remain input and persistence adapters.

### Column and execution order

Column drag and reset mutate both persisted order and live pawn-table columns. Specific-job drag mutates saved display order, which also changes the execution tie-break. These are game-affecting commands even when initiated by layout UI.

### Workloads, persistence, and multiplayer

Workload-owned template, session, intent, and projection state is distinct from normal Work Tab state. The current implementation concentrates several responsibilities in four large files: `Workload2Backend.cs` at 9,658 lines, `MultiplayerBridge.cs` at 4,129 lines, `WorkloadGateway.cs` at 3,749 lines, and `WorkGiverReassignmentManager.cs` at 3,168 lines.

Workloads V2 runtime results cross into UI code as structured facts. Operation results, validation issues, descriptors, multiplayer status, and commit reports contain codes, identities, paths, values, and state. `WorkloadPresentationResolver` maps those facts to localized messages and tooltips. Runtime exceptions, persistence diagnostics, and multiplayer protocol details remain technical data and never become player-facing text.

The current V2 apply path performs strong validation and rollback, but it is oversized. Its order is:

1. validate session, source identity, decision, plan, typed state, repository identity, and multiplayer capability;
2. capture and revalidate the runtime baseline and scope;
3. stage the repository mutation;
4. apply live specific-job, manual-mode, parent-priority, schedule, and settings changes;
5. revalidate authority and specific-job revisions;
6. compare-and-swap persistence after the live mutation;
7. publish invalidation and a persistence or recovery receipt;
8. on failure, restore only state for which the exact revision and fingerprint are still owned.

The application migration reuses these guarantees but removes normal domain mutation, invalidation, and persistence coordination from the workload backend. The workload boundary keeps only template/session/intent/schema, pure patch/diff/plan logic, repository identity, and workload-specific recovery metadata.

Workload capture is currently split among backend capture, UI completion, and controller baseline extension. One complete capture service replaces all three implementations; it does not wrap them. Global capture stays outside pawn membership iteration, and schedule ownership is acquired only when a schedule is actually captured.

Template CRUD also bypasses the revisioned commit path. Select, save, rename, and delete move to one workload-owned repository operation that advances revision/fingerprint state and publishes the required workload change. Template IDs and persistence receipts remain outside normal application contracts.

Single-player stores the workload envelope in world persistence. Multiplayer stores it in a per-player local profile. Prepare currently requires every peer to find the same source template ID and fingerprint locally; a missing or divergent peer profile rejects the transaction. The protocol must make profile parity or template distribution explicit before it can be treated as a reliable synchronized repository operation.

## Priority and schedule precedence

1. Resolve a coherent authority snapshot.
2. If an external store owns priority data, read its effective value and do not apply BWT schedules.
3. Under BWT authority, read the stored parent priority.
4. Apply a pinned parent schedule for the pawn's local hour; linked hours inherit the stored value.
5. Resolve a specific-job pawn override, then global override unless a global clear applies, then the effective parent priority.
6. Apply a pawn-specific WorkGiver schedule, then a global WorkGiver schedule.
7. A zero base priority cannot be re-enabled by either schedule layer.

Projected `Set` and `Clear` values are resolved ahead of the corresponding live layer. A projected read is observational and must not initiate an authority transition.

## Specific-job state semantics

Priority and order have three exact global states: absent, set, and clear. A local clear currently removes the local value and restores inheritance; there is no durable local tombstone. Preview can express a local projected clear, which is a parity risk until the durable contract makes the intended meaning explicit.

Display sequence resolution is:

1. pawn-local order;
2. global order unless a global clear applies;
3. remaining valid WorkGivers in native priority order.

Execution sorts by effective child priority and uses display index as the tie-break. The architecture exposes that coupling explicitly before any migration separates it.

## Revision inventory

The current application has several overlapping revision systems:

- `WorkTabInvalidationHub` renderer counters, category revisions, dirty flags, and sparse cell dirtiness;
- `WorkTabEffectiveStateRevisionVector` provider, source, session, persistence, authority, schedule, specific-job, settings, and membership fields;
- priority authority and external registry generations;
- `TimePriorityService.CurrentVersion`;
- `WorkGiverReassignmentManager.CurrentSyncVersion`;
- `ColumnOrderGeneration`;
- workload session, persistence, repository, and multiplayer revisions.

The target keeps independent concurrency components but removes renderer invalidation as a source of durable truth. Accepted application transactions advance affected domain revisions once and return the combined vector in one state change. Hover, drag, animation, tutorial, viewport, and window activity retain UI-only revisions.

## Workload dependency boundary

Normal BWT features do not import Workload feature or UI types. Workloads consumes
the same neutral seams available to other feature clients:

- `WorkTabEffectiveStateRuntime` and the typed WorkGrid preview contracts provide
  pass-stable reads and preview edits without exposing Workload sessions, keys,
  payloads, or persistence types;
- `WorkTabDomainPorts` provides exact priority, schedule, and specific-job capture
  plus optimistic validation while keeping concrete managers behind application
  adapters;
- `WorkTabStagedMutation` is the shared live mutation vocabulary and exposes only
  application-owned values and an opaque rollback receipt;
- presentation preview uses the neutral presentation value, intent, ownership, and
  revision contracts; and
- `WorkTabGameRoot` is the per-game runtime boundary. The historical
  `GameComponent_BWTWorldSettings` remains a save-compatible composition and
  Scribe shell rather than a service locator for normal domains.

Workload-owned UI and startup composition may register these adapters. WorkGrid,
rules, settings, application contracts, and domain behavior do not depend back on
Workload-specific types. A deterministic isolation suite rejects regression of
these dependency edges.

## Confirmed defects and parity risks

- Schema-2 workload persistence initializes an absent typed presentation-intent member as an empty list. Conversion then treats that empty list as authoritative and discards scalar-only presentation data. Migration must preserve absent versus explicitly empty typed members and promote legacy scalars exactly once.
- Reassignment cleanup removes a WorkGiver name from the wrong level of pawn order dictionaries, leaving stale names inside persisted lists.
- Reassignment does not migrate schedules, rule schedule targets, or workload keys from the former WorkType identity.
- Some schedule and specific-job writers do not apply the same unavailable-target validation as root parent-priority input.
- The Harmony postfix for native priority writes can shadow-write BWT storage while an external store owns priority data.
- Full-day Fluffy schedule assignment can commit a parent priority before a later schedule clear fails.
- Classic and V2 rules can report failure after retaining earlier world mutations.
- Layout-history application can write a global order while an existing global clear still shadows it.
- A source-contract test expects footer action dispatch inside draw methods even though production now separates drawing from event handling.

Each item requires a reproducing test before its policy changes.

## First vertical slices

The first write slice covers displayed parent-priority input and synchronized replay. It migrates every current production caller of the old `SetPriorityCommand` so the obsolete command type, its unused step/toggle/paint siblings, the corresponding gateway overloads, the private parent mutation path, and the obsolete synchronized wrapper can be deleted in the same coherent change.

Displayed parent intent remains distinct from stored parent intent:

- a scheduled live root cell pins the current hour and leaves the stored base unchanged;
- an unscheduled live root cell changes the stored parent priority;
- preview edits only the projection and never enters the game-state application;
- classic rules continue to target stored parent priority and migrate later as a complete deterministic ruleset batch.

The first read slice migrates every parent-priority observation through one workload-neutral resolver fed by an exact live source plus an optional projection overlay. The slice is complete only when the duplicated parent-priority members and policy can be removed from the old effective-state contracts, live adapter/provider, runtime, and projected provider.

The first view slice evolves `WorkGridSnapshot` into the pass-stable view and deletes the redundant presentation-access or render-context surface it replaces. It does not add a wrapper that leaves both object graphs alive.

The first workload slice repairs the schema-2 presentation loss and consolidates complete live capture. It must delete the backend, gateway, and controller capture loops in the same change. A new persistence schema preserves typed-member presence; absent scalar-only records are promoted, while explicitly empty typed state remains authoritative.

## Performance constraints

- Build a frame-stable state view once per frame or accepted revision, not per cell.
- Do not scan pawns, resolve external providers, reflect, normalize 24-hour arrays, or compute workload fingerprints in cell drawing.
- Preserve sparse priority dirtiness and batched schedule or specific-job notifications.
- Publish one state change per accepted bulk transaction.
- Keep exact authority capture and revalidation around the mutation and mirror boundary.
- Cache execution plans by domain revision and avoid LINQ allocation inside pawn work-giver rebuilds.

## Subtraction sequence

The highest-value deletions are ordered as follows:

1. Replace three workload capture implementations with one complete capture service.
2. Replace the workload-shaped WorkGrid read contracts rather than wrapping them; remove the legacy scalar provider/editor surface and extension glue as callers migrate.
3. Move direct workload template CRUD behind one revisioned repository and delete direct `Store.Records` edits and parallel notification paths.
4. Introduce the normal live mutation executor only when its batch removes or materially shrinks workload `ApplyLive`, persistence, rollback, and invalidation coordination.
5. Migrate all five current parent `SetPriorityCommand` call sites in one write slice, then delete the obsolete parent command types, parent gateway overloads, private parent mutation helper, and superseded synchronized wrapper.
6. Evolve `WorkGridSnapshot` in place and remove redundant presentation-access or render-context data instead of adding a second view graph.
7. Replace the effective presentation mechanism and fallback call spans with one concrete snapshot, deleting at least 252 production lines and at least 53 inline settings-singleton dependencies.
8. Collapse the existing workload settings writer's duplicated apply and rollback scaffolding around one receipt-bearing execute-and-compensate path, deleting at least 85 production lines.

Rules do not receive a common compiler or transport layer yet. That extraction is allowed only when it removes all relevant classic and Rule Builder application regions, preserves sequential and random semantics, and produces a net reduction of at least 100 production lines in the same batch.

Unrelated cleanup cannot offset an additive architecture slice. Each slice is measured against its own affected-source baseline.
