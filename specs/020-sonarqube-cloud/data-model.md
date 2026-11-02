# Data Model: análise estática com SonarQube Cloud e Quality Gate no CI

**Requisito**: REQ-20 | **Spec**: [spec.md](./spec.md)

**Nenhuma mudança no domínio, no banco ou nos contratos v1.** Os "dados" deste requisito são
configuração versionada e o estado guardado no SonarQube Cloud e no GitHub:

| Item | Onde | Conteúdo |
|------|------|----------|
| Parâmetros da análise | `.github/workflows/build-testes.yml` | projeto, organização, caminhos de cobertura e de resultados, exclusões ([contrato](./contracts/analise-no-ci.md)) |
| Ferramentas | `.config/dotnet-tools.json` | `dotnet-ef` 10.0.13 e `dotnet-sonarscanner` 11.3.0 |
| Módulos da cobertura | `coverage.runsettings` | Domain, Application, Infrastructure, API, Worker e PagamentosSimulados; sem migrations e `obj/` |
| Proteção da `main` | `.github/rulesets/protecao-main.json` (cópia versionada) e o ruleset 26400051 no GitHub | regras e verificações exigidas |
| Credencial | segredo `SONAR_TOKEN` do repositório | nunca versionada nem logada |
| Análises, Quality Gate e pontos de segurança | SonarQube Cloud | resultados por PR e pela `main`; revisões dos pontos de segurança com a justificativa |
| Situação de partida | [research](./research.md) R-10 | números da primeira análise da `main`, com a data |
