# FILE: src/TruthLens.Worker/Dockerfile

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /workDir

COPY ["src/TruthLens.Worker/TruthLens.Worker.csproj", "src/TruthLens.Worker/"]
COPY ["src/TruthLens.Application/TruthLens.Application.csproj", "src/TruthLens.Application/"]
COPY ["src/TruthLens.Domain/TruthLens.Domain.csproj", "src/TruthLens.Domain/"]
COPY ["src/TruthLens.Infrastructure/TruthLens.Infrastructure.csproj", "src/TruthLens.Infrastructure/"]

RUN dotnet restore "src/TruthLens.Worker/TruthLens.Worker.csproj"

COPY . .
RUN dotnet publish "src/TruthLens.Worker/TruthLens.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TruthLens.Worker.dll"]
