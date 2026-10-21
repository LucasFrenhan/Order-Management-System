# Contrato: configuração da autenticação (REQ-03)

Chaves novas lidas pela API. Valem as regras do REQ-02: variáveis de ambiente prevalecem
sobre os arquivos, e só valores de desenvolvimento podem estar versionados.

## Banco:PrepararNaInicializacao (bool)

| Regra | Valor |
|-------|-------|
| Padrão | `true` |
| `true` em Development ou `Testes` | aplica as migrations pendentes (cria o banco se preciso), cria perfis e usuários iniciais |
| `true` nos demais ambientes | não altera o esquema; recusa-se a iniciar se houver migration pendente; cria perfis e usuários iniciais |
| `false` | não consulta o banco ao iniciar (comportamento do REQ-02) |
| Sobrescrita | `Banco__PrepararNaInicializacao` |

Mensagens de falha ao iniciar, sem dados de conexão:

- "Não foi possível acessar o banco de dados (ConnectionStrings:SqlServer) para preparar a
  inicialização."
- "O banco de dados tem migrations pendentes. Aplique-as com o comando documentado antes de
  iniciar a API."

## UsuariosIniciais (lista)

| Campo | Obrigatório | Regra |
|-------|-------------|-------|
| `Nome` | sim | 2 a 100 caracteres sem contar espaços nas pontas |
| `Email` | sim | formato válido, até 256 caracteres |
| `Senha` | sim | regra do contrato v1: 8 a 128 caracteres, com maiúscula, minúscula e dígito |
| `Perfil` | sim | `Admin` ou `OperadorLogistica` |

- Lista vazia ou ausente: nenhum usuário inicial é criado.
- E-mail já cadastrado: o usuário existente não é alterado.
- Valor inválido: a API não inicia; a mensagem cita a chave, por exemplo "A configuração
  UsuariosIniciais:0:Senha é inválida: ...", sem mostrar o valor.
- Sobrescrita: `UsuariosIniciais__0__Email`, `UsuariosIniciais__0__Senha` etc.

Valores em `appsettings.Development.json` (SOMENTE DESENVOLVIMENTO):

| Nome | Email | Senha | Perfil |
|------|-------|-------|--------|
| Administrador Dev | `admin@oms.local` | `AdminDev2026` | Admin |
| Operador Dev | `operador@oms.local` | `OperadorDev2026` | OperadorLogistica |

## Chaves existentes reaproveitadas

- `ConnectionStrings:SqlServer` (REQ-02): agora também usada pelo `ContextoBancoDados`.
- `Jwt:Emissor`, `Jwt:Audiencia`, `Jwt:ChaveAssinatura`, `Jwt:DuracaoTokenMinutos` (REQ-01):
  agora também usadas na emissão.
