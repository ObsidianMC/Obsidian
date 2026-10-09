![logo](https://i.imgur.com/jU1lkP4.png)

---

[![.NET Build](https://github.com/ObsidianMC/Obsidian/actions/workflows/dotnet.yml/badge.svg)](https://github.com/ObsidianMC/Obsidian/actions/workflows/dotnet.yml)
[![Discord](https://img.shields.io/discord/772894170451804220.svg)](https://discord.gg/gQBtqyXChu)

Obsidian is a C# .NET implementation of the Minecraft server protocol. Obsidian is currently still in development, and a lot of love and care is being put into the project!

Feel free to join our [Discord](https://discord.gg/gQBtqyXChu) if you're curious about the current state of the project, questions are always welcome!

[![Obsidian Discord](https://discord.com/api/guilds/772894170451804220/embed.png?style=banner2)](https://discord.gg/gQBtqyXChu)

## ✅ Roadmap
- [x] A custom plugin framework
- [x] Player movement/Info and chat
- [x] Basic chunk loading
- [x] Block breaking/placing
- [x] Other gamemodes besides creative
- [x] Usable storage and crafting blocks
- [x] Low memory usage
- [x] Inventory management
- [x] Daylight and weather cycle
- [x] World generation
- [x] Liquid physics
- [x] Mobs AI & pathfinding (basic)
- [ ] Redstone circuits

## 💻 Contribute
Contributions are always welcome!
Read about how you can contribute [here](https://github.com/ObsidianMC/Documentation/blob/master/articles/contrib.md)

## 🔌 Develop plugins
Plugins are cool! Wanna make them yourself?
Find out about plugin development [here](https://docs.obsidianmc.net/articles/contrib.html)

## 🔥 Development builds
Very early development builds are available over at the [GitHub Actions](https://github.com/ObsidianMC/Obsidian/actions) page for this repository.
- Ensure you have the latest [.NET Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) installed
- Find the latest `.NET Build` [action](https://github.com/ObsidianMC/Obsidian/actions?query=branch%3Amaster) and scroll to the bottom of the page to find the artifacts.
- Unzip the artifact and run `dotnet ObsidianApp.dll` to start the server.
- On first run, a config file is generated. Fill this file with your preferenced values and run the previous command again.
- The first start needs internet access: Obsidian downloads the vanilla server jar from Mojang to extract the structure templates it can't ship, and caches them in `cache/`.
Easy, isn't it?

## 🛠️ Building from source
Building needs the .NET 10 SDK, Java 25 or newer (on the `PATH` or in `JAVA_HOME`) and internet access. The first build downloads the vanilla server jar of the Minecraft version set in `Obsidian/Obsidian.csproj` (`MinecraftVersion`), runs its data generators, and generates `Obsidian/Assets` from them (`Obsidian.AssetGenerator`). Later builds reuse the generated assets until the version changes.

## 🐟 Docker
You can now run Obsidian using Docker! As of right now, no image is available on DockerHub yet, but it will be sometime soon.

For now, to run Obsidian on Docker you will have to follow the following steps:
1. Clone Obsidian `git clone --recurse-submodules https://github.com/ObsidianMC/Obsidian.git`
2. Go to Obsidian's cloned directory `cd Obsidian`
3. Build the docker image `docker build . -t obsidian`
4. Run the container `docker run -d -p YOUR_HOST_PORT:25565 -v YOUR_SERVERFILES_PATH:/files --name YOUR_CONTAINER_NAME obsidian`
5. Obsidian creates configuration files in `YOUR_SERVERFILES_PATH/config/`. Edit them or supply environment overrides with `docker run -e MaxPlayers=50 -e Network__CompressionThreshold=256 ...`.
6. Start Obsidian's container again. `docker restart YOUR_CONTAINER_NAME`

### Docker Compose
The Compose sample lists every writable server, network, message, RCON, whitelist, and world setting as a commented environment option.

1. Clone Obsidian `git clone --recurse-submodules https://github.com/ObsidianMC/Obsidian.git`
2. Go to Obsidian's cloned directory `cd Obsidian`
3. Uncomment and edit the options you want in `docker-compose.yml`.
4. Run `docker compose up --build -d`. Server files persist in the `obsidian-files` named volume, mounted at `/files`.

Environment values override saved configuration files; omitted values fall back to those files, then built-in defaults. Nested properties use double underscores (for example, `Network__CompressionThreshold`); lists use zero-based indexes (for example, `Worlds__0__Seed` or `WhitelistedPlayers__0__Name`). List overrides merge by index and preserve omitted entries. Quote all Compose environment values.

`FEEDBACK_WEBHOOK_URL` remains an alias for `FeedbackWebhookUrl` and takes precedence if both are set. An empty value disables feedback. Changing environment values requires recreating the container with `docker compose up -d`; they are not written into the saved configuration. World seed overrides affect generation of new chunks, not existing chunks. RCON configuration fields are available, but the RCON server is currently unimplemented.

## 😎 The Obsidian Team
- [Naamloos](https://github.com/Naamloos) (creator)
- [Tides](https://github.com/Tides) (developer)
- [Craftplacer](https://github.com/Craftplacer/) (developer)
- [Seb-stian](https://github.com/Seb-stian) (developer)
- [Jonpro03](https://github.com/Jonpro03) (developer)

## 💕 Thank-you's
Thank you to [`#mcdevs`](https://minecraft.wiki/w/Minecraft_Wiki:Projects/wiki.vg_merge/MCDevs) for additional support.

Thank you to [TkTech](https://tkte.ch/) for hosting [Wiki.vg](https://tkte.ch/articles/2024/11/11/sunsetting.html) and for the [`#mcdevs`](https://minecraft.wiki/w/Minecraft_Wiki:Projects/wiki.vg_merge/MCDevs) community documenting Minecraft's protocol.

Thank you to the [Minecraft Wiki](https://minecraft.wiki) for continuing to host Wiki.vg's contents after the site shut down [[1](https://minecraft.wiki/w/Minecraft_Wiki:Projects/wiki.vg_merge)] [[2](https://tkte.ch/articles/2024/11/11/sunsetting.html)], as well as providing further resources on Minecraft's inner workings.

Thank you to Mojang for creating this wonderful game named [Minecraft](https://www.minecraft.net).

**...and of course the biggest thank you to everyone that contributed!**

<a href="https://github.com/obsidianserver/obsidian/graphs/contributors">
  <img src="https://contributors-img.web.app/image?repo=obsidianserver/obsidian" />
</a>

<sub><sup>Made with [contributors-img](https://contributors-img.web.app)</sup></sub>

![repobeats](https://repobeats.axiom.co/api/embed/18e251a59758b25b1ecebdfe0f4b6b4004b8d0f9.svg "Repobeats analytics image")
