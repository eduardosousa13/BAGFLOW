# BAGFLOW — instruções únicas para Codex e Claude Code

> **Ler primeiro:** `docs/START_HERE.md`, `docs/PROJECT_SPEC.md`, `docs/CONTRACTS.md`, `docs/MATH_MODELS.md`, `docs/TASKS.md`, `docs/UNITY_HANDOFF.md`, `docs/DEFINITION_OF_DONE.md` e `docs/VALIDATION_PLAN.md`. Este é o **único ficheiro de instruções para IA**: não criar instruções divergentes para cada assistente.

## Contexto e realidade do repositório
- Projeto académico de Processamento de Informação, **BAGFLOW**, aprovado pelo professor.
- Repositório: `https://github.com/eduardosousa13/BAGFLOW`; branch principal `main`.
- Projeto Unity **já criado** na raiz do repo: Unity Editor **6000.6.7f1**, template **Universal 2D / URP**; cena principal `Assets/Scenes/MainSimulation.unity`.
- O coordenador é o proprietário da `main`, dos contratos e da cena principal.
- Demonstração no Unity Editor; sem cronograma obrigatório; cinco membros com agentes Codex/Claude Code.
- Avaliação prática: mostrar simulador, explicar a matemática e o local do código. O mini-relatório é um apoio, não uma entrega confirmada.
- Cinco VAs aprovadas: Triangular (transporte), Exponencial (inspeção), Normal truncada (peso), Binomial (sinalizações) e Poisson (ocorrências de bloqueio).

## Regras de execução obrigatórias
1. **Antes de editar, declarar o módulo e a branch**, ler a especificação e os contratos. Nunca redefinir as cinco VAs ou parâmetros acordados sem autorização do coordenador.
2. Trabalhar **somente** nos caminhos atribuídos em `docs/TASKS.md`. Se o contrato for insuficiente, propor alteração ao coordenador antes de implementar.
3. Só o coordenador edita `Assets/Scenes/MainSimulation.unity` e `Assets/Scripts/Contracts/SimulationContracts.cs`.
4. O **motor lógico** é a única fonte de verdade para estados, tempo e eventos. A UI não gera números aleatórios, nem decide resultados de inspeção.
5. **Binomial** = soma dos sorteios individuais Bernoulli realizados nas inspeções, sem segundo sorteio. **Poisson** = contagens em janelas fixas de um processo de chegadas de avarias com intervalos exponenciais, sem sorteio redundante.
6. **Amostras brutas** das distribuições têm de ser preservadas para validação. Nunca alterar distribuição com arredondamentos/clamps arbitrários. Timestamps e duração efetiva podem ser discretizados pelo motor.
7. O código de domínio não depende de `UnityEngine`; usar namespace `AirportBaggageSimulator`, `double` e segundos para tempo, quilogramas para peso. Importações externas: mínimas e justificadas.
8. **Não alterar Unity Editor, URP, `Packages/manifest.json`, `.gitignore` ou `ProjectSettings`** sem autorização. Nunca versionar `Library`, `Temp`, `Logs`, `Obj`, `UserSettings` ou secrets.
9. Unity é a plataforma de execução; testes matemáticos independentes em `validation/` são referência, **não substituem testes C#/Unity**.
10. Git: branch do membro, commits pequenos e pull request; não fazer push direto para `main` por membros não coordenadores. Reportar ficheiros alterados, testes efetivamente executados, limitações e modo de validar.
11. Nunca implementar 3D, passageiros, múltiplos scanners, base de dados, backend, networking ou física realista das malas. O objetivo é o mínimo académico bem fundamentado.
12. **Não afirmar que um teste passou sem o executar.** Se o agente não tiver acesso ao Unity Editor, marcar explicitamente `não testado no Editor`.

## Como usar este ficheiro nas IAs
- **Codex**: ler `AGENTS.md` na raiz antes de executar tarefas.
- **Claude Code**: começar a sessão com o pedido explícito: «Lê e aplica `AGENTS.md` e `docs/START_HERE.md` como instruções do projeto; não cries um `CLAUDE.md` adicional».
- Ambos devem ler os documentos do professor disponíveis ao grupo, mas sem substituir decisões da especificação por exemplos de anos anteriores.

## Primeira resposta esperada de cada agente
Antes de escrever código, apresentar: (a) módulo/branch e caminhos que pode editar; (b) contrato de entrada/saída; (c) dependências; (d) plano de teste. Depois executar a tarefa, apenas nos ficheiros permitidos.

## Contrato UI e reinício
- Para os componentes UI e integração, usar apenas as interfaces presentes em `SimulationContracts.cs` e a especificação `docs/UNITY_HANDOFF.md`; usar progresso e posição FIFO calculados pelo motor.
- **Reset reconstroi as streams** com a seed original; a UI nunca modifica registos originais recebidos em snapshots.
- Todos os PRs seguem `docs/DEFINITION_OF_DONE.md`.
