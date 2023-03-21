#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["ObsidianCloud/ObsidianCloud.ClientService/ObsidianCloud.ClientService.csproj", "ObsidianCloud/ObsidianCloud.ClientService/"]
RUN dotnet restore "ObsidianCloud/ObsidianCloud.ClientService/ObsidianCloud.ClientService.csproj"
COPY . .
WORKDIR "/src/ObsidianCloud/ObsidianCloud.ClientService"
RUN dotnet build "ObsidianCloud.ClientService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ObsidianCloud.ClientService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ObsidianCloud.ClientService.dll"]