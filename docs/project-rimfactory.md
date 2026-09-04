# Project RimFactory Revived compatibility plan

This document plans the Multiplayer Compatibility integration for
[Project RimFactory Revived](https://github.com/zymex22/Project-RimFactory-Revived).
It complements the deterministic scheduler work in PRF itself and covers the
remaining player-issued commands, dialog state, and small deterministic guards.

## Baseline and scope

The plan was prepared against:

- Multiplayer Compatibility `bcffd46671bd326f1b978d421423e3c5b68305b8`
  (2026-08-20).
- Project RimFactory Revived `8f4de68148a5ff0cd0f96ba02cf7b4b258224924`
  on `feat/scheduler-multiplayer-safe` (2026-08-20).
- RimWorld 1.6 and the corresponding current Multiplayer release.
- PRF package ID `spdskatr.projectrimfactory`.

PRF's scheduler patch remains the owner of scheduler persistence, absolute-tick
reproduction, deterministic insertion order, and old-save migration. This repository
must not reproduce or Harmony-patch `MapTickManager`. Its responsibility is to sync
the user actions that mutate PRF state and to guard the few remaining deterministic
hazards outside the scheduler.

The integration should be reflection-only, like the other mod compatibility classes,
and should not add a compile-time reference to the PRF assembly.

## Bootstrap and compatibility gate

Add `Source/Mods/ProjectRimFactory.cs` with
`[MpCompatFor("spdskatr.projectrimfactory")]`.

Use `LongEventHandler.ExecuteWhenFinished(LatePatch)` so types, nested types, and
compiler-generated methods are resolved only after all mods have loaded. Before
registering anything, resolve
`ProjectRimFactory.AutoMachineTool.ScheduledCall`. This is the feature gate for the
scheduler-safe PRF version. If it is absent, log one clear incompatibility error and
skip the entire integration; partial support would still allow scheduler desyncs.

The minimum supported PRF release should be recorded in the compatibility class and
README once the scheduler patch has landed in a tagged PRF release.

## Design rules

1. Sync state-changing methods, not drawing code.
2. Prefer named methods or small explicit wrappers. Register lambda ordinals only when
   there is no stable named target, and re-check every ordinal against the release DLL.
3. Never read `Find.Selector` inside a synchronized method. Capture the ordered
   `Thing[]` selection on the issuing client and pass it as an argument.
4. Keep search text, scroll positions, expanded rows, selected tabs, and other purely
   visual state local.
5. Wrap dialog drawing in `WatchBegin`/`WatchEnd` when edits are applied through fields.
   Buffer sliders and numeric text fields so partially typed values cannot become sync
   commands.
6. Use post-apply callbacks for required side effects and cache invalidation rather than
   relying on a later tick to repair state.
7. Serialize stable game identities. For example, identify a slot group by a stable
   representative cell rather than serializing a `SlotGroup` instance.
8. Explicitly mark development-only registrations with `.SetDebugOnly()`.

## Coverage matrix

### Conveyors and item movement

Sync the commands for:

- Dropping a carried item.
- Item puller active/forbidden state and output side.
- Conveyor output direction.
- Marking or clearing the end-of-line state.

For multi-selection gizmos, pass the selected compatible buildings explicitly. Test
with a different local selection on the second client to prove that no remote selector
state leaks into execution.

### Belt splitter

Sync active outputs, priority, and filter-copy actions. A splitter output is not a
standalone `Thing`; identify an output link by its parent splitter and `Rot4`. Use the
same context for its `ThingFilter`, so filter edits are routed to the correct output on
every client.

### Production settings

Cover:

- `outputToEntireStockpile`.
- Obey-storage-filter and forbid-on-placing toggles.
- Product limitation enabled/count/count-stacks settings.
- Target slot group.

Resolve a target slot group from a stable cell on the synchronized map. Do not sync a
`SlotGroup` reference directly. When a setting needs recalculation or power refresh,
perform that work in the synchronized method or its post-apply callback.

### Power and range settings

Sync speed power, range power, range shape, and rotation. After applying a value, call
the appropriate PRF side effect (`AdjustPower`, `RefreshPowerStatus`, or the equivalent
resolved method) immediately. Slider values must be buffered and sent only when the
user commits a changed value.

### Smart hopper

Sync all five pickup-policy flags and the hopper's output settings. `OutputSettings` is
not a `Thing`, and its dialog stores the active target in static state. Implement an
owner-based sync worker that resolves the owning smart hopper, then watches
`UseMin`, `UseMax`, `min`, and `max` around dialog drawing.

Filter-copy operations must receive their source/target owners explicitly; they must
not infer them from the open dialog or current selection during replay.

### Assemblers

Sync:

- Allow-forbidden state.
- Zone creation.
- Recipe queue insertion and ordering.
- Recipe cancellation.
- Aborting the active recipe.

Wrappers should identify the assembler and recipe/queue entry directly. Do not
serialize an open inspector tab or its local row index unless the index is validated
against stable queue data at execution time.

### Slaughterhouse

The slaughter configuration is stored in a non-`Thing` dictionary. Synchronize one
complete row through a method such as `ApplySlaughterSetting(owner, animalDef, values)`.
The method must create the dictionary entry if it does not yet exist, then apply the
whole row atomically so intermediate field changes cannot escape to simulation.

### Drones

Sync:

- Lockdown for one station and for an explicit selection of stations.
- Allowed area.
- Enabled work types.
- Fuel target.
- Grow-zone assignment.

Any "all selected" operation receives a `Thing[]`; it must not enumerate the receiving
client's selection.

### Storage and I/O ports

Cover rename actions, storage-family reorganization, pawn-access policy, I/O binding,
I/O mode, forbid-output state, and output settings. Multi-port operations receive the
ordered selected ports explicitly. Output settings use the same owner-based worker and
watched fields as smart hoppers.

Vanilla storage-filter behavior is expected to be handled by Multiplayer core, but it
must still receive a two-client smoke test when embedded in PRF storage dialogs.

### Items inspector tab

Sync state-changing item actions, including drop, ingest, equip, wear, and partial
caravan/job actions exposed through PRF's items tab. Register the delegate that performs
the action, or wrap it with explicit owner/item/count arguments. Never serialize or
reconstruct the local inspector-tab instance on the remote client.

### Atomic reconstruction

Sync `ThingToGenerate` and reconstruction speed. Apply any required work-total,
progress, or power recalculation in the synchronized operation.

### Paperclips

Sync fuel-consumption configuration, duplicator binding, and the complete duplication
transaction. The transaction wrapper should carry stable source/target identities and
all committed values; it must not depend on transient inspector-tab state.

### Colored lights

Wrap color changes with an explicit selected-building array and the selected color key.
The remote method must not consult the selector.

### Development actions

Register state-changing debug gizmos only when needed for development parity and mark
every registration with `.SetDebugOnly()`.

## Custom sync contexts and workers

The implementation is expected to need these explicit contexts:

| Context | Stable identity | Purpose |
|---|---|---|
| Splitter output link | Parent splitter + `Rot4` | Route priority and output-filter changes |
| Splitter filter | Same parent + `Rot4` context | Route `ThingFilter` operations |
| Output settings | Owning smart hopper or I/O port | Recreate the non-`Thing` settings object |
| Slaughter row | Owner + animal `ThingDef` | Apply/create one dictionary row atomically |
| Slot group | Map + representative cell | Resolve the current `SlotGroup` safely |

Prefer owner reconstruction over serializing internal PRF objects. Workers should fail
loudly and leave state untouched when the owner or child context no longer exists.

## Determinism follow-up

Use Multiplayer's unsafe-sync logger during implementation and the final stress test.
Two constructors require focused review because they may consume `Verse.Rand` during a
user-issued synchronized action:

- `Building_BeltSplitter` construction.
- Drone-area construction.

If the logger or source audit confirms RNG use, add scoped `Rand.PushState`/`PopState`
patches with `try/finally`-equivalent safety. Do not blanket-patch unrelated PRF random
calls. The scheduler itself is excluded because the required deterministic work is in
PRF's scheduler branch.

## Intentionally local or excluded behavior

Do not synchronize:

- Search strings, scroll positions, selected inspector rows, and open/close dialog state.
- Selector, camera, mouse position, sounds, and hover effects.
- Help/information dialogs and ArchoCipher presentation state.
- Mod settings that are not issued as in-game simulation commands.
- Informational logs and messages, unless testing proves they gate a state change.

## Implementation sequence

Keep changes reviewable in four logical commits:

1. Bootstrap, scheduler feature gate, reflection helpers, and confirmed RNG guards.
2. Gizmos and explicit-selection wrappers.
3. Inspector tabs, dialogs, custom workers, and contexts.
4. Debug-only registrations, compatibility README entry, and final test notes.

The sequence is organizational, not a reason to ship partial compatibility. Until the
coverage and acceptance gates below pass, the feature gate should prevent users from
mistaking an incomplete integration for safe support.

## Acceptance gates

1. Build the release configuration and start the game with no reflection-resolution
   errors or compatibility warnings on the supported PRF release.
2. Exercise every coverage-matrix row in a host-plus-one-client session.
3. Deliberately keep different selections, inspectors, and dialogs open on each client
   while testing every multi-selection operation.
4. Repeat representative actions on two maps to catch implicit `Find.CurrentMap` use.
5. Leave each patched dialog open without editing and confirm that it emits no repeated
   sync commands.
6. Save/reload, join in progress, and load a pre-scheduler-patch save after upgrading to
   the supported PRF version.
7. Stress contested factories: two machines pulling the final item, splitter/filter edits
   during flow, queue edits during production, and drone setting changes during work.
8. Review unsafe-sync logger output and resolve every PRF-originated finding or document
   why it is local-only.
9. Confirm both clients produce matching checksums after each test group and after a
   subsequent save/reload.

## Open decisions before implementation

- Select the exact minimum PRF version after the scheduler work is merged and released.
- Resolve and record compiler-generated lambda ordinals against that release DLL.
- Decide whether the two constructor RNG guards are necessary after source audit and
  unsafe-logger testing.
- Confirm which item-tab and embedded storage-filter actions Multiplayer core already
  covers, while retaining smoke tests for their PRF call paths.

## Definition of done

The patch is ready for review when every state-changing PRF UI command is either covered
by a named registration/wrapper or explicitly documented as handled by Multiplayer core;
the supported PRF version passes the full acceptance matrix; no operation depends on
remote selector or dialog state; and the unsafe-sync logger reports no unexplained PRF
simulation hazards.
