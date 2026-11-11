# Known gaps

Behavior Obsidian doesn't have yet, or has on purpose in a narrower form than vanilla: vanilla parity, test plumbing
and contracts left weaker on purpose. Each file starts with its status, then says what's missing, why it was left
and where to start.

## World generation

- [Clamp generation to the build range](clamp-generation-to-the-build-range.md)
- [Completion reads light from changing blocks](completion-reads-light-from-changing-blocks.md)
- [Dimension effects beyond the sky, clouds and fog](dimension-effects-beyond-the-sky-clouds-and-fog.md)
- [Lakes, features and structures in flat dimensions](lakes-features-and-structures-in-flat-dimensions.md)
- [Seams between 1.21.11 and 26.3 terrain](seams-between-1-21-11-and-26-3-terrain.md)
- [Structure pieces in saved chunks](structure-pieces-in-saved-chunks.md)
- [World upgrade drops and refusals](world-upgrade-drops-and-refusals.md)

## Blocks and redstone

- [26.3 blocks](26-3-blocks.md)
- [Block item placement](block-item-placement.md)
- [Block-use state validation](block-use-stale-state.md)
- [Brushing and armor stands](brushing-and-armor-stands.md)
- [Bubble columns started by water](bubble-columns-started-by-water.md)
- [Cauldrons](cauldrons.md)
- [Comparators reading unopened loot containers](comparators-reading-unopened-loot-containers.md)
- [Dispenser behaviours](dispenser-behaviours.md)
- [Eyeblossoms, dried ghasts and portal piglins](eyeblossoms-dried-ghasts-and-portal-piglins.md)
- [Player break effects](player-break-effects.md)
- [Plant growth beyond saplings](plant-growth-beyond-saplings.md)
- [Projectile block hits](projectile-block-hits.md)
- [Recursive placement callbacks](recursive-placement-callbacks.md)
- [Redstone](redstone.md)
- [Sculk catalysts](sculk-catalysts.md)
- [Stepped and fallen-on contacts](stepped-and-fallen-on-contacts.md)
- [Sign interactions](sign-interactions.md)
- [Special blocks](special-blocks.md)
- [Tilling and other block transformers](tilling-and-other-block-transformers.md)
- [Vanilla blocks' reactions to block updates](vanilla-blocks-reactions-to-block-updates.md)
- [Waxing, bells and redstone ore](waxing-bells-and-redstone-ore.md)

## Items, crafting and trading

- [Block entity data on items](block-entity-data-on-items.md)
- [Item access beyond containers](item-access-beyond-containers.md)
- [Item component numbers](item-component-numbers.md)
- [Item components the server doesn't apply yet](item-components-the-server-doesnt-apply-yet.md)
- [Item components without a saved codec](item-components-without-a-saved-codec.md)
- [Special crafting recipes](special-crafting-recipes.md)
- [Villager trades](villager-trades.md)
- [Workstation details](workstation-details.md)

## Entities and players

- [26.3 entities](26-3-entities.md)
- [26.3 gameplay left out of the port](26-3-gameplay-left-out-of-the-port.md)
- [Entities lost when an unloading save fails](entities-lost-when-an-unloading-save-fails.md)
- [Entity behavior beyond 26.3 metadata](entity-behavior-beyond-26-3-metadata.md)
- [Lightning](lightning.md)
- [Mob ambient sounds](mob-ambient-sounds.md)
- [Movement and swing details](movement-and-swing-details.md)
- [Plugin entities](plugin-entities.md)
- [Saved passenger trees and leads](saved-passenger-trees-and-leads.md)
- [Sleeping and spawn points](sleeping-and-spawn-points.md)

## Server, commands and networking

- [26.3 commands](26-3-commands.md)
- [Chunk ticket parity](chunk-ticket-parity.md)
- [Context number provider commands and world sources](context-number-provider-commands-and-world-sources.md)
- [Inventory and world lock order](inventory-world-locking.md)
- [Low disk space notifications](low-disk-space-notifications.md)
- [Progress parity](progress-parity.md)
- [Scoreboard display waits only for queueing](scoreboard-display-waits-only-for-queueing.md)
- [Signed chat and block picking](signed-chat-and-block-picking.md)
- [World clocks, timelines and decorative registries](world-clocks-timelines-and-decorative-registries.md)

## Mods

- [Attachment interoperability and saved-data migrations](attachment-interoperability-and-saved-data-migrations.md)
- [Mods' menus in the inventory screen](mods-menus-in-the-inventory-screen.md)

When you add a gap, give it its own file named after its title and list it here under its area.
