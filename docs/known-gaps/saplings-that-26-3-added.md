# Saplings that 26.3 added


**Status:** open, left out of the 26.3 port; see [26.3 gameplay left out of the port](26-3-gameplay-left-out-of-the-port.md).

Since 26.1 a `TreeGrower` picks from weighted feature lists. `sapling_growers.json` keeps the old shape (a feature,
a secondary one and a secondary chance), which every 1.21.11 sapling fits; a grower that doesn't fit, like the
poplar's three trees, is left out, so that sapling doesn't grow. Supported saplings now grow 26.3's configured features through the native feature adapter.

**Where to start:** give `TreeGrower` weighted lists like vanilla's, dumped as they are by `VanillaDumper`.
