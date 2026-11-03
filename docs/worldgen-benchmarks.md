# Worldgen benchmarks

How fast Obsidian generates worlds. The newest measurements come first. The sections after them are kept as history:
each names the commits it measured, and the current branch can't reproduce them.

- [26.3 against 1.21.11 after the pregeneration work](#263-against-12111-after-the-pregeneration-work) (current)
- [History: the 26.3 pipeline against the 1.21.11 generator](#history-the-263-pipeline-against-the-12111-generator)
- [History: Obsidian's 1.21.11 generator against vanilla 1.21.11](#history-obsidians-12111-generator-against-vanilla-12111)

## 26.3 against 1.21.11 after the pregeneration work

Measured on 3 November 2026 at `25bedebc` on `26.3.x`, which includes #672–#675. The 1.21.11 generator has been
removed from the branch, so its numbers come from two older commits, built and run as they are:
- **Single-threaded:** `4142363b`, the last commit whose `Obsidian.WorldgenBench serial --generator 1.21.11` still runs
  the 1.21.11 generator.
- **Server:** `28d30925`, the last commit whose server generated with the 1.21.11 generator.

### Summary

Pregenerating the overworld's 1,024 chunks around spawn, with Server GC:

| Cores | 26.3 | 1.21.11 | Speedup | World ready (26.3 / 1.21.11) | Peak memory (26.3 / 1.21.11) |
|---:|---:|---:|---:|---:|---:|
| 32 threads | **1.59 s** | 1.91 s | 1.2× | 4.27 s / 6.19 s | 790 MB / 1,014 MB |
| 16 cores | **1.43 s** | 2.04 s | 1.4× | 4.01 s / 6.07 s | 770 MB / 902 MB |
| 8 cores | **1.89 s** | 2.80 s | 1.5× | 4.49 s / 7.01 s | 685 MB / 928 MB |
| 2 cores | **11.16 s** | 12.24 s | 1.1× | 18.05 s / 23.66 s | 665 MB / 824 MB |

- **Server:** the 26.3 generator is faster in every dimension at every core count: 1.1–1.5× in the overworld,
  1.25–1.6× in the nether and 1.8–2.7× in the end.
- **World ready:** the server is joinable 24–36% sooner. That time covers all three dimensions and the spawn search.
- **Memory:** peak working set is 15–26% lower.
- **One thread:** without a server, the 26.3 generator takes 8% less time per chunk in the overworld, 42% less in the
  nether and 27% less in the end. It allocates 25%, 47% and 65% less.
- **Output:** every single-threaded run matched its reference hashes, and every server run of a build spawned at the
  same position.

### Setup

| | |
|---|---|
| Machine | AMD Ryzen 9 9950X (16 cores, 32 threads), 64 GB, Windows 11 Pro 23H2 (10.0.22631) |
| .NET | SDK 10.0.403, runtime 10.0.14, Release builds |
| 26.3 | `25bedebc` |
| 1.21.11 | `4142363b` for the single-threaded bench, `28d30925` for the server |
| GC, server | Server GC, which both builds' runtime configs ship (`ServerGarbageCollection` in `Obsidian.ConsoleApp.csproj`). One more run per build at 32 threads used workstation GC (`DOTNET_gcServer=0`), to compare with the older tables. |
| GC, bench | Workstation GC, the bench's default |

### Method

**Single-threaded:**
- Command: `Obsidian.WorldgenBench serial --dimension all`, with `--generator 1.21.11` on `4142363b`.
- Seed 12345, radius 16: 1,024 chunks per dimension, with 1,296 carved and 1,156 decorated. The overworld is around
  (6, -2), the nether around (0, 0) and the end around (6, 0).
- Three runs per build, alternating builds. The tables show medians.

**Server:**
- Each run starts `Obsidian.ConsoleApp` on a fresh world. It uses seed 12345 and `--PregenerateChunkRange=16`, which
  is 1,024 chunks per dimension, generated in the order nether, end, overworld.
- Each run is in offline mode on a free port. All runs share a structure-template cache that was filled beforehand, so
  no run downloads the server jar.
- The process starts suspended. It gets its CPU affinity mask, then resumes:
  - 32 threads: `0xffffffff`;
  - 16 cores: `0x55555555`;
  - 8 cores: `0x5555`;
  - 2 cores: `0x5`.

  The core counts are the first N physical cores without their SMT siblings.
- Three runs per build at 32, 16 and 8 cores, and two at 2 cores. The builds alternate run by run.
- Before each run, the driver waits until no other server, bench or test process is running.
- The tables show medians, with the min–max range in brackets.
- The metrics are measured as in [the vanilla comparison](#what-is-measured):
  - Pregeneration runs from the "Generating N chunks" line to the end of the progress loop.
  - CPU is `QueryProcessCycleTime`.
  - Busy cores are CPU seconds divided by pregeneration seconds.
  - A dimension's peak working set is the highest sample in its window. The whole run's is the process's peak when
    the server is ready.
- The driver is a Python script outside the repository, as before.

### Single-threaded

| Dimension | 26.3: ms per chunk (chunks/s) | 1.21.11: ms per chunk (chunks/s) | 26.3: KB allocated per chunk | 1.21.11: KB allocated per chunk |
|---|---:|---:|---:|---:|
| Overworld | **9.98 (100)** | 10.83 (92) | 264 | 354 |
| Nether | **3.66 (273)** | 6.29 (159) | 123 | 234 |
| End | **0.56 (1,791)** | 0.77 (1,298) | 42 | 119 |

Every run of both builds printed `match` for all three dimensions. The 26.3 hashes are:
- overworld: `hash 2d54ff3885c4051e nbt 38e77404a2af1eec`;
- nether: `hash 3ef4470d0a7794b8 nbt d93fe3cc43d1ee2d`;
- end: `hash 5c34c3f5867f27ac nbt 762ccf03eda84ce2`.

The 26.3 overworld takes longer per chunk than the 9.14 ms in
[the earlier comparison](#history-the-263-pipeline-against-the-12111-generator). Those timings were taken before
the 26.3 pipeline ran post-processing, final heightmaps, lighting and creature population.

**Where the overworld spends its time.** These are medians of each stage over the three runs, in ms per chunk. Each
stage includes the neighbouring chunks it needs. The 26.3 bench times biomes, noise, surface and carvers together as
one `Terrain` stage, so the 1.21.11 row is the sum of those four stages: 0.65 + 2.96 + 3.14 + 0.55.

| Stage | 1.21.11 | 26.3 | 1.21.11: KB per call | 26.3: KB per call |
|---|---:|---:|---:|---:|
| Biomes, noise, surface and carvers | 7.30 | **6.34** | 83 | 104 |
| Structure starts | 0.01 | 0.02 | 0 | 0 |
| Features | 2.92 | 2.93 | 74 | 65 |
| Post-processing | 0.06 | 0.22 | 13 | 15 |
| Light | 0.47 | 0.46 | 4 | 19 |
| **Total** | **10.75** | **9.94** | | |

### Server pregeneration

Server GC, 1,024 chunks per dimension.

**Overworld** (generated last):

| Cores | Generator | Pregen s | Chunks/s | CPU ms/chunk | Busy cores | Peak WS MB |
|---:|---|---:|---:|---:|---:|---:|
| 32 threads | 26.3 | **1.59** (1.55–1.60) | 646 (639–659) | 21.2 (20.6–21.7) | 13.6 (13.6–14.0) | 789 |
| | 1.21.11 | 1.91 (1.89–1.98) | 535 (517–543) | 23.0 (22.6–23.1) | 12.3 (11.9–12.3) | 990 |
| 16 cores | 26.3 | **1.43** (1.40–1.44) | 717 (709–733) | 17.6 (16.8–18.1) | 12.5 (12.3–13.0) | 769 |
| | 1.21.11 | 2.04 (2.01–2.07) | 501 (494–509) | 16.0 (15.8–16.0) | 7.9 (7.9–8.1) | 899 |
| 8 cores | 26.3 | **1.89** (1.84–1.96) | 543 (521–557) | 12.4 (12.2–12.9) | 6.8 (6.7–6.8) | 671 |
| | 1.21.11 | 2.80 (2.67–2.81) | 366 (365–383) | 13.7 (13.4–14.0) | 5.1 (5.0–5.1) | 926 |
| 2 cores | 26.3 | **11.16** (10.49–11.82) | 92 (87–98) | 11.9 (11.3–12.6) | 1.1 (1.1–1.1) | 664 |
| | 1.21.11 | 12.24 (12.13–12.34) | 84 (83–84) | 12.8 (12.7–13.0) | 1.1 (1.1–1.1) | 823 |

**Nether** (generated first):

| Cores | Generator | Pregen s | Chunks/s | CPU ms/chunk | Busy cores | Peak WS MB |
|---:|---|---:|---:|---:|---:|---:|
| 32 threads | 26.3 | **1.96** (1.93–2.04) | 524 (503–531) | 23.5 (23.1–24.5) | 12.3 (12.3–12.3) | 488 |
| | 1.21.11 | 2.61 (2.58–2.65) | 392 (387–396) | 34.4 (33.6–35.3) | 13.5 (13.3–13.7) | 577 |
| 16 cores | 26.3 | **1.88** (1.82–2.01) | 546 (509–563) | 21.1 (20.5–22.2) | 11.5 (11.3–11.5) | 478 |
| | 1.21.11 | 2.35 (2.35–2.37) | 435 (432–436) | 17.7 (17.6–17.9) | 7.7 (7.7–7.7) | 564 |
| 8 cores | 26.3 | **1.79** (1.75–1.86) | 571 (550–584) | 11.2 (11.1–11.4) | 6.4 (6.2–6.5) | 472 |
| | 1.21.11 | 2.66 (2.60–2.67) | 385 (384–395) | 12.3 (12.2–12.4) | 4.8 (4.7–4.8) | 537 |
| 2 cores | 26.3 | **5.16** (4.95–5.37) | 199 (191–207) | 7.3 (6.9–7.6) | 1.4 (1.4–1.5) | 447 |
| | 1.21.11 | 8.03 (7.87–8.19) | 128 (125–130) | 9.6 (9.4–9.9) | 1.2 (1.2–1.2) | 506 |

**End** (generated second):

| Cores | Generator | Pregen s | Chunks/s | CPU ms/chunk | Busy cores | Peak WS MB |
|---:|---|---:|---:|---:|---:|---:|
| 32 threads | 26.3 | **0.33** (0.33–0.34) | 3,082 (2,976–3,109) | 2.4 (2.3–2.7) | 7.3 (7.2–8.1) | 509 |
| | 1.21.11 | 0.74 (0.71–0.83) | 1,389 (1,232–1,435) | 3.4 (3.4–3.7) | 4.7 (4.6–4.9) | 656 |
| 16 cores | 26.3 | **0.30** (0.29–0.31) | 3,398 (3,267–3,477) | 2.3 (2.1–2.3) | 7.5 (7.5–7.7) | 505 |
| | 1.21.11 | 0.72 (0.69–0.72) | 1,430 (1,414–1,481) | 2.5 (2.4–2.7) | 3.6 (3.6–3.9) | 628 |
| 8 cores | 26.3 | **0.30** (0.28–0.31) | 3,435 (3,260–3,694) | 1.5 (1.4–1.5) | 5.1 (5.0–5.3) | 474 |
| | 1.21.11 | 0.80 (0.74–0.80) | 1,283 (1,280–1,391) | 1.8 (1.8–1.9) | 2.4 (2.4–2.5) | 594 |
| 2 cores | 26.3 | **0.81** (0.79–0.83) | 1,267 (1,233–1,302) | 1.1 (1.1–1.1) | 1.4 (1.4–1.4) | 516 |
| | 1.21.11 | 1.42 (1.38–1.46) | 720 (700–740) | 1.4 (1.3–1.5) | 1.0 (1.0–1.1) | 554 |

**Whole run:**

| Cores | Generator | World ready s | Startup s | Peak WS MB | Peak private MB | Overworld spawn |
|---:|---|---:|---:|---:|---:|---|
| 32 threads | 26.3 | **4.27** (4.18–4.35) | 2.44 (2.41–2.46) | 790 (781–800) | 721 | `96.5, 145.0, -31.5` |
| | 1.21.11 | 6.19 (6.06–6.22) | 2.41 (2.39–2.49) | 1,014 (1,010–1,016) | 923 | `96.5, 136.0, -31.5` |
| 16 cores | 26.3 | **4.01** (3.98–4.11) | 2.38 (2.38–2.42) | 770 (755–801) | 677 | `96.5, 145.0, -31.5` |
| | 1.21.11 | 6.07 (6.07–6.07) | 2.33 (2.32–2.41) | 902 (820–934) | 824 | `96.5, 136.0, -31.5` |
| 8 cores | 26.3 | **4.49** (4.34–4.60) | 2.48 (2.40–2.51) | 685 (667–696) | 587 | `96.5, 145.0, -31.5` |
| | 1.21.11 | 7.01 (6.91–7.01) | 2.36 (2.35–2.36) | 928 (900–937) | 837 | `96.5, 136.0, -31.5` |
| 2 cores | 26.3 | **18.05** (17.51–18.58) | 2.66 (2.54–2.79) | 665 (619–711) | 580 | `96.5, 145.0, -31.5` |
| | 1.21.11 | 23.66 (23.40–23.92) | 2.57 (2.51–2.63) | 824 (768–881) | 727 | `96.5, 136.0, -31.5` |

- Startup runs from process start until world creation begins.
- World ready runs from world creation until the server is joinable.
- Every run stopped cleanly and logged no errors.
- Both builds spawn the end at `100.5, 50.0, 0.5`.

**Workstation GC**, one run per build at 32 threads:

| Generator | Overworld s | Nether s | End s | Overworld CPU ms/chunk | World ready s | Peak WS MB |
|---|---:|---:|---:|---:|---:|---:|
| 26.3 | **1.63** | **1.85** | **0.35** | 20.6 | **4.25** | 679 |
| 1.21.11 | 2.12 | 2.65 | 0.73 | 22.8 | 6.43 | 748 |

Workstation GC generates about as fast as Server GC. Server GC's peak working set is higher: 790 MB against 679 MB
for 26.3, and 1,014 MB against 748 MB for 1.21.11.

### What changed since the previous 26.3 numbers

Four changes landed after the last 26.3 server measurement, which was at `85947875`:

- **#672: density scratch buffers.** Density functions borrow nested scratch buffers from a per-thread stack. Before,
  they rented from `ArrayPool`'s shared pool, whose single per-thread slot overflowed on nested rents.
- **#673: unloading unchanged chunks.** Pregeneration no longer rewrites chunks that are unchanged when it unloads
  them. Scheduled tick lists count as changed only after game time has moved. Each region's chunks unload as one batch
  with one commit per file.
- **#674: allocations.** The overworld allocates 264 KB per chunk instead of 611 KB. The nether is down from 255 to
  124 KB and the end from 178 to 42 KB.
- **#675: staging and the thread cap.** Pregeneration staging follows dependencies instead of wave barriers. The
  generation pool is capped at the physical cores the process may use.

The same driver measured `85947875` earlier the same day, on this machine, with workstation GC:
- At 32 threads, the 26.3 overworld took 2.33 s, the world was ready in 6.45 s and the peak working set was 981 MB.
  With the four changes, the workstation-GC run above takes 1.63 s, is ready in 4.25 s and peaks at 679 MB.
- At `85947875`, the 26.3 server was still slower than the 1.21.11 one at 32 and 16 cores. It is now faster at every
  core count.

### Caveats

- **Spawn height.** 26.3 spawns at `96.5, 145.0, -31.5`, on a dark oak canopy. That is the same column as 1.21.11's
  `96.5, 136.0, -31.5`, but higher. Every run of each build spawned at the same position.
- **The thread cap.** Since #675, generation runs at most one thread per physical core, and leaves one logical
  processor free. That is 16 threads at 32 threads, 15 at 16 cores, 7 at 8 cores and 1 at 2 cores.
  - At 32 threads, the 16 generation threads may land on SMT siblings. They also share cores with the tick and I/O
    threads. So 16 cores generates faster than 32 threads (1.43 s against 1.59 s) and uses less CPU per chunk (17.6
    against 21.2 ms).
  - At 2 cores, one thread generates. The 2-core numbers are therefore close to single-threaded speed.
- **2-core spread.** The 26.3 overworld's two runs at 2 cores took 10.49 s and 11.82 s.
- **Region files.** The 26.3 overworld's region files are 19.7 MB, against 18.2 MB for 1.21.11.
- **Decoration order.** The [border-order caveat](#caveats-of-the-vanilla-comparison) still applies.

### Open items

- **The region flush snapshots under the tick lock.** Snapshotting a dimension's chunks in the pregeneration flushes
  takes about 155 ms. `FlushRegionsAsync` holds the level's simulation gate during that time, and each chunk's snapshot
  also takes the fluid tick lock. The level doesn't tick until the snapshots finish.
- **Block entities that vary between runs.** Block entities, for example sculk, varied with core count. This predates
  #672–#675, and these runs found it only in the 1.21.11 build:
  - 28 server runs of 26.3 at `85947875` and `25bedebc` were checked across all core counts. Each saved the same 1,875
    overworld block entities at the same positions.
  - The 1.21.11 build saved five different sets, from 1,407 to 2,026 block entities. Most of the difference is sculk
    sensors, catalysts and shriekers.
  - The 1.21.11 sets didn't follow core count: the 1,407 set appeared at 32, 16 and 8 cores.
  - That generator is gone, so this needs nothing on the current branch unless it shows up in 26.3.
- **Entities lost when an unload write fails.** See
  [entities lost when an unloading save fails](known-gaps/entities-lost-when-an-unloading-save-fails.md).

## History: the 26.3 pipeline against the 1.21.11 generator

These single-threaded numbers, at `a7230826`, were taken before the 1.21.11 generator was removed. The
[current section](#263-against-12111-after-the-pregeneration-work) measures both generators again.

Phase 5 of the [26.3 port](porting-26.3-plan.md) runs `BiomeTerrain` on the live path: biomes, terrain, structures
and decoration on 26.3's data and templates, followed by fluid/shape post-processing, final heightmaps, lighting and
initial creature population. `Obsidian.WorldgenBench serial` calls those same generation methods.

This comparison retired the 1.21.11 generator. With the 26.3 pipeline faster in every dimension, the 1.21.11
generator, its data and the bench's `--generator 1.21.11` mode (and the `pipeline` mode, which only ran it) were
removed; the bench now runs the 26.3 pipeline alone, with no `--generator` option. The figures below are kept as
history.

The live switch re-recorded only the 26.3 Overworld and Nether hashes in `BenchOptions.cs`: post-processing changes
final blocks, lighting and scheduled ticks, and shared vegetation helpers select current support tags. Both
generators matched their references in all three dimensions until the 1.21.11 one was removed. The timings below
predate these completion steps; they established that the 26.3 pipeline was faster before the switch.

Neither has a server around it here: both run `Obsidian.WorldgenBench serial` on the same 1,024 chunks per dimension,
on one thread.

| | |
|---|---|
| Machine | AMD Ryzen 9 9950X, 64 GB, Windows 11 Pro 23H2, .NET SDK 10.0.403, Release build |
| Build | origin/26.3.x `a7230826` |
| Command | `serial --dimension all --generator 26.3` or `--generator 1.21.11`, seed 12345, radius 16 (1,024 chunks), warm |
| Chunks | Overworld around (6, -2), Nether around (0, 0), End around (6, 0); 1,296 carved and 1,156 decorated |
| Runs | Three of each, the two generators alternating; the table shows medians |

| Dimension | 26.3: ms per chunk (chunks/s) | 1.21.11: ms per chunk (chunks/s) | 26.3: KB allocated per chunk | 1.21.11: KB allocated per chunk |
|---|---:|---:|---:|---:|
| Overworld | **9.14 (109)** | 10.14 (99) | 730 | 353 |
| Nether | **3.49 (286)** | 6.03 (166) | 382 | 234 |
| End | **0.63 (1,595)** | 0.73 (1,363) | 205 | 119 |

Both generators' hashes matched their references in every run, and the 26.3 pipeline's output matches vanilla 26.3 in
every worldgen replay.

### Where the overworld spends its time

One run near the median, in ms per chunk (each stage includes the neighbouring chunks it needs):

| Stage | 1.21.11 | 26.3 at `80555196` | 26.3 now |
|---|---:|---:|---:|
| Biomes | 0.58 | 6.08 | 0.48 |
| Noise (terrain density, aquifers, beardifier) | 2.86 | 37.13 | 2.51 |
| Surface (material rules, ore veins) | 3.11 | 24.41 | 2.29 |
| Carvers | 0.44 | 1.74 | 0.60 |
| Structure starts | 0.01 | 0.04 | 0.01 |
| Features | 2.68 | 3.34 | 2.70 |
| Post-processing | 0.05 | — | 0.01 |
| Light | 0.44 | 0.44 | 0.43 |
| **Total** | **10.16** | **73.18** | **9.01** |

### How it got there

Every change kept the output identical; the bench hashes and the worldgen replays check that.

- **Density functions** (#640, #641, #646): `cache` functions remember values, and those that don't vary with y fill
  one layer and copy it down. `range_choice` and `lerp` sample a branch only where points take it, and interpolation
  samples only the cell corners it needs. Arithmetic loops pick their operation once, and noise octaves sample eight
  points at a time with AVX2, in exactly the scalar path's operation order (`VectorizedNoise` checks the two agree bit
  for bit).
- **Noise fill** (#641, #646): blocks are decided into one byte each and written a layer at a time, seeding each
  section's palette in the order per-block writes would have met the blocks. The beardifier looks only at nearby
  pieces, and positions above the aquifers' range take the global fluid without asking them.
- **Biomes and surfaces** (#642, #645, #648): material rules read the chunk's stored biomes, as vanilla's do, from a
  table filled once per chunk; biome conditions naming nothing in reach are false without a lookup. Temperature and
  vegetation are sampled once per column when they don't vary with height.
- **Writes** (#644, #647): terrain writes to chunks no other thread can see yet are batched under one lock, and
  heightmap upkeep on every write no longer allocates. #647 also sped up the live generator, which #638's per-write
  heightmap upkeep had slowed from 8.85 to 11.56 ms per chunk.
- **Features and carvers** (#642, #646, #648): placements reuse per-thread position lists, ore blobs stamp visits
  instead of clearing a buffer per blob, tag tests use lookup tables, and carver settings are parsed once instead of
  per chunk.

### What's left

- The 26.3 pipeline still allocated more per chunk than the 1.21.11 generator did (527 KB vs 354 KB in the
  overworld), mostly in noise (122 KB vs 81 KB per call; much of it is the structure starts the beardifier looks up)
  and features (129 KB per call). The biome fill is down to 9 KB per call, from 170 KB.
- Carvers were still about 0.15 ms per chunk slower than the 1.21.11 generator's.

## History: Obsidian's 1.21.11 generator against vanilla 1.21.11

This comparison measures Obsidian's 1.21.11 generator against the vanilla 1.21.11 server. Obsidian now generates
with the 26.3 pipeline. The 1.21.11 generator was removed once the 26.3 pipeline outran it (see
[the 26.3 pipeline against the 1.21.11 generator](#history-the-263-pipeline-against-the-12111-generator)), so the
numbers here are history: the current branch can't reproduce them.

Measured on branch `worldgen-vanilla-parity` at commit `2b2345e3`. The single-threaded numbers were checked again at `9d922c39`: they were the same speed and gave the same hashes.

### Summary

Pregenerating the 1,024 chunks around spawn:

| Cores | Obsidian | Vanilla | Speedup | CPU ms per chunk (Obsidian / vanilla) | Peak memory (Obsidian / vanilla) |
|---:|---:|---:|---:|---:|---:|
| 32 threads | **1.7 s** | 14.6 s | 8.6× | 26.9 / 87.1 | 488 MB / 1,389 MB |
| 16 cores | **1.8 s** | 14.7 s | 8.0× | 17.1 / 67.6 | 468 MB / 1,410 MB |
| 8 cores | **2.9 s** | 16.0 s | 5.6× | 13.9 / 57.0 | 460 MB / 1,215 MB |
| 2 cores | **7.2 s** | 38.4 s | 5.4× | 12.1 / 56.5 | 448 MB / 1,259 MB |

- **Speed:** Obsidian generates the same chunks 5–9× faster than vanilla.
- **CPU:** it uses 3–5× less CPU per chunk.
- **Memory:** its peak memory is about a third of vanilla's.
- **Startup:** it starts in about 1.5 s; vanilla takes about 4 s.

### Setup

| | |
|---|---|
| CPU | AMD Ryzen 9 9950X (16 cores, 32 threads) |
| RAM | 64 GB |
| OS | Windows 11 Pro 23H2 (10.0.22631) |
| Obsidian | .NET SDK 10.0.401, Release build, workstation GC |
| Vanilla | `server-1.21.11.jar` on Temurin OpenJDK 25.0.3, `-Xmx6G`, default (G1) GC |

**World:**
- Seed `12345`, structures on. Spawn is in chunk (6, -2) on both servers.
- Each server generates the 32×32 square of chunks around spawn (radius 16, 1,024 chunks) to full status, starting from a fresh world.
- Obsidian uses its `pregenerateChunkRange: 16` setting. Vanilla uses force-load tickets that it activates at startup.
- Vanilla's world folder keeps only `level.dat` and `data/chunks.dat`, so every chunk is generated rather than loaded.

**Core counts:**
- Each run is pinned to a CPU affinity mask.
- "16 cores", "8 cores" and "2 cores" mean the first N physical cores, without their SMT siblings.
- "32 threads" means every logical processor.
- Each process starts suspended, so the mask applies before the runtime sizes its thread pools.

**Runs:**
- 3 runs per server at 32, 16 and 8 cores, and 2 runs per server at 2 cores.
- The two servers alternate run by run, so thermal or background drift affects both equally.
- The tables show medians.

#### What is measured

| Metric | How |
|---|---|
| Pregeneration time | Obsidian: from its "N chunks to generate" log line to the end of its progress loop. Vanilla: from "Loading N persistent chunks" to "Done". |
| World ready | From world creation until the server is joinable. For Obsidian this includes the spawn search. |
| Startup | From process start until world creation begins. |
| CPU | The process's CPU cycles over the pregeneration window (`QueryProcessCycleTime`), divided by the TSC frequency. `GetProcessTimes` is not used: it samples at clock ticks and under-counted Obsidian's short, bursty thread-pool work by about 3×. |
| Peak memory | The process's peak working set (`GetProcessMemoryInfo`), sampled every 50 ms. |
| Live heap | After a forced full GC: `dotnet-gcdump` for Obsidian, `jcmd GC.heap_info` for vanilla. |

### Full results

Medians, with the min–max range in brackets.

| Cores | Server | Pregen s | Chunks/s | CPU ms/chunk | World ready s | Startup s | Peak WS MB | Peak private MB | Live heap MB | Region files MB |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 32 threads | Obsidian | 1.70 (1.60–1.72) | 601 (596–641) | 26.9 (25.1–27.4) | 2.49 | 1.46 | 488 (487–489) | 411 | 299 | 14.0 |
| | Vanilla | 14.56 (14.35–14.57) | 70 (70–71) | 87.1 (82.5–89.7) | 14.77 | 4.05 | 1,389 (1,366–1,449) | 1,482 | 352 | 11.7 |
| 16 cores | Obsidian | 1.85 (1.81–1.99) | 554 (516–565) | 17.1 (16.9–19.4) | 2.67 | 1.45 | 468 (467–477) | 389 | 289 | 14.0 |
| | Vanilla | 14.72 (14.12–15.23) | 70 (67–73) | 67.6 (63.3–72.6) | 14.98 | 3.83 | 1,410 (1,339–1,436) | 1,479 | 345 | 11.7 |
| 8 cores | Obsidian | 2.87 (2.81–2.92) | 357 (351–364) | 13.9 (13.9–14.0) | 4.31 | 1.43 | 460 (458–471) | 381 | 284 | 13.9 |
| | Vanilla | 15.96 (15.76–16.31) | 64 (63–65) | 57.0 (54.9–57.7) | 16.15 | 3.68 | 1,215 (1,164–1,276) | 1,266 | 343 | 11.7 |
| 2 cores | Obsidian | 7.18 (6.94–7.41) | 143 (138–148) | 12.1 (11.7–12.6) | 8.98 | 1.68 | 448 (446–449) | 368 | 277 | 13.9 |
| | Vanilla | 38.43 (38.03–38.82) | 27 (26–27) | 56.5 (55.8–57.3) | 39.01 | 5.69 | 1,259 (1,218–1,299) | 1,307 | 351 | 11.6 |

The spawn search accounts for 0.8 s of Obsidian's world-ready time at 32 and 16 cores, 1.4 s at 8 cores and 1.8 s at 2 cores.

### Single-threaded stage breakdown

These numbers come from generating the same 1,024 chunks on one thread, with no server, and timing each generation stage. They show where the time goes, without scheduling or I/O. "Before" is the generator when it first reached parity, before the performance work.

| Stage | Before (ms/chunk) | Now (ms/chunk) | Vanilla, estimated (ms/chunk) |
|---|---:|---:|---:|
| Noise (terrain shape, aquifers, ores) | 14.3 | 3.7 | 9.8 |
| Features (decoration) | 18.7 | 2.7 | 4.5 |
| Surface | 3.3 | 1.6 | 2.8 |
| Biomes | 1.1 | 0.5 | 1.7 |
| Carvers | 1.5 | 0.4 | 0.4 |
| Light | 0.7 | 0.4 | 0.7 |
| Post-processing | 1.9 | 0.05 | — |
| **Total** | **41.7** | **9.6** | |
| Allocated per chunk | 5.9 MB | 0.2 MB | |

- Vanilla's figures are estimates taken from a Java Flight Recorder profile, because vanilla has no per-stage timer. Treat them as approximate.
- "Per chunk" means per fully generated chunk. The stages also run on the ring of neighbouring chunks that decoration needs: 1,296 chunks are noised and carved, and 1,156 are decorated.

### Parity

The speedups did not change the output:
- The single-threaded run hashes every chunk's blocks, light, biomes and heightmaps, plus the chunk as saved (block entities, scheduled ticks and so on). It checks them against the hashes recorded when the generator first matched vanilla.
- Radius 8 gives `hash 6c67f5c6ea91e8b1 nbt 33b6502794db74a4`.
- Radius 16 gives `hash 446d8fced8e36799 nbt a13afd0b8789418d`.

On the server, seed 12345 spawns at `96.5, 136.0, -31.5` in every run.

### Caveats of the vanilla comparison

- **Decoration order on the server.** Chunks decorate concurrently, as they do in vanilla. Where features from two neighbouring chunks meet (a tree on a chunk border, for example), the result can depend on which chunk decorated first. Vanilla behaves the same way. The single-threaded parity run uses a fixed order, and so does the spawn search, so the spawn point is deterministic.
- **Hyperthreads don't help much.** Going from 16 physical cores to all 32 threads barely changes the time (1.85 s → 1.70 s) but uses 57% more CPU cycles per chunk. Vanilla shows the same pattern (67.6 → 87.1 ms).
- **Region files are about 20% larger** than vanilla's (14.0 MB vs 11.7 MB for the same chunks). This hasn't been investigated, and it doesn't affect generation.
- **First start downloads the server jar.** Obsidian no longer ships vanilla's structure templates. On its first start it downloads the vanilla server jar from Mojang and extracts them into `cache/`, which took about 2 s here. The runs above had no such step, and later starts reuse the cache.
- **Runtime setting.** `Obsidian.ConsoleApp` sets `System.Runtime.TieredCompilation.CallCountingDelayMs=0`, so hot generator code is optimized sooner during the first seconds of pregeneration.
- **Not in the repo.** The benchmark driver (a Python script that launches both servers, pins affinity and samples the processes) is not part of this repository. The single-threaded stage harness is `Obsidian.WorldgenBench serial`.
