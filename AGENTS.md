# AGENTS.md

Instruções para qualquer agente de código neste repositório: **Claude Code, Codex e Antigravity**. Este arquivo é a fonte da verdade. `CLAUDE.md` e `GEMINI.md` só apontam para cá; não coloque regras neles.

## O projeto

ThermoBook: app de bandeja + pílula flutuante para Windows que mostra temperatura, CPU, GPU e RAM e troca modos de energia, modo segundo plano e desliga a tela. C# .NET 10, WinForms, sem dependências além de `System.Management`.

Antes de mexer em qualquer coisa, leia nesta ordem:

1. [docs/STATE.md](docs/STATE.md) — onde estamos e qual é a próxima subfase.
2. [docs/ROADMAP.md](docs/ROADMAP.md) — a subfase que você vai fazer e o critério de "pronto".
3. [PRODUCT.md](PRODUCT.md) e [DESIGN.md](DESIGN.md) — o que o app é e como ele parece.
4. [docs/INDEX.md](docs/INDEX.md) — mapa de arquivos.

## Comandos

```
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/ThermoBook -- --diagnostico
```

Instalador (precisa do Inno Setup 6):

```
dotnet publish src/ThermoBook -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
ISCC installer\ThermoBook.iss
```

## Regras de trabalho

- **Uma subfase do ROADMAP por vez.** Branch `feat/<fase>-<nome-curto>` (ex.: `feat/7.1-power-generico`), PR para `main`. Não junte fases num PR.
- **Ao terminar uma subfase:** marque `[x]` no ROADMAP, atualize o STATE (o que foi feito, próximo passo, decisões novas) no mesmo PR.
- **Pronto = critério da subfase verificado**, não "compila". Se o critério pede o notebook real (tampa, `powercfg /qh`, UAC), diga no PR que não foi verificado e o que falta; não marque `[x]`.
- **Nunca suba a versão nem crie tag por conta própria.** Proponha o número e espere aprovação do dono.
- **Teste** toda lógica nova não trivial (encaixe da pílula, snapshot do segundo plano, migração de settings, parser de argumentos) em `tests/ThermoBook.Tests` (xUnit). Interface e P/Invoke se verificam rodando o app.
- Interface, comentários, mensagens de commit e documentos em **português do Brasil**.

## Regras de arquitetura

- **Sem administrador no app principal.** Só as tarefas da fase 11 pedem UAC, num processo separado (`ThermoBook.exe --tarefa ...`) com lista fechada de tarefas.
- **Sem driver de kernel** (nada de WinRing0, LibreHardwareMonitor, OpenHardwareMonitor).
- **Sem `powercfg.exe`, `reg.exe` ou PowerShell por processo** para o que tem API: energia via `powrprof.dll` (`LibraryImport`), registro via `Microsoft.Win32.Registry`, mensagens via `user32.dll`.
- **Toda mudança no sistema é reversível ou confirmada.** Guarde o valor anterior antes de gravar (no `settings.json`). O que não tem volta fica desmarcado por padrão e pede confirmação.
- **Sensores só na thread de fundo** do `SensorService`; a UI lê `Latest`. Nunca consulte WMI na thread da interface.
- **Um só lugar para o estado** (modo atual, segundo plano) no `TrayApp`; janelas recebem por evento (fase 10.3).
- **Sem dependência nova** sem perguntar. WinForms + P/Invoke resolvem a pílula.
- Configurações em `%AppData%\ThermoBook\settings.json` (record `Settings`); campo novo sempre com valor padrão, para não quebrar o arquivo de quem já usa.

## Papéis sugeridos por agente

Qualquer agente pode fazer qualquer subfase; isto é só onde cada um costuma render mais.

| Agente | Bom para |
|---|---|
| **Claude Code** | Fases com P/Invoke e lógica de sistema (7, 8, 11), revisão de PR, manter ROADMAP/STATE em dia. |
| **Codex** | Subfases bem delimitadas com teste claro (7.2 migração, 9.3 função de encaixe, 11.1 parser), trabalho em paralelo num worktree. |
| **Antigravity** | Interface e verificação visual (9.1, 9.2, 10.1, 10.2, 12.1): rodar o app, tirar print em 100/150/200% de escala, comparar com o DESIGN.md. |

**Trabalho em paralelo:** dois agentes nunca editam o mesmo arquivo ao mesmo tempo. Use um worktree por agente. A fase 9 (pílula, arquivos novos `PillForm*.cs`) pode correr junto da 7/8 (`Services/`). `TrayApp.cs` é ponto de conflito: só um agente por vez.

## Referências

- Scripts originais que o app substitui: `C:\Users\notefael\Desktop\Bats\` (máquina do dono; não estão no repositório). A tabela de valores dos modos já está copiada no ROADMAP 7.2.
- Pílula de referência: OpenUsage para Windows em `~/projetos/openusage` (`Windows/`, `docs/windows.md`, seção "Provider icon notch").
- Plano e decisões da v0.1: [docs/PLANO.md](docs/PLANO.md).
