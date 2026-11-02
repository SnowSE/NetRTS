FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/NetRts.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ConnectionStrings__sqlite="Data Source=/home/app/netrts.db"
USER $APP_UID
ENTRYPOINT ["dotnet", "NetRts.Server.dll"]
