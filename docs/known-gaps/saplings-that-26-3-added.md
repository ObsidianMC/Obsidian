# Saplings that 26.3 added


**Status:** open, left out of the 26.3 port; see [26.3 gameplay left out of the port](26-3-gameplay-left-out-of-the-port.md).

Since 26.1 a `TreeGrower` picks from weighted feature lists. `sapling_growers.json` keeps the old shape (a feature,
a secondary one and a secondary chance), which every 1.21.11 sapling fits; a grower that doesn't fit, like the
poplar's three trees, is left out, so that sapling doesn't grow. Saplings also grow 1.21.11's configured features,
since worldgen runs on 1.21.11's data for now.

**Where to start:** give `TreeGrower` weighted lists like vanilla's, dumped as they are by `VanillaDumper`.
