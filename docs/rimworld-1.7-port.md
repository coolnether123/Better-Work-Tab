# RimWorld 1.7 port

This branch targets the Waylith build `1.7.4976 rev1043`, inspected October 12 and 13, 2026. It preserves BWT's `2.0.0` mod version and existing save keys. Both production modes build and pass the exercised live acceptance, including populated specific-job persistence, footer clearance and preview teardown. The save and footer sections retain their earlier candidate-specific receipts. Current frozen BWT packages are in `preview-reload-01/packages-01/packages`, with the normal-launch evidence described below. This is not a claim of compatibility with every mod, device or later unstable build.

## Required build changes

`Source/Better Work Tab.csproj` now uses the .NET SDK and `net10.0`. Supply the actual 1.7 game root as `RimWorldManagedDir`. Its assemblies are flat in the game directory. The former Unity `Managed` directory and Krafs 1.6 references do not describe this build.

Use the shared tooling `Invoke-RimWorldBuild.ps1` with configuration/version `1.7`, `Engine DotNet`, explicit BWT and standalone Spine roots, and the BWT build contract. The contract checks the shared source mirror before compilation. Build outputs and packages belong outside source.

`CopyLocalLockFileAssemblies` keeps `0MultiplayerAPI.dll` available for mod loading. Game assemblies and Harmony remain external references. Shipping this API does not establish Multiplayer compatibility.

`LoadFolders.xml` selects exactly one 1.7 payload. Without Spine it selects `1.7`; with package `CoolNether123.Spine` it selects `1.7/ExternalSpine`. The external variant must load the separately built 1.7 Spine package. Keep root `Assemblies` empty and never include `Spine.Transpilers.dll`.

## Rendering changes

| Change | Source reason | Acceptance needed |
| --- | --- | --- |
| Preserve shader blend/cull property values as integers | Waylith omits Unity's enum types, but materials accept integer properties | Outline color, alpha blending and retained-texture compositing |
| Remove `GL.sRGBWrite`, `GL.Viewport` and `GL.InvalidateState` mutations | The installed compatibility implementation reports unsupported setters/calls; target size follows `RenderTexture.active` | Correct rendering and state restoration after cache drawing |
| Bypass retained row caching for fractional device rectangles | `GUIUtility.AlignRectToDevice` is absent; shifting cached geometry would change pixel phase | UI scales, clipping, scrolling, drag transitions and cache use |
| Remove unused obsolete namespace imports | .NET 10 and the compatibility assemblies do not contain those namespaces | Both builds, then ordinary gameplay |

The port retains `RenderTexture.active`, GUI matrix/color and GL matrix restoration. Fractional row geometry uses the existing direct drawing path. It is not a claim that Waylith needs no alignment strategy.

## Specific-job save persistence

`BWT17-SAVE-001` loses populated global work-giver order, moved-header markers, pawn priority overrides, pawn ordering and explicit clear sets after loading. The serialization pattern exists unchanged in the pre-port baseline. A 1.6 runtime reproduction has not been performed, so the evidence does not establish a port-introduced regression.

`WorkGiverReassignmentData.ExposeData()` now retains transport lists on the exposable instance across `LoadingVars`, `ResolvingCrossRefs` and `PostLoadInit`. It rebuilds the authoritative dictionaries and sets during finalization, then releases the transport lists. The nested `PawnWorkGiverOrderRecord` follows the same contract. Saving also releases the temporary lists after writing. All XML keys and `SyncVersion` are unchanged. No migration is required.

The private `SaveScribeRegressions` suite uses the installed engine's actual Scribe loader. It checks populated collections, two independent instances, re-saving, empty legacy keys, absent legacy keys and revision defaults. Each frozen baseline mode fails 18 of 51 assertions. Each repaired candidate11 mode passes all 51, as do both separately compiled probe assemblies. Evidence is `save-regressions-01/verified-final-02/verification.json`. These offline results do not prove normal game initialization or fresh-process acceptance.

Candidate04 is retained as failure evidence, not an accepted distribution candidate. The historical candidate11 save-fix packages are under `save-fix-packages-01`; normal game save/load, visible readback and process restart receipts belong under `save-fix-live-01`. Current packages in `preview-reload-01/packages-01/packages` retain the save fix and add the preview-teardown correction. Preserve the original defect saves and test only disposable copies.

`save-fix-live-01/acceptance-02-verified.json` passes 164 structured checks. Each mode saves populated specific-job state, reloads it normally, then starts a separate process and saves the loaded state again. All eight outer XML fields match the disposable populated seed at each readback. Completed load generations, exact loaded BWT/harness hashes and four graceful session releases are verified. External readbacks also attest the loaded standalone Spine path and hash.

Both isolated probes ran inside the actual game and passed 51 assertions per mode with no engine gate. `save-fix-private-01/verification.json` passes 39 aggregate steps, including 280 deterministic tests, with no failures or skips. The synthetic legacy-empty dictionary fixture emits two engine messages about missing keys. Those messages do not come from a normal colony save; the run is not log-error-free.

Both normal-reload captures and the unobstructed fresh-process captures show Jim's Fix breakdown priority at 1 and Smooth walls with its yellow moved marker. All eight XML fields also match after both supplemental fresh-process readbacks. `save-fix-live-01/parent-visual-inspection.json` records the four inspected image hashes. The original fresh captures contain a tutorial dialog over the relevant cells and do not supply visual acceptance.

The supplemental External session released gracefully. The Embedded supplemental stop returned a coordination timeout after shutdown; its known manifest records completion with exit code 0 and `ShutdownRequested=true`. Subsequent canonical doctor found no active leases, unknown games or blockers, but the completed session was absent from authority discovery. Its final release receipt remains unavailable. Preserve the refusals and do not treat that timeout as proof that shutdown was never dispatched. Later exact-session cleanup checks do not reconstruct that historical receipt or invalidate the populated-save readbacks and four original successful releases.

Current source evidence binds 489 tracked source files to the preview-teardown candidates, with zero drift. The save fix adds 31 lines and removes 10 in one production file. It adds no shipping helper. All 13 distinct XML keys across 14 Scribe calls remain unchanged. Keep earlier candidate11 binaries with their save-specific receipts. Bind current frozen binary hashes and source hashes separately from later documentation commits rather than inferring byte-identical output from a rebuild.

## Engine reference

The local hash-pinned reference is `A:\Dev\RimWorld\Decompiled\1.7.4976-rev1043`. Its accepted managed receipt covers 238 assemblies as C# and IL, with zero failures or source drift. Start with `inventory.json` and `receipt.json`, then the relevant `source` file. Use IL when reconstructed C# obscures a signature or transpiler pattern.

The shared tooling guide `docs/waylith-1.7-reference.md` indexes mod loading, profile isolation, GUI conversion, GL, materials, capture and native analysis. Generated proprietary code stays local, outside Git. Native pseudocode is recovered analysis, not original source.

## Evidence and remaining checks

Fresh evidence is local under `A:\Dev\RimWorld\Deliverables\BWT-1.7-port`. `cleanup-private-08/verification.json` records 39 passed steps, zero failures or skips, including 280 deterministic tests, ten GUI-input contracts, eleven registry-refresh contracts, twelve runtime-driver contracts, six schedule-fixture contracts and ten priority-gesture contracts. `cleanup-embedded-04.json` and `cleanup-external-04.json` record both production builds and their 28-file Spine mirror checks. These are build and deterministic results, not full gameplay acceptance.

Candidate04 passes all 24 live footer cases across Embedded and External Spine at UI scales 1, 1.25 and 1.5. Each scale covers normal, schedule, Focus and ExpandBeside states. `footer-acceptance-04.json` records terminal results and screenshot hashes, with zero failed cases. Inspected 125% and 150% captures show the restored gap. BWT commit `1460ec89` contains the five-file footer fix. Private test commit `dbc5b42` contains the unchanged 12-pixel clearance assertion. Both commits are pushed to their owning 1.7 branches.

Both modes pass the full terminal runtime suite in `runtime-embedded-10-terminal-result.txt` and `runtime-external-10-terminal-result.txt`, with harness09 and private08. Priority-cache completion, priority99 and specific-job smokes also pass. The suite exercises actual input ownership, settings/world Scribe round-trips, migration, tutorial/rule state and schedule fallback. Fresh-process readbacks in `acceptance-embedded-09-reload` and `acceptance-external-10-reload` restore the workload identity and Construction hour3 priority4, including its independent pinned-hour mask.

`core-acceptance-10.json` binds both terminal results, the 24 footer cases, package file hashes, persistence readbacks and inspected captures. Fresh candidate05 builds are not byte-identical to live-tested candidate04. SourceLink now names the footer commit and each build has its own symbol path. `rebuild-equivalence-05.json` records identical assembly metadata, IL and resources in both modes after a Cecil rewrite omits debug records and zeros MVID/timestamp. These earlier passes did not cover populated specific-job persistence. Candidate04 is rejected for distribution after BWT17-SAVE-001; use the repaired candidate only after its own acceptance gates pass.

The tooling-owned `waylith-preserved-runtime` is available/verified for normal Runtime admission on this pinned host. Current canonical `rwa.cmd doctor -Version 1.7` uses the default host configuration and does not require `-HostConfigPath` or `-RuntimeValidation`. Keep `-Version 1.7` explicit because the default remains 1.6. The earlier `cleanup-live-packages-09/host.json` and harness09 package identify historical core acceptance only. Current normal launch and package bindings appear in the final grid section. Runtime admission does not establish performance support.

Earlier failures exposed private fixture defects: a late frame-counter baseline, a consumed debug request that could not resume, overlapping authority/UI fixtures, assumptions that seeded schedules were empty, mapless disposable-game UI initialization, and an assertion requiring a diagnostic with no recorder caller. The corrections retain the behavior assertions, isolate and restore the seed, and observe the real gesture-matcher result inside the priority handler. The new `PriorityGestureProbe` has one runtime installation caller. Retain it as a private instrumentation boundary; it adds no shipping diagnostic or gameplay hook.

Live startup reports retained row composition unavailable and uses the existing direct clipped-rendering fallback. The exercised Work tab renders and accepts input through that path. Retained-cache performance and pixel equivalence are not established by these results. Waylith also warns that `hideFlags` is a stub. Do not describe this port as warning-free or claim a performance improvement.

Both modes have separate evidence for schedule gestures, Construction column reorder, workload preview isolation/cancel/apply, settings rendering and Rule Builder Construction selection. External evidence uses the `acceptance-external-10` prefixes. Its schedule readbacks show the independent-hour link toggle and a numeric change from4 to3. The inspected drag screenshot places Construction before Mining. Preview receipts require `livePriorityChanged=false`; Apply publishes `Applied` and the subsequent priority signature changes. The fixture workload reports skipped entries and rejects specific-job-order edits it does not store. Those diagnostics remain visible; this is not proof that every workload field applied.

The external fixture must load Spine before BWT, as `About/About.xml` already declares. The launch recipe now supplies that order and waits for the requested save's completed load state. The earlier BWT-before-Spine lane had type-load failures and is retained as a failed setup arm, not passing compatibility evidence.

Launch only through the canonical RimWorld Agent after `rwa.cmd doctor -Version 1.7` admits the exact preserved runtime. Verify Work-tab ownership, extended priorities, specific jobs, schedules, workload/rule persistence, settings, tutorial, rendering at multiple UI scales, and fresh-process reload in both Spine modes. Treat third-party mods, Multiplayer and performance as separate acceptance arms.

## Recommendations

Keep engine-specific rendering decisions in BWT's existing drawing boundary. Retain the direct fallback until screenshots and live geometry prove a 1.7 device-alignment replacement. Do not spread guessed Unity behavior through the UI.

Keep the asset-excluded Krafs reference until MultiplayerAPI drops its transitive 1.6 reference. Removing that exclusion reintroduces incompatible game assets. The actual compile references remain the pinned 1.7 assemblies.

Keep gameplay and Scribe contracts unchanged unless runtime evidence requires a change. Engine replacement alone is not a reason to rewrite priority ownership, schedules or rule persistence.

Re-run API, Harmony and rendered acceptance whenever the unstable game build or input hashes change. A passing compile against this build is not forward compatibility with the next unstable build.

## Lifecycle and architecture cleanup

These findings come from the installed `1.7.4976 rev1043` reference and BWT `Support/1.7`, with the port's owned changes. Footer fix commit is `1460ec89`. Updated October 12, 2026 at 8:00 PM CDT. The ownership and input cleanup below is implemented. Both footer matrices, both terminal runtime suites and the exercised feature acceptance pass.

### Keep the existing game lifecycle

The decompiled `Assembly-CSharp.dll/Verse/Game.cs` calls `GameComponentUtility.FinalizeInit()` during both new-game initialization and loading. `StartedNewGame()` or `LoadedGame()` follows the corresponding initialization. `Verse/GameComponent.cs` still supplies update, tick, GUI and Scribe callbacks. It supplies no component disposal callback.

BWT already delegates its save component's `FinalizeInit()` to `WorkTabGameLifecycle`. That service belongs to the per-game `WorkTabGameRoot`. Retain this ownership and the existing Scribe keys. A second lifecycle framework would duplicate working boundaries.

`GameComponent_BWTWorldSettings` attaches `WorkTabGameRoots` and `WorkloadWorldStates` in its constructor. Each registry now uses a game-keyed `ConditionalWeakTable`. Constructing or replacing another game's component no longer evicts the current game's owner. The game keeps its owner alive, and an unreachable game-owner cycle can collect. Null lookup remains optional; attachment requires an owning game. Save keys and the composition root are unchanged.

Seven synthetic ownership checks pass. The same tests against the untouched baseline fail five cases. The Scribe fixture now constructs separate disposable games and checks that their owners and the live game's owners coexist. It no longer repairs private registration fields. Both terminal runtime suites now pass the actual component-construction and Scribe checks.

Do not remove teardown patches on the assumption that the new engine disposes components. `Game.Dispose()` disposes maps and the world, not game components. `Verse/Profile/MemoryUtility.cs` still clears map/world fields and cached tab windows. BWT must release retained surfaces before those references disappear. Its existing teardown coordinator already gives the window's resource owners independent release attempts.

### Isolate engine drawing and input

`UnityStubs.dll/UnityEngine/GUIUtility.cs` forwards public GUI/screen conversions to the current GUI backend. The pinned `RimWorld.dll/RimWorldWaylith/Imgui/ImGuiGuiBackend.cs` applies the group origin outside the matrix in `GuiToScreenPoint`, but inside it in the actual drawing method `ToScreen`. `ScreenToGuiPoint` has the corresponding inverse-order difference from `ToGui`, which updates event positions. They agree at identity scale and differ in scaled or rotated groups. Live harness gestures reproduced the mismatch at scales 1.25 and 1.5.

`GuiMousePosition` now reads the current group's content origin through the public backend `GetGroup` API. Forward conversion applies `GUI.matrix` to the local point plus origin. Reverse conversion inverts the matrix first, then subtracts the origin. This matches actual drawing and event coordinates in the pinned build. Recheck the engine methods when upgrading rather than retaining this workaround unconditionally.

Device reads use the game's top-left client pixels, without the former bottom-left flip or foreground-window lookup. Sub-work gestures capture their return point at mouse-down. Optional cursor restoration remains in `NativeCursorPosition`; it rejects an unfocused game, cancels on focus loss and verifies the foreground window belongs to the game process before moving the OS cursor. Its method and field names now state the client-coordinate contract.

Ten synthetic input checks pass, covering scale/group conversion, rotated and translated groups, inverse conversion, root groups, event versus device position, top-left coordinates, missing device and focus rejection/cancellation. `ToGuiPosition` has one calling method, `NativeCursorPosition.DrawPendingMoveCue`, which converts both cue endpoints. Retain it as the inverse-coordinate boundary rather than duplicating the transform there. Semantic caller discovery is partial because the query did not load the shared build's complete reference set; source inspection and the actual builds establish this boundary.

The harness's 1.7 adapter uses the same group-origin and matrix ordering. Waylith's `ImGuiGuiBackend.UpdateEventMouse` recalculates pointer coordinates and hit clipping on group/transform changes. During an owned injected Work-window event, the adapter supplies its client point to that calculation and restores the physical pointer field in a finalizer. This changes no OS input. Scaled schedule-opening gestures pass both footer matrices. Schedule edits, column reorder, workload preview/cancel/apply, settings and Rule Builder selection have separate readbacks in both Spine modes.

### Preserve the footer reservation

The inline schedule previously reclaimed 18 logical pixels from the footer. In the three-pawn live fixture this left about 3 pixels before the workload buttons. `WorkGridLayoutMetrics`, `WorkTabViewportController` and `WorkTabHeaderRenderer` now reserve the full footer space. Schedule rows add to the grid height instead of borrowing from the controls. The obsolete reclaim calculation and frame fields are removed.

Verse rounds the window rectangle before drawing. Candidate03 left roughly 11.1 to 11.2 logical pixels in some fractional-height cases and failed the unchanged geometry assertion. `WorkTabWindowSizingController` now rounds requested height upward with `Mathf.Ceil`, preserving the reserved space through window rounding.

The private live geometry validator requires 12 logical pixels between the last row and footer in the three-pawn fixture, with its existing 0.51-pixel tolerance. All 24 candidate04 cases pass across both Spine modes, with screenshots inspected. No save keys or user settings change.

Keep unsupported GL calls and device-alignment decisions inside the existing prepared/retained rendering owners. The direct rendering fallback remains necessary until an alternative proves clipping and pixel alignment. `GL.sRGBWrite`, `Viewport` and `InvalidateState` are stubs in this build; removing their mutations is already part of the port.

### Use the modern runtime selectively

The 1.7 project targets `net10.0` and the actual preserved Waylith assemblies. Keep the explicit `Krafs.Rimworld.Ref` dependency with `ExcludeAssets="all"`. MultiplayerAPI 0.6 transitively resolves its 1.6 reference package; removing the exclusion imported 106 incompatible compile assets and reproduced 2,072 compiler errors. The exclusion is a dependency constraint, not a game reference. `Krafs.Publicizer` remains required for production member access. Do not remove shared legacy adapters merely because this branch uses a newer runtime.

BWT already has immutable drawing snapshots and targeted invalidation through `WorkTabInvalidationHub`. Extend those owners when a measured issue requires it, rather than adding a second event bus or rebuilding the whole grid each frame. .NET 10 does not establish that Verse objects, global Scribe state or GUI resources are safe to access from background threads. Keep gameplay mutations and resource work on their established game/GUI boundaries.

The private 1.7 aggregate includes the cleanup regressions and rejects Unity-era references in compiled BWT and probe assemblies. Current build receipts are `cleanup-embedded-04.json`, `cleanup-external-04.json` and `owned-harness-09.json`. Both BWT builds passed the 28-file Spine mirror check. Rendering optimization and threading changes require separate measured evidence.

## Code change size

Compared with BWT baseline `55195cad5b478dece556d006745ddbbe445fdb86`, the current port changes 28 files under `Source`, adding 288 lines and removing 365. This count includes the project manifest, transpiler profiles, footer fix, architecture cleanup, specific-job save fix and preview teardown. It excludes documentation, metadata, external tests, standalone Spine and the harness. Line changes measure the patch, not feature completeness. Priority rules, schedule save keys and workload semantics remain unchanged.

### Release preview ownership at game teardown

Fresh history testing found a second production defect after the save fix. An active `WorkloadPreviewController.Current` survived normal reload. A new window could not claim the preview, old queued lifecycle actions waited on an undrawn window, and the new window's priority click could change live priorities instead of its draft.

`GameCacheResetUtility.Reset` now calls `WorkloadPreviewController.ResetForGameTeardown`. That boundary releases `Current`, the commit-status subscription, queued actions and local preview state. Same-game window replacement still preserves an active preview. The change adds 29 lines in two production files, preserves every save key and adds no lifecycle framework.

Both frozen baseline modes fail six of seven compiled production-boundary regressions. Both replacements pass seven of seven, including new-owner draft editing and old canceled-draft rejection. These console checks use disposable synthetic state. `compatibility-final-01/checkbox-embedded-05-active-reload/acceptance.json` and `checkbox-external-05-active-reload/acceptance.json` separately pass five actual normal-reload checks per mode. New-window preview clicks leave live priorities unchanged. Both `checkbox-*-05-history/acceptance.json` receipts pass 12 supported workload-draft and specific-job history checks. Ordinary parent-cell edits do not belong to that undo contract.

The new reset helper has one caller. Retain it as the explicit game-transition boundary; same-game ownership must use a different contract. Semantic caller discovery remains partial because the project query lacked the shared build references. Current source and successful builds establish the actual method-group binding.

## Normal-launch grid verification

October 13, 2026, at 10:23 AM CDT, Embedded08 and External09 each pass 12 final grid checks on actual 100 and 300 pawn colonies at UI scales 1 and 1.5. Each mode retains 20 independently settled Shift-wheel events. All events preserve priorities and horizontal position, move vertically from 0 to 1200, and correlate with the canonical release receipt. Four captures per mode were inspected. The corrected setup verifies free-colonist population and the drawn row count before labeling a case. The earlier Embedded06 100-pawn claim was rejected because its inherited save actually contained 300 pawns.

The default canonical harness route admits the pinned runtime without `-HostConfigPath` or `-RuntimeValidation`. The host default remains 1.6, so use explicit `-Version 1.7`. These launches use frozen harness `5995BD3D`, BWT Embedded `2C49BD4E` or External `C37501C8`, and External Spine `A40172AD`. Exact hashes and receipts are under `A:/Dev/RimWorld/Deliverables/BWT-1.7-port/normal-final-01`. External09 additionally verifies all four loaded assembly hashes, serialized Spine/BWT/probe order and zero ERROR/FATAL log lines. Both sessions released normally with exit0 and no forced termination.

The private probe correction emits Publicizer runtime access permissions explicitly because its project disables generated assembly metadata. Both probes now load all 198 private types against the original shipping DLLs. The loader gate, 19 observation checks and 718 package-owner checks pass per mode. Original probe failures remain preserved. This changes no BWT or Spine code.

Embedded08 used the earlier wheel probe; External09 used the repaired probe. Their grid passes did not establish live acceptance of both repaired probes with the corrected harness. External09 also exposed an independent harness response defect during cold startup: a `status` request received the automatic Work-tab capture response. Its original receipt remains retained.

## Final harness and both-mode acceptance

The harness source correction is committed and pushed as `205db1d763482e093c13bbf00ca96134f97716ec`. The sender negotiates `request-id-v1` from `ready.txt` and accepts only its request-specific reply. Automatic capture, deferred commands, visual watches, load completion and latency results use separate receipts. Older payloads retain their legacy fallback, without the stronger correlation claim. The integrated canonical bridge preserves its existing `load-completion.result` contract and additional source changes.

The parent rebuilt canonical 1.7 and 1.6, passed 39 correlation assertions, 424 input assertions plus 21 scroll-evidence assertions, 79 extension cases, timing, both lifecycle suites and CLI checks. Integration preserved 186 unrelated inputs. Only the canonical catalog's 1.7 harness binding changed; all 19 catalog entries and the host default 1.6 remain. The frozen harness is `normal-final-packages-03/RimWorldAgent`, SHA-256 `3B1755254361ED9821D2E9EB25DB4E86E412DEE2A16DB4B702727FB5F96BF864`.

Fresh Embedded10 and External10 launched from actual Entry without a seed save, host override or runtime-validation override. Both used the repaired probes. Cold startup, delayed capture, visual-watch completion and disposable normal save/load pass 15 and 16 live correlation checks respectively. Every observed direct status reply retains its requested body. The separate completion files and rendered cold-colony captures are preserved.

Both final grid matrices pass 12 checks and 20 independently settled Shift-wheel events at actual 100/300 pawns and scales 1/1.5. Both modes verify actual loaded hashes, mod order and zero ERROR/FATAL log lines. Eight inspected grid captures retain separate footer controls; two cold captures show the rendered colony and initial feature-tour dialog. Both exact sessions released with exit0, without force. The `normal-embedded10` and `normal-external10` receipts under `normal-final-01` and `correlation-parent-01`, plus `normal-final-01/final-visual-inspection.json`, bind this acceptance.

These checks close the reproduced save, footer, preview-owner and harness-response defects on the pinned build. Retain the one-caller preview reset as the game-transition boundary. Retain the harness's one-caller request parser and command-result publisher as the wire-validation and response-ownership boundaries. Do not merge instrumentation into shipping BWT.

Multiplayer, absent foreign mods, physical input, individual-pixel parity, controlled performance, other devices and later unstable builds remain unverified. The optional harness 0.19 compile check could not resolve its configured historical depot and failed before compilation. No historical payload was replaced. This port does not claim universal compatibility or original native source recovery. Final source and upstream bindings are recorded separately from frozen binaries.
