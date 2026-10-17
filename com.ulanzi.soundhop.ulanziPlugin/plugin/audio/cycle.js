/**
 * Normalizes the settings saved by the property inspector.
 * Checkbox groups come back as a string when only one item is checked.
 */
export function normalizeSettings(param = {}) {
  let devices = param.devices || [];
  if (!Array.isArray(devices)) devices = [devices];
  return {
    mode: param.mode === 'selected' ? 'selected' : 'all',
    devices: devices.filter(Boolean),
    label: ['iface', 'name', 'desc', 'none'].includes(param.label) ? param.label : 'iface',
  };
}

/** Devices the key should rotate through, in system order. */
export function candidates(devices, settings) {
  if (settings.mode !== 'selected') return devices;
  return devices.filter((d) => settings.devices.includes(d.id));
}

/** The device that should become default on the next press, or null. */
export function nextDevice(devices, settings) {
  const list = candidates(devices, settings);
  if (list.length === 0) return null;
  const idx = list.findIndex((d) => d.isDefault);
  return list[(idx + 1) % list.length];
}

export function labelFor(device, label) {
  if (!device || label === 'none') return '';
  return device[label] || device.name || '';
}
