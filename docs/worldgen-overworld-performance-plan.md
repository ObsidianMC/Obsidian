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

### Progress

| Step | Status | Measured |
|---|---|---|
| 0. Harness | Done | — |
| 1. Surface biome lookups | Done, with allocation-free legacy randoms | Serial surface: overworld 1.59 → 1.45 ms, nether 2.42 → 1.86 ms per chunk. Nether total 4.11 → 3.6 ms per chunk, allocations halved |
| 2. Compile the noise router | Done for cell corners, as part of step 3 | Dispatch turned out to be a small share of noise (see step 2) |
| 3. Vectorised noise sampling | Done: corners sampled a column at a time, noises four corners at once | Overworld corner sampling 1.19 → 0.83 ms per chunk, noise stage 3.1 → 2.8 ms |
| 4. Cold start | Options measured, none adopted | ReadyToRun, `TC_QuickJitForLoops=0` and a startup warmup each cost more than they gain. Server GC is left to deployments, which will use it by default (see step 4) |
| 5. Pregeneration scheduler | Done | Overworld pregeneration: 4 cores 4.11 → 3.31 s, 8 cores 2.64 → 2.03 s, 16 cores 1.64 → 1.42 s, 32 threads unchanged |
| 6. Feature writes | Partly done: block-entity removal skipped where a chunk has none | Overworld features 2.52 → 2.37 ms per chunk. The rest is spread across many small operations (see step 6) |
| 7. Aquifer substance | Done: branch-free nearest-centre search | Aquifer calls 0.85 → 0.57 ms per chunk; overworld noise stage 3.6 → 3.2 ms |
| Noise floors | Done | Flooring noise coordinates without saturating conversions: blended noise samples 410 → 340 ns |

The output is unchanged: the bench's parity hashes match after every step, and the committed tests pass.

### 0. Put the harness in the repository (done)

`Obsidian.WorldgenBench` is a console tool in the solution:
- `serial` gives per-dimension stage timings and checks the output against the recorded parity hashes. It exits with 1
  if a hash differs.
- `pipeline` gives the parallel pipeline bound, timed on a warm pass unless `--cold` is passed.

The server driver stays outside the repo for now. It's a Windows-only Python script that pins and samples both servers.

### 1. Biome lookups in surface rules (done)

Surface rules look up the biome of every block they test. Each lookup measures 8 jittered quart corners and hashed a
key to find the chosen corner's biome. The biome manager now does two things while it builds a chunk's surface:
- It keeps the quart biomes a lookup can reach in an array by position.
- It returns at once when all 8 corners share a biome, since the jitter can't change the answer then.

The same step made `PositionalRandom` allocation-free for legacy worlds such as the nether. It used to create a
`LegacyRandomSource` per block for surface depths and bedrock gradients.

The upper bound measured by skipping lookups entirely was about 0.45 ms in the overworld and 1.15 ms in the nether. Most
of what's left is lookups on chunk borders, whose corners reach into neighbouring chunks.

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

**Finding (deprioritised):** with dynamic PGO off, noise gets only 22% slower, while surface gets 70%, biomes 40% and
features 35% slower. In the profile, the frames of the density tree and its fillers add up to about 0.5 s of the noise
stage's 3.8 s; noise sampling itself and the aquifer take most of it. Compiling the router would save perhaps 0.3–0.5 ms per
chunk, so steps 3 and 7 come first. Surface rules, compiled into delegate trees, depend most on PGO, so they're a better
candidate for straight-line code.

### 3. Vectorised noise sampling (medium)

Corner sampling calls `ImprovedNoise` one point at a time. A column of cell corners shares X and Z, so:
- The X/Z permutation lookups and gradient terms can be hoisted out of the loop.
- The loop can run over Y in `Vector256<double>` lanes, with the same arithmetic per lane, so results stay bit-exact.
- The same applies to `BlendedNoise`, which samples 16 + 8 + 8 octaves per corner.

**Expected:** Perlin plus blended noise go from about 0.8 to about 0.35 ms per chunk. This builds naturally on step 2,
since generated code can call column samplers.

**Measured:** corner sampling is 1.34 ms of the overworld's 3.86 ms noise stage, about 8,300 density-tree evaluations
per chunk at about 160 ns each. The aquifer is about 0.95 ms and the per-block fill about 1.5 ms.

Batching needs a column method on the density functions and the noise chunk's caches, because corners are sampled
through the whole tree, not just the noises. That's the largest remaining compute item, and also the largest piece of
work.

**Done:** the noise chunk compiles each interpolated function into column fillers (`NoiseChunk.ColumnFillers.cs`).
- **Columns:** each node is visited once per column of corners, not once per corner. Shared functions cache the corners
  sampled so far, and functions that don't depend on Y (flat caches, 2D caches) are sampled once.
- **Skips:** arguments a node skips at a corner (a product's second argument after a zero, a range choice's other
  branch, a minimum's second argument below its bound) aren't sampled there either.
- **Lanes:** noises, shifted noises, weird scaled samplers and the blended noise sample four corners at once in
  `Vector256` lanes (`ImprovedNoiseLanes`). Each lane does the scalar arithmetic in the same order with no fused
  multiply-adds, so the values are bit for bit the same; a test compares them. The lanes need AVX, and other CPUs keep
  the corner-by-corner path.
- **Result:** overworld corner sampling 1.19 → 0.83 ms per chunk, the noise stage 3.1 → 2.8 ms. What's left is the
  blended noise (0.29 ms) and the cave noises (0.39 ms).

**Findings:**
- **The interpolated final density dominates corner sampling:** 0.99 of 1.19 ms. Each evaluation cost about 1 µs,
  mostly 15–18 cave noises of one or two octaves each, plus the blended noise.
- **Batching octaves doesn't help.** Sampling four octaves of one noise in lanes made 8-octave noise 1.8× faster in
  isolation, but generation didn't change: most cave noises have one or two octaves, which can't fill the lanes.
  Batching corners can.
- **Hardware gathers** for the permutation tables were no faster than looking the four entries up one by one, and are
  slow on Intel CPUs with the gather data sampling mitigation, so the lanes don't use them.
- **The sampling profiler over-attributes methods with loops.** It suspends threads at safe points, and loop back
  edges are safe points. Timing each interpolator with a stopwatch gave the breakdown above.

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

**Findings:**
- **ReadyToRun is slower.** Cold, the overworld bound goes from 2.0 to 2.4 s; warm, from 1.14 to 1.27 s, and the nether
  from 0.53 to 1.2 s. The steady-state losses suggest precompiled code misses the dynamic PGO the JIT path gets.
- **`DOTNET_TC_QuickJitForLoops=0` trades steady state for startup.** It speeds the cold server up (nether 1.22 → about
  1.06 s, overworld 1.49 → 1.40 s), but serial steady state gets 16–21% slower, because loop methods never get PGO. A
  long-running server shouldn't take that trade.
- **Triggering OSR earlier, or instrumenting all code rather than only hot code, shows no clear gain.** Results were
  within run-to-run noise, slightly better for the overworld and slightly worse for the nether.
- **A startup warmup barely pays.** A background task generated a few throwaway chunks on idle cores while the worlds
  loaded.
  - Warming the overworld sped the overworld up but slowed the nether and end, which generate first: the warmup's
    overworld-only methods filled the JIT's single tier-1 queue.
  - Warming whichever dimension generates first was better, but the time to the end of pregeneration only improved by
    0–10% (overworld 2.88 → 2.78 s at 32 threads, 3.49 → 3.21 s at 8 cores). Startup is only about 1.4 s, and the
    warmup needs most of that to build its own generator. Not adopted.
- **There's no setting for the number of tier-1 JIT workers.** In the pipeline bench, a cold pass compiles about 10,000
  methods (2.6 s of JIT time) and even a warm pass about 3,000.
- **Compiling surface rules into one expression tree is slower.** It replaced the delegate tree with one compiled
  method.
  - Surface got slower: overworld 1.50 → 2.0 ms per chunk.
  - Splitting large branches into separate methods brought the overworld to 1.7 ms but slowed the nether to 2.3 ms.
  - PGO already devirtualises and inlines the delegate calls well, while compiled expressions get no PGO.
- **Server GC (with .NET 10's dynamic heap sizing)**, medians of 3 runs:

  | Case | Workstation GC | Server GC |
  |---|---:|---:|
  | Overworld, 32 threads | 1.53 s, 501 MB peak | 1.32 s, 599 MB |
  | Overworld, 8 cores | 2.17 s, 486 MB | 1.96 s, 510 MB |
  | Nether, 32 threads | 1.40 s, 393 MB | 1.27 s, 456 MB |
  | Nether, 8 cores | 1.41 s, 383 MB | 1.56 s, 431 MB |

  It gains 10–14% for the overworld and costs 5–20% more memory, still well under vanilla's 1.2–1.4 GB. Deployments
  will run with server GC by default, so the repository doesn't set it.

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

**Done:** `MojangGenerator.PrepareAreaAsync` does this for pregeneration, and the job loop then only marks chunks
complete. Results (median of interleaved runs, 1,024 chunks):

| Cores | Overworld | Nether |
|---:|---:|---:|
| 4 | 4.11 → 3.31 s | 2.02 → 2.00 s |
| 8 | 2.64 → 2.03 s | 1.50 → 1.36 s |
| 16 | 1.64 → 1.42 s | 1.20 → 1.27 s |
| 32 threads | 1.41 → 1.42 s | 1.25 → 1.28 s |

At 16 cores and above, cold-start JIT is the limit, not locks: the nether is generated first in a fresh process, so it
gains nothing there. Light was checked to be independent of the order chunks are lit in. On-demand generation (players
exploring) still uses the lock-based path.

**Later finding: the nether pays for the waves at 32 threads.** A longer interleaved run puts the original nether at
1.15 s and the waves at 1.29–1.33 s. Timing the phases on the server shows why: carving takes 870 ms, decorating 270 ms and
lighting 100 ms, where perfect scaling would give about 18 and 15 ms for the last two. Decoration and light then run
cold and can't overlap with carving, as they did in the job loop. Two variants didn't help:
- **Carving in parallel but decorating and lighting in the job loop** was slower everywhere (overworld 8 cores 1.81 →
  2.63 s, nether 32 threads 1.32 → 1.41 s).
- **Decorating 4 or 8 spaced-out chunks during carving**, to compile the decoration code early, changed nothing.

Even with the JIT warm, per-call costs in the pipeline bench grow far more than SMT explains (nether light 0.45 → 2.9 ms,
features 0.40 → 1.29 ms). The light engine shares nothing between threads, so GC pauses and memory pressure are the
likely causes. That's the next thing to investigate for high core counts.

### 6. Feature writes (medium)

Ores (1.1 ms per chunk) and other block-heavy features go through `IWorldGenLevel` for every block read and write, with
an area lookup and a section lookup each time.

**Change:** give `WorldGenRegion` a section cursor, like vanilla's `BulkSectionAccess`: cache the last section and its
palette, and read and write state ids directly. Have `OreFeature`, `IsAdjacentToAir` and the patch features use it.

**Also:** profile sculk patches (0.6 ms per chunk in this square, which has deep dark) against vanilla's Java Flight
Recorder profile, and tune them only if they're slower than vanilla's relative cost.

**Measured (timing each feature type's placement):**

| Feature | ms per chunk |
|---|---:|
| Ores | 1.00 |
| Sculk patches | 0.60 |
| Random selectors (includes trees they place) | 0.20 |
| Trees | 0.19 |
| Vegetation patches | 0.15 |
| Geodes | 0.15 |

- **Ores:** each chunk tests about 10,000 blocks and places 7,800 (dirt, gravel, granite and the other stone blobs count
  as ores), at about 100 ns per tested block.
- **Block-entity removal:** the profile blamed most of the ore time on the removal every write does. Skipping it saves
  only about 0.15 ms per chunk. The sampling profiler suspends threads at safe points, which biases its leaf frames
  toward lock and GC-poll paths.
- **The empty-check:** skipping the removal when a chunk has no block entities made features twice as slow, because
  `ConcurrentDictionary.IsEmpty` takes every lock when the dictionary is empty.
- **Deferred:** no single step dominates.

**Done:** the region asks each chunk once whether it has block entities and tracks the ones it adds, so writes to
chunks without any skip the removal. Overworld features 2.52 → 2.37 ms per chunk.

**Also measured (cutting parts of the ore placement):** the sphere geometry costs about 0.28 ms per chunk, the block
tests about 0.17 ms and the writes about 0.2 ms; the rest is placement and the spheres' setup. Sculk patches spend about
0.22 ms per chunk on charge use, 0.18 ms on vein spreading and 0.11 ms on cursor moves, spread across property lookups
and block reads.
None is large enough on its own to justify reworking vanilla's algorithms.

**Expected:** features 2.6 → about 1.8 ms.

### 7. Aquifer substance (medium)

`ComputeSubstance` (1.2 ms per chunk) searches the 12 candidate aquifer centres for every non-solid block below the
skip height. Within one grid cell, the candidates are the same for every block in the column.

**Change:**
- Resolve the candidates and their statuses once per (column, grid Y cell).
- Update the nearest four incrementally as Y steps down.
- Evaluate the barrier noise only when vanilla would, which keeps parity.

**Expected:** about 0.5 ms per chunk.

**Measured (counting each path):**
- **Calls:** about 17,700 per chunk, almost all reaching the 12-candidate search, which costs about 55 ns per call and
  about 0.95 ms per chunk in all.
- **Barrier noise:** only about 50 samples per chunk.
- **Status computations:** about 85 per chunk.
- **Exit points:** two thirds of the calls end at the first similarity check.
- **Deferred:** what's left is the search itself, which a branch-light or SIMD version might cut by a third (about
  0.3 ms per chunk).

**Done:** each candidate gets a key packing its squared distance and search order, and the four smallest keys are kept
sorted in a `Vector128<int>` with min and max, so nothing branches. That gives vanilla's order: by distance, then the
latest candidate first. In a replay of one chunk's real calls, aquifer calls go from 0.85 to 0.57 ms per chunk; status
computations are only about 0.05 ms of that.

### Outcome

Serial, single thread, after JIT warmup (ms per chunk; parity hashes unchanged in every dimension):

| Stage | Overworld before | Overworld after | Nether before | Nether after |
|---|---:|---:|---:|---:|
| Noise | 3.89 | 2.77 | 0.61 | 0.38 |
| Features | 2.63 | 2.42 | 0.50 | 0.40 |
| Surface | 1.59 | 1.40 | 2.26 | 1.92 |
| Biomes | 0.47 | 0.47 | 0.09 | 0.08 |
| Light | 0.42 | 0.42 | 0.48 | 0.46 |
| Carvers | 0.40 | 0.35 | 0.21 | 0.20 |
| **Total** | **9.55** | **7.96** | **4.24** | **3.49** |

The end went from 0.71 to 0.60 ms per chunk.

Server pregeneration of 1,024 chunks, medians of 3 interleaved runs, original `1.21.x` against this branch:

| | 8 cores | 32 threads |
|---|---:|---:|
| Overworld | 2.60 → **1.84 s** | 1.39 → 1.39 s |
| Nether | 1.46 → **1.33 s** | 1.15 → 1.31 s (see step 5) |

- **At 8 cores the overworld is 29% faster**, and within 1.4× of the nether, against 1.8× before.
- **At 32 threads both dimensions sit at about 1.3–1.4 s.** Cold-start JIT and parallel per-call costs set that
  floor, not the serial cost: a third less serial work didn't move it.
- **The serial goal of about 6 ms per chunk wasn't reached.** What's left is spread across surface rules (1.4 ms),
  vanilla's feature algorithms (2.4 ms) and noise that's now mostly the blended and cave noises' arithmetic.

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
