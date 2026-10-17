/** Normalizes the settings saved by the mute property inspector. */
export function normalizeMuteSettings(param = {}) {
  return {
    scope: param.scope === 'all' ? 'all' : 'default',
    mode: param.mode === 'ptt' ? 'ptt' : 'toggle',
    label: ['state', 'iface', 'name', 'none'].includes(param.label) ? param.label : 'state',
  };
}

/** Devices affected by the key: the default one, or every active device. */
export function muteTargets(devices, settings) {
  if (settings.scope === 'all') return devices;
  return devices.filter((d) => d.isDefault);
}

/** A key reads as muted only when every device it controls is muted. */
export function isMuted(devices, settings) {
  const targets = muteTargets(devices, settings);
  return targets.length > 0 && targets.every((d) => d.muted);
}
