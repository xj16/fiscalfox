# Build the FiscalFox API (C# + F# analytics) and produce a runtime image.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against the whole solution so project references resolve.
COPY FiscalFox.sln global.json Directory.Build.props ./
COPY src/ ./src/
COPY data/ ./data/

RUN dotnet restore src/FiscalFox.Api/FiscalFox.Api.csproj
RUN dotnet publish src/FiscalFox.Api/FiscalFox.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Ship the cached price data alongside the app.
COPY --from=build /src/data/prices ./data/prices
EXPOSE 8080
ENTRYPOINT ["dotnet", "FiscalFox.Api.dll"]
