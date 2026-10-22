# Quickstart: validação do REQ-07

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/verificacao-e-demonstracao.md](./contracts/verificacao-e-demonstracao.md)

## 1. Suíte local, sem mudança

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde. Sem Docker e sem `OMS_EXIGIR_DOCKER`, os testes de banco
continuam ignorados com "Docker indisponível" (FR-021).

## 2. Exigência de Docker

```powershell
docker compose down; # garanta que o Docker Desktop está parado, ou use uma máquina sem Docker
$env:OMS_EXIGIR_DOCKER = "true"
dotnet test tests/PedidoFulfillment.Testes.Integracao --filter "FullyQualifiedName~SaudeComDependencias"
Remove-Item Env:OMS_EXIGIR_DOCKER
```

**Esperado**: falha com "Docker obrigatório neste ambiente (OMS_EXIGIR_DOCKER=true).". O
teste unitário de `AmbienteDocker.Obrigatorio` cobre a regra sem precisar parar o Docker.

## 3. Cobertura local, igual ao CI

```powershell
dotnet test PedidoFulfillment.sln -c Release --logger "trx;LogFilePrefix=resultado" --results-directory resultados --collect "Code Coverage;Format=cobertura" --settings coverage.runsettings
bash .github/scripts/resumo-testes.sh resultados
```

**Esperado**:
- os arquivos `.trx` e `.cobertura.xml` aparecem em `resultados/`;
- o script imprime a tabela de contagens, a cobertura sem `Migracoes` e sai com 0 (nenhum
  teste ignorado).

## 4. Demonstração (User Story 4)

```powershell
docker compose --profile demo up -d --wait --build
curl.exe -s http://localhost:5230/health/pronto
curl.exe -s -H "Content-Type: application/json" -d '{\"email\":\"admin@oms.local\",\"senha\":\"AdminDev2026\"}' http://localhost:5230/api/v1/auth/login
```

**Esperado**:
- os três serviços ficam `healthy` (`docker compose --profile demo ps`), em menos de 3
  minutos com as imagens baixadas;
- a prontidão responde 200;
- o login devolve `tokenAcesso`;
- `http://localhost:5230/scalar/v1` abre.

A API não pode estar rodando pelo SDK ao mesmo tempo, porque as duas usam a porta 5230.

## 5. Sem perfil e encerramento

```powershell
docker compose --profile demo down -v
docker compose up -d --wait
docker compose ps --services
docker compose down
```

**Esperado**:
- `down -v` remove os três serviços e os volumes;
- sem perfil, só `sqlserver` e `rabbitmq` sobem.

## 6. No GitHub

1. Faça push da branch e abra o PR.

   **Esperado**: os jobs "Build e testes" e "Demonstração" rodam em paralelo e ficam
   verdes. O resumo mostra as contagens (0 ignorados) e a cobertura, e o artefato
   `resultados-testes` está disponível.
2. Num commit temporário, quebre um teste.

   **Esperado**: "Build e testes" fica vermelho, com o nome do teste no resumo. Reverta o
   commit.
3. Para saber o tempo total, consulte a duração da execução.

   **Esperado**: até 15 minutos.
