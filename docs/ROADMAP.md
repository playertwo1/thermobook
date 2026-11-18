# ThermoBook — Roadmap

Do app de bandeja (v0.1.0, pronto) até a **pílula flutuante com modos e ferramentas** (v0.2.0). O que é o produto está em [PRODUCT.md](../PRODUCT.md), como ele parece em [DESIGN.md](../DESIGN.md), onde estamos em [STATE.md](STATE.md).

## Como ler

- **Fase** = entrega que pode ir para o `main` sozinha, com o app compilando e os testes verdes.
- **Subfase** = um PR pequeno (ou um commit) dentro da fase. Cada uma tem *o que fazer* e *pronto quando*.
- Uma fase só começa quando a anterior está no `main`, exceto onde a coluna "depende de" diz outra coisa.
- Ao terminar uma subfase, marque `[x]` aqui e atualize [STATE.md](STATE.md).

## Visão geral

| Fase | Nome | Depende de | Versão |
|---|---|---|---|
| 0–6 | Núcleo, bandeja, painel, alertas, instalador, release | — | **v0.1.0 ✅** |
| 7 | Motor de energia e catálogo de modos | 6 | v0.2 |
| 8 | Ações do sistema: segundo plano e desligar tela | 7 | v0.2 |
| 9 | Pílula flutuante | 6 (pode correr junto da 7/8) | v0.2 |
| 10 | Painel rápido e flyout | 8, 9 | v0.2 |
| 11 | Ferramentas com administrador: limpeza e otimização | 7 | v0.2 |
| 12 | Painel completo em abas, instalador e release | 10, 11 | **v0.2.0** |
| — | Backlog | — | depois |

Caminho crítico: **7 → 8 → 10 → 12**. A fase 9 pode ser feita em paralelo (outro agente/worktree) porque só lê o `Snapshot` que já existe; a 11 também, depois da 7.

---

## Fases 0–6 — v0.1.0 ✅

Feitas e publicadas. Detalhes e decisões em [PLANO.md](PLANO.md). Resultado: bandeja com temperatura, 4 perfis (turbo + limite), painel, alerta, iniciar com o Windows, instalador Inno Setup, release por tag.

---

## Fase 7 — Motor de energia e catálogo de modos

**Objetivo:** o app aplica qualquer configuração que o `Ativar_Segundo_Plano.bat` aplica, sem `powercfg` e sem administrador, e os perfis da v0.1 viram um catálogo único de 6 modos.

### 7.1 PowerService genérico
- [x] Trocar os dois GUIDs fixos por leitura/gravação de qualquer `(subgrupo, configuração)` em AC e DC: `Read(sub, setting)`, `Write(sub, setting, ac, dc)`.
- [x] Constantes para todos os GUIDs do `.bat`: `PROCTHROTTLEMIN`, `PROCTHROTTLEMAX`, `PERFBOOSTMODE`, `PERFEPP` (e `PERFEPP1` para núcleos de eficiência), `CPMINCORES`, `CPMAXCORES`, latência de estacionamento, limiares de aumento/redução, `DISKIDLE`, personalidade do plano.
- [x] Overlay do modo de energia do Windows: `PowerSetActiveOverlayScheme` / `PowerGetEffectiveOverlayScheme` via `LibraryImport` (hoje o `.bat` abre um PowerShell só para isso).
- [x] Configuração que não existe no hardware (ex.: `PERFEPP1` num CPU sem núcleos E) é ignorada, não derruba a aplicação do modo.

**Pronto quando:** teste de ida e volta (`Write` → `Read`) passa para cada GUID na máquina de testes; erro de configuração inexistente não lança.

> Feito no PR da 7.1: ida e volta automatizada (xUnit) e GUIDs conferidos contra `powercfg /qh` em uma máquina de desenvolvimento. Falta no notebook de testes: ida e volta com `powercfg /qh` mostrando o valor gravado e o overlay trocando de fato.

### 7.2 Catálogo de modos
- [x] `record Mode(Nome, Descrição, Overlay, valores AC/DC…)` substitui `PowerProfile`. Valores tirados do `.bat`:

| Modo | Overlay | CPU mín AC/DC | CPU máx | Turbo | EPP | Núcleos mín AC/DC | Disco ocioso |
|---|---|---|---|---|---|---|---|
| Economia | melhor eficiência | 5/5 | 70 | 0 desligado | 80 | 25/25 | 300 s |
| Frio | equilibrado | 5/5 | 100 | 0 desligado | 50 | 75/25 | 900 s |
| Eficiência | melhor eficiência | 5/5 | 100 | 3 eficiência | 50 | 25/25 | 600 s |
| Equilibrado | equilibrado | 5/5 | 100 | 1 ligado | 50 | 75/25 | 900 s |
| Alto | melhor desempenho | 100/50 | 100 | 2 agressivo | 25 | 100/75 | nunca |
| Máximo | máximo desempenho | 100/100 | 100 | 2 agressivo | 0 | 100/100 | nunca |

  (Frio = Equilibrado com turbo desligado: é o perfil que baixou 83–86 °C para 54–57 °C na v0.1. Latência e limiares de estacionamento seguem o `.bat` de cada modo.)
- [x] Migração do `settings.json`: `Silencioso → Economia`, `Normal → Eficiência`, `Frio` e `Máximo` mantêm o nome.
- [x] Menu da bandeja passa a listar os 6 modos.

**Pronto quando:** teste de migração cobre os 4 nomes antigos e `null`; aplicar cada modo e conferir com `powercfg /qh` bate com a tabela.

> Feito no PR da 7.2: migração testada (4 nomes + null). Falta no notebook: aplicar cada modo e conferir com `powercfg /qh`. Latência e limiares de estacionamento do `.bat` não foram incluídos (valores não estão no repositório).

### 7.3 Detecção e reaplicação
- [x] `Current()` compara só as configurações que definem o modo (turbo, limites, EPP, overlay); combinação desconhecida = "Personalizado".
- [x] Reaplicar o modo salvo ao iniciar e quando o plano ativo mudar (`RegisterPowerSettingNotification` com `GUID_POWERSCHEME_PERSONALITY`, em vez de checar a cada leitura).

**Pronto quando:** trocar o modo no app de Configurações do Windows faz o ThermoBook mostrar "Personalizado"; trocar de plano de energia faz ele reaplicar o modo salvo.

> Feito no PR da 7.3: `Current()` compara turbo, CPU mín/máx e EPP; o overlay ficou fora (fora do plano Equilibrado ele não pega e o modo nunca bateria). A reaplicação usa `GUID_ACTIVE_POWERSCHEME` em vez de `GUID_POWERSCHEME_PERSONALITY`, porque a personalidade só avisa quando o tipo do plano muda. Falta no notebook: trocar o modo nas Configurações do Windows e ver "Personalizado"; trocar de plano e ver o modo reaplicado.

### 7.4 Diagnóstico
- [x] `ThermoBook.exe --diagnostico` imprime todos os valores do modo atual lado a lado com o esperado.

**Pronto quando:** a saída bate com `powercfg /qh` no notebook de testes.

> Feito no PR da 7.4: tabela atual x esperado (AC/DC) para CPU mín/máx, turbo, EPP, núcleos mínimos, disco ocioso e overlay, comparada ao modo atual (ou ao modo salvo, se o plano for Personalizado). Conferido contra `powercfg /q` numa máquina de desenvolvimento (CPU máxima 70/70 = 0x46). Falta no notebook de testes: conferir a saída completa com `powercfg /qh` depois de aplicar cada modo.

---

## Fase 8 — Ações do sistema

**Objetivo:** substituir o modo segundo plano do `.bat`, o `Restaurar_Plano_Padrao.bat` e o `desligar_tela.bat`.

### 8.1 Desligar tela
- [x] `DisplayService.TurnOff()`: `PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` após 500 ms (mesmo atraso do `.bat`, para o soltar da tecla/clique não religar). `PostMessage` e não `SendMessage`, que pode travar esperando uma janela que não responde.
- [x] Item no menu da bandeja.

**Pronto quando:** clicar desliga a tela e mexer o mouse religa, sem o app travar.

> Feito no PR da 8.1: `DisplayService.TurnOff()` (espera 500 ms numa tarefa e faz `PostMessage` broadcast de `SC_MONITORPOWER`) e item "Desligar a tela" no menu da bandeja. É P/Invoke sem teste automatizado. Falta verificar rodando o app: clicar desliga a tela, mexer o mouse religa, o app não trava.

### 8.2 Atalho global
- [x] `RegisterHotKey` para desligar tela (padrão `Ctrl+Alt+Shift+D`, configurável no `settings.json`). Se o atalho já estiver em uso, avisa uma vez e segue sem ele.

**Pronto quando:** o atalho funciona com outro app em foco.

> Feito no PR da 8.2: `HotkeyService` (parser testado + `RegisterHotKey`) e `HotkeyWindow` no `TrayApp`; atalho em `settings.json` (`Hotkey`, vazio desliga). Se o registro falhar, avisa uma vez com balão e segue sem o atalho. Falta verificar rodando o app: o atalho desliga a tela com outro app em foco, e um atalho já em uso mostra o aviso.

### 8.3 Modo segundo plano
- [x] Ligar: guarda no `settings.json` os valores **atuais** de `LIDACTION`, `STANDBYIDLE`, `HIBERNATEIDLE` (AC e DC) do plano ativo e grava `0` (não fazer nada / nunca).
- [x] Desligar: restaura exatamente os valores guardados (não um padrão fixo como o `.bat` faz) e apaga o snapshot.
- [x] O snapshot fica salvo antes de gravar: se o app fechar ou o PC reiniciar com o modo ligado, ao abrir ele continua ligado e ainda dá para desligar e voltar ao original.
- [x] Se o usuário trocar de plano de energia com o modo ligado, reaplica no plano novo (guardando o snapshot desse plano também).
- [x] Indicador no ícone da bandeja (e depois na pílula, fase 9) e item com check no menu.

**Pronto quando:** testes da lógica de snapshot (ligar → desligar devolve o original; ligar duas vezes não sobrescreve o snapshot); no notebook, fechar a tampa com o modo ligado não suspende, e desligar devolve o que o `powercfg /qh` mostrava antes.

> Feito no PR da 8.3: `BackgroundMode` (lógica pura, testada: ligar → desligar devolve o original, ligar duas vezes não sobrescreve, snapshot salvo antes de gravar, dois planos, configuração ausente, JSON) + item "Modo segundo plano" no menu e pontinho azul no ícone. Ao abrir o app com o modo ligado ele reaplica no plano ativo; ao trocar de plano reaplica no plano novo (usa o `PlanWatcher` da 7.3). GUIDs de tampa/suspensão/hibernação conferidos com `powercfg /qh`. **Falta no notebook (por isso a 8.3 não está 100%)**: fechar a tampa com o modo ligado não suspende; desligar devolve o que o `powercfg /qh` mostrava antes. A pílula (fase 9) ainda não mostra o indicador.

---

## Fase 9 — Pílula flutuante

**Objetivo:** a cápsula do [DESIGN.md](../DESIGN.md#pílula-estado-padrão), sempre visível, presa à borda. Pode começar em paralelo à fase 7 (só depende do `Snapshot` do `SensorService`).

### 9.1 Janela base
- [x] `PillForm`: sem borda, `TopMost`, `ShowInTaskbar = false`, estilos `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (não rouba foco, fora do Alt+Tab).
- [x] Cantos arredondados com `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)` no Windows 11, `Region` como plano B.
- [x] Pintura própria com double buffer; redesenha só quando algum valor arredondado muda.

**Pronto quando:** clicar na pílula com o Bloco de Notas em foco não tira o foco do Bloco de Notas; a pílula não aparece no Alt+Tab.

> Feito no PR da 9.1: `PillForm` (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`, `TopMost`, fora da barra de tarefas), cantos pelo DWM com `Region` como plano B, `PillState` arredondado para só redesenhar quando algo muda, e item provisório "Mostrar pílula" no menu da bandeja (a 9.4 refina). Estilos de janela testados; pintura é mínima (o desenho final é a 9.2). **Falta verificar rodando o app:** clicar na pílula com o Bloco de Notas em foco não tira o foco dele; a pílula não aparece no Alt+Tab. Observação: o atributo de cantos do DWM no Windows 11 arredonda pouco (não é uma cápsula completa); conferir o visual no 9.2.

### 9.2 Conteúdo
- [x] Linhas: temperatura (cor por limiar), CPU, GPU, RAM (número + medidor de 2 px), modo atual (ponto; anel se segundo plano ligado).
- [x] Orientação vertical e horizontal com o mesmo código de layout.
- [x] Nome acessível por linha (`AccessibleObject` próprio).
- [x] Valor indisponível mostra `–`.

**Pronto quando:** teste do layout (dado tamanho e orientação, retângulos das linhas não se sobrepõem e cabem na janela); fica nítida em 100%, 150% e 200%.

> Feito no PR da 9.2: `PillLayout` (um só código para vertical e horizontal, testado: retângulos sem sobreposição e dentro da janela em 100/125/150/200%), linhas de temperatura (cor por limiar), CPU, GPU e RAM com medidor de 2 px, ponto do modo (cor por modo, sem vermelho/laranja; cinza em Personalizado) com anel quando o segundo plano está ligado, `–` para valor indisponível e `AccessibleObject` por linha. Conferi a renderização em imagem (vertical e horizontal, 100%). **Falta verificar rodando o app:** nitidez em 150% e 200% num monitor real; leitor de tela lendo as linhas.

### 9.3 Posição e encaixe
- [x] Arrastar pela pílula inteira (sem usar a barra de título do Windows).
- [x] Ao soltar a até 24 px de uma borda, encaixa; perto de duas, encaixa no canto. Borda lateral → vertical; topo/base → horizontal.
- [x] Salvar posição por monitor no `settings.json`; restaurar ao abrir. Se o monitor sumiu (cabo desligado), volta ao canto padrão do monitor principal (direita, centro vertical).
- [x] Reagir a `SystemEvents.DisplaySettingsChanged` e `WM_DPICHANGED`.

**Pronto quando:** teste da função de encaixe (posição solta + área de trabalho → posição final e orientação) cobre as 4 bordas, os 4 cantos e o meio da tela; desconectar o monitor externo traz a pílula de volta para a tela do notebook.

> Feito no PR da 9.3: `PillDock.Snap`/`Resolve` (lógica pura, testada: 4 bordas, 4 cantos, meio da tela, limite de 24 px, monitor ausente → canto padrão do principal), arrastar pela pílula inteira (sem foco), posição por monitor em `settings.json` (`PillMonitor`/`PillPositions`, chave = nome do dispositivo + resolução), `OnDpiChanged` e `SystemEvents.DisplaySettingsChanged` (reposiciona na thread da interface). **Falta verificar rodando o app:** arrastar e encaixar de verdade, e desligar o monitor externo traz a pílula de volta para a tela do notebook.

### 9.4 Convivência
- [x] Esconder quando um app estiver em tela cheia (`SHQueryUserNotificationState` = `QUNS_BUSY`/`QUNS_RUNNING_D3D_FULL_SCREEN`/`QUNS_PRESENTATION_MODE`), voltar sozinha depois.
- [x] Menu da bandeja: "Mostrar pílula" (check), salvo no `settings.json`. Ligado por padrão.
- [x] Botão direito na pílula abre o mesmo `ContextMenuStrip` da bandeja.

**Pronto quando:** um vídeo em tela cheia no navegador esconde a pílula e sair da tela cheia mostra de novo; a pílula aberta o dia todo fica abaixo de ~1% de CPU.

> Feito no PR da 9.4: `FullscreenGuard` (`SHQueryUserNotificationState`: BUSY, tela cheia D3D e modo apresentação escondem; consulta a cada 2 s; falha na consulta não esconde), item "Mostrar pílula" com check salvo em `settings.json` (`ShowPill`, ligado por padrão), botão direito na pílula abre o menu da bandeja. **Falta verificar rodando o app:** vídeo em tela cheia no navegador esconde a pílula e sair mostra de novo; uso de CPU da pílula aberta o dia todo abaixo de ~1% (não medido); o menu de contexto fecha ao clicar fora (a janela não ativa).

---

## Fase 10 — Painel rápido e flyout

**Objetivo:** trocar modo, ligar segundo plano e desligar a tela sem sair da pílula.

### 10.1 Flyout ao passar o mouse
- [x] Aparece após 300 ms ao lado da pílula, do lado oposto à borda; some 150 ms após o mouse sair.
- [x] Conteúdo: temperatura por zona + "quente/quente demais", GHz e turbo, GPU 3D/vídeo, RAM em GB, disco, modo, segundo plano.
- [x] Também não rouba foco.

**Pronto quando:** passar o mouse mostra o flyout sem piscar a pílula; o flyout nunca sai da área de trabalho (testar com a pílula em cada canto).

> Feito no PR da 10.1: `FloatingForm` (base comum de pílula e flyout: sem foco, fora do Alt+Tab, cantos), `FlyoutText` (zonas de temperatura com normal/quente/quente demais, GHz e turbo, GPU 3D/vídeo, RAM em GB, disco, modo, segundo plano), `FlyoutPlacement` (lado oposto à borda, sempre dentro da área de trabalho; testado nos 4 cantos nas duas orientações), `PillFlyout` (abre 300 ms depois de o mouse entrar, fecha 150 ms depois de sair; arrastar ou clicar fecha). Renderizei o flyout em imagem e conferi. **Falta verificar rodando o app:** passar o mouse mostra o flyout sem piscar a pílula; o tempo de 300/150 ms na prática.

### 10.2 Painel rápido ao clicar
- [x] Cartão de 280 px ancorado à pílula: 6 modos segmentados, botões *Segundo plano*, *Desligar tela*, *Ferramentas…*, rodapé com "Abrir painel completo".
- [x] Este cartão **pode** receber foco (precisa de teclado): Tab/setas/Enter/Esc. Fecha ao clicar fora (`Deactivate`).
- [x] Trocar modo aqui chama o mesmo caminho do menu da bandeja (um único `ApplyMode`).

**Pronto quando:** dá para trocar de modo e ligar o segundo plano só com o teclado; o resultado aparece na pílula na leitura seguinte.

> Feito no PR da 10.2: `QuickPanel` (cartão de 280 px com 6 modos em grade 3x2, botões Segundo plano / Desligar tela / Ferramentas e rodapé "Abrir painel completo"), clique simples na pílula abre/fecha, ancorado pela mesma `FlyoutPlacement` do flyout, recebe foco (`CanActivate` em `FloatingForm`), Esc/clicar fora fecham (`Deactivate`), trocar modo usa o mesmo `ApplyMode` do menu da bandeja, e `PublishMode` atualiza pílula e painel. Conferi o visual em imagem. **Diferenças e pendências:** os botões de modo não têm ícone (só nome); "Ferramentas" abre por enquanto o painel completo atual (a aba Ferramentas é da fase 12); a engrenagem de configurações do rodapé não foi feita. **Falta verificar rodando o app:** trocar modo e ligar o segundo plano só com o teclado (Tab/setas/Enter/Esc); o painel realmente recebe foco ao clicar na pílula; o resultado aparece na pílula na leitura seguinte.

### 10.3 Unificar a interface
- [x] Bandeja, pílula, painel rápido e painel completo leem o mesmo estado (modo, segundo plano) de um único lugar no `TrayApp` e são avisados por um evento quando muda. Nada de cada janela consultar o `PowerService` por conta própria.

**Pronto quando:** trocar o modo em qualquer lugar atualiza os outros três sem esperar a próxima leitura de sensores.

> Feito no PR da 10.3: `ModeStore` (único lugar do estado do modo e do segundo plano, evento `Changed` só quando muda; testado). O `TrayApp` é o único que lê o `PowerService` para a interface (`RefreshPowerState`) e `OnModeStateChanged` atualiza bandeja, pílula, painel rápido e painel completo; trocar o modo ou o segundo plano em qualquer lugar chama `RefreshPowerState` na hora, sem esperar a leitura de sensores. **Falta verificar rodando o app:** trocar o modo no painel rápido, no menu da bandeja e no painel completo e ver as outras três atualizarem sem atraso. Obs.: o painel completo ainda tem controles manuais de turbo/limite que continuam passando por `ApplyPower` (combinação Personalizada).

---

## Fase 11 — Ferramentas com administrador

**Objetivo:** substituir o `Manutencao_e_Limpeza_Automatica.bat` e o `Otimizar_e_Debloat_Windows11.ps1` sem rodar o app inteiro como administrador.

### 11.1 Execução elevada
- [x] O app relança **ele mesmo** com `ThermoBook.exe --tarefa <nome> --relatorio <arquivo>` via `ShellExecute` verbo `runas` (UAC). O processo elevado não abre janela, faz a tarefa, grava um relatório JSON e sai.
- [x] O app normal espera o processo, lê o relatório e mostra uma notificação com o resultado ("1,2 GB liberados").
- [x] UAC cancelado = notificação "Cancelado", sem erro.
- [x] Lista fechada de tarefas aceitas (`limpeza`, `otimizacao`); qualquer outro argumento é recusado (o processo elevado é uma fronteira de confiança).

**Pronto quando:** teste do parser de argumentos (aceita só as tarefas conhecidas); cancelar o UAC não gera exceção.

> Feito no PR da 11.1: `ElevatedTask` com `Parse` (aceita só `--tarefa <limpeza|otimizacao> --relatorio <caminho completo .json>`, nessa ordem e nada mais), `RunElevated` (roda a tarefa, grava o relatório com `CreateNew`, então o processo elevado nunca sobrescreve um arquivo existente) e `LaunchAsync` (`runas`, espera o processo, lê e apaga o relatório; UAC cancelado, erro 1223, vira "Cancelado" e nunca lança). `Program` desvia para o lado elevado antes do mutex e da bandeja; `TrayApp.RunTool` mostra o balão (erro só quando a tarefa falha). Testado: parser (válidos e 9 inválidos), arquivo existente não é sobrescrito, UAC cancelado simulado, ida e volta do relatório. Rodei o `.exe` direto com `--tarefa`: grava o relatório e sai sem abrir janela. As duas tarefas ainda devolvem "ainda não implementada" (corpos na 11.2 e na 11.3), e nenhum botão chama `RunTool` ainda (chega com a 11.2). **Falta verificar no notebook:** o UAC aparecer de verdade e "Não" mostrar o balão "Cancelado".

### 11.2 Limpeza
- [x] Porta o `.bat`: `%TEMP%`, `C:\Windows\Temp`, relatórios WER e crash dumps, `SoftwareDistribution\Download`, pastas vazias, cache DNS. Itens em uso são pulados em silêncio.
- [x] **Esvaziar a Lixeira** é opção separada, desmarcada por padrão (não tem volta).
- [x] Mede o espaço livre no C: antes e depois.

**Pronto quando:** rodar duas vezes seguidas: a primeira libera espaço, a segunda diz "já estava limpo"; nada fora das pastas listadas é apagado (teste com pasta temporária falsa).

> Feito no PR da 11.2: `Cleanup` (`DeleteContents` apaga só o conteúdo das 7 pastas da lista e mantém a pasta; em uso é pulado; junction/link é removido sem seguir o destino; recusa caminho relativo e raiz de disco) + `Run` (mede o C: antes/depois, cache DNS por `DnsFlushResolverCache` e Lixeira por `SHEmptyRecycleBin`, sem PowerShell). Tarefa nova `limpeza-lixeira` (a lista fechada passou a ter 3 nomes) para a Lixeira ser opção separada; o menu da bandeja tem "Limpar temporários (administrador)..." e "Limpar e esvaziar a Lixeira..." (esta pede confirmação, "não tem volta"). O passo 5 do `.bat` (pastas vazias) está incluído no passo de apagar o conteúdo. Testado com pastas falsas: duas rodadas seguidas (a segunda não acha nada), nada fora da pasta, item em uso, link, arquivo somente leitura. **Falta no notebook:** rodar de verdade com UAC (o "1,2 GB liberados" real e "Já estava limpo" na segunda vez), DNS e Lixeira. Eu não rodei a limpeza real na sua máquina.

### 11.3 Otimização do Windows
- [x] Tela com itens marcáveis, agrupados como o `.ps1`: Privacidade e IA (Bing, Copilot, anúncios, telemetria), Desempenho (transparência, apps em segundo plano, Delivery Optimization), Disco (TRIM), Apps pré-instalados.
- [x] Ajustes de registro guardam o valor anterior e têm **Desfazer**.
- [x] Remover apps pré-instalados: lista com nome de cada app, todos desmarcados, confirmação "isso não pode ser desfeito pelo ThermoBook; reinstale pela Microsoft Store".
- [x] Status do Defender: só leitura, mostrado no fim.
- [x] Ajustes só de `HKCU` rodam sem administrador; só TRIM e o que for de máquina usam a 11.1.

**Pronto quando:** aplicar e desfazer cada ajuste de registro volta o valor original (teste com chave de teste em `HKCU\Software\ThermoBook\Teste`); nenhum app é removido sem estar marcado.

> Feito no PR da 11.3: `Tweaks` (catálogo, `Apply`/`Undo` com backup salvo antes de gravar, TRIM por WMI `MSFT_Volume.Optimize`, Defender por WMI `MSFT_MpComputerStatus`, apps pelo `PackageManager` do WinRT) + `OptimizeForm` (menu da bandeja "Otimizar o Windows..."). Itens, decididos com o dono: Privacidade e IA = Bing, botão do Copilot, Widgets (`TaskbarDa`; builds recentes do Windows bloqueiam a escrita e o item aparece como "Não aplicado"), ID de anúncios, experiências personalizadas; Desempenho = transparência, apps em segundo plano (`GlobalUserDisabled`) e Delivery Optimization pela **política de máquina** (`HKLM\...\Policies\...\DeliveryOptimization\DODownloadMode=0`, elevado), porque o valor de HKCU do `.ps1` é ignorado. Ficaram de fora: `CortanaConsent` (Cortana não existe mais), `BackgroundAppGlobalToggle`, `netsh autotuning=normal` (já é o padrão), temporários e DNS (já estão na 11.2) e políticas de telemetria de máquina. Backup de HKCU em `settings.json` (`TweakBackup`); backup de HKLM em `HKLM\SOFTWARE\ThermoBook\Desfazer`, porque o processo elevado pode ser de outro usuário. Tarefas elevadas novas: `otimizacao-entrega`, `otimizacao-trim`, `otimizacao-entrega-trim`, `otimizacao-desfazer` (o marcador `otimizacao` saiu). Apps do sistema (que o Windows não deixa remover) não aparecem na lista. Para o `PackageManager`, o TFM passou a `net10.0-windows10.0.19041.0` (o `Microsoft.Windows.SDK.NET` tem ~24 MB; o .exe publicado ficou com 135 MB). Testado com chave falsa: aplicar e desfazer cada ajuste (valor existia e não existia), aplicar duas vezes, backup antes de gravar, valor de outro tipo intocado, ida e volta dos ajustes de máquina, tela (apps desmarcados, nada removido sem marcar, confirmação negada não faz nada, ajustes da conta sem UAC). Conferi só leitura nesta máquina: a lista de apps e o status do Defender. **Falta no notebook:** aplicar e desfazer de verdade (não rodei a otimização real), o UAC do TRIM/Delivery Optimization, o resultado do TRIM e a remoção real de um app.

---

## Fase 12 — Painel completo, instalador e release v0.2.0

### 12.1 Painel completo em abas
- [x] Abas **Monitor** (cartões e gráficos da v0.1), **Modos** (tabela dos 6 modos com descrição + controles avançados de turbo/limite da v0.1), **Ferramentas** (11.2 e 11.3), **Configurações** (pílula, atalho, alerta, iniciar com o Windows).
- [x] Fundo preto `#000000` igual à pílula.

> Feito na 12.1 (commit direto no `main`, a pedido do dono): `MainForm` virou a moldura em abas no jeito do OpenUsage (faixa de botões de segmento no topo, aba ativa em `raised` + negrito, uma página por vez, rodapé com o status; Ctrl+Tab/Ctrl+Shift+Tab trocam de aba). Modos: os 6 modos com descrição (o atual em `accent`), "Ativo agora", controles avançados de turbo/limite e o botão da ventoinha Samsung. Ferramentas: `ToolsPage` (era a `OptimizeForm`, agora página) com a limpeza no topo (Lixeira desmarcada e com confirmação) e a otimização da 11.3 sem mudança de regra (apps desmarcados com confirmação, Desfazer, WMI e PackageManager fora da thread da interface; a lista de apps e o Defender só são lidos quando a aba aparece pela primeira vez). Configurações: `SettingsPage` com pílula, atalho (registra na hora; se estiver em uso, avisa e fica o anterior; vazio desliga), alerta (°C e segundos, troca para Frio) e iniciar com o Windows; toda mudança passa por `TrayApp.ChangeSettings`, que salva e atualiza o menu da bandeja e a aba. O menu da bandeja troca os 3 itens de ferramentas por um só "Ferramentas...", e o "Ferramentas" do painel rápido abre o painel já nessa aba. Testado: abas (uma visível por vez, Ctrl+Tab dá a volta), limpeza com e sem Lixeira, aba Configurações (mostrar não dispara mudança, cada controle muda só o seu campo, atalho válido/vazio/inválido/em uso). Renderizei as 4 abas em imagem a 100%. **Falta verificar rodando o app:** nitidez em 150% e 200%, navegação por teclado e foco (Tab, Ctrl+Tab, Enter) e a aba Ferramentas listando apps e Defender de verdade.

### 12.2 Instalador
- [x] Desinstalar: se o segundo plano estiver ligado, restaura o snapshot; volta ao modo Eficiência (padrão de fábrica do notebook de testes); remove a Run key e o atalho global.
- [ ] Rodar o fluxo instalar → usar → desinstalar numa conta limpa.

> Feito na 12.2 (commit no `main`): `Uninstall.Run` (chamado por `--restaurar`) desliga o segundo plano devolvendo o snapshot de cada plano, desfaz os ajustes de HKCU da otimização, volta ao modo Eficiência e, só se houver backup de máquina (Delivery Optimization), pede o UAC para `otimizacao-desfazer`; recusar deixa como está. Cada passo é independente (um que falha não impede os outros). A Run key já saía pelo `[Registry]` do `.iss`; o atalho global não fica salvo no sistema (`RegisterHotKey` morre com o processo, que o `taskkill` fecha antes). Testado com chave e `settings.json` falsos: tudo ligado → tudo desfeito; nada ligado → só o modo, sem UAC; passo que falha; UAC recusado. Instalador compilado com o ISCC. **Falta:** o fluxo instalar → usar → desinstalar numa conta limpa (não rodei; mudaria o modo e os ajustes desta máquina) e ver o UAC aparecer na desinstalação. A chave vazia `HKLM\SOFTWARE\ThermoBook` fica depois de desfazer.

### 12.3 Documentação
- [x] README com imagem da pílula; PRODUCT/DESIGN/STATE atualizados com o que mudou na implementação.

> Feito na 12.3: README reescrito para a v0.2 com `docs/img/pilula.png` (desenhada pelo `OnPaint` real da pílula a 200%, com os cantos do notch aplicados na composição). PRODUCT: o que a v0.2 entrega e limites novos (.exe de ~135 MB, Widgets, apps removidos). DESIGN: cores do ponto do modo, painel rápido sem ícones e sem engrenagem, menu do botão direito como é de fato, atualização a cada segundo. A imagem não é um print do app rodando.

### 12.4 Release
- [x] Propor o número da versão e **esperar aprovação do dono** antes de criar a tag.

> O dono aprovou `v0.2.0-beta.1` (2026-11-18) para testar a v0.2 no notebook pela Release antes da `v0.2.0`. O `release.yml` passou a marcar tag com hífen como pré-lançamento.
- [ ] Tag `v0.2.0` → `release.yml` publica o instalador; baixar da Release e instalar.

> A `v0.2.0-beta.1` saiu como pré-lançamento com o instalador (workflow verde). O que falta conferir no notebook antes da `v0.2.0` está junto em [VERIFICACAO.md](VERIFICACAO.md).

**Pronto quando:** o usuário instala a v0.2.0 pela Release e não precisa mais da pasta `Desktop\Bats` para nada do que está em [PRODUCT.md](../PRODUCT.md).

---

## Backlog (depois da v0.2)

Em ordem de valor provável:

1. Modo automático por regra: na bateria → Economia; na tomada → Equilibrado; programa X aberto (ex.: `ffmpeg`) → Frio.
2. Agendar a limpeza (Agendador de Tarefas), substituindo o "pode ser agendado" do `.bat`.
3. Temperatura de GPU dedicada NVIDIA via `nvidia-smi`.
4. Histórico em CSV e gráfico de 24 h.
5. Atualização automática pelas Releases.
6. Tema claro e inglês.
7. Integrar os outros scripts da pasta `Bats` (rede, conversão AV1) se fizer sentido — avaliar antes, não são sobre temperatura/energia.
