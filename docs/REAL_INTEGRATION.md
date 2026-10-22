# Integração real M1 + M3

Base: `origin/main` após integrar os PRs #1 e #2. Contratos aprovados e código de Probability/Simulation preservados.

## Composição e cena

`SimulationComposition`, em `Assets/Scripts/Integration/Composition/`, é o ponto de composição concreto M1. No seu `Start`, depois de `Awake`/`OnEnable` das vistas, constrói `SimulationEngine` e chama `SimulationController.Bind` através de `ISimulationEngine` e das três interfaces M4 existentes. Não inicializa ou inicia uma execução automaticamente: o evento `StartRequested(config)` continua a validar a configuração, criar as streams determinísticas e inicializar o motor através de `SimulationSession`.

O objeto `BAGFLOW Integration` de `MainSimulation.unity` contém o controlador e a composição. Câmara/luz/URP e configuração da cena inicial permanecem intactas. Entrar em Play já constrói o motor real, sem vistas fictícias nem simulação automática na ausência dos controlos M4.

A composição explícita `Compose(world, dashboard, controls)` pode ser feita antes de Start e tem precedência sobre os campos serializados. Uma chamada explícita posterior substitui o motor/run e remove os controlos antigos através de Bind. Deve ser usada apenas ao montar/substituir o conjunto de componentes, nunca por frame ou para pausa/reset. `Engine` expõe a instância como `ISimulationEngine` para observabilidade; durante a execução, só o controlador/sessão chama `Step`.

## Assemblies

| Assembly | Dependências BAGFLOW |
|---|---|
| Contracts | nenhuma; sem UnityEngine |
| Probability | Contracts; sem UnityEngine |
| Simulation | Contracts; sem UnityEngine |
| Integration | Contracts, Probability |
| Composition (novo) | Contracts, Integration, Simulation |
| RealIntegrationTests (Editor, novo) | Contracts, Integration, Simulation, Composition |

O controlador e a sessão continuam sem referência à classe concreta do motor. A construção concreta fica no assembly separado de composição, evitando ciclos ou dependência de domínio na apresentação. Não foi alterada nenhuma interface partilhada, nem foi identificada incompatibilidade de contratos entre os módulos.

`SimulationController.Advance(double realSeconds)` contém o caminho que já existia em `Update`; este passa a delegar `Time.unscaledDeltaTime` nesse método. Isto permite testar o controlador com frames determinísticos sem trocar o motor ou os geradores, e sem esperar horas reais numa execução a 1×. Não introduz lógica de domínio nem altera a regra de acumulação/limite de ticks. Não chamar Advance manualmente além do Update durante uma execução normal, pois isso adicionaria tempo de frame duas vezes.

## Ligações futuras M4 e M5

Para a **cena principal**, atribuir nos campos de `SimulationComposition` os componentes que implementam `ISimulationWorldView`, `ISimulationDashboardView` e `ISimulationControlView`. Os campos legados do controlador permanecem vazios nessa cena; a composição é a fonte das ligações. Em montagem dinâmica, passar as três interfaces a Compose depois da inicialização das vistas. Os controlos não devem emitir pedidos automáticos antes desse ponto de composição.

As vistas continuam a receber snapshots separados, com estado, métricas, progressos e posições FIFO calculados pelo motor. Botões emitem os seis eventos existentes. Pausa não avança relógio/streams; reset fica parado e reconstrói streams com a seed/configuração original; velocidades aceites continuam 1/5/10/20×.

O ponto M5 continua a ser o callback local `controller.BindExporter(...)`, chamado após conclusão com cópias dos resultados reais. O adaptador pode chamar `NotifyExportCompleted(path)` e ligar `ExportCompleted` à apresentação. Não se escreveu exportador, ficheiros CSV/JSON ou UI. Continuam pendentes o acordo da API partilhada de exportação e um método de informação/sucesso no dashboard, já documentados no relatório M1/M2. Os testes exercitam o callback com recetores de teste, sem escrita de ficheiros e sem fingir que M5 está concluído.

## Verificação real

Testes em `Assets/Tests/Editor/RealIntegration/RealEngineIntegrationTests.cs`, num assembly próprio. Não há motores ou samplers fictícios nesses testes; os únicos adaptadores são recetores de vistas/controlos e o callback de exportação.

- N=1/60/500 em chegadas Constant e Exponential, percorrendo composição → controlador → sessão → motor → Probability.
- Conservação em frames observados, estados finais Approved/Flagged, IDs, progressos e posições FIFO.
- Histórico de eventos verifica a FIFO independentemente da implementação, exclusividade do scanner, uma chegada/decisão por ID, conclusão única, instantes Poisson crescentes e contagens consistentes.
- Métricas finais comparadas aos registos e à integração dos intervalos ocupados/bloqueados nos eventos.
- Pausa/retoma com histórico inalterado durante pausa; reset reproduz estado inicial e o resultado completo, mesmo após editar a configuração original no chamador.
- Comparação exata de todos os campos dos registos de malas, eventos e métricas em 1× e 20×, com partições de frame diferentes. O tempo real/saldo visual não faz parte do histórico lógico.
- Sessão direta comparada à composição completa, com motor real e mesma seed.
- Cenário com bloqueios frequentes/sobrepostos e p=0/1, incluindo scanner a trabalhar durante bloqueio.
- Validação inválida sem substituir uma execução ativa; falha real do horizonte apresentada sem conclusão normal.
- Callback recebe resultados reais separados; mutações do recetor não alteram o motor; erro I/O é apresentado.
- Cena aberta em EditMode sem scripts em falta; ensaio em Play Mode carrega a cena, verifica a composição automática em Start e avanço pelo Update real, pausa e conclusão com adaptadores de teste.

Para repetir, Unity 6000.6.7f1 > Test Runner > EditMode > Run All, ou:

```sh
Unity -batchmode -nographics -projectPath /copia/isolada/BAGFLOW \
  -runTests -testPlatform EditMode \
  -testResults /tmp/bagflow-real-integration-results.xml \
  -logFile /tmp/bagflow-real-integration-unity.log
```

A execução em batch verifica scripts/lógica/ciclo de vida, não o aspeto gráfico de componentes M4 ainda inexistentes. Windows e player distribuível exigem validação posterior.

## Resultado executado

22/10/2026, Unity **6000.6.7f1 (b22767f04f04)**, macOS, Mono, base `83f1611` (main com os dois PRs). Executado o comando batch acima na cópia isolada `/tmp/bagflow-m1m2-unity` com Assets/Packages/ProjectSettings da branch, sem modificar configurações/caches do projeto original.

- **96/96 testes passaram**, zero falhas/ignorados, exit code **0**. São **14 casos novos de integração real** mais **82 casos anteriores**.
- Compilação e referências entre assemblies verificadas no Editor, sem erros C# no log.
- N=1/60/500 concluídos em ambos os modos de chegada, com igualdade exata do histórico completo entre 1× e 20× depois do reset. Pausa/retoma e restauração da seed/configuração original passaram.
- FIFO, scanner único, conservação, estados finais, métricas calculadas a partir dos registos/eventos e bloqueios sobrepostos passaram.
- Os dois ensaios novos em Play Mode passaram: composição explícita antes da ativação sem ser substituída por Awake/Start; e carregamento de MainSimulation com composição automática, avanço pelo Update real, pausa e conclusão. Juntam-se aos dois ensaios anteriores de lifecycle.
- O teste de importação da cena passou sem scripts em falta; câmara/luz preservadas.
- Também executado `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s validation -p 'test_reference.py' -v`: **7/7 testes Python passaram**, exit code 0.
- Artefactos completos locais: `/tmp/bagflow-real-integration-results.xml`, `/tmp/bagflow-real-integration-unity.log` e `/tmp/bagflow-real-integration-python.log`. Não são versionados.

Uma execução intermédia passou 94/95 testes; o teste do callback de exportação precisava de retomar após o erro esperado de pedir exportação antes da conclusão, pois o controlador pausa em caso de erro. A expectativa foi corrigida, sem alterar esse comportamento. A contagem acima corresponde à execução final.

Sem alterações a contratos aprovados, motor, amostradores, Packages, ProjectSettings ou parâmetros por defeito. Pendentes: implementação/ligação M4, adaptador/exportação real M5, inspeção gráfica, Windows e player distribuível. Nenhum impedimento à publicação desta integração.
