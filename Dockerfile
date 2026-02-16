# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore (caching layer)
COPY AutoToolCatalog.csproj .
RUN dotnet restore

# Copy everything else and publish
COPY . .
# IMPORTANT: Remove --no-restore so publish re-verifies/resolves packages if needed
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime (smaller image)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Use Render's $PORT (recommended) – defaults to 10000
EXPOSE 10000
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000}

# Correct DLL name (matches your .csproj)
ENTRYPOINT ["dotnet", "AutoToolCatalog.dll"]