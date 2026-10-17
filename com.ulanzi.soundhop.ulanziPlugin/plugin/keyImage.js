import fs from 'fs';
import path from 'path';
import crypto from 'crypto';
import { fileURLToPath } from 'url';

// Ulanzi Studio does not reliably draw the `textData` of a state update, so
// the device name is baked into an SVG written to disk and set as a path icon.

const PLUGIN_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const RUNTIME_DIR = 'runtime-icons';
const SIZE = 144;
const MAX_CHARS = 15;

const iconCache = new Map();
const slots = new Map();

function iconDataUri(iconPath) {
  if (!iconCache.has(iconPath)) {
    const data = fs.readFileSync(path.join(PLUGIN_ROOT, iconPath)).toString('base64');
    iconCache.set(iconPath, `data:image/png;base64,${data}`);
  }
  return iconCache.get(iconPath);
}

function escapeXml(s) {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[c]));
}

function ellipsize(s) {
  return s.length > MAX_CHARS ? s.slice(0, MAX_CHARS - 1).trimEnd() + '…' : s;
}

/** Splits a name into at most two lines, preferring to break at spaces/brackets. */
export function wrapText(text) {
  const clean = String(text || '').replace(/\s+/g, ' ').trim();
  if (clean.length <= MAX_CHARS) return clean ? [clean] : [];

  let cut = -1;
  for (let i = Math.min(MAX_CHARS, clean.length - 1); i > MAX_CHARS / 2; i--) {
    if (/[\s(\-_/]/.test(clean[i])) { cut = i; break; }
  }
  if (cut < 0) cut = MAX_CHARS;
  return [clean.slice(0, cut).trim(), ellipsize(clean.slice(cut).trim())].filter(Boolean);
}

export function renderSvg(iconPath, text) {
  const icon = iconDataUri(iconPath);
  const lines = wrapText(text);
  const iconBox = lines.length === 0 ? { x: 16, y: 16, s: 112 }
    : lines.length === 1 ? { x: 30, y: 10, s: 84 }
    : { x: 36, y: 6, s: 72 };
  const fontSize = lines.length > 1 ? 16 : 18;
  const firstY = lines.length > 1 ? 104 : 120;
  const textSvg = lines.map((line, i) =>
    `<text x="72" y="${firstY + i * 19}" text-anchor="middle" fill="#ffffff" ` +
    `font-family="Segoe UI,Microsoft YaHei,sans-serif" font-size="${fontSize}" font-weight="600">${escapeXml(line)}</text>`
  ).join('');

  return `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" version="1.1" ` +
    `width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}">` +
    `<rect width="${SIZE}" height="${SIZE}" fill="#17191d"/>` +
    `<image x="${iconBox.x}" y="${iconBox.y}" width="${iconBox.s}" height="${iconBox.s}" href="${icon}" xlink:href="${icon}"/>` +
    textSvg + `</svg>`;
}

/**
 * Writes the key image and returns its plugin-relative path. Alternates
 * between two files per key so the host never caches a stale image.
 */
export function writeKeyImage(context, iconPath, text) {
  const dir = path.join(PLUGIN_ROOT, RUNTIME_DIR);
  fs.mkdirSync(dir, { recursive: true });
  const id = crypto.createHash('sha1').update(context).digest('hex').slice(0, 12);
  const slot = (slots.get(context) ?? 1) ^ 1;
  slots.set(context, slot);
  const file = `${id}-${slot}.svg`;
  fs.writeFileSync(path.join(dir, file), renderSvg(iconPath, text), 'utf8');
  return `./${RUNTIME_DIR}/${file}`;
}

export function clearKeyImages(context) {
  const id = crypto.createHash('sha1').update(context).digest('hex').slice(0, 12);
  for (const slot of [0, 1]) {
    fs.rmSync(path.join(PLUGIN_ROOT, RUNTIME_DIR, `${id}-${slot}.svg`), { force: true });
  }
  slots.delete(context);
}

export function clearAllKeyImages() {
  fs.rmSync(path.join(PLUGIN_ROOT, RUNTIME_DIR), { recursive: true, force: true });
}
