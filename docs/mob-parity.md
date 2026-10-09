# Mob parity gaps — Java Edition 1.21.11

This document inventories missing and approximate mob behavior in the current source, including shared systems and older mobs. A concrete factory implementation does not imply vanilla parity. **Missing** means no corresponding behavior was found in the audited implementation; **approximate** means behavior exists but uses a reduced model; **unverified** means equivalence still needs a targeted comparison or runtime validation. Shared gaps apply to every relevant mob, even when not repeated in its entry.

The audit covers `Obsidian/Entities`, mob spawning/storage, village/raid systems, game events, explosions and End-fight handling. It is a source audit, not an exhaustive comparison of every vanilla code path. Builds, tests and in-game checks were not run. Unknown defects and unlisted edge cases remain possible.

## Shared systems

### AI, movement and targeting

- **Approximate:** Brain-driven mobs use goals and local timers rather than the complete vanilla activities, memories and sensors. Work, social behavior, hunting, retreat, group coordination and interruption rules are reduced.
- **Approximate:** Ground paths use a common node evaluator with fixed step/drop constraints; swimming and flying use a common volume movement model. Species-specific path costs, water entry/exit, obstacle avoidance, landing selection, door handling and stuck recovery are not fully equivalent.
- **Approximate:** Movement uses common gravity, friction, fluid checks and a fixed 0.6-block collision step. Per-mob step heights, fluid flow, climbing, block movement effects and exact steering need parity work.
- **Missing/incomplete:** General entity pushing, crowd collision and entity cramming are not part of the mob movement collision model. Target predicates frequently use explicit type lists rather than the complete vanilla tags and context rules.
- **Unverified:** Goal priority, target visibility/retention, difficulty scaling, attack reach and `NoAI` handling across every species/state.

Source: `Entities/Mob.AI.cs`, `Entities/AI/*`, `Entities/PathfinderMob.cs`, individual goal registrations.

### Damage, effects and equipment

- **Missing:** A complete typed damage-source model. The attacker entity cannot represent all direct/indirect owners, projectile/magic/explosion distinctions and bypass tags, so immunities, retaliation and kill attribution cannot consistently match vanilla.
- **Incomplete:** Shield blocking/disabling, enchantment protections and attacks, absorption consumption, equipment attribute modifiers, durability rules and exact damage/knockback ordering. Mob armor has a material-name lookup; player incoming damage has a cooldown but no equivalent armor/shield calculation in `Player.Combat.cs`.
- **Approximate:** Equipment selection scores item names, armor values and durability rather than the complete component/enchantment/preference rules. Spawn armor is randomized without complete enchantment initialization. Melee weapon values are a limited item-name calculation.
- **Missing/incomplete:** Suffocation inside blocks, cactus damage, freezing, wither-rose damage and the complete environmental damage matrix in the common mob environment tick. Fire, lava, drowning, falls, magma and berry bushes cover only part of that matrix; the void check uses a fixed Y threshold.
- **Approximate:** Active effects replace an existing effect directly, without vanilla hidden-effect stacking/priority behavior. Only selected effects have server-side gameplay handling. Restoration saves duration/amplifier without the complete ambient/icon/particle state; living metadata writes an empty effect-particle list.
- **Unverified:** Invulnerability windows, resistance/protection exceptions, potion interactions and special damage behavior across all mobs. Dedicated sonic/magic helpers do not resolve the shared damage model.

Source: `Entities/Living.cs`, `Entities/Mob.AI.cs`, `Entities/Mob.Environment.cs`, `Entities/Mob.Equipment.cs`, `Entities/Player.Combat.cs`.

### Spawning and despawning

- **Approximate:** Natural spawning uses common category caps and placement checks. Per-player local caps, spawn-cost budgets, biome generation probabilities, pack initialization and all species-specific placement predicates are not fully represented.
- **Incomplete:** Structure overrides beyond the explicit fortress path, specialized spawns, jockey creation, equipment/enchantment distributions and biome/variant probabilities. Trial-spawner and ominous-trial spawning behavior is not covered by the ordinary mob-spawner path.
- **Incomplete:** The 1.21.11 hostile mount groups lack general mob passenger support: Zombie Horse/Zombie, Camel Husk/Husk/Parched and Zombie Nautilus/Drowned spawning and rider-controlled combat need implementation.
- **Approximate:** Common persistence/despawn rules do not capture every species' interaction-based persistence, distance policy and cap classification. The generic animal exemption is unsuitable for mobs whose vanilla despawn rules differ.
- **Approximate:** Village-cat spawning counts nearby beds/cats around a villager instead of the complete village spawning rules. Patrol and trader placement use a common ground search rather than their complete biome/POI/placement checks.

Source: `WorldData/MobSpawner*.cs`, `WorldData/AbstractLevel.Raids.cs`, `Entities/Mob.AI.cs`, `Entities/Factories/EntitySpawner.cs`.

### Loot, XP and explosions

- **Incomplete:** Mob drops use explicit code rather than full vanilla loot tables and damage context. Looting, rare/conditional drops, equipment-drop eligibility/damage, player or pet kill attribution and XP rewards are not universally equivalent.
- **Incomplete:** Breeding, trades and combat do not cover all advancement/statistic/criterion hooks. Breeding tracks love time but lacks a general responsible-player context.
- **Approximate:** Explosion drops create the block's matching item with a decay probability, bypassing full block loot/container contents and explosion-specific callbacks. Exposure, knockback and typed damage exceptions also need parity work.
- **Unverified:** Special item lifetimes/immunities and death presentation across reloads and special kills.

Source: individual `OnDeathAsync` methods, `Entities/Animal.cs`, `Entities/MerchantTrading.cs`, `WorldData/AbstractLevel.Explosion.cs`.

### Interactions and riding

- **Missing:** A general leash system, lead physics, fence knots, leash synchronization/restoration and multiple-lead transport behavior.
- **Incomplete:** Shared passengers are players only; mob jockeys use isolated special cases. Arbitrary passenger trees, correct seat placement, safe dismount selection and passenger/vehicle restoration are not generally supported.
- **Missing:** Functional horse-family chest inventories and inventory screens in `ChestedHorse`; equipping a chest currently supplies a flag rather than usable storage. Complete saddle/body-equipment inventory management is also absent from the horse/Nautilus interaction paths.
- **Approximate:** Mount inputs, charged jumps/dashes, braking, interaction reach and owner permissions vary from vanilla. Equipment removal/replacement and every creative/offhand/item-component combination need parity work.
- **Unverified:** Breeding, taming, bucket capture/release, conversion and cross-dimension passenger behavior for every state combination.

Source: `Entities/Mob.Riding.cs`, `Entities/AbstractHorse.cs`, `Entities/ChestedHorse.cs`, `Entities/Mob.Buckets.cs`, mount/pet interaction methods.

### Persistence and presentation

- **Incomplete:** Vanilla save interoperability. Equipment and effects use `ObsidianEquipment`/`ObsidianEffects`; many memories and timers use custom fields. Preserving unknown NBT does not make it drive behavior. Complete vanilla Brain, attribute modifiers, equipment/effect and passenger data are not interpreted.
- **Incomplete:** Numerous goal-local timers, targets, animation states and cross-references reset on reload. Generic conversion copies selected fields rather than the complete vanilla transfer policy; gossip, offers, effects and passenger relationships require explicit handling.
- **Unverified:** Owner/rider/projectile-reference restoration when entities load in different chunks/orders or dimensions.
- **Approximate:** Mob relative movement packets floor the displacement rather than using the rounded-position delta helper in `Entity.cs`; exact client position reconstruction needs parity work.
- **Incomplete:** Full sounds, particles, status events and animations. Common sound naming/volume/pitch omits species- and state-specific cases. Packet payloads, metadata indexes, late tracking and movement/animation synchronization require client validation.

Source: `Entities/Mob.Persistence.cs`, `Entities/EntityNbt.cs`, `WorldData/MobStorage.cs`, mob metadata/sound methods and tracking packets.

### Gamerules and world events

- **Missing/incomplete:** `mob_griefing` checks on several older actions, including sheep grass eating, fox berry harvesting, Enderman block pickup/placement, silverfish infestation/wake-up and Snow Golem snow placement. Generic equipment pickup likewise lacks that rule check.
- **Incomplete:** Exhaustive coverage of spawn/drop/griefing rules and specialized event contexts. Existing checks in bosses, raids and egg systems do not establish compliance for every mob action.
- **Incomplete:** Full vibration event coverage and calibrated-sensor frequency filtering. Warning escalation, pending vibrations and active sensor/shrieker timing are transient rather than a complete saved vanilla listener state.
- **Approximate:** Hive occupant timing/release and frogspawn lifecycle run through the player-nearby spawning loop; block lifecycles outside that loop and exact release/hatching conditions need parity work.

Source: `Entities/Sheep.cs`, `Entities/Fox.cs`, `Entities/Enderman.cs`, `Entities/Silverfish.cs`, `Entities/SnowGolem.cs`, `Entities/Mob.Equipment.cs`, `WorldData/AbstractLevel.GameEvents.cs`, `WorldData/MobSpawner.Lifecycles.cs`.

## Bosses

| Mob/system | Missing or approximate behavior |
| --- | --- |
| Wither | Approximate flight, side-head targeting/shooting and idle-head rotation; incomplete smoke/charge particles and block-break presentation. Typed immunity and armored projectile exceptions depend on the shared damage model. Construction charge and ordinary summon initialization differ and need context-specific validation. |
| Ender Dragon | Approximate flight graph, steering, phase decisions, multipart transforms and collision attacks. Full crystal/phase/damage exceptions, breath presentation and fight recovery need validation. |
| End fight, exit portal and gateways | Simplified remote-island destination search; portal transport is player-oriented rather than general entity transport. Gateway block age/cooldown animation and complete vanilla save interoperability are incomplete. Resurrection/death interruption and portal placement still need runtime comparison. |
| Warden | Approximate anger selection, sniffing/navigation and Brain memories. Incomplete digging presentation, shield disabling and typed damage/protection rules; listener/anger state and warning escalation are not fully vanilla-equivalent. |

Source: `Entities/Wither.cs`, `Entities/EnderDragon.cs`, `Entities/Warden.cs`, their projectiles/clouds, `WorldData/AbstractLevel.EndFight.cs` and `WorldData/AbstractLevel.GameEvents.cs`.

## Villagers, traders and raids

| Mob/system | Missing or approximate behavior |
| --- | --- |
| Villager | Full profession offer pools/random selection and component-sensitive enchanted, dyed and explorer-map trades. Gossip/reputation/cure/Hero discounts; full schedules, POI competition, food inventory/sharing, farming, breeding, village golem spawning, social behavior and hero gifts. Restock/work/sleep behavior uses a reduced model. |
| Zombie Villager | Full gossip/offer/discount transfer through infection and cure; cure-player reputation and complete equipment/conversion rules. Copying villager data and XP does not restore the full merchant state. |
| Wandering Trader | Complete randomized offer selection, potion/enchanted-tool components and vanilla wander/avoid/drink/despawn interaction rules. |
| Trader Llama | Actual trader leash, detachment/lead drops and caravan behavior; full trader-linked despawn/taming/loading rules. |
| Raids | Exact wave bonuses, mounted raiders, equipment progression, captain banner components/replacement, witch support, celebration and village/raid-center rules. Nearby loaded raiders determine wave completion, so unloaded or distant raiders are not tracked with vanilla semantics. |
| Patrols | Full patrol leader/follower navigation, recruitment, biome exclusions and placement rules. Captain equipment is a plain white banner rather than the full ominous banner. |
| Merchant interface | Complete container click modes, input-order handling and component matching; exact trade XP/reward rules. Current handling covers pickup and quick-move paths, with a fixed player XP reward per completed trade. |

Source: `Entities/Villager*.cs`, `Entities/ZombieVillager.cs`, `Entities/WanderingTrader*.cs`, `Entities/TraderLlama.cs`, `Entities/Merchant*.cs`, `WorldData/AbstractLevel.Villages.cs` and `WorldData/AbstractLevel.Raids.cs`.

## Animals, pets and mounts

| Mob(s) | Missing or approximate behavior |
| --- | --- |
| Allay | Reduced inventory/item matching, Brain memories and delivery/throw trajectories; complete note-block/music memory and duplication behavior need validation. |
| Armadillo | Explicit threat list instead of full threat tags/context; exact shell timing, sounds and damage exceptions remain incomplete. Riding-player checks exist, but full mounted/leashed/environment and brushing/baby interaction parity is unverified. |
| Axolotl | Approximate play-dead trigger/healing, hunting categories/cooldowns and player-assistance rewards; reduced aquatic Brain/navigation and dry-state handling. |
| Bat | Approximate flight/roost selection, support checks and wake-up rules; full ambient presentation and spawn predicate equivalence unverified. |
| Bee | Missing crop growth while carrying nectar; reduced hive/flower search, pollination and anger/sting timing. Complete smoke/fire/hive harvesting/release interactions and saved anger targets need parity work. |
| Cat | Missing bed/chest/furnace sitting, owner-sleep relaxation and morning gifts. Lying/relaxation metadata is always false; owner following lacks the distant teleport behavior. Full village/moon/variant selection is incomplete. |
| Ocelot | Simplified temptation/trust checks, avoidance and prey stalking; complete feeding movement constraints and spawn-group behavior need comparison. |
| Wolf | Missing wolf-armor equip/remove/repair and its damage absorption. Complete pack/owner-defense targeting, prey/tag rules, anger and follow/teleport conditions remain incomplete. |
| Parrot | Missing shoulder landing/release and jukebox dancing behavior. Imitation is limited to an explicit older mob list, omitting the 1.21.11 additions; following lacks full teleport/perching behavior. |
| Fox | Missing full trusted-player defense, held-food eating and glow-berry interaction; simplified sleep/pounce/prey behavior. Metadata only exposes sleeping, omitting the full crouch/pounce/sit/faceplant presentation. |
| Frog, Tadpole | Reduced long-jump/swimming/tongue decisions and water search; frog pregnancy uses a local placement scan rather than full spawn-laying behavior. Exact tongue timing, frogspawn conditions and maturation/bucket transfer need validation. |
| Goat | Approximate ram target selection/run-up/collision and long-jump trajectory/landing checks; full horn selection/drop and screaming/inheritance rules need comparison. |
| Panda | Reduced personality activities, rolling, sneezing, resting and aggression. Breeding uses a fixed nearby bamboo count; full social/unhappy/thunder behavior and animation state are incomplete. |
| Pig, Cow, Chicken, Sheep | Shared breeding/temptation/spawn-group/loot limitations. Sheep color mixing is a hard-coded subset rather than recipe-driven matching, and grazing ignores `mob_griefing`. Exact biome distributions, egg/jockey behavior and interaction presentation need validation. |
| Mooshroom | Missing lightning-driven red/brown conversion; complete flower/stew/conversion state and presentation need validation. |
| Rabbit | Approximate jumps/navigation/avoidance and crop interactions; full spawn variants, killer-rabbit behavior and conditional/looting drops need comparison. |
| Polar Bear | Approximate cub/parent defense, anger and standing/attack decisions; full target retention and presentation need comparison. |
| Horse, Donkey, Mule | Missing functional inventories and complete armor/saddle management. Horse offspring inherit a whole variant from one parent rather than full coat/marking selection; attribute inheritance/randomization and food-specific growth/healing need parity work. Charged jumping and taming/rearing are reduced. |
| Llama | Shared chest-inventory gap; missing leash caravans and complete feeding/defense/spitting context. Strength, variant, carpet and breeding probabilities need comparison. |
| Skeleton Horse | Approximate trap placement, equipment/enchantments and rider loading/steering; complete lightning/trap presentation and underwater mount behavior need validation. |
| Zombie Horse | Missing mushroom feeding/temptation, rotten-flesh drops and hostile jockey integration. Spawn attribute randomization, interaction persistence and hostile-cap/despawn semantics are incomplete. |
| Camel | Approximate charged dash, sitting transitions, step height, seat/dismount positions and rider movement; complete saddle/interaction state matrix unverified. |
| Camel Husk | Missing natural two-mob rider group and rider-directed combat; interaction persistence and the inherited camel/baby/saddle state matrix need parity work. |
| Happy Ghast | Approximate flight/braking, seat placement and standing-player stabilization; missing multiple-lead transport. Complete harness, home, healing/growth and environment rules need comparison. |
| Nautilus | Missing retaliatory/pufferfish dash combat, shell loot and equipment screen; incomplete taming-item versus food/bucket handling, roaming restrictions and interaction persistence. Owner following, riding dash scaling and effect duration need parity work. |
| Zombie Nautilus | Missing Drowned rider integration and rotten-flesh drops; hostility is modeled on the mount itself instead of solely the rider. Warm-ocean variant initialization and full shared Nautilus behavior are incomplete. |
| Sniffer | Approximate sniff/search/navigation/dig timing and explored-site memory; complete animation/sound/event behavior and egg/snifflet lifecycle conditions need comparison. |
| Strider | Approximate lava pathfinding, cold movement and riding/boost rules; missing full jockey spawning and interaction combinations. |
| Turtle | Approximate home/migration/egg-laying navigation, breeding and conditional drops; complete egg hatching/trampling predicates and presentation need comparison. |

The mount and imitation requirements above use the [official Java Edition 1.21.11 release notes](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-11) as their version baseline. Other entries follow the species implementation and the shared source references above.

## Aquatic mobs and golems

| Mob(s) | Missing or approximate behavior |
| --- | --- |
| Cod, Salmon, Tropical Fish | Reduced schooling/navigation/group data; full tropical-fish distribution and bucket/component preservation, Salmon size distributions and exact spawn predicates need comparison. |
| Pufferfish | Approximate inflation threat filtering/timing and contact behavior; complete bucket/poison interactions need validation. |
| Squid, Glow Squid | Approximate movement/tentacle rotation, fleeing/ink effects and environmental behavior; glow/dark timing and full presentation unverified. |
| Dolphin | Treasure following uses short waypoints toward a structure position rather than full search/navigation; item play only pushes a nearby item. Complete breathing, swimmer-following, jumping and group retaliation behavior is reduced. |
| Guardian, Elder Guardian | Approximate beam targeting, movement and thorns damage; exact mining-fatigue selection/timing/presentation and home restrictions need parity work. |
| Iron Golem | Missing iron-ingot repair, flower offering and iron/poppy death drops in the current class. Reduced village defense without full reputation, village patrol/home and attack-launch behavior. |
| Snow Golem | Approximate target selection, temperature/snow-placement rules and projectile behavior; snow placement ignores `mob_griefing`. |
| Copper Golem | Approximate chest search, sorting/timing and container matching; loot-table containers are skipped. Missing full statue reactivation/lightning/event rules; double-chest/open-container behavior and oxidation/wax interactions need comparison. |

Source: `Entities/Fish.cs`, `Entities/Squid.cs`, `Entities/GlowSquid.cs`, `Entities/Dolphin.cs`, `Entities/Guardian.cs`, `Entities/IronGolem.cs`, `Entities/SnowGolem.cs`, `Entities/CopperGolem.cs` and their shared systems.

## Hostile and neutral combat mobs

| Mob(s) | Missing or approximate behavior |
| --- | --- |
| Breeze | Approximate jump destinations, trajectory, attack decisions and Brain state. Missing full projectile deflection and wind-triggered button/lever/door/bell interactions; complete wind particles and spawn context need parity work. |
| Creaking | Missing natural heart spawning, resin production and the full heart lifecycle. Observation/activation uses a broad look-direction check; exact visibility, home restrictions, damage exceptions and teardown presentation remain incomplete. |
| Zombie, Husk, Drowned | Missing full jockey and spear attack behavior; reduced reinforcement, equipment/enchantment and conversion context. Drowned water/day-night hunting, beach navigation, equipment/loot probabilities and owner attribution remain approximate. |
| Skeleton, Stray, Bogged, Parched | Approximate bow aiming/strafe/target behavior, equipment/enchantments and environmental conversion. Full tipped-arrow conditional drops and shearing/presentation need comparison. |
| Wither Skeleton | Incomplete skull/conditional loot and Looting rules, equipment/enchantment initialization and structure-specific spawn context. |
| Creeper | Missing lingering effect cloud after explosion and full charged-creeper head-drop rules; exact fuse/cat avoidance, lightning charging and skeleton-kill disc context are incomplete. |
| Spider, Cave Spider | Approximate wall climbing, light/target decisions and leaps; missing full jockey and group spawn-effect initialization. Complete poison/damage immunity and cobweb behavior need parity work. |
| Slime, Magma Cube | Approximate hopping/contact attacks and split placement/state inheritance; complete spawn initialization, fluid behavior and particles need comparison. |
| Enderman | Approximate staring, anger, teleport and block-placement predicates; block pickup/placement ignores `mob_griefing`. Full indirect-damage, endermite-origin and carried-block behavior is incomplete. |
| Endermite | Missing complete player-ender-pearl origin attribution and corresponding Enderman attraction rules. |
| Silverfish | Approximate wake-up scan/infestation and damage trigger; infestation/wake-up ignores `mob_griefing`. Complete infested-block break, Silk Touch and explosion context needs parity work. |
| Blaze | Approximate hover height, burst aim/spread and navigation; full water/damage/particle and spawn-group behavior needs comparison. |
| Ghast | Approximate flight/target/fireball behavior and explosion context; full projectile deflection/owner semantics, particles and spawn constraints need comparison. |
| Zombified Piglin | Reduced group anger propagation, forgiveness, target retention and sound timing. |
| Piglin, Piglin Brute | Missing complete Brain hunting/retreat/celebration/home/group memories. Simplified admiration, inventory, bartering/provocation and equipment selection; complete conversion and loot context unverified. |
| Hoglin, Zoglin | Reduced herd retreat/group coordination, targeting and Brain behavior; exact knockback, conversion and baby rules need comparison. |
| Pillager, Vindicator | Shared raid/patrol limitations; exact crossbow/melee targeting, doors, captain behavior, wave equipment and Johnny targeting rules need comparison. |
| Evoker | Approximate spell decisions, fang placement and Vex summons; incomplete raid coordination and spell particles/animations. |
| Vex | Approximate charging, owner targeting, limited-life/summoner behavior and no-collision movement; complete lifecycle/state transfer unverified. |
| Ravager | Incomplete riders, block destruction, raid context and exact attack/stun/roar sequencing. |
| Witch | Approximate drink/splash selection, effect strength and timing; incomplete raid healing and typed damage exceptions. |
| Phantom | Approximate flight/swoop/target decisions and insomnia spawning conditions; full cat avoidance, group spawning and presentation need comparison. |
| Shulker | Approximate attachment/teleport checks and bullet homing; missing full bullet-hit reproduction/duplication rules. |
| Illusioner | Incomplete visual duplicate positions and illusion/spell presentation; combat/spell target rules need comparison. |
| Giant | No attack AI is expected for vanilla Java; remaining gaps are the shared damage, physics, interaction and presentation limitations. |

Source: the corresponding classes in `Entities`, `Entities/AI/MobGoals.cs`, mob projectiles, `WorldData/MobSpawner*.cs`, `WorldData/AbstractLevel.Raids.cs` and `WorldData/AbstractLevel.Explosion.cs`.

