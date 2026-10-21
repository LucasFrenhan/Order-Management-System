# Data Model: Estrutura da solução e ambiente local (REQ-02)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Este REQ não cria entidades de negócio (elas chegam no REQ-04). Os modelos são de
configuração e de estado de saúde.

## Configuração

### ConnectionStrings:SqlServer (string)

| Regra | Valor |
|-------|-------|
| Obrigatória | sim, em todos os ambientes; a API não inicia sem ela |
| Desenvolvimento | `Server=localhost,1433;Database=PedidoFulfillment;User Id=sa;Password=<senha dev>;Encrypt=True;TrustServerCertificate=True` |
| Sobrescrita | variável `ConnectionStrings__SqlServer` |
| Mensagem se ausente | "A configuração ConnectionStrings:SqlServer é obrigatória." |

### OpcoesRabbitMQ (seção `RabbitMQ`)

| Campo | Tipo | Obrigatório | Desenvolvimento | Regra |
|-------|------|-------------|-----------------|-------|
| Servidor | string | sim | `localhost` | não vazio |
| Porta | int | sim | `5672` | 1–65535 |
| Usuario | string | sim | `oms` | não vazio |
| Senha | string | sim | `oms-dev-local` | não vazio |
| VirtualHost | string | sim | `/` | não vazio |

Mensagem se inválido: "A configuração RabbitMQ:<Campo> é obrigatória." (ou "RabbitMQ:Porta
deve estar entre 1 e 65535."), sem mostrar valores.

## Estado de saúde

| Dependência | Nome no relatório | Verificação | Limite |
|-------------|-------------------|-------------|--------|
| Banco | `sqlserver` | conexão no catálogo `master` + `SELECT 1` | 5 s |
| Broker | `rabbitmq` | abrir e fechar conexão AMQP | 5 s |

Estados: `Healthy` ou `Unhealthy`. Formato da resposta em [contracts/saude.md](./contracts/saude.md).

## Direção de dependências entre projetos (FR-002)

| Projeto | Pode referenciar |
|---------|------------------|
| Domain | nenhum projeto da solução |
| Application | Domain |
| Infrastructure | Application, Domain |
| API | Application, Infrastructure, Domain |
