# Saved passenger trees and leads


**Status:** riding is a live relationship between any two entities and travel carries whole passenger trees; leads
held by entities are read, saved, shown and dropped as vanilla does around travel (see
[plugin-content.md](../plugin-content.md#travel-and-portals)). Chunk saves still store each entity on its own, so a
mob-on-mob ride (other than the skeleton trap horse's rider, which finds its skeleton again by UUID) is lost on
reload; a vanilla save's nested `Passengers` list is kept as saved but its passengers aren't spawned. Using a lead on
a mob, fence knots (a knot-tied `leash` field is kept untouched) and the lead's elastic pull and snapping distance are
not modelled.

**Where to start:** save passengers nested in their root vehicle's `Passengers` list like vanilla's
`saveAsPassenger`, skipping them at the top level, and spawn and seat them when the root loads. Leads next need the
lead item's use on mobs and fences, the knot entity and `Leashable`'s distance checks.
