# Evidência de testes M1 + M2

Execução inicial do PR em 22/10/2026, Unity **6000.6.7f1 (b22767f04f04)**, macOS, Mono. O executável real do Editor compilou os assemblies e executou o Test Runner numa cópia isolada em `/tmp/bagflow-m1m2-unity`, com `Assets`, `Packages` e `ProjectSettings` do repositório. A primeira importação ocorreu numa Library nova. Não foram alteradas as configurações do projeto original.

## Resultado inicial

- Comando: `Unity -batchmode -nographics -projectPath /tmp/bagflow-m1m2-unity -runTests -testPlatform EditMode -testResults /tmp/bagflow-m1m2-results.xml -logFile /tmp/bagflow-m1m2-unity.log`.
- Exit code: **0**.
- XML NUnit: **38 testes, 38 passaram, 0 falharam, 0 ignorados**.
- Inclui `RealPlayModeLifecyclePausesAndUnsubscribesWithoutDuplicates`, que entra e sai de Play Mode para testar o ciclo de vida real do controlador.
- `MainSceneContainsControllerAndPreservesCameraAndLight` carrega a cena com o EditorSceneManager e verifica câmara, luz, controlador e ausência de scripts em falta.
- Compilação dos contratos, probabilidade, integração e testes verificada no Editor.
- XML e log completos da execução disponíveis localmente nos caminhos indicados no comando; são artefactos temporários, não caches versionados.

## Resultados estatísticos

Valores registados pelo NUnit; unidades e parâmetros descritos em `M1_M2_IMPLEMENTATION.md`. As tolerâncias usam cinco erros padrão e momentos de quarta ordem/majorantes, não percentagens arbitrárias.

| Teste | n | Média observada | Média teórica | Variância observada | Variância teórica |
|---|---:|---:|---:|---:|---:|
| Triangular(6,9,13) | 100000 | 9.337808369 | 9.333333333 | 2.058434626 | 2.055555556 |
| Exponencial inspeção (média 8 s) | 100000 | 7.973067440 | 8.000000000 | 63.159358362 | 64.000000000 |
| Normal truncada (18,4,5,32) | 100000 | 18.003761551 | 18.004629210 | 15.791764424 | 15.845472744 |
| Normal truncada (0,1,2,3) | 10000 | 2.317823920 | 2.315821327 | 0.061998600 | 0.061520780 |
| Binomial(60,0.08) | 10000 | 4.758600000 | 4.800000000 | 4.280354075 | 4.416000000 |
| Poisson (0.1/s × 60 s) | 10000 | 5.987800000 | 6.000000000 | 5.822433403 | 6.000000000 |
| Exponencial chegadas (média 5 s) | 100000 | 4.989865744 | 5.000000000 | 24.646986061 | 25.000000000 |

Também passaram os testes de parâmetros inválidos/não finitos, casos degenerados, limites, rejeição limitada da normal extrema, seeds iguais/diferentes, vetor PRNG conhecido, isolamento das seis streams, pausa/reset, velocidades 1/5/10/20x, backlog sem perda de ticks, horizonte/estagnação, cópias de configuração e exportação, e ligações de controlos.

Adicionalmente, executado `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s validation -p 'test_reference.py' -v`: **7 testes de referência Python passaram**. São uma referência independente e não substituem os testes C#/Unity acima.

## Limitações verificáveis

- Não executada a simulação operacional completa com N=1/60/500, FIFO, scanner ou bloqueios aplicados: M3 ainda não está implementado. Os testes Poisson/Binomial exercitam as amostras reais do M2, não o motor futuro.
- Não inspecionado o aspeto gráfico no Editor; execução em `-nographics`. M4 ainda não entregou UI/prefabs.
- Não gravados/lidos CSV ou JSON com M5: exportador e contrato partilhado de exportação ainda pendentes. O teste do controlador usa callback de teste e verifica cópias isoladas.
- Windows e reprodução entre plataformas não testados.
- Não gerado player/build distribuível; a plataforma de demonstração é o Editor.
- PDFs/notebooks do professor não encontrados no repositório/contexto disponibilizado.

A primeira execução no sandbox falhou na criação do socket do Package Manager. A execução com as permissões necessárias resolveu esse bloqueio. Duas execuções intermédias detetaram uma expectativa de lifecycle inválida no teste EditMode; a verificação foi transferida para o ensaio real em Play Mode. O resultado acima corresponde à bateria final, sem suprimir logs de erro.


## Revalidação após auditoria do PR #1

Correções na mesma branch `feature/integration-probability`, em 22/10/2026:

- `Advance` passa de 2N para N+1 chamadas a `GetSnapshot()` para N ticks na mesma chamada. Reutiliza o snapshot anterior localmente, sem alterar contratos. O teste conta 6 consultas para 5 ticks, mais 4 para 3 ticks na chamada seguinte, e nenhuma consulta em frames sem ticks, pausa ou após conclusão. Preservados os testes de estagnação, horizonte e conclusão; acrescentados casos de relógio não finito antes/depois de `Step` e relógio regressivo.
- Composição explícita antes de `Awake` preservada após ativação de um objeto inicialmente inativo. Teste real em Play Mode confirma também que substituir uma ligação remove os controlos antigos e os atualiza para parado. Documentado ponto de composição preferencial no `Start` do coordenador, após inicialização das vistas. Sem alteração de contratos ou implementação de M3/M4/M5.
- `MainSimulation` configurada como primeira cena habilitada em `EditorBuildSettings`, com GUID validado pelo AssetDatabase. O teste de carregamento continua a verificar câmara, luz, controlador e ausência de scripts em falta.
- `.DS_Store`, `__pycache__/` e `*.pyc` ignorados. `.DS_Store` removido apenas do índice; ficheiros locais da raiz e de `Assets` preservados.

Resultado da execução final: **48/48 testes Unity passaram, zero falhas/ignorados, exit code 0**, com Unity **6000.6.7f1** em macOS. Bateria EditMode inclui dois ensaios que entram em Play Mode. Compilação verificada, sem erros C# no log. Os algoritmos probabilísticos não foram alterados. Os **7/7 testes Python** também voltaram a passar, exit code 0.

Comandos executados:

```sh
Unity -batchmode -nographics -projectPath /tmp/bagflow-m1m2-unity \
  -runTests -testPlatform EditMode \
  -testResults /tmp/bagflow-pr1-audit-results.xml \
  -logFile /tmp/bagflow-pr1-audit-unity.log
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s validation -p 'test_reference.py' -v
```

Artefactos locais completos: `/tmp/bagflow-pr1-audit-results.xml`, `/tmp/bagflow-pr1-audit-unity.log` e `/tmp/bagflow-pr1-audit-python.log`. Não são incluídos no Git. A cópia isolada recebeu os scripts, cena, Packages e ProjectSettings atuais; nenhum cache Unity é versionado.

Mantêm-se as limitações operacionais anteriores: motor/UI/exportador reais ainda pendentes, sem demonstração gráfica (`-nographics`), sem testes Windows ou player distribuível. Nenhum impedimento à publicação destas correções.
