FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["NetRts.sln", "."]
COPY ["src/NetRts.Client/NetRts.Client.csproj", "src/NetRts.Client/"]
COPY ["src/NetRts.Contracts/NetRts.Contracts.csproj", "src/NetRts.Contracts/"]

RUN dotnet restore

COPY . .
WORKDIR "/src/src/NetRts.Client"
RUN dotnet build "NetRts.Client.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NetRts.Client.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=publish /app/publish/wwwroot .
COPY src/NetRts.Client/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
