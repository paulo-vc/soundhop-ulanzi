import { UlanziApi } from './ulanzi-api/index.js';
import AudioService from './audio/AudioService.js';
import { normalizeSettings, nextDevice, labelFor } from './audio/cycle.js';
import { normalizeMuteSettings, muteTargets, isMuted } from './audio/mute.js';
import { writeKeyImage, clearKeyImages, clearAllKeyImages } from './keyImage.js';

const PLUGIN_UUID = 'com.ulanzi.ulanzistudio.soundhop';
const POLL_INTERVAL = 1500;

// Action UUID suffix -> what the key does and which device flow it controls.
const KINDS = {
  output: { type: 'switch', flow: 'render' },
  input: { type: 'switch', flow: 'capture' },
  muteoutput: { type: 'mute', flow: 'render' },
  mutemic: { type: 'mute', flow: 'capture' },
};

const ICONS = {
  render: { on: 'assets/icons/output.png', off: 'assets/icons/speaker-muted.png' },
  capture: { on: 'assets/icons/input.png', off: 'assets/icons/mic-muted.png' },
};
const MUTED_BACKGROUND = '#3b1218';

const $UD = new UlanziApi();
const audio = new AudioService();

// context -> { type, flow, settings, active, lastRender }
const ACTIONS = {};

const MESSAGES = {
  en: {
    noSelected: 'None of the selected devices is connected',
    noDevices: 'No audio device found',
    muted: 'MUTED',
    live: 'LIVE',
  },
  pt: {
    noSelected: 'Nenhum dos dispositivos selecionados está conectado',
    noDevices: 'Nenhum dispositivo de áudio encontrado',
    muted: 'MUDO',
    live: 'ATIVO',
  },
};

function t(key) {
  const lang = String($UD.language || 'en').startsWith('pt') ? 'pt' : 'en';
  return MESSAGES[lang][key];
}

function kindOf(uuid = '') {
  return KINDS[uuid.split('.').pop()] || KINDS.output;
}

function upsert(jsn) {
  const context = jsn.context;
  const kind = kindOf(jsn.uuid);
  const normalize = kind.type === 'mute' ? normalizeMuteSettings : normalizeSettings;
  const action = ACTIONS[context] || { ...kind, active: true, lastRender: null };
  if (jsn.param && Object.keys(jsn.param).length) {
    action.settings = normalize(jsn.param);
  } else if (!action.settings) {
    action.settings = normalize({});
  }
  action.lastRender = null;
  ACTIONS[context] = action;
  return action;
}

function keyFace(action, devices) {
  const list = devices[action.flow];
  const current = list.find((d) => d.isDefault);

  if (action.type === 'switch') {
    return { icon: ICONS[action.flow].on, text: labelFor(current, action.settings.label) };
  }

  const muted = isMuted(list, action.settings);
  const { label } = action.settings;
  let text = '';
  if (label === 'state') text = muted ? t('muted') : t('live');
  else if (label !== 'none' && action.settings.scope !== 'all') text = labelFor(current, label);
  return {
    icon: muted ? ICONS[action.flow].off : ICONS[action.flow].on,
    text,
    background: muted ? MUTED_BACKGROUND : undefined,
  };
}

function render(context, devices) {
  const action = ACTIONS[context];
  if (!action || !action.active) return;
  const face = keyFace(action, devices);
  const signature = JSON.stringify(face);
  if (signature === action.lastRender) return;
  action.lastRender = signature;
  try {
    $UD.setPathIcon(context, writeKeyImage(context, face.icon, face.text, { background: face.background }), '');
  } catch (err) {
    $UD.logMessage(`render failed: ${err.message}`, 'error');
  }
}

async function refresh(contexts = Object.keys(ACTIONS)) {
  if (!contexts.length) return;
  try {
    const devices = await audio.list();
    contexts.forEach((ctx) => render(ctx, devices));
  } catch (err) {
    $UD.logMessage(`list failed: ${err.message}`, 'error');
  }
}

async function switchDevice(context, action) {
  const devices = await audio.list();
  const target = nextDevice(devices[action.flow], action.settings);
  if (!target) {
    $UD.showAlert(context);
    $UD.toast(t(action.settings.mode === 'selected' ? 'noSelected' : 'noDevices'));
    return;
  }
  await audio.setDefault(target.id);
}

/** mute: true / false, or undefined to toggle. */
async function setMute(context, action, mute) {
  const devices = (await audio.list())[action.flow];
  const targets = muteTargets(devices, action.settings);
  if (!targets.length) {
    $UD.showAlert(context);
    $UD.toast(t('noDevices'));
    return;
  }
  const next = mute ?? !isMuted(devices, action.settings);
  await Promise.all(targets.filter((d) => d.muted !== next).map((d) => audio.mute(d.id, next)));
}

async function handle(jsn, fn) {
  const context = jsn.context;
  const action = ACTIONS[context] || upsert(jsn);
  try {
    await fn(context, action);
    // Update every key, so mute and switch keys of the same flow stay in sync.
    await refresh();
  } catch (err) {
    $UD.showAlert(context);
    $UD.logMessage(`action failed: ${err.message}`, 'error');
  }
}

function isPushToTalk(action) {
  return action?.type === 'mute' && action.settings.mode === 'ptt';
}

clearAllKeyImages();
$UD.connect(PLUGIN_UUID);

$UD.onConnected(() => {
  audio.start().catch((err) => $UD.logMessage(`audio host: ${err.message}`, 'error'));
});

$UD.onAdd((jsn) => {
  upsert(jsn);
  refresh([jsn.context]);
});

$UD.onParamFromApp((jsn) => {
  upsert(jsn);
  refresh([jsn.context]);
});

$UD.onParamFromPlugin((jsn) => {
  upsert(jsn);
  refresh([jsn.context]);
});

$UD.onSetActive((jsn) => {
  const action = ACTIONS[jsn.context];
  if (!action) return;
  action.active = jsn.active;
  if (jsn.active) {
    action.lastRender = null;
    refresh([jsn.context]);
  }
});

$UD.onClear((jsn) => {
  (jsn.param || []).forEach((item) => {
    delete ACTIONS[item.context];
    clearKeyImages(item.context);
  });
});

$UD.onRun((jsn) => {
  const action = ACTIONS[jsn.context] || upsert(jsn);
  if (action.type === 'switch') handle(jsn, switchDevice);
  else if (!isPushToTalk(action)) handle(jsn, (ctx, a) => setMute(ctx, a));
});

// Push-to-talk: unmuted only while the key is held.
$UD.onKeyDown((jsn) => {
  if (isPushToTalk(ACTIONS[jsn.context])) handle(jsn, (ctx, a) => setMute(ctx, a, false));
});

$UD.onKeyUp((jsn) => {
  if (isPushToTalk(ACTIONS[jsn.context])) handle(jsn, (ctx, a) => setMute(ctx, a, true));
});

// Property inspector asks for the device list of its action's flow.
$UD.onSendToPlugin(async (jsn) => {
  const payload = jsn.payload || {};
  if (payload.cmd !== 'getDevices') return;
  const { flow } = kindOf(jsn.uuid);
  try {
    const devices = await audio.list();
    $UD.sendToPropertyInspector({ cmd: 'devices', flow, devices: devices[flow] }, jsn.context);
  } catch (err) {
    $UD.sendToPropertyInspector({ cmd: 'devices', error: err.message, devices: [] }, jsn.context);
  }
});

// Keep the keys in sync when the default device or mute state is changed
// elsewhere (Windows sound settings, Discord, a headset button...).
setInterval(() => {
  const visible = Object.keys(ACTIONS).filter((ctx) => ACTIONS[ctx].active);
  refresh(visible);
}, POLL_INTERVAL);

process.on('exit', () => audio.stop());
