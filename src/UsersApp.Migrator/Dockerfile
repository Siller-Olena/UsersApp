FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/UsersApp.Data/UsersApp.Data.csproj src/UsersApp.Data/
COPY src/UsersApp.Migrator/UsersApp.Migrator.csproj src/UsersApp.Migrator/
RUN dotnet restore src/UsersApp.Migrator/UsersApp.Migrator.csproj
COPY src/UsersApp.Data src/UsersApp.Data
COPY src/UsersApp.Migrator src/UsersApp.Migrator
RUN dotnet publish src/UsersApp.Migrator/UsersApp.Migrator.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "UsersApp.Migrator.dll"]
