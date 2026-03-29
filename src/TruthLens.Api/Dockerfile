# FILE: src/TruthLens.Api/Dockerfile

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /workDir

COPY ["src/TruthLens.Api/TruthLens.Api.csproj", "src/TruthLens.Api/"]
COPY ["src/TruthLens.Application/TruthLens.Application.csproj", "src/TruthLens.Application/"]
COPY ["src/TruthLens.Domain/TruthLens.Domain.csproj", "src/TruthLens.Domain/"]
COPY ["src/TruthLens.Infrastructure/TruthLens.Infrastructure.csproj", "src/TruthLens.Infrastructure/"]

RUN dotnet restore "src/TruthLens.Api/TruthLens.Api.csproj"

COPY . .
RUN dotnet publish "src/TruthLens.Api/TruthLens.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5075
ENV ASPNETCORE_URLS=http://+:5075
ENTRYPOINT ["dotnet", "TruthLens.Api.dll"]
