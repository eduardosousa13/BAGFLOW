# M1 + M2 — implementação e ligação

Este relatório descreve a entrega original. A composição real com M3, após os PRs #1/#2, está documentada em `docs/REAL_INTEGRATION.md`.

## Âmbito e ficheiros

- `Assets/Scripts/Probability/DistributionSampler.cs`: `IDistributionSampler`, C# puro.
- `Assets/Scripts/Probability/DeterministicRandomStreamProvider.cs`: `IRandomStreamProvider`, seis streams com estado próprio.
- `Assets/Scripts/Integration/SimulationConfiguration.cs`: validação completa e cópia dos campos escalares de `SimulationConfig`.
- `Assets/Scripts/Integration/SimulationSession.cs`: adaptação de tempo real para chamadas `Step`, pausa, retoma, reset e limites de segurança, sem regras operacionais.
- `Assets/Scripts/Integration/SimulationController.cs`: MonoBehaviour que liga motor e interfaces de apresentação/controlos.
- `Assets/Tests/Editor/Probability/ProbabilityTests.cs`: testes dos amostradores.
- `Assets/Tests/Editor/Integration/SimulationIntegrationTests.cs`: testes da integração com um motor de teste, exclusivamente no assembly de testes.
- `Assets/Scenes/MainSimulation.unity`: novo objeto `BAGFLOW Integration`; câmara, luz e configuração URP preservadas.

Não foram implementados estados/FIFO/scanner (M3), visuais/prefabs/UI (M4), nem CSV/JSON/notebook (M5). O conteúdo de `SimulationContracts.cs` permanece inalterado. Os `.asmdef` isolam contratos e probabilidade sem referências a UnityEngine e permitem testes EditMode; futuros assemblies próprios de M3/M4/M5 devem referenciar `BAGFLOW.Contracts` (e `BAGFLOW.Probability` apenas se necessário). Os scripts nos assemblies predefinidos do Unity recebem estas referências automaticamente.

## Matemática e limites numéricos

As cinco leis, FDP/FMP, FDPC e momentos estão definidos em `docs/MATH_MODELS.md`; os valores por defeito continuam em `SimulationConfig`, sem alterações. São pressupostos didáticos.

`DistributionSampler` implementa:

1. **Triangular**: transformação inversa em dois ramos. A expressão é normalizada por `b-a` para evitar produtos de larguras grandes. Aceita `a=c`, `c=b` e `a=b` (constante); rejeita parâmetros não finitos, ordem inválida ou largura não representável.
2. **Exponencial**: `-log(1-U)/rate`, com taxa finita positiva, sem duração mínima. Amostras não representáveis como `double` positivo originam erro explícito; não há clamp nem arredondamento.
3. **Normal truncada**: Box–Muller `sqrt(-2 log U1) cos(2 pi U2)` seguido da transformação `mu+sigma*z` e rejeição fora de `[L,H]`. Cada tentativa usa duas uniformes novas; não existe cache partilhada. O limite é 100000 tentativas por amostra. Truncamentos de massa extremamente pequena podem falhar explicitamente, conforme a especificação, sem alterar a distribuição dos valores aceites. O teste `[40,41]` verifica essa falha limitada; `[-0.001,0.001]` e `[2,3]` verificam rejeição difícil que ainda é viável. Intervalos além da resolução finita da uniforme/Box–Muller não são prometidos como amostráveis.
4. **Bernoulli**: `U<p`, inclusive `p=0`/`p=1`. O motor chama exatamente uma vez após cada inspeção. A Binomial é a soma desses resultados; não há método que sorteie outra contagem para a simulação.
5. **Poisson**: o motor acumula intervalos `Exponential(lambdaB)` da stream `Blockage`. Não há sorteio separado de contagens. Taxa zero significa que M3 não agenda eventos nem chama `Exponential(0)`. A contagem estatística usa instantes sorteados e janelas fixas, incluindo ocorrências durante paragens.

Para a normal truncada, com `alpha=(L-mu)/sigma`, `beta=(H-mu)/sigma`, `Z=Phi(beta)-Phi(alpha)`:

- FDP no suporte: `phi((w-mu)/sigma)/(sigma*Z)`.
- FDPC no suporte: `(Phi((w-mu)/sigma)-Phi(alpha))/Z`, 0 abaixo e 1 acima.
- Média: `mu+sigma*(phi(alpha)-phi(beta))/Z`.
- Variância: `sigma²*(1+(alpha*phi(alpha)-beta*phi(beta))/Z-((phi(alpha)-phi(beta))/Z)²)`.

Os testes usam momentos destas fórmulas, calculados independentemente com `math.erf` de Python: para `(18,4,5,32)`, média `18.004629209507574` kg e variância `15.845472744238233` kg²; para `(0,1,2,3)`, média `2.3158213267437833` e variância `0.06152077957448565`. Na validação externa de caudas extremas, M5 deve usar funções de sobrevivência/logaritmos numericamente estáveis, evitando subtrair duas CDF próximas de 1.

## Seeds e utilização pelo M3

Algoritmo versionado: `SplitMix64-open52-v1`. Não usa `System.Random`, `UnityEngine.Random` ou `GetHashCode`. A seed `int` é estendida com sinal para 64 bits, reinterpretada como `ulong` e combinada por XOR com uma constante fixa para cada domínio:

| Domínio | Constante hexadecimal |
|---|---|
| Arrival | A0761D6478BD642F |
| Transport | E7037ED1A0B428DB |
| Weight | 8EBC6AF09C88C6E3 |
| Inspection | 589965CC75374CC3 |
| Flagging | 1D8E4E27C47D124F |
| Blockage | EB44ACCAB455D165 |

O estado é incrementado por `9E3779B97F4A7C15`, com mistura SplitMix64 em aritmética módulo 2^64 explícita. Os 52 bits superiores são convertidos por `(bits+0.5)/2^52`, garantindo `2^-53 <= U <= 1-2^-53`. Isto evita extremos logarítmicos sem alterar amostras através de clamps. Streams distintas não consomem estado umas das outras; a independência é a hipótese pseudoaleatória do modelo, não uma garantia de independência matemática de um PRNG finito. Instâncias não são globais nem destinadas a acesso concorrente.

```csharp
IRandomStreamProvider streams = new DeterministicRandomStreamProvider(config.Seed);
double weight = streams.Get(RandomStream.Weight).TruncatedNormal(
    config.WeightBaseMeanKg, config.WeightBaseStdKg, config.MinWeightKg, config.MaxWeightKg);
double transport = streams.Get(RandomStream.Transport).Triangular(
    config.TransportMinSeconds, config.TransportModeSeconds, config.TransportMaxSeconds);
double inspection = streams.Get(RandomStream.Inspection).Exponential(1 / config.MeanInspectionSeconds);
bool flagged = streams.Get(RandomStream.Flagging).Bernoulli(config.FlagProbability);
// Só no modo Exponential; primeira chegada sempre em t=0:
double arrivalGap = streams.Get(RandomStream.Arrival).Exponential(1 / config.MeanArrivalIntervalSeconds);
// Só quando config.BlockageRatePerSecond > 0:
double blockageGap = streams.Get(RandomStream.Blockage).Exponential(config.BlockageRatePerSecond);
```

O exemplo identifica chamadas em momentos diferentes do ciclo de vida, não um sorteio conjunto por frame. M3 preserva os valores brutos e acumula tempos de ocorrência. Se a soma de um intervalo e timestamp deixar de ser finita ou estritamente crescente devido à resolução de `double`, deve emitir erro numérico explícito; não aumentar artificialmente o intervalo. Reiniciar exige **novo provider**, não voltar a obter as streams antigas. A sequência inteira do PRNG é estável; funções transcendentes de `Math` podem variar nos últimos bits entre plataformas, pelo que a reprodução de amostras é exigida no mesmo Editor/runtime/algoritmo.

## Integração Unity disponível

O controlador permite duas formas de composição:

- Componentes MonoBehaviour que implementam as interfaces existentes podem ser atribuídos no Inspector. Interfaces não são serializadas diretamente.
- Um motor C# puro pode ser injetado por `controller.Bind(engine, world, dashboard, controls)` a partir da composição M1, após os módulos existirem. Não exige nomes de classes concretas nem reflexão para descobrir implementações de motor.

A composição explícita com `Bind` tem precedência sobre os campos do Inspector, mesmo quando feita antes de `Awake` num objeto inativo. `Awake` só aplica os campos serializados quando ainda não houve ligação. `OnEnable` subscreve uma única vez; `OnDisable` pausa e remove as subscrições. Ao substituir uma ligação, `Bind` desliga os controlos antigos e atualiza-os para parado.

Para a futura composição M1/M3/M4, usar um único ponto de composição: preferencialmente chamar `Bind` no `Start` do objeto coordenador, depois dos `Awake`/`OnEnable` das vistas, e só então permitir comandos. Em criação dinâmica, pode-se criar o controlador inativo, chamar `Bind` e ativar o objeto após preparar as vistas. Não emitir pedidos automáticos de início a partir de `Awake`/`OnEnable` antes dessa composição; se necessário, o coordenador solicita o início após ligar os componentes. Não é necessário configurar ordem de execução global nem alterar contratos partilhados.

`Advance` reutiliza o snapshot devolvido após cada tick como referência temporal do tick seguinte: N ticks na mesma chamada requerem N+1 snapshots, em vez de 2N. Chamadas sem ticks, em pausa ou após conclusão não pedem snapshots dentro de `Advance`. A reutilização é local à chamada, sem manter entre frames uma referência potencialmente exposta à UI; `Snapshot` continua a pedir uma cópia ao motor para renderização. Mantêm-se as verificações de relógio finito/crescente, horizonte, orçamento de ticks e conclusão.

`StartRequested` valida antes de iniciar, copia a configuração, cria streams e chama `Initialize`. A configuração original é isolada tanto da UI como do motor. `ResetRequested` inicializa novamente e fica parado; `ResumeRequested` inicia a progressão. Reset repõe também a velocidade original. Alterar velocidade durante a execução não altera o passo nem os sorteios.

O controlador acumula `Time.unscaledDeltaTime*speed`; `SimulationSession` executa no máximo 1000 ticks/frame por defeito, preservando o saldo. Uma pausa não acumula tempo nem consome RNG. A conclusão suspende stepping. Falhas do motor, relógio estagnado/não finito, overflow e horizonte máximo param a execução e mostram erro, sem apresentar conclusão normal. Cada frame apresenta apenas o último snapshot; a UI recebe progresso/FIFO calculados por M3.

Na cena principal atual, `SimulationComposition` constrói o motor real no Start e liga-o ao controlador. Os componentes M4 devem ser atribuídos nos campos da composição; os campos legados do controlador ficam vazios nessa cena. Entrar em Play não inicia automaticamente uma execução sem os controlos. OnDisable desliga os eventos e pausa; OnEnable volta a ligar sem duplicar subscrições. Ver `docs/REAL_INTEGRATION.md` para as ligações e testes reais.

## Lacunas de contrato e exportação

Os contratos de geradores, motor, registos, métricas, snapshots, progresso, FIFO e seis eventos de controlos são suficientes para M1/M2. Não foi necessário alterar a API existente.

Faltam duas capacidades partilhadas para fechar a exportação:

- API do exportador M5 para receber configuração, bagagens e eventos e devolver o caminho gravado.
- Método de estado/sucesso na apresentação para mostrar o caminho da exportação; `ISimulationDashboardView` só define `Render` e `ShowError`.

Proposta mínima **pendente de acordo com M4/M5**: definir num PR de contratos uma interface de exportação com aqueles argumentos e retorno de caminho, e acrescentar um método de informação no dashboard. Nenhuma destas alterações foi aplicada unilateralmente.

Até esse acordo, M1 dispõe de `BindExporter(Action<SimulationConfig,IReadOnlyList<BagRecord>,IReadOnlyList<SimulationEventRecord>>)` como callback local de composição. Só é chamado após conclusão, com cópias separadas dos registos/configuração. Um adaptador M5 pode chamar `NotifyExportCompleted(path)`; o evento local `ExportCompleted` pode ligar à apresentação futura. Não é um exportador implementado nem substitui o contrato pendente. Sem adaptador, o botão apresenta erro explícito. Erros de I/O do adaptador são tratados pelo controlador.

## Como executar os testes

Unity Editor **6000.6.7f1**, Window > General > Test Runner > EditMode > Run All. O pacote de testes já existia; não foram adicionadas dependências ao manifest.

Para batch (ajustar o caminho do executável):

```sh
Unity -batchmode -nographics -projectPath /caminho/BAGFLOW \
  -runTests -testPlatform EditMode \
  -testResults /tmp/bagflow-results.xml -logFile /tmp/bagflow-unity.log
```

Não usar `-quit` com `-runTests`, pois pode terminar antes de completar os testes. No desenvolvimento desta alteração usa-se uma cópia temporária de `Assets`, `Packages` e `ProjectSettings` para não tocar nas configurações/cache do projeto em uso.

Os ensaios usam 100000 amostras nas distribuições contínuas principais, 10000 lotes Binomial(60,0.08), 10000 processos Poisson em janelas de 60 s e 10000 amostras na normal condicionada a `[2,3]`. Médias: tolerância de cinco erros padrão `5*sqrt(var/n)`. Variâncias: `5*sqrt((mu4-var²)/n)`; na triangular `mu4=2.4*var²`, na exponencial `mu4=9*var²`, na binomial `mu4=3*var²+var*(1-6p(1-p))`, na Poisson `mu4=3*var²+var`. Para truncadas, usa-se o majorante rigoroso `mu4 <= max(|L-E|,|H-E|)²*var`. São verificações estatísticas determinísticas com seed fixa, não prova empírica de adequação a aeroportos.

Os testes originais de integração usam um motor de teste: validam pausa, reset, velocidades, backlog, conclusão, configurações inválidas, erro por estagnação/horizonte, eventos UI, cópias de exportação e importação da cena sem scripts em falta. Foram complementados pelos testes M3 e pelos testes reais documentados em `docs/REAL_INTEGRATION.md`; a inspeção visual da demo continua pendente.

A evidência da execução efetiva e as limitações encontram-se em `docs/M1_M2_TEST_RESULTS.md`.
