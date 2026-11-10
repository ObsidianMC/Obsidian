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
- Furnaces, blast furnaces and smokers give out what they cooked as vanilla does (`furnace_results.json`), but:
  - The chance of a recipe's fraction of a point is drawn from the level's `CookingExperienceRandom`, a random safe
    to share, rather than the level's random, as Obsidian gives the experience out from a player's click, off the
    level's tick. Its orbs don't draw vanilla's check for orbs to merge with either, so the level's random parts
    from vanilla's sequence where vanilla would draw. Breaking a machine also doesn't draw vanilla's drop position
    for each of its slots, empty ones included, which vanilla draws before the experience.
  - A broken machine drops its recipes' experience as one amount, split into orbs together, where vanilla splits
    each recipe's on its own (`AbstractLevel.BlockEntities`), and drops it at the block's corner rather than its
    centre.
  - Taking a result awards no `minecraft:crafted` statistic, which vanilla's result slot awards for the items
    taken.
  - The recipes come on the player's next progress tick rather than with the click, so a client sees their
    recipe_unlocked advancements a tick later than vanilla's.
