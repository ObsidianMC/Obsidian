# Attachment interoperability and saved-data migrations


**Status:** deliberately scoped to Obsidian mods.

Saved data uses vanilla's gzip envelope, but the payload's schema belongs to its plugin codec. The engine does not
run Mojang data fixers or provide a separate plugin migration registry; a plugin can include a version in its own
payload and migrate in Decode. Attachments use `obsidian:attachments` in saves and an optional Obsidian channel;
they do not read NeoForge's attachment saves or speak NeoForge's attachment payloads. NeoForge and Fabric clients
need a matching bridge mod to receive this optional channel.

**Where to start:** add compatibility only for a concrete mod and captured save/wire fixtures. Keep schema upgrades
inside that mod's codecs unless several mods need an engine migration contract.
