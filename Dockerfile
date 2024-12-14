ARG GIT_SHA=v0.1

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish Obsidian.ConsoleApp/ -c Release -o out /p:SourceRevisionId=$GIT_SHA

# RUNNER
FROM alpine:latest
WORKDIR /app
COPY --from=build /src/out .
RUN apk upgrade --update-cache --available && apk add openssl libstdc++ && rm -rf /var/cache/apk/*

WORKDIR /files
# set env variable
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENTRYPOINT ["dotnet", "/app/Obsidian.ConsoleApp"]
