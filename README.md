# ColorMatch

> Iguala cor e brilho entre monitores no Windows.

**Versão atual: 1.5.0** — cada correção sobe `1.0.x`; cada recurso novo sobe `1.x.0`.

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
  Generic Gray 2.2, Generic Gray, ProPhoto RGB e LG FULL HD) e os **perfis ICC importados**.
  Ela mostra a predefinição ativa daquele monitor e aplica **só nele**, mudando **só a
  curva de gamma**; brilho/contraste/ganhos calibrados são mantidos.
- **Importar ICC**: "Importar perfil ICC..." lê a curva (TRC) do perfil e aplica a
  compensação que o macOS faria (perfil γ1.96 vira curva 0.89, por exemplo). As primárias
  (gamut) não são reproduzíveis por LUT; perfis com `vcgt` são avisados. Os arquivos ficam
  em `%APPDATA%\ColorMatch\icc\`.
- **Filtro de cor por monitor**: temperatura (frio ↔ quente, azul ↔ vermelho) e matiz
  (verde ↔ magenta) direto na LUT; **saturação** via DDC/CI (VCP 0x8A) quando o monitor
  expõe esse controle (senão, use o OSD do monitor).
- **Tema "Mac (nativo)" embutido**: predefinição pronta que reproduz o visual capturado no
  macOS 26.7 com os mesmos dois monitores (perfil ativo **Generic RGB**, γ1.80, LUT do
  sistema em identidade). Fica na lista **Perfil** já na primeira execução: AOC 24G50F
  62/54 com ganhos 51/53/68, Acer KA242Y E 75/55 com 50/50/50 e **Gamma 0,81** nos dois
  (cinza 50% = 0,427, o mesmo que o macOS manda para a tela). É original protegido:
  editar cria "Mac (nativo) (editado)".
- **Originais protegidos**: editar um perfil salvo ou uma predefinição cria automaticamente
  uma cópia (**"nome (editado)"**); o original nunca é sobrescrito.
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

## Tema "Mac (nativo)"

Predefinição que reproduz o visual do macOS 26.7 nos dois monitores (AOC 24G50F +
Acer KA242Y E), capturado em 29/10/2026 com eles ligados no Mac:

| | AOC 24G50F | Acer KA242Y E |
|---|---|---|
| DDC brilho/contraste | 62 / 54 | 75 / 55 |
| Ganhos R/G/B | 51 / 53 / 68 | 50 / 50 / 50 |
| OSD (VCP) | preset **Usuário** (0x14=11), preto R/B 50 (0x6C/0x6E) | idem |
| Gamma (curva) | 0,81 | 0,81 |
| Origem | perfil ativo **Generic RGB** (γ1.80), LUT do sistema em identidade | idem |

A conta é a mesma do `Icc.cs`: a curva do perfil aplicada sobre linear sRGB deixa o cinza
50% em **0,427** (o app chega em 0,4270 com Gamma 0,81). A partir da 1.5.0 o tema entra
sozinho na lista **Perfil** na primeira execução.

O tema também carrega os controles do **OSD** lidos por DDC/CI (preset de cor e níveis de
preto); ao aplicar o perfil o app reescreve só o que estiver diferente — e escreve o preset
antes de brilho/ganhos, porque trocar o preset reinicializa esses valores no monitor.

Sem recompilar nada, dá para instalar só o perfil (vale para a 1.4.0 também):

```bat
copy presets\Mac-nativo.xml "%APPDATA%\ColorMatch\perfis\"
```

Depois reabra o ColorMatch e escolha **"Mac (nativo)"** na lista Perfil.

**Limite:** uma LUT por canal reproduz a curva (contraste/gamma) e o ponto branco; as
primárias (gamut) do perfil não são reproduzíveis — a saturação extra do macOS não vem
junto. Se o monitor estiver em outro perfil de cor no Mac, o tema não corresponde.

## Calibração

Os valores de calibração (brilho, contraste, ganhos, gamma, realce) ficam no
`profile.xml` do usuário e **não são alterados pelo código**. O app só os aplica.

## Histórico

- **1.5.0** — tema embutido **"Mac (nativo)"**: captura do visual do macOS 26.7 nos dois
  monitores (perfil Generic RGB γ1.80 + LUT identidade) traduzida para o app — Gamma 0,81
  (cinza 50% = 0,427) e o DDC/OSD que já estava gravado neles (AOC 62/54 ganhos 51/53/68;
  Acer 75/55 ganhos 50/50/50; preset de cor 0x14=11 e níveis de preto 0x6C/0x6E=50).
  Aplicar um perfil agora **reaplica os extras do OSD** (só os seguros, e só se estiverem
  diferentes), sempre antes de brilho/contraste/ganhos. Corrige também a direção das
  predefinições de γ1.8 (Apple RGB, Generic RGB, Generic Gray, ProPhoto): agora
  **escurecem** os meios-tons como o macOS faz (antes usavam 1.22, que clareava). As
  primárias (gamut) continuam fora do alcance de uma LUT por canal.
- **1.4.0** — importação de perfis **ICC** (curva/TRC, preset "LG FULL HD (macOS)"); **filtro
  de cor** por monitor (temperatura frio/quente, matiz verde/magenta e saturação por DDC
  quando o monitor suporta); **originais protegidos** (editar um perfil salvo/predefinição
  cria "nome (editado)" e nunca sobrescreve o original).
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
  Icc.cs        - leitura de perfis ICC (TRC/vcgt)
  BuiltinThemes.cs - temas embutidos (Mac nativo)
  App.cs        - janela principal, vigilância, arrasto
build.cmd
build_installer.cmd
```
