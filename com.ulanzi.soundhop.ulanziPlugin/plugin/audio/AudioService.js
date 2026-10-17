import { spawn } from 'child_process';
import { createInterface } from 'readline';
import path from 'path';
import { fileURLToPath } from 'url';

const HOST_SCRIPT = path.join(path.dirname(fileURLToPath(import.meta.url)), 'audio-host.ps1');
const REQUEST_TIMEOUT = 8000;

/**
 * Talks to a long-running PowerShell process (audio-host.ps1) that wraps the
 * Windows Core Audio APIs. Keeping it alive avoids paying the C# compile cost
 * on every key press.
 */
export default class AudioService {
  constructor() {
    this.proc = null;
    this.ready = null;
    this.nextId = 1;
    this.pending = new Map();
  }

  start() {
    if (this.ready) return this.ready;

    this.ready = new Promise((resolve, reject) => {
      const proc = spawn('powershell.exe', [
        '-NoProfile', '-NoLogo', '-NonInteractive',
        '-ExecutionPolicy', 'Bypass',
        '-File', HOST_SCRIPT,
      ], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
      this.proc = proc;

      const rl = createInterface({ input: proc.stdout, crlfDelay: Infinity });
      rl.on('line', (line) => {
        let msg;
        try { msg = JSON.parse(line); } catch { return; }

        if (msg.id === 0) {
          if (msg.ok) resolve();
          else reject(new Error(msg.error || 'audio host failed to start'));
          return;
        }

        const req = this.pending.get(msg.id);
        if (!req) return;
        this.pending.delete(msg.id);
        clearTimeout(req.timer);
        if (msg.ok) req.resolve(msg.data);
        else req.reject(new Error(msg.error));
      });

      proc.stderr.on('data', (d) => console.warn('[audio-host]', d.toString()));

      proc.on('exit', (code) => {
        console.warn(`[audio-host] exited with code ${code}`);
        reject(new Error(`audio host exited (${code})`));
        for (const req of this.pending.values()) {
          clearTimeout(req.timer);
          req.reject(new Error('audio host exited'));
        }
        this.pending.clear();
        if (this.proc === proc) {
          this.proc = null;
          this.ready = null; // next request respawns it
        }
      });
    });

    return this.ready;
  }

  async request(line) {
    await this.start();
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`audio host timeout: ${line}`));
      }, REQUEST_TIMEOUT);
      this.pending.set(id, { resolve, reject, timer });
      this.proc.stdin.write(`${id} ${line}\n`);
    });
  }

  /** @returns {Promise<{render: Device[], capture: Device[]}>} */
  list() {
    return this.request('list');
  }

  /** Makes the device the default for console, multimedia and communications. */
  setDefault(deviceId) {
    if (!/^[\w{}.\-]+$/.test(deviceId)) {
      return Promise.reject(new Error(`invalid device id: ${deviceId}`));
    }
    return this.request(`set ${deviceId}`);
  }

  stop() {
    if (this.proc) {
      this.proc.stdin.write('0 exit\n');
      this.proc.kill();
    }
  }
}

/**
 * @typedef {object} Device
 * @property {string} id
 * @property {string} name   Full friendly name, e.g. "Speakers (Realtek(R) Audio)"
 * @property {string} desc   e.g. "Speakers"
 * @property {string} iface  e.g. "Realtek(R) Audio"
 * @property {boolean} isDefault
 * @property {boolean} isDefaultComm
 */
