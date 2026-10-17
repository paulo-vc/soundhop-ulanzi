let SETTINGS = { scope: 'default', mode: 'toggle', label: 'state' };
let form = null;

$UD.connect();

$UD.onConnected(() => {
  form = document.querySelector('#property-inspector');
  document.querySelector('.uspi-wrapper').classList.remove('hidden');

  // Push-to-talk only makes sense for the microphone.
  const isMic = String($UD.uuid || '').endsWith('.mutemic');
  form.querySelector('.mode-section').classList.toggle('hidden', !isMic);

  form.addEventListener('change', save);
  applySettings();
});

// Saved settings arrive through either event depending on how the PI was opened.
$UD.onAdd((jsn) => jsn && jsn.param && loadSettings(jsn.param));
$UD.onParamFromApp((jsn) => jsn && jsn.param && loadSettings(jsn.param));

function normalize(param) {
  return {
    scope: param.scope === 'all' ? 'all' : 'default',
    mode: param.mode === 'ptt' ? 'ptt' : 'toggle',
    label: param.label || 'state',
  };
}

function loadSettings(param) {
  SETTINGS = normalize(param);
  applySettings();
}

function applySettings() {
  if (!form) return;
  form.querySelector(`input[name="scope"][value="${SETTINGS.scope}"]`).checked = true;
  form.querySelector(`input[name="mode"][value="${SETTINGS.mode}"]`).checked = true;
  form.querySelector('select[name="label"]').value = SETTINGS.label;
}

function save() {
  SETTINGS = normalize(Utils.getFormValue(form));
  $UD.sendParamFromPlugin(SETTINGS);
}
