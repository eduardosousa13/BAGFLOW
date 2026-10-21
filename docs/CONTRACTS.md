# CONTRACTS — interfaces e fronteiras entre módulos

## Regras globais
- Usar `AirportBaggageSimulator` como namespace C# em todos os scripts do projeto.
- `Assets/Scripts/Contracts/SimulationContracts.cs` é a fonte oficial dos tipos; não duplicar enums/configurações/records.
- As classes do motor, de geração e exportação **não podem depender de** `UnityEngine` (exceto adaptadores na camada Presentation/UI).
- Só motor sorteia resultados via geradores; UI/dashboard/exportação **nunca sorteiam** para reproduzir dados.
- Seed principal, streams separadas por domínio (`Arrival, Transport, Weight, Inspection, Flagging, Blockage`).
- Valores temporais e pesos em `double`, tempo simulado em segundos; UI pode converter para float apenas ao animar.
- Alterações a este documento ou contratos só via PR aprovado pelo coordenador, anunciado a todos.

## Entradas e saídas obrigatórias
`SimulationConfig` contém todos os parâmetros, modo de chegada, seed, velocidade e passo lógico.
`BagRecord` contém estado, identificador, amostras brutas e instantes de eventos.
`SimulationEventRecord` contém instante, tipo, ID opcional e detalhe.
`SimulationMetrics`/`SimulationSnapshot` contêm estado agregado para UI e export.
`IDistributionSampler` fornece `Triangular`, `Exponential`, `TruncatedNormal`, `Bernoulli`.
`ISimulationEngine` fornece `Initialize`, `Step`, `GetSnapshot`, `GetBagRecords`, `GetEventRecords`, `IsComplete`.
`ISimulationWorldView`, `ISimulationDashboardView` e `ISimulationControlView` definem os métodos e eventos consumidos pelo `SimulationController`; ver `UNITY_HANDOFF.md`.

## Contrato do motor temporal
`Step()` avança Δt exatamente uma vez quando em execução. `SimulationController` acumula tempo visual*velocidade e chama `Step()` o número inteiro de ticks devido. `Pause` suspende chamadas; `Reset` deve recriar o `IRandomStreamProvider` com a seed inicial e voltar a chamar `Initialize(config, newProvider)`. Nunca reutilizar streams já consumidas.

**Importante:** validar a distribuição exponencial, triangular e normal nas **amostras brutas**, pois os timestamps operacionais são quantizados ao tick. Não prometer reprodução bit a bit entre diferentes versões de runtime se usar `System.Random`; exigir mesma versão de Editor/runtime para testes de seeds. Não usar `GetHashCode()` para derivar seeds; derivar de constantes fixas.

## Enums e eventos sugeridos
`BagState: Scheduled, WaitingEntry, Transporting, ScannerQueue, Scanning, Approved, Flagged`.
`BagRecord` expõe `TransportProgress01`, `ScanProgress01` e `QueuePosition` (0-baseado na fila; -1 fora da fila); `SimulationSnapshot.Bags` deve conter **cópias separadas** do estado interno, para a UI não alterar resultados.
`SimulationEventType: BagArrived, TransportStarted, TransportFinished, ScanQueued, ScanStarted, ScanFinished, BagApproved, BagFlagged, BlockageStarted, BlockageOccurrence, BlockageEnded, RunCompleted`.
Os eventos de bloqueio diferenciam ocorrência estatística e mudança de estado físico. Exatamente uma ocorrência estatística por evento Poisson.

## Ordenação de tick e casos de empate
- No mesmo tick processar eventos de ocorrência / chegada por instante agendado crescente, depois ID para desempates.
- Atualizar progresso de transporte e scanner com passo fixo.
- Processar transporte concluído, depois scanner concluído, atualizar fila, permitir próximo scan.
- Nunca contar uma bagagem mais do que uma vez; valores quase zero completos no próximo tick se necessário.
- Definir explicitamente contador/limite para detectar loops, N inválido ou bloqueios com taxa extrema.

## Propriedade de ficheiros
- Coordenador: `Assets/Scenes/MainSimulation.unity`, integração `SimulationController`, organização do projeto Unity.
- Probabilidade: `Assets/Scripts/Probability/**`, testes matemáticos associados.
- Motor: `Assets/Scripts/Simulation/**`, teste de estados e FIFO.
- Visuais: `Assets/Scripts/Presentation/**`, `Assets/Scripts/UI/**`, `Assets/Prefabs/**`, `Assets/Sprites/**`; **sem editar cena principal**.
- Validação: `Assets/Scripts/Export/**`, `validation/**`, `docs/DATA_SOURCES.md`, `docs/VALIDATION_PLAN.md`.
- `Assets/Scripts/Contracts/**` pertence ao coordenador; alterações só via PR específico.

## Convenções de CSV
`bags.csv`: id,weight_kg,nominal_transport_s,observed_transport_s,inspection_s,arrival_s,queue_enter_s,scan_start_s,complete_s,queue_wait_s,total_s,flagged
`events.csv`: time_s,event_type,bag_id,detail,scheduled_time_s
`config.json`: todos os parâmetros, versão do app, algoritmo PRNG e seed.
Separador vírgula, ponto decimal (InvariantCulture), CSV UTF-8, escape de campos de texto.

## Integração
- Branches: `feature/probability`, `feature/engine`, `feature/visual-ui`, `feature/validation`.
- Cada PR inclui o que alterou, testes efetuados, screenshots (se visual), incompatibilidades e ficheiros afetados.
- Nunca fazer push direto para main por outros membros; sem commits de Library, Temp, build, caches, soluções IDE.
- Antes de merge: projeto abre, compila, testes passando, sem logs de erro críticos.

## Coordenação UI/motor
Ler obrigatoriamente `docs/UNITY_HANDOFF.md` antes de criar scripts UI ou o `SimulationController`. Seguir os nomes/métodos de interfaces existentes em `SimulationContracts.cs`. O coordenador é o único dono da cena principal; M4 fornece prefabs e pode testar em cena isolada.
