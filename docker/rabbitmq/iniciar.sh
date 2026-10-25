#!/usr/bin/env bash
# Sobe o RabbitMQ e garante o usuário da API (REQ-14, research R-09): ele pode abrir a conexão
# da prontidão no vhost /, mas os padrões "^$" de configurar, escrever e ler não casam com
# nenhuma fila nem troca. Roda a cada subida e é idempotente: um ambiente local antigo ganha o
# usuário sem recriar o volume. O usuário do Worker continua o RABBITMQ_DEFAULT_USER.
set -euo pipefail

usuario="${OMS_RABBITMQ_API_USUARIO:?defina OMS_RABBITMQ_API_USUARIO}"
senha="${OMS_RABBITMQ_API_SENHA:?defina OMS_RABBITMQ_API_SENHA}"

docker-entrypoint.sh rabbitmq-server &
servidor=$!
trap 'kill -TERM "$servidor" 2>/dev/null; wait "$servidor"' TERM INT

# O ping responde antes de a aplicação "rabbit" terminar de subir; await_startup espera por ela.
until rabbitmq-diagnostics -q ping >/dev/null 2>&1; do
  if ! kill -0 "$servidor" 2>/dev/null; then
    echo "O RabbitMQ parou antes de responder." >&2
    exit 1
  fi
  sleep 1
done
rabbitmqctl -q await_startup

if rabbitmqctl -q list_users | awk '{print $1}' | grep -qx "$usuario"; then
  rabbitmqctl -q change_password "$usuario" "$senha"
else
  rabbitmqctl -q add_user "$usuario" "$senha"
fi
rabbitmqctl -q set_permissions -p / "$usuario" '^$' '^$' '^$'
echo "Usuário da API no broker pronto: $usuario (só conecta)."

wait "$servidor"
