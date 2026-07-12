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

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SubVora.Api.dll"]
