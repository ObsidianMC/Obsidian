FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG GIT_SHA=v0.1
WORKDIR /src
# Generating the vanilla assets runs the Minecraft server's data generators.
RUN apk add --no-cache openjdk21-jdk
COPY . .
RUN dotnet restore
RUN dotnet publish Obsidian.ConsoleApp/ -c Release -o out /p:SourceRevisionId=$GIT_SHA

# RUNNER
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /src/out .
# Terminal definitions let the interactive console read arrow, Home and End keys when attached.
RUN apk add --no-cache ncurses-terminfo-base

WORKDIR /files
# set env variable
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENTRYPOINT ["dotnet", "/app/Obsidian.ConsoleApp.dll"]
