# SQL Server 2022 com Full-Text Search (REQ-22, research R-02).
#
# A imagem oficial Linux não traz o Full-Text Search, que a busca do catálogo de produtos usa
# (português do Brasil, LCID 1046). Esta imagem é a mesma base de antes mais o pacote
# mssql-server-fts. A versão dos pacotes é fixada na versão do motor da base: sem isso, o apt
# instala o CU mais novo e troca o motor. Para atualizar, troque a tag da base e a VERSAO juntas
# (a versão do motor está em SELECT SERVERPROPERTY('ProductVersion')).
FROM mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04

ARG VERSAO=16.0.4265.3-1

USER root

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gnupg ca-certificates \
    && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
        | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
    && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main" \
        > /etc/apt/sources.list.d/mssql-server-2022.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends "mssql-server=${VERSAO}" "mssql-server-fts=${VERSAO}" \
    && rm -rf /var/lib/apt/lists/*

USER mssql
