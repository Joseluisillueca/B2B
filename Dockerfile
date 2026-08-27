# syntax=docker/dockerfile:1
# Imagen de producción de B2BNew (portal + API de ingesta del conector BC).
# .NET 10, framework-dependent. Sirve el frontend estático de wwwroot y la API.

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore en capa propia (cachea si no cambian las dependencias)
COPY backend/src/B2B.Api/B2B.Api.csproj backend/src/B2B.Api/
RUN dotnet restore backend/src/B2B.Api/B2B.Api.csproj

# Resto del código y publicación
COPY backend/ backend/
RUN dotnet publish backend/src/B2B.Api/B2B.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# QuestPDF (SkiaSharp) necesita fontconfig para renderizar texto en los PDF
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
# Railway (y la mayoría de PaaS) inyectan PORT; enlazamos ahí. Fallback 8080 en local.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet B2B.Api.dll"]
