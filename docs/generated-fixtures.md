# Generated parity fixtures

`dotnet build Obsidian.slnx` runs `Obsidian.AssetGenerator` before the shared projects compile. The generator writes its results atomically into ignored `Obsidian/Assets`; the Obsidian assembly embeds them as `Obsidian.Assets.<filename>`. Do not commit this folder, fixture captures, jars, mappings, or decompiled game source.

The existing server jar and mappings supply `item-components-<version>.json`, `client-entity-metadata-<version>.json`, and `client-screens-<version>.json`. Component captures use the committed synthetic inputs `component-fixtures-input.json`, `component-samples.json`, and `merchant-fixtures-input.json`.

With Minecraft installed through the official launcher, the existing installed-client dumper also generates `client_models.json` and `client-{hud,particles,player-physics,protocol}-<version>.json`. Set `MINECRAFT_DIR` to a non-default installation before building. This path uses the installed client jar and installed libraries, verifies the jar against launcher metadata, and reuses the existing client-mappings download. It adds no download paths or remapping dependency. Without the installation these client-only outputs are skipped; the client parity tests require them.

The HUD, particle, and physics probes use `.java.template` files. Explicit `// @map TOKEN fully.qualified.Type#member(parameters)` directives resolve only names in our own probe source via the existing Mojang mapping reader. `FixtureSource.java` writes the resolved probes into the ignored generator work directory; Java's existing source launcher runs them against the unchanged installed jar. Physics probes substitute world services while executing the jar's actual collision/travel methods.

The generated-assets marker is touched only after the complete run succeeds. Java sources, templates, and JSON inputs are copied to the generator output and participate in its incremental inputs. Missing server fixture outputs also invalidate the target. If Minecraft is installed after a server-only build, remove `Obsidian/Assets/.generated-<version>` once and rebuild to generate the optional client outputs.

`ItemComponentWire` reads the component capture from the Obsidian assembly. Client tests in the separate client repository read the same embedded assets, independent of their working directory.
