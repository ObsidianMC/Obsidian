# Redstone implementation plan

The redstone handoff plan is split into worker-sized documents in [`docs/redstone/`](redstone/):

1. [`00-index-and-rules.md`](redstone/00-index-and-rules.md) — architecture rules, package order, and worker checklist.
2. [`01-foundation.md`](redstone/01-foundation.md) — parity fixtures, persisted block ticks, and safe live block updates.
3. [`02-core-circuits.md`](redstone/02-core-circuits.md) — power engine, sources/consumers, wire, repeaters, comparators, and observers.
4. [`03-devices-and-pistons.md`](redstone/03-devices-and-pistons.md) — inventories, rails, block entities, pistons, and moving blocks.
5. [`04-parity-and-release.md`](redstone/04-parity-and-release.md) — coverage audit, performance, test gate, and release criteria.

Workers should begin with the index, then load only their assigned document and any explicitly referenced predecessor API summary.
