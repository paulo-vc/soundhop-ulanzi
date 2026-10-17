import { spawn } from 'child_process';
import { EventEmitter } from 'events';
import { createInterface } from 'readline';
import path from 'path';
import { fileURLToPath } from 'url';

const EXE = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'bin', 'soundhop-helper.exe');
const REQUEST_TIMEOUT = 8000;

/**
 * Talks to bin/soundhop-helper.exe, a small native process built from
 * native/SoundHopHelper.cs that wraps the Windows Core Audio APIs.
 * Requests are tab-separated lines. The helper emits 'audio' whenever a
 * default device or a mute state changes, so nothing here polls.
 */
export default class AudioService extends EventEmitter {
  constructor() {
    super();
    this.proc = null;
    this.ready = null;
    this.nextId = 1;
    this.pending = new Map();
  }

  start() {
    if (this.ready) return this.ready;

    this.ready = new Promise((resolve, reject) => {
      const proc = spawn(EXE, [], { windowsHide: true, stdio: ['pipe', 'pipe', 'ignore'] });
      this.proc = proc;

      createInterface({ input: proc.stdout, crlfDelay: Infinity }).on('line', (line) => {
        let msg;
        try { msg = JSON.parse(line); } catch { return; }

        if (msg.event) return this.emit(msg.event, msg);
        if (msg.id === 0) return msg.ok ? resolve() : reject(new Error(msg.error || 'audio helper failed to start'));

        const req = this.pending.get(msg.id);
        if (!req) return;
        this.pending.delete(msg.id);
        clearTimeout(req.timer);
        if (msg.ok) req.resolve(msg.data);
        else req.reject(new Error(msg.error));
      });

      proc.on('error', reject);
      proc.on('exit', (code) => {
        reject(new Error(`audio helper exited (${code})`));
        for (const req of this.pending.values()) {
          clearTimeout(req.timer);
          req.reject(new Error('audio helper exited'));
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

  async request(...parts) {
    await this.start();
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`audio helper timeout: ${parts[0]}`));
      }, REQUEST_TIMEOUT);
      this.pending.set(id, { resolve, reject, timer });
      this.proc.stdin.write([id, ...parts].join('\t') + '\n');
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
    return this.request('set', deviceId);
  }

  mute(deviceId, mute) {
    if (!/^[\w{}.\-]+$/.test(deviceId)) {
      return Promise.reject(new Error(`invalid device id: ${deviceId}`));
    }
    return this.request('mute', deviceId, mute ? 1 : 0);
  }

  stop() {
    if (!this.proc) return;
    try { this.proc.stdin.write('0\texit\n'); } catch {}
    this.proc.kill();
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
 * @property {boolean} muted
 */
