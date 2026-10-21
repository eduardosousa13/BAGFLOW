# UNITY_HANDOFF — contrato executável entre motor e UI

**Obrigatório para M1 e M4.** O ficheiro `Assets/Scripts/Contracts/SimulationContracts.cs` define `ISimulationWorldView`, `ISimulationDashboardView` e `ISimulationControlView`. Não criar outros eventos públicos incompatíveis. O motor não conhece Unity; os views não sorteiam números nem alteram `BagRecord`.

## Ligações que M1 tem de realizar
1. `SimulationController` (único MonoBehaviour integrador na `MainSimulation.unity`) recebe referências no Inspector para componentes Unity que implementam aquelas três interfaces; se usar campos serializados de componentes concretos, converter para as interfaces em `Awake`. M4 fornece os prefabs/components. Não esperar que o Inspector serialize interfaces diretamente.
2. O M1 cria `IRandomStreamProvider` NOVO com `config.Seed` cada vez que inicia ou reinicia. **Não reutilizar a mesma instância de provider** em Reset porque as streams já foram consumidas.
3. `StartRequested(config)`: validar parâmetros, instanciar streams, inicializar motor, reiniciar visualização, aplicar primeiro snapshot. Campos inválidos => `ShowValidationError`, não iniciar.
4. `PauseRequested`, `ResumeRequested`: alternam apenas a acumulação de ticks; nenhuma chamada a RNG durante pausa.
5. `PlaybackSpeedRequested(speed)`: aceitar apenas velocidades predefinidas 1, 5, 10 e 20 (10 é a velocidade inicial); não mudar Δt nem resultados lógicos.
6. `ResetRequested`: parar, recriar stream/provider, `Initialize` com config original; `ResetView` e `Render`; sequência gerada deve repetir para seed/config iguais.
7. `ExportRequested`: chamar exportador com cópias finais das bagagens, eventos e config, indicar caminho do ficheiro no dashboard; tratar erro de I/O.
8. `Update()`: acumular `Time.unscaledDeltaTime * playbackSpeed`; chamar `engine.Step()` para cada tick completo, com limite prudente de ticks por frame e preservação do restante acumulado; não descartar simulação acumulada silenciosamente. Render uma vez por frame (último snapshot); quando concluído suspender stepping.
9. Exceção/caso sem progresso até `MaxSimulatedSeconds`: parar e apresentar erro explícito; não confundir com conclusão normal.

## Nomes e implementação M4
- `BaggageWorldView : MonoBehaviour, ISimulationWorldView`, implementa `Render(SimulationSnapshot)` e `ResetView()`.
- `DashboardView : MonoBehaviour, ISimulationDashboardView`, implementa `Render(SimulationSnapshot)` e `ShowError(string)`.
- `SimulationControlPanel : MonoBehaviour, ISimulationControlView`, implementa exatamente os 6 eventos e os métodos indicados nos contratos.
- Componentes visuais próprios podem ter nomes adicionais. Prefab-raiz recomendado `BAGFLOW_UI`, prefab individual `BaggageSpriteView`.
- Para testes visuais, M4 pode criar `Assets/Scenes/VisualSandbox.unity` **sem alterar** `MainSimulation.unity`; usar snapshots fictícios estáticos.

## Regra de representação de estados
- `Scheduled`: não renderizar.
- `WaitingEntry`: junto à entrada, com disposição em fila/contador.
- `Transporting`: posição ao longo do tapete proporcional a `BagRecord.TransportProgress01` (motor fornece valor em [0,1]; bloqueio mantém progresso constante).
- `ScannerQueue`: usar `QueuePosition` do motor (0 para primeiro), não recalcular FIFO na UI.
- `Scanning`: posição fixa no scanner; barra de progresso `ScanProgress01`.
- `Approved`/`Flagged`: saída correspondente, com acumulador e remoção visual quando espaço se tornar escasso. Não alterar estado lógico.
- `SimulationSnapshot.Bags` são cópias dos registos do motor. **Não modificar**; `Render` pode ler mas nunca editar.

## Forma e tempos das animações
A coordenada visual é didática. M4 escolhe âncoras na vista superior para entrada, tapete, fila, scanner e saídas e documenta-as; não calcular tempos físicos a partir dos pixels. Identificar os objetos por ID e fazer pooling básico se N for elevado. As métricas apresentadas vêm de `SimulationSnapshot.Metrics`.

## Casos que devem funcionar antes do merge
Snapshot com 0 bagagens; com 1; com 50 transportando; com fila; bloqueio ativo; scanner a 50%; 5 bagagens terminais. Os controlos devem disparar os eventos definidos e não conter lógica duplicada.
