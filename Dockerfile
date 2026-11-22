FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src
COPY global.json NuGet.Config Directory.Build.props Directory.Build.targets ./
COPY modules/ modules/
COPY src/ src/
COPY docs/architecture/preparation-multidepots/PACKAGES.tsv /tmp/alycia-packages.tsv
RUN set -eu; mkdir -p artifacts/local-feed; \
    for p in modules/contracts/src/Alycia.Contracts modules/platform/src/Alycia.Platform.Persistence; do \
        dotnet restore "$p" --configfile /src/NuGet.Config --locked-mode; \
        dotnet pack "$p" --no-restore -c Release -o /src/artifacts/local-feed; \
    done; \
    awk '$1 == "Alycia.Contracts" || $1 == "Alycia.Platform.Persistence" {print $3 "  artifacts/local-feed/" $1 "." $2 ".nupkg"}' /tmp/alycia-packages.tsv | sha256sum -c -; \
    dotnet restore src/Alycia.Api/Alycia.Api.csproj --configfile /src/NuGet.Config --locked-mode; \
    dotnet publish src/Alycia.Api/Alycia.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /out ./

RUN mkdir -p /app/logs /app/files && chown "$APP_UID" /app/logs /app/files

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Alycia.Api.dll"]
