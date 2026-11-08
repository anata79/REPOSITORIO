#!/usr/bin/env bash
# Instala o Monitor Portoconseg no VPS (Ubuntu ou Debian), como root:
#   curl -fsSLo instalar.sh https://raw.githubusercontent.com/anata79/REPOSITORIO/main/deploy/instalar-servidor.sh
#   bash instalar.sh monitor.portoconseg.com.br
set -euo pipefail

REPO="https://github.com/anata79/REPOSITORIO.git"
DIR="/opt/monitor-portoconseg"
DOMAIN="${1:-}"

if [ "$(id -u)" -ne 0 ]; then
  echo "Rode como root (ou com sudo)." >&2
  exit 1
fi

if [ -z "$DOMAIN" ]; then
  read -rp "Endereço do painel (ex.: monitor.portoconseg.com.br): " DOMAIN
fi
DOMAIN="${DOMAIN#https://}"
DOMAIN="${DOMAIN#http://}"
DOMAIN="${DOMAIN%%/*}"

echo "==> Conferindo se $DOMAIN aponta para este servidor"
MY_IP="$(curl -fsS4 https://api.ipify.org || true)"
DNS_IP="$(getent ahostsv4 "$DOMAIN" | awk '{print $1; exit}' || true)"
if [ -n "$MY_IP" ] && [ "$MY_IP" != "$DNS_IP" ]; then
  echo "ATENÇÃO: $DOMAIN aponta para '${DNS_IP:-nenhum IP}', mas este servidor é $MY_IP."
  echo "Crie no hPanel um registro DNS tipo A: nome 'monitor', valor $MY_IP. Pode levar alguns minutos para valer."
  read -rp "Continuar mesmo assim? (s/N) " ok
  [ "${ok,,}" = "s" ] || exit 1
fi

echo "==> Instalando Git e Docker (se faltar)"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq ca-certificates curl git >/dev/null
if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL https://get.docker.com | sh
fi
systemctl enable --now docker >/dev/null 2>&1 || true

echo "==> Baixando o sistema em $DIR"
if [ -d "$DIR/.git" ]; then
  git -C "$DIR" pull --ff-only
else
  git clone --depth 1 "$REPO" "$DIR"
fi

cd "$DIR/deploy"
echo "DOMAIN=$DOMAIN" > .env

if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
  echo "==> Liberando portas 80 e 443 no firewall"
  ufw allow 80/tcp >/dev/null
  ufw allow 443/tcp >/dev/null
fi

echo "==> Montando e iniciando (a primeira vez leva alguns minutos)"
docker compose up -d --build

echo "==> Aguardando o servidor responder"
for _ in $(seq 1 60); do
  if docker compose exec -T app sh -c 'test -f /data/monitor.db' 2>/dev/null; then break; fi
  sleep 2
done

create_user() {
  local login="$1" name="$2"
  if docker compose exec -T app ./Monitor.Server usuario-listar | grep -q "^${login}[[:space:]]"; then
    echo "Usuário $login já existe."
  else
    echo "==> Criando o acesso de $name (login: $login)"
    docker compose exec -it app ./Monitor.Server usuario-criar "$login" "$name"
  fi
}
create_user andre "André"
create_user renata "Renata"

echo
echo "Pronto. Abra https://$DOMAIN e entre com o seu login."
echo "Se o navegador avisar sobre o certificado, espere 1 ou 2 minutos: o HTTPS é emitido automaticamente."
