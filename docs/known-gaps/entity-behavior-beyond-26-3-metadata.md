# Entity behavior beyond 26.3 metadata


**Status:** metadata layouts match the 26.3 fixture; the following behavior remains open.

- Sulfur cubes expose the ageable flags, size, fuse and bucket flag, but have no archetype selection, AI, bounce
  physics, fuse/explosion behavior or bucket interactions. Cushions expose their color but have no seating,
  movement or item placement/drop behavior. Their type-specific state is not persisted yet.
- Age locking is sent to clients, but golden dandelion interactions, stopping aging and saving the lock remain
  unimplemented. Cat, cow, pig and chicken sound variants default to the classic registry entry; choosing,
  inheriting, saving and playing those variants remains unimplemented.
- Block and item displays write vanilla's default transformation and empty contents, but have no editing or
  type-specific persistence. Interaction entities expose width, height and response metadata, but do not apply
  those dimensions on the server or record attacks/uses. Ominous item spawners expose their item but do not time
  or release it. Markers and player wind charges use the nonliving entity layout; marker data and wind-charge
  projectile/impact behavior remain unimplemented.
- Villagers and wandering traders send an inactive unhappy counter; villagers send an unfinalized data flag.
  Those animation/finalization transitions are not modeled yet.

Poplar boats and chest boats use the existing boat implementation, including its item mapping and inventory.

**Where to start:** `Obsidian/Entities` and its factory; `EntityMetadataParity` checks every entity type and all 44
serializer ids, requires complete layouts for ageable mobs and the metadata implementations above, and checks
representative holder and primitive payloads against the captured serializer bytes.
