# Verificação da v0.2 no notebook

Tudo o que o ROADMAP marca como "falta verificar" nas fases 7–12, na ordem de fazer. Use a **v0.2.0-beta.1** instalada pela [Release](https://github.com/playertwo1/thermobook/releases/tag/v0.2.0-beta.1).

Marque `[x]` ao passar. Se falhar, anote ao lado o que aconteceu (e o número da subfase) para virar correção. Quando tudo passar, a `v0.2.0` pode sair (12.4).

Comandos de apoio (PowerShell):

```
powercfg /qh > antes.txt                                                # foto do plano antes de mexer
& "$env:LOCALAPPDATA\Programs\ThermoBook\ThermoBook.exe" --diagnostico | Out-String   # atual x esperado do modo
```

> O instalador põe o app em `{userpf}\ThermoBook` (normalmente `%LOCALAPPDATA%\Programs\ThermoBook`).

## 1. Instalar (12.4)

- [ ] Baixar `ThermoBook-Setup-0.2.0-beta.1.exe` da Release e instalar. Esperado: SmartScreen avisa "Editor desconhecido" (sem assinatura), instala **sem pedir administrador**, abre o app no fim.
- [ ] A pílula aparece na borda direita, centro vertical, do monitor principal (D8, D9).

## 2. Pílula (9.1–9.4)

- [ ] **Foco (9.1):** com o Bloco de Notas em foco, clicar na pílula. Esperado: o cursor continua piscando no Bloco de Notas.
- [ ] **Alt+Tab (9.1):** a pílula não aparece na lista nem na barra de tarefas.
- [ ] **Nitidez (9.2):** Configurações > Tela > Escala em 150% e em 200%. Esperado: números e medidores nítidos, nada cortado.
- [ ] **Narrador (9.2):** Ctrl+Win+Enter, passar pelas linhas da pílula. Esperado: "Temperatura 54 graus", "CPU 12 por cento" etc.
- [ ] **Arrastar e encaixar (9.3):** soltar perto de cada borda e de cada canto. Esperado: encaixa como notch (cantos do lado da tela retos); no topo/base fica horizontal.
- [ ] **Posição salva (9.3):** Sair pelo menu e abrir de novo. Esperado: volta onde estava.
- [ ] **Monitor externo (9.3):** com a pílula no monitor externo, desligar o cabo. Esperado: ela volta para a tela do notebook.
- [ ] **Tela cheia (9.4):** vídeo do YouTube em tela cheia (F11/tecla F). Esperado: a pílula some em até ~2 s e volta ao sair.
- [ ] **Mostrar pílula (9.4):** desmarcar no menu da bandeja esconde; reabrir o app mantém escondida; marcar mostra.
- [ ] **Menu (9.4):** botão direito na pílula abre o menu da bandeja; clicar fora fecha.
- [ ] **Custo (9.4):** Gerenciador de Tarefas depois de algumas horas. Esperado: ThermoBook abaixo de ~1% de CPU e ~60 MB de RAM.

## 3. Flyout e painel rápido (10.1–10.3)

- [ ] **Flyout (10.1):** passar o mouse na pílula. Esperado: abre depois de ~300 ms, do lado oposto à borda, sem a pílula piscar; some ~150 ms depois de sair.
- [ ] **Flyout nos cantos (10.1):** com a pílula em cada um dos 4 cantos, o flyout fica inteiro dentro da tela.
- [ ] **Painel rápido (10.2):** clicar na pílula abre o cartão e ele recebe o foco; Esc e clicar fora fecham.
- [ ] **Só teclado (10.2):** com o painel aberto, Tab/setas até um modo e Enter; depois até "Segundo plano" e Enter. Esperado: os dois funcionam e o resultado aparece na pílula.
- [ ] **Estado unificado (10.3):** trocar o modo pelo painel rápido, pelo menu da bandeja e pelo painel completo. Esperado: os outros lugares atualizam na hora.

## 4. Modos de energia (7.1–7.4)

Plano **Equilibrado** ativo (o overlay só pega nele).

- [ ] **Cada modo (7.2/7.4):** para cada um dos 6 modos, aplicar e rodar `--diagnostico`. Esperado: coluna atual = esperado; `powercfg /qh` mostra os mesmos valores (tabela no ROADMAP 7.2).
- [ ] **Overlay (7.1):** depois de Economia/Eficiência, o controle "Modo de energia" em Configurações > Energia mostra "Melhor eficiência"; Alto → "Melhor desempenho".
- [ ] **Personalizado (7.3):** trocar o modo de energia pelas Configurações do Windows. Esperado: o ThermoBook mostra "Personalizado" (ponto cinza).
- [ ] **Troca de plano (7.3):** trocar de plano de energia (Painel de Controle > Opções de Energia). Esperado: o modo salvo é reaplicado no plano novo.
- [ ] **Frio esfria (v0.1):** carga pesada em Equilibrado e depois em Frio. Esperado: temperatura bem mais baixa no Frio.

## 5. Segundo plano e tela (8.1–8.3)

- [ ] **Antes:** `powercfg /qh > antes.txt`.
- [ ] **Tampa (8.3):** ligar Modo segundo plano (anel na pílula, ponto azul no ícone), fechar a tampa na tomada e na bateria. Esperado: o PC não suspende (um download ou música continua).
- [ ] **Persistência (8.3):** com o modo ligado, reiniciar o PC. Esperado: continua ligado depois.
- [ ] **Desligar (8.3):** desligar o modo e `powercfg /qh > depois.txt`. Esperado: `fc antes.txt depois.txt` sem diferença na tampa, suspensão e hibernação.
- [ ] **Desligar a tela (8.1):** pelo menu e pelo painel rápido. Esperado: tela apaga, mexer o mouse religa, o app segue respondendo.
- [ ] **Atalho (8.2):** com outro app em foco, `Ctrl+Alt+Shift+D`. Esperado: tela apaga.
- [ ] **Atalho em uso (8.2/12.1):** em Configurações, pôr um atalho que outro programa já usa. Esperado: aviso e o anterior continua valendo.

## 6. Painel completo (12.1)

- [ ] **Nitidez:** as 4 abas em 150% e 200%.
- [ ] **Teclado:** Ctrl+Tab / Ctrl+Shift+Tab trocam de aba; Tab percorre os controles; Enter aciona botões.
- [ ] **Ferramentas:** a aba lista os apps pré-instalados (todos desmarcados) e mostra o status do Defender.
- [ ] **Configurações:** cada opção muda na hora e sobrevive a fechar e abrir o app.

## 7. Ferramentas com administrador (11.1–11.3)

- [ ] **UAC cancelado (11.1):** Limpar agora e responder "Não". Esperado: "Cancelado", sem erro.
- [ ] **Limpeza (11.2):** Limpar agora e "Sim". Esperado: "X GB liberados". Rodar de novo: "Já estava limpo" (ou quase nada).
- [ ] **Lixeira (11.2):** com algo na Lixeira, marcar a opção. Esperado: pede confirmação; depois a Lixeira fica vazia.
- [ ] **Ajustes da conta (11.3):** Aplicar ajustes (sem marcar apps). Esperado: sem UAC para os de conta; itens ficam "(aplicado)"; Widgets pode dizer "Não aplicado" (conhecido).
- [ ] **TRIM e Delivery Optimization (11.3):** Esperado: um UAC; mensagem com o resultado do TRIM.
- [ ] **Desfazer (11.3):** Desfazer ajustes. Esperado: os de conta voltam sem UAC, os de máquina pedem UAC; os "(aplicado)" somem.
- [ ] **Remover um app (11.3):** marcar **um** app que não faz falta. Esperado: confirmação "não pode ser desfeito"; app removido.

## 8. Desinstalar (12.2)

Melhor numa **conta limpa** do Windows (usuário novo). Antes: deixar Segundo plano ligado, ajustes da otimização aplicados (inclusive Delivery Optimization) e um modo diferente de Eficiência.

- [ ] Desinstalar por Configurações > Apps. Esperado: pede UAC uma vez (ajustes de máquina); termina sem janela de erro.
- [ ] `powercfg /qh`: tampa/suspensão voltaram ao de antes do segundo plano; processador no modo Eficiência.
- [ ] Ajustes de conta e Delivery Optimization desfeitos; "Iniciar com o Windows" sumiu (`HKCU\...\Run` sem `ThermoBook`); `%AppData%\ThermoBook` apagada.
- [ ] Conhecido: sobra a chave vazia `HKLM\SOFTWARE\ThermoBook`.

## 9. Fecho

- [ ] Dá para apagar `Desktop\Bats` sem sentir falta (critério da v0.2 no PRODUCT).
