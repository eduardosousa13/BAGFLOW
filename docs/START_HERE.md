# START HERE — BAGFLOW, base técnica partilhada

**Estado confirmado:** projeto e cinco distribuições aprovados pelo docente; Unity Universal 2D/URP criado no repositório; Editor 6000.6.7f1; cena principal `Assets/Scenes/MainSimulation.unity`; primeiro push realizado pelo coordenador. O código de simulação ainda não foi desenvolvido. Nunca afirmar que o Unity já compila o novo `SimulationContracts.cs` sem verificar no Editor após a importação.

## Fontes de verdade e ordem de leitura
1. `AGENTS.md` — regras únicas para Codex e Claude Code.
2. `docs/PROJECT_SPEC.md` — funcionamento e âmbito do sistema.
3. `docs/CONTRACTS.md` e `Assets/Scripts/Contracts/SimulationContracts.cs` — tipos partilhados.
4. `docs/MATH_MODELS.md` — matemática e validação das cinco VAs.
5. `docs/TASKS.md` — tarefas, caminhos permitidos, testes e instruções por membro.
6. `docs/UNITY_HANDOFF.md` e `docs/DEFINITION_OF_DONE.md` — contrato UI e checklist de aceitação.
7. `docs/VALIDATION_PLAN.md` e `docs/DATA_SOURCES.md` — evidências e rastreabilidade.

## Preparação do coordenador (antes de distribuir tarefas)
1. Fazer backup/confirmar `main` limpa e `git push` concluído.
2. Extrair a pasta deste pacote **para a raiz** de `~/BAGFLOW` sem criar subpasta adicional, preservando `Assets`, `Packages`, `ProjectSettings` existentes. O pacote NÃO contém nem substitui `Packages`, `ProjectSettings` ou `.gitignore`.
3. Abrir `Assets/Scenes/MainSimulation.unity`, esperar pela importação dos `.meta` do Unity e confirmar **Console sem erros de C#**.
4. Verificar `git status`, `git diff --stat` e que não há `Library/` ou outros caches rastreados; commitar e dar push à base única.
5. Dar acesso aos quatro colegas no GitHub; confirmar que conseguem clonar e abrir com **Unity 6000.6.7f1**. Macs devem instalar build compatível com arquitetura; Windows com build Windows da mesma versão.
6. Atribuir M2/M3/M4/M5 em `docs/TASKS.md`. Coordenador integra, mantém `main` utilizável e é dono da cena principal.

## Ordem de implementação, não calendário
**G0**: contratos fixos e ambiente compatível → **G1**: geradores matemáticos + motor + visuais + validação paralelos → **G2**: integração de uma mala → **G3**: todas as funcionalidades essenciais/recomendadas → **G4**: testes de distribuições e sistema → **G5**: mini-relatório e ensaio da apresentação.

Não avançar sobre módulos com interfaces em conflito. Não acrescentar funcionalidades por iniciativa dos agentes.

## Contexto académico
Documentos do professor: Aula 0, Aula 1, Aula 2, Formulario-PI, dois textos sobre geração de VAs, notebook PickEm, exemplo Wild West. Aulas 1/2 suportam FMP/FDPC/Binomial/Poisson e condições de validade das FDP; para triangular e normal truncada, usar também os materiais gerais e bibliografia matemática adequada. Não inventar dados operacionais.

## Avisos práticos v3
- O pacote chama-se `BAGFLOW_GUIDE.md`, não `README.md`, para não sobrescrever um README do repositório. Não inclui `.gitignore` nem caches.
- Só M1 cria/altera MainSimulation.unity; M4 entrega prefabs e cena visual isolada.
- A confirmação de compilação no Unity só pode ser feita num Editor real.
