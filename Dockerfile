# Stage 1: Build + test
# The SDK image (~800 MB) is used only during build. Tests run here so a failing
# test fails the Docker build — nobody gets an image with red tests.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first to leverage layer caching for restore step
COPY Directory.Build.props ./
COPY SyncMetrics.WeatherPipeline.sln ./
COPY src/SyncMetrics.Pipeline.Core/SyncMetrics.Pipeline.Core.csproj src/SyncMetrics.Pipeline.Core/
COPY src/SyncMetrics.Pipeline.Application/SyncMetrics.Pipeline.Application.csproj src/SyncMetrics.Pipeline.Application/
COPY src/SyncMetrics.Pipeline.Infrastructure/SyncMetrics.Pipeline.Infrastructure.csproj src/SyncMetrics.Pipeline.Infrastructure/
COPY src/SyncMetrics.Pipeline.Console/SyncMetrics.Pipeline.Console.csproj src/SyncMetrics.Pipeline.Console/
COPY tests/SyncMetrics.Pipeline.UnitTests/SyncMetrics.Pipeline.UnitTests.csproj tests/SyncMetrics.Pipeline.UnitTests/
COPY tests/SyncMetrics.Pipeline.IntegrationTests/SyncMetrics.Pipeline.IntegrationTests.csproj tests/SyncMetrics.Pipeline.IntegrationTests/

RUN dotnet restore

# Copy source and run tests
COPY . .
RUN dotnet build -c Release --no-restore
RUN dotnet test -c Release --no-restore --no-build --verbosity normal

# Publish the console project
RUN dotnet publish src/SyncMetrics.Pipeline.Console/SyncMetrics.Pipeline.Console.csproj \
    -c Release -o /app/publish --no-restore

# Stage 2: Runtime
# The runtime image (~80 MB) contains only the .NET runtime — no SDK, no source,
# no test assemblies. 10x smaller than the build stage.
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Mount a volume for output: docker run --rm -v "${PWD}/output:/app/output" syncmetrics-pipeline
ENTRYPOINT ["dotnet", "SyncMetrics.Pipeline.Console.dll"]
