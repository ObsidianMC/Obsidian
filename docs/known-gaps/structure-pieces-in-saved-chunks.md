# Structure pieces in saved chunks


**Status:** saved in vanilla 26.3's format; starts saved of other pieces are kept but unused, and inline processor
lists and plugin-defined pieces save incompletely.

Starts and their pieces are saved like vanilla 26.3's `StructureStart.createTag`, for every piece type Obsidian
generates, as generated and once placed (`VanillaStructureSaves` replays 28 starts vanilla saved, 12 of them placed,
`Assets/structure_pieces.json`), and vanilla's own loader reads them with every piece. Obsidian still recomputes
starts from the seed and reads back only what placing changes (boxes, heights, chests, spawners...), from its own
saves, older ones without piece ids included, and from vanilla's. What remains:

- A save whose pieces aren't those Obsidian generates (in number, type, or a field placing doesn't change: another
  structure version, a changed data pack, another generator) is rejected (`StructureStart.IsRejected`). The chunk
  keeps it as saved, and the start isn't placed, takes no references, doesn't adapt terrain and isn't a fortress
  for spawning; searches still find its start chunk. Obsidian doesn't build the saved pieces, so the structure
  isn't finished or used, and until the start is restored (as the first chunk it reaches is decorated) its
  generated pieces count.
- A placed feature a data pack defines inline in a feature pool element is saved as the JSON it was read from,
  which vanilla reads but saves normalised (defaults left out, numbers typed); a save's inline feature isn't
  compared with the generated one, only the rest of its element and piece.
- Template pools a data pack defines with a non-empty processor list inline (vanilla's own only inline empty lists)
  save that list empty; plugin pool elements save only their type and projection, and plugin structure pieces
  only the fields every piece has (`obsidian:plugin_piece`). Vanilla knows none of the plugins' types anyway.

**Where to start:** build pieces from the saved tag (vanilla's piece types' load constructors) for rejected saves, and
add a codec for processor lists to save inline ones.
