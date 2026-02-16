# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore (caching layer)
COPY *.sln .
COPY */*.csproj ./
RUN for file in $(find . -name "*.csproj"); do mkdir -p ${file%/*} && mv $file ${file%/*}/; done
RUN dotnet restore

# Copy everything else and build
COPY . .
WORKDIR /src
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime (smaller image)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Auto-Tool-Catalog.dll"]