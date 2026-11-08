# ThermoBook

App de bandeja para Windows que mostra a temperatura do notebook e o uso de CPU, GPU, RAM e disco, e troca o comportamento do processador (Turbo Boost e limite da CPU) com um clique, para esquentar menos.

> Em desenvolvimento. O plano está em [docs/PLANO.md](docs/PLANO.md). O protótipo em PowerShell que já funciona está em [prototype/](prototype/).

No notebook de testes (Galaxy Book6 Pro), desligar o Turbo Boost baixou a temperatura de 83–86 °C para 54–57 °C.

## Gerar o instalador

Precisa do .NET 10 SDK e do Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```
dotnet publish src/ThermoBook -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
ISCC installer\ThermoBook.iss
```

O instalador sai em `installer\Output\`. Ele instala só para o usuário, sem pedir administrador. Ao desinstalar, o processador volta ao perfil Normal e a opção "Iniciar com o Windows" é removida.
