# Mob AI plan: one goal system for vanilla and plugin mobs

A plan for reworking how mobs think: the goal selector, navigation and path search, and the plugin API over them.
Today vanilla mobs run on a capable server-side goal system that plugins can barely reach, and plugin mobs start with
no AI at all. The plan gives both one system, exposed through an API shaped like the rest of Obsidian's plugin API,
then fixes the navigation and wandering parity gaps the review below found.

Written 2026-11-15 against `26.3.x` at `7d840bc4d`. Line numbers are as of that commit. Nothing was run; the review
read the code. The parity items come from reading vanilla and need checking against the pinned source before they're
built.

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

### Vanilla parity (verify against the pinned source)

11. `Navigator` copies a moving target's position every tick and searches again (`Navigator.cs:146`), and stops
    within 0.5 blocks (`Navigator.cs:149`). Vanilla's `moveTo(entity)` follows a fixed path and leaves searching
    again to the goal. There is no reach (accuracy) argument.
12. The search visits at most 1024 nodes (`PathFinder.cs:258`); vanilla allows `floor(base follow range * 16)`.
    The range is `Math.Max(16, FollowRange)` (`PathFinder.cs:250`); vanilla uses the follow range.
13. A block change on a mob's path doesn't make it search again, as vanilla's does. Mobs walk into new blocks until
    the stuck check fires.
14. `RandomPosition` (`MobGoals.cs:6-38`) picks the best of ten candidates by a random score instead of
    `getWalkTargetValue` (animals prefer grass, monsters darkness), searches ±3 vertically instead of ±7, and has no
    home restriction.
15. `Mob.Random` is `new Random()` (`Mob.AI.cs:14`), not a vanilla `RandomSource`, so AI can't be replayed against
    captures. `BeeParity` is the only AI parity test.
16. Vanilla's `GoalSelector` disables controls (Move, Jump, Look) while a passenger steers or the mob sits in a boat.
    Obsidian can only pause the whole selector, and only plugin mobs do.
17. `PanicGoal` ignores the `panic_causes` damage tag and freezing, and its water search misses waterlogged blocks
    (`MobGoals.cs:105`).
18. `NoAI` cancels the built-in selector but not a replacement `GoalController` (`Mob.AI.cs:407`).

### Performance and tidiness

19. Hot paths read attributes by name (`"minecraft:generic.movement_speed"`, step height), each resolving the name
    and taking a lock; about 100 sites use the old `generic.` names.
20. `WalkNodeEvaluator.GetNeighbors` is a `yield` iterator (an allocation per expanded node), `Node` is a class, the
    ground cache key holds a `double`, and `MeleeAttackGoal` falls back to `new PathFinder(...)` (`MobGoals.cs:468`).
21. Path code reads block state ids through `block.GetHashCode()`.
22. Goal constructor parameters keep the name of the first mob they were written for (`TemptGoal(Animal pig, …)`,
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

    bool MoveTo(VectorD target, float speed = 1, int reach = 0);

    bool MoveTo(IEntity target, float speed = 1, int reach = 1);

    /// <summary>For scripted code, not goals: completes when the move ends, however it ends.</summary>
    ValueTask<NavigationStatus> MoveToAsync(
        VectorD target,
        float speed = 1,
        int reach = 0,
        CancellationToken cancellationToken = default);

    void Stop();

    /// <summary>Searches synchronously on the tick, as vanilla's goals need the path in the same tick.</summary>
    NavigationPath? FindPath(VectorD target, int reach = 0);

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
- `NoAI` and riding use `Disable` instead of pausing (problems 16 and 18).
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

Problems 11 to 13, and `MoveToAsync`.

- Entity targets follow a fixed path; goals decide when to search again (most already do, as `MeleeAttackGoal`
  does). Drop the 0.5-block stop. Add reach.
- Visit cap and range from the follow range, as vanilla.
- Search again when a changed block lies on the path. Find where block changes are published per level and have
  navigators with a path through that block search again on their next tick.
- `MoveToAsync` with the completion rules above, and a test that it completes `Cancelled` on unload and removal.

Parity evidence: capture navigation scenarios from vanilla with the scenario tooling
([scenario captures](scenario-captures.md)): a mob following a moving target, a path blocked mid-walk, and a long
path at the visit cap. These need phase 3's random source to replay exactly; until then compare paths for
deterministic setups only (no random draws on the way).

### Phase 3: wandering and randomness

Problems 14, 15 and 17.

- Give each mob a vanilla `RandomSource` seeded as vanilla seeds it, used by AI in vanilla's draw order.
- `RandomPosition` with vanilla's ranges, `getWalkTargetValue` per mob type (animals, monsters, others) and home
  restriction.
- `PanicGoal` causes from the `panic_causes` tag plus freezing; water search including waterlogged blocks.
- One scenario replay for wandering (a stroll from a fixed seed) and one for panic.

### Phase 4: performance and tidying

Problems 19 to 22. Benchmark first with the load test (`Obsidian.LoadTest`) and a mob-heavy level, so each change is
kept only if it shows.

- Typed attribute handles or per-tick cached values for movement speed, step height and follow range.
- Neighbour search without iterator allocation; struct nodes or pooled nodes; an integer ground-cache key.
- A state id on `IBlock` instead of `GetHashCode()`.
- Rename goal constructor parameters to `mob`.

### Phase 5 (separate plan): brains

Vanilla drives villagers, piglins, hoglins, axolotls, goats, camels, allays, frogs, sniffers, breezes, armadillos
and the warden with brains (memories, sensors, activities). Obsidian runs them on approximate goals, so their timing
and random draws can't match ([mob parity](mob-parity.md#ai-movement-and-targeting)). Parity needs a brain layer:
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
