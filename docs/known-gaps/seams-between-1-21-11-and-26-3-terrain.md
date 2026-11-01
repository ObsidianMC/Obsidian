# Seams between 1.21.11 and 26.3 terrain

**Status:** expected compatibility limit of the live 26.3 generator switch.

Existing chunks keep their saved terrain and biomes. Newly generated chunks use 26.3 noise, biomes, features and
structures, including in worlds created with 1.21.11. There is no blending or regeneration across that frontier,
so abrupt terrain and biome seams are expected. The saved-world upgrade converts data; it does not rebuild terrain.

Saved structure starts whose pieces are incompatible with the newly generated 26.3 start are retained in the save
but rejected for further placement. A structure crossing the old/new frontier can therefore stay incomplete.
Keep the upgrade backup if preserving the old generation boundary matters.
