# Build Obsidian first
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-sdk
WORKDIR /work
COPY ../ ./
RUN dotnet publish Obsidian/Obsidian.csproj -c Debug -o build

# Build image
FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /obsidian
COPY --from=build-sdk /work/build .
EXPOSE 25565/TCP
VOLUME "/files"

# Define entry
ENTRYPOINT ["dotnet", "Obsidian.dll", "/files"]