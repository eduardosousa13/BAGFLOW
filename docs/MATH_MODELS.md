# MATH_MODELS — cinco distribuições aprovadas

**Convenção:** U uniformemente distribuída em (0,1), independente entre sorteios. FMP = função massa para discreta; FDP = função densidade para contínua; FDPC ou CDF = função de distribuição acumulada F(x)=P(X<=x). Este documento define fórmulas e testes; não substitui a demonstração com código nem valida a adequação empírica a um aeroporto real.

## VA1. Tempo nominal de transporte T_T — Triangular(a,c,b)
Condição a<c<b no perfil ordinário; degenerados devem ser testados/tratados isoladamente, não usar fórmulas com divisão por zero. Unidade s.

f(t)=0 fora de [a,b]; f(t)=2(t-a)/((b-a)(c-a)) para a<=t<c; f(t)=2(b-t)/((b-a)(b-c)) para c<=t<=b.

F(t)=0 para t<a; (t-a)^2/((b-a)(c-a)) para a<=t<c; 1-(b-t)^2/((b-a)(b-c)) para c<=t<b; 1 para t>=b.

Geração por inversão: seja q=(c-a)/(b-a); se U<q, T=a+sqrt(U(b-a)(c-a)); senão, T=b-sqrt((1-U)(b-a)(b-c)).

E[T]=(a+b+c)/3; Var(T)=(a^2+b^2+c^2-ab-ac-bc)/18.

## VA2. Inspeção T_I — Exponencial(λI)
λI>0 em s^-1. f(t)=λI exp(-λI t) para t>=0, senão 0. F(t)=1-exp(-λI t) para t>=0, senão 0.

Geração T= -ln(1-U)/λI. E[T]=1/λI; Var(T)=1/λI^2. Não adicionar mínimo, não fazer clamp: isso mudaria a distribuição aprovada. No motor de passo fixo, duração efetiva pode arredondar à resolução Δt: **validar a amostra bruta**, não a duração discreta efetiva.

## VA3. Peso W — Normal truncada(μ, σ, L, H)
A normal base N(μ,σ²) é condicionada a L<=W<=H, σ>0, L<H. Seja α=(L-μ)/σ, β=(H-μ)/σ, Z=Φ(β)-Φ(α)>0, com φ densidade e Φ cumulativa normal padrão.

f_W(w)=φ((w-μ)/σ)/(σ Z) no intervalo [L,H], senão 0.
F_W(w)=0 para w<L; [Φ((w-μ)/σ)-Φ(α)]/Z para L<=w<=H; 1 para w>H.

E[W]=μ + σ[φ(α)-φ(β)]/Z.
Var(W)=σ²{1+[αφ(α)-βφ(β)]/Z - ([φ(α)-φ(β)]/Z)^2}.

Geração C#: método Box-Muller para normal padrão Z0=sqrt(-2ln U1)*cos(2πU2); candidato μ+σZ0; rejeitar se fora de [L,H]. Gerar U1 estritamente em (0,1); rejeição ilimitada teoricamente, mas implementar limite de segurança e lançar erro claro para configuração extrema, nunca devolver valor forçado por clamp. Para notebook de validação usar SciPy com normal truncada como referência teórica, identificando μ/σ da NORMAL ORIGINAL.

## VA4. Total de sinalizadas N_S — Binomial(n,p)
Para cada bagagem i, A_i em {0,1} Bernoulli(p), sorteio único após concluir inspeção. A_i=1 significa sinalizada, probabilidade p fixa, sem dependência de peso, histórico, congestionamento ou outra bagagem; supor independência.

N_S = Σ_(i=1)^n A_i; FMP P(N_S=k)=C(n,k) p^k (1-p)^(n-k), k=0,...,n.
FDPC F(k)=Σ_(j=0)^floor(k) C(n,j)p^j(1-p)^(n-j) com saturação em 0 para k<0 e 1 para k>=n.
E[N_S]=np; Var(N_S)=np(1-p).

**Validação:** uma execução completa com N bagagens fornece UM valor de N_S. Histograma binomial exige muitas execuções independentes com N e p fixos ou lote isolado de Bernoulli; não confundir 60 resultados Bernoulli com 60 amostras Binomial(60,p).

## VA5. Bloqueios N_B(T) — Poisson(λB T)
Processo homogéneo de ocorrências ao longo do tempo lógico, taxa λB>=0 em bloqueios/s, observação em janela FIXA T>0 em s.

P(N_B(T)=k)=exp(-λB T)(λB T)^k/k!, k=0,1,...; FDPC F(k)=Σ_(j=0)^floor(k) P(j), com limite de suporte correto.
E[N_B(T)]=λB T; Var(N_B(T))=λB T.

Geração operacional: se λB=0 não há eventos; se λB>0, instantes consecutivos t_(m+1)=t_m+Exp(λB), iniciando em t0=0. O tapete para por d segundos cada vez; ocorrências dentro de paragem continuam a contar como novos eventos, e podem prolongá-la para max(end, t_m+d).

**Validação:** observar contagens em janelas de duração FIXA T independentemente da duração variável de cada execução, ou simular processos independentes num horizonte fixo. Não analisar o número total de bloqueios numa execução que termina quando as N malas saem como se tivesse parâmetro fixo λB T constante, porque o tempo de exposição varia (e pode depender dos bloqueios).

## Variáveis auxiliares e derivadas
- Chegadas: modo constante t_i=(i-1)d ou modo estocástico diferenças Exp(1/d), com primeira em t=0. Esta NÃO conta entre as cinco exigidas.
- Espera na fila Wq = t_start_scan - t_enter_scanner_queue >=0; não sortear Wq.
- Tempo total no sistema Ttotal=t_complete - t_arrival >=0; não sortear Ttotal.
- Duração observada no tapete pode superar TT devido a interrupções; comparar a triangular à variável NOMINAL amostrada.

## Testes matemáticos mínimos
1. Normalização: integrais das FDP igual a 1 e somatório das FMP igual a 1 (incluindo limites); FDPC monótona em [0,1].
2. Limites, parâmetros inválidos e valores extremos (p=0,p=1,λB=0).
3. Médias/variâncias: comparar amostras com momentos teóricos, com intervalos de tolerância dependentes do tamanho da amostra; gráficos de histograma/FDP e ECDF/CDF.
4. Binomial: múltiplos lotes de n ensaios; Poisson: múltiplas janelas fixas T.
5. Geradores devem produzir amostras a partir de U, em C#, e exportar amostras brutas para validação externa.

## Fontes/terminologia
Aula 1 (2026/27): FMP, FDPC, Bernoulli/Binomial, Poisson, média, variância. Aula 2: FDP, degrau e regras de normalização. Formulario-PI.pdf e Computer Generation of Statistical Distributions (Saucier, 2000) como referências de fórmulas/algoritmos. Triangular e truncada podem exigir bibliografia complementar porque não estão desenvolvidas nas Aulas 1 e 2.
