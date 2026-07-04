FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential git python3-dev \
        libkrb5-3 curl python3 python3-pip \
    && pip3 install --break-system-packages --no-cache-dir "graphifyy[openai,gemini,kimi,ollama]" anthropic \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy Central Package Management files first
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/OpenDeepWiki/OpenDeepWiki.csproj", "src/OpenDeepWiki/"]
COPY ["src/OpenDeepWiki.EFCore/OpenDeepWiki.EFCore.csproj", "src/OpenDeepWiki.EFCore/"]
COPY ["src/OpenDeepWiki.Entities/OpenDeepWiki.Entities.csproj", "src/OpenDeepWiki.Entities/"]
COPY ["src/EFCore/OpenDeepWiki.Sqlite/OpenDeepWiki.Sqlite.csproj", "src/EFCore/OpenDeepWiki.Sqlite/"]
COPY ["src/EFCore/OpenDeepWiki.Postgresql/OpenDeepWiki.Postgresql.csproj", "src/EFCore/OpenDeepWiki.Postgresql/"]
RUN dotnet restore "src/OpenDeepWiki/OpenDeepWiki.csproj"
COPY . .
WORKDIR "/src/src/OpenDeepWiki"
RUN dotnet build "./OpenDeepWiki.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./OpenDeepWiki.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OpenDeepWiki.dll"]
