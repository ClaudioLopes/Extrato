#!/usr/bin/env bash
# Smoke test do app publicado: sobe o docker compose num projeto separado e confere o que só quebra em execução
# (os testes de unidade não pegam): index.html com os nomes com hash, arquivos que o service worker guarda para o
# modo offline, API protegida, primeiro acesso com código e login que sobrevive ao reinício do container.
#   bash scripts/smoke-docker.sh            (usa as portas 18080 e 14334; apaga tudo ao terminar)
set -uo pipefail

PROJETO=${PROJETO:-extrato-smoke}
export EXTRATO_PORTA=${EXTRATO_PORTA:-18080}
export SQL_PORTA=${SQL_PORTA:-14334}
export AUTH_CODIGO=codigo-smoke-123
URL="http://localhost:$EXTRATO_PORTA"
TMP=$(mktemp -d)
COOKIES="$TMP/cookies.txt"
falhas=0

dc() { docker compose -p "$PROJETO" "$@"; }
ok() { echo "ok     $1"; }
falha() { echo "FALHA  $1"; falhas=$((falhas + 1)); }
status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
confere() { # confere <descrição> <esperado> <obtido>
    if [ "$2" = "$3" ]; then ok "$1"; else falha "$1 (esperado $2, veio $3)"; fi
}
esperar_app() {
    for _ in $(seq 1 90); do
        [ "$(status "$URL/health")" = 200 ] && return 0
        sleep 2
    done
    return 1
}
encerrar() {
    if [ "$falhas" -gt 0 ]; then
        echo "--- últimas linhas do log do app"
        dc logs --no-color --tail 60 extrato || true
    fi
    dc down -v --remove-orphans >/dev/null 2>&1 || true
    rm -rf "$TMP"
}
trap encerrar EXIT

echo "== subindo $PROJETO em $URL"
if ! dc up -d --build --quiet-pull; then
    falha "docker compose up"
    exit 1
fi
if ! esperar_app; then
    falha "app não respondeu em /health"
    exit 1
fi
ok "app no ar"

echo "== páginas"
for rota in / /registrar /treino/sessao/3 /index.html; do
    confere "GET $rota" 200 "$(status "$URL$rota")"
done
curl -s "$URL/" -o "$TMP/index.html"
if grep -q '#\[' "$TMP/index.html"; then falha "index.html ainda tem o marcador #[.{fingerprint}]"; else ok "marcador de fingerprint substituído"; fi
if grep -q '<script type="importmap"></script>' "$TMP/index.html"; then falha "importmap vazio no index.html"; else ok "importmap preenchido"; fi
if grep -qE 'src="_framework/blazor\.webassembly\.[a-z0-9]+\.js"' "$TMP/index.html"; then ok "script do Blazor com hash"; else falha "script do Blazor sem hash"; fi
confere "Cache-Control do index.html" "no-cache" "$(curl -s -D - -o /dev/null "$URL/" | tr -d '\r' | awk -F': ' 'tolower($1)=="cache-control"{print $2}')"

echo "== scripts do index.html e modo offline"
curl -s "$URL/service-worker-assets.js" -o "$TMP/sw-assets.js"
confere "GET /service-worker.js" 200 "$(status "$URL/service-worker.js")"
# todo .js do _framework citado no index.html precisa responder; os com hash (os carregados de fato)
# precisam estar no manifesto do service worker, senão o app não abre sem internet
referencias=$(grep -oE '_framework/[A-Za-z0-9._-]+\.js' "$TMP/index.html" | sort -u)
[ -n "$referencias" ] || falha "nenhum script do _framework no index.html"
for arquivo in $referencias; do
    confere "GET /$arquivo" 200 "$(status "$URL/$arquivo")"
    if [[ "$arquivo" =~ \.[a-z0-9]{10}\.js$ ]]; then
        if grep -q "\"$arquivo\"" "$TMP/sw-assets.js"; then ok "offline: $arquivo no service worker"; else falha "offline: $arquivo fora do manifesto do service worker"; fi
    fi
done

echo "== API e acesso"
confere "GET /api/perfil sem login" 401 "$(status "$URL/api/perfil")"
confere "GET /api/inexistente" 404 "$(status "$URL/api/inexistente")"
corpo='{"login":"smoke","senha":"Teste-Fumaca-2026!"}'
sem_codigo=$(status -X POST -H 'Content-Type: application/json' -d "$corpo" "$URL/api/auth/configurar")
if [ "$sem_codigo" = 200 ]; then falha "primeiro acesso aceito sem o código"; else ok "primeiro acesso sem código recusado ($sem_codigo)"; fi
confere "primeiro acesso com código" 200 "$(status -c "$COOKIES" -X POST -H 'Content-Type: application/json' \
    -d '{"login":"smoke","senha":"Teste-Fumaca-2026!","codigo":"'"$AUTH_CODIGO"'"}' "$URL/api/auth/configurar")"
if grep -qi 'httponly' "$COOKIES"; then ok "cookie de sessão HttpOnly"; else falha "cookie de sessão sem HttpOnly"; fi
confere "GET /api/alimentos logado" 200 "$(status -b "$COOKIES" "$URL/api/alimentos?busca=arroz")"

echo "== reinício do container"
dc restart extrato >/dev/null
if esperar_app; then ok "app voltou"; else falha "app não voltou após reiniciar"; fi
confere "sessão vale após reiniciar" 200 "$(status -b "$COOKIES" "$URL/api/alimentos?busca=arroz")"
if dc logs --no-color extrato 2>&1 | grep -qiE 'access to the path .* denied|failed to (write|store).*key|unauthorizedaccess'; then
    falha "erro ao gravar as chaves da sessão no volume"
else
    ok "chaves da sessão gravadas sem erro"
fi

echo
if [ "$falhas" -gt 0 ]; then
    echo "$falhas verificação(ões) falharam"
    exit 1
fi
echo "todas as verificações passaram"
