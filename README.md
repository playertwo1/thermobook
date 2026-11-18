# ThermoBook

Pílula flutuante para Windows que mostra **temperatura, CPU, GPU e RAM** o tempo todo, presa à borda da tela, e troca o **modo de energia**, liga o **modo segundo plano** (tampa fechada sem suspender) e **desliga a tela** com um clique.

![Pílula vertical presa à borda direita e horizontal presa ao topo](docs/img/pilula.png)

No notebook de testes (Galaxy Book6 Pro), o modo **Frio** (turbo desligado) baixou a temperatura de 83–86 °C para 54–57 °C.

> A v0.1.0 está publicada nas Releases. A v0.2 (pílula, modos, ferramentas) está feita no `main` e falta verificar no notebook — ver [docs/ROADMAP.md](docs/ROADMAP.md) e [docs/STATE.md](docs/STATE.md). Agentes: leiam [AGENTS.md](AGENTS.md). O protótipo original em PowerShell está em [prototype/](prototype/).

## O que faz

- **Pílula**: a temperatura muda de cor (verde, laranja, vermelho), CPU/GPU/RAM com medidor e um ponto com o modo atual. Arraste para qualquer borda ou canto; passar o mouse mostra o detalhe, clicar abre o painel rápido, o botão direito abre o menu. Some sozinha com app em tela cheia.
- **Seis modos de energia**: Economia, Frio, Eficiência, Equilibrado, Alto e Máximo, sem `powercfg` e sem administrador.
- **Modo segundo plano**: fechar a tampa e ficar parado não suspendem o PC. Desligar devolve exatamente o que havia antes.
- **Desligar a tela**: botão ou `Ctrl+Alt+Shift+D` (dá para trocar o atalho nas Configurações).
- **Ferramentas**: limpeza de temporários e otimização do Windows em itens marcáveis, com Desfazer. Só o que é de máquina pede administrador, e só na hora do clique.
- **Painel completo**: abas Monitor, Modos, Ferramentas e Configurações.

Sem driver de kernel e sem rodar como administrador no dia a dia. Limites conhecidos em [PRODUCT.md](PRODUCT.md#limites-conhecidos).

## Gerar o instalador

Precisa do .NET 10 SDK e do Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```
dotnet publish src/ThermoBook -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
ISCC installer\ThermoBook.iss
```

O instalador sai em `installer\Output\`. Ele instala só para o usuário, sem pedir administrador. Ao desinstalar, o modo segundo plano é desligado (tampa e suspensão voltam ao que eram), os ajustes da otimização são desfeitos (os de máquina pedem UAC), o processador volta ao modo Eficiência e a opção "Iniciar com o Windows" é removida.

## Conferir sem abrir a bandeja

```
dotnet run --project src/ThermoBook -- --diagnostico
```

Imprime sensores e a tabela do modo atual (valor no plano x esperado).
