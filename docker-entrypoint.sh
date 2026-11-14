#!/bin/sh
# Volumes (Docker ou Fly.io) podem chegar como root: ajusta o dono de /data e roda o app sem root.
set -e
mkdir -p /data/chaves
if [ "$(id -u)" = "0" ]; then
    chown -R "$APP_UID:$APP_UID" /data
    chmod 700 /data/chaves
    exec setpriv --reuid="$APP_UID" --regid="$APP_UID" --clear-groups dotnet Extrato.Api.dll
fi
exec dotnet Extrato.Api.dll
