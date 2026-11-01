# Block entity data on items


**Status:** partly done. A plugin's block entity keeps its data on its block's item (`BlockEntity.CollectComponents`
and `ApplyComponents`, see [plugin content](../plugin-content.md#block-entities)).

- Creative pick-block (`PickItemFromBlock`) isn't handled, so no block entity's data reaches a picked item, and a
  placed item's `block_entity_data` isn't loaded into its block entity, which vanilla does before
  `applyImplicitComponents` (an operator's or a creative player's raw block entity data).

Plugin component values default to hashes of their saved NBT, which matches vanilla's `HashOps` for codecs writing
NBT-shaped values. Codecs whose types NBT loses (such as booleans stored as bytes) must supply
`ComponentNetworkCodec<TValue>.Hash` to match NeoForge or Fabric predictions. Without it, hashes can differ. The
server currently ignores client click predictions and sends authoritative contents after a click; hash mismatches
do not change the click it applies.

**Where to start:** pick-block in `PlayClientHandler`, collecting through the same path as breaks
(`BlockEntityComponents.CaptureAsync` without retiring a container).
