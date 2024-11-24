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
- [ ] Mobs AI & pathfinding
- [ ] Redstone circuits

## 💻 Contribute
Contributions are always welcome!
Read about how you can contribute [here](https://github.com/ObsidianMC/Documentation/blob/master/articles/contrib.md)

## 🔌 Develop plugins
Plugins are cool! Wanna make them yourself?
Find out about plugin development [here](https://github.com/ObsidianMC/Documentation/blob/master/articles/plugins.md)

## 🔥 Development builds
Very early development builds are available over at the [GitHub Actions](https://github.com/ObsidianMC/Obsidian/actions) page for this repository.
- Ensure you have the latest [.NET Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) installed
- Find the latest `.NET Build` [action](https://github.com/ObsidianMC/Obsidian/actions?query=branch%3Amaster) and scroll to the bottom of the page to find the artifacts.
- Unzip the artifact and run `dotnet ObsidianApp.dll` to start the server.
- On first run, a config file is generated. Fill this file with your preferenced values and run the previous command again.
Easy, isn't it?

## 🐟 Docker
You can now run Obsidian using Docker! As of right now, no image is available on DockerHub yet, but it will be sometime soon.

For now, to run Obsidian on Docker you will have to follow the following steps:
1. Clone Obsidian `git clone https://github.com/ObsidianMC/Obsidian.git`
2. Go to Obsidian's cloned directory `cd Obsidian`
3. Build the docker image `docker build . -t obsidian`
4. Run the container `docker run -d -p YOUR_HOST_PORT:25565 -v YOUR_SERVERFILES_PATH:/files --name YOUR_CONTAINER_NAME obsidian`
5. Obsidian will pregenerate a config file. Fill it out in `YOUR_SERVERFILES_PATH/config.json`
6. Start Obsidian's container again. `docker restart YOUR_CONTAINER_NAME`

### Docker Compose
There's also docker-compose support.
1. Clone Obsidian `git clone https://github.com/ObsidianMC/Obsidian.git`
2. Go to Obsidian's cloned directory `cd Obsidian`
3. Run `docker-compose up -V` to generate the `config.json`
4. Edit your `docker-compose.yml` file, along with `files/config.json`
5. `docker-compose up -Vd` to have the server run! The world, plugin and other server related files will be created in the `files` directory.

## 😎 The Obsidian Team
- [Naamloos](https://github.com/Naamloos) (creator)
- [Tides](https://github.com/Tides) (developer)
- [Craftplacer](https://github.com/Craftplacer/) (developer)
- [Seb-stian](https://github.com/Seb-stian) (developer)
- [Jonpro03](https://github.com/Jonpro03) (developer)

## 💕 Thank-you's
Thank you to [`#mcdevs`](https://wiki.vg/MCDevs) for additional support.

Thank you to [TkTech](https://tkte.ch/) for hosting [Wiki.vg](https://wiki.vg/) and for the [`#mcdevs`](https://wiki.vg/MCDevs community documenting Minecraft's protocol.

Thank you to Mojang for creating this wonderful game named [Minecraft](https://www.minecraft.net).

**...and of course the biggest thank you to everyone that contributed!**

<a href="https://github.com/obsidianserver/obsidian/graphs/contributors">
  <img src="https://contributors-img.web.app/image?repo=obsidianserver/obsidian" />
</a>

<sub><sup>Made with [contributors-img](https://contributors-img.web.app)</sup></sub>

![repobeats](https://repobeats.axiom.co/api/embed/18e251a59758b25b1ecebdfe0f4b6b4004b8d0f9.svg "Repobeats analytics image")
