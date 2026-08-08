FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SubVora.Api/SubVora.Api.csproj src/SubVora.Api/
COPY src/SubVora.Application/SubVora.Application.csproj src/SubVora.Application/
COPY src/SubVora.Infrastructure/SubVora.Infrastructure.csproj src/SubVora.Infrastructure/
COPY src/SubVora.Domain/SubVora.Domain.csproj src/SubVora.Domain/
RUN dotnet restore src/SubVora.Api/SubVora.Api.csproj

COPY src/ src/
RUN dotnet publish src/SubVora.Api/SubVora.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The base image ships a non-root 'app' user for this; without it the container runs as root.
# Nothing here needs it - the port is above 1024 and nothing is written outside /app - so this
# just removes the privilege from any future container-escape or RCE chain rather than after one.
USER $APP_UID

# Local/compose default. Render overrides it to 10000 via render.yaml to match the port it injects,
# so that file is the authority in deployment - this line only matters outside it.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SubVora.Api.dll"]
