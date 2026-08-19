#!/usr/bin/env node
import { spawn } from 'node:child_process';
import { createReadStream, statSync } from 'node:fs';
import { mkdtemp, rm } from 'node:fs/promises';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const webRoot = path.join(repoRoot, 'public');
const port = Number(process.env.WEB_SMOKE_PORT ?? 5279);
const debugPort = Number(process.env.WEB_SMOKE_DEBUG_PORT ?? 19433);
const chrome = process.env.CHROME_BIN ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const profile = await mkdtemp(path.join(os.tmpdir(), '2dtd-static-web-smoke-'));
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
  ['.wasm', 'application/wasm'], ['.pck', 'application/octet-stream'],
  ['.png', 'image/png'], ['.json', 'application/json; charset=utf-8'],
]);
const server = http.createServer((request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url ?? '/', `http://127.0.0.1:${port}/`).pathname);
    const relative = pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, '');
    const file = path.resolve(webRoot, relative);
    if (!file.startsWith(`${webRoot}${path.sep}`) && file !== path.join(webRoot, 'index.html')) {
      response.writeHead(403).end(); return;
    }
    const info = statSync(file);
    if (!info.isFile()) { response.writeHead(404).end(); return; }
    response.writeHead(200, { 'Content-Type': mime.get(path.extname(file).toLowerCase()) ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    createReadStream(file).pipe(response);
  } catch { response.writeHead(404).end(); }
});
await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });

const proc = spawn(chrome, [
  '--headless=new', `--remote-debugging-port=${debugPort}`, '--remote-allow-origins=*',
  `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check',
  '--window-size=844,390', 'about:blank',
], { stdio: ['ignore', 'ignore', 'pipe'] });
let stderr = '';
proc.stderr.on('data', chunk => { stderr += chunk.toString(); });

let target;
for (let i = 0; i < 100; i++) {
  try {
    const targets = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
    target = targets.find(x => x.type === 'page');
    if (target) break;
  } catch {}
  await sleep(100);
}
if (!target) throw new Error(`Chrome DevTools target unavailable: ${stderr}`);

const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
let id = 0;
const pending = new Map();
const errors = [];
const consoles = [];
ws.onmessage = event => {
  const message = JSON.parse(event.data);
  if (message.method === 'Runtime.exceptionThrown') errors.push(`exception: ${message.params?.exceptionDetails?.text ?? 'unknown'}`);
  if (message.method === 'Log.entryAdded') {
    const entry = message.params?.entry;
    if (entry?.level === 'error') errors.push(`log: ${entry.text}`);
  }
  if (message.method === 'Runtime.consoleAPICalled') {
    const values = (message.params?.args ?? []).map(x => x.value ?? x.description ?? '').join(' ');
    consoles.push(values);
  }
  if (message.id && pending.has(message.id)) {
    const handler = pending.get(message.id); pending.delete(message.id);
    message.error ? handler.reject(new Error(message.error.message)) : handler.resolve(message.result);
  }
};
const cdp = (method, params = {}) => new Promise((resolve, reject) => {
  const next = ++id; pending.set(next, { resolve, reject }); ws.send(JSON.stringify({ id: next, method, params }));
});
const evaluate = async expression => {
  const response = await cdp('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (response.exceptionDetails) throw new Error(response.exceptionDetails.text ?? 'Runtime evaluation failed');
  return response.result?.value;
};

try {
  await cdp('Runtime.enable'); await cdp('Log.enable'); await cdp('Page.enable');
  await cdp('Emulation.setDeviceMetricsOverride', { width: 844, height: 390, deviceScaleFactor: 1, mobile: false });
  await cdp('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  let ready = false;
  for (let i = 0; i < 180; i++) {
    ready = await evaluate(`document.querySelector('canvas')?.width === 844 && document.querySelector('canvas')?.height === 390`);
    if (ready && consoles.some(x => x.includes('Godot Engine v4.6.3'))) break;
    await sleep(100);
  }
  if (!ready) throw new Error('Godot Web canvas did not reach 844x390');
  if (!consoles.some(x => x.includes('WebGL 2.0'))) throw new Error(`WebGL2 startup log missing: ${consoles.join(' | ')}`);
  const productErrors = errors.filter(x => !x.includes('AudioContext'));
  if (productErrors.length) throw new Error(`Godot Web runtime errors: ${productErrors.join(' | ')}`);
  console.log('Static Godot Web smoke: OK canvas=844x390 webgl=2 source=' + (await (await fetch(`http://127.0.0.1:${port}/SOURCE_REVISION.txt`).catch(() => null))?.text?.() ?? 'artifact'));
} finally {
  ws.close(); proc.kill('SIGTERM'); server.close(); await sleep(200); await rm(profile, { recursive: true, force: true });
}
