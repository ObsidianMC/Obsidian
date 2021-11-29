FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-sdk
RUN dotnet publish -c Release -o build

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS runtime
COPY /build /server
WORKDIR /server
ENTRYPOINT ["dotnet", "Obsidian.dll"]