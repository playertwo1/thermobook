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
- [ ] `record Mode(Nome, Descrição, Overlay, valores AC/DC…)` substitui `PowerProfile`. Valores tirados do `.bat`:

| Modo | Overlay | CPU mín AC/DC | CPU máx | Turbo | EPP | Núcleos mín AC/DC | Disco ocioso |
|---|---|---|---|---|---|---|---|
| Economia | melhor eficiência | 5/5 | 70 | 0 desligado | 80 | 25/25 | 300 s |
| Frio | equilibrado | 5/5 | 100 | 0 desligado | 50 | 75/25 | 900 s |
| Eficiência | melhor eficiência | 5/5 | 100 | 3 eficiência | 50 | 25/25 | 600 s |
| Equilibrado | equilibrado | 5/5 | 100 | 1 ligado | 50 | 75/25 | 900 s |
| Alto | melhor desempenho | 100/50 | 100 | 2 agressivo | 25 | 100/75 | nunca |
| Máximo | máximo desempenho | 100/100 | 100 | 2 agressivo | 0 | 100/100 | nunca |

  (Frio = Equilibrado com turbo desligado: é o perfil que baixou 83–86 °C para 54–57 °C na v0.1. Latência e limiares de estacionamento seguem o `.bat` de cada modo.)
- [ ] Migração do `settings.json`: `Silencioso → Economia`, `Normal → Eficiência`, `Frio` e `Máximo` mantêm o nome.
- [ ] Menu da bandeja passa a listar os 6 modos.

**Pronto quando:** teste de migração cobre os 4 nomes antigos e `null`; aplicar cada modo e conferir com `powercfg /qh` bate com a tabela.

### 7.3 Detecção e reaplicação
- [ ] `Current()` compara só as configurações que definem o modo (turbo, limites, EPP, overlay); combinação desconhecida = "Personalizado".
- [ ] Reaplicar o modo salvo ao iniciar e quando o plano ativo mudar (`RegisterPowerSettingNotification` com `GUID_POWERSCHEME_PERSONALITY`, em vez de checar a cada leitura).

**Pronto quando:** trocar o modo no app de Configurações do Windows faz o ThermoBook mostrar "Personalizado"; trocar de plano de energia faz ele reaplicar o modo salvo.

### 7.4 Diagnóstico
- [ ] `ThermoBook.exe --diagnostico` imprime todos os valores do modo atual lado a lado com o esperado.

**Pronto quando:** a saída bate com `powercfg /qh` no notebook de testes.

---

## Fase 8 — Ações do sistema

**Objetivo:** substituir o modo segundo plano do `.bat`, o `Restaurar_Plano_Padrao.bat` e o `desligar_tela.bat`.

### 8.1 Desligar tela
- [ ] `DisplayService.TurnOff()`: `PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` após 500 ms (mesmo atraso do `.bat`, para o soltar da tecla/clique não religar). `PostMessage` e não `SendMessage`, que pode travar esperando uma janela que não responde.
- [ ] Item no menu da bandeja.

**Pronto quando:** clicar desliga a tela e mexer o mouse religa, sem o app travar.

### 8.2 Atalho global
- [ ] `RegisterHotKey` para desligar tela (padrão `Ctrl+Alt+Shift+D`, configurável no `settings.json`). Se o atalho já estiver em uso, avisa uma vez e segue sem ele.

**Pronto quando:** o atalho funciona com outro app em foco.

### 8.3 Modo segundo plano
- [ ] Ligar: guarda no `settings.json` os valores **atuais** de `LIDACTION`, `STANDBYIDLE`, `HIBERNATEIDLE` (AC e DC) do plano ativo e grava `0` (não fazer nada / nunca).
- [ ] Desligar: restaura exatamente os valores guardados (não um padrão fixo como o `.bat` faz) e apaga o snapshot.
- [ ] O snapshot fica salvo antes de gravar: se o app fechar ou o PC reiniciar com o modo ligado, ao abrir ele continua ligado e ainda dá para desligar e voltar ao original.
- [ ] Se o usuário trocar de plano de energia com o modo ligado, reaplica no plano novo (guardando o snapshot desse plano também).
- [ ] Indicador no ícone da bandeja (e depois na pílula, fase 9) e item com check no menu.

**Pronto quando:** testes da lógica de snapshot (ligar → desligar devolve o original; ligar duas vezes não sobrescreve o snapshot); no notebook, fechar a tampa com o modo ligado não suspende, e desligar devolve o que o `powercfg /qh` mostrava antes.

---

## Fase 9 — Pílula flutuante

**Objetivo:** a cápsula do [DESIGN.md](../DESIGN.md#pílula-estado-padrão), sempre visível, presa à borda. Pode começar em paralelo à fase 7 (só depende do `Snapshot` do `SensorService`).

### 9.1 Janela base
- [ ] `PillForm`: sem borda, `TopMost`, `ShowInTaskbar = false`, estilos `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (não rouba foco, fora do Alt+Tab).
- [ ] Cantos arredondados com `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)` no Windows 11, `Region` como plano B.
- [ ] Pintura própria com double buffer; redesenha só quando algum valor arredondado muda.

**Pronto quando:** clicar na pílula com o Bloco de Notas em foco não tira o foco do Bloco de Notas; a pílula não aparece no Alt+Tab.

### 9.2 Conteúdo
- [ ] Linhas: temperatura (cor por limiar), CPU, GPU, RAM (número + medidor de 2 px), modo atual (ponto; anel se segundo plano ligado).
- [ ] Orientação vertical e horizontal com o mesmo código de layout.
- [ ] Nome acessível por linha (`AccessibleObject` próprio).
- [ ] Valor indisponível mostra `–`.

**Pronto quando:** teste do layout (dado tamanho e orientação, retângulos das linhas não se sobrepõem e cabem na janela); fica nítida em 100%, 150% e 200%.

### 9.3 Posição e encaixe
- [ ] Arrastar pela pílula inteira (sem usar a barra de título do Windows).
- [ ] Ao soltar a até 24 px de uma borda, encaixa; perto de duas, encaixa no canto. Borda lateral → vertical; topo/base → horizontal.
- [ ] Salvar posição por monitor no `settings.json`; restaurar ao abrir. Se o monitor sumiu (cabo desligado), volta ao canto padrão do monitor principal (direita, centro vertical).
- [ ] Reagir a `SystemEvents.DisplaySettingsChanged` e `WM_DPICHANGED`.

**Pronto quando:** teste da função de encaixe (posição solta + área de trabalho → posição final e orientação) cobre as 4 bordas, os 4 cantos e o meio da tela; desconectar o monitor externo traz a pílula de volta para a tela do notebook.

### 9.4 Convivência
- [ ] Esconder quando um app estiver em tela cheia (`SHQueryUserNotificationState` = `QUNS_BUSY`/`QUNS_RUNNING_D3D_FULL_SCREEN`/`QUNS_PRESENTATION_MODE`), voltar sozinha depois.
- [ ] Menu da bandeja: "Mostrar pílula" (check), salvo no `settings.json`. Ligado por padrão.
- [ ] Botão direito na pílula abre o mesmo `ContextMenuStrip` da bandeja.

**Pronto quando:** um vídeo em tela cheia no navegador esconde a pílula e sair da tela cheia mostra de novo; a pílula aberta o dia todo fica abaixo de ~1% de CPU.

---

## Fase 10 — Painel rápido e flyout

**Objetivo:** trocar modo, ligar segundo plano e desligar a tela sem sair da pílula.

### 10.1 Flyout ao passar o mouse
- [ ] Aparece após 300 ms ao lado da pílula, do lado oposto à borda; some 150 ms após o mouse sair.
- [ ] Conteúdo: temperatura por zona + "quente/quente demais", GHz e turbo, GPU 3D/vídeo, RAM em GB, disco, modo, segundo plano.
- [ ] Também não rouba foco.

**Pronto quando:** passar o mouse mostra o flyout sem piscar a pílula; o flyout nunca sai da área de trabalho (testar com a pílula em cada canto).

### 10.2 Painel rápido ao clicar
- [ ] Cartão de 280 px ancorado à pílula: 6 modos segmentados, botões *Segundo plano*, *Desligar tela*, *Ferramentas…*, rodapé com "Abrir painel completo".
- [ ] Este cartão **pode** receber foco (precisa de teclado): Tab/setas/Enter/Esc. Fecha ao clicar fora (`Deactivate`).
- [ ] Trocar modo aqui chama o mesmo caminho do menu da bandeja (um único `ApplyMode`).

**Pronto quando:** dá para trocar de modo e ligar o segundo plano só com o teclado; o resultado aparece na pílula na leitura seguinte.

### 10.3 Unificar a interface
- [ ] Bandeja, pílula, painel rápido e painel completo leem o mesmo estado (modo, segundo plano) de um único lugar no `TrayApp` e são avisados por um evento quando muda. Nada de cada janela consultar o `PowerService` por conta própria.

**Pronto quando:** trocar o modo em qualquer lugar atualiza os outros três sem esperar a próxima leitura de sensores.

---

## Fase 11 — Ferramentas com administrador

**Objetivo:** substituir o `Manutencao_e_Limpeza_Automatica.bat` e o `Otimizar_e_Debloat_Windows11.ps1` sem rodar o app inteiro como administrador.

### 11.1 Execução elevada
- [ ] O app relança **ele mesmo** com `ThermoBook.exe --tarefa <nome> --relatorio <arquivo>` via `ShellExecute` verbo `runas` (UAC). O processo elevado não abre janela, faz a tarefa, grava um relatório JSON e sai.
- [ ] O app normal espera o processo, lê o relatório e mostra uma notificação com o resultado ("1,2 GB liberados").
- [ ] UAC cancelado = notificação "Cancelado", sem erro.
- [ ] Lista fechada de tarefas aceitas (`limpeza`, `otimizacao`); qualquer outro argumento é recusado (o processo elevado é uma fronteira de confiança).

**Pronto quando:** teste do parser de argumentos (aceita só as tarefas conhecidas); cancelar o UAC não gera exceção.

### 11.2 Limpeza
- [ ] Porta o `.bat`: `%TEMP%`, `C:\Windows\Temp`, relatórios WER e crash dumps, `SoftwareDistribution\Download`, pastas vazias, cache DNS. Itens em uso são pulados em silêncio.
- [ ] **Esvaziar a Lixeira** é opção separada, desmarcada por padrão (não tem volta).
- [ ] Mede o espaço livre no C: antes e depois.

**Pronto quando:** rodar duas vezes seguidas: a primeira libera espaço, a segunda diz "já estava limpo"; nada fora das pastas listadas é apagado (teste com pasta temporária falsa).

### 11.3 Otimização do Windows
- [ ] Tela com itens marcáveis, agrupados como o `.ps1`: Privacidade e IA (Bing, Copilot, anúncios, telemetria), Desempenho (transparência, apps em segundo plano, Delivery Optimization), Disco (TRIM), Apps pré-instalados.
- [ ] Ajustes de registro guardam o valor anterior e têm **Desfazer**.
- [ ] Remover apps pré-instalados: lista com nome de cada app, todos desmarcados, confirmação "isso não pode ser desfeito pelo ThermoBook; reinstale pela Microsoft Store".
- [ ] Status do Defender: só leitura, mostrado no fim.
- [ ] Ajustes só de `HKCU` rodam sem administrador; só TRIM e o que for de máquina usam a 11.1.

**Pronto quando:** aplicar e desfazer cada ajuste de registro volta o valor original (teste com chave de teste em `HKCU\Software\ThermoBook\Teste`); nenhum app é removido sem estar marcado.

---

## Fase 12 — Painel completo, instalador e release v0.2.0

### 12.1 Painel completo em abas
- [ ] Abas **Monitor** (cartões e gráficos da v0.1), **Modos** (tabela dos 6 modos com descrição + controles avançados de turbo/limite da v0.1), **Ferramentas** (11.2 e 11.3), **Configurações** (pílula, atalho, alerta, iniciar com o Windows).
- [ ] Fundo preto `#000000` igual à pílula.

### 12.2 Instalador
- [ ] Desinstalar: se o segundo plano estiver ligado, restaura o snapshot; volta ao modo Eficiência (padrão de fábrica do notebook de testes); remove a Run key e o atalho global.
- [ ] Rodar o fluxo instalar → usar → desinstalar numa conta limpa.

### 12.3 Documentação
- [ ] README com imagem da pílula; PRODUCT/DESIGN/STATE atualizados com o que mudou na implementação.

### 12.4 Release
- [ ] Propor o número da versão e **esperar aprovação do dono** antes de criar a tag.
- [ ] Tag `v0.2.0` → `release.yml` publica o instalador; baixar da Release e instalar.

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
