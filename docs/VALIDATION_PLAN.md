# VALIDATION_PLAN — evidências para apresentação

## Separar três objetos de teste
1. **Amostrador:** valores brutos de cada distribuição seguem a lei matemática indicada.
2. **Motor:** as regras operacionais são coerentes; tempos observados de fila/transporte diferem das amostras primitivas.
3. **Representação Unity:** ecrã corresponde ao estado real do motor, sem decisões adicionais.

## Amostras mínimas para gráficos
Para triangular, exponencial e normal truncada: 10 000+ sorteios independentes por configuração, exportados de sampler sem arredondamento. Binomial: 2 000+ lotes independentes de tamanho N fixo com p fixo, para obter N_S por lote. Poisson: 2 000+ janelas independentes de duração fixa Δ com taxa constante. Ajustar número a performance/erros. Não exigir coincidência ponto a ponto entre histograma e FDP.

## Gráficos necessários
- Triangular, Exponencial, Normal truncada: histogramas com normalização em densidade, curvas FDP; CDF empírica e teórica.
- Binomial, Poisson: frequências relativas por inteiro e FMP teórica; CDF empírica e teórica.
- Tabela de média e variância teóricas vs observadas, seed, N, parâmetros e tamanho de amostra.
- Série temporal do tamanho da fila e estatísticas comparativas de presets (opcional recomendado).

## Testes de motor
- N=1 e N=60 e N=500; termina sem erro.
- p=0 => nenhum flagged; p=1 => todos flagged.
- λB=0 => 0 bloqueios, transporte não interrompido.
- Forçar λB elevado => eventos em paragem contam, não há paragens múltiplas contraditórias.
- Mesmo seed/config/mesma versão => mesmo CSV lógico, independentemente da taxa de playback.
- Fila FIFO sem dois scanners simultâneos.
- `Completed=Approved+Flagged`, `Created=N`, `Completed=N` após terminar.
- Peso dentro de [L,H], tempo TT bruto em [a,b], tempos/fila >=0.
- Quando uma mala é sinalizada, registo, UI e CSV concordam.
- Verificar pause/reset, export, leitura de CSV, comportamento em Windows e Mac.

## Critérios de robustez
Todos os erros de validação mostram mensagem clara; não aceitar NaN, Inf nem loops ilimitados. Para qualquer estatística, declarar n e unidade. Sem dados suficientes, explicar limitações de amostra e correlação temporal.
