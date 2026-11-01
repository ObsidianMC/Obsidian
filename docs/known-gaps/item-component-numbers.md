# Item component numbers


**Status:** mostly done. Furnaces, brewing stands and composters read 26.3's `minecraft:cooking_fuel`,
`minecraft:brewing_fuel` and `minecraft:compostable` components (`WorldData/ItemFuels.cs`), and brewing stands brew
by the `minecraft:brewing` recipes.

- The components' numbers name context number providers, which `WorldData/ComponentNumbers.cs` works out with only
  the provider and predicate types vanilla's fuel and compost providers use. It stands in for Phase 4's context
  number providers and should be replaced by them.
- A brewing fuel's numbers are worked out for a default brewing stand rather than the stand's own block state, and a
  fuel's crafting remainder (none of vanilla's brewing fuels has one) isn't given back.
- Brewing outputs can set only `minecraft:potion_contents`, and potion tags in brewing inputs aren't read.
- `minecraft:villager_food` and `minecraft:mob_visibility` aren't read: villagers don't pick up or share food, and
  mobs don't see players wearing heads any less yet.

**Where to start:** once context number providers land, resolve `ComponentIntValue` and `ComponentFloatValue` with
them and delete `ComponentNumbers`.
