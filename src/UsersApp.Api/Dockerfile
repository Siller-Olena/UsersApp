FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/UsersApp.Data/UsersApp.Data.csproj src/UsersApp.Data/
COPY src/UsersApp.Api/UsersApp.Api.csproj src/UsersApp.Api/
RUN dotnet restore src/UsersApp.Api/UsersApp.Api.csproj
COPY src/UsersApp.Data src/UsersApp.Data
COPY src/UsersApp.Api src/UsersApp.Api
RUN dotnet publish src/UsersApp.Api/UsersApp.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "UsersApp.Api.dll"]
