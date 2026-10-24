# ColorMatch

> Iguala cor e brilho entre monitores no Windows.

**Versão atual: 1.3.1** — cada correção sobe `1.0.x`; cada recurso novo sobe `1.x.0`.

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
- **Lista de perfis aplicáveis**: o seletor "Perfil" mostra a calibração atual e todos os
  perfis salvos (`%APPDATA%\ColorMatch\perfis\`). "+ Novo perfil" cria um snapshot na hora
  e ele já aparece na lista; dá para aplicar, renomear e excluir.
- **Predefinições por monitor**: cada cartão tem a lista **Predefinição** com os perfis do
  macOS (sRGB IEC61966-2.1, Display P3, Adobe RGB (1998), Apple RGB, Generic RGB,
  Generic Gray 2.2, Generic Gray e ProPhoto RGB). Ela mostra a predefinição ativa daquele
  monitor e aplica **só nele**, mudando **só a curva de gamma**; brilho/contraste/ganhos
  calibrados são mantidos.
- **Abertura instantânea**: o DDC/CI é lido em segundo plano depois que a janela aparece
  (monitor lento não segura mais a abertura).
- **Reordenar cartões** arrastando o cabeçalho; a ordem fica salva no perfil.
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

## Requisitos e problemas ao abrir

- Windows 10 ou 11 (64 ou 32 bits). O app é um único `.exe`: não tem DLL extra nem
  dependência para instalar — usa o .NET Framework 4.x que já faz parte do Windows.
- DDC/CI ligado no OSD do monitor (para brilho/contraste/ganhos). A curva de gamma e a LUT
  funcionam mesmo sem DDC/CI.

**Se o Windows bloquear o download** ("O Windows protegeu o seu PC"): clique em
**Mais informações → Executar assim mesmo**. Arquivos baixados do GitHub não são assinados
digitalmente; o app instalado pelo setup já fica sem essa marca.

**Se a janela não aparecer**: o app pode já estar rodando invisível (autostart). Abra de
novo pelo atalho: a segunda execução pede para a primeira mostrar a janela. Se ainda assim
não abrir, veja o log em `%APPDATA%\ColorMatch\apply.log`.

## Calibração

Os valores de calibração (brilho, contraste, ganhos, gamma, realce) ficam no
`profile.xml` do usuário e **não são alterados pelo código**. O app só os aplica.

## Histórico

- **1.3.1** — a predefinição virou uma **lista visível em cada cartão** (mostra a predefinição
  ativa e aplica sem confirmação); dica fixa explicando que o cartão é um monitor e o
  "Perfil" do topo vale para os dois.
- **1.3.0** — predefinições do macOS **por monitor** (botão dentro de cada cartão, muda só a
  curva daquele monitor); abertura instantânea (leitura DDC em segundo plano, sem segurar a
  janela); pedido de "mostrar janela" com nova tentativa quando o app já está rodando
  invisível; log de erro fatal e diagnóstico de início.
- **1.2.0** — lista de perfis aplicáveis (criar/aplicar/renomear/excluir) com os perfis do
  macOS na mesma lista; aplicar perfil do macOS muda só a curva e preserva a calibração.
- **1.1.0** — barra de comandos agrupada e rotulada; preset "Generic RGB (macOS)" (gamma efetivo 1.8);
  autostart persistente no perfil e reparado automaticamente; re-aplicação da LUT após exibir a janela;
  confirmação antes de aplicar presets.
- **1.0.0** — primeira versão pública.

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
