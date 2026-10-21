# PROJECT_SPEC — Especificação funcional v1.0

## 1. Objetivo e âmbito
Simulador de gestão e triagem de bagagens de aeroporto em Unity, **2D vista superior**, funcionamento automático de N malas, um tapete lógico, uma fila FIFO, um scanner e duas saídas. Foco académico na implementação/validação de **3 variáveis contínuas (triangular, exponencial, normal truncada)** e **2 discretas (binomial, Poisson)**. O projeto/seleção de distribuições foram aprovados pelo docente.

Regras atuais transmitidas pelo utilizador: sem exame/teste; mostrar o simulador funcional, matemática utilizada e localização da implementação. O mini-relatório é apoio de apresentação, não entrega obrigatória conhecida. Código não solicitado para entrega, mas será versionado para o grupo. Distinguir estas informações das notas dos slides antigos.

## 2. Visual e experiência
- Vista superior 2D com aspeto simples/moderadamente elaborado; fundo limpo, tapete animado, scanner, duas saídas e dashboard.
- No painel inicial: N malas; modo de chegada `Constant` ou `Exponential`; intervalo/tempo médio entre chegadas; seleção de perfis (`Didático`, `Baixa procura`, `Alta procura`, `Bloqueios frequentes`); seed; Iniciar.
- Durante: animação das malas, cores/legenda por estado, relógio simulado, nº criadas/concluídas/em fila, scanner ocupado/livre, bloqueios, tempo médio de espera, tempo total; Pausa/Retoma, velocidade 1x/5x/10x/20x, Reiniciar.
- Ao terminar: indicador de conclusão; resumo das métricas; botões para reiniciar/exportar; gráficos científicos em notebook externo, não são necessários histogramas completos no Unity.
- Só o coordenador edita a cena principal; visual usa prefabs, scripts e sprites próprios.

## 3. Configuração didática *hipotética* (não dados de aeroporto real)
| Parâmetro | Valor | Unidade/nota |
|---|---:|---|
| N | 60 | bagagens |
| seed | 42 | inteiro |
| chegada fixa / média aleatória | 5 | segundos |
| triangular (a, c, b) | (6, 9, 13) | segundos de movimento efetivo |
| inspeção exponencial | média 8 | segundos; taxa λI=1/8 s^-1 |
| normal base | μ=18, σ=4 | kg |
| truncamento | [5,32] | kg |
| limiar visual peso elevado | 23 | kg (não altera triagem) |
| sinalização p | 0,08 | probabilidade por mala |
| bloqueios λB | 1/120 | eventos/segundo |
| pausa por bloqueio | 8 | segundos simulados |
| passo lógico Δt | 0,1 | segundos simulados |
| velocidade visual inicial | 10 | x |

Todos configuráveis devem passar por validação; N em [1,500], σ>0, taxas >0 quando aplicáveis, λB>=0, tempos >=0, a<=c<=b, L<H, 0<=p<=1, Δt>0. Desligar bloqueios = λB=0. Permitir valores-padrão mas não afirmar calibração empírica.

## 4. Estados por bagagem
`Scheduled` -> `WaitingEntry` (se entrada ocorrer com tapete bloqueado) -> `Transporting` -> `ScannerQueue` -> `Scanning` -> `Approved` OU `Flagged`.
`Scheduled` pode passar diretamente a `Transporting` quando não está bloqueado. `Approved`/`Flagged` são terminais. Cada mala recebe ID único [1,N]. Nunca retroceder de estado nem terminar duas vezes.

## 5. Chegadas
- Primeiro saco no tempo t=0; criar exatamente N, sem novas chegadas depois.
- `Constant`: t_i=(i-1)*d para d>0. Para d=0, rejeitar configuração (evita burst sem necessidade).
- `Exponential`: t_1=0, t_(i+1)=t_i+Exp(λA), λA=1/d.
- Durante bloqueio, malas chegadas aguardam em `WaitingEntry` e entram em FIFO ao retomar; intervalos aleatórios sorteados continuam a ser registrados para validação independente.

## 6. Transporte
- Cada mala tem `NominalTransportSeconds ~ Triangular(a,c,b)`, sorteado UMA vez no início do transporte (ou na criação, mas documentar a escolha; v1 = criação).
- Tapete aceita múltiplas malas simultâneas (capacidade simplificada), sem colisões e sem limites físicos.
- O progresso de transporte aumenta com Δt *apenas* quando tapete não está bloqueado; atingir `NominalTransportSeconds` move mala para `ScannerQueue`.
- `ObservedTransportElapsedSeconds` pode ser maior devido a bloqueios; manter métricas distintas. Atualizar apenas o tempo nominal realizado em ticks de Δt; tempo gráfico intercala posições.
- Quando várias malas chegam à fila no mesmo passo, desempatar por instante de conclusão e depois ID.

## 7. Scanner e triagem
- Apenas um scanner; uma mala por vez; fila FIFO.
- Quando livre, retirar o primeiro elemento da fila e atribuir `InspectionSeconds ~ Exponential(λI)` UMA vez; contar serviço conforme o passo lógico.
- Ao completar, sortear `Bernoulli(pFlag)` UMA vez; `true` -> `Flagged`, senão `Approved`, e iniciar próxima mala se houver.
- O peso não afeta probabilidade pFlag nem tempo de scanner nesta versão: condição necessária para Binomial(n,p) homogénea.
- Bloqueio do tapete **não interrompe** uma inspeção em curso.

## 8. Bloqueios
- Processo Poisson homogéneo em tempo lógico de calendário; gerar sucessivamente tempos de ocorrência por intervalos exponenciais Exp(λB).
- Uma ocorrência provoca estado de tapete parado até pelo menos `event_time + fixedDuration`.
- Se ocorrer durante uma interrupção, **contar a nova ocorrência**, atualizar `blockEnd = max(blockEnd, event_time+duration)`; não criar uma segunda paragem sobreposta visualmente.
- O scanner mantém funcionamento; bagagens no tapete param; novas entradas aguardam.
- Eventos de bloqueio registados com instante matemático sorteado e instante de aplicação no tick lógico, se forem diferentes. Na validação de Poisson usar instantes *sorteados* e janelas de observação FIXAS.
- Fim normal ao concluir N malas; não continuar só para completar uma janela Poisson.

## 9. Motor temporal
- Passo lógico fixo, recomendado 0.1s; animação interpolada por frame e independente dos sorteios.
- A velocidade 1x/5x/10x/20x determina número de passos lógicos acumulados por tempo real; não altera Δt nem ordem dos sorteios.
- Ciclo de tick determinístico: avançar relógio; aplicar chegadas/bloqueios devidos; atualizar tapete/scanner; terminar etapas; libertar scanner/iniciar próximo. Documentar ordem exata e manter estável.
- `Pause` não avança tempo nem RNG. `Reset` recria estado e streams com seed.
- Limite de segurança simulado (ex. 3h) e limite de passos para detectar configurações impossíveis; comunicar erro em vez de loop infinito. A duração total é variável.

## 10. Observabilidade e exportação
- Um registo BagRecord por bagagem, com atributos sorteados, timestamps de estados, tempo observado no tapete, espera e resultado.
- Um registo por evento operacional, incluindo tipo, instante, ID (se existir) e contexto. Não registrar um bloqueio duas vezes.
- CSVs: `bags.csv`, `events.csv`; `config.json` com parâmetros, seed, versão e notas de origem dos parâmetros; opcional `summary.csv`.
- Escrita com `InvariantCulture` e UTF-8; cabeçalhos estáveis; valores ausentes vazios e campos escapados corretamente.
- Pasta de exportação selecionada com caminho permitido no Editor (ex. `Application.persistentDataPath`); UI indica localização resultante.

## 11. Métricas
`Created`, `WaitingEntry`, `OnBelt`, `QueueLength`, `Scanning`, `Approved`, `Flagged`, `Completed`, `BlockEvents`, `BlockedTime`, `SimTime`, `MeanQueueWait`, `MeanTotalTime`, `ScannerBusyFraction`. Todos calculados dos eventos/registos, sem RNG extra.

Invariantes: `0<=Completed<=Created<=N`; `Completed=Approved+Flagged`; cada bagagem em exatamente um estado; nunca mais de 1 a digitalizar; ao completar `Completed=N` e nenhum estado transitório; timestamps não decrescentes; tempo de fila >=0; intervalos nominal TT nos limites e pesos [L,H]; mesma seed/parâmetros/versão = sequência equivalente numa execução com o mesmo Δt.

## 12. Exclusões explícitas
Sem 3D, física de colisão, passageiros, voos, inventário, identificação real de bagagens, inspeção manual, múltiplos scanners, base de dados, rede, inteligência artificial operacional ou alegações de previsão real de aeroportos. Sem mudar distribuição aprovada por conveniência.

## 13. Experiências para demonstração
A: baixa procura (intervalos de chegada maiores). B: procura elevada (intervalos menores). C: bloqueios frequentes (λB maior). Cada cenário é guardado com seed e configuração; comparar fluxos/tempos; não interpretar uma execução isolada como conclusão estatística. Para inferência, repetir seeds.
