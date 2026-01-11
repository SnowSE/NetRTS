FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["NetRts.sln", "."]
COPY ["src/NetRts.Api/NetRts.Api.csproj", "src/NetRts.Api/"]
COPY ["src/NetRts.Application/NetRts.Application.csproj", "src/NetRts.Application/"]
COPY ["src/NetRts.Domain/NetRts.Domain.csproj", "src/NetRts.Domain/"]
COPY ["src/NetRts.Infrastructure/NetRts.Infrastructure.csproj", "src/NetRts.Infrastructure/"]
COPY ["src/NetRts.Contracts/NetRts.Contracts.csproj", "src/NetRts.Contracts/"]
COPY ["src/NetRts.ServiceDefaults/NetRts.ServiceDefaults.csproj", "src/NetRts.ServiceDefaults/"]

RUN dotnet restore

COPY . .
WORKDIR "/src/src/NetRts.Api"
RUN dotnet build "NetRts.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NetRts.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NetRts.Api.dll"]
