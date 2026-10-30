# Vanilla 26.3 world fixture

Written by a vanilla 26.3 server (DataVersion 5023), for the Phase 3 round-trip tests. It's a superflat world
(seed 777, no structures) where these commands ran before `save-all flush`:

```
forceload add 0 0 15 15
setblock 0 -60 0 minecraft:furnace[facing=north]{Items:[{Slot:0b,id:"minecraft:iron_ore",count:2}],cooking_time_spent:5,cooking_total_time:200,lit_time_remaining:100,lit_total_time:300}
setblock 2 -60 0 minecraft:brewing_stand{Items:[{Slot:4b,id:"minecraft:blaze_powder",count:1}]}
setblock 4 -60 0 minecraft:decorated_pot{sherds:{back:"minecraft:angler_pottery_sherd",left:"minecraft:brick",right:"minecraft:brick",front:"minecraft:arms_up_pottery_sherd"},item:{id:"minecraft:emerald",count:5}}
setblock 6 -60 0 minecraft:oak_sign{front_text:{messages:["Hello","","",""]},is_waxed:1b}
setblock 8 -60 0 minecraft:oak_stairs[facing=east,half=top]
setblock 10 -60 0 minecraft:stone
setblock 12 -60 0 minecraft:red_bed[part=foot,facing=south]
setblock 12 -60 1 minecraft:red_bed[part=head,facing=south]
setblock 14 -60 0 minecraft:chest{Items:[{Slot:0b,id:"minecraft:diamond",count:3}]}
summon minecraft:armor_stand 3 -60 4 {NoGravity:1b}
time set 1000
weather rain 6000
difficulty hard
gamerule keep_inventory true
```

A second run then replaced the pot (as above) and saved again; starting the server applied `server.properties`'
default difficulty, so `level.dat` ends up `easy` despite the `difficulty hard` above.

The overworld's `region/r.0.0.mca` was trimmed to chunk (0, 0); `poi/`, `session.lock` and `level.dat_old` were left
out. Nobody joined, so there's no `players/` folder.
