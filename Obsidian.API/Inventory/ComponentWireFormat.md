# Item components (Java 1.21.11)

`NetworkBuffer` consumes all 104 vanilla component wire shapes. The component enum is generated from the vanilla registry, and the golden test verifies every ID and identifier. A small wire walker handles framing independently of the public models. Existing modeled components retain typed values; remaining types use `OpaqueDataComponent(Type, Data)` with their complete encoded payload.

Decoded typed components also retain their original encoding. Writing an unchanged value emits those bytes; editing a typed value emits its current serialization. This preserves rich chat/NBT details and registry holder IDs that the display models cannot fully express. If a valid value cannot be projected into a public model, it is retained as an opaque component. Unknown future-version IDs are rejected: undelimited patches cannot safely skip unknown wire formats.

## Client API

These methods are on `NetworkBuffer` and the corresponding `INetStreamReader` / `INetStreamWriter` interfaces:

```csharp
ItemStack? ReadItemStack();
void WriteItemStack(ItemStack? itemStack);
ItemStack ReadRequiredItemStack();
void WriteRequiredItemStack(ItemStack itemStack);
ItemStack? ReadUntrustedItemStack();
void WriteUntrustedItemStack(ItemStack? itemStack);
ItemStack?[] ReadItemStackList();
void WriteItemStackList(params ReadOnlySpan<ItemStack?> values);
IHashedItemStack? ReadHashedItemStack();
void WriteHashedItemStack(ItemStack? itemStack);
void WriteHashedItemStack(IHashedItemStack? itemStack);
DataComponent ReadDataComponent(DataComponentType type);
void WriteDataComponent(DataComponent component);
```

Use the optional codec for container content/slot, cursor, player inventory and equipment. Empty stacks are `null`. The list codec includes its VarInt length and permits empty entries. Use the untrusted codec for creative slot packets: each added component has a VarInt payload length after its type ID. Required stacks use the same nonempty wire format but reject empty values. Nested containers use optional stacks; bundles, charged projectiles and use remainders require nonempty stacks.

`NetworkBuffer` additionally exposes `ReadItemStack(bool delimitedComponents)` and `WriteItemStack(ItemStack? itemStack, bool delimitedComponents)`; prefer the named methods above. Merchant helpers are `TradeItem ReadItemCost()`, `void WriteItemCost(TradeItem value)`, `TradeEntry ReadMerchantOffer()` and `void WriteMerchantOffer(TradeEntry value)`. An offers list is a VarInt count followed by these entries.

For click predictions, use `WriteHashedItemStack(ItemStack?)`. The interface overload relays already computed hashes unchanged. Cast a null literal to the intended overload. `bool TryHashDataComponent(DataComponent component, out int hash)` reports whether the particular value can be hashed exactly. Before hashing a vanilla server's items, set `Func<string, int, string?>? NetworkBuffer.ComponentRegistryName` to resolve registry identifiers and IDs using that connection's registries (return a fully qualified resource location, or null). Dynamic registry ordering is not universal. Without a callback, only the available Obsidian item, enchantment, potion and effect registries are used.

Display data is available through `ItemStack.Damage`, `MaxDamage`, `CustomName`, `ItemName`, `Lore`, `Enchantments` and `HasEnchantmentGlint`. The latter respects explicit overrides and removal, vanilla's glint item defaults, enchantments and lodestone compasses. Names/lore are `ChatMessage` projections; original encodings preserve additional features, but the display projection is not a complete vanilla text renderer. Resolve enchantment IDs against the connection's registry. For specific models use `GetComponent<TComponent>(DataComponentType)`; it returns null if the value is opaque. Set components through the indexer and remove them through `Remove(type)` so the patch tracks additions and removals. `Patch` exposes only changes to item defaults.

## Hash coverage

Hashes use CRC32C over vanilla HashOps' persistent component representation, including type markers, little endian primitives, UTF-16 code units, child hashes, unsigned map ordering and persistent-codec defaults. Hashes are not CRCs of network bytes.

Implemented types (64):

`custom_data`, `max_stack_size`, `max_damage`, `damage`, `unbreakable`, `use_effects`, `custom_name`, `minimum_attack_charge`, `item_name`, `item_model`, `lore`, `rarity`, `enchantments`, `attribute_modifiers`, `custom_model_data`, `tooltip_display`, `repair_cost`, `enchantment_glint_override`, `intangible_projectile`, `food`, `use_remainder`, `use_cooldown`, `damage_resistant`, `tool`, `weapon`, `attack_range`, `enchantable`, `repairable`, `glider`, `tooltip_style`, `swing_animation`, `stored_enchantments`, `dyed_color`, `map_color`, `map_id`, `map_decorations`, `charged_projectiles`, `bundle_contents`, `potion_contents`, `potion_duration_scale`, `suspicious_stew_effects`, `writable_book_content`, `written_book_content`, `trim`, `debug_stick_state`, `bucket_entity_data`, `ominous_bottle_amplifier`, `provides_banner_patterns`, `recipes`, `firework_explosion`, `fireworks`, `note_block_sound`, `banner_patterns`, `base_color`, `pot_decorations`, `container`, `block_state`, `container_loot`, `wolf/collar`, `tropical_fish/base_color`, `tropical_fish/pattern_color`, `cat/collar`, `sheep/color`, `shulker/color`.

Coverage is conditional on available registry names. Nested items require every nested component hash. Trim and banner patterns support registry references; inline definitions return unsupported. Chat supports literal/translation text, common formatting, insertion, shadow color, extra/with lists and NbtOps mixed-list wrappers. More complex chat (including events, fonts, keybind, score, selector and NBT content) returns unsupported. Raw custom NBT retains its numeric tag types when hashing. The fixture suite checks every implemented type against vanilla for its captured values, rather than claiming exhaustive coverage of every value combination.

Not implemented (40):

`damage_type`, `can_place_on`, `can_break`, `creative_slot_lock`, `consumable`, `equippable`, `death_protection`, `blocks_attacks`, `piercing_weapon`, `kinetic_weapon`, `map_post_processing`, `entity_data`, `block_entity_data`, `instrument`, `provides_trim_material`, `jukebox_playable`, `lodestone_tracker`, `profile`, `bees`, `lock`, `break_sound`, `villager/variant`, `wolf/variant`, `wolf/sound_variant`, `fox/variant`, `salmon/size`, `parrot/variant`, `tropical_fish/pattern`, `mooshroom/variant`, `rabbit/variant`, `pig/variant`, `cow/variant`, `chicken/variant`, `zombie_nautilus/variant`, `frog/variant`, `horse/variant`, `painting/variant`, `llama/variant`, `axolotl/variant`, `cat/variant`.

Unsupported hashes are omitted from the click prediction. Vanilla detects the patch mismatch and resends the authoritative slot. Wire round-tripping remains supported for these components. Received hashes can always be relayed with the interface overload.

## Golden fixtures and integration boundaries

`Obsidian.AssetGenerator/Java/ComponentFixtures.java` runs the actual cached 1.21.11 codecs and HashOps without starting a server. It uses the existing dumper reflection/mapping helper. Inputs beside it cover 13 composite stacks, one sample of every component, and two merchant offers. The small committed fixture is `Obsidian.Tests/Assets/item-components-1.21.11.json`. Tests assert exact consumption with a following sentinel, byte-identical optional/untrusted output, component registry IDs, hashes, merchant framing, typed edits and malformed input handling.

To regenerate from PowerShell in the repository root:

```powershell
$root = (Get-Location).Path
$cache = Join-Path $root 'Obsidian.AssetGenerator/obj/vanilla/1.21.11'
$cp = ((Get-ChildItem "$cache/versions", "$cache/libraries" -Filter *.jar -Recurse).FullName -join ';')
Push-Location $cache
try {
    java -cp $cp "$root/Obsidian.AssetGenerator/Java/ComponentFixtures.java" "$cache/server.txt" "$root/Obsidian.AssetGenerator/Java/component-fixtures-input.json" "$root/Obsidian.Tests/Assets/item-components-1.21.11.json"
} finally { Pop-Location }
```

The source generator distinguishes optional, required and untrusted item codecs. Existing handwritten packet serializers must select the right helper themselves. In particular, `SetCreativeModeSlotPacket.Deserialize` still needs its existing `ReadItemStack()` call replaced with `ReadUntrustedItemStack()`, and the handwritten merchant offer serializer should call the new merchant helper. Those packet files are outside this implementation's ownership. Recipe display polymorphic codecs remain outside this change. The item component named `recipes` itself is fully framed and preserved.
