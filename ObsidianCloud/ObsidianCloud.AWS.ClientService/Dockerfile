#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["ObsidianCloud/ObsidianCloud.AWS.ClientService/ObsidianCloud.AWS.ClientService.csproj", "ObsidianCloud/ObsidianCloud.AWS.ClientService/"]
RUN dotnet restore "ObsidianCloud/ObsidianCloud.AWS.ClientService/ObsidianCloud.AWS.ClientService.csproj"
COPY . .
WORKDIR "/src/ObsidianCloud/ObsidianCloud.AWS.ClientService"
RUN dotnet build "ObsidianCloud.AWS.ClientService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ObsidianCloud.AWS.ClientService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN apt-get update && apt install -y wget
RUN wget https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && dpkg -i packages-microsoft-prod.deb
RUN apt-get update && apt-get install -y aspnetcore-runtime-7.0
COPY --from=publish /app/publish .
EXPOSE 25565
ENV ASPNETCORE_URLS=http://*:25565
ENTRYPOINT ["dotnet", "ObsidianCloud.AWS.ClientService.dll"]