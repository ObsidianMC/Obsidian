# Parity, coverage, and release (R9–R10)

Load [`00-index-and-rules.md`](00-index-and-rules.md) first.

## R9 — Coverage audit and special systems

### Work

- Audit every generated block that has a `powered`, `lit`, `power`, `triggered`, `enabled`, or `open` state; is a generated signal source; or exposes comparator output.
- Maintain a table with: block/system, status, responsible package, test, and any dependency preventing support.
- Complete applicable special systems: calibrated sculk sensor filtering/directionality, lectern, chiseled bookshelf, item frame, trapped chest, crafter, and version-specific redstone blocks.
- Add integration coverage for generated structures. Jungle temple and desert pyramid traps must function, not just generate correctly.

## R10 — Hardening and release gate

### Tests

- Expand Java-generated golden scenarios and `VanillaRedstone` snapshots. `VanillaFluids.cs` is the local template: it parses embedded JSON, builds a small `Chunk`-backed scenario world, applies actions by tick, advances `FluidTickScheduler`, and compares every expected state. Reuse that structure with the redstone scheduler/engine.
- Add real `AbstractLevel` integration tests for placement/use/break/explosion, chunk lifecycle, `block_ticks` persistence, and emitted client block/event packets.
- Regression-test existing sculk, note-block, jukebox, Breeze lever, and worldgen wire behavior.
- Test generated jungle temple (`WorldData/Structures/JungleTempleStructure.cs`) and desert pyramid (`DesertPyramidStructure.cs`) because they already place tripwire, wire, levers, repeaters, pistons, dispensers, and pressure plates.

### Performance and safety

- Instrument queued/processed/deferred redstone updates, scheduled ticks, cap hits, and slow/hot chunks.
- Stress clocks, wire grids, hopper chains, piston arrays, and rapid edits.
- Confirm no recursion overflow, forced chunk loading, unsafe concurrent world mutation, or invalid persisted tick/state data.

### Validation commands

Use the repository harness:

- `obsidian_workflow` doctor
- `obsidian_workflow` build
- `obsidian_workflow` test with `linux-smoke`
- targeted redstone tests added by this work

The solution targets .NET 10; generated assets require Java 25+ and may download/generate data on a fresh build. On this Linux host, do not use `Obsidian.Tests.Noise` (System.Drawing limitation); known unrelated failures are Encryption (invalid AES IV length) and Compression (stale asserted packet id). Report them separately rather than attributing them to redstone.

### Completion criteria

- All intended feature rows in the coverage table are implemented or intentionally deferred with an explicit missing subsystem.
- Tick ordering and persistence are deterministic and covered by tests.
- Supported behavior meets the R0 parity contract and golden scenarios.
- The README roadmap item may be marked complete only after this audit is resolved.
