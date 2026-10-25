"""
Drives a headless vanilla server (the generated 1.21.11 server jar) as the oracle for redstone parity.

The server runs with its game time frozen. Each game tick is advanced explicitly with `tick step 1` and completed
before the next command runs: commands execute between ticks, so once `time query gametime` reports the new time the
tick has finished. Block states are read with `execute if block`, which matches a partial state, so each property is
resolved on its own from the domains in `Obsidian/Assets/blocks.json`.
"""

from __future__ import annotations

import json
import shutil
import socket
import struct
import subprocess
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
BLOCKS_JSON = REPO / "Obsidian" / "Assets" / "blocks.json"
VANILLA_JAR = REPO / "Obsidian.AssetGenerator" / "obj" / "vanilla" / "1.21.11" / "server.jar"
PASSWORD = "obsidian-oracle"
RCON_PORT = 25599
GAME_PORT = 25598
SYMBOLS = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"


class Rcon:
    def __init__(self, port: int = RCON_PORT):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=30)
        self._send(1, 3, PASSWORD)
        if self._recv()[0] == -1:
            raise RuntimeError("RCON login failed")

    def _send(self, request_id: int, kind: int, body: str) -> None:
        payload = body.encode() + b"\0\0"
        self.sock.sendall(struct.pack("<iii", len(payload) + 8, request_id, kind) + payload)

    def _recv(self) -> tuple[int, str]:
        length = struct.unpack("<i", self._read(4))[0]
        data = self._read(length)
        request_id = struct.unpack("<i", data[:4])[0]
        return request_id, data[8:-2].decode(errors="replace")

    def _read(self, count: int) -> bytes:
        chunks = b""
        while len(chunks) < count:
            part = self.sock.recv(count - len(chunks))
            if not part:
                raise ConnectionError("RCON connection closed")
            chunks += part
        return chunks

    def command(self, text: str) -> str:
        self._send(2, 2, text)
        return self._recv()[1]

    def close(self) -> None:
        self.sock.close()


class VanillaOracle:
    """Owns the server process for one work directory and exposes the operations the scenarios need."""

    def __init__(self, work_directory: Path):
        self.work = work_directory
        self.process: subprocess.Popen | None = None
        self.rcon: Rcon | None = None
        self.blocks = json.loads(BLOCKS_JSON.read_text())

    # Lifecycle ----------------------------------------------------------------------------------------------------

    def prepare(self) -> None:
        """Writes the server files. The world is reset so every generation starts from the same vanilla world."""
        self.work.mkdir(parents=True, exist_ok=True)
        jar = self.work / "server.jar"
        if not jar.exists() or jar.stat().st_mtime < VANILLA_JAR.stat().st_mtime:
            shutil.copy2(VANILLA_JAR, jar)
        shutil.rmtree(self.work / "world", ignore_errors=True)
        self._write_freeze_datapack()
        (self.work / "eula.txt").write_text("eula=true\n")
        (self.work / "server.properties").write_text("\n".join([
            "online-mode=false",
            "level-type=minecraft:flat",
            "generate-structures=false",
            "spawn-protection=0",
            "spawn-monsters=false",
            "spawn-animals=false",
            "spawn-npcs=false",
            "view-distance=4",
            "simulation-distance=4",
            "max-tick-time=-1",
            "enable-command-block=false",
            "initial-enabled-packs=vanilla,file/oracle",
            "function-permission-level=4",
            "enable-rcon=true",
            f"rcon.port={RCON_PORT}",
            f"rcon.password={PASSWORD}",
            f"server-port={GAME_PORT}",
            "",
        ]))

    def _write_freeze_datapack(self) -> None:
        """
        A datapack whose load function freezes time and sets the gamerules. Load functions run while the server starts,
        so a restart cannot advance the world beyond the one tick the server runs before them. Freezing over RCON after
        the server is up would race with the ticks that run in between.
        """
        pack = self.work / "world" / "datapacks" / "oracle"
        (pack / "data" / "oracle" / "function").mkdir(parents=True, exist_ok=True)
        (pack / "data" / "minecraft" / "tags" / "function").mkdir(parents=True, exist_ok=True)
        (pack / "pack.mcmeta").write_text(json.dumps({"pack": {"description": "Obsidian redstone oracle",
                                                               "pack_format": 81, "supported_formats": [17, 81]}}))
        (pack / "data" / "oracle" / "function" / "freeze.mcfunction").write_text(
            "tick freeze\ngamerule random_tick_speed 0\n")
        (pack / "data" / "minecraft" / "tags" / "function" / "load.json").write_text(
            json.dumps({"values": ["oracle:freeze"]}))

    def start(self) -> None:
        log = self.work / "server.log"
        self.process = subprocess.Popen(["java", "-jar", "server.jar", "--nogui"], cwd=self.work,
                                        stdin=subprocess.DEVNULL, stdout=open(log, "a"), stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 300
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError(f"Server exited with {self.process.returncode}; see {log}")
            try:
                self.rcon = Rcon()
                break
            except OSError:
                time.sleep(0.5)
        else:
            raise RuntimeError("Timed out waiting for RCON")
        self.rcon.command("tick freeze")
        self.rcon.command("gamerule random_tick_speed 0")

    def stop(self) -> None:
        if self.rcon is not None:
            self.rcon.command("stop")
            self.rcon.close()
            self.rcon = None
        if self.process is not None:
            self.process.wait(timeout=300)
            self.process = None

    def save_and_restart(self) -> int:
        """
        Saves the world with a flush, stops the server and starts it again on the same world. Returns how many game
        ticks the restart advanced the world before the load function froze it (one, every time this was measured).
        """
        saved = self.game_time()
        self.rcon.command("save-all flush")
        self.stop()
        self.start()
        return self.game_time() - saved

    # Ticks ----------------------------------------------------------------------------------------------------------

    def game_time(self) -> int:
        reply = self.rcon.command("time query gametime")
        return int(reply.rsplit(" ", 1)[-1])

    def step(self) -> None:
        """Runs exactly one game tick and returns once it has completed."""
        before = self.game_time()
        reply = self.rcon.command("tick step 1")
        if "Stepping" not in reply:
            raise RuntimeError(f"tick step failed: {reply}")
        deadline = time.monotonic() + 60
        while self.game_time() <= before:
            if time.monotonic() > deadline:
                raise RuntimeError("Timed out waiting for the stepped tick")
            time.sleep(0.005)

    # Commands and queries -------------------------------------------------------------------------------------------

    def run(self, command: str) -> str:
        reply = self.rcon.command(command)
        if reply.startswith(("Incorrect", "Unknown", "That position", "Unknown or incomplete")):
            raise RuntimeError(f"`{command}` failed: {reply}")
        return reply

    def set_block(self, position: tuple[int, int, int], state: str) -> None:
        x, y, z = position
        self.run(f"setblock {x} {y} {z} {state} replace")

    def force_load(self, minimum: tuple[int, int], maximum: tuple[int, int]) -> None:
        self.run(f"forceload add {minimum[0]} {minimum[1]} {maximum[0]} {maximum[1]}")

    def matches(self, position: tuple[int, int, int], state: str) -> bool:
        x, y, z = position
        return self.rcon.command(f"execute if block {x} {y} {z} {state}") == "Test passed"

    def read_state(self, position: tuple[int, int, int], candidates: list[str]) -> str:
        """Returns the full canonical state at a position, e.g. ``minecraft:repeater[delay=2,facing=east,...]``."""
        block = next((candidate for candidate in candidates if self.matches(position, candidate)), None)
        if block is None:
            raise RuntimeError(f"{position}: none of {candidates} matches; add the block to the scenario")
        if block == "minecraft:air":
            return block

        definition = self.blocks[block]
        properties = definition.get("properties", {})
        values = {}
        for name, domain in properties.items():
            value = next((candidate for candidate in domain
                          if self.matches(position, f"{block}[{name}={candidate}]")), None)
            if value is None:
                raise RuntimeError(f"{position}: {block} has no {name} value in {domain}")
            values[name] = value
        if not values:
            return block
        return block + "[" + ",".join(f"{name}={values[name]}" for name in sorted(values)) + "]"


def symbol_palette(states: list[str]) -> dict[str, str]:
    """Maps each distinct state to a one-character symbol in the order of first use."""
    if len(set(states)) > len(SYMBOLS):
        raise ValueError("Too many distinct states for one checkpoint")
    return {state: SYMBOLS[index] for index, state in enumerate(dict.fromkeys(states))}


def timed_rows(min_pos: tuple[int, int, int], max_pos: tuple[int, int, int], states: dict[tuple[int, int, int], str]):
    """Returns the palette and rows of a box: one string per (y, z) with a symbol per x, y then z ascending."""
    palette = symbol_palette([states[(x, y, z)] for y in range(min_pos[1], max_pos[1] + 1)
                              for z in range(min_pos[2], max_pos[2] + 1) for x in range(min_pos[0], max_pos[0] + 1)])
    rows = []
    for y in range(min_pos[1], max_pos[1] + 1):
        for z in range(min_pos[2], max_pos[2] + 1):
            rows.append("".join(palette[states[(x, y, z)]] for x in range(min_pos[0], max_pos[0] + 1)))
    return list(palette), rows
