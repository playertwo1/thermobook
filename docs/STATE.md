# Estado atual

> Atualize este arquivo no mesmo commit de cada subfase concluída. Mantenha curto: o histórico fica no git e no [STATE arquivado da v0.2](archive/STATE-v0.2.md).

**Atualizado em:** 2026-11-18
**Versão publicada:** v0.1.0; pré-lançamentos v0.2.0-beta.1 e v0.3.0-beta.2
**Pendente da v0.2:** verificação no notebook pelo dono ([checklist](VERIFICACAO.md)) e tag `v0.2.0` com aprovação. As fases 0–12 estão em [archive/ROADMAP-v0.2.md](archive/ROADMAP-v0.2.md).
**Próxima subfase:** [18.2 Release](ROADMAP.md#182-release): `v0.3.0-beta.2` publicada; falta o dono passar o [checklist da v0.3](VERIFICACAO-v0.3.md) (seções 1–4 no notebook; a 15 numa máquina com NVIDIA) e aprovar a `v0.3.0` final.

## Progresso

| Fase | Status |
|---|---|
| 0–12 v0.1 e v0.2 | ⚠️ no `main`, falta a verificação da v0.2 no notebook |
| 13 Modo automático por regra | ⚠️ no `main` mas falta verificar (13.1–13.3 feitas; falta rodar o app e o notebook) |
| 14 Limpeza agendada | ⚠️ no `main` mas falta verificar (14.1–14.2 feitas; falta UAC no notebook) |
| 15 Temperatura da GPU NVIDIA | ⚠️ no `main` só com testes (15.1–15.2 feitas; falta uma máquina com NVIDIA) |
| 16 Histórico em CSV e gráfico de 24 h | ⚠️ no `main` mas falta verificar (16.1–16.2 feitas; falta rodar o app por horas) |
| 17 Atualização automática | ⚠️ no `main` mas falta verificar (17.1–17.2 feitas; falta atualizar de verdade pela Release) |
| 18 Verificação e release | 🟨 em andamento (18.1 checklist feito; 18.2 espera o dono) |

Legenda: ⬜ não iniciada · 🟨 em andamento · ✅ no `main` · ⚠️ no `main` mas falta verificar no notebook

## Decisões tomadas

D1–D17 vêm da v0.2 e continuam valendo.

| # | Decisão | Por quê |
|---|---|---|
| D1 | Pílula feita em WinForms, no mesmo processo | Sem dependência nova; o app já é WinForms e o `Snapshot` já existe. |
| D2 | Catálogo de 6 modos: Economia, Frio, Eficiência, Equilibrado, Alto, Máximo | Junta as 5 opções do `.bat` com o perfil Frio, que é o que mais esfria. Silencioso → Economia e Normal → Eficiência na migração. |
| D3 | Modos aplicados no **plano ativo**, não num GUID fixo | O `.bat` usa `859668fb-…`, que só existe nesta máquina. |
| D4 | Segundo plano restaura o **snapshot** do que havia antes, não um padrão fixo | O `Restaurar_Plano_Padrao.bat` força o plano Equilibrado e "suspender", o que pode não ser o que o usuário tinha. |
| D5 | Limpeza/otimização num processo elevado separado, com lista fechada de tarefas | O app principal nunca roda como administrador. |
| D6 | Esvaziar Lixeira e remover apps desmarcados por padrão | Não têm volta. |
| D7 | Atalho para desligar a tela é `Ctrl+Alt+Shift+D`, configurável em `settings.json` | Proposta aceita por falta de objeção. |
| D8 | Posição inicial da pílula: borda direita, centro vertical, monitor principal | Proposta aceita por falta de objeção. |
| D9 | Pílula aparece já na primeira execução | Proposta aceita por falta de objeção. |
| D10 | Apps pré-instalados removidos pelo `PackageManager` do WinRT; TFM `net10.0-windows10.0.19041.0` | Única API sem PowerShell. Aceito pelo dono, mesmo com o .exe ~24 MB maior. |
| D11 | Delivery Optimization pela política de máquina (HKLM, elevado), não pelo valor de HKCU do `.ps1` | O valor de HKCU é ignorado pelo Windows. |
| D12 | Otimização fica no que o `.ps1` faz: sem políticas de telemetria/Copilot/Recall de máquina; `TaskbarDa` entra; `CortanaConsent`, `BackgroundAppGlobalToggle` e `netsh` saem | Decidido com o dono. |
| D13 | Backup dos ajustes de máquina em `HKLM\SOFTWARE\ThermoBook\Desfazer`, não no `settings.json` | O processo elevado pode ser de outro usuário; HKLM só administrador grava. |
| D14 | Abas do painel completo no jeito do OpenUsage, não o `TabControl` do Windows | O `TabControl` nativo não fica preto. |
| D15 | Aba Configurações edita tudo e salva na hora | Decidido com o dono. |
| D16 | Menu da bandeja: um "Ferramentas..." que abre a aba | Decidido com o dono. |
| D17 | Trabalho direto no `main`, sem branch nem PR | Pedido do dono (2026-11-18). |
| D18 | Próxima versão: itens 1–5 do backlog (modo automático, limpeza agendada, GPU NVIDIA, histórico, atualização automática); tema claro, inglês e os outros scripts da `Bats` ficam fora | Decidido com o dono (2026-11-18). |

## Decisões em aberto

Confirmar com o dono antes da subfase indicada. Se não houver resposta, siga a proposta.

| # | Pergunta | Proposta | Antes de |
|---|---|---|---|
| A2 | Quando várias regras valem ao mesmo tempo, e o que acontece com a troca manual | Alerta de temperatura > programa > energia; entre programas, o primeiro da lista. Troca manual vale até as regras pedirem outro modo. (Implementado assim na 13.1.) | 13.1 |
| A3 | Limpeza agendada: padrão e condições | Desligada por padrão; ao ligar, semanal no domingo às 12 h, só na tomada, roda depois se o PC estava desligado; nunca esvazia a Lixeira. | 14.1 |
| A4 | GPU NVIDIA pela NVML (`nvml.dll`) em vez do `nvidia-smi` do backlog | NVML: é API, não abre processo a cada segundo (regra do AGENTS). | 15.1 |
| A5 | A pílula e o alerta passam a usar a temperatura da GPU? | Não: continuam na CPU; a GPU aparece no flyout, no painel e no CSV. | 15.2 |
| A6 | Formato e duração do histórico | Uma linha por minuto, `;` com vírgula decimal (Excel em português), 30 dias, ligado por padrão (~100 KB por dia). | 16.1 |
| A7 | Atualização: avisar ou instalar sozinho? Betas? | Só avisa; baixa e instala com um clique do usuário. Betas só com a opção ligada. | 17.1 |
| A8 | De onde sai a tag `v0.2.0` se o código das fases 13+ já estiver no `main` | Tag no commit da beta verificada (`84e6d98` ou a última correção da verificação), não no `main` com fases novas pela metade. | 12.4 |
| A9 | Número e momento da próxima versão | Proposta: `v0.3.0-beta.1` (pré-lançamento) no `main`, depois `v0.3.0` quando o dono passar o [checklist](VERIFICACAO-v0.3.md). Aguardando aprovação do dono; nenhuma tag criada. | 18.2 |

## Riscos ativos

- **Verificação real exige o notebook** (tampa, UAC, `powercfg /qh`, carregador): CI só cobre build e testes. A v0.2 inteira ainda não foi verificada nele.
- **Fase 15 sem máquina de teste:** o notebook tem Intel Arc B390, sem NVIDIA. Sem outra máquina, a 15 fica só com testes.
- **Fase 17 instala código baixado:** a confiança vem do HTTPS do GitHub e do SHA-256 do asset (que vem da mesma API); o instalador não é assinado.
- **Fase 14 cria tarefa elevada:** se ela rodasse o `ThermoBook.exe` da pasta do usuário, quem trocasse o `.exe` ganharia administrador sem UAC. O ROADMAP 14.1 resolve com uma cópia em `%ProgramFiles%\ThermoBook\`; essa cópia não se atualiza com o app (a 17 precisa reagendar, ou seja, um UAC, quando a versão mudar).
- **Overlay do modo de energia** só funciona com o plano Equilibrado ativo.

## Notas da última sessão

- 13.2: `TrayApp.RunAutoMode` (tique de 1 s) + `PollProcesses` (timer de 5 s fora da interface) + `ModeState.Auto` mostrado no flyout, painel rápido, pílula (leitor de tela) e dica da bandeja. Ligar só pelo `settings.json` até a 13.3. Não rodei o app. 281 testes.
- 13.1: `AutoMode` (regras puras, edge-triggered: só pede troca quando o modo desejado muda), `AutoRules`/`ProgramRule`, `Settings.Auto` (null = nunca configurado) e `TempAlert.Active`. Nada ligado ao app ainda (13.2). 280 testes passando.
- Roadmap novo (fases 13–18) com os itens 1–5 do backlog; fases 0–12 e o STATE da v0.2 arquivados em `docs/archive/`. Nenhum código mudou.
