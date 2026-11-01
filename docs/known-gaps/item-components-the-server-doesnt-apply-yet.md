# Item components the server doesn't apply yet


**Status:** open. Found while honouring plugin items' default components; vanilla items have the same gaps.

- Breaking time isn't checked: a player's client times breaking from the held tool's mining speed and the server
  accepts the finished break. The tool component still decides drops and wear.
- Mobs' equipment modifiers don't go into their attributes: mobs add their armour items' armour, toughness and
  knockback resistance where they're hit, and their held item's attack damage where they attack. Players apply
  every modifier of what they hold and wear. Enchantments' attribute effects aren't applied to anyone.
- Equipping by use ignores `equippable`'s `swappable` and plays no equip sound, and a `consumable` without `food`
  isn't consumed.

**Where to start:** for mobs' attributes, `Player.UpdateEquipmentAttributes`'s per-slot modifier tracking, run on a
mob's equipment.
