#!/usr/bin/env bash
# Atualiza o Monitor Portoconseg no VPS para a versão mais nova do repositório. Os dados são mantidos.
set -euo pipefail
cd /opt/monitor-portoconseg
git pull --ff-only
cd deploy
docker compose up -d --build
docker image prune -f >/dev/null
echo "Atualizado."
