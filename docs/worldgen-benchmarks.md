# Worldgen benchmarks: Obsidian vs vanilla 1.21.11

This compares Obsidian's Mojang generator (`minecraft:mojang_generator`) with the vanilla 1.21.11 server. Both generate the same world, and Obsidian's output matches vanilla's (see [Parity](#parity)), so the comparison is like for like.

Measured on branch `worldgen-vanilla-parity` at commit `2b2345e3`. The single-threaded numbers were checked again at `9d922c39`: they were the same speed and gave the same hashes.

## Summary

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

## Setup

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

### What is measured

| Metric | How |
|---|---|
| Pregeneration time | Obsidian: from its "N chunks to generate" log line to the end of its progress loop. Vanilla: from "Loading N persistent chunks" to "Done". |
| World ready | From world creation until the server is joinable. For Obsidian this includes the spawn search. |
| Startup | From process start until world creation begins. |
| CPU | The process's CPU cycles over the pregeneration window (`QueryProcessCycleTime`), divided by the TSC frequency. `GetProcessTimes` is not used: it samples at clock ticks and under-counted Obsidian's short, bursty thread-pool work by about 3×. |
| Peak memory | The process's peak working set (`GetProcessMemoryInfo`), sampled every 50 ms. |
| Live heap | After a forced full GC: `dotnet-gcdump` for Obsidian, `jcmd GC.heap_info` for vanilla. |

## Full results

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

## Single-threaded stage breakdown

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

## Parity

The speedups did not change the output:
- The single-threaded run hashes every chunk's blocks, light, biomes and heightmaps, plus the chunk as saved (block entities, scheduled ticks and so on). It checks them against the hashes recorded when the generator first matched vanilla.
- Radius 8 gives `hash 6c67f5c6ea91e8b1 nbt 33b6502794db74a4`.
- Radius 16 gives `hash 446d8fced8e36799 nbt a13afd0b8789418d`.

On the server, seed 12345 spawns at `96.5, 136.0, -31.5` in every run.

## Caveats

- **Decoration order on the server.** Chunks decorate concurrently, as they do in vanilla. Where features from two neighbouring chunks meet (a tree on a chunk border, for example), the result can depend on which chunk decorated first. Vanilla behaves the same way. The single-threaded parity run uses a fixed order, and so does the spawn search, so the spawn point is deterministic.
- **Hyperthreads don't help much.** Going from 16 physical cores to all 32 threads barely changes the time (1.85 s → 1.70 s) but uses 57% more CPU cycles per chunk. Vanilla shows the same pattern (67.6 → 87.1 ms).
- **Region files are about 20% larger** than vanilla's (14.0 MB vs 11.7 MB for the same chunks). This hasn't been investigated, and it doesn't affect generation.
- **First start downloads the server jar.** Obsidian no longer ships vanilla's structure templates. On its first start it downloads the vanilla server jar from Mojang and extracts them into `cache/`, which took about 2 s here. The runs above had no such step, and later starts reuse the cache.
- **Runtime setting.** `Obsidian.ConsoleApp` sets `System.Runtime.TieredCompilation.CallCountingDelayMs=0`, so hot generator code is optimized sooner during the first seconds of pregeneration.
- **Not in the repo.** The benchmark driver (a Python script that launches both servers, pins affinity and samples the processes) and the single-threaded stage harness are not part of this repository.
