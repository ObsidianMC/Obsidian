# Mob AI plan: one goal system for vanilla and plugin mobs

A plan for reworking how mobs think: the goal selector, navigation and path search, and the plugin API over them.
Today vanilla mobs run on a capable server-side goal system that plugins can barely reach, and plugin mobs start with
no AI at all. The plan gives both one system, exposed through an API shaped like the rest of Obsidian's plugin API,
then fixes the navigation and wandering parity gaps the review below found.

Written 2026-11-15 against `26.3.x` at `7d840bc4d`. Line numbers are as of that commit. Nothing was run; the review
read the code. The parity items were then checked against a decompile of the named 26.3 server jar
(`server-26.3.jar`, SHA-256 `A362163E…8AE6`) and corrected; class and method names below are vanilla's.

Related: [mob parity](mob-parity.md) (per-species gaps), [mob parity follow-up](mob-parity-follow-up.md),
[plugin entities](known-gaps/plugin-entities.md), [plugin content](plugin-content.md#entities).

## Where it stands

- `GoalSelector` (`Obsidian/Entities/AI/Goal.cs`) follows vanilla's model: priorities, Move/Look/Jump/Target flags,
  interruption, and goal selection every other tick with `RequiresUpdateEveryTick` goals ticked in between.
- 67 mob classes fill an action selector and a target selector in `RegisterGoals(actions, targets)`. There are 48
  goal classes, 83 `CanUse` overrides and 61 `Start` overrides.
- `Navigator` follows a path with vanilla's stuck checks; `PathFinder` is an A* search over a vanilla-style
  `WalkNodeEvaluator`, with `VolumeMovement` for flying and swimming mobs.
- Plugins see `IEntity.GoalController` (`IGoal.Run()`), `IEntity.Navigator` (`INavigator`), and
  `IPathfinder`/`IPathBody` for path searches of their own.

## Problems

### Plugin API

1. `IGoal` has only `Run()`: no `CanUse`, `CanContinue`, `Start`, `Stop`, flags or priority. The server's `Goal`
   isn't plugin API.
2. A plugin's `IGoal` is wrapped as a `LegacyGoal` with priority `int.MaxValue` holding Move, Look and Jump
   (`Goal.cs:201`). On a vanilla mob any running stroll, look or attack goal blocks it, though `Run()` reads as
   "every tick".
3. `IGoalController` has `Pause()` but no `Resume()`, no priorities, no target selector and no way to list goals.
4. A plugin's own `IGoalController` or `INavigator` is never ticked: the tick only runs a `GoalSelector` and the
   built-in `Navigator` (`Mob.AI.cs:421-423`). Both setters are public on every `IEntity`, players and items included.
5. `INavigator` has no `Stop()`, speed or result. Its `Target` and `TargetLocation` setters skip `NavigateTo`'s
   bookkeeping. `IPathfinder` is found by casting `IEntity.Navigator`.
6. Attack target, look, jump, path malus, door opening and floating are internal.
7. `PluginMob` registers no goals, not even floating, and plugins can't reuse the vanilla goals.
8. `Obsidian.API/AI/AStarPath.cs` is internal, unreferenced and stubbed (`IsValidMove` returns `true`).
9. `Goal.Run()` blocks on async work (`Goal.cs:33`). `Navigator()` makes a navigator with no mob that silently does
   nothing; `IsNavigating` has a public setter.
10. Adding or removing a goal from a goal's own tick throws: `TickAsync` loops the list with `foreach`.

### Vanilla parity (checked against 26.3)

11. **Following an entity.** `Navigator` copies an entity target's position every tick and searches again once
    its 10–19 tick delay runs out and the target has moved more than a block (`Navigator.cs:146`, `:167`). It also
    stops within 0.5 blocks of the target (`Navigator.cs:149`). Vanilla's `PathNavigation.moveTo(Entity)` paths to
    the target's block as it was; goals ask for a new search (`MeleeAttackGoal.tick`), and block changes trigger one
    (item 13). Vanilla has no 0.5-block stop: `followThePath` advances by a width-based horizontal tolerance and a
    vertical tolerance of 1. Obsidian has no reach argument. Vanilla's reach is a Manhattan distance between path
    nodes and the target node, and both ordinary `moveTo` overloads (entity and coordinates) default it to 1;
    `MeleeAttackGoal` passes 0.
12. **Search budget and length.** The search visits at most a fixed 1024 nodes (`PathFinder.cs:258`). Vanilla's
    budget is `floor(max(follow range, requiredPathLength) × 16)`, `requiredPathLength` defaulting to 16 and
    changed by `setRequiredPathLength`; it's refreshed when the follow or tempt range changes
    (`Mob.onAttributeUpdated`), and `PathFinder.findPath` scales it by `maxVisitedNodesMultiplier` (default 1).
    `Math.Max(16, FollowRange)` (`PathFinder.cs:250`) matches vanilla's default maximum path length, but vanilla
    limits the distance walked along the path, while Obsidian checks only the straight-line distance from the start
    (`PathFinder.cs:282`).
13. **Block changes.** Nothing in Obsidian tells navigation about block changes. Mobs walk into new blocks until
    the stuck check stops them; the stuck checks stop navigation rather than search again. In vanilla,
    `ServerLevel.sendBlockUpdated` compares the old and new collision shapes, and when they differ asks each
    navigation `shouldRecomputePath`: a proximity test against the stretch between the mob and its path's end, not
    a test that the block sits on a node. `recomputePath` searches at once only if more than 20 game ticks have
    passed since the last recompute, otherwise on a later tick.
14. **Wandering targets.** `RandomPosition` (`MobGoals.cs:6-38`) draws ten candidates and keeps the best by a random
    score (or by distance from a threat when fleeing), sampling ±3 vertically, with no home restriction. Vanilla's
    `RandomStrollGoal` uses `DefaultRandomPos.getPos(mob, 10, 7)`: ten candidates, the highest
    `getWalkTargetValue` kept (the earlier one on a tie), home restriction through `GoalUtils.mobRestricted` and a
    bias toward home. `WaterAvoidingRandomStrollGoal` uses `LandRandomPos` (10, 7), or (15, 7) in water. Vanilla
    draws each offset in X, Y, Z order and tests the sampled block, where Obsidian draws X, Z, Y and searches the
    column for ground, so matching the random source and ranges alone won't reproduce candidates. Walk target
    values:

    | Class | Value |
    |---|---|
    | `PathfinderMob` | 0 |
    | `Animal` | 10 on grass, else from light |
    | `Monster` | Negative, from light |
    | `MushroomCow` | 10 on mycelium, else from light |
    | `Bee` | 10 in air, else 0 |
    | `Turtle` | 10 in water when not going home, else 10 on sand, else from light |
    | `Guardian` | In water 10 plus the light value, else as a monster |
    | `Silverfish` | 10 above an infestable block, else as a monster |
    | `Strider` | 10 in lava; negative infinity out of lava while in lava; else 0 |
    | `Hoglin` | −1 near a repellent, 10 on crimson nylium, else 0 |
    | `HappyGhast` | 0 in an occupied block; 10 in air with air above and a block two below; other air 5 |
    | `Giant` | Positive, from light |
    | `Axolotl`, `AbstractNautilus`, `Enderman`, `Pillager`, `Creaking`, `Warden` | 0 |

    "From light" is `LevelReader.getPathfindingCostFromLightLevels`.
15. **Randomness.** `Mob.Random` is `System.Random` (`Mob.AI.cs:14`). Vanilla's `Entity.random` is a
    `LegacyRandomSource` (Java's 48-bit LCG) from `RandomSource.create`, seeded by `RandomSupport.generateUniqueSeed`
    from a process-wide counter and `System.nanoTime()`, so it isn't reproducible from the world seed. Brain setup
    and sensor start delays draw from it too. Replaying vanilla needs a Java-compatible generator, the entity's
    seed controlled or captured, and vanilla's draw order. `BeeParity` is a hand-written test, not a capture replay;
    no AI behaviour is checked against vanilla captures yet.
16. **Disabled controls.** Obsidian has no disabled controls: only cancelling or pausing a whole selector
    (`Goal.cs:115`), which plugin mobs (`PluginMob.cs:183`) and several vanilla mounts (`AbstractHorse.cs:128`,
    `HappyGhast.cs:115`, `Nautilus.cs:119`, `Strider.cs:92`) do while ridden. Vanilla's `GoalSelector` has disabled
    flags, which stop running goals holding them and keep them from starting. `Mob.updateControlFlags` sets them
    every 5 ticks: a controlling passenger that's a mob disables Move, Look and Jump; riding a boat disables Jump.
    Target is never disabled, and a player steering doesn't disable anything here; mounts handle that themselves.
17. **Panic.** `PanicGoal` (`MobGoals.cs:91`) panics whenever the mob burns, or after an attacker within 100 ticks.
    Vanilla's `PanicGoal.shouldPanic` needs the last damage source, which `getLastDamageSource` keeps for 40 ticks,
    to be in the goal's damage type tag (`minecraft:panic_causes` by default, which takes in freeze, fire and lava
    damage through `panic_environmental_causes`). There's no separate burning or freezing trigger. When burning,
    vanilla's `lookForWater` searches ±5 horizontally and ±1 vertically in Manhattan order, accepts any water fluid
    (waterlogged blocks included), and doesn't search if the mob's own block has a collision shape. Obsidian checks
    the water block id only, by straight-line distance, without that guard (`MobGoals.cs:105`). Vanilla's fallback is
    `DefaultRandomPos.getPos(mob, 5, 4)`.
18. **NoAI.** Obsidian cancels the built-in selectors and navigation (`Mob.AI.cs:407`) but not a replacement
    `GoalSelector`, which keeps its running state without a `Stop`. It still calls `Travel` afterwards
    (`Mob.AI.cs:444`). Vanilla's `Mob.isEffectiveAi` is false under NoAI, so `LivingEntity.aiStep` skips
    `serverAiStep` (goals, navigation, brain, controls) and ordinary travel. `setNoAi` only sets the flag; it
    cancels nothing.
19. **Smaller divergences.**
    - `MeleeAttackGoal` doesn't add vanilla's 15-tick delay when a search fails (`MobGoals.cs:486`); Obsidian's
      navigator doesn't report failure to it.
    - `RandomStrollGoal` refuses while the mob has an attack target (`MobGoals.cs:129`). Vanilla instead refuses with
      a controlling passenger, and stops starting strolls once `noActionTime` reaches 100. Its `Next(60)` matches
      vanilla's 120-tick interval.
    - `Navigator` moves to the next node by tolerance only (`Navigator.cs:195`); vanilla's `followThePath` also
      skips ahead at corners in the direction of travel.

### Performance and tidiness

20. Hot paths read attributes by name (`"minecraft:generic.movement_speed"`, step height), each resolving the name
    and taking a lock; about 100 sites use the old `generic.` names.
21. `WalkNodeEvaluator.GetNeighbors` is a `yield` iterator (an allocation per expanded node), `Node` is a class, the
    ground cache key holds a `double`, and `MeleeAttackGoal` falls back to `new PathFinder(...)` (`MobGoals.cs:468`).
22. Path code reads block state ids through `block.GetHashCode()`.
23. Goal constructor parameters keep the name of the first mob they were written for (`TemptGoal(Animal pig, …)`,
    `NearestAttackableTargetGoal(PathfinderMob zombie, …)`).

## The API

The rest of the plugin API registers content with a properties record, hangs optional hooks on a behaviour interface
with default methods, names things with string identifiers and dispatches cancellable `…EventArgs`. AI follows the
same shape, and vanilla mobs' `RegisterGoals` goes through the same builder plugins get, so there is one system.

### Goals

```csharp
// Obsidian.API/AI/MobGoal.cs; the server's Goal derives from it.
public abstract class MobGoal
{
    /// <summary>The controls the goal holds while it runs; goals sharing one don't run together.</summary>
    public virtual GoalControls Controls => GoalControls.Move;

    public virtual bool Interruptible => true;

    /// <summary>Whether the goal ticks every tick rather than on selection ticks only.</summary>
    public virtual bool TickEveryTick => false;

    public abstract bool CanStart();

    public virtual bool CanContinue() => CanStart();

    public virtual ValueTask StartAsync() => default;

    public virtual ValueTask TickAsync() => default;

    public virtual void Stop() { }
}

[Flags]
public enum GoalControls { None = 0, Move = 1, Look = 2, Jump = 4, Target = 8 }
```

Which members are async (decided 2026-11-15):

- **`TickAsync` and `StartAsync` return `ValueTask`.** Ticks already await attacks, damage and events, and target
  goals set the target in `StartAsync`, which raises an async cancellable event. `ValueTask` matches
  `IEntityBehavior` and the existing selector; a goal that completes synchronously allocates nothing.
- **`CanStart`, `CanContinue` and `Stop` stay synchronous.** The checks run for every idle goal of every mob on each
  selection tick and must be pure: an await between "can start" and "start" lets the world change. `Stop` is called
  from synchronous paths (removal, cancel, pause, `NoAI`, level change, plugin unload).
- **Rule, documented on the class:** a goal may await work that finishes within the tick (damage, events, packets),
  never I/O or delays. The selector awaits goals in turn, so a goal awaiting a delay stalls the level's tick. Before
  committing to this, confirm where the level tick resumes after a real await; if continuations can leave the tick
  thread, the "tick thread only" contract needs enforcing.

### Goal sets

```csharp
public interface IGoalSet
{
    /// <summary>Adds a goal under an identifier, such as <c>ruby:guard_ore</c>; lower priorities win.</summary>
    void Add(int priority, string id, MobGoal goal);

    bool Remove(string id);

    bool Replace(string id, MobGoal with);

    IReadOnlyList<GoalEntry> Entries { get; }

    /// <summary>Stops goals holding these controls and keeps them from starting (vanilla's disabled flags).</summary>
    void Disable(GoalControls controls);

    void Enable(GoalControls controls);
}

public sealed record GoalEntry(int Priority, string Id, MobGoal Goal, bool Running);
```

- Built-in goals get stable ids (`minecraft:random_stroll`, `minecraft:melee_attack`, …). Vanilla has no goal ids;
  these are Obsidian's, chosen to match vanilla's class names in snake case.
- Changes made during a tick (from a goal, an event or a behaviour hook) are queued and applied when the selector's
  tick ends, which fixes problem 10.

### Mobs and navigation

```csharp
public interface IMob : ILiving
{
    IMobNavigation Navigation { get; }

    IMobControls Controls { get; }          // LookAt, Jump, MoveTo: the raw move, look and jump controls

    ILiving? Target { get; }

    /// <summary>Sets the attack target through a cancellable MobTargetEventArgs; false if cancelled.</summary>
    ValueTask<bool> SetTargetAsync(ILiving? target, TargetReason reason);

    IGoalSet Goals { get; }

    IGoalSet TargetGoals { get; }

    bool AiEnabled { get; set; }            // vanilla's NoAI
}

public interface IMobNavigation
{
    NavigationPath? Path { get; }

    NavigationStatus Status { get; }        // Idle, Moving, Reached, Partial, Stuck, NoPath

    // Reach is vanilla's: a Manhattan distance in nodes, 1 by default for both overloads as in vanilla.
    bool MoveTo(VectorD target, float speed = 1, int reach = 1);

    bool MoveTo(IEntity target, float speed = 1, int reach = 1);

    /// <summary>For scripted code, not goals: completes when the move ends, however it ends.</summary>
    ValueTask<NavigationStatus> MoveToAsync(
        VectorD target,
        float speed = 1,
        int reach = 1,
        CancellationToken cancellationToken = default);

    void Stop();

    /// <summary>Searches synchronously on the tick, as vanilla's goals need the path in the same tick.</summary>
    NavigationPath? FindPath(VectorD target, int reach = 1);

    PathRules Rules { get; }                // malus per PathNodeType, CanOpenDoors, CanFloat, MaxFall
}
```

- `MoveTo`, `Stop` and `FindPath` stay synchronous: moving only records a request, and the search is CPU work on
  the tick with per-thread buffers.
- `MoveToAsync` is backed by one reusable completion source per navigator (no allocation per move). It always
  completes: `Cancelled` when another move or `Stop` replaces it, the mob is removed or changes level, or the
  awaiting plugin unloads, since a pending continuation would keep the plugin loaded. It completes after the
  navigator's tick, not inside it, so a continuation that moves again doesn't re-enter the navigator. A `ValueTask`
  can be awaited once; the docs say so.
- `Target` is read-only and `SetTargetAsync` raises `MobTargetEventArgs` (`EntityEventArgs`, `ICancellable`)
  through `IEventDispatcher.ExecuteEventAsync`. This is why `StartAsync` is async.

### Wiring it up

```csharp
public interface IAiBuilder
{
    IGoalSet Goals { get; }

    IGoalSet TargetGoals { get; }

    PathRules Navigation { get; }

    IVanillaGoals Vanilla { get; }          // factories for the built-in goals; the API can't see server types
}

public interface IEntityBehavior
{
    // New optional hook, run once per mob when its AI starts, like the server's RegisterGoals.
    void ConfigureAi(IMob mob, IAiBuilder ai) { }
}
```

```csharp
sealed class GolemBehavior : IEntityBehavior
{
    public void ConfigureAi(IMob mob, IAiBuilder ai)
    {
        ai.Goals.Add(0, "minecraft:float", ai.Vanilla.Float());
        ai.Goals.Add(1, "minecraft:melee_attack", ai.Vanilla.MeleeAttack(speed: 1));
        ai.Goals.Add(4, "ruby:guard_ore", new GuardOreGoal(mob));
        ai.Goals.Add(5, "minecraft:random_stroll", ai.Vanilla.RandomStroll(speed: 0.8f));
        ai.Goals.Add(6, "minecraft:look_at_player", ai.Vanilla.LookAtPlayer(range: 8));

        ai.TargetGoals.Add(1, "minecraft:hurt_by_target", ai.Vanilla.HurtByTarget(alertSameType: true));
        ai.TargetGoals.Add(2, "minecraft:nearest_attackable_target", ai.Vanilla.NearestTarget<IPlayer>(mustSee: true));

        ai.Navigation.SetMalus(PathNodeType.Water, -1);
    }
}
```

- A global `MobAiConfiguringEventArgs` hands plugins `(IMob, IAiBuilder)` for every mob, vanilla ones included, so a
  plugin can remove or replace `minecraft:random_stroll` on zombies without touching server types.
- Ids could instead be supplied by the goal (a virtual `Id`), which makes `Add` shorter. Settle this in phase 1.

### Compatibility

`IGoal`, `IGoalController` and `INavigator` stay for one release as `[Obsolete]` adapters over the new API, then go.
`IEntity.Navigator` and `IEntity.GoalController` move to `IMob`. The plugin unload sweep keeps working: goals are
still plain classes whose assembly decides their plugin (`GoalSelector.RemoveGoalsFrom`, the `PluginGoal` wrapper).

## Phases

Each phase is one PR against `26.3.x`. Phases 2 to 4 can run in parallel once phase 1 lands; phase 5 is separate.

### Phase 1: the API and one goal system

Server:

- `Goal` derives from `MobGoal`. Rename `CanUse` to `CanStart`, `Start` to `StartAsync`, `Flags` to `Controls`,
  `IsInterruptible` to `Interruptible` and `RequiresUpdateEveryTick` to `TickEveryTick` across the 48 goal classes.
  This is mechanical and can go to a builder; it changes no behaviour.
- `GoalSelector` implements `IGoalSet`: ids, queued mutation during a tick, `Disable`/`Enable`. `RegisterGoals`
  becomes `ConfigureAi(IAiBuilder)` on `Mob`, and the 67 registrations gain ids.
- `Mob` implements `IMob` (`Target`, `SetTargetAsync` with `MobTargetEventArgs`, `AiEnabled`); `Navigator`
  implements `IMobNavigation` with `Status`, `Stop`, speed and `PathRules`, without `MoveToAsync` yet.
- `PluginMob` calls `IEntityBehavior.ConfigureAi`; raise `MobAiConfiguringEventArgs` for every mob.
- Riding uses disabled controls as vanilla's `updateControlFlags` does, every 5 ticks (problem 16); mounts that
  pause their whole selector today switch to it where vanilla's do.
- `NoAI` skips the whole AI step and ordinary travel, as vanilla's does, and stops a replacement selector's goals
  too (problem 18). Disabled controls alone aren't enough for it.
- Delete `AStarPath.cs`, `Goal.Run()`, the public `Navigator()` constructor and the public `IsNavigating` setter.
- Wrap `IGoal`, `IGoalController` and `INavigator` as `[Obsolete]` adapters.

Docs: the AI section of [plugin content](plugin-content.md#entities); a known-gaps entry for what's left.

Tests (Obsidian-only risk, per AGENTS.md):

- A plugin mob configured through `ConfigureAi` runs a plugin goal and a vanilla goal, and unloading the plugin
  releases both (extend `PluginUnloading`).
- A goal that adds and removes goals from its own tick doesn't throw, and the change applies after the tick.
- `MobTargetEventArgs` cancelled by a handler leaves the target unchanged.

No hand-written gameplay tests. The existing non-worldgen suite must stay green, since this phase changes no
behaviour beyond the plugin surface and `Disable`.

### Phase 2: navigation parity

Problems 11 to 13, the navigation parts of 19, and `MoveToAsync`.

- Entity targets get a path to where the target was; goals decide when to search again (most already do, as
  `MeleeAttackGoal` does). Drop the 0.5-block stop. Add reach as a node Manhattan distance, 1 by default.
- The visit budget as vanilla's (follow range or required length, times 16, refreshed on attribute changes), and a
  limit on the distance walked along the path rather than from the start.
- Search again after block changes as vanilla does: on a collision-shape change, ask each navigation in the level
  whether the block is near its remaining path, and recompute at most once per 20 ticks, deferring otherwise.
  Find where block changes are published per level (`AbstractLevel.BlockUpdates.cs`).
- Report search failure to goals, so `MeleeAttackGoal` can add vanilla's 15-tick delay; corner skipping in path
  following.
- `MoveToAsync` with the completion rules above, and a test that it completes `Cancelled` on unload and removal.

Parity evidence: capture navigation scenarios from vanilla with the scenario tooling
([scenario captures](scenario-captures.md)): a mob following a moving target, a path blocked mid-walk, and a long
path at the visit cap. These need phase 3's random source to replay exactly; until then compare paths for
deterministic setups only (no random draws on the way).

### Phase 3: wandering and randomness

Problems 14, 15, 17 and the stroll part of 19.

- Give each mob a Java-compatible `LegacyRandomSource` and draw from it in vanilla's order. Vanilla seeds it from
  the clock, so it can't match a live server; what matters is that a capture can fix or record the seed and the
  replay sets the same one. The capture tooling needs a way to do that.
- `RandomPosition` as vanilla's `DefaultRandomPos` and `LandRandomPos`: X, Y, Z draws, the sampled block tested,
  vanilla's ranges per caller, `getWalkTargetValue` per class (the table in problem 14), home restriction and the
  bias toward home.
- `RandomStrollGoal` eligibility: controlling passenger and `noActionTime`, not the attack target.
- `PanicGoal` triggered by recent damage in the goal's damage type tag (default `minecraft:panic_causes`), with no
  separate burning or freezing check; vanilla's water search and `(5, 4)` fallback.
- One scenario replay for wandering (a stroll from a fixed seed) and one for panic.

### Phase 4: performance and tidying

Problems 20 to 23. Benchmark first with the load test (`Obsidian.LoadTest`) and a mob-heavy level, so each change is
kept only if it shows.

- Typed attribute handles or per-tick cached values for movement speed, step height and follow range.
- Neighbour search without iterator allocation; struct nodes or pooled nodes; an integer ground-cache key.
- A state id on `IBlock` instead of `GetHashCode()`.
- Rename goal constructor parameters to `mob`.

### Phase 5 (separate plan): brains

Vanilla 26.3 drives villagers, piglins, piglin brutes, hoglins, zoglins, axolotls, goats, camels, camel husks,
allays, frogs, tadpoles, sniffers, breezes, armadillos, wardens, creakings, copper golems, nautiluses, zombie
nautiluses and baby happy ghasts with brains (memories, sensors, activities); adult happy ghasts use goals.
Obsidian runs them on approximate goals, so their timing and random draws can't match ([mob parity](mob-parity.md#ai-movement-and-targeting)). Parity needs a brain layer:
typed memories (`MemoryKey<T>`, shaped like data components and saved to vanilla's `Brain` NBT), sensors on vanilla's
intervals, and activities. `IMob.Memories` would also give plugins state shared across goals. This is large enough to
need its own plan after phases 1 to 3.

## Open questions

1. Goal ids: passed to `Add`, or a virtual `Id` on `MobGoal`?
2. Does the level tick resume on its own thread after a goal awaits something that truly waits? The async contract
   depends on it.
3. Should `MobAiConfiguringEventArgs` fire for mobs loaded from saves as well as new spawns? Probably yes, since AI
   isn't saved.
4. How long do the `[Obsolete]` adapters stay: one release, or until the 26.3 port ships?
