# Índice

Mapa do repositório. Se criar ou mover um arquivo importante, atualize aqui.

## Documentos

| Arquivo | Para quê |
|---|---|
| [AGENTS.md](../AGENTS.md) | Regras para agentes (Claude Code, Codex, Antigravity). Comece por aqui. |
| [CLAUDE.md](../CLAUDE.md) / [GEMINI.md](../GEMINI.md) | Só apontam para o AGENTS.md. |
| [PRODUCT.md](../PRODUCT.md) | O que o app é, para quem, o que entra e o que fica de fora. |
| [DESIGN.md](../DESIGN.md) | Cores, tipografia, pílula, flyout, painel rápido, comportamento. |
| [docs/ROADMAP.md](ROADMAP.md) | Fases 13–18 (próxima versão) e subfases com critério de pronto. |
| [docs/archive/](archive/) | ROADMAP e STATE da v0.2 (fases 0–12), arquivados. Tabela de valores dos 6 modos no ROADMAP arquivado, 7.2. |
| [docs/STATE.md](STATE.md) | Onde estamos, decisões tomadas e em aberto, riscos. |
| [docs/PLANO.md](PLANO.md) | Plano e decisões da v0.1 (histórico). |
| [README.md](../README.md) | Apresentação e como gerar o instalador. |
| [docs/VERIFICACAO.md](VERIFICACAO.md) | Checklist de verificação da v0.2 no notebook (todos os "falta verificar" das fases 7–12, em ordem). |
| `docs/img/pilula.png` | Imagem da pílula (vertical e horizontal, 200%), usada no README e no DESIGN. Gerada pelo `OnPaint` real do `PillForm`. |
| `docs/img/banner.png`, `flyout.png`, `painel-rapido.png`, `painel-*.png` | Banner e capturas do README: desenho real das janelas (`DrawToBitmap`) com dados de exemplo, gerados por um teste temporário. Refazer quando o visual mudar. |

## Código

| Caminho | O que faz |
|---|---|
| `src/ThermoBook/Program.cs` | Entrada: instância única, `--diagnostico`, inicia o `TrayApp`. |
| `src/ThermoBook/TrayApp.cs` | Ícone da bandeja, menu, ciclo de vida, aplica perfil. Ponto central de estado. |
| `src/ThermoBook/TrayIconRenderer.cs` | Desenha a temperatura no ícone da bandeja. |
| `src/ThermoBook/MainForm.cs` | Painel completo em abas (12.1): Monitor (cartões e gráficos) e Modos; recebe Ferramentas e Configurações prontas. |
| `src/ThermoBook/SettingsPage.cs` | Fase 12.1: aba Configurações (pílula, atalho, alerta, iniciar com o Windows). |
| `src/ThermoBook/Theme.cs` | Cores do DESIGN.md e botão escuro das abas. |
| `src/ThermoBook/QuickPanel.cs`, `PillForm.cs`, `PillLayout.cs`, `PillDock.cs`, `Flyout.cs`, `FloatingForm.cs` | Fases 9 e 10: pílula, flyout e painel rápido. |
| `src/ThermoBook/ModeStore.cs` | Fase 10.3: único lugar do estado do modo e do segundo plano. |
| `src/ThermoBook/Services/SensorService.cs` | Coleta WMI em thread de fundo → `Snapshot`. |
| `src/ThermoBook/Services/PowerService.cs` | P/Invoke `powrprof.dll`: turbo e limite da CPU (vira genérico na fase 7). |
| `src/ThermoBook/Services/SettingsStore.cs` | `settings.json` em `%AppData%\ThermoBook`. |
| `src/ThermoBook/Services/StartupService.cs` | Run key do "Iniciar com o Windows". |
| `src/ThermoBook/Services/TempAlert.cs` | Lógica do alerta de temperatura. |
| `src/ThermoBook/Services/Cleanup.cs` | Fase 11.2: limpeza de temporários, DNS e Lixeira (processo elevado). |
| `src/ThermoBook/Services/ElevatedTask.cs` | Fase 11: relança o app com UAC (`--tarefa`), lista fechada de tarefas, relatório JSON. |
| `src/ThermoBook/Services/Tweaks.cs` | Fase 11.3: ajustes de registro com backup e Desfazer, TRIM, status do Defender, apps pré-instalados. |
| `src/ThermoBook/Services/Uninstall.cs` | Fase 12.2: o que o desinstalador desfaz (`--restaurar`): segundo plano, ajustes da otimização, modo Eficiência. |
| `src/ThermoBook/Services/AutoMode.cs` | Fase 13: regras do modo automático (tomada/bateria, programa aberto, temperatura alta) e decisão de quando trocar. |
| `src/ThermoBook/Services/CleanupSchedule.cs` | Fase 14: limpeza agendada (horário, definição da tarefa, Agendador de Tarefas por COM, `limpeza.json`). |
| `src/ThermoBook/Services/HistoryWriter.cs` | Fase 16: histórico em CSV (um minuto por linha, um arquivo por dia, 30 dias). |
| `src/ThermoBook/ToolsPage.cs` | Aba Ferramentas: limpeza (11.2) e otimização do Windows (11.3: itens marcáveis, Aplicar, Desfazer). |
| `tests/ThermoBook.Tests/CoreTests.cs` | Testes xUnit. |
| `installer/ThermoBook.iss` | Instalador Inno Setup. |
| `.github/workflows/build.yml` | Build e testes em push/PR. |
| `.github/workflows/release.yml` | Tag `v*` → instalador na GitHub Release. |
| `prototype/Monitor_Temperatura.ps1` | Protótipo original em PowerShell (referência). |

## Arquivos previstos na próxima versão

| Caminho | Fase |
|---|---|
| `src/ThermoBook/Services/NvidiaSensor.cs` | 15 |
| `src/ThermoBook/Services/UpdateService.cs` | 17 |
