# DATA_SOURCES — rastreabilidade de parâmetros

**Regra:** os valores didáticos são pressupostos ilustrativos; não tratá-los como medições reais. O professor aprovou o tema/modelos, não uma calibração operacional real.

| Parâmetro | Valor v1 | Classe | Fonte/evidência | Ação |
|---|---:|---|---|---|
| N bagagens | 60 | configurável | desenho didático do grupo | manter |
| intervalo chegadas | 5 s | pressuposto | desenho didático | pesquisar intervalos operacionais comparáveis se existirem |
| transporte (a,c,b) | 6,9,13 s | pressuposto | desenho didático | pesquisar e documentar geometria hipotética |
| serviço scanner | média 8 s | pressuposto | desenho didático | buscar literatura de sistemas de bagagens |
| peso base μ,σ | 18,4 kg | pressuposto | desenho didático | distinguir de regras de franquia aérea |
| truncamento peso | [5,32] kg | pressuposto | desenho didático | explicar limites do modelo |
| limiar alto | 23 kg | convenção visual | não equivale a política universal | sem efeitos na triagem |
| p sinalização | 0,08 | pressuposto | desenho didático | evitar alegar frequência real de inspeção |
| bloqueios λB | 1/120 s^-1 | pressuposto | desenho didático | dados mecânicos públicos escassos; manter como cenário |
| duração bloqueio | 8 s | pressuposto | desenho didático | pode variar por cenário, não sorteio |

## Fontes académicas fornecidas
- ISCTE, Aula 0: estrutura de projeto e requisitos históricos (nota do docente prevalece).
- ISCTE, Aula 1 (2026/27): FMP, FDPC, binomial e Poisson, hipóteses de processos.
- ISCTE, Aula 2 (2026/27): variáveis contínuas, FDP, integração/normalização e funções degrau.
- ISCTE, Formulario-PI.pdf: fórmulas de referência.
- Richard Saucier (2000), *Computer Generation of Statistical Distributions*, Army Research Laboratory: algoritmos de geração.
- Notebook VariaveisPickEm e Wild West Conquerors: referências de integração, exemplos - não copiar cegamente (notebook contém apenas binomial e exponencial).

## Fontes externas a consultar/verificar
- IATA Baggage Tracking / Resolution 753: https://www.iata.org/en/programs/ops-infra/baggage/baggage-tracking/
- IATA Baggage Standards: https://www.iata.org/en/programs/ops-infra/baggage/standards/
- IATA Baggage Reference Manual (pode não estar livremente acessível): https://www.iata.org/en/publications/manuals/baggage-reference-manual/
- Literatura científica sobre tempo de atendimento e congestionamento em baggage handling systems, recolhendo autores, DOI e contexto do processo real.

## Procedimento de pesquisa
Para cada fonte: guardar URL/DOI, entidade, ano, acesso, local/contexto, definição da variável, população/amostra, valor+unidade, qualidade, compatibilidade com o modelo. Rejeitar comparações espúrias: taxa de malas extraviadas ≠ taxa de avarias do tapete; peso permitido numa companhia ≠ distribuição real de pesos; tempo de security screening de passageiros ≠ tempo do scanner de bagagens de porão.

## Registo M5 por parâmetro (outubro 2026)

Valores = `SimulationConfig` aprovado (não alterados). Classe: **real** (medido/normativo), **aproximação** (ordem de grandeza compatível com fonte), **didático** (pressuposto do grupo). Nenhum valor foi calibrado com dados operacionais de um aeroporto.

| Campo | Significado | Valor | Unidade | Justificação | Fonte | Classe |
|---|---|---:|---|---|---|---|
| `BaggageCount` | malas por execução (N da Binomial) | 60 | malas | dimensão legível na demonstração; ≤500 por validação do controlador | — | didático |
| `ArrivalMode` / `MeanArrivalIntervalSeconds` | chegada à entrada do tapete (constante ou Exp) | Constant / 5 | s | ritmo regular para visualização; não é uma das 5 VA | — | didático |
| `TransportMin/Mode/MaxSeconds` (a,c,b) | tempo nominal no tapete, Triangular | 6 / 9 / 13 | s | triangular é usual quando só há mínimo, moda e máximo estimados; troço curto hipotético | forma/algoritmo: Saucier (2000) | didático |
| `MeanInspectionSeconds` (1/λI) | duração da inspeção, Exponencial | 8 | s | 8 s ⇒ 450 malas/h, dentro da ordem de grandeza de EDS de bagagem de porão (fabricantes anunciam até 1 800 malas/h em linha, a 0,5 m/s); valor não é medição | Smiths Detection HI-SCAN 10080 XCT (alegação do fabricante); Analogic high-speed EDS (fabricante) | aproximação (ordem de grandeza) / exponencial é pressuposto didático |
| `WeightBaseMeanKg`, `WeightBaseStdKg` (μ,σ) | normal base do peso | 18 / 4 | kg | média abaixo da recomendação de 23 kg por volume | HSE (IATA AHM): recomendação de 23 kg sem acordo prévio | didático (não há distribuição real de pesos citada) |
| `MinWeightKg`, `MaxWeightKg` (L,H) | truncatura do peso | 5 / 32 | kg | H = 32 kg coincide com o limite que algumas companhias aceitam; L é pressuposto | HSE: "some airlines will accept baggage up to 32kg" | H: normativo/aproximação; L: didático |
| `HeavyWeightThresholdKg` | limiar "heavy" (só visual) | 23 | kg | etiqueta "heavy" obrigatória acima de 23 kg segundo a orientação citada pelo HSE; sem efeito na triagem | HSE (IATA Airport Handling Manual) | real (convenção de manuseamento) |
| `FlagProbability` (p) | probabilidade de sinalização, Bernoulli/Binomial | 0,08 | — | não existe taxa pública fiável de alarme por mala; não alegar frequência real | — | didático |
| `BlockageRatePerSecond` (λB) | taxa de ocorrências de bloqueio, Poisson | 1/120 | s⁻¹ | ~1 ocorrência por 2 min para ser visível na demonstração; dados públicos de falhas de tapetes escassos | — | didático |
| `BlockageDurationSeconds` (d) | paragem fixa por ocorrência | 8 | s | cenário; não é sorteada | — | didático |
| `SimulationStepSeconds` (Δt) | passo lógico | 0,1 | s | resolução temporal do motor | — | técnico |
| `MaxSimulatedSeconds` | horizonte de segurança | 10 800 | s | limite contra ciclos sem fim | — | técnico |
| `Seed` | semente principal | 42 | — | reprodutibilidade (SplitMix64-open52-v1) | — | técnico |

Fontes consultadas em 22/10/2026:
- UK Health and Safety Executive, *Air transport FAQs* (cita o IATA Airport Handling Manual): https://hse.gov.uk/airtransport/faqs.htm — 23 kg por volume sem acordo prévio, algumas companhias aceitam até 32 kg, etiqueta "heavy" acima de 23 kg.
- Smiths Detection, caso de estudo HI-SCAN 10080 XCT: https://www.smithsdetection.com/case-study/making-room-for-hold-baggage-screening-in-limited-space — "up to 1,800 bags per hour", tapete a 0,5 m/s (alegação do fabricante).
- Analogic, EDS de bagagem de porão de alta velocidade: https://www.analogic.com/imaging-and-detection/checked-baggage/high-speed/ (alegação do fabricante).
- R. Saucier (2000), *Computer Generation of Statistical Distributions*, ARL-TR-2168, Army Research Laboratory, DOI 10.21236/ADA374109: https://apps.dtic.mil/sti/pdfs/ADA374109.pdf — algoritmos de geração (inversão triangular e exponencial, Box–Muller).

Comparações a evitar (mantidas da regra acima): débito anunciado por um fabricante ≠ tempo médio de serviço num aeroporto concreto; limite de peso de manuseamento ≠ distribuição de pesos de passageiros; nenhuma fonte encontrada justifica p = 0,08 ou λB = 1/120 s⁻¹ como valores reais.
