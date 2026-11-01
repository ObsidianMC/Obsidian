# Recursive placement callbacks


**Status:** synchronous placement retained for vanilla parity; the deferred-hook contract is narrower.

Vanilla 1.21.11 calls `BlockState.onPlace` directly inside `LevelChunk.setBlockState`. Its collecting neighbour
updater defers nested neighbour and shape updates, not placement. Obsidian follows that order. Plugin placement
or removal callbacks that repeatedly place other blocks can recurse deeply, and can start neighbour updates before
the outer placement callback returns. Plugins must bound those chains or schedule follow-up work as block ticks.

**Where to start:** an explicit plugin-only deferred-placement API could make long chains safe while retaining
vanilla's synchronous placement ordering. Capture ordering before changing the existing hooks.
