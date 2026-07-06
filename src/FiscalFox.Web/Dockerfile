# Build the FiscalFox Blazor Server UI and produce a runtime image.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY FiscalFox.sln global.json Directory.Build.props ./
COPY src/ ./src/

RUN dotnet restore src/FiscalFox.Web/FiscalFox.Web.csproj
RUN dotnet publish src/FiscalFox.Web/FiscalFox.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "FiscalFox.Web.dll"]
