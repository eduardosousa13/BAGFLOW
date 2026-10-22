# M5 — exportação, validação estatística e fontes

Branch `feature/validation-export`. Não altera contratos, geradores M2, motor M3, `SimulationController`, cena principal nem componentes M4.

## Ficheiros

| Caminho | Conteúdo |
|---|---|
| `Assets/Scripts/Export/` (`BAGFLOW.Export`, sem UnityEngine) | `CsvFormat`, `SimulationExporter`, `ValidationBatchRunner` |
| `Assets/Scripts/Export/Unity/` (`BAGFLOW.Export.Unity`) | `SimulationCsvExportAdapter` — adaptador para `BindExporter` |
| `Assets/Scripts/Export/Editor/` | `ValidationDataMenu` — gera os conjuntos de validação |
| `Assets/Tests/Editor/Export/ExportTests.cs` | testes Unity (EditMode + 1 Play Mode) |
| `validation/bagflow_analysis.py` | leitura, fórmulas teóricas, estatísticas |
| `validation/test_analysis.py` | testes Python (fórmulas, ferramentas, dados Unity reais) |
| `validation/build_notebook.py` → `validation/BAGFLOW_validation.ipynb` | notebook (fonte e versão executada) |
| `validation/data/unity/` | exportações reais do Unity usadas pelo notebook |
| `validation/figures/` | figuras geradas pelo notebook |

## Formato de exportação (docs/CONTRACTS.md)

Cada exportação cria uma pasta **nova** `bagflow_<AAAAMMDD_hhmmss UTC>_seed<S>_n<N>` (sufixo `_2`, `_3`… se já existir) com:

* `bags.csv` — `id,weight_kg,nominal_transport_s,observed_transport_s,inspection_s,arrival_s,queue_enter_s,scan_start_s,complete_s,queue_wait_s,total_s,flagged`
* `events.csv` — `time_s,event_type,bag_id,detail,scheduled_time_s`
* `config.json` — todos os campos de `SimulationConfig` (nomes C#), seed, algoritmo PRNG (`SplitMix64-open52-v1`), streams, versão da app, data UTC, resumo (contagens).

Convenções: UTF-8 sem BOM, separador `,`, fim de linha `\n`, ponto decimal (InvariantCulture, independente do idioma do sistema), números em representação *round-trip* (amostras brutas sem arredondamento), aspas RFC 4180 nos campos de texto, **campo vazio = valor inexistente** (mala não inspecionada/não concluída), `flagged` = `1`/`0`/vazio, `NaN`/`Inf` recusados. `queue_wait_s = scan_start − queue_enter` e `total_s = complete − arrival` são derivados (nunca sorteados). `scheduled_time_s` de `BlockageOccurrence` é o instante matemático da ocorrência Poisson.

Proteções: nada é escrito se a formatação falhar; ficheiros escritos como `.tmp` e renomeados; uma pasta com `bags.csv/events.csv/config.json` existentes nunca é substituída sem `AllowOverwrite`; ficheiros parciais são removidos em caso de erro; erros de I/O propagam-se com mensagem clara.

Diretório por defeito (adaptador Unity): `Application.persistentDataPath/BAGFLOW/exports`
(Windows: `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\BAGFLOW\exports`; macOS: `~/Library/Application Support/<Company>/<Product>/BAGFLOW/exports`). Pode ser alterado no campo `Output Folder`.

## Ligação ao controlador (feito pelo M1)

O controlador expõe `BindExporter(Action<SimulationConfig, IReadOnlyList<BagRecord>, IReadOnlyList<SimulationEventRecord>>)` e `NotifyExportCompleted(string)`. O adaptador usa exatamente estas assinaturas:

1. Na `MainSimulation.unity`, selecionar o objeto `BAGFLOW Integration` (tem `SimulationController` + `SimulationComposition`).
2. **Add Component ▸ Simulation Csv Export Adapter**. Deixar `Controller` vazio (usa o do mesmo objeto) e `Output Folder` vazio (pasta por defeito).
3. Gravar a cena. Em Play: concluir uma simulação e carregar **Exportar resultados**.
4. O adaptador escreve a pasta e chama `NotifyExportCompleted(caminho)`; a UI M4 (`BagflowUi`) mostra "Exportado para: …". Exportar antes de concluir continua a ser recusado pelo M1; erros de I/O aparecem no banner do dashboard (o controlador pausa e reporta).

Ligação por código (alternativa): `adapter.Configure(controller, pasta)` ou `controller.BindExporter(adapter.Export)`. Não há segundo controlo da simulação: o adaptador só recebe as cópias finais que o controlador lhe passa.

## Conjuntos de validação

`ValidationBatchRunner` executa o motor M3 real com um `DeterministicRandomStreamProvider` novo por seed até à conclusão (mesmo caminho lógico do controlador, sem cena) e escreve `runs.csv`, `batch_bags.csv` (`seed` + colunas de `bags.csv`), `blockages.csv` (todas as ocorrências Poisson) e `batch.json`. Gerar no Unity: **BAGFLOW ▸ M5 ▸ Generate validation data**, ou

```sh
Unity -batchmode -nographics -projectPath <projeto> -executeMethod AirportBaggageSimulator.EditorTools.ValidationDataMenu.GenerateBatch [-bagflowOut <pasta>]
```

A pasta de destino não pode já conter os conjuntos (apagar `validation/data/unity` para regenerar). Os conjuntos versionados foram gerados assim (Unity 6000.6.7f1, Windows); os `batch_bags.csv` grandes foram comprimidos sem perdas para `.csv.gz` (`gzip`, mtime 0) e o notebook lê ambos.

| Conjunto | Configuração | Finalidade |
|---|---|---|
| `example_seed42_n60` | parâmetros aprovados, seed 42 | exportação de referência no formato do contrato |
| `samples_n500` | N=500, 25 seeds (1000–1024) | 12 500 amostras brutas de cada VA contínua |
| `binomial_n60` | aprovados, 2 000 seeds (100000–101999) | 2 000 valores de N_S; 4 000 janelas Poisson de 120 s em [0,240) s |
| `scenario_exponential_arrivals` / `scenario_no_blockages` | 300 seeds cada | comparação de cenários (não substituem os parâmetros aprovados) |

Poisson: só se usam janelas fixas dentro de [0, 240 s). Com chegadas constantes a última mala chega aos 295 s, logo todas as execuções cobrem esse horizonte e cada janela tem a mesma exposição T (sem viés pela duração variável). O total de bloqueios por execução **não** é usado como Poisson(λT).

## Notebook

```sh
pip install -r validation/requirements-optional.txt
python validation/build_notebook.py
python -m jupyter nbconvert --to notebook --execute --inplace validation/BAGFLOW_validation.ipynb
```

Contém: proveniência e integridade; para Triangular (transporte **nominal**), Exponencial (inspeção sorteada; contraste com espera e tempo total), Normal truncada (FDP condicionada, Φ estável): histograma normalizado + FDP, FDPC empírica vs teórica, média/variância com z, KS; Binomial (FMP/FDPC, momentos, χ²) a partir de execuções; Poisson em janelas fixas, N(t) acumulado, distinção ocorrências / inícios de paragem / tempo parado; fila reconstruída dos eventos; tempos totais, esperas, utilização; comparação de cenários com erros-padrão. Não há modo de dados sintéticos: sem exportações o notebook pára com instruções.

## Testes

* Unity: Test Runner ▸ EditMode (ou `-runTests -testPlatform EditMode`) — `BAGFLOW.ExportTests`.
* Python: `python -m unittest discover -s validation -p "test_*.py" -v`.

## Limitações e dependências restantes

* M1 tem de adicionar o adaptador à `MainSimulation.unity` (passos acima).
* Amostras de uma execução vêm do mesmo stream; seeds consecutivas entre execuções. Testes de ajustamento são indicativos (sem correção para comparações múltiplas).
* Tempos operacionais quantizados a Δt = 0,1 s; o tempo bloqueado exportado em `runs.csv` é a contabilização por tick do motor.
* Parâmetros didáticos (ver `docs/DATA_SOURCES.md`); as fontes só contextualizam ordens de grandeza.
