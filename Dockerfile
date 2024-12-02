ARG GIT_SHA=v0.1

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish Obsidian.ConsoleApp/ -c Release -r linux-musl-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded /p:SourceRevisionId=$GIT_SHA

FROM alpine:latest
WORKDIR /app
COPY --from=build /src/Obsidian.ConsoleApp/bin/Release/net9.0/linux-musl-x64/publish/ .
RUN apk upgrade --update-cache --available && apk add openssl libstdc++ && rm -rf /var/cache/apk/*

WORKDIR /files
ENTRYPOINT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 /app/Obsidian.ConsoleApp
