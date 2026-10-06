FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential python3-dev \
        libkrb5-3 curl python3 python3-pip \
    && pip3 install --break-system-packages --no-cache-dir "graphifyy[openai,gemini,kimi,ollama]" anthropic \
    && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /app/logs && chown -R $APP_UID:$APP_UID /app/logs
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
# framework 项目在 ProjectReference 链上；缺失时 restore 只警告不生成其
# project.assets.json，后续 publish --no-restore 会报 NETSDK1004
COPY ["framework/OpenDeepWiki.Cache.Abstractions/OpenDeepWiki.Cache.Abstractions.csproj", "framework/OpenDeepWiki.Cache.Abstractions/"]
COPY ["framework/OpenDeepWiki.Cache.DependencyInjection/OpenDeepWiki.Cache.DependencyInjection.csproj", "framework/OpenDeepWiki.Cache.DependencyInjection/"]
COPY ["framework/OpenDeepWiki.Cache.Memory/OpenDeepWiki.Cache.Memory.csproj", "framework/OpenDeepWiki.Cache.Memory/"]
RUN dotnet restore "src/OpenDeepWiki/OpenDeepWiki.csproj"
COPY . .
WORKDIR "/src/src/OpenDeepWiki"
# publish 自带编译；--no-restore 复用上方 restore 层，整个流程只编译一次
RUN dotnet publish "./OpenDeepWiki.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenDeepWiki.dll"]
