# 26.3 entities

**Status:** open. The entities and mob changes 26.3 added are not implemented. They were left out of the 26.3 port
(see [26.3 gameplay left out of the port](26-3-gameplay-left-out-of-the-port.md)). The
[block interactions plan](../block-interactions-plan.md) covers 26.3's blocks, and of everything below only age
locking, which golden dandelions set; nothing else here has a plan.

Both new entities exist as types, so they load, save their common fields and sync their metadata to clients. Neither
does anything.

- **Sulfur Cube** (`Obsidian/Entities/SulfurCube.cs`). It sends the ageable flags, its size, its fuse and its
  from-bucket flag, and nothing else. Missing: archetype selection, AI, bounce physics, absorbing blocks, the fuse
  and explosion, shearing, splitting, buckets and dispenser behaviour. Its own state isn't saved. Natural spawning
  is recorded as skipping Sulfur Cubes, so Sulfur Caves have none.
- **Cushion** (`Obsidian/Entities/Cushion.cs`). It sends its colour. Missing: seating, movement, placing it from
  its item and dropping it. Its colour isn't saved.
- **Mob changes.** Age locking is sent to clients but never set or saved (golden dandelions set it; those are in
  the block interactions plan, phase 2 batch G). 26.3's baby hitboxes and persistent mobs idling when no player is
  near are not modelled. Spectators already travel through nether and end portals; end gateways still refuse them
  (`AbstractLevel.EndFight.cs:307`).

Poplar boats and chest boats work: they use the existing boat implementation.

[Entity behavior beyond 26.3 metadata](entity-behavior-beyond-26-3-metadata.md) has the metadata-level detail for
these and for the display, interaction and marker entities.

**Where to start:** `Obsidian/Entities` and `Entities/Factories/EntitySpawner.cs`. Capture each behaviour from
vanilla first, as [scenario captures](../scenario-captures.md) describes; `EntityMetadataParity` already checks both
entities' metadata layouts.
