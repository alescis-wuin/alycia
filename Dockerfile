FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Alycia.Modules.Workspaces.Domain/Alycia.Modules.Workspaces.Domain.csproj src/Alycia.Modules.Workspaces.Domain/
COPY src/Alycia.Modules.Workspaces.Application/Alycia.Modules.Workspaces.Application.csproj src/Alycia.Modules.Workspaces.Application/
COPY src/Alycia.Modules.Workspaces.Infrastructure/Alycia.Modules.Workspaces.Infrastructure.csproj src/Alycia.Modules.Workspaces.Infrastructure/
COPY src/Alycia.Api/Alycia.Api.csproj src/Alycia.Api/
RUN dotnet restore src/Alycia.Api/Alycia.Api.csproj

COPY src/Alycia.Modules.Workspaces.Domain/ src/Alycia.Modules.Workspaces.Domain/
COPY src/Alycia.Modules.Workspaces.Application/ src/Alycia.Modules.Workspaces.Application/
COPY src/Alycia.Modules.Workspaces.Infrastructure/ src/Alycia.Modules.Workspaces.Infrastructure/
COPY src/Alycia.Api/ src/Alycia.Api/
RUN dotnet publish src/Alycia.Api/Alycia.Api.csproj \
    -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /out ./

RUN mkdir -p /app/logs && chown "$APP_UID" /app/logs

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Alycia.Api.dll"]
