# BAGFLOW — pacote-base técnico v3

Este pacote fornece **documentação de projeto**, **regras para IA**, **contratos C#** e **testes matemáticos de referência em Python**. Não é um simulador completo. **O projeto Unity já foi criado pelo coordenador** e este pacote acrescenta apenas a documentação, contratos C# e referências matemáticas.

## Instalação na base existente
1. Abrir o repositório existente `~/BAGFLOW` (Unity Universal 2D, Editor 6000.6.7f1; `Assets`, `Packages` e `ProjectSettings` já existentes).
2. Confirmar que o projeto abre e que `Assets/Scenes/MainSimulation.unity` está guardada.
3. Copiar o conteúdo deste ZIP para a **raiz** do repositório (sem criar pasta intermédia). O ZIP não contém README.md para não sobrescrever o README existente do repo.
4. Manter o `.gitignore` já existente no repo e verificar que os caches não são versionados (não substituir por outro sem necessidade).
5. Abrir Unity para permitir a criação dos `.meta` dos ficheiros importados, confirmar que `SimulationContracts.cs` compila.
6. `git status` para inspecionar; commit/push da base. Convidar os restantes membros.
7. Cada IA lê o **único** `AGENTS.md` e os documentos em `docs/`; depois recebe a tarefa da sua área. Para Claude Code, pedir explicitamente a leitura de `AGENTS.md` ao iniciar a sessão.

## Testes já efetuados fora do Unity
`python -m unittest discover -s validation -p 'test_reference.py' -v`

Foram executados cinco testes de referência (triangular, exponencial, normal truncada, binomial e Poisson), todos com sucesso, **em Python**. Os testes de integração C# / Unity continuam por realizar.

## Documentos mais importantes
- `AGENTS.md`: instruções comuns dos dois agentes (não há `CLAUDE.md`).
- `docs/START_HERE.md`: arranque e prioridade.
- `docs/PROJECT_SPEC.md`: funcionamento e âmbito aprovados internamente.
- `docs/MATH_MODELS.md`: fórmulas e validade das distribuições.
- `docs/CONTRACTS.md`: interfaces e ownership para codificação por IA.
- `docs/UNITY_HANDOFF.md`: API exata que liga motor a vista e controlos, incluindo progresso visual e eventos.
- `docs/DEFINITION_OF_DONE.md`: checklist por módulo, evidência de testes, entrega e demo.
- `docs/TASKS.md`: tarefas dos cinco membros e prompts específicos para Codex/Claude Code.
- `docs/VALIDATION_PLAN.md`: critérios de ensaio.
- `docs/DATA_SOURCES.md`: pressupostos e registo de evidências.

A matemática é uma proposta verificada nas fórmulas de referência; os parâmetros são **didáticos** e não foram calibrados com dados empíricos de aeroportos.

## Limites de garantia
Este ZIP não contém o Unity Editor, o projeto Unity existente, os PDFs do professor, prefabs ou o motor já implementado. Os contratos C# foram revistos como código-fonte, mas a sua compilação real no Unity 6000.6.7f1 tem de ser verificada pelo coordenador após importar. Os testes Python apenas verificam o comportamento das implementações de referência.
