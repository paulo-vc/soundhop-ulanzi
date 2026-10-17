let SETTINGS = { mode: 'all', devices: [], label: 'iface' };
let DEVICES = [];
let form = null;

$UD.connect();

$UD.onConnected(() => {
  form = document.querySelector('#property-inspector');
  document.querySelector('.uspi-wrapper').classList.remove('hidden');

  form.addEventListener('change', save);
  document.querySelector('.refresh').addEventListener('click', requestDevices);

  requestDevices();
  applySettings();
});

// Saved settings arrive through either event depending on how the PI was opened.
$UD.onAdd((jsn) => jsn && jsn.param && loadSettings(jsn.param));
$UD.onParamFromApp((jsn) => jsn && jsn.param && loadSettings(jsn.param));

$UD.onSendToPropertyInspector((jsn) => {
  const payload = jsn.payload || {};
  if (payload.cmd !== 'devices') return;
  DEVICES = payload.devices || [];
  renderDevices();
});

function requestDevices() {
  $UD.sendToPlugin({ cmd: 'getDevices' });
}

function loadSettings(param) {
  let devices = param.devices || [];
  if (!Array.isArray(devices)) devices = [devices];
  SETTINGS = {
    mode: param.mode === 'selected' ? 'selected' : 'all',
    devices,
    label: param.label || 'iface',
  };
  applySettings();
}

function applySettings() {
  if (!form) return;
  form.querySelector(`input[name="mode"][value="${SETTINGS.mode}"]`).checked = true;
  form.querySelector('select[name="label"]').value = SETTINGS.label;
  renderDevices();
}

function renderDevices() {
  if (!form) return;
  form.querySelector('.device-section').classList.toggle('hidden', SETTINGS.mode !== 'selected');

  const list = form.querySelector('.device-list');
  list.innerHTML = '';

  // Keep saved devices that are currently unplugged so the selection isn't lost.
  const known = new Set(DEVICES.map((d) => d.id));
  const missing = SETTINGS.devices
    .filter((id) => !known.has(id))
    .map((id) => ({ id, name: $UD.t('Disconnected device'), missing: true }));

  [...DEVICES, ...missing].forEach((device, i) => {
    const row = document.createElement('div');
    row.className = 'uspi-item-child' + (device.missing ? ' device-missing' : '');

    const input = document.createElement('input');
    input.type = 'checkbox';
    input.name = 'devices';
    input.id = `device_${i}`;
    input.value = device.id;
    input.checked = SETTINGS.devices.includes(device.id);

    const label = document.createElement('label');
    label.htmlFor = input.id;
    label.textContent = device.name;
    if (device.isDefault) {
      const tag = document.createElement('span');
      tag.className = 'device-default';
      tag.textContent = '● ' + $UD.t('current');
      label.appendChild(tag);
    }

    row.append(input, label);
    list.appendChild(row);
  });

  if (!DEVICES.length && !missing.length) {
    list.textContent = $UD.t('Loading devices...');
  }
}

function save() {
  const value = Utils.getFormValue(form);
  let devices = value.devices || [];
  if (!Array.isArray(devices)) devices = [devices];
  SETTINGS = {
    mode: value.mode === 'selected' ? 'selected' : 'all',
    devices,
    label: value.label || 'iface',
  };
  renderDevices();
  $UD.sendParamFromPlugin(SETTINGS);
}
