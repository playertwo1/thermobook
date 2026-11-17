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

## O que já existe (v0.1.0)

Ícone na bandeja com a temperatura, menu com 4 perfis (Frio, Silencioso, Normal, Máximo), painel com cartões e gráficos, alerta de temperatura, iniciar com o Windows, instalador sem admin e release automática. Detalhes em [docs/PLANO.md](docs/PLANO.md).

## O que a v0.2 entrega

1. **Pílula flutuante** sempre visível, presa a borda/canto, arrastável, com temperatura, CPU, GPU, RAM e o modo atual.
2. **Seis modos de energia** (catálogo único que junta os perfis da v0.1 e as opções do `.bat`).
3. **Modo segundo plano** reversível: guarda os valores originais e restaura exatamente esses.
4. **Desligar tela** por botão e por atalho global.
5. **Ferramentas** que precisam de administrador (limpeza e otimização), rodando num processo elevado separado, só quando o usuário pede.

## Princípios

- **Sem administrador no dia a dia.** Monitorar, trocar modo, segundo plano e desligar tela funcionam como usuário comum. Só limpeza e otimização pedem UAC, e só no clique.
- **Sem driver de kernel.** Nada de WinRing0/LibreHardwareMonitor: antivírus bloqueiam e é risco. Aceitamos as limitações de leitura (ver Limites).
- **Tudo reversível ou avisado.** Toda mudança de energia/registro guarda o valor anterior. O que não tem volta (apagar apps, esvaziar lixeira) pede confirmação explícita e fica desmarcado por padrão.
- **Leve.** A coleta roda numa thread de fundo; a pílula não pode custar mais que ~1% de CPU nem roubar o foco de outras janelas.
- **Português do Brasil** em toda a interface.

## Fora do escopo da v0.2

Controle de ventoinha (o firmware Samsung não expõe), modo automático por programa ou bateria, histórico em CSV, inglês, tema claro, atualização automática, agendamento da limpeza. Ficam no backlog do [ROADMAP](docs/ROADMAP.md).

## Limites conhecidos

- **Temperatura da CPU** vem das zonas térmicas ACPI, não do sensor por núcleo.
- **Temperatura da GPU** integrada (Intel Arc) não é exposta sem driver; a pílula mostra só uso da GPU. GPU dedicada NVIDIA pode ganhar temperatura depois via `nvidia-smi`.
- **Modos de energia do Windows (overlay)** só têm efeito com o plano Equilibrado ativo; em outros planos o app grava os valores do processador direto.
- **SmartScreen** mostra "Editor desconhecido" (sem certificado de assinatura).

## Como saber que deu certo

- A pílula fica aberta o dia todo sem passar de ~1% de CPU e ~60 MB de RAM.
- Trocar de modo na pílula muda os valores que o `powercfg /qh` mostra.
- Com o modo segundo plano ligado, fechar a tampa não suspende; desligar o modo devolve os valores originais (conferidos com `powercfg /qh`).
- O usuário apaga a pasta `Desktop\Bats` e não sente falta.
