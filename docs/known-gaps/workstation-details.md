# Workstation details


**Status:** mostly done. The anvil, smithing table, stonecutter, loom, grindstone and cartography table work like
vanilla's menus (`MainEventHandler.Workstations`), with vanilla's and plugins' stonecutting and smithing recipes.

- The smithing table finds a trim's material from a fixed item list rather than the addition's
  `provides_trim_material` component, so a plugin item can't supply a trim material.
- The loom's patterns come from `BannerPatternsRegistry`, which only has vanilla's patterns and pattern items.
- The stonecutter's, loom's and cartography table's take sounds go only to the player using them, and a cartography table's map is made new
  as it's taken, even when the take then fails.
- The beacon, lectern and crafter work too (`RedstoneBlockEntities`, `MainEventHandler.RedstoneMenus`). A beacon
  measures its pyramid and beam all at once every 80 game ticks rather than a layer a tick, and doesn't fire
  `construct_beacon`. A crafter crafts only shaped and shapeless recipes, like the crafting table.
