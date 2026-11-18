# ThermoBook — Produto

## Em uma frase

Uma pílula flutuante no canto da tela que mostra **temperatura, CPU, GPU e RAM** o tempo todo e deixa trocar o **modo de energia**, ligar o **modo segundo plano** (tampa fechada sem suspender) e **desligar a tela** com um clique, sem abrir nenhum `.bat`.

## Para quem

O dono de um notebook fino que esquenta (o de testes é um Samsung Galaxy Book6 Pro, Intel Core Ultra, GPU Intel Arc) e que hoje controla o PC com scripts soltos na Área de Trabalho:

| Script de hoje (`Desktop\Bats`) | Vira no ThermoBook |
|---|---|
| `Ativar_Segundo_Plano.bat` opções 1–5 | **Modos de energia** na pílula e no menu |
| `Ativar_Segundo_Plano.bat` (tampa/suspensão) | **Modo segundo plano** (liga/desliga) |
| `Restaurar_Plano_Padrao.bat` / opção 0 | Desligar o modo segundo plano (volta exatamente ao que era antes) |
| `desligar_tela.bat` | **Desligar tela** (botão e atalho de teclado) |
| `Manutencao_e_Limpeza_Automatica.bat` | **Limpeza** (pede administrador só nessa hora) |
| `Otimizar_e_Debloat_Windows11.bat/.ps1` | **Otimização do Windows** em itens marcáveis, com desfazer onde der |

## Referência de forma

A pílula de ícones do OpenUsage para Windows (`~/projetos/openusage`): uma cápsula preta, estreita, presa a uma borda ou canto da tela, que mostra o essencial sem atrapalhar. Passar o mouse abre detalhes; clicar abre um painel compacto. O ThermoBook usa a mesma ideia com métricas no lugar de provedores. Ver [DESIGN.md](DESIGN.md).

## v0.1.0 (publicada)

Ícone na bandeja com a temperatura, menu com 4 perfis (Frio, Silencioso, Normal, Máximo), painel com cartões e gráficos, alerta de temperatura, iniciar com o Windows, instalador sem admin e release automática. Detalhes em [docs/PLANO.md](docs/PLANO.md).

## v0.2 (feita no código, falta verificar no notebook)

1. **Pílula flutuante** sempre visível, presa a borda/canto como um notch, arrastável, com temperatura, CPU, GPU, RAM e o modo atual. Passar o mouse abre o flyout; clicar abre o painel rápido; some com app em tela cheia.
2. **Seis modos de energia** (Economia, Frio, Eficiência, Equilibrado, Alto, Máximo): catálogo único que junta os perfis da v0.1 e as opções do `.bat`. Silencioso vira Economia e Normal vira Eficiência sozinhos. Mexer no modo pelo Windows mostra "Personalizado"; trocar de plano reaplica o modo salvo.
3. **Modo segundo plano** reversível: guarda os valores originais de cada plano e restaura exatamente esses.
4. **Desligar tela** por botão e pelo atalho global `Ctrl+Alt+Shift+D` (configurável).
5. **Ferramentas** que precisam de administrador (limpeza e a parte de máquina da otimização), rodando num processo elevado separado, só quando o usuário pede. Os ajustes da conta rodam sem UAC e têm Desfazer.
6. **Painel completo em abas**: Monitor, Modos, Ferramentas e Configurações (tudo do `settings.json` editável e salvo na hora).
7. **Desinstalar devolve o PC como estava**: desliga o segundo plano, desfaz os ajustes da otimização (os de máquina pedem UAC) e volta ao modo Eficiência.

## Próxima versão (no `main`, falta verificar; ver [VERIFICACAO-v0.3](docs/VERIFICACAO-v0.3.md))

1. **Modo automático por regra**: modo na bateria, na tomada e por programa aberto; o calor continua com prioridade.
2. **Limpeza agendada** semanal ou mensal pelo Agendador, só na tomada, com uma cópia do app em `Program Files` para não deixar um executável editável rodando como administrador.
3. **Temperatura da GPU NVIDIA** pela NVML, no flyout, no painel e no CSV (a pílula e o alerta seguem na CPU).
4. **Histórico em CSV** (um minuto por linha, 30 dias) e **gráfico de 24 h** na aba Monitor.
5. **Atualização automática** pela Release do GitHub, com SHA-256 conferido e desligável.

## Princípios

- **Sem administrador no dia a dia.** Monitorar, trocar modo, segundo plano e desligar tela funcionam como usuário comum. Só limpeza e otimização pedem UAC, e só no clique.
- **Sem driver de kernel.** Nada de WinRing0/LibreHardwareMonitor: antivírus bloqueiam e é risco. Aceitamos as limitações de leitura (ver Limites).
- **Tudo reversível ou avisado.** Toda mudança de energia/registro guarda o valor anterior. O que não tem volta (apagar apps, esvaziar lixeira) pede confirmação explícita e fica desmarcado por padrão.
- **Leve.** A coleta roda numa thread de fundo; a pílula não pode custar mais que ~1% de CPU nem roubar o foco de outras janelas.
- **Português do Brasil** em toda a interface.

## Fora do escopo da v0.2

Controle de ventoinha (o firmware Samsung não expõe), inglês e tema claro.
## Limites conhecidos

- **Temperatura da CPU** vem das zonas térmicas ACPI, não do sensor por núcleo.
- **Temperatura da GPU** integrada (Intel Arc) não é exposta sem driver; a pílula mostra só uso da GPU. GPU dedicada NVIDIA pode ganhar temperatura depois via `nvidia-smi`.
- **Modos de energia do Windows (overlay)** só têm efeito com o plano Equilibrado ativo; em outros planos o app grava os valores do processador direto.
- **SmartScreen** mostra "Editor desconhecido" (sem certificado de assinatura).
- **O .exe tem ~135 MB**: o `PackageManager` do WinRT (remover apps pré-instalados) traz ~24 MB de SDK, além do .NET embutido.
- **Widgets** na barra de tarefas: builds recentes do Windows bloqueiam a escrita e o ajuste aparece como "Não aplicado".
- **Apps pré-instalados removidos** não voltam pelo ThermoBook; só pela Microsoft Store.

## Como saber que deu certo

- A pílula fica aberta o dia todo sem passar de ~1% de CPU e ~60 MB de RAM.
- Trocar de modo na pílula muda os valores que o `powercfg /qh` mostra.
- Com o modo segundo plano ligado, fechar a tampa não suspende; desligar o modo devolve os valores originais (conferidos com `powercfg /qh`).
- O usuário apaga a pasta `Desktop\Bats` e não sente falta.
