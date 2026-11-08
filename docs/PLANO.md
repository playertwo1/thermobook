# ThermoBook — Plano

App de bandeja para Windows que mostra temperatura e uso de CPU, GPU, RAM e disco e deixa trocar o comportamento do processador (Turbo Boost e limite da CPU) com um clique. Feito para notebooks finos que esquentam, testado num Samsung Galaxy Book6 Pro (Intel Core Ultra, GPU Intel Arc).

## Ponto de partida

O protótipo em PowerShell ([`prototype/Monitor_Temperatura.ps1`](../prototype/Monitor_Temperatura.ps1)) já funciona e validou a ideia:

- Desligar o Turbo Boost (`PERFBOOSTMODE = 0`) baixou a temperatura de **83–86 °C para 54–57 °C** com o mesmo uso.
- Todas as leituras funcionam **sem administrador e sem driver**: contadores de desempenho do Windows via WMI.
- A ventoinha **não pode ser lida nem controlada**: o firmware Samsung só implementa leitura ACPI `_FST` e não expõe nada ao Windows. O caminho é o modo "Alto desempenho" do app Configurações Samsung.

O ThermoBook é a versão de verdade desse protótipo: um `.exe` nativo, com ícone na bandeja e instalador.

## Decisões

| Tema | Escolha | Por quê |
|---|---|---|
| Linguagem / UI | C# .NET 10, WinForms | `NotifyIcon` nativo, sem dependências, o SDK já está instalado. |
| Distribuição | `dotnet publish` self-contained, single-file, win-x64 | O usuário não precisa instalar o runtime do .NET. Custa ~50–70 MB (WinForms não suporta trimming). |
| Sensores | WMI `Win32_PerfFormattedData_*` numa thread de fundo | Igual ao protótipo. Sem admin e sem driver de kernel; LibreHardwareMonitor usa o WinRing0, que antivírus bloqueiam. |
| Controle de energia | P/Invoke em `powrprof.dll` (`PowerReadACValueIndex`, `PowerWriteACValueIndex`/`DCValueIndex`, `PowerSetActiveScheme`) | Mesmo efeito do `powercfg`, sem abrir processo e sem depender do idioma do Windows. |
| Configurações | JSON em `%AppData%\ThermoBook\settings.json` | Simples e legível. |
| Instalador | Inno Setup 6, instalação por usuário em `%LocalAppData%\Programs\ThermoBook` | Sem UAC, com atalho no Menu Iniciar e desinstalador. |
| Release | GitHub Actions: tag `v*` → build → instalador → GitHub Release | Instalador sempre gerado do código do repositório. |

GUIDs de energia usados (subgrupo processador `54533251-82be-4824-96c1-47b60b740d00`):

- `PERFBOOSTMODE` `be337238-0d82-4146-a960-4f3749d470c7`: 0 desligado, 1 ligado, 2 agressivo, 3 eficiência (padrão de fábrica deste notebook).
- `PROCTHROTTLEMAX` `bc5038f7-23e0-4960-96da-33abaf5935ec`: limite da CPU, de 0 a 100%.

## Funcionalidades da v0.1 (MVP)

1. **Ícone na bandeja com a temperatura desenhada** (número verde/laranja/vermelho), atualizado a cada 2 s. Tooltip com CPU, GPU, RAM e temperatura.
2. **Menu do botão direito**:
   - Perfis Frio, Silencioso, Normal e Máximo, com o ativo marcado.
   - Abrir painel.
   - Ventoinha (Configurações Samsung).
   - Iniciar com o Windows.
   - Sair.
3. **Painel** (clique no ícone): versão do protótipo com 6 cartões e gráficos (temperatura, CPU, GPU, RAM, disco, bateria), mais os controles de turbo e de limite da CPU. Fechar a janela só esconde na bandeja.
4. **Alerta de temperatura**: notificação do Windows quando passar do limite (padrão 90 °C por 30 s). Opcionalmente troca sozinho para o perfil Frio.
5. **Lembrar o perfil** escolhido e reaplicar ao iniciar, porque atualizações do Windows e o app da Samsung podem trocar o plano de energia.
6. **Instância única**.

Perfis:

| Perfil | Turbo | Limite da CPU |
|---|---|---|
| Frio | 0 (desligado) | 100% |
| Silencioso | 0 (desligado) | 80% |
| Normal | 3 (eficiência) | 100% |
| Máximo | 2 (agressivo) | 100% |

Fora da v0.1 (ideias para depois):

- Perfil automático por programa (ex.: `ffmpeg` rodando → Frio) ou por tomada/bateria.
- Histórico em CSV.
- Inglês.
- Tema claro.
- Atualização automática pelas Releases.

## Estrutura

```
thermobook/
  src/ThermoBook/
    Program.cs              instância única, inicia o TrayApp
    TrayApp.cs              NotifyIcon, menu, ciclo de vida
    TrayIconRenderer.cs     desenha a temperatura no ícone
    MainForm.cs             painel com cartões e gráficos
    Services/
      SensorService.cs      coleta WMI em thread de fundo, publica Snapshot
      PowerService.cs       P/Invoke powrprof: ler/gravar turbo e limite
      SettingsStore.cs      settings.json
      StartupService.cs     HKCU\...\Run
  tests/ThermoBook.Tests/   xUnit
  installer/ThermoBook.iss  Inno Setup
  assets/thermobook.ico
  prototype/                protótipo PowerShell (referência)
  .github/workflows/        build.yml (PR/push), release.yml (tag v*)
```

## Fases

Cada fase termina com o app compilando e um critério verificável.

| # | Fase | Pronto quando |
|---|---|---|
| 0 | **Esqueleto**: solution, projeto WinForms + testes, `.gitignore`, workflow `build.yml` | `dotnet build` e `dotnet test` verdes no GitHub Actions. |
| 1 | **Núcleo**: `SensorService`, `PowerService`, `SettingsStore`. Testes da agregação de GPU (nomes `..._eng_N_engtype_X`) e da leitura/gravação de configurações | Um modo `--diagnostico` imprime os mesmos valores que o protótipo; `PowerService` lê o turbo atual igual ao `powercfg /qh`. |
| 2 | **Bandeja**: ícone dinâmico, tooltip, menu, troca de perfil | Escolher "Frio" no menu muda o turbo para 0 (conferido com `powercfg`) e o ícone mostra a temperatura caindo. |
| 3 | **Painel**: port dos cartões e gráficos, DPI alto (PerMonitorV2), fechar → bandeja | Fica nítido em 150%; abrir e fechar o painel não para a coleta. |
| 4 | **Alertas e inicialização**: notificação de temperatura, Run key, reaplicar perfil | Reiniciar o PC sobe o app com o perfil salvo; simular temperatura alta dispara a notificação. |
| 5 | **Instalador**: `ThermoBook.iss`, ícone, versão no `.csproj`; o desinstalador volta ao perfil Normal e remove a Run key | Instalar, usar e desinstalar numa conta limpa sem deixar sobras. |
| 6 | **Release v0.1.0**: `release.yml` gera `ThermoBook-Setup-0.1.0.exe` na Release | Baixar o instalador da Release e instalar funciona. |

## Riscos e limites conhecidos

- **SmartScreen**: sem certificado de assinatura de código, o Windows mostra "Editor desconhecido" na instalação. Aceito na v0.1 (certificado custa caro).
- **Temperatura**: vem das zonas térmicas ACPI (TZ00/TZ01), não do sensor exato de cada núcleo. Sem driver de kernel, não há leitura melhor.
- **Ventoinha**: sem leitura de RPM nem controle; só o atalho para o app da Samsung.
- **Plano de energia**: as mudanças valem para o plano ativo. Se outro programa trocar de plano, o app detecta na próxima leitura e reaplica o perfil salvo.
- **Outros notebooks**: turbo e limite funcionam em qualquer Windows com Intel ou AMD. O botão da Samsung só aparece se o app estiver instalado.
