# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on its own layer: it only re-runs when the csproj changes.
COPY MdfTracker.Api.csproj .
RUN dotnet restore MdfTracker.Api.csproj

COPY . .
RUN dotnet publish MdfTracker.Api.csproj -c Release -o /app/publish --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# The aspnet image ships a non-root "app" user; use it.
USER app

# Render (and most PaaS) inject PORT and expect the app to bind to it.
# 8080 is just the local default when PORT is not set.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "dotnet MdfTracker.Api.dll --urls http://0.0.0.0:${PORT}"]
