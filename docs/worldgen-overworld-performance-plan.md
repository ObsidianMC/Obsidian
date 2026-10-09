# Overworld worldgen performance plan

Benchmarks of Obsidian's Mojang generator against vanilla 1.21.11 in all three dimensions, and a plan to bring the
overworld down to the nether's speed. Measured on 2026-10-09 at `origin/1.21.x` `e2c0e005`, on the setup described in
[worldgen-benchmarks.md](worldgen-benchmarks.md) (Ryzen 9 9950X, Windows 11, .NET 10 workstation GC, Temurin 25 `-Xmx6G`).

## Where things stand

### Server pregeneration, per dimension

Each server generates the same 1,024 chunks (a 32×32 square) to full, from a fresh world, in a fresh process, with
seed 12345. Vanilla generates from force-load tickets placed in the dimension being measured. Obsidian generates the
measured dimension first, so its code is as cold as vanilla's. The squares are centred on chunk (6, -2) for the
overworld (spawn), (0, 0) for the nether and (6, 0) for the end (the end spawn platform). Each cell is the median of 3
alternating runs; CPU is process cycles over the generation window.

| Dimension | Cores | Obsidian | Vanilla | Speedup | CPU ms/chunk (Obsidian / vanilla) | Peak WS MB (Obsidian / vanilla) |
|---|---:|---:|---:|---:|---:|---:|
| Overworld | 32 threads | **1.50 s** | 13.96 s | 9.3× | 15.0 / 89.1 | 485 / 1,318 |
| | 8 cores | **2.68 s** | 15.71 s | 5.9× | 11.6 / 54.2 | 462 / 1,242 |
| | 2 cores | **6.66 s** | 39.66 s | 6.0× | 11.1 / 51.0 | 451 / 1,309 |
| Nether | 32 threads | **1.35 s** | 9.12 s | 6.7× | 18.1 / 42.0 | 376 / 794 |
| | 8 cores | **1.64 s** | 9.52 s | 5.8× | 6.9 / 25.0 | 368 / 770 |
| | 2 cores | **3.74 s** | 15.92 s | 4.3× | 6.3 / 21.8 | 364 / 915 |
| End | 32 threads | **0.44 s** | 1.49 s | 3.4× | 5.5 / 27.8 | 296 / 741 |
| | 8 cores | **0.49 s** | 1.53 s | 3.1× | 2.1 / 9.9 | 299 / 641 |
| | 2 cores | **0.97 s** | 6.45 s | 6.6× | 1.9 / 8.9 | 313 / 720 |

- **Relative to vanilla, the overworld is already the strongest dimension.** The gap you see in absolute speed is
  mostly how much more work an overworld chunk is: vanilla itself spends 51 / 22 / 9 ms of CPU per chunk in the
  overworld, nether and end.
- **The end is not a realistic target.** End terrain is a single noise with no aquifers, caves, ores or surface rules.
  The nether is the right yardstick.
- **At 32 threads the overworld and nether are almost the same speed** (1.50 s and 1.35 s), although a nether chunk is
  less than half the work. Both hit the same ceilings: cold-start JIT and the server's scheduling (see below).

### Single-threaded stage costs (ms per full chunk)

The same 1,024 chunks on one thread with no server, after a JIT warmup.

| Stage | Overworld | Nether | End | Overworld − nether |
|---|---:|---:|---:|---:|
| Noise (terrain, aquifers, ore veins) | 3.89 | 0.61 | 0.34 | **+3.28** |
| Features | 2.63 | 0.50 | 0.04 | **+2.13** |
| Surface | 1.59 | 2.26 | 0.10 | −0.67 |
| Biomes | 0.47 | 0.09 | 0.03 | +0.38 |
| Light | 0.42 | 0.48 | 0.11 | −0.06 |
| Carvers | 0.40 | 0.21 | 0.01 | +0.19 |
| Post-processing and structure starts | 0.04 | 0.01 | 0.01 | — |
| **Total** | **9.55** | **4.24** | **0.71** | **+5.31** |
| Allocated | 191 KB | 227 KB | 28 KB | |

The overworld's extra cost is almost all noise and features.

Parity hashes from this run, to check that changes leave the output unchanged (`hash` covers blocks, light, biomes and
heightmaps; `nbt` covers the chunk as saved):

| Dimension | hash | nbt |
|---|---|---|
| Overworld | `446d8fced8e36799` (unchanged since the parity work) | `02a515930da0fd64` |
| Nether | `af7e0c06757df39c` | `0182f3c2505ffb8c` |
| End | `e2618139ae48a1ca` | `7c2bb04c8b8081bd` |

### Where the overworld's time goes

From a sampled CPU profile of the single-threaded run. Inclusive times only: the sampler's innermost frames (`PollGC`,
`Monitor.Enter_Slowpath`) proved to be artifacts. Removing the one real lock they pointed at changed nothing, and GC
pauses total under 100 ms.

- **Noise, 3.9 ms**
  - Final-density cell fill: 45%. Most of it is sampling noises at cell corners (`PerlinNoise` 16%, `BlendedNoise` 12%),
    going through the density-function tree as virtual calls.
  - Aquifer `ComputeSubstance`: 30%. This is the nearest-four-centres search for every non-solid block.
  - The rest: block writes (`SetLayer`) 5%, the beardifier 5%, and the generator's own loop 9%.
- **Features, 2.6 ms**
  - `OreFeature` 42%, sculk patches 23%. The rest are vegetation patches, geodes, random selectors and structure pieces,
    each under 7%.
  - Ores already skip work with bitsets and state ids. What's left is a call through `IWorldGenLevel`, an area lookup
    and a section lookup for every block they test.
- **Surface, 1.6 ms** (2.3 ms in the nether, where it's the biggest stage)
  - About a third of it is biome lookups: `BiomeManager.GetBiome` runs vanilla's 8-corner jittered search for every block
    a rule asks about.

### Scaling and cold start

The local stage bench has a parallel mode: carve every chunk in parallel, then decorate in 9 passes, then post-process
and light in 9 passes. Each pass takes one class of a 3×3 colouring of chunk positions, so no two chunks in a pass share
an area and nothing needs a lock. That gives an upper bound on what the server's scheduler could reach:

| Overworld, 1,024 chunks | 8 cores | 16 cores | 32 threads |
|---|---:|---:|---:|
| Pipeline, cold process | 1.89 s | 1.53 s | 1.43 s |
| Pipeline, warm (JIT settled) | 1.54 s | 1.02 s | 0.92 s |
| Server (measured above) | 2.68 s | — | 1.50 s |

| Nether, 1,024 chunks | 8 cores | 16 cores | 32 threads |
|---|---:|---:|---:|
| Pipeline, cold process | 0.93 s | 1.11 s | 0.92 s |
| Pipeline, warm | 0.69 s | 0.47 s | 0.42 s |
| Server | 1.64 s | — | 1.35 s |

- **Cold start costs about a third of the overworld's time.** A fresh process runs tier-0 and instrumented code for most
  of the 1.5 s.
- **Turning off dynamic PGO or tiering is not an option.** `TieredPGO=0` makes the cold run faster (2.2 s → 1.5 s with no
  warmup), but serial steady state gets 40% slower (9.6 → 13.3 ms per chunk). PGO's guarded devirtualization is carrying
  the density-function interpreter.
- **The server's scheduler costs the nether far more than the overworld.** Server 1.35 s against a warm bound of 0.42 s.
  A trace of the nether server shows two phases:
  - First second: all 32 threads busy, mostly in surface rules.
  - Second second: only 10–14 threads busy. A third of the CPU goes to `LockAsync` (the 3×3 chunk-lock semaphores
    contend and spin), plus decoration and light.
  - The overworld will hit the same wall as soon as its per-chunk cost drops.
- **Per-call stage cost rises with core count**, even warm: noise is 3.1 ms single-threaded, 4.7 ms at 8 cores and 4.8 ms
  at 16. SMT and lower all-core clocks explain part of this; memory traffic from the per-chunk caches and allocations
  explains the rest.
- **Server GC helps the overworld only:** 1.42 s → 1.22 s on the server, with no gain for the nether. Disabling the
  thread pool's semaphore spinning changed nothing.

## Goal

Make overworld pregeneration as fast as the nether's is today, and then keep it within about 1.5× of the nether as both
improve:
- **At least 2× faster at 32 threads:** 1.50 s → 0.75 s or less, about 18× vanilla.
- **Serial cost down by a third:** 9.6 → about 6 ms per chunk.

Every step must keep the parity hashes above unchanged.

## Plan

The steps are ordered by expected overworld gain for the effort. Each is independent, and each lands as its own PR with
before and after numbers from the harness.

### 0. Put the harness in the repository (done)

`Obsidian.WorldgenBench` is a console tool in the solution:
- `serial` gives per-dimension stage timings and checks the output against the recorded parity hashes. It exits with 1
  if a hash differs.
- `pipeline` gives the parallel pipeline bound, timed on a warm pass unless `--cold` is passed.

The server driver stays outside the repo for now. It's a Windows-only Python script that pins and samples both servers.

### 1. Biome lookups in surface rules (small; helps the nether most)

Surface rules call `BiomeManager.GetBiome` for each block they test. That's 8 jittered corner distances per call, and
it's a third of the surface stage.

**Change:** compute each block column's biome once per run of Y whose quart cell and fraction give the same answer, and
reuse it down the column. Store it in a per-thread buffer of `height` entries per column, filled lazily as Y decreases.

**Parity:** the lookup function is unchanged; only its results are reused.

**Expected:** about 0.5 ms per overworld chunk and 0.8 ms per nether chunk, serially.

### 2. Compile the noise router (large; the main overworld lever)

Noise is 3.9 ms of the overworld's 9.6. It evaluates the router as a tree of `IDensityFunction` objects with virtual
`GetValue` calls, and the 40% PGO dependency shows how much those calls cost. Turn each vanilla router function into
straight-line code with no dispatch:
- **Approach:** source-generate C# from the worldgen JSON at build time, next to the existing
  `WorldgenNoiseRegistryGenerator`, for the overworld, nether and end routers.
  - Keep the interpreter as the fallback for datapack routers.
  - Generated code is monomorphic, so it's fast from its first tier-1 compile, needs no PGO, and can be ReadyToRun
    compiled. That shrinks the cold start too (step 4).
- **Parity:** keep vanilla's order of operations and its caches (flat cache, cache-2D, cache-once, interpolation order
  per fill mode) exactly. RyuJIT doesn't contract into FMA, so the same order gives the same bits. Note in a comment
  that this order is what parity requires.
- **Fill whole runs, not points:** generate `Fill(cell, Span<double>)` bodies directly, so the `BinaryFiller` and
  `UnaryFiller` object trees also go away.
- **Expected:** noise 3.9 → about 2.2 ms. Confirm with a prototype that compiles only `final_density` before committing
  to the full generator.

### 3. Vectorised noise sampling (medium)

Corner sampling calls `ImprovedNoise` one point at a time. A column of cell corners shares X and Z, so:
- The X/Z permutation lookups and gradient terms can be hoisted out of the loop.
- The loop can run over Y in `Vector256<double>` lanes, with the same arithmetic per lane, so results stay bit-exact.
- The same applies to `BlendedNoise`, which samples 16 + 8 + 8 octaves per corner.

**Expected:** Perlin plus blended noise go from about 0.8 to about 0.35 ms per chunk. This builds naturally on step 2,
since generated code can call column samplers.

### 4. Cold start (medium)

About 0.5 s of the overworld's 1.5 s is JIT warmup, and the nether pays it in full because it has no spawn search to
warm it up. Measure these options together, after step 2, because compiled routers change the picture:
- **ReadyToRun:** compile `Obsidian.dll`, or only the worldgen namespaces as partial ReadyToRun, and check how much
  steady state it loses. Recent runtimes instrument hot ReadyToRun methods before their tier-1 compile, so PGO should
  survive, but this is unverified here.
- **Warmup during startup:** generate a few chunks far from spawn on every core while registries load, and throw them
  away. This is the cheapest option, but it spends CPU at every first start.
- **Server GC with DATAS** for the console host: −0.2 s on the overworld. Measure peak memory before adopting it,
  because the current 2–3× memory advantage over vanilla is worth keeping.

### 5. Pregeneration scheduler (medium; the main lever at high core counts)

Pregeneration enqueues chunks row by row, and each job takes 3×3 chunk locks for decoration, post-processing and
light. So the jobs in flight are mostly neighbours waiting on each other's locks. The nether loses about 0.9 s to this
at 32 threads, and the overworld will once steps 1–3 land.

**Change:** run pregeneration (a known square) as the staged pipeline that the bound above measures:
- Carve every chunk in parallel.
- Decorate in 3×3-colour waves.
- Post-process and light in waves.

No chunk locks are needed inside a wave. For on-demand generation (players exploring), keep the lock-based path but
take jobs in an order that spaces concurrent jobs at least 3 chunks apart.

**Watch for:**
- Decoration order at chunk borders. Waves are deterministic, which is an improvement on today's races (vanilla races
  the same way).
- Fluid tracking and light updates sent to players.
- Region flushing every 1,024 chunks.

**Expected:** overworld 1.5 → about 1.0 s at 32 threads with today's per-chunk cost, and further as steps 1–3 cut it.

### 6. Feature writes (medium)

Ores (1.1 ms per chunk) and other block-heavy features go through `IWorldGenLevel` for every block read and write, with
an area lookup and a section lookup each time.

**Change:** give `WorldGenRegion` a section cursor, like vanilla's `BulkSectionAccess`: cache the last section and its
palette, and read and write state ids directly. Have `OreFeature`, `IsAdjacentToAir` and the patch features use it.

**Also:** profile sculk patches (0.6 ms per chunk in this square, which has deep dark) against vanilla's Java Flight
Recorder profile, and tune them only if they're slower than vanilla's relative cost.

**Expected:** features 2.6 → about 1.8 ms.

### 7. Aquifer substance (medium)

`ComputeSubstance` (1.2 ms per chunk) searches the 12 candidate aquifer centres for every non-solid block below the
skip height. Within one grid cell, the candidates are the same for every block in the column.

**Change:**
- Resolve the candidates and their statuses once per (column, grid Y cell).
- Update the nearest four incrementally as Y steps down.
- Evaluate the barrier noise only when vanilla would, which keeps parity.

**Expected:** about 0.5 ms per chunk.

### Expected outcome

| | Today | After 1–3, 6–7 | After 4–5 as well |
|---|---:|---:|---:|
| Overworld serial ms/chunk | 9.6 | about 6 | about 6 |
| Overworld pregeneration, 32 threads | 1.50 s | about 1.1 s | **about 0.6–0.75 s** |
| Nether pregeneration, 32 threads | 1.35 s | about 1.2 s | about 0.45 s |

These are estimates from the bounds and profiles above, not measurements. Each step's PR replaces its line with a
measured one.

## Reproducing

Stage timings, parity hashes and the pipeline bound come from `Obsidian.WorldgenBench` (Release build):

```
dotnet run -c Release --project Obsidian.WorldgenBench -- serial [--dimension overworld|nether|end|all]
dotnet run -c Release --project Obsidian.WorldgenBench -- pipeline [--dimension ...] [--cold]
```

To pin the pipeline to the first N physical cores on Windows, run it with `start /affinity <mask> /wait /b`. The mask
sets every other bit, so `55555555` is 16 cores.

The server comparison uses the local harness in `C:\Users\LostT\mcdecomp\bench`:
- `bench_dims.py <obsidian|vanilla> <overworld|nether|end> <tag> --cpus N` runs one server benchmark. `run_dims.sh` runs
  the whole matrix, and `summarize_dims.py` prints the table above.
- `ObsidianHostWg` is the headless Obsidian host it starts. It builds against the checkout named in its project
  reference.
