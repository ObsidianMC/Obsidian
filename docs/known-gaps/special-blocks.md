# Special blocks

**Status:** partial. Ender chests and shulker boxes open and refuse to like vanilla's; shulker lids open on the
server, grow their collision box and push what's in their way; dragon eggs teleport when used and attacked; conduits
are a typed block entity that checks its frame, gives Conduit Power and hunts; End gateways are a typed block entity
with their beam, cooldown and safe exit, and send entities and players. All of it is checked against vanilla by
`special_blocks.json` (see [scenario captures](../scenario-captures.md)). What's left:

- **Observers write their pulse with every update.** An observer a shulker lid's shape updates set off writes its
  powered and unpowered states with `UPDATE_ALL` (3); vanilla's `ObserverBlock.tick` writes them for clients only (2)
  and then updates the block in front alone. So an observer's pulse notifies all six neighbours, where vanilla's
  reaches the block it faces. This is the existing observer (`RedstoneObserver.TickAsync`), which the redstone
  scenarios don't see, since they record states and not flags. The replay pins the six writes of
  `shulker_box_observer_sees_lid`.
- **Landing entities emit no `hit_ground`.** Vanilla's `Entity.checkFallDamage` emits the game event each time an
  entity lands after falling. No Obsidian entity does, so sculk sensors don't hear them. The replay pins the four
  landings of `shulker_box_up_lifts_falling_tnt`; the brushing replay pins two more (see
  [brushing and armor stands](brushing-and-armor-stands.md)).
- **A creative player breaking a block sends its sound, not level event 2001.** The dragon egg's creative case
  (`dragon_egg_attack_creative`) is pinned; see [player break effects](player-break-effects.md).
- **Armor stands have no physics.** Obsidian's armor stands never fall or move on their own, so the capture shows a
  falling body landing on an open lid with primed TNT, whose physics Obsidian has; armor stands with `NoGravity`
  check the pushing.
- **Shulker lids don't push players.** Vanilla's lid moves a server player as it moves other entities; Obsidian
  leaves players to their clients, which tick the lid and move themselves.
- **Sideways lids collide only within their own block's column.** An open lid reaches half a block out of its block.
  Obsidian's collision search (`MobTerrain`) looks one block below an entity's box for shapes reaching up, but not
  sideways, so a lid facing north, east, south or west only stops entities whose box also reaches into the box's own
  block. Pushing isn't affected: it uses the lid's swept box directly.
- **Vanilla's mutable gateway event position is modelled, not copied.** Vanilla's `EndGatewayBlock.entityInside` hands
  `triggerCooldown` the inside-block walk's mutable position, which has moved on to the walk's last block by the time
  block events run; the event only flashes the beam (and restarts the cooldown on its tick) when that block is a
  gateway. Obsidian queues the event at the block of the top corner of the entity's box, which is where the walk ends
  for an entity standing still (`EndGateways.EventPosition`). A moving entity's walk can end elsewhere.
- **New End gateway exits.** Using a gateway of the End that has no exit builds Obsidian's simplified far gateway: on
  an End stone platform at the surface 1024 blocks out, with exact exits both ways. Vanilla's
  `findOrCreateValidTeleportPos` walks the chunks out from the main island, searches the first one with land for End
  stone with room above, places an End island where it finds none, raises the gateway 10 blocks above the tallest
  block, and links both gateways without `ExactTeleport`, so both arrive at the safe exit. Not captured.
- **An ender chest opened while another is open.** Vanilla sets the player's active chest before closing the menu
  already open, so the old chest's openers are counted down on the new chest, which then doesn't open its lid until a
  recheck. Obsidian closes the old one first. Not captured.
- **A conduit hunting among several mobs.** Vanilla picks its mob from the hostile mobs in reach, in its entity
  sections' order, by the level's random; Obsidian draws the same way from the mobs near it in its own order, so with
  more than one the pick can differ. The captures have one.

**Where to start:** `RedstoneObserver.TickAsync` for the observer's flags, `PrimedTnt` and `Mob` for `hit_ground`,
`AbstractLevel.BuildEndGatewayExitAsync` for vanilla's gateway exits.
