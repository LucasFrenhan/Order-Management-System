# Análise estática

[← Voltar ao README](../README.md)

O código é analisado pelo SonarQube Cloud em todo PR e no push da `main`:
[painel do projeto](https://sonarcloud.io/project/overview?id=LucasFrenhan_Order-Management-System)
(organização `lucasfrenhan`, projeto `LucasFrenhan_Order-Management-System`; não são
segredos). A credencial de análise é o segredo `SONAR_TOKEN` do repositório e só chega aos
passos do scanner.

- **Onde ver**: no PR, a verificação do SonarQube Cloud e o comentário com o Quality Gate,
  os problemas novos, a cobertura e a duplicação do código novo, com link para o detalhe.
- **Quality Gate** ("Sonar way", só sobre o código novo): nenhum bug nem vulnerabilidade
  novos, todos os pontos de segurança (security hotspots) revisados, nota A de
  manutenibilidade, cobertura de pelo menos 80% e no máximo 3% de duplicação.
- **Quality Gate reprovado**: abra o detalhe pelo comentário do PR e corrija o problema no
  mesmo PR (para cobertura, acrescente testes ao código novo). Um falso positivo, ou um ponto
  de segurança que é seguro (como uma credencial só de desenvolvimento), é marcado no painel
  com a justificativa, sem desligar a regra.
- **Fora da análise**: as migrations, o código gerado, `specs/` e `.specify/`; os projetos de
  `tests/` contam como teste. A cobertura soma os testes unitários e os de integração.
- **No editor** (opcional): o SonarQube for IDE, em modo conectado ao projeto, mostra os
  mesmos problemas antes do push.

**Pull requests de forks não são aceitos.** O GitHub não permite desativá-los num repositório
público, e eles não poderiam ser mergeados (a análise não roda sem acesso ao segredo, e a
`main` a exige). O workflow [`prs-de-fork`](../.github/workflows/prs-de-fork.yml) comenta a
política e fecha esses PRs, sem fazer checkout do código deles. Para sugerir uma mudança,
abra uma issue.
