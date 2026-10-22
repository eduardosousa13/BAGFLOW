# M3 — motor lógico BAGFLOW

`SimulationEngine : ISimulationEngine` é C# puro, namespace `AirportBaggageSimulator`, segundos/kg em `double`. O assembly `BAGFLOW.Simulation` referencia apenas `BAGFLOW.Contracts`, com `noEngineReferences=true`. Não depende de Probability, Integration ou UnityEngine em execução.

## Entrada, utilização e reinício

Recebe `SimulationConfig` e `IRandomStreamProvider` por `Initialize`. Valida todos os valores finitos e limites da especificação, e copia a configuração antes de a usar. `EngineConfiguration` valida a fronteira do domínio independentemente da validação da UI; não implementa algoritmos probabilísticos.

```csharp
ISimulationEngine engine = new SimulationEngine();
engine.Initialize(config, streams); // Provider criado pelo coordenador para config.Seed.
while (!engine.IsComplete) engine.Step();
SimulationSnapshot state = engine.GetSnapshot();
```

M1 deve injetar a instância com `SimulationController.Bind`. Não foi alterado o controlador nem a cena nesta entrega M3. A pausa consiste em não chamar `Step`. Playback não interfere no motor: cada `Step` executa exatamente um passo lógico. Após conclusão, `Step` não faz nada e não consome streams nem emite outro evento final. Reset = `Initialize(config, NOVO provider com a mesma seed)`. O motor não consegue reconstruir um provider arbitrário recebido por interface; essa responsabilidade continua em M1.

## Sorteios e amostras brutas

- Na inicialização, cria exatamente N registos com IDs 1..N, estado `Scheduled`, peso da stream `Weight` e transporte nominal da stream `Transport`, uma vez por bagagem.
- Chegada inicial em t=0; seguintes `i*d` no modo Constant ou soma de N-1 intervalos exponenciais da stream `Arrival`. `ArrivalTimeSeconds` guarda o instante sorteado; diferenças consecutivas preservam os intervalos brutos.
- Ao iniciar scanner, sorteia uma exponencial da stream `Inspection`. Ao terminar, faz um único Bernoulli de `Flagging`; Approved/Flagged e a contagem binomial derivam dessa mesma decisão.
- Bloqueios: só com taxa positiva, gera o próximo intervalo exponencial em `Blockage`; agenda sucessivamente instantes estritamente crescentes. Cada ocorrência emite exatamente um `BlockageOccurrence`, incluindo durante uma paragem. Não existe contagem Poisson independente.
- Amostras inválidas do provider produzem erro explícito, sem clamp. Intervalos de chegada/bloqueio devem ser estritamente positivos e as suas somas finitas/crescentes; inspeção zero é permitida e só termina no tick seguinte. Atributos brutos não são substituídos pelas durações quantizadas.

`SimulationEventRecord.ScheduledTimeSeconds` preserva instantes brutos de chegadas/bloqueios e fins matemáticos de paragem; `SimulationTimeSeconds` indica a aplicação no tick. Para janelas Poisson, M5 deve usar os `ScheduledTimeSeconds` de `BlockageOccurrence`, nunca uma duração variável da execução como se fosse uma janela fixa.

## Ordem de um tick e quantização

A inicialização aplica a primeira chegada em t=0. Em cada `Step`:

1. Guarda o estado de tapete/scanner no início do intervalo, calcula t = número de passos × Δt e verifica relógio/horizonte.
2. Aplica chegadas, ocorrências e fins de bloqueio devidos até t, em ordem de instante agendado. Empates: ocorrência, fim de bloqueio, chegada; chegadas por ID.
3. Contabiliza o intervalo acabado com o estado que vigorou no seu início. O bloqueio aplicado agora afeta o intervalo seguinte; não é retroativo. Uma bagagem cujo transporte começa em t só começa a mover-se no próximo intervalo. O mesmo vale para um scanner iniciado em t.
4. Atualiza transporte, incluindo duração observada; bloqueio congela o trabalho nominal, mas não o relógio. Ordena conclusões no mesmo passo pelo instante de cruzamento do limiar nominal e depois ID; emite `TransportFinished` e `ScanQueued`, mantendo os elementos que já estavam na FIFO à frente.
5. Atualiza o scanner que já estava em serviço, termina-o no máximo uma vez e sorteia a decisão única. Um bloqueio do tapete não interrompe esta inspeção.
6. Se o scanner estiver livre, retira o primeiro da fila e inicia uma inspeção. Atualiza `QueuePosition` (0-baseado, -1 fora da fila).
7. Se todas as malas terminaram, emite uma única vez `RunCompleted` e marca conclusão.

Esta contabilização respeita os instantes de aplicação no tick: um `BlockageStarted` em t não pode parar movimento já ocorrido antes de t. Não há integração contínua/física entre ticks. Durações quase zero podem ocupar um tick completo, sem ciclos de conclusão imediata.

Cada ocorrência prolonga a paragem até `max(fimAtual, instanteSorteado + duraçãoFixa)`. Uma chegada durante uma paragem fica em `WaitingEntry`; a retoma liberta esta fila por ordem de chegada. Se início e fim matemático de uma paragem curta caírem no mesmo tick, ambos os eventos são aplicados nesse tick e a ocorrência conta, mas não se inventa um intervalo operacional parado. Duração zero conta ocorrências sem eventos físicos de paragem.

## Snapshots e métricas

`GetSnapshot`, `GetBagRecords` e `GetEventRecords` devolvem cópias independentes; listas são só de leitura, os objetos recebidos nunca são usados pelo motor. Nenhuma consulta consome streams. `TransportProgress01` e `ScanProgress01` estão em [0,1], não diminuem e atingem 1 ao terminar a etapa. O motor é a única origem destes valores e das posições na FIFO.

`Created` conta chegadas aplicadas, não os registos futuros ainda Scheduled. Ao concluir, `Created=Completed=N`, `Completed=Approved+Flagged`, e nenhum estado transitório permanece. Espera média usa as inspeções já iniciadas; tempo total médio usa apenas malas concluídas e `completion-arrival` (incluindo espera por aplicação da chegada no tick). `ScannerUtilization` usa tempo lógico ocupado, incluindo a quantização do último tick. `TotalBlockedSeconds` é a soma de intervalos lógicos parados; sobreposições não somam duas paragens.

## Segurança

Configurações inválidas são rejeitadas antes de usar streams. Há limites explícitos de 10000 eventos de calendário por tick, 1000000 registos de evento e 10000000 passos, além do horizonte configurado. Um horizonte que não seja múltiplo de Δt não permite um passo que o ultrapasse. Overflow, perda de resolução do calendário, amostras inválidas e falhas de execução geram exceção; após falhar um `Step`, é obrigatório reinicializar com streams novas. Não se apresenta a falha como conclusão normal. Snapshots do estado alcançado continuam disponíveis para diagnóstico.

## Testes

Testes M3 em `Assets/Tests/Editor/Simulation/SimulationEngineTests.cs`, assembly Editor próprio `BAGFLOW.SimulationTests`. Streams fictícias existem apenas nos testes de regras. O Probability já está na main de base; execuções completas e reprodução usam os seus geradores reais, sem dependência do motor nesse assembly.

No Unity **6000.6.7f1**, Test Runner > EditMode > Run All. Para batch:

```sh
Unity -batchmode -nographics -projectPath /copia/isolada/BAGFLOW \
  -runTests -testPlatform EditMode \
  -testResults /tmp/bagflow-engine-results.xml -logFile /tmp/bagflow-engine-unity.log
```

Cobertura: N=1/60/500 nos dois modos; IDs/estados/contagens/progresso; FIFO e desempates dentro do tick; bloqueios sobrepostos e curtos; entrada à espera; scanner não interrompido; p=0/1 e uma decisão por mala; preservação de amostras e tempos sorteados; métricas calculadas à mão; snapshots separados; reset com provider novo; seeds e playback; parâmetros/amostras inválidos; horizonte e calendário denso; consultas antes de Initialize e Step após conclusão.

O motor não implementa a UI, exportador, geradores, nem altera contratos/cenas. A ligação final e inspeção gráfica pertencem à integração M1/M4; CSV/JSON e histogramas pertencem a M5. Windows e player distribuível não são inferidos de testes macOS no Editor.

## Evidência executada nesta entrega

22/10/2026, Unity **6000.6.7f1 (b22767f04f04)**, macOS, Mono. O executável real do Editor compilou o código e correu o Test Runner numa cópia isolada do projeto em `/tmp/bagflow-m1m2-unity`, preservando as configurações/caches da cópia de trabalho.

- Comando: `Unity -batchmode -nographics -projectPath /tmp/bagflow-m1m2-unity -runTests -testPlatform EditMode -testResults /tmp/bagflow-engine-results.xml -logFile /tmp/bagflow-engine-unity.log`.
- Exit code **0**: **82 testes passaram, zero falhas/ignorados**. São **34 casos M3** e **48 casos anteriores** de Probability/integração. A bateria inclui dois ensaios existentes que entram realmente em Play Mode e a importação de `MainSimulation` sem scripts em falta.
- N=1/60/500 concluídos nos dois modos de chegada, com os geradores reais. Também concluído o cenário N=500, intervalo de chegada 0.001 s, inspeção média 0.01 s, bloqueios 2/s e duração 1 s, mantendo contagens e scanner único.
- Bloqueios sobrepostos testados com mala ainda Transporting e outra Scanning: transporte congelado, inspeção avança, ocorrências contadas e fila de entrada libertada por ordem.
- Métricas verificadas independentemente num caso de três malas: saídas em 4/7/10 s, espera média 2 s, tempo total médio 6 s, utilização do scanner 0.9.
- Comando independente: `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s validation -p 'test_reference.py' -v`: **7/7 passaram**. Referência Python, não substituição dos testes C#/Unity.
- Logs/XML completos locais: `/tmp/bagflow-engine-unity.log`, `/tmp/bagflow-engine-results.xml` e `/tmp/bagflow-engine-python.log`; não são versionados.

Limitações: execução em `-nographics`, sem demo visual da ligação deste motor aos prefabs/controlos M4, exportação real M5, testes Windows ou build distribuível. A composição concreta com M1 ainda tem de ser ligada pelo coordenador; os contratos e a cena principal não foram alterados.
