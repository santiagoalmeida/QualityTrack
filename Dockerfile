# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/QualityTrack.Web -c Release -o /app

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The SQLite database lives in a volume so it survives image upgrades.
VOLUME /data
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__QualityTrack="Data Source=/data/qualitytrack.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "QualityTrack.Web.dll"]
