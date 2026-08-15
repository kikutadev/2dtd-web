#!/usr/bin/env node
import { execFileSync, spawn } from 'node:child_process';
import { createReadStream, statSync } from 'node:fs';
import http from 'node:http';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..');
const port = Number(process.env.WEB_SMOKE_PORT ?? '5279');
const debugPort = Number(process.env.WEB_SMOKE_DEBUG_PORT ?? '19433');
const externalUrl = process.env.WEB_SMOKE_BASE_URL?.trim();
const url = externalUrl ? (externalUrl.endsWith('/') ? externalUrl : `${externalUrl}/`) : `http://127.0.0.1:${port}/`;
const chrome = process.env.CHROME_BIN ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const profileDir = await mkdtemp(path.join(os.tmpdir(), '2dtd-web-smoke-profile-'));
let publishDir;
let webRoot;
if (!externalUrl) {
  publishDir = await mkdtemp(path.join(os.tmpdir(), '2dtd-web-smoke-publish-'));
  execFileSync('dotnet', ['publish', 'src/DungeonDefense.Web/DungeonDefense.Web.csproj', '-c', 'Release', '--no-restore', '-o', publishDir], { cwd: repoRoot, stdio: 'pipe' });
  webRoot = path.join(publishDir, 'wwwroot');
}
const children = [];
let staticServer;

function start(command, args, options = {}) {
  const child = spawn(command, args, { cwd: repoRoot, stdio: ['ignore', 'pipe', 'pipe'], ...options });
  children.push(child);
  return child;
}

async function waitUntil(check, timeoutMs, label) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const value = await check();
      if (value) return value;
    } catch (error) {
      lastError = error;
    }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error(`Timed out waiting for ${label}${lastError ? `: ${lastError.message}` : ''}`);
}

async function fetchJson(endpoint) {
  const response = await fetch(endpoint);
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json();
}

let socket;
let nextId = 1;
const pending = new Map();
const browserDiagnostics = [];
function cdp(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
  });
}

async function evaluate(expression) {
  const response = await cdp('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (response.exceptionDetails) throw new Error(response.exceptionDetails.text ?? 'Runtime.evaluate failed');
  return response.result?.value;
}

async function waitForExpression(expression, timeoutMs, label) {
  return waitUntil(async () => await evaluate(expression), timeoutMs, label);
}

async function clickButtonContaining(text) {
  const literal = JSON.stringify(text);
  const clicked = await evaluate(`(() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent.includes(${literal}) && !x.disabled); if (!button) return false; button.click(); return true; })()`);
  if (!clicked) throw new Error(`Clickable button not found: ${text}`);
}

async function clickFirst(selector) {
  const literal = JSON.stringify(selector);
  const clicked = await evaluate(`(() => { const element = document.querySelector(${literal}); if (!element) return false; element.click(); return true; })()`);
  if (!clicked) throw new Error(`Clickable element not found: ${selector}`);
}

try {
  if (!externalUrl) {
    const contentTypes = new Map([
      ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript; charset=utf-8'],
      ['.css', 'text/css; charset=utf-8'], ['.json', 'application/json; charset=utf-8'],
      ['.wasm', 'application/wasm'], ['.png', 'image/png'], ['.svg', 'image/svg+xml'],
      ['.ico', 'image/x-icon'], ['.dat', 'application/octet-stream'],
    ]);
    staticServer = http.createServer((request, response) => {
      try {
        const pathname = decodeURIComponent(new URL(request.url ?? '/', url).pathname);
        const relative = pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, '');
        const filePath = path.resolve(webRoot, relative);
        if (!filePath.startsWith(`${webRoot}${path.sep}`) && filePath !== path.join(webRoot, 'index.html')) {
          response.writeHead(403).end();
          return;
        }
        const info = statSync(filePath);
        if (!info.isFile()) { response.writeHead(404).end(); return; }
        const extension = path.extname(filePath).toLowerCase();
        response.writeHead(200, { 'Content-Type': contentTypes.get(extension) ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        createReadStream(filePath).pipe(response);
      } catch {
        response.writeHead(404).end();
      }
    });
    await new Promise((resolve, reject) => {
      staticServer.once('error', reject);
      staticServer.listen(port, '127.0.0.1', resolve);
    });
  } else {
    await waitUntil(async () => (await fetch(url, { cache: 'no-store' })).ok, 15_000, 'public Web host');
  }

  const chromeProcess = start(chrome, [
    '--headless=new',
    `--remote-debugging-port=${debugPort}`,
    '--remote-allow-origins=*',
    `--user-data-dir=${profileDir}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-gpu',
    'about:blank',
  ]);
  let chromeError = '';
  chromeProcess.stderr.on('data', chunk => { chromeError += chunk.toString(); });

  const target = await waitUntil(async () => {
    const targets = await fetchJson(`http://127.0.0.1:${debugPort}/json/list`);
    return targets.find(x => x.type === 'page');
  }, 10_000, 'Chrome page target');

  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.onopen = resolve;
    socket.onerror = event => reject(new Error(`CDP websocket error: ${event.message ?? 'unknown'}`));
  });
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.method === 'Runtime.exceptionThrown') browserDiagnostics.push(`exception: ${message.params?.exceptionDetails?.text ?? 'unknown'}`);
    if (message.method === 'Log.entryAdded') browserDiagnostics.push(`log: ${message.params?.entry?.level ?? ''} ${message.params?.entry?.text ?? ''}`);
    if (message.method === 'Runtime.consoleAPICalled') {
      const values = (message.params?.args ?? []).map(x => x.value ?? x.description ?? '').join(' ');
      browserDiagnostics.push(`console: ${message.params?.type ?? ''} ${values}`);
    }
    if (!message.id || !pending.has(message.id)) return;
    const handler = pending.get(message.id);
    pending.delete(message.id);
    if (message.error) handler.reject(new Error(message.error.message));
    else handler.resolve(message.result);
  };

  await cdp('Runtime.enable');
  await cdp('Log.enable');
  await cdp('Page.enable');
  await cdp('Page.navigate', { url });
  try {
    await waitForExpression(`document.body?.innerText.includes('ダンジョン構築')`, 15_000, 'Build phase');
  } catch (error) {
    console.error('browser-diagnostics=', browserDiagnostics.join('\n'));
    console.error('browser-body=', await evaluate(`document.body?.innerText ?? ''`));
    console.error('browser-html=', String(await evaluate(`document.documentElement?.outerHTML ?? ''`)).slice(0, 4000));
    throw error;
  }

  const desktopHasPageOverflow = await evaluate(`document.documentElement.scrollWidth > window.innerWidth + 1`);
  if (desktopHasPageOverflow) throw new Error('Unexpected document-level horizontal overflow on desktop viewport');

  await clickFirst('.cell.build-valid');
  await waitForExpression(`document.body.innerText.includes('棘罠を配置しました')`, 3_000, 'Trap placement');

  await clickButtonContaining('スケルトン戦士');
  await clickFirst('.cell.build-valid');
  await waitForExpression(`document.body.innerText.includes('スケルトン戦士を配置しました')`, 3_000, 'Guard placement');

  await clickButtonContaining('矢狭間');
  await clickFirst('.cell.build-valid');
  await waitForExpression(`document.body.innerText.includes('矢狭間を配置しました')`, 3_000, 'Facility placement');

  const countBeforeBattle = await evaluate(`document.querySelector('.status-row > div:last-child strong')?.textContent.trim()`);
  if (countBeforeBattle !== '3') throw new Error(`Expected 3 placements before battle, got ${countBeforeBattle}`);

  await clickButtonContaining('この構成で防衛開始');
  await waitForExpression(`document.body.innerText.includes('防衛中')`, 5_000, 'Defense phase');
  await clickButtonContaining('3×');

  const outcome = await waitUntil(async () => {
    const text = await evaluate(`document.body.innerText`);
    if (text.includes('迎撃成功')) return '迎撃成功';
    if (text.includes('迎撃失敗')) return '迎撃失敗';
    return false;
  }, 20_000, 'Defense result');

  await clickButtonContaining('構築に戻る');
  await waitForExpression(`document.body.innerText.includes('ダンジョン構築')`, 5_000, 'Return to Build');

  const countAfterReturn = await evaluate(`document.querySelector('.status-row > div:last-child strong')?.textContent.trim()`);
  if (countAfterReturn !== '3') throw new Error(`Expected edited build to retain 3 placements, got ${countAfterReturn}`);

  await clickButtonContaining('配置を削除');
  await clickFirst('.cell.remove-target');
  const countAfterRemove = await waitUntil(async () => {
    const count = await evaluate(`document.querySelector('.status-row > div:last-child strong')?.textContent.trim()`);
    return count === '2' ? count : false;
  }, 3_000, 'Remove after return');

  await clickButtonContaining('EN');
  await waitForExpression(`document.body.innerText.includes('Dungeon Build') && document.body.innerText.includes('Spike Trap')`, 3_000, 'English locale');

  await cdp('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await new Promise(resolve => setTimeout(resolve, 250));
  const mobileMetrics = await evaluate(`(() => ({ viewport: window.innerWidth, page: document.documentElement.scrollWidth, boardClient: document.querySelector('.board-wrap')?.clientWidth ?? 0, boardScroll: document.querySelector('.board-wrap')?.scrollWidth ?? 0 }))()`);
  if (mobileMetrics.page > mobileMetrics.viewport + 1) throw new Error(`Unexpected document-level mobile overflow: ${JSON.stringify(mobileMetrics)}`);
  if (mobileMetrics.boardScroll <= mobileMetrics.boardClient) throw new Error(`Expected wide board overflow to remain contained inside board-wrap: ${JSON.stringify(mobileMetrics)}`);

  await clickButtonContaining('Invasion Demo');
  await waitForExpression(`document.body.innerText.includes('偵察 / 編成') && document.body.innerText.includes('黒鉄坑道')`, 8_000, 'Invasion briefing');
  const invasionCapacity = await evaluate(`document.querySelector('.invasion-formation .capacity-text')?.textContent.trim()`);
  if (invasionCapacity !== '12 / 12') throw new Error(`Expected canonical invasion formation capacity 12 / 12, got ${invasionCapacity}`);

  await clickButtonContaining('侵攻開始');
  await waitForExpression(`document.body.innerText.includes('侵攻戦')`, 5_000, 'Invasion battle');
  await clickButtonContaining('全軍投入');
  await clickButtonContaining('3×');

  const invasionOutcome = await waitUntil(async () => {
    const wardClicked = await evaluate(`(() => { const button = [...document.querySelectorAll('button')].find(x => x.textContent.includes('防護 35 MP') && !x.disabled); if (button) { button.click(); return true; } return false; })()`);
    if (wardClicked) await new Promise(resolve => setTimeout(resolve, 40));
    const text = await evaluate(`document.body.innerText`);
    if (text.includes('侵攻成功')) return '侵攻成功';
    if (text.includes('部隊壊滅')) return '部隊壊滅';
    if (text.includes('撤退完了')) return '撤退完了';
    return false;
  }, 25_000, 'Invasion result');
  if (invasionOutcome !== '侵攻成功') throw new Error(`Expected canonical invasion success, got ${invasionOutcome}`);

  const invasionMobileMetrics = await evaluate(`(() => ({ viewport: window.innerWidth, page: document.documentElement.scrollWidth, trackClient: document.querySelector('.section-track')?.clientWidth ?? 0, trackScroll: document.querySelector('.section-track')?.scrollWidth ?? 0 }))()`);
  if (invasionMobileMetrics.page > invasionMobileMetrics.viewport + 1) throw new Error(`Unexpected invasion document-level mobile overflow: ${JSON.stringify(invasionMobileMetrics)}`);
  if (invasionMobileMetrics.trackScroll <= invasionMobileMetrics.trackClient) throw new Error(`Expected invasion section track to contain its own mobile overflow: ${JSON.stringify(invasionMobileMetrics)}`);

  await clickButtonContaining('偵察へ戻る');
  await waitForExpression(`document.body.innerText.includes('偵察 / 編成')`, 3_000, 'Return to invasion briefing');
  await clickButtonContaining('EN');
  await waitForExpression(`document.body.innerText.includes('Briefing / Formation') && document.body.innerText.includes('Black Iron Mine')`, 3_000, 'Invasion English locale');
  await clickButtonContaining('Dungeon Defense');
  await waitForExpression(`document.body.innerText.includes('Dungeon Build')`, 3_000, 'Return to Defense mode');

  console.log(`browser-smoke=ok defense=${outcome} placements=3->${countAfterRemove} locale=en mobile=${mobileMetrics.viewport}px invasion=${invasionOutcome} invasionLocale=en`);
} finally {
  try { socket?.close(); } catch {}
  if (staticServer) await new Promise(resolve => staticServer.close(resolve));
  for (const child of children.reverse()) {
    try { child.kill('SIGTERM'); } catch {}
  }
  await new Promise(resolve => setTimeout(resolve, 250));
  try { await rm(profileDir, { recursive: true, force: true }); } catch {}
  if (publishDir) { try { await rm(publishDir, { recursive: true, force: true }); } catch {} }
}
