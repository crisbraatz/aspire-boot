# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/AspireBoot.Core/AspireBoot.Core.csproj src/AspireBoot.Core/
COPY src/AspireBoot.ServiceDefaults/AspireBoot.ServiceDefaults.csproj src/AspireBoot.ServiceDefaults/
COPY src/AspireBoot.Worker/AspireBoot.Worker.csproj src/AspireBoot.Worker/
RUN dotnet restore src/AspireBoot.Worker/AspireBoot.Worker.csproj
COPY src/ src/
# Placeholder connection strings: codegen builds the service graph but never connects.
RUN ConnectionStrings__aspireboot="Host=codegen;Database=codegen" \
    ConnectionStrings__rabbitmq="amqp://codegen" \
    ConnectionStrings__redis="codegen:6379" \
    dotnet run --project src/AspireBoot.Worker -c Release --no-launch-profile -- codegen write
RUN dotnet publish src/AspireBoot.Worker -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "AspireBoot.Worker.dll"]
