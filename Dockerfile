FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Alycia.Modules.Workspaces.Domain/Alycia.Modules.Workspaces.Domain.csproj src/Alycia.Modules.Workspaces.Domain/
COPY src/Alycia.Modules.Workspaces.Application/Alycia.Modules.Workspaces.Application.csproj src/Alycia.Modules.Workspaces.Application/
COPY src/Alycia.Modules.Workspaces.Infrastructure/Alycia.Modules.Workspaces.Infrastructure.csproj src/Alycia.Modules.Workspaces.Infrastructure/
COPY src/Alycia.Modules.Chat.Domain/Alycia.Modules.Chat.Domain.csproj src/Alycia.Modules.Chat.Domain/
COPY src/Alycia.Modules.Chat.Application/Alycia.Modules.Chat.Application.csproj src/Alycia.Modules.Chat.Application/
COPY src/Alycia.Modules.Chat.Infrastructure/Alycia.Modules.Chat.Infrastructure.csproj src/Alycia.Modules.Chat.Infrastructure/
COPY src/Alycia.Api/Alycia.Api.csproj src/Alycia.Api/
RUN dotnet restore src/Alycia.Api/Alycia.Api.csproj

COPY src/Alycia.Modules.Workspaces.Domain/ src/Alycia.Modules.Workspaces.Domain/
COPY src/Alycia.Modules.Workspaces.Application/ src/Alycia.Modules.Workspaces.Application/
COPY src/Alycia.Modules.Workspaces.Infrastructure/ src/Alycia.Modules.Workspaces.Infrastructure/
COPY src/Alycia.Modules.Chat.Domain/ src/Alycia.Modules.Chat.Domain/
COPY src/Alycia.Modules.Chat.Application/ src/Alycia.Modules.Chat.Application/
COPY src/Alycia.Modules.Chat.Infrastructure/ src/Alycia.Modules.Chat.Infrastructure/
COPY src/Alycia.Api/ src/Alycia.Api/
RUN dotnet publish src/Alycia.Api/Alycia.Api.csproj \
    -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /out ./

RUN mkdir -p /app/logs /app/files && chown "$APP_UID" /app/logs /app/files

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Alycia.Api.dll"]
