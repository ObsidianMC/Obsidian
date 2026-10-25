"""
Redstone parity scenarios for R0 (see docs/redstone/01-foundation.md).

Coordinates are local to a scenario origin: x and z are offsets from the origin, and y is absolute (the floor is at
y=63 and circuits are built at y=64). Each scenario is run on the vanilla oracle and its per-tick block states are
written to Obsidian.Tests/Assets/redstone_scenarios.json by generate.py.

Scenario timing follows the same rule as the fluid fixtures: setup blocks are placed first, live, in order, with
neighbor updates (like /setblock). Then for every tick t from 1 to `ticks`: the tick's reloads and actions happen
first, at the previous game time, and then the game advances one tick and its scheduled work runs. Checkpoint t is
taken after that tick (checkpoint 0 is taken right after setup).
"""

from __future__ import annotations

from dataclasses import dataclass, field

FLOOR_Y = 63
CIRCUIT_Y = 64
BOX_Y = (63, 65)
ORIGIN_Z = 8


@dataclass
class Action:
    tick: int
    pos: tuple[int, int, int]  # local x, absolute y, local z
    state: str


@dataclass
class EntityEvent:
    """
    An entity event at a tick. An armor stand (a non-spectator entity that triggers tripwires) can be summoned at a local position
    with its feet at the given absolute y, or every armor stand can be killed. `contacts` is the set of local cells, with absolute y,
    that the entities touch from this tick on; the replay applies it as the entity contact for that tick.
    """
    tick: int
    summon: tuple[int, int, int] | None = None
    kill: bool = False
    contacts: list[tuple[int, int, int]] = field(default_factory=list)


@dataclass
class ArrowEvent:
    """
    An arrow shot: vanilla summons it on `tick` at `origin` (local x and z, absolute y, as floats) moving by `motion`. It enters
    `block` (local) through `face` at `location` (local, floats) during that tick's step. The replay injects the same hit.
    """
    tick: int
    origin: tuple[float, float, float]
    motion: tuple[float, float, float]
    block: tuple[int, int, int]
    location: tuple[float, float, float]
    face: str


@dataclass
class Scenario:
    name: str
    description: str
    coverage: list[str]
    floor: tuple[int, int, int, int]  # local x0, x1, z0, z1 of the stone floor
    box: tuple[int, int, int, int]  # local x0, x1, z0, z1 of the checked region (y is BOX_Y)
    setup: list[tuple[tuple[int, int, int], str]] = field(default_factory=list)
    actions: list[Action] = field(default_factory=list)
    reloads: list[int] = field(default_factory=list)
    entities: list[EntityEvent] = field(default_factory=list)
    arrows: list[ArrowEvent] = field(default_factory=list)
    ticks: int = 10


def lever_floor(powered: bool) -> str:
    return f"minecraft:lever[face=floor,facing=north,powered={str(powered).lower()}]"


def scenarios() -> list[Scenario]:
    lever = lever_floor
    dust = "minecraft:redstone_wire"
    # A repeater's facing names its input side: power enters from the block at its facing and leaves on the opposite side.
    repeater = "minecraft:repeater[delay={delay},facing=west,locked=false,powered=false]"

    return [
        Scenario(
            name="source_timing_lever",
            description="A floor lever feeds a four-block dust line. The lever is on at tick 2, off at 6, on at 10 and off "
                        "at 14, so the dust's on and off timing is observed at each change.",
            coverage=["source_timing", "dust_attenuation"],
            floor=(0, 5, 0, 2),
            box=(0, 5, 0, 2),
            setup=[((0, CIRCUIT_Y, 1), lever(False))] + [((x, CIRCUIT_Y, 1), dust) for x in range(1, 5)],
            actions=[Action(2, (0, CIRCUIT_Y, 1), lever(True)), Action(6, (0, CIRCUIT_Y, 1), lever(False)),
                     Action(10, (0, CIRCUIT_Y, 1), lever(True)), Action(14, (0, CIRCUIT_Y, 1), lever(False))],
            ticks=16,
        ),
        Scenario(
            name="dust_attenuation",
            description="A 16-block dust line from a lever. Power falls by one per block; the lever is turned on at "
                        "tick 2 and off at tick 20.",
            coverage=["dust_attenuation", "source_timing"],
            floor=(0, 16, 0, 2),
            box=(0, 16, 0, 2),
            setup=[((0, CIRCUIT_Y, 1), lever(False))] + [((x, CIRCUIT_Y, 1), dust) for x in range(1, 17)],
            actions=[Action(2, (0, CIRCUIT_Y, 1), lever(True)), Action(20, (0, CIRCUIT_Y, 1), lever(False))],
            ticks=24,
        ),
        Scenario(
            name="repeater_delay",
            description="Lever -> dust -> repeater (delay 2, input from the west) -> lamp. The lever is on from tick 2 "
                        "to 10, so the repeater's delayed output is observed on both edges.",
            coverage=["repeater_delay", "source_timing"],
            floor=(0, 5, 0, 2),
            box=(0, 5, 0, 2),
            setup=[((0, CIRCUIT_Y, 1), lever(False)), ((1, CIRCUIT_Y, 1), dust),
                   ((2, CIRCUIT_Y, 1), repeater.format(delay=2)),
                   ((3, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]")],
            actions=[Action(2, (0, CIRCUIT_Y, 1), lever(True)), Action(10, (0, CIRCUIT_Y, 1), lever(False))],
            ticks=16,
        ),
        Scenario(
            name="repeater_lock",
            description="A repeater (delay 2) at (2,1) is locked by a second repeater (delay 1, input from the north) at "
                        "(2,0) that outputs into its side. The locking repeater is fed by a redstone block, placed at "
                        "tick 1 and removed at tick 8. The lever is on at tick 3 and off at tick 12, so the lamp only "
                        "responds once the lock is released.",
            coverage=["repeater_delay", "repeater_lock"],
            floor=(0, 5, -1, 2),
            box=(0, 5, -1, 2),
            setup=[((0, CIRCUIT_Y, 1), lever(False)), ((1, CIRCUIT_Y, 1), dust),
                   ((2, CIRCUIT_Y, 1), repeater.format(delay=2)),
                   ((3, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]"),
                   ((2, CIRCUIT_Y, 0), "minecraft:repeater[delay=1,facing=north,locked=false,powered=false]")],
            actions=[Action(1, (2, CIRCUIT_Y, -1), "minecraft:redstone_block"),
                     Action(3, (0, CIRCUIT_Y, 1), lever(True)),
                     Action(8, (2, CIRCUIT_Y, -1), "minecraft:air"),
                     Action(12, (0, CIRCUIT_Y, 1), lever(False))],
            ticks=18,
        ),
        Scenario(
            name="comparator_compare_and_subtract",
            description="A comparator (input from the west, output to the east) with a rear block (15) and side dust at 14. "
                        "Compare mode passes the rear value when it is at least the side value; subtract mode outputs the "
                        "difference. The mode changes at tick 2, the side source is removed at tick 5, the rear source at "
                        "tick 8, the mode returns to compare at tick 10 and the rear source is restored at tick 12.",
            coverage=["comparator_modes"],
            floor=(0, 5, 0, 3),
            box=(0, 5, 0, 3),
            setup=[((2, CIRCUIT_Y, 1), "minecraft:redstone_block"),
                   ((3, CIRCUIT_Y, 1), "minecraft:comparator[facing=west,mode=compare,powered=false]"),
                   ((4, CIRCUIT_Y, 1), dust),
                   ((5, CIRCUIT_Y, 2), "minecraft:redstone_block"),
                   ((4, CIRCUIT_Y, 2), dust), ((3, CIRCUIT_Y, 2), dust)],
            actions=[Action(2, (3, CIRCUIT_Y, 1), "minecraft:comparator[facing=west,mode=subtract,powered=false]"),
                     Action(5, (5, CIRCUIT_Y, 2), "minecraft:air"),
                     Action(8, (2, CIRCUIT_Y, 1), "minecraft:air"),
                     Action(10, (3, CIRCUIT_Y, 1), "minecraft:comparator[facing=west,mode=compare,powered=false]"),
                     Action(12, (2, CIRCUIT_Y, 1), "minecraft:redstone_block")],
            ticks=16,
        ),
        Scenario(
            name="observer_pulse",
            description="An observer (facing east) watches the block at (2,64,1). That block is placed at tick 2, "
                        "removed at tick 6, placed at tick 10 and removed at tick 14; the observer's back powers a lamp.",
            coverage=["observer_pulses"],
            floor=(0, 3, 0, 2),
            box=(0, 3, 0, 2),
            setup=[((0, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]"),
                   ((1, CIRCUIT_Y, 1), "minecraft:observer[facing=east,powered=false]")],
            actions=[Action(2, (2, CIRCUIT_Y, 1), "minecraft:stone"), Action(6, (2, CIRCUIT_Y, 1), "minecraft:air"),
                     Action(10, (2, CIRCUIT_Y, 1), "minecraft:stone"), Action(14, (2, CIRCUIT_Y, 1), "minecraft:air")],
            ticks=20,
        ),
        Scenario(
            name="torch_inversion",
            description="A standing redstone torch on a floor block that is itself swapped for a redstone block at tick 2 "
                        "(the torch goes out) and back to stone at tick 6 (the torch relights). A lamp beside the torch "
                        "follows it.",
            coverage=["torch_inversion"],
            floor=(0, 3, 0, 2),
            box=(0, 3, 0, 2),
            setup=[((1, CIRCUIT_Y, 1), "minecraft:redstone_torch[lit=true]"),
                   ((2, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]")],
            actions=[Action(2, (1, FLOOR_Y, 1), "minecraft:redstone_block"),
                     Action(6, (1, FLOOR_Y, 1), "minecraft:stone")],
            ticks=12,
        ),
        Scenario(
            name="torch_burnout",
            description="The torch's support alternates between a redstone block and stone every two ticks from tick 1 to "
                        "tick 33. A torch re-checks its support two ticks after a change, so each change toggles it, and "
                        "rapid toggling burns it out: after that it stays off for the rest of the scenario.",
            coverage=["torch_burnout"],
            floor=(0, 3, 0, 2),
            box=(0, 3, 0, 2),
            setup=[((1, CIRCUIT_Y, 1), "minecraft:redstone_torch[lit=true]"),
                   ((2, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]")],
            actions=[Action(tick, (1, FLOOR_Y, 1),
                            "minecraft:redstone_block" if (tick - 1) % 4 == 0 else "minecraft:stone")
                     for tick in range(1, 34, 2)],
            ticks=50,
        ),
        Scenario(
            name="save_reload_repeater",
            description="A repeater (delay 4, input from the west) is mid-delay when the world is saved and the server "
                        "restarts at tick 3, so its scheduled tick must survive the reload. The lever is on at tick 2 "
                        "and off at tick 8. The scenario is placed inside the spawn chunks (see generate.py).",
            coverage=["save_reload", "repeater_delay"],
            floor=(0, 4, 0, 2),
            box=(0, 4, 0, 2),
            setup=[((0, CIRCUIT_Y, 1), lever(False)), ((1, CIRCUIT_Y, 1), dust),
                   ((2, CIRCUIT_Y, 1), repeater.format(delay=4)),
                   ((3, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]")],
            actions=[Action(2, (0, CIRCUIT_Y, 1), lever(True)), Action(8, (0, CIRCUIT_Y, 1), lever(False))],
            reloads=[3],
            ticks=20,
        ),
        Scenario(
            name="tripwire_line",
            description="Two hooks on a stone support each, facing each other across three tripwire blocks. An armor stand touches the middle "
                        "wire from tick 2 to tick 8 and is then killed. The wire powers at tick 2 and rechecks 10 ticks later (tick 12), "
                        "when nothing touches it, so it releases then. The hooks follow it.",
            coverage=["tripwire", "tripwire_hook"],
            floor=(0, 6, 0, 2),
            box=(0, 6, 0, 2),
            setup=[((0, CIRCUIT_Y, 1), "minecraft:stone"), ((6, CIRCUIT_Y, 1), "minecraft:stone"),
                   ((1, CIRCUIT_Y, 1), "minecraft:tripwire_hook[attached=false,facing=east,powered=false]"),
                   ((5, CIRCUIT_Y, 1), "minecraft:tripwire_hook[attached=false,facing=west,powered=false]")]
                  + [((x, CIRCUIT_Y, 1), "minecraft:tripwire") for x in (2, 3, 4)],
            entities=[EntityEvent(2, summon=(3, CIRCUIT_Y, 1), contacts=[(3, CIRCUIT_Y, 1)]),
                      EntityEvent(8, kill=True, contacts=[])],
            ticks=16,
        ),
        Scenario(
            name="target_arrow",
            description="A target block on a stone floor with a lamp on its east side. The first arrow enters the west face off-centre "
                        "at tick 2 and sets power 7. The second arrow enters at the centre at tick 5 while that pulse is pending, so it "
                        "does nothing. The pulse ends 20 ticks after the first hit (tick 22), and the lamp follows the power.",
            coverage=["target", "arrow"],
            floor=(0, 6, 0, 2),
            box=(0, 6, 0, 2),
            setup=[((3, CIRCUIT_Y, 1), "minecraft:target"), ((4, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]")],
            arrows=[ArrowEvent(2, origin=(2.5, CIRCUIT_Y + 0.5, 1.25), motion=(0.6, 0.0, 0.0), block=(3, CIRCUIT_Y, 1),
                               location=(3.0, CIRCUIT_Y + 0.5, 1.25), face="west"),
                    ArrowEvent(5, origin=(2.5, CIRCUIT_Y + 0.5, 1.5), motion=(0.6, 0.0, 0.0), block=(3, CIRCUIT_Y, 1),
                               location=(3.0, CIRCUIT_Y + 0.5, 1.5), face="west")],
            ticks=26,
        ),
        Scenario(
            name="reload_target_pulse",
            description="A target is hit at tick 2 (power 15, pulse due at tick 22). The world is saved and the server restarts "
                        "at tick 10, while the pulse is still pending, so its remaining delay must survive the reload. The lamp "
                        "beside the target follows the pulse.",
            coverage=["save_reload", "target"],
            floor=(0, 4, 0, 2),
            box=(0, 4, 0, 2),
            setup=[((3, CIRCUIT_Y, 1), "minecraft:target"), ((4, CIRCUIT_Y, 1), "minecraft:redstone_lamp[lit=false]")],
            arrows=[ArrowEvent(2, origin=(2.5, CIRCUIT_Y + 0.5, 1.5), motion=(0.6, 0.0, 0.0), block=(3, CIRCUIT_Y, 1),
                               location=(3.0, CIRCUIT_Y + 0.5, 1.5), face="west")],
            reloads=[10],
            ticks=26,
        ),
        # Pistons (R8): a redstone block powers a piston from behind (its back side, not its front), and a stone or
        # obsidian block sits in front. The power is on at tick 2 and off at tick 12.
        Scenario(
            name="piston_extend_retract",
            description="A normal piston faces east with a stone block in front. Power from a redstone block behind it "
                        "extends it at tick 2 and removes the power at tick 12; the head retracts and the stone stays.",
            coverage=["piston_extend", "piston_retract"],
            floor=(0, 6, 0, 2),
            box=(0, 6, 0, 2),
            setup=[((2, CIRCUIT_Y, 1), "minecraft:piston[extended=false,facing=east]"), ((3, CIRCUIT_Y, 1), "minecraft:stone"),
                   ((1, CIRCUIT_Y, 1), "minecraft:air")],
            actions=[Action(2, (1, CIRCUIT_Y, 1), "minecraft:redstone_block"), Action(12, (1, CIRCUIT_Y, 1), "minecraft:air")],
            ticks=24,
        ),
        Scenario(
            name="piston_sticky_pull",
            description="A sticky piston faces east with a stone block in front. Power on at tick 2 and off at tick 12; on "
                        "retraction the stone is pulled back to the piston.",
            coverage=["piston_extend", "piston_retract", "sticky_pull"],
            floor=(0, 6, 0, 2),
            box=(0, 6, 0, 2),
            setup=[((2, CIRCUIT_Y, 1), "minecraft:sticky_piston[extended=false,facing=east]"), ((3, CIRCUIT_Y, 1), "minecraft:stone"),
                   ((1, CIRCUIT_Y, 1), "minecraft:air")],
            actions=[Action(2, (1, CIRCUIT_Y, 1), "minecraft:redstone_block"), Action(12, (1, CIRCUIT_Y, 1), "minecraft:air")],
            ticks=24,
        ),
        Scenario(
            name="piston_obsidian_blocked",
            description="A powered piston faces east toward obsidian. The obsidian can't move, so the piston stays retracted.",
            coverage=["piston_immovable"],
            floor=(0, 6, 0, 2),
            box=(0, 6, 0, 2),
            setup=[((2, CIRCUIT_Y, 1), "minecraft:piston[extended=false,facing=east]"), ((3, CIRCUIT_Y, 1), "minecraft:obsidian"),
                   ((1, CIRCUIT_Y, 1), "minecraft:air")],
            actions=[Action(2, (1, CIRCUIT_Y, 1), "minecraft:redstone_block")],
            ticks=12,
        ),
    ]
