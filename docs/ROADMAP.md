# ThermoBook — Roadmap

Da v0.2 (pílula, 6 modos e ferramentas) até a **próxima versão: o app troca de modo sozinho, limpa por agenda, lê a GPU NVIDIA, guarda histórico e se atualiza**. O que é o produto está em [PRODUCT.md](../PRODUCT.md), como ele parece em [DESIGN.md](../DESIGN.md), onde estamos em [STATE.md](STATE.md).

As fases 0–12 (até a v0.2.0) estão arquivadas em [archive/ROADMAP-v0.2.md](archive/ROADMAP-v0.2.md), com a tabela de valores dos 6 modos (7.2).

## Como ler

- **Fase** = entrega que pode ir para o `main` sozinha, com o app compilando e os testes verdes.
- **Subfase** = um commit (ou PR pequeno) dentro da fase. Cada uma tem *o que fazer* e *pronto quando*.
- Uma fase só começa quando a anterior está no `main`, exceto onde a coluna "depende de" diz outra coisa.
- Ao terminar uma subfase, marque `[x]` aqui e atualize [STATE.md](STATE.md).
- Decisões que este roadmap supõe e que o dono ainda não confirmou estão em [STATE.md](STATE.md#decisões-em-aberto) (A2–A8). Sem resposta, vale a proposta.

## Visão geral

| Fase | Nome | Depende de | Versão |
|---|---|---|---|
| 0–12 | Bandeja, pílula, modos, ferramentas, instalador | — | v0.1.0 ✅, v0.2.0 (falta verificar) |
| 13 | Modo automático por regra | 12 | próxima (proposta: v0.3) |
| 14 | Limpeza agendada | 12 | próxima |
| 15 | Temperatura da GPU NVIDIA | 12 | próxima |
| 16 | Histórico em CSV e gráfico de 24 h | 12 (a coluna da GPU NVIDIA entra se a 15 já estiver pronta) | próxima |
| 17 | Atualização automática | 12 | próxima |
| 18 | Verificação e release | 13–17 e a v0.2.0 publicada | **próxima versão** |

Ordem sugerida: **13 → 14 → 16 → 17 → 15 → 18** (valor para o dono primeiro; a 15 por último porque o notebook de testes não tem NVIDIA). As fases são independentes entre si, então dá para correr em paralelo, um agente por worktree, cuidando dos pontos de conflito:

- `TrayApp.cs`: 13.2 e 17.2.
- `SettingsPage.cs`: 13.3, 16.1 e 17.2.
- `MainForm.cs`: 15.2 e 16.2.
- `ToolsPage.cs`, `ElevatedTask.cs`, `Uninstall.cs`: só a 14.

---

## Pendente da v0.2

Não é código: é verificação no notebook, feita pelo dono. Detalhes em [archive/ROADMAP-v0.2.md](archive/ROADMAP-v0.2.md#122-instalador).

- [ ] **12.2** Rodar o fluxo instalar → usar → desinstalar numa conta limpa (seção 8 do [checklist](VERIFICACAO.md)).
- [ ] **12.4** Passar a `v0.2.0-beta.1` pelo [checklist de verificação](VERIFICACAO.md); com o dono aprovando, tag `v0.2.0` (de onde sai a tag: decisão A8).

---

## Fase 13 — Modo automático por regra

**Objetivo:** o app escolhe o modo sozinho: na bateria → Economia, na tomada → Equilibrado, com um programa aberto (ex.: `ffmpeg`) → Frio. Desligado por padrão.

### 13.1 Motor de regras
- [x] `AutoMode.Decide(regras, naTomada, quente, programasAbertos) → modo ou nada`, lógica pura, sem tocar no sistema.
- [x] Prioridade: alerta de temperatura (Frio automático da v0.1) > regra de programa > regra de energia. Entre programas, vale o primeiro da lista (A2).
- [x] Troca manual (bandeja, pílula, painel, ou pelo Windows) vale até as regras pedirem **outro modo** (tomada ↔ bateria, programa abre ou fecha, temperatura); aí as regras voltam a mandar (A2).
- [x] `Settings.Auto` com padrão desligado e as 3 regras de exemplo do backlog; `settings.json` antigo carrega sem erro.

**Pronto quando:** testes cobrem cada regra, a prioridade, a troca manual pausando e voltando, regras vazias e `settings.json` sem o campo.

> Feito na 13.1: `AutoMode` (`Services/AutoMode.cs`) com `Target` (o modo que as regras querem) e `Decide`, que só devolve um modo quando o desejado muda. Por isso não há "pausa" explícita: uma troca manual, venha de onde vier, fica até as regras pedirem outro modo; mudar de condição sem mudar o modo desejado (ex.: tirar da tomada com o `ffmpeg` aberto) não desfaz a troca manual. Programa casa pelo nome sem diferença de maiúsculas e com ou sem `.exe`; modo desconhecido numa regra é ignorado. `Settings.Auto` nulo = nunca configurado (desligado; a interface mostra `AutoRules.Default`). `TempAlert.Active` diz se o alerta está valendo (até esfriar 5 °C abaixo do limite), para a 13.2 passar como "quente". 18 testes novos (280 no total).

### 13.2 Ligar ao app
- [x] Energia: `SystemInformation.PowerStatus` lido no tique de 1 s da interface (uma chamada barata ao Windows; mais simples que assinar `SystemEvents`).
- [x] Programas: `Process.GetProcesses()` a cada 5 s numa thread de fundo, comparando só os nomes das regras (sem o evento de processo do WMI, que pede administrador).
- [x] O modo escolhido passa pelo `ModeStore` (único lugar do estado); pílula, flyout, painel rápido e bandeja mostram que o modo veio de uma regra ("Economia · automático").

**Pronto quando:** no notebook, tirar o carregador troca para Economia em até ~2 s e pôr de volta troca para Equilibrado; abrir o `ffmpeg` vai para Frio e fechá-lo volta; uma troca manual não é desfeita até as regras pedirem outro modo; CPU do app continua abaixo de ~1%.

> Feito na 13.2: `TrayApp.RunAutoMode` no tique de 1 s (tomada: `PowerLineStatus`, "desconhecido" conta como tomada; quente = Frio automático ligado e `TempAlert.Active`) chama `AutoMode.Decide` e aplica pelo `ApplyMode` de sempre (salva o `Profile`, publica no `ModeStore`). `PollProcesses` roda num `System.Threading.Timer` de 5 s e só lista processos se houver regra de programa ligada; exceção ali é engolida (derrubaria o app). `ModeState.Auto` = modo atual igual ao que as regras querem: flyout "Modo: Economia · automático", título do painel rápido "Modo de energia · automático", leitor de tela da pílula e dica da bandeja. Ainda não há como ligar pela interface (13.3); só editando `"Auto"` no `settings.json`. **Falta verificar no notebook** (não rodei o app: mexeria no modo desta máquina): carregador, `ffmpeg`, troca manual e CPU.

### 13.3 Interface
- [x] Seção "Modo automático" na aba Configurações: ligar/desligar, modo na bateria, modo na tomada, lista de programas → modo (adicionar pelo nome do `.exe` ou escolhendo um processo aberto).
- [x] Item "Modo automático" (marcável) no menu da bandeja.

**Pronto quando:** cada controle muda na hora e sobrevive a fechar e abrir o app; dá para fazer tudo só com teclado.

> Feito na 13.3 (marcado `[x]` só no código; falta verificar rodando o app): `SettingsPage` ganhou "Modo automático" (ligar, modo na bateria e na tomada com "(nenhum)", lista de programas, nome editável com a lista de processos abertos preenchida ao abrir, modo, Adicionar/Remover; Delete remove) e o menu da bandeja o item marcável "Modo automático". Tudo passa por `ChangeSettings`; o primeiro uso grava `AutoRules.Default` com a edição aplicada. Programa repetido (com ou sem `.exe`, sem diferença de maiúsculas) troca o modo em vez de duplicar. A ordem da lista é a de inclusão (sem subir/descer; para mudar a prioridade, remover e adicionar de novo). **Falta verificar:** aparência e nitidez em 150%/200%, teclado (Tab, setas, Delete) e a lista de processos.

---

## Fase 14 — Limpeza agendada

**Objetivo:** a limpeza da 11.2 roda sozinha (semanal ou mensal) pelo Agendador de Tarefas, sem UAC a cada vez, substituindo o "pode ser agendado" do `.bat`.

### 14.1 Tarefa no Agendador
- [ ] Criar e remover a tarefa `\ThermoBook\Limpeza` pela API COM do Agendador (`Schedule.Service`, sem dependência nova e sem `schtasks.exe`).
- [ ] Criar/remover precisa de administrador: duas tarefas novas na lista fechada do `ElevatedTask`, `limpeza-agendar` e `limpeza-desagendar` (UAC uma vez, ao ligar ou desligar).
- [ ] O app fica na pasta do usuário, que qualquer programa do usuário pode trocar; uma tarefa elevada apontando para lá daria administrador sem UAC a quem trocasse o `.exe`. Por isso `limpeza-agendar` copia o `ThermoBook.exe` para `%ProgramFiles%\ThermoBook\` (só administrador grava) e a tarefa aponta para essa cópia; `limpeza-desagendar` apaga a cópia.
- [ ] A tarefa roda `ThermoBook.exe --limpeza-agendada` com privilégios mais altos, só com o usuário conectado (sem guardar senha), "executar assim que possível se perdeu o horário" e só na tomada (A3).
- [ ] `--limpeza-agendada` limpa **sem** a Lixeira (não tem volta) e grava o resultado em `%AppData%\ThermoBook\limpeza.json` (arquivo próprio, para não disputar o `settings.json` com o app aberto).

**Pronto quando:** testes cobrem a definição da tarefa (gatilho, ação, privilégio, condições), o parser de `--limpeza-agendada` e a leitura/gravação do `limpeza.json` (inclusive arquivo ausente ou corrompido).

### 14.2 Interface e desinstalação
- [ ] Aba Ferramentas: "Limpeza automática" (desligada / semanal / mensal), dia e hora, e "Última limpeza automática: data, X GB".
- [ ] O desinstalador remove a tarefa (entra no UAC que a 12.2 já pede; sem tarefa, sem UAC a mais).

**Pronto quando:** no notebook, agendar para daqui a 2 min e ver rodar sem janela e sem UAC, com o resultado aparecendo na aba; desligar tira a tarefa do Agendador; desinstalar remove a tarefa.

---

## Fase 15 — Temperatura da GPU NVIDIA

**Objetivo:** com placa NVIDIA, a temperatura da GPU aparece junto do uso. Sem NVIDIA, nada muda.

### 15.1 Leitura
- [ ] Ler pela NVML (`nvml.dll`, que vem com o driver NVIDIA) com `LibraryImport` e `NativeLibrary.TryLoad`, em vez de rodar o `nvidia-smi` a cada leitura (A4: regra do AGENTS, sem processo para o que tem API).
- [ ] Só na thread de fundo do `SensorService`; `Snapshot` ganha `GpuTemp` (nulo sem NVIDIA ou se a NVML falhar). Falha na NVML não derruba as outras leituras.

**Pronto quando:** teste com leitor falso cobre sem NVIDIA (nulo), com uma placa, com duas (vale a mais quente) e erro da NVML.

### 15.2 Mostrar
- [ ] Flyout, cartão da GPU na aba Monitor, `--diagnostico` e o CSV da fase 16 mostram a temperatura da GPU.
- [ ] A pílula e o alerta continuam usando a temperatura da CPU (A5).

**Pronto quando:** numa máquina com NVIDIA, o valor bate com `nvidia-smi --query-gpu=temperature.gpu --format=csv`. **O notebook de testes não tem NVIDIA (Intel Arc B390):** a verificação real precisa de outra máquina; sem ela, a fase fica com `[x]` só nos testes e a nota "falta verificar".

---

## Fase 16 — Histórico em CSV e gráfico de 24 h

**Objetivo:** dá para ver como a temperatura e o uso andaram nas últimas 24 h e abrir os dados no Excel.

### 16.1 Gravação
- [ ] `HistoryWriter` lê o `Latest` do `SensorService` (não consulta sensor), junta um minuto e grava uma linha: hora, temperatura máx, CPU, GPU, temperatura da GPU (se houver), RAM, modo.
- [ ] Um arquivo por dia: `%AppData%\ThermoBook\historico\AAAA-MM-DD.csv`, com `;` e vírgula decimal (abre direto no Excel em português, A6). Gravação fora da thread da interface.
- [ ] Apaga arquivos com mais de 30 dias (A6). `Settings.History` ligado por padrão, com opção em Configurações.

**Pronto quando:** testes cobrem a média do minuto, a virada do dia (linha no arquivo certo), a limpeza dos antigos e o formato da linha.

### 16.2 Gráfico de 24 h
- [ ] Aba Monitor: alternar "Ao vivo / 24 h"; o modo 24 h lê os CSVs das últimas 24 h numa thread de fundo e reaproveita o `Graph` que já existe.
- [ ] Botão "Abrir pasta do histórico".
- [ ] Linha quebrada ou arquivo faltando é pulado, não derruba o gráfico.

**Pronto quando:** testes cobrem a leitura (24 h atravessando dois arquivos, linha quebrada, pasta vazia); rodando o app algumas horas, o gráfico mostra o período e o CSV abre no Excel com as colunas certas.

---

## Fase 17 — Atualização automática

**Objetivo:** o app avisa quando sai versão nova na Release e, com um clique, se atualiza sem o usuário baixar nada à mão.

### 17.1 Verificar
- [ ] `UpdateService` consulta a API de Releases do GitHub (`/releases/latest`, que ignora pré-lançamentos) com `HttpClient`, ao abrir e uma vez por dia, numa thread de fundo. Nenhum dado do usuário vai junto (User-Agent genérico `ThermoBook`).
- [ ] Compara com a versão do app no formato `0.2.0-beta.1` (pré-lançamento é menor que a versão final).
- [ ] Opção "Receber versões beta" (desligada; ligada passa a olhar também os pré-lançamentos) (A7).
- [ ] Sem rede ou erro da API: silêncio, tenta no dia seguinte.

**Pronto quando:** testes cobrem a comparação de versões (`0.2.0-beta.1 < 0.2.0 < 0.2.1 < 0.3.0`), o JSON da API (arquivo de exemplo), escolher o asset `ThermoBook-Setup-*.exe` e Release sem instalador.

### 17.2 Baixar e instalar
- [ ] Aviso "ThermoBook X disponível" (balão uma vez por versão + item no menu da bandeja + linha em Configurações). Nada é baixado sem o clique.
- [ ] Ao confirmar: baixa para `%TEMP%`, confere o SHA-256 com o `digest` do asset na API (sem `digest` ou diferente: não instala e avisa), roda o instalador em modo silencioso e o app sai; o instalador reabre o app no fim.
- [ ] `Settings.CheckUpdates` (ligado) e `Settings.UpdateBeta` (desligado); a versão já avisada não avisa de novo.

**Pronto quando:** um build local com versão menor (ex.: `-p:Version=0.0.1`) instalado acha a Release mais nova, atualiza com um clique e reabre já na versão da Release, com `settings.json` e posição da pílula preservados; com `CheckUpdates` desligado não há nenhuma chamada de rede.

---

## Fase 18 — Verificação e release

### 18.1 Checklist
- [ ] Checklist de verificação das fases 13–17 no notebook (no formato do [VERIFICACAO.md](VERIFICACAO.md)), com o que só dá para conferir em outra máquina (15) separado.

### 18.2 Release
- [ ] Propor o número da versão (proposta: `v0.3.0`, com beta antes se o dono quiser) e **esperar aprovação do dono** antes de criar a tag.
- [ ] Tag → `release.yml` publica o instalador; instalar pela Release. A v0.2.0 não se atualiza sozinha para ela (a atualização automática só existe a partir desta versão); a 17 se confere de verdade na versão seguinte.

**Pronto quando:** o dono instala pela Release, passa o checklist e não sente falta de nada do backlog que foi feito.

---

## Fora do roadmap

- Tema claro e inglês: o dono decidiu que não precisa (2026-11-18).
- Integrar os outros scripts da pasta `Bats` (rede, conversão AV1): não são sobre temperatura nem energia; o dono decidiu que não precisa (2026-11-18).
- Controle de ventoinha: o firmware Samsung não expõe.
