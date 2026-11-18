# Índice

Mapa do repositório. Se criar ou mover um arquivo importante, atualize aqui.

## Documentos

| Arquivo | Para quê |
|---|---|
| [AGENTS.md](../AGENTS.md) | Regras para agentes (Claude Code, Codex, Antigravity). Comece por aqui. |
| [CLAUDE.md](../CLAUDE.md) / [GEMINI.md](../GEMINI.md) | Só apontam para o AGENTS.md. |
| [PRODUCT.md](../PRODUCT.md) | O que o app é, para quem, o que entra e o que fica de fora. |
| [DESIGN.md](../DESIGN.md) | Cores, tipografia, pílula, flyout, painel rápido, comportamento. |
| [docs/ROADMAP.md](ROADMAP.md) | Fases e subfases com critério de pronto. |
| [docs/STATE.md](STATE.md) | Onde estamos, decisões tomadas e em aberto, riscos. |
| [docs/PLANO.md](PLANO.md) | Plano e decisões da v0.1 (histórico). |
| [README.md](../README.md) | Apresentação e como gerar o instalador. |

## Código

| Caminho | O que faz |
|---|---|
| `src/ThermoBook/Program.cs` | Entrada: instância única, `--diagnostico`, inicia o `TrayApp`. |
| `src/ThermoBook/TrayApp.cs` | Ícone da bandeja, menu, ciclo de vida, aplica perfil. Ponto central de estado. |
| `src/ThermoBook/TrayIconRenderer.cs` | Desenha a temperatura no ícone da bandeja. |
| `src/ThermoBook/MainForm.cs` | Painel com cartões e gráficos. |
| `src/ThermoBook/Services/SensorService.cs` | Coleta WMI em thread de fundo → `Snapshot`. |
| `src/ThermoBook/Services/PowerService.cs` | P/Invoke `powrprof.dll`: turbo e limite da CPU (vira genérico na fase 7). |
| `src/ThermoBook/Services/SettingsStore.cs` | `settings.json` em `%AppData%\ThermoBook`. |
| `src/ThermoBook/Services/StartupService.cs` | Run key do "Iniciar com o Windows". |
| `src/ThermoBook/Services/TempAlert.cs` | Lógica do alerta de temperatura. |
| `src/ThermoBook/Services/Cleanup.cs` | Fase 11.2: limpeza de temporários, DNS e Lixeira (processo elevado). |
| `src/ThermoBook/Services/ElevatedTask.cs` | Fase 11: relança o app com UAC (`--tarefa`), lista fechada de tarefas, relatório JSON. |
| `tests/ThermoBook.Tests/CoreTests.cs` | Testes xUnit. |
| `installer/ThermoBook.iss` | Instalador Inno Setup. |
| `.github/workflows/build.yml` | Build e testes em push/PR. |
| `.github/workflows/release.yml` | Tag `v*` → instalador na GitHub Release. |
| `prototype/Monitor_Temperatura.ps1` | Protótipo original em PowerShell (referência). |

## Arquivos previstos na v0.2

| Caminho | Fase |
|---|---|
| `src/ThermoBook/Services/ModeCatalog.cs` | 7.2 |
| `src/ThermoBook/Services/DisplayService.cs` | 8.1 |
| `src/ThermoBook/Services/BackgroundMode.cs` | 8.3 |
| `src/ThermoBook/Pill/PillForm.cs`, `PillLayout.cs`, `PillDocking.cs` | 9 |
| `src/ThermoBook/Pill/PillFlyout.cs`, `QuickPanel.cs` | 10 |
| `src/ThermoBook/Services/Tweaks.cs` | 11.3 |
