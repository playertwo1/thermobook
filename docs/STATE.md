# Estado atual

> Atualize este arquivo no mesmo PR de cada subfase concluída. Mantenha curto: o histórico fica no git.

**Atualizado em:** 2026-11-18
**Versão publicada:** v0.1.0 (fases 0–6)
**Em andamento:** fase 7 (7.1 a 7.4 feitas, falta conferir no notebook); fase 8 (8.1 a 8.3 feitas, falta verificar no notebook)
**Próxima subfase:** [9.1 Janela base da pílula](ROADMAP.md#91-janela-base)

## Progresso da v0.2

| Fase | Status |
|---|---|
| 7 Motor de energia e modos | 🟨 em andamento (7.1–7.4 feitas, falta conferir no notebook) |
| 8 Segundo plano e desligar tela | 🟨 em andamento (8.1–8.3 feitas, falta verificar no notebook) |
| 9 Pílula flutuante | ⬜ não iniciada |
| 10 Painel rápido e flyout | ⬜ não iniciada |
| 11 Ferramentas com administrador | ⬜ não iniciada |
| 12 Painel em abas, instalador, release | ⬜ não iniciada |

Legenda: ⬜ não iniciada · 🟨 em andamento · ✅ no `main` · ⚠️ no `main` mas falta verificar no notebook

## Decisões tomadas (v0.2)

| # | Decisão | Por quê |
|---|---|---|
| D1 | Pílula feita em WinForms, no mesmo processo | Sem dependência nova; o app já é WinForms e o `Snapshot` já existe. |
| D2 | Catálogo de 6 modos: Economia, Frio, Eficiência, Equilibrado, Alto, Máximo | Junta as 5 opções do `.bat` com o perfil Frio, que é o que mais esfria. Silencioso → Economia e Normal → Eficiência na migração. |
| D3 | Modos aplicados no **plano ativo**, não num GUID fixo | O `.bat` usa `859668fb-…`, que só existe nesta máquina. |
| D4 | Segundo plano restaura o **snapshot** do que havia antes, não um padrão fixo | O `Restaurar_Plano_Padrao.bat` força o plano Equilibrado e "suspender", o que pode não ser o que o usuário tinha. |
| D5 | Limpeza/otimização num processo elevado separado, com lista fechada de tarefas | O app principal nunca roda como administrador. |
| D6 | Esvaziar Lixeira e remover apps desmarcados por padrão | Não têm volta. |
| D7 | Atalho para desligar a tela é `Ctrl+Alt+Shift+D`, configurável em `settings.json` | Proposta A2 aceita por falta de objeção. |

## Decisões em aberto

Confirmar com o dono antes da subfase indicada. Se não houver resposta, siga a proposta.

| # | Pergunta | Proposta | Antes de |
|---|---|---|---|
| A1 | Os valores do modo **Frio** (além do turbo desligado) | Iguais ao Equilibrado | 7.2 |
| A3 | Posição inicial da pílula | Borda direita, centro vertical, monitor principal | 9.3 |
| A4 | Mostrar a pílula logo na primeira execução | Sim | 9.4 |

## Riscos ativos

- **GPU Intel Arc sem temperatura** sem driver de kernel: a pílula mostra só uso. Aceito.
- **Overlay do modo de energia** só funciona com o plano Equilibrado ativo. Fora dele, os valores do processador são gravados direto e o overlay é ignorado.
- **Verificação real exige o notebook** (tampa, UAC, `powercfg /qh`): CI só cobre build e testes.
- **Modo segundo plano mexe na tampa/suspensão**: se algo falhar no meio, o snapshot em `settings.json` (`BackgroundPlans`) guarda os valores originais por plano; desligar o modo restaura.

## Notas da última sessão

- 8.3: `BackgroundMode` + menu + ponto azul no ícone. Não testei fechar a tampa.
- 8.2: `HotkeyService` + `HotkeyWindow`; `Settings.Hotkey` novo com padrão, compatível com settings antigos.
- 8.1: `DisplayService.TurnOff()` + item no menu da bandeja. Não testei desligar a tela de verdade na sessão do agente.
- 7.4: `--diagnostico` imprime tabela atual x esperado (`PowerDiagnostics`). Observação: o `--diagnostico` só imprime no console quando chamado de um terminal que o anexe (ex.: PowerShell `& ThermoBook.exe --diagnostico | Out-String`).
- 7.1: `PowerService` agora tem `Read`/`Write`/`Commit` genéricos (AC+DC), constantes de GUID, overlay (`GetOverlay`/`SetOverlay`); configuração ausente retorna `null`/`false`. Overlay GUIDs de memória (Max Performance `3af9b8d9-…` não conferido). `Apply(turbo, limit)` da v0.1 continua igual.
- 7.3: `PlanWatcher` (janela invisível no `TrayApp`) reaplica o modo salvo quando o plano ativo muda, só se o GUID do plano mudar (evita laço com o `Commit()` do próprio app). `Current()` compara também a CPU mínima. Rótulo "Personalizado".
- 7.2: catálogo `PowerService.Modes` (6 modos), `ModeByName` com migração Silencioso→Economia / Normal→Eficiência, `Current()` compara turbo+limite+EPP, menu e painel listam os 6 modos. Desinstalador (`--restaurar`) agora aplica Eficiência.
- Antes: criados `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `PRODUCT.md`, `DESIGN.md`, `docs/ROADMAP.md`, `docs/INDEX.md` e este arquivo. Nenhum código mudou.
