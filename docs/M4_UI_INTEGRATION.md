# M4 — Interface e visualização Unity 2D (montagem e integração)

**Integração final:** a cena principal já contém o prefab UI, as três referências da composição e o adaptador M5. Ver [montagem final e roteiro de demonstração](FINAL_SCENE_INTEGRATION.md). O relato original do módulo abaixo mantém a sua rastreabilidade.

Branch `feature/visual-ui`. Implementa `ISimulationWorldView`, `ISimulationDashboardView` e `ISimulationControlView` sem alterar contratos, motor, geradores, `SimulationController`/`SimulationComposition`, exportador ou `MainSimulation.unity`.

## Ficheiros

| Caminho | Função |
|---|---|
| `Assets/Scripts/Presentation/` (assembly `BAGFLOW.Presentation`) | todo o código de apresentação |
| `Assets/Scripts/Presentation/Editor/` | gerador de prefabs/cena e atalho de ligação |
| `Assets/Prefabs/BAGFLOW_UI.prefab` | UI completa (Canvas + 3 vistas) |
| `Assets/Prefabs/BaggageSpriteView.prefab` | visual de uma mala (usado pelo pool) |
| `Assets/Sprites/ui_rounded.png`, `ui_circle.png` | únicos sprites (gerados por código, 9-slice) |
| `Assets/Scenes/VisualSandbox.unity` | cena de teste própria do M4 |
| `Assets/Tests/Editor/Presentation/` | testes EditMode e Play Mode |

Componentes: `BaggageWorldView` (ISimulationWorldView, gere o pool), `BaggageSpriteView` (mala), `BaggageViewPool`, `BeltView` (tapete + bloqueio), `ScannerView`, `StationView` (entrada, fila, saídas), `DashboardView` (ISimulationDashboardView), `SimulationControlPanel` (ISimulationControlView), `BagflowUi` (raiz do prefab), `BagflowUiFactory` (constrói o prefab; usado pelos testes), `SandboxScenarios`/`VisualSandboxDemo` (apenas sandbox).

## Como a UI usa o motor

* Estado visual por `BagState` (sem segundo sistema de estados); cor e etiqueta em `UiTheme`.
* `Transporting` → posição = `Lerp(início, fim do tapete, TransportProgress01)`; `ScannerQueue` → slot = `QueuePosition` (0 = junto ao scanner); `Scanning` → posição fixa + `ScanProgress01`; `WaitingEntry` → pilha à entrada; `Approved`/`Flagged` → faixa de saída (as 8 conclusões mais recentes por faixa são desenhadas, o contador vem de `Metrics`). `Scheduled` não é desenhado.
* O movimento das riscas do tapete usa o tempo simulado do snapshot: pára com `BeltBlocked` (e em pausa) e retoma sem saltos.
* Nenhum valor aleatório, nenhuma métrica recalculada (o único total derivado é `Bags.Count`).
* Suavização visual (`Animate`) só interpola para o alvo calculado a partir do snapshot.

## Montar na `MainSimulation.unity` (feito pelo M1)

1. Arrastar `Assets/Prefabs/BAGFLOW_UI.prefab` para a raiz da cena. Não é preciso câmara nova; o prefab cria um `EventSystem` (Input System) se a cena não tiver nenhum.
2. Selecionar o objeto `BAGFLOW Integration` (já tem `SimulationController` + `SimulationComposition`). **Não** adicionar outro controlador/composição.
3. No `SimulationComposition`, ligar no Inspector:
   * `World View Component` → arrastar o GameObject `BAGFLOW_UI/World`
   * `Dashboard View Component` → `BAGFLOW_UI/Dashboard`
   * `Control View Component` → `BAGFLOW_UI/Controls`

   Cada um destes três GameObjects tem apenas o seu componente (sem `Graphic`), por isso o arrastar é inequívoco. Atalho: com `BAGFLOW_UI` selecionado, menu **BAGFLOW ▸ M4 ▸ Connect selected BAGFLOW_UI to SimulationComposition**, e gravar a cena.
4. Os campos legados do `SimulationController` ficam vazios (a composição liga tudo em `Start`).
5. Opcional: no componente `BagflowUi` da raiz, o campo `Controller` pode ficar vazio (procura o primeiro `SimulationController`). Serve para mostrar o resultado de `controller.NotifyExportCompleted(path)` (M5).
6. Entrar em Play: o painel mostra `PRONTO`; **Iniciar** envia `StartRequested(config)`.

Verificar que a UI recebe snapshots: depois de Iniciar, o relógio `Tempo simulado` avança, o chip passa a `EM SIMULAÇÃO`, as malas surgem à entrada e a coluna "Criadas n / N" cresce. Se nada acontecer: confirmar que os três campos da composição não estão vazios (Console: `BAGFLOW composition: ...`) e que existe um `EventSystem` ativo.

## Controlos

Iniciar (envia a configuração do formulário), Pausar/Retomar (um botão que emite `PauseRequested` ou `ResumeRequested`), Reiniciar, velocidades 1×/5×/10×/20× (`PlaybackSpeedRequested` após o arranque; antes disso entram em `config.PlaybackSpeed`), quantidade de malas, seed, modo de chegadas e *Parâmetros avançados* (todos os campos restantes de `SimulationConfig`; o intervalo médio entre bloqueios é convertido em `BlockageRatePerSecond = 1/intervalo`, 0 = sem bloqueios). A UI só verifica que o texto é numérico; a validação de domínio é do controlador e o erro aparece no painel (`ShowValidationError`) ou no banner do dashboard (`ShowError`). **Exportar** emite `ExportRequested` e fica ativo só com a simulação concluída; o resultado de `NotifyExportCompleted` aparece no painel e no banner. Não existe exportador no M4.

## VisualSandbox

`Assets/Scenes/VisualSandbox.unity` contém `BAGFLOW_UI`, um `SimulationController` + `SimulationComposition` próprios (motor real, ligados como acima) e `VisualSandboxDemo`. Em Play: **Iniciar** corre o motor real; ← / → ou 1–9 mostram snapshots fictícios (vazio, N=1, 50 a transportar, fila + scanner a 50%, bloqueio ativo, 5 terminais, N=60, N=500, concluída); **0** volta ao motor real.

## Regenerar os assets

Menu **BAGFLOW ▸ M4 ▸ Build Prefabs and VisualSandbox**, ou em batch:

```sh
Unity -batchmode -nographics -projectPath <projeto> -executeMethod AirportBaggageSimulator.EditorTools.BagflowUiBuilder.BuildAllBatch
```

## Testes

Unity ▸ Test Runner ▸ EditMode ▸ `BAGFLOW.PresentationTests` (inclui testes Play Mode através de `EnterPlayMode`, como os de integração existentes), ou `-runTests -testPlatform EditMode`. Os resultados executados estão no PR.

## Limitações

* A inspeção gráfica real (aspeto, legibilidade em projetor) é manual; testes automáticos verificam estrutura, estados e comportamento, não a aparência.
* Layout desenhado para 16:9 (referência 1920×1080, `CanvasScaler` por largura/altura); outras proporções funcionam mas não foram afinadas.
* A fila desenha 6 posições e as saídas 8 por faixa; o excedente é indicado em texto/contadores.
* Texto com a fonte integrada do Unity (sem TextMeshPro, para não depender de recursos importados).
* Sem exportador (M5) nem alterações a `MainSimulation.unity`; a montagem nessa cena é feita pelo M1.


## Correções após revisão independente

- Reiniciar repõe também a seleção visual da velocidade original da execução, de acordo com o reset do controlador. A UI guarda essa velocidade apenas quando `SetRunning(true)` confirma um `StartRequested` aceite; um arranque rejeitado não altera o valor guardado. Um novo arranque aceite passa a definir a nova velocidade original. Não se emite um pedido de mudança de velocidade durante o reset.
- As oito linhas dos parâmetros avançados foram compactadas para deixar espaço para os botões inferiores. O prefab `BAGFLOW_UI` foi atualizado apenas nas âncoras dos campos/etiquetas, preservando os restantes objetos e referências. `VisualSandbox` herda o layout do prefab, sem alterações à cena principal.
- Regressões acrescentadas: quatro pares de velocidades com controlador/motor reais; arranque rejeitado; novo arranque aceite; e um teste em Play Mode com raycasts no centro de cada um dos 15 campos avançados, tanto na factory como no prefab guardado.

Revalidação das correções no Unity **6000.6.7f1**, macOS, numa cópia isolada: **133/133 testes passaram**, zero falhas/ignorados, exit code 0 (126 existentes + 7 casos de regressão). A compilação foi verificada no Editor. O teste de layout entrou em Play Mode e confirmou o raycast nos 15 campos, na factory e no prefab efetivamente guardado. O reset foi testado a partir de 1×/5×/10×/20×, incluindo um arranque rejeitado e um novo arranque aceite.

Comando: `Unity -batchmode -nographics -projectPath /tmp/bagflow-pr4-review -runTests -testPlatform EditMode -testResults /tmp/bagflow-pr4-fixed-results.xml -logFile /tmp/bagflow-pr4-fixed-unity.log`.

Logs/XML locais: `/tmp/bagflow-pr4-fixed-unity.log` e `/tmp/bagflow-pr4-fixed-results.xml` (não versionados). O prefab mantém todo o conteúdo anterior, com apenas 58 alterações de âncoras. Contratos, controlador, motor, geradores, cena principal e configuração Unity permaneceram inalterados. A execução em batch não substitui a inspeção gráfica manual em projetor.
