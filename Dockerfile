# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore (efficient caching)
COPY AutoToolCatalog.csproj .
RUN dotnet restore

# Copy everything else and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime image (small & secure)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Use Render's $PORT env var (defaults to 10000) for binding
EXPOSE 10000
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000}

# Correct DLL name from your project
ENTRYPOINT ["dotnet", "AutoToolCatalog.dll"]