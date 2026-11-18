# Estado atual

> Atualize este arquivo no mesmo PR de cada subfase concluída. Mantenha curto: o histórico fica no git.

**Atualizado em:** 2026-11-18
**Versão publicada:** v0.1.0 (fases 0–6)
**Em andamento:** fase 7 (7.1 a 7.4 feitas, falta conferir no notebook); fase 8 (8.1 a 8.3 feitas, falta verificar no notebook); fase 9 (9.1–9.4 feitas, falta verificar rodando o app); fase 10 (10.1–10.3 feitas, falta verificar rodando o app); fase 11 (11.1–11.3 feitas, falta rodar no notebook com UAC); fase 12 (12.1, a parte de código da 12.2 e a 12.3 feitas, falta verificar rodando o app e o fluxo instalar → desinstalar numa conta limpa)
**Próxima subfase:** [12.4 Release](ROADMAP.md#124-release) — antes, verificar no notebook o que está pendente nas fases 7–12; o número da versão espera aprovação do dono.

## Progresso da v0.2

| Fase | Status |
|---|---|
| 7 Motor de energia e modos | 🟨 em andamento (7.1–7.4 feitas, falta conferir no notebook) |
| 8 Segundo plano e desligar tela | 🟨 em andamento (8.1–8.3 feitas, falta verificar no notebook) |
| 9 Pílula flutuante | 🟨 em andamento (9.1–9.4 feitas, falta verificar rodando o app) |
| 10 Painel rápido e flyout | 🟨 em andamento (10.1–10.3 feitas, falta verificar rodando o app) |
| 11 Ferramentas com administrador | ⚠️ no `main` mas falta verificar no notebook (11.1–11.3 feitas, falta rodar com UAC) |
| 12 Painel em abas, instalador, release | 🟨 em andamento (12.1 e 12.2 feitas no código; falta verificar rodando o app e desinstalar numa conta limpa) |

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
| D8 | Posição inicial da pílula: borda direita, centro vertical, monitor principal | Proposta A3 aceita por falta de objeção. |
| D9 | Pílula aparece já na primeira execução | Proposta A4 aceita por falta de objeção. |
| D10 | Apps pré-instalados removidos pelo `PackageManager` do WinRT; TFM `net10.0-windows10.0.19041.0` | Única API sem PowerShell. Aceito pelo dono (2026-11-18), mesmo com o .exe ~24 MB maior. |
| D11 | Delivery Optimization pela política de máquina (HKLM, elevado), não pelo valor de HKCU do `.ps1` | O valor de HKCU é ignorado pelo Windows. Decidido com o dono. |
| D12 | Otimização fica no que o `.ps1` faz: sem políticas de telemetria/Copilot/Recall de máquina; `TaskbarDa` entra; `CortanaConsent`, `BackgroundAppGlobalToggle` e `netsh` saem | Decidido com o dono (2026-11-18). |
| D13 | Backup dos ajustes de máquina em `HKLM\SOFTWARE\ThermoBook\Desfazer`, não no `settings.json` | O processo elevado pode ser de outro usuário (UAC com senha de administrador); HKLM só administrador grava. |
| D14 | Abas do painel completo no jeito do OpenUsage (botões de segmento, aba ativa em `raised` + negrito), não o `TabControl` do Windows | O `TabControl` nativo não fica preto. Decidido com o dono (2026-11-18). |
| D15 | Aba Configurações edita tudo (pílula, atalho, alerta em °C e segundos, Frio automático, iniciar com o Windows) e salva na hora | Decidido com o dono (2026-11-18). |
| D16 | Menu da bandeja: os 3 itens de ferramentas viram um "Ferramentas..." que abre a aba; a janela "Otimizar o Windows" deixa de existir | Decidido com o dono (2026-11-18). |
| D17 | Trabalho direto no `main`, sem branch nem PR | Pedido do dono (2026-11-18), a partir da 12.1. |

## Decisões em aberto

Confirmar com o dono antes da subfase indicada. Se não houver resposta, siga a proposta.

| # | Pergunta | Proposta | Antes de |
|---|---|---|---|
| A1 | Os valores do modo **Frio** (além do turbo desligado) | Iguais ao Equilibrado | 7.2 |

## Riscos ativos

- **GPU Intel Arc sem temperatura** sem driver de kernel: a pílula mostra só uso. Aceito.
- **Overlay do modo de energia** só funciona com o plano Equilibrado ativo. Fora dele, os valores do processador são gravados direto e o overlay é ignorado.
- **Verificação real exige o notebook** (tampa, UAC, `powercfg /qh`): CI só cobre build e testes.
- **Modo segundo plano mexe na tampa/suspensão**: se algo falhar no meio, o snapshot em `settings.json` (`BackgroundPlans`) guarda os valores originais por plano; desligar o modo restaura.
- **Fase 11 exige UAC e processo separado**: nada da 11.1–11.3 rodou de verdade com UAC; a otimização real (registro, TRIM, remoção de apps) não foi rodada nesta máquina.

## Notas da última sessão

- 12.3: README, PRODUCT e DESIGN alinhados com o que foi implementado; imagem `docs/img/pilula.png` gerada por um teste temporário (já removido) que desenha o `PillForm` a 200%. Não é print do app rodando.
- 12.2: `Uninstall.Run` (via `--restaurar`): segundo plano desligado com o snapshot, ajustes de HKCU desfeitos, modo Eficiência, e UAC para desfazer os de máquina só se existirem. Resolve a pendência "o desinstalador não desfaz a 11.3". Testado com chave/`settings.json` falsos; instalador compilado. Não rodei instalar → desinstalar (mudaria esta máquina). Sobra a chave vazia `HKLM\SOFTWARE\ThermoBook`.
- Pílula como notch do OpenUsage (pedido do dono depois de ver o app): `FloatingForm` desliga o arredondamento e o contorno claro do DWM e recorta com `Region` própria; `PillDock.Edges`/`Corners` deixam retos os cantos do lado da tela (testado). A borda de 1 px da pílula saiu. Flyout e painel rápido também perdem o contorno do DWM e ficam com os 4 cantos arredondados. Cantos por `Region` não têm antisserrilhado (igual ao OpenUsage). Depois, também a pedido do dono: pílula um pouco maior (40 px de largura, linhas de 32 px, números de 13 px em negrito, medidor de 3 px, ponto de 7 px) e cantos de raio 10 px em vez de cápsula completa; "100°" e "100" conferidos em imagem nas duas orientações.
- 12.1: `MainForm` em abas (Monitor, Modos, Ferramentas, Configurações), `ToolsPage` (era `OptimizeForm`) com a limpeza, `SettingsPage`, `Theme` (cores do DESIGN.md) e `TrayApp.ChangeSettings` como única porta das configurações. `TrayApp.RunTool` saiu (a limpeza agora roda pela aba). Renderizei as 4 abas a 100%; não rodei o app nem a limpeza/otimização real. Pendências fora da 12.1: o desinstalador ainda não desfaz os ajustes da 11.3 (ver na 12.2); aviso CS8765 antigo em `PillForm.cs(260)`.
- 11.3: `Tweaks` + `OptimizeForm` + item "Otimizar o Windows..." na bandeja. Testes só com `HKCU\Software\ThermoBook\Teste\<guid>` (removida no fim). Conferi só leitura a lista de apps e o Defender. Não rodei a otimização real.
- 11.2: `Cleanup` + tarefas `limpeza` e `limpeza-lixeira` + 2 itens no menu da bandeja. Um teste meu chamou `DeleteContents("C:")` e apagou itens da pasta atual (caminho relativo ao disco); corrigido com `IsPathFullyQualified` antes de resolver e teste de regressão. Estrago ficou só na pasta de build. Não rodei a limpeza real.

- 11.1: `ElevatedTask` (`Parse`/`RunElevated`/`LaunchAsync`) + desvio em `Program` antes do mutex + `TrayApp.RunTool` (balão). Tarefas ainda são marcadores; nenhum botão chama `RunTool` (vem na 11.2). Não vi o UAC de verdade.
- 10.3: `ModeStore` + `OnModeStateChanged`; `PublishMode` removido. Warnings antigos de CS0108 corrigidos (`QuickPanel.Highlight`, `PillForm.SnapToEdge`).
- 10.2: `QuickPanel` + clique na pílula + `FloatingForm.CanActivate`. `PublishMode` no `TrayApp` já começa a unificar o estado (10.3 completa com evento). Sem ícones nos modos e sem engrenagem por ora.
- 10.1: `FloatingForm` extraído do `PillForm`; flyout com texto, posição e controlador de tempos. Não passei o mouse de verdade.
- 9.4: `FullscreenGuard`, `ShowPill` e menu de contexto na pílula. Não medi CPU nem testei tela cheia de verdade.
- 9.3: `PillDock` (encaixe/restauração testados) + arrastar, posição por monitor, DPI e mudança de monitores no `PillForm`. Não arrastei a pílula de verdade.
- 9.2: `PillLayout` + desenho completo da pílula (vertical/horizontal), `SetMode` ligado ao `TrayApp`. Cores do ponto do modo escolhidas por mim (DESIGN.md não define): Economia verde, Frio azul, Eficiência ciano, Equilibrado branco, Alto/Máximo roxo, Personalizado cinza.
- 9.1: `PillForm` base + item provisório no menu. Não verifiquei foco/Alt+Tab rodando o app.
- 8.3: `BackgroundMode` + menu + ponto azul no ícone. Não testei fechar a tampa.
- 8.2: `HotkeyService` + `HotkeyWindow`; `Settings.Hotkey` novo com padrão, compatível com settings antigos.
- 8.1: `DisplayService.TurnOff()` + item no menu da bandeja. Não testei desligar a tela de verdade na sessão do agente.
- 7.4: `--diagnostico` imprime tabela atual x esperado (`PowerDiagnostics`). Observação: o `--diagnostico` só imprime no console quando chamado de um terminal que o anexe (ex.: PowerShell `& ThermoBook.exe --diagnostico | Out-String`).
- 7.1: `PowerService` agora tem `Read`/`Write`/`Commit` genéricos (AC+DC), constantes de GUID, overlay (`GetOverlay`/`SetOverlay`); configuração ausente retorna `null`/`false`. Overlay GUIDs de memória (Max Performance `3af9b8d9-…` não conferido). `Apply(turbo, limit)` da v0.1 continua igual.
- 7.3: `PlanWatcher` (janela invisível no `TrayApp`) reaplica o modo salvo quando o plano ativo muda, só se o GUID do plano mudar (evita laço com o `Commit()` do próprio app). `Current()` compara também a CPU mínima. Rótulo "Personalizado".
- 7.2: catálogo `PowerService.Modes` (6 modos), `ModeByName` com migração Silencioso→Economia / Normal→Eficiência, `Current()` compara turbo+limite+EPP, menu e painel listam os 6 modos. Desinstalador (`--restaurar`) agora aplica Eficiência.
- Antes: criados `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `PRODUCT.md`, `DESIGN.md`, `docs/ROADMAP.md`, `docs/INDEX.md` e este arquivo. Nenhum código mudou.
