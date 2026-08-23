# ===== Backend — Walletter API (ASP.NET Core + EF Core) =====
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia los proyectos y restaura (aprovecha caché de capas).
COPY backend/Walletter.slnx ./
COPY backend/src/Walletter.Domain/Walletter.Domain.csproj src/Walletter.Domain/
COPY backend/src/Walletter.Application/Walletter.Application.csproj src/Walletter.Application/
COPY backend/src/Walletter.Infrastructure/Walletter.Infrastructure.csproj src/Walletter.Infrastructure/
COPY backend/src/Walletter.Api/Walletter.Api.csproj src/Walletter.Api/
RUN dotnet restore src/Walletter.Api/Walletter.Api.csproj

# Copia el código y compila en modo Release.
COPY backend/src/ src/
RUN dotnet publish src/Walletter.Api/Walletter.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Directorio para la base SQLite y su volumen.
RUN mkdir -p /app/data && chmod 777 /app/data

COPY --from=build /app/publish ./

# Puerto de la API.
EXPOSE 3002

# `ASPNETCORE_URLS` lo define docker-compose; default interno.
ENV ASPNETCORE_URLS=http://+:3002

ENTRYPOINT ["dotnet", "Walletter.Api.dll"]
