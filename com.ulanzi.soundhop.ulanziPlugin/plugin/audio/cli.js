// Manual test helper, no Ulanzi Studio needed:
//   node plugin/audio/cli.js                 -> list devices
//   node plugin/audio/cli.js next output     -> switch to the next output device
//   node plugin/audio/cli.js next input      -> switch to the next input device
import AudioService from './AudioService.js';
import { nextDevice, normalizeSettings } from './cycle.js';

const audio = new AudioService();
const [cmd, which] = process.argv.slice(2);

try {
  const all = await audio.list();
  if (cmd === 'next') {
    const flow = which === 'input' ? 'capture' : 'render';
    const target = nextDevice(all[flow], normalizeSettings({}));
    if (target) {
      await audio.setDefault(target.id);
      console.log(`Default ${flow} -> ${target.name}`);
    }
  } else {
    for (const flow of ['render', 'capture']) {
      console.log(flow === 'render' ? 'Output:' : 'Input:');
      for (const d of all[flow]) {
        console.log(`  ${d.isDefault ? '*' : ' '}${d.isDefaultComm ? 'C' : ' '} ${d.name}`);
      }
    }
  }
} catch (err) {
  console.error(err.message);
  process.exitCode = 1;
} finally {
  audio.stop();
}
