<p align="center">
  <img src="com.ulanzi.soundhop.ulanziPlugin/assets/icons/plugin.png" width="96" alt="SoundHop">
</p>

<h1 align="center">SoundHop</h1>

<p align="center">
  Troque e mute seus dispositivos de áudio com um toque no Ulanzi Deck.<br>
  <em>Switch and mute your audio devices with one key press on the Ulanzi Deck.</em>
</p>

---

Plugin para o **Ulanzi Studio** (Windows) com quatro ações:

| Ação | O que faz |
|------|-----------|
| **Alternar Saída** | Pula para o próximo alto-falante / fone / headset |
| **Alternar Entrada** | Pula para o próximo microfone |
| **Mutar Microfone** | Muta / desmuta o microfone, com indicador na tecla |
| **Mutar Áudio** | Muta / desmuta a saída de áudio, com indicador na tecla |

Cada troca define o dispositivo como **Dispositivo Padrão** *e* **Dispositivo de Comunicação Padrão**, então Discord, Teams e afins acompanham a troca.

## Recursos

**Alternar**
- Alternar entre **todos** os dispositivos ativos ou só entre os **selecionados**.
- O nome do dispositivo atual aparece na tecla (nome curto, nome completo, tipo ou nenhum).
- Dispositivos desconectados continuam salvos na seleção e voltam ao ciclo quando reconectados.

**Mutar**
- Mute global no nível do Windows: vale para qualquer app (Discord, Teams, Meet, jogos…).
- Indicador: a tecla fica **vermelha com o ícone riscado** quando mudo e mostra `MUDO` / `ATIVO` (ou o nome do dispositivo).
- Aplica no dispositivo padrão ou em **todos** os dispositivos de uma vez.
- Microfone com modo **push-to-talk**: segure a tecla para falar, solte para mutar.

**Geral**
- As teclas acompanham mudanças feitas fora do plugin (configurações do Windows, botão do headset, Discord…).
- Leve: um helper nativo de 17 KB compilado com o `csc.exe` que já vem no Windows, usando as APIs de áudio do próprio sistema (`IMMDeviceEnumerator`, `IPolicyConfig`, `IAudioEndpointVolume`). Sem PowerShell, sem executáveis de terceiros.
- Orientado a eventos: o Windows avisa quando o dispositivo padrão ou o mute muda, então nada fica consultando o sistema em loop.
- Interface em português e inglês.

## Instalação

1. Baixe o `SoundHop-x.y.z.zip` em [Releases](../../releases).
2. Extraia a pasta `com.ulanzi.soundhop.ulanziPlugin` em `%APPDATA%\Ulanzi\UlanziDeck\Plugins\`.
3. Reinicie o Ulanzi Studio. As ações aparecem na categoria **SoundHop**.

### A partir do código-fonte

```bash
powershell -ExecutionPolicy Bypass -File tools/build-helper.ps1
cd com.ulanzi.soundhop.ulanziPlugin && npm install --omit=dev
```

Copie a pasta (com `bin` e `node_modules`) para o diretório de plugins acima. Para gerar o zip de release (já compila o helper):

```bash
powershell -ExecutionPolicy Bypass -File tools/pack.ps1
```

## Requisitos

- Windows 10 ou 11
- Ulanzi Studio 2.1.4+ (testado com Ulanzi Studio 3)

## Desenvolvimento

```bash
cd com.ulanzi.soundhop.ulanziPlugin
node plugin/audio/cli.js              # lista dispositivos (* = padrão, C = comunicação)
node plugin/audio/cli.js next output  # troca para a próxima saída
node plugin/audio/cli.js next input   # troca para a próxima entrada
```

Com o [UlanziDeck Simulator](https://github.com/UlanziTechnology/UlanziDeckPlugin-SDK), inicie o serviço apontando para a porta do simulador. Sem argumentos ele conecta na porta 3906, que é a do Ulanzi Studio real:

```bash
node plugin/app.js 127.0.0.1 39069 pt_PT
```

### Estrutura

```
native/SoundHopHelper.cs        # helper nativo (Core Audio via COM + eventos)
tools/build-helper.ps1          # compila o helper com o csc.exe do Windows
com.ulanzi.soundhop.ulanziPlugin/
├── bin/soundhop-helper.exe     # gerado pelo build-helper.ps1
├── manifest.json
├── plugin/
│   ├── app.js              # serviço principal (Node)
│   ├── keyImage.js         # desenha ícone + nome do dispositivo na tecla
│   ├── audio/
│   │   ├── AudioService.js     # ponte Node ↔ helper nativo
│   │   ├── cycle.js            # lógica de rotação
│   │   └── mute.js             # lógica de mute
│   └── ulanzi-api/         # SDK Node da Ulanzi
├── property-inspector/     # tela de configuração
└── libs/                   # SDK HTML da Ulanzi
```

## Licença

[Apache 2.0](LICENSE). Inclui as bibliotecas do [UlanziDeck Plugin SDK](https://github.com/UlanziTechnology/UlanziDeckPlugin-SDK) (Apache 2.0).
