# Verificação das fases 13–17 (v0.3)

O que o [ROADMAP](ROADMAP.md) marca como "falta verificar" nas fases 13, 14, 16 e 17, na ordem de fazer, mais a fase 15, que só dá para conferir numa máquina com NVIDIA. Mesmo formato do [VERIFICACAO.md](VERIFICACAO.md) (v0.2): marque `[x]` ao passar; se falhar, anote ao lado o que aconteceu e a subfase.

Instale a versão de teste pela Release (o número vem da 18.2, quando o dono aprovar) e **faça antes a verificação da v0.2**.

Apoio (PowerShell):

```
& "$env:LOCALAPPDATA\Programs\ThermoBook\ThermoBook.exe" --diagnostico | Out-String
explorer "$env:APPDATA\ThermoBook"
```

## 1. Modo automático por regra (13.1–13.3)

Configurações > Modo automático.

- [ ] **Ligar:** marcar "Modo automático". Esperado: a pílula, o flyout ("Modo: X · automático") e o painel rápido ("Modo de energia · automático") mostram que está automático.
- [ ] **Tomada x bateria:** com regras Bateria = Economia e Tomada = Equilibrado, tirar e pôr o carregador. Esperado: o modo troca em poucos segundos nos dois sentidos.
- [ ] **Programa:** abrir o `ffmpeg` (ou o programa da lista) e fechá-lo. Esperado: vai para o modo da regra (Frio, por padrão) e volta ao de tomada/bateria ao fechar.
- [ ] **Lista de programas:** adicionar e remover um programa; a mudança vale sem reiniciar e sobrevive a fechar e abrir o app.
- [ ] **Troca manual:** com o automático ligado, escolher outro modo pelo menu. Esperado: a escolha manual vale até a condição mudar (carregador, programa); aí o automático volta a mandar.
- [ ] **Calor:** com "resfriar quando quente" ligado, esquentar a CPU até o alerta. Esperado: o modo vai para Frio mesmo com a regra de tomada; ao esfriar, volta.
- [ ] **CPU:** Gerenciador de Tarefas depois de algumas horas com o automático ligado. Esperado: sem aumento visível de CPU (a lista de processos é lida a cada poucos segundos).

## 2. Limpeza agendada (14.1–14.2)

Ferramentas > Limpeza automática.

- [ ] **Agendar:** escolher semanal (dia e hora) e Aplicar. Esperado: um UAC; mensagem de sucesso. No Agendador de Tarefas aparece a tarefa do ThermoBook, só com energia da tomada, privilégio mais alto.
- [ ] **Cópia protegida:** a tarefa roda um `ThermoBook.exe` em `C:\Program Files\ThermoBook` (não o de `%LOCALAPPDATA%`).
- [ ] **Rodar de verdade:** no Agendador, "Executar". Esperado: `%AppData%\ThermoBook\limpeza.json` aparece e a aba mostra "Última limpeza: ... X GB liberados".
- [ ] **Perdeu o horário:** agendar para daqui a 2 min, deixar o PC dormindo e acordar depois. Esperado: roda ao acordar (StartWhenAvailable).
- [ ] **Mensal:** trocar para mensal e Aplicar. Esperado: a tarefa antiga é substituída, não duplicada.
- [ ] **UAC cancelado:** Aplicar e responder "Não". Esperado: "Cancelado", agendamento anterior intacto.
- [ ] **Desligar:** escolher "Desligada" e Aplicar (frequências: "Toda semana" / "Todo mês"). Esperado: a tarefa some do Agendador.
- [ ] **Desinstalar:** com a limpeza agendada, desinstalar o app. Esperado: a tarefa some e `C:\Program Files\ThermoBook` também.

## 3. Histórico em CSV e gráfico de 24 h (16.1–16.2)

Deixe o app rodando por algumas horas antes.

- [ ] **Arquivo:** `%AppData%\ThermoBook\historico\AAAA-MM-DD.csv` existe, com uma linha por minuto.
- [ ] **Excel:** abrir o CSV no Excel em português. Esperado: colunas separadas, vírgula decimal, acentos do modo corretos. Com o arquivo aberto no Excel, o app continua gravando (as linhas aparecem ao fechar o Excel).
- [ ] **Gráfico:** aba Monitor > botão "24 h". Esperado: curvas de temperatura, CPU, GPU e RAM; buraco onde o app ficou fechado (sem linha ligando os lados).
- [ ] **Virada do dia:** conferir à meia-noite (ou mudando a hora): o gráfico junta os dois arquivos.
- [ ] **Desligar:** desmarcar "Gravar o histórico" em Configurações. Esperado: para de gravar; o que já existe fica.
- [ ] **Limpeza antiga:** arquivo com mais de 30 dias é apagado na próxima abertura do app.
- [ ] **Abrir a pasta:** "Abrir pasta do histórico" abre `historico` no Explorador.

## 4. Atualização automática (17.1–17.2)

Só dá para conferir de verdade quando existir uma versão **maior** que a instalada; a v0.3.0 não se atualiza sozinha a partir da v0.2 (a função só existe daqui para a frente). Use a versão seguinte, ou uma beta mais nova.

- [ ] **Aviso:** com versão nova na Release, ~30 s depois de abrir o app aparece o balão uma vez e o item "Atualizar para a versão X..." no menu; Configurações > Atualizações mostra a versão.
- [ ] **Instalar:** clicar. Esperado: baixa, confere o SHA-256, instala em silêncio, fecha o app e abre de novo na bandeja (sem janela do instalador).
- [ ] **Sem rede:** desligar a internet e abrir o app. Esperado: nenhum erro nem janela.
- [ ] **Desligar:** desmarcar "Verificar atualizações". Esperado: nenhuma conexão (conferir no Monitor de Recursos > Rede).
- [ ] **Beta:** marcar "Receber também versões beta". Esperado: passa a oferecer betas; desmarcado, não oferece.
- [ ] **Conhecido:** a cópia da limpeza agendada em `Program Files` não se atualiza sozinha; reaplicar a limpeza depois de atualizar.

## 5. Temperatura da GPU NVIDIA (15.1–15.2) — outra máquina

Este notebook é Intel Arc e não tem `nvml.dll`. Rode numa máquina com placa NVIDIA e driver instalado.

- [ ] `--diagnostico` imprime `temperatura N C` na linha da GPU, e N bate com `nvidia-smi --query-gpu=temperature.gpu --format=csv` (±2 °C).
- [ ] O flyout mostra "GPU NVIDIA: N °C, ..." só nessa máquina; neste notebook a linha não existe.
- [ ] O cartão da GPU na aba Monitor mostra a temperatura no detalhe.
- [ ] O CSV traz `gpu_temperatura_c` preenchida (vazia neste notebook).
- [ ] Com duas placas (se houver), vale a mais quente.
- [ ] A pílula e o alerta continuam na temperatura da CPU.

## 6. Fecho

- [ ] Nada do backlog feito (13–17) faz falta ou incomoda no uso diário. Quando tudo passar, o dono aprova o número da versão e a tag (18.2).
