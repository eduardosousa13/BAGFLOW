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
