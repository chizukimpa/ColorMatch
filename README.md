# ColorMatch

> Iguala cor e brilho entre monitores no Windows.

## Sobre

Aplicativo Windows (WinForms) para **colorir monitores iguais** (mesma cor/brilho entre
telas), sem colorímetro: usa **DDC/CI** (controles do OSD do monitor) + **LUT de gamma
do Windows** (`SetDeviceGammaRamp`) por monitor (`\\.\DISPLAYx`).

Desenvolvido para rodar **100% invisível** (sem ícone na bandeja) e **sem UAC/admin**.

## Funcionalidades

- **Corrigir cor entre telas**: brilho, contraste, ganhos RGB, gamma e realce (curva de
  software) por monitor, com sliders modernos.
- **Vigia automático**: re-verifica a LUT a cada 60 s (e após mudanças de resolução/rede/repouso)
  e restaura só se algo apagou.
- **DDC/CI completo (OSD avançado)**: lê e escreve códigos VCP do monitor, com nomes MCCS,
  valores tabelados (entrada, preset, mudo, energia) e área avançada listando todos os
  códigos suportados.
- **Banco de monitores** (`%APPDATA%\ColorMatch\monitores.xml`): auto-identifica
  marca/modelo/tamanho/ano/série via EDID e capacidades DDC/CI na primeira vez que o
  monitor aparece — funciona com monitores de qualquer marca.
- **Perfis por monitor** (`%APPDATA%\ColorMatch\profile.xml`): calibração não é alterada,
  e valores extras (controles OSD) são salvos e reaplicados.
- **Reordenar cartões** arrastando o cabeçalho; a ordem fica salva no perfil.
- **Preset "Generic RGB (macOS)"**: aproxima o visual do perfil *Generic RGB* do macOS
  (gamma efetivo ~1.8, DDC neutro), útil para quem gosta daquele look.
- **Iniciar com o Windows**: entrada em `HKCU\...\Run` com `/invisivel`, sem UAC, e
  reparada automaticamente se sumir (flag `AutoStart` no perfil).
- **Janela auto-dimensionada** para N monitores (rola se não couber na tela).
- Ícone próprio, UI arredondada (estilo macOS/One UI), tema escuro.

## Tecnologias

- C# / WinForms
- DDC/CI (VCP/MCCS)
- LUT de gamma do Windows (`SetDeviceGammaRamp`)
- EDID
- .NET Framework 4.0+

## Download e acesso

O instalador é gerado localmente com `build_installer.cmd` (veja abaixo).

## Como rodar

### Requisitos

- Windows 10/11
- .NET Framework 4.0+ (compilador `csc.exe` já incluso no Windows)
- Monitores com suporte a DDC/CI (a maioria dos monitores modernos)

### Compilar

```bat
build.cmd
```

Gera `ColorMatch.exe` na pasta do projeto. O script usa o `csc.exe` nativo do Windows.

### Executar

- `ColorMatch.exe` — abre a janela.
- `/invisivel` — roda sem ícone na bandeja (modo padrão do autostart).
- `/osd` — abre a janela OSD de cada monitor ao iniciar.
- `/dump` — gera diagnóstico em arquivo.
- `/aplicar` — aplica o perfil e sai.

Fechar (X) minimiza para a bandeja; o botão de energia encerra de vez.

### Instalador

```bat
build_installer.cmd
```

Gera `ColorMatchSetup.exe`, que instala em `%LOCALAPPDATA%\ColorMatch` (sem UAC), cria
atalho no Menu Iniciar, registra autostart e desinstalador.

O próprio app cria a entrada no registro (`HKCU\...\Run`) com `/invisivel`, sem UAC.

## Calibração

Os valores de calibração (brilho, contraste, ganhos, gamma, realce) ficam no
`profile.xml` do usuário e **não são alterados pelo código**. O app só os aplica.

## Estrutura

```
src/
  Api.cs        - DDC/CI, VCP, ramp gamma, EDID
  Profiles.cs   - perfil XML (MonitorCal, Ordem, Extras)
  MonitorDb.cs  - banco de monitores (auto-identificação)
  Ui.cs         - tema e controles modernos
  Osd.cs        - janela OSD avançada
  App.cs        - janela principal, vigilância, arrasto
build.cmd
build_installer.cmd
```
