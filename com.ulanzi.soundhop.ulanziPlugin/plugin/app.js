import { UlanziApi } from './ulanzi-api/index.js';
import AudioService from './audio/AudioService.js';
import { normalizeSettings, nextDevice, labelFor } from './audio/cycle.js';
import { writeKeyImage, clearKeyImages, clearAllKeyImages } from './keyImage.js';

const PLUGIN_UUID = 'com.ulanzi.ulanzistudio.soundhop';
const ICONS = { render: 'assets/icons/output.png', capture: 'assets/icons/input.png' };
const POLL_INTERVAL = 2000;

const $UD = new UlanziApi();
const audio = new AudioService();

// context -> { flow: 'render' | 'capture', settings, active, lastLabel }
const ACTIONS = {};

const MESSAGES = {
  en: {
    noSelected: 'None of the selected devices is connected',
    noDevices: 'No audio device found',
  },
  pt: {
    noSelected: 'Nenhum dos dispositivos selecionados está conectado',
    noDevices: 'Nenhum dispositivo de áudio encontrado',
  },
};

function t(key) {
  const lang = String($UD.language || 'en').startsWith('pt') ? 'pt' : 'en';
  return MESSAGES[lang][key];
}

function flowOf(uuid = '') {
  return uuid.endsWith('.input') ? 'capture' : 'render';
}

function upsert(jsn) {
  const context = jsn.context;
  const action = ACTIONS[context] || { flow: flowOf(jsn.uuid), active: true, lastLabel: null };
  if (jsn.param && Object.keys(jsn.param).length) {
    action.settings = normalizeSettings(jsn.param);
  } else if (!action.settings) {
    action.settings = normalizeSettings({});
  }
  ACTIONS[context] = action;
  return action;
}

function render(context, devices) {
  const action = ACTIONS[context];
  if (!action || !action.active) return;
  const current = devices[action.flow].find((d) => d.isDefault);
  const text = labelFor(current, action.settings.label);
  if (text === action.lastLabel) return;
  action.lastLabel = text;
  try {
    $UD.setPathIcon(context, writeKeyImage(context, ICONS[action.flow], text), '');
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

clearAllKeyImages();
$UD.connect(PLUGIN_UUID);

$UD.onConnected(() => {
  audio.start().catch((err) => $UD.logMessage(`audio host: ${err.message}`, 'error'));
});

$UD.onAdd((jsn) => {
  const action = upsert(jsn);
  action.lastLabel = null;
  refresh([jsn.context]);
});

$UD.onParamFromApp((jsn) => {
  const action = upsert(jsn);
  action.lastLabel = null;
  refresh([jsn.context]);
});

$UD.onParamFromPlugin((jsn) => {
  const action = upsert(jsn);
  action.lastLabel = null;
  refresh([jsn.context]);
});

$UD.onSetActive((jsn) => {
  const action = ACTIONS[jsn.context];
  if (!action) return;
  action.active = jsn.active;
  if (jsn.active) {
    action.lastLabel = null;
    refresh([jsn.context]);
  }
});

$UD.onClear((jsn) => {
  (jsn.param || []).forEach((item) => {
    delete ACTIONS[item.context];
    clearKeyImages(item.context);
  });
});

$UD.onRun(async (jsn) => {
  const context = jsn.context;
  const action = ACTIONS[context] || upsert(jsn);
  try {
    const devices = await audio.list();
    const target = nextDevice(devices[action.flow], action.settings);
    if (!target) {
      $UD.showAlert(context);
      $UD.toast(t(action.settings.mode === 'selected' ? 'noSelected' : 'noDevices'));
      return;
    }
    await audio.setDefault(target.id);

    // Update every key showing this flow, not only the one pressed.
    const updated = await audio.list();
    Object.keys(ACTIONS).forEach((ctx) => render(ctx, updated));
  } catch (err) {
    $UD.showAlert(context);
    $UD.logMessage(`switch failed: ${err.message}`, 'error');
  }
});

// Property inspector asks for the device list of its action's flow.
$UD.onSendToPlugin(async (jsn) => {
  const payload = jsn.payload || {};
  if (payload.cmd !== 'getDevices') return;
  try {
    const devices = await audio.list();
    $UD.sendToPropertyInspector({
      cmd: 'devices',
      flow: flowOf(jsn.uuid),
      devices: devices[flowOf(jsn.uuid)],
    }, jsn.context);
  } catch (err) {
    $UD.sendToPropertyInspector({ cmd: 'devices', error: err.message, devices: [] }, jsn.context);
  }
});

// Keep the key labels in sync when the default device is changed elsewhere
// (Windows sound settings, another app, device unplugged...).
setInterval(() => {
  const visible = Object.keys(ACTIONS).filter((ctx) => ACTIONS[ctx].active);
  refresh(visible);
}, POLL_INTERVAL);

process.on('exit', () => audio.stop());
