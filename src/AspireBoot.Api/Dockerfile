# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/ .config/
COPY src/AspireBoot.Core/AspireBoot.Core.csproj src/AspireBoot.Core/
COPY src/AspireBoot.ServiceDefaults/AspireBoot.ServiceDefaults.csproj src/AspireBoot.ServiceDefaults/
COPY src/AspireBoot.Api/AspireBoot.Api.csproj src/AspireBoot.Api/
RUN dotnet restore src/AspireBoot.Api/AspireBoot.Api.csproj
COPY src/ src/
# Pre-generate Wolverine handler code (production runs with TypeLoadMode.Static). No infrastructure needed.
# Placeholder connection strings: codegen builds the service graph but never connects.
RUN ConnectionStrings__aspireboot="Host=codegen;Database=codegen" \
    ConnectionStrings__rabbitmq="amqp://codegen" \
    ConnectionStrings__redis="codegen:6379" \
    dotnet run --project src/AspireBoot.Api -c Release --no-launch-profile -- codegen write
RUN dotnet publish src/AspireBoot.Api -c Release -o /app/publish --no-restore /p:UseAppHost=false
# Self-contained EF Core migration bundle, executed by the one-shot "migrator" service.
RUN dotnet tool restore && dotnet ef migrations bundle -p src/AspireBoot.Core -s src/AspireBoot.Api \
    --self-contained -r linux-$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/') -o /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=build /app/efbundle ./efbundle
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "AspireBoot.Api.dll"]
