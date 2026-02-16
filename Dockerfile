# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for better caching
COPY *.sln .
COPY */*.csproj ./  # This assumes projects are in subfolders like CNCToolingDatabase/CNCToolingDatabase.csproj

# This loop fixes directory structure if COPY flattened files (common issue with multi-project solutions)
RUN for file in $(find . -name "*.csproj"); do \
      dir=$(dirname "$file"); \
      mkdir -p "$dir" 2>/dev/null || true; \
      mv "$file" "$dir/" 2>/dev/null || true; \
    done

RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Publish (adjust project path/name if needed — see notes below)
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime (smaller image)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Best practice for Render: use the $PORT env var (Render sets it to 10000 by default)
EXPOSE 10000
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000}
# Alternative (if you really want 5000): ENV ASPNETCORE_URLS=http://0.0.0.0:5000
# But using $PORT is strongly recommended to avoid detection issues

# ENTRYPOINT — IMPORTANT: DLL name must match your actual published output
ENTRYPOINT ["dotnet", "Auto-Tool-Catalog.dll"]