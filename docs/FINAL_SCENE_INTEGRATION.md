# Integração final M1–M5 e demonstração

A `MainSimulation.unity` contém uma instância ligada a `Assets/Prefabs/BAGFLOW_UI.prefab`. O objeto existente `BAGFLOW Integration` mantém um único `SimulationController` e uma única `SimulationComposition`, e recebe `SimulationCsvExportAdapter`. A composição referencia explicitamente `World`, `Dashboard` e `Controls`; a raiz da UI e o adaptador referenciam o mesmo controlador. Os campos legados do controlador ficam vazios. A montagem foi guardada pelo Unity Editor usando `PrefabUtility.InstantiatePrefab` e `BagflowUiBuilder.ConnectComposition`.

Câmara, luz, URP, contratos, distribuições, motor e análise estatística foram preservados. A composição cria o único motor real no seu `Start`; o prefab cria um único EventSystem quando ainda não existe. Não há `VisualSandboxDemo` na cena principal.

## Procedimento de demonstração

1. Abrir o projeto com Unity **6000.6.7f1**, abrir `Assets/Scenes/MainSimulation.unity` e entrar em Play. O estado inicial é **PRONTO**.
2. Manter seed **42** e os parâmetros didáticos aprovados. Introduzir N=1 para observar uma mala; carregar **Iniciar**. Repetir com N=60 e N=500 (a edição de um campo só produz uma nova configuração ao carregar Iniciar).
3. Experimentar **Pausar/Retomar** e velocidades **1×/5×/10×/20×**. A pausa congela o relógio lógico; a velocidade altera a reprodução, sem alterar o passo lógico nem os sorteios.
4. Observar fila FIFO, scanner único, contadores e faixas de aprovação/sinalização. Com os parâmetros por defeito e seed 42, N=60/500 também mostram bloqueios; o scanner continua a trabalhar durante as paragens do tapete.
5. **Reiniciar** repõe a seed/configuração/velocidade da execução aceite e fica parado. **Retomar** repete essa execução. Para usar valores novos no formulário, carregar **Iniciar**.
6. Ao mostrar **CONCLUÍDA**, carregar **Exportar resultados**. O painel e o banner mostram `Exportado para: <pasta>`; abrir essa pasta e confirmar `bags.csv`, `events.csv` e `config.json`. As contagens finais devem corresponder ao dashboard.

Os valores são pressupostos didáticos, não medições de um aeroporto. Em 20×, uma execução N=500 com os valores por defeito pode demorar vários minutos reais.

## Pasta de exportação

Por defeito: `Application.persistentDataPath/BAGFLOW/exports`, com uma pasta nova por exportação. O caminho efetivo aparece na UI e na Console. macOS: `~/Library/Application Support/<Company>/<Product>/BAGFLOW/exports`; Windows: `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\BAGFLOW\exports`.

O campo `Output Folder` do adaptador permite escolher outro diretório gravável. A cena mantém esse campo vazio e o controlador explicitamente atribuído. Erros de escrita aparecem no dashboard. Detalhes do formato e da recuperação de sobrescritas: [M5_VALIDATION_EXPORT.md](M5_VALIDATION_EXPORT.md).

## Atalhos da VisualSandbox

A sandbox continua separada da cena principal e fora das cenas de build. Os atalhos 1–9, 0 e as setas são ignorados enquanto um `Selectable` da UI estiver selecionado (campos de texto, botões e outros controlos de teclado, incluindo os seus filhos). Assim, escrever `1` na quantidade não muda para o cenário vazio; as setas também ficam disponíveis para editar o texto. Clicar numa área sem controlos para retirar o foco permite voltar a usar os atalhos. Sem foco, 1–9/setas escolhem snapshots fictícios e 0 devolve o controlo ao motor real.

## Verificação e limites

Os testes novos em `Assets/Tests/Editor/FinalIntegration/` carregam as cenas guardadas, sem reconstruir a UI nem substituir o motor/exportador. Verificam referências serializadas, prefab, ausência de duplicados/scripts em falta, N=1/60/500, botões, pausa, velocidades, reset/seed, histórico lógico entre 1×/20×, dashboard, fila, bloqueios e exportações reais com mensagem de sucesso. Os exports dos testes usam uma pasta temporária e são removidos no final. A regressão da sandbox injeta eventos de um teclado Input System real, com e sem foco de edição.

Os ensaios entram em Play Mode, verificam avanço pelo `Update` e usam depois durações de frame controladas através do mesmo `SimulationController.Advance` para completar as execuções sem esperar o tempo de reprodução integral. Não chamam `Step` diretamente. Os testes existentes de invariantes, FIFO e distribuições continuam a ser executados.

Executar: Test Runner → EditMode → Run All (inclui ensaios que entram em Play Mode), ou:

```sh
Unity -batchmode -nographics -projectPath <copia-do-projeto> \
  -runTests -testPlatform EditMode -testResults /tmp/bagflow-final-results.xml \
  -logFile /tmp/bagflow-final-unity.log
PYTHONDONTWRITEBYTECODE=1 python -m unittest discover -s validation -p 'test_*.py' -v
```

Limitações: a execução batch não verifica a aparência/legibilidade em projetor nem os cliques físicos; isso requer inspeção manual. O layout é otimizado para 16:9; a fila mostra seis posições e as saídas oito malas por faixa, mantendo as contagens completas. Os testes foram executados em macOS; Windows e player distribuível não foram testados nesta integração. Não foram alterados os parâmetros estatísticos ou regenerados os dados do notebook.

## Resultados executados em 23/10/2026

Unity **6000.6.7f1**, macOS, cópia isolada `/tmp/bagflow-final-unity`: **155/155 testes Unity aprovados**, zero falhas/ignorados, exit code 0 (152 existentes e 3 novos). **23/23 testes Python aprovados**, exit code 0. A compilação e a ausência de scripts em falta foram verificadas no Editor. A bateria inclui os ensaios em Play Mode descritos acima e a regressão dos atalhos com e sem foco.

| N (seed 42, valores por defeito) | Aprovadas | Sinalizadas | Ocorrências Poisson | Tempo lógico (s) |
|---|---:|---:|---:|---:|
| 1 | 1 | 0 | 0 | 25,4 |
| 60 | 56 | 4 | 5 | 499,5 |
| 500 | 472 | 28 | 30 | 4074,5 |

As três exportações reais passaram a comparação dos CSV com os registos do motor e a verificação de `config.json`, além da mensagem de sucesso no painel/banner. Os replays após reset produziram CSV lógico idêntico a 1× e 20×. Nenhum registo existente da câmara, luz, renderização ou controlador foi alterado na serialização da cena.

Artefactos locais, não versionados: `/tmp/bagflow-final-results.xml`, `/tmp/bagflow-final-unity.log` e `/tmp/bagflow-final-python.log`. Uma execução intermédia detetou que o teclado virtual não chegava ao estado de Play Mode em batch; o teste passou a encaminhá-lo com políticas temporárias de foco, restauradas no fim. A contagem acima corresponde à execução final completa.
