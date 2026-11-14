# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Extrato.sln ./
COPY src/Extrato.Domain/Extrato.Domain.csproj src/Extrato.Domain/
COPY src/Extrato.Contracts/Extrato.Contracts.csproj src/Extrato.Contracts/
COPY src/Extrato.Application/Extrato.Application.csproj src/Extrato.Application/
COPY src/Extrato.Infrastructure/Extrato.Infrastructure.csproj src/Extrato.Infrastructure/
COPY src/Extrato.Web/Extrato.Web.csproj src/Extrato.Web/
COPY src/Extrato.Api/Extrato.Api.csproj src/Extrato.Api/
RUN dotnet restore src/Extrato.Api/Extrato.Api.csproj
COPY src/ src/
RUN dotnet publish src/Extrato.Api/Extrato.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# tzdata: o "dia" do painel usa o fuso America/Sao_Paulo
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && apt-get clean
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=America/Sao_Paulo \
    ConnectionStrings__Extrato="Data Source=/data/extrato.db" \
    Auth__PastaChaves=/data/chaves
COPY --from=build /app .
# /data (volume) guarda o banco SQLite e as chaves do cookie de sessão. O entrypoint começa como root só
# para dar /data ao usuário do app (volumes do Fly.io chegam como root) e roda o app sem root (setpriv).
COPY docker-entrypoint.sh /usr/local/bin/
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh && chmod 755 /usr/local/bin/docker-entrypoint.sh && mkdir -p /data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["docker-entrypoint.sh"]
