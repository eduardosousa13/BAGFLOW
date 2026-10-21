# DEFINITION_OF_DONE — critérios de aceitação e integração

## O que é o pacote
Este é um **pacote de especificação e bootstrap**, não o simulador funcional. O código completo será desenvolvido pelos membros no repositório existente. Os PDFs do professor devem estar disponíveis às IAs de cada membro separadamente; não estão neste ZIP.

## Portas para desenvolvimento em paralelo
- [ ] `main` limpa e commit de referência publicado.
- [ ] Unity Editor 6000.6.7f1 em todos os utilizadores que abram o projeto; Universal 2D, cena `MainSimulation.unity` existente.
- [ ] `SimulationContracts.cs` efetivamente compilado no Editor após importação, Console sem erros vermelhos.
- [ ] M1 comunica os nomes de branch e dono de cada pasta; M4 leu `UNITY_HANDOFF.md`.
- [ ] Colaboradores têm permissões; documentação disponibilizada a todos, incluindo o docente e bibliografia relevantes nas suas ferramentas.

## Para qualquer PR
- [ ] Não alterou contratos partilhados, `main`, `MainSimulation.unity` nem ProjectSettings sem aprovação.
- [ ] Compila no Unity (ou reporta que não o pôde executar; não fingir que testou).
- [ ] Inclui entradas/saídas e limitações, nomes de ficheiros, testes executados e exemplos.
- [ ] Não produz cálculos de aleatoriedade fora do gerador/motor.
- [ ] Não tem caches `.pyc`, `Library/`, ficheiros pessoais ou segredos.

## M2 Probabilidade
- [ ] Implementar `IDistributionSampler` e `IRandomStreamProvider` e regra de seed estável; testes de limites, falhas, reprodução e momentos.
- [ ] Integrar transformações corretas; tratar `U=0`, taxas inválidas e caudas da normal truncada sem clamp.
- [ ] Poisson é processo de ocorrências por incrementos exponenciais, não uma segunda contagem aleatória redundante.

## M3 Motor
- [ ] `ISimulationEngine` com dois modos de chegada, N finito, tapete com bloqueios, scanner único, FIFO, triagem e término.
- [ ] Expor `TransportProgress01`, `ScanProgress01` e `QueuePosition` em cópias do snapshot; valores válidos e monotónicos no estado correspondente.
- [ ] Não processar duas malas no scanner; não sinalizar mais do que N; contador de eventos é único.
- [ ] Seeds de teste reproduzem eventos; adicionar limites de segurança para configuração inválida/estagnação.

## M4 UI
- [ ] Implementar as interfaces de `UNITY_HANDOFF.md`; produzir prefabs que M1 possa arrastar para `MainSimulation.unity`.
- [ ] Não editar cena principal; demonstrar estados com snapshots fictícios na cena `VisualSandbox.unity`.
- [ ] Visível: entrada, tapete, FIFO, scanner, aprovado/sinalizado; painel de métricas; pausa, reset, velocidade e exportar.

## M5 Validação
- [ ] Exportar `bags.csv`, `events.csv`, `config.json` com cabeçalhos e tipos especificados.
- [ ] O notebook lê CSV do Unity (não dados fictícios), compara amostras brutas com FDP/FMP e FDPC; Binomial por lotes; Poisson por janelas fixas.
- [ ] Registar fontes reais apenas quando sustentam exatamente a métrica; classificar os restantes parâmetros como didáticos.

## M1 Integração e fecho
- [ ] `main` compila; cena corre N malas e mostra 2 saídas; 1/5/10/20x e seed constante não alteram a lógica.
- [ ] Exports coerentes com contagens do dashboard e sem eventos duplicados.
- [ ] Há evidência dos cinco modelos, histogramas, limitações e mini-relatório/roteiro.
- [ ] Testar reinício, pausa, bloqueios em série, chegada constante e exponencial, erro de configuração e conclusão.
