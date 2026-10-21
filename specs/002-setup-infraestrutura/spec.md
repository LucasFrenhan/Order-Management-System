# Feature Specification: Estrutura da solução e ambiente local

**Feature Branch**: `feature/req-02-setup-infraestrutura`

**Requisito**: REQ-02 (GitHub issue #3 — "[Setup] Estrutura da Solução .NET 10 e Docker Compose")

**Created**: 2026-10-21

**Status**: Draft

**Input**: User description: "REQ-02 (GitHub issue #3): completar a estrutura base da solução PedidoFulfillment e o ambiente local. A solução já existe desde o REQ-01 com Domain, Application e API; falta o projeto PedidoFulfillment.Infrastructure (referenciado pela API, referenciando Application e Domain) para hospedar persistência (REQ-04) e mensageria (REQ-06). Criar docker-compose.yml subindo SQL Server (decisão já tomada: SQL Server, não PostgreSQL, conforme constituição) e RabbitMQ com plugin de Management na porta 15672. Configurar appsettings.Development.json com strings de conexão locais do SQL Server e do RabbitMQ. Sem segredos reais versionados. Desenvolvedor deve conseguir subir dependências com um comando e a API deve ler as configurações."

## Contexto

O REQ-01 criou a solução com as camadas Domain, Application e API, o mínimo para publicar
o contrato. Este requisito completa a estrutura de camadas prevista na issue de setup e
prepara o ambiente local de que os próximos requisitos dependem: o banco de dados
relacional (persistência, REQ-04) e o broker de mensagens (eventos, REQ-06).

A escolha do banco já está feita pela constituição: SQL Server. A issue cita "PostgreSQL ou
SQL Server", e esta spec fixa SQL Server.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Subir as dependências locais com um comando (Priority: P1)

Um desenvolvedor recém-chegado clona o repositório e, com um único comando, sobe o banco
de dados e o broker de mensagens. Ele sabe quando os dois estão prontos e acessa o painel de
administração do broker no navegador.

**Why this priority**: Sem o ambiente local, ninguém consegue desenvolver nem testar a
persistência e a mensageria dos próximos requisitos.

**Independent Test**: Em uma máquina com apenas o Docker e o SDK instalados, executar o
comando de subida e verificar que o banco aceita conexões, que o broker aceita conexões e
que o painel do broker abre na porta 15672.

**Acceptance Scenarios**:

1. **Given** um clone novo do repositório e nenhum contêiner rodando, **When** o
   desenvolvedor executa o comando de subida documentado, **Then** o banco de dados e o
   broker de mensagens sobem e são reportados como saudáveis.
2. **Given** as dependências no ar, **When** o desenvolvedor abre `http://localhost:15672`,
   **Then** vê o painel de administração do broker e consegue entrar com as credenciais de
   desenvolvimento documentadas.
3. **Given** as dependências paradas e subidas de novo, **When** o desenvolvedor consulta o
   banco, **Then** os dados criados antes continuam lá.
4. **Given** a necessidade de recomeçar do zero, **When** o desenvolvedor executa o comando
   de limpeza documentado, **Then** os dados locais são apagados e a próxima subida começa
   de um ambiente vazio.

---

### User Story 2 - API lê a configuração do ambiente local (Priority: P1)

Com as dependências no ar, o desenvolvedor executa a API em Development. Ela lê, sem
nenhum passo manual, os endereços e as credenciais de desenvolvimento do banco e do broker.
Em qualquer outro ambiente, esses valores vêm de variáveis de ambiente ou de um cofre de
segredos, e nunca de arquivos versionados.

**Why this priority**: O REQ-04 e o REQ-06 precisam dessas configurações. Sem um padrão
definido agora, cada requisito inventaria o seu.

**Independent Test**: Executar a API em Development e verificar que ela carrega as duas
configurações. Executar em Production sem essas configurações e verificar que ela se recusa
a iniciar com uma mensagem clara.

**Acceptance Scenarios**:

1. **Given** as dependências no ar e a API em Development, **When** a API inicia, **Then**
   as configurações de banco e de broker são carregadas a partir dos valores de
   desenvolvimento versionados.
2. **Given** a API em um ambiente que não é Development e sem as configurações de banco ou
   de broker, **When** a API inicia, **Then** ela falha imediatamente com uma mensagem que
   nomeia a configuração ausente, sem expor valores.
3. **Given** uma variável de ambiente com outra string de conexão, **When** a API inicia,
   **Then** o valor da variável prevalece sobre o do arquivo.
4. **Given** a API em execução, **When** o endereço de vida (`/health`) é consultado sem
   token, **Then** responde 200, mesmo com as dependências fora do ar.
5. **Given** a API e as dependências no ar, **When** o endereço de prontidão
   (`/health/pronto`) é consultado sem token, **Then** responde 200 e informa banco e broker
   como saudáveis.
6. **Given** o banco ou o broker fora do ar, **When** `/health/pronto` é consultado,
   **Then** responde 503 e indica qual dependência falhou, sem expor strings de conexão,
   credenciais nem mensagens internas.

---

### User Story 3 - Estrutura de camadas completa e protegida (Priority: P2)

O time tem as quatro camadas previstas (Domain, Application, Infrastructure, API) com a
direção de dependências da arquitetura garantida automaticamente. Assim, ninguém faz, por
exemplo, o domínio depender da infraestrutura por engano.

**Why this priority**: É a base estrutural dos próximos requisitos, mas não bloqueia o
desenvolvimento imediato.

**Independent Test**: Compilar a solução e rodar o teste de arquitetura, que falha se
alguma camada depender de outra fora da direção permitida.

**Acceptance Scenarios**:

1. **Given** a solução, **When** ela é compilada, **Then** contém os projetos
   PedidoFulfillment.Domain, PedidoFulfillment.Application,
   PedidoFulfillment.Infrastructure e PedidoFulfillment.API, sem erros nem avisos.
2. **Given** uma referência proibida (por exemplo, Domain → Infrastructure), **When** os
   testes rodam, **Then** o teste de arquitetura falha e aponta a referência.
3. **Given** a solução atual, **When** os testes rodam, **Then** todos os testes do REQ-01
   continuam passando, sem exigir Docker.

---

### Edge Cases

- Porta local já ocupada (1433, 5672 ou 15672) → a documentação explica como trocar a porta
  sem editar arquivos versionados.
- Docker não instalado ou parado → o comando de subida falha com a mensagem do próprio
  Docker, e a documentação lista o pré-requisito.
- A API é executada em Development sem as dependências no ar → ela inicia normalmente, e
  só as operações que precisam de banco ou broker falham (nos próximos requisitos).
- Primeira subida mais lenta (download de imagens e inicialização do banco) → o
  desenvolvedor sabe quando está pronto pelo estado de saúde, e não por tempo fixo.
- Senha de desenvolvimento que não atende à política mínima do banco → o banco não sobe; a
  senha versionada já atende à política.

## Requirements *(mandatory)*

### Functional Requirements

**Estrutura da solução**

- **FR-001**: A solução MUST conter os projetos PedidoFulfillment.Domain,
  PedidoFulfillment.Application, PedidoFulfillment.Infrastructure e PedidoFulfillment.API.
- **FR-002**: A direção de dependências MUST ser: Domain não depende de nenhum projeto;
  Application depende só de Domain; Infrastructure depende de Application e Domain; API
  depende de Application, Infrastructure e Domain.
- **FR-003**: Um teste automatizado MUST falhar quando qualquer projeto violar a direção do
  FR-002.
- **FR-004**: O projeto Infrastructure MUST expor um ponto único de registro dos seus
  serviços, chamado pela API na inicialização, para que REQ-04 e REQ-06 acrescentem
  persistência e mensageria sem alterar a composição da API.

**Ambiente local**

- **FR-005**: O repositório MUST conter uma definição de ambiente local que suba, com um
  único comando, o banco de dados relacional SQL Server e o broker de mensagens RabbitMQ com
  o painel de administração na porta 15672.
- **FR-006**: Cada dependência MUST ter verificação de saúde, para que o comando de subida
  possa aguardar até ambas estarem prontas.
- **FR-007**: Os dados do banco e do broker MUST persistir entre reinícios e MUST poder ser
  apagados com um comando documentado.
- **FR-008**: As versões das imagens das dependências MUST ser fixadas (sem `latest`).
- **FR-009**: Portas e credenciais de desenvolvimento MUST poder ser trocadas por variáveis
  de ambiente, sem editar arquivos versionados.

**Configuração da API**

- **FR-010**: A API MUST ler a string de conexão do banco em `ConnectionStrings:SqlServer` e
  as configurações do broker na seção `RabbitMQ` (servidor, porta, usuário, senha e virtual
  host).
- **FR-011**: Os valores de desenvolvimento MUST estar em `appsettings.Development.json` e
  MUST ser marcados como exclusivos de desenvolvimento. Nenhum arquivo versionado MUST
  conter credenciais de outros ambientes.
- **FR-012**: Fora de Development, a ausência da string de conexão ou da configuração do
  broker MUST impedir a API de iniciar, com uma mensagem que nomeia a configuração ausente e
  não expõe valores.
- **FR-013**: Variáveis de ambiente MUST prevalecer sobre os arquivos de configuração.

**Saúde da API**

- **FR-015**: A API MUST expor `/health` (vida), que responde 200 enquanto o processo
  estiver de pé, sem consultar dependências.
- **FR-016**: A API MUST expor `/health/pronto` (prontidão), que verifica a conexão com o
  banco e com o broker e responde 200 quando ambos estão saudáveis e 503 quando algum
  falha. A resposta indica o estado de cada dependência pelo nome (`sqlserver`, `rabbitmq`)
  e não expõe strings de conexão, credenciais nem mensagens de exceção.
- **FR-017**: Os dois endereços de saúde MUST ser públicos (sem token), como permite o
  Princípio III, e MUST ficar fora do documento OpenAPI do contrato v1.
- **FR-018**: Cada verificação de prontidão MUST terminar em no máximo 5 segundos, para que
  uma dependência travada não deixe o endereço sem resposta.

**Documentação**

- **FR-014**: O README MUST explicar pré-requisitos, o comando de subida, como saber se está
  pronto, o endereço do painel do broker, as credenciais de desenvolvimento, como trocar
  portas e como apagar os dados locais.

### Key Entities *(include if feature involves data)*

- **Dependência local**: serviço de apoio executado na máquina do desenvolvedor (banco de
  dados ou broker), com porta, credenciais de desenvolvimento, volume de dados e estado de
  saúde.
- **Configuração de conexão**: endereço e credenciais que a API usa para acessar cada
  dependência, com origem por ambiente (arquivo de desenvolvimento ou variável de ambiente).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um desenvolvedor novo, só com Docker e o SDK instalados, sobe as dependências
  e roda a API em menos de 10 minutos seguindo apenas o README, sem contar o download das
  imagens.
- **SC-002**: Com as imagens já baixadas, o comando de subida deixa as duas dependências
  saudáveis em menos de 2 minutos.
- **SC-003**: 100% das violações de direção de dependência entre camadas são detectadas pelo
  teste de arquitetura.
- **SC-004**: Zero credenciais de ambientes que não sejam desenvolvimento nos arquivos
  versionados.
- **SC-005**: A suíte de testes existente continua 100% verde e roda sem Docker.
- **SC-006**: Com dependências no ar, o desenvolvedor confirma que o ambiente está correto em uma única consulta ao endereço de prontidão.

## Assumptions

- O desenvolvedor tem o Docker com suporte a Compose v2 instalado. Docker é pré-requisito
  documentado, não algo que este requisito instala.
- As credenciais de desenvolvimento (senha do banco, usuário e senha do broker) não são
  segredos reais: valem só para contêineres locais. Por isso podem ser versionadas, desde
  que identificadas como exclusivas de desenvolvimento.
- O banco de dados da aplicação e as migrations são criados no REQ-04. Aqui o banco sobe
  vazio, apenas com o servidor pronto.
- Exchanges, filas e políticas de DLQ são criadas no REQ-06. Aqui o broker sobe com o
  virtual host padrão.
- Pipeline de CI/CD e ambientes de homologação ou produção estão fora do escopo.
- A API roda pelo SDK na máquina do desenvolvedor. Só as dependências ficam em contêineres.
  O Dockerfile da API fica para um requisito futuro, junto com o CI/CD.
- Os endereços de saúde são operacionais, não fazem parte do contrato de negócio v1 e não
  aparecem no documento OpenAPI (o REQ-01 exige exatamente 5 operações no contrato).
