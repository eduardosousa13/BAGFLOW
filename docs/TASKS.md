# TASKS — divisão paralela SEM TIMELINES

## Sequência de dependências (não cronograma)
G0 — Unity + Git estruturados e contratos congelados → G1 — matemática/geradores + motor/visuais/validação em paralelo → G2 — integração mínima completa → G3 — bloqueios/modos/configuração/exportação → G4 — validação e cenário final → G5 — mini-relatório e apresentação.

## M1 — Coordenador técnico / integração (dono da main e da cena)
**Antes de atribuir código:** Unity **já criado** com template **Universal 2D**, Editor **6000.6.7f1**, repositório `eduardosousa13/BAGFLOW` ligado e cena `Assets/Scenes/MainSimulation.unity` guardada. Importar o pacote-base, confirmar `.gitignore` e `.meta`, testar `SimulationContracts.cs` no Editor, commit/push da base, adicionar colaboradores. Confirmar que todos usam a versão exata do Unity apropriada ao seu sistema operativo.
**Implementar:** `SimulationController` conecta config, engine, visual, UI; montar cena a partir de prefabs entregues pelo M4; unir PRs e executar smoke tests.
**Aceite:** outro membro faz clone, abre Unity, corre cena e vê N malas processadas; `main` sem erros de compilação.

## M2 — Matemática / Probabilidade
**Ler:** `MATH_MODELS.md`, docs professor, `SimulationContracts.cs`.
**Implementar:** fábrica de streams pseudoaleatórias determinística, amostradores para triangular, exponencial, normal truncada, Bernoulli. Poisson por agendamento de intervalos exponenciais (motor usa sampler de bloqueio); geradores não dependem de Unity.
**Testes:** limites, σ>0, λ>0, casos extremos, médias/variâncias amostrais, seed, nunca NaN/Infinity. Exportar funções ou lotes para validação sem Unity.
**Aceite:** interfaces compilaram; samples coerentes em 10000+ testes; documentação identifica o código de cada variável.

## M3 — Motor lógico
**Ler:** `PROJECT_SPEC.md`, `CONTRACTS.md`, `MATH_MODELS.md`.
**Implementar:** estados por mala, configurações, progresso visual `TransportProgress01/ScanProgress01`, `QueuePosition` no snapshot, chegadas 2 modos, tapete nominal com pausas, FIFO do scanner, decisão Bernoulli, eventos Poisson e tempo fixo de paragem, snapshots, término.
**Limites:** UnityEngine proibido no motor; não implementar gráficos; usar provider de RNG injetado e contratos fixos.
**Teste:** 1 mala; 0 bloqueios; p=0, p=1; n=500; chegada d muito curta; com/sem bloqueios; seed repetida; quantização temporal.
**Aceite:** simulação headless completa com invariantes preservados; evento emitido uma única vez.

## M4 — Visual/UI
**Ler:** `PROJECT_SPEC.md`, `CONTRACTS.md`, `UNITY_HANDOFF.md`.
**Implementar:** as interfaces `ISimulationWorldView`, `ISimulationDashboardView`, `ISimulationControlView` (com métodos e eventos exatos), sprites simples e consistentes, tapete, scanner, vias de saída, cor/identificador da mala, efeitos de bloqueio, painel inicial, métricas, botões Start/Pause/Reset/Speed, prefabs testáveis.
**Não fazer:** simular física, sortear variáveis, decidir conclusão, editar MainSimulation.unity. **Criar apenas prefabs e uma `VisualSandbox.unity` própria para testes.**
**Aceite:** componentes podem receber snapshots/eventos artificiais sem conhecer engine; layouts legíveis no ecrã de apresentação.

## M5 — Validação + fontes + export
**Ler:** `MATH_MODELS.md`, `CONTRACTS.md`, notebook PickEm do professor.
**Implementar:** `CsvExporter` com cabeçalhos aprovados, notebook Python (matplotlib/scipy/numpy) que lê CSV; verifica FDP/FMP, FDPC, médias, variâncias, exceções, Binomial em lotes de N e Poisson em janelas fixas; pesquisar fontes e registar evidência por parâmetro.
**Aceite:** notebook lê export real, gráficos são reproduzíveis e identificam pressupostos; testes funcionais e modelos documentados.

## Prompts de arranque para Codex/Claude Code
**Prompt comum para Codex ou Claude Code (colocar antes do específico):**
> Estás num repositório Unity 2D de uma UC de Processamento de Informação. Lê primeiro AGENTS.md, docs/START_HERE.md, docs/PROJECT_SPEC.md, docs/CONTRACTS.md, docs/MATH_MODELS.md, docs/TASKS.md. Respeita literalmente as interfaces e a propriedade dos ficheiros. Faz apenas o módulo que te foi atribuído. Não edites a cena MainSimulation.unity salvo se fores o coordenador. Não inventes dados reais, não simplifiques substituindo as 5 distribuições e não sorteies nada fora do motor. Testa, apresenta ficheiros alterados e resultados, e não faças merge sem autorização.

**M2 acrescenta:**
> Implementa apenas a camada Probability e os respetivos testes, sem UnityEngine; inclui sampler exponencial, triangular, normal truncada Box–Muller+rejeição, Bernoulli e fábrica de streams; documenta fórmulas e casos-limite.

**M3 acrescenta:**
> Implementa apenas SimulationEngine e os testes de estados; chegada fixa/aleatória, tapete interrompível, fila FIFO, scanner único, triagem por Bernoulli, ocorrências Poisson e paragem fixa. Usa o contrato partilhado. A UI apenas reproduzirá eventos.

**M4 acrescenta:**
> Cria apenas prefabs/Assets/Sprites/Assets/Scripts/Presentation e UI, com layout top-down e dashboard. Usa dados fictícios numa cena de teste própria para testar visualmente; não edites MainSimulation.unity. Sem RNG ou lógica de simulação na UI.

**M5 acrescenta:**
> Cria exportador CSV e notebook independente, com validação bruta de amostras e validação do motor, cenários, histogramas e fontes. Escreve relatórios de discrepâncias, não modifiques distribuições para ajustar histogramas.

**M1 acrescenta:**
> Faz o bootstrap do projeto Unity e contratos; gere MainSimulation.unity e integra progressivamente branches em main; liga as interfaces UI definidas em `UNITY_HANDOFF.md` e recria as streams no Reset; mantém compilação limpa e rastreabilidade entre modelos matemáticos e UI. Não implemente sozinho todos os módulos que já foram atribuídos.

**Revisão obrigatória antes de abrir PR:** consultar `docs/DEFINITION_OF_DONE.md`. A responsabilidade M4 e a integração M1 ficam descritas em `docs/UNITY_HANDOFF.md`.
