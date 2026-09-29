FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LinkxAi.Api/LinkxAi.Api.csproj src/LinkxAi.Api/
RUN dotnet restore src/LinkxAi.Api/LinkxAi.Api.csproj
COPY src/LinkxAi.Api/ src/LinkxAi.Api/
RUN dotnet publish src/LinkxAi.Api/LinkxAi.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "LinkxAi.Api.dll"]
