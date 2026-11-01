# Villager trades


**Status:** Phase 4 trades implemented.

Offers come from the 26.3 trade sets in vanilla's draw order (see `TradeFixtures.java`). What remains:

- Random sequences are kept per level in memory, shared with loot, and not saved. Vanilla keeps one set per server,
  across dimensions, in its saved data, so offers drawn after a restart differ from vanilla's.
- Trade data can only give or want the `minecraft:potion_contents` component, the only one vanilla's trades use.
- Plugins can't add trades or trade sets yet.
- Gossip and Hero of the Village discounts (an offer's special price) aren't applied.
