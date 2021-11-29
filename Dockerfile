# Build Obsidian first
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-sdk
WORKDIR /work
COPY ../ ./
RUN dotnet publish Obsidian/Obsidian.csproj -c Release -o build

# Build image
FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /obsidian
COPY --from=build-sdk /work/build .
# Exposing port 25565 for TCP. UDP broadcast unsupported.
EXPOSE 25565/TCP
ENTRYPOINT ["dotnet", "Obsidian.dll"]