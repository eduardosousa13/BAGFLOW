# BAGFLOW — checklist de integração do coordenador

## Verificações antes de copiar
- `git status` limpo na `main` e `git remote -v` para `eduardosousa13/BAGFLOW`.
- `Assets/Scenes/MainSimulation.unity` existe; Unity Editor 6000.6.7f1.
- O pacote contém apenas documentação, código de contratos e testes de referência; não tem `ProjectSettings`, `Packages` ou `.gitignore`.

## Verificações depois de copiar
- Na raiz: `AGENTS.md`, `README.md`, pasta `docs/`, pasta `validation/`, `Assets/Scripts/Contracts/SimulationContracts.cs`.
- Nenhum `CLAUDE.md` obrigatório; Claude Code deve ser instruído a ler `AGENTS.md`.
- Reabrir o Unity e deixar concluir a importação: confirmar Console sem erros e cena `MainSimulation` guardada.
- `git status --short` mostra apenas os ficheiros esperados e `.meta` novos das pastas Unity; **não** deve mostrar caches `Library/` ou `Temp/`.
- Fazer commit e push da base antes de colegas criarem branches.
- Colegas devem clonar após este commit e usar uma instalação local do Editor **na mesma versão**, mas própria para macOS ou Windows.

## Não fazer
- Não substituir ficheiros atuais em `ProjectSettings/` ou `Packages/`.
- Não instalar Unity VCS em paralelo com GitHub.
- Não criar outro projeto Unity dentro do atual.
- Não deixar assistentes editar `MainSimulation.unity` em paralelo.
- Não misturar branches antes de validar compilação e contratos.

## V3 — correções de coordenação
- [ ] `UNITY_HANDOFF.md` lido por M1 e M4; interface e nomes exatos reconciliados.
- [ ] `TransportProgress01`, `ScanProgress01`, `QueuePosition` visíveis a partir do motor, sem cálculo de estados na UI.
- [ ] Reset recria RNG provider a partir da seed, não reutiliza instância.
- [ ] Confirmar que README existente permaneceu inalterado ao extrair o ZIP.
- [ ] Não versionar __pycache__ nem *.pyc.
