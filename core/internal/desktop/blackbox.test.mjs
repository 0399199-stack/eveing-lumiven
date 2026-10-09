// Runs real handlers/Gopeed/SQLite in an isolated directory without system changes.
// Usage: node internal/desktop/blackbox.test.mjs <absolute core executable>
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtemp, writeFile, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash, createHmac, randomBytes } from 'node:crypto';
import { request as httpRequest } from 'node:http';
import vm from 'node:vm';

if (!process.argv[2]) throw new Error('Provide the built core executable');
const exe = path.resolve(process.argv[2]);
const concurrency = Number(process.argv[3] || 5);
const directory = await mkdtemp(path.join(tmpdir(), 'lumiven-core-test-'));
const payload = Buffer.alloc(2 * 1024 * 1024, 0x45);
payload.write('ftypisom', 4);
const activeVideos = new Map();
let maximumConcurrentVideos = 0;
const source = createServer((request, response) => {
  if (request.url === '/failure') { response.writeHead(503).end('fixture failure'); return; }
  if (request.url === '/interrupted') { response.writeHead(200, { 'Content-Length': payload.length, 'Content-Type': 'video/mp4' }); response.write(payload.subarray(0, 4096)); setTimeout(() => response.destroy(), 50); return; }
  const range = /bytes=(\d+)-(\d*)/.exec(request.headers.range || '');
  const begin = range ? Number(range[1]) : 0;
  const end = range && range[2] ? Number(range[2]) : payload.length - 1;
  const slice = payload.subarray(begin, end + 1);
  response.writeHead(range ? 206 : 200, {
    'Content-Type': 'video/mp4', 'Accept-Ranges': 'bytes', 'Content-Length': slice.length,
    ...(range ? { 'Content-Range': `bytes ${begin}-${end}/${payload.length}` } : {})
  });
  if (request.method === 'HEAD') { response.end(); return; }
  const identity = new URL(request.url, 'http://localhost').searchParams.get('task');
  if (identity !== null) {
    activeVideos.set(identity, (activeVideos.get(identity) || 0) + 1);
    maximumConcurrentVideos = Math.max(maximumConcurrentVideos, activeVideos.size);
    response.once('close', () => { const count = activeVideos.get(identity) - 1; if (count) activeVideos.set(identity, count); else activeVideos.delete(identity); });
  }
  let offset = 0;
  const timer = setInterval(() => {
    response.write(slice.subarray(offset, offset + 65536)); offset += 65536;
    if (offset >= slice.length) { clearInterval(timer); response.end(); }
  }, request.url.startsWith('/stall') ? 250 : 15);
  response.once('close', () => clearInterval(timer));
});
await new Promise(resolve => source.listen(0, '127.0.0.1', resolve));
const serverPort = source.address().port;
const portProbe = createServer();
await new Promise(resolve => portProbe.listen(0, '127.0.0.1', resolve));
const managementPort = portProbe.address().port;
await new Promise(resolve => portProbe.close(resolve));
const token = 'lumiven-blackbox-isolated-token-1234567890';
const pageToken = createHmac('sha256', token).update('lumiven-page-uploads').digest('hex');
const base = `http://127.0.0.1:${managementPort}`;
const config = path.join(directory, 'config.yaml');
await writeFile(config, `download_dir: ${JSON.stringify(path.join(directory, '媒体'))}\nlog_file: ${JSON.stringify(path.join(directory, 'core.log'))}\ndownload_concurrency: ${concurrency}\ndownload_retry_count: 1\n`);
const launch = () => spawn(exe, ['--desktop-managed', '--desktop-test-mode', '--desktop-token', token, '--desktop-parent', String(process.pid), '--desktop-data-dir', directory, '--port', String(managementPort - 1), '--config', config], { windowsHide: true, cwd: directory, stdio: 'ignore' });
let child = launch();
const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const exit = () => child.exitCode !== null ? Promise.resolve() : new Promise(resolve => child.once('exit', resolve));
async function request(route, body) {
  const response = await fetch(base + route, { method: body ? 'POST' : 'GET', headers: { 'X-Local-Auth': route.startsWith('/__wx_channels_api/') ? pageToken : token, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  assert.equal(response.ok, true, `${route}: ${response.status} ${await response.clone().text()}`);
  return response.json();
}
async function tasksUntil(predicate) {
  for (let i = 0; i < 150; i++) { const { tasks } = await request('/api/desktop/tasks'); if (predicate(tasks)) return tasks; await wait(100); }
  throw new Error('Task completion timeout');
}
const events = [];
let webSocket;
async function connectEvents() {
  const socket = await new Promise((resolve, reject) => {
    const request = httpRequest(base + '/ws/desktop', { headers: { Upgrade: 'websocket', Connection: 'Upgrade', 'Sec-WebSocket-Version': '13', 'Sec-WebSocket-Key': randomBytes(16).toString('base64'), 'X-Local-Auth': token } });
    request.on('upgrade', (_, socket) => resolve(socket)); request.on('response', response => reject(new Error('WebSocket upgrade failed: ' + response.statusCode))); request.on('error', reject); request.end();
  });
  let buffer = Buffer.alloc(0);
  socket.on('data', bytes => {
    buffer = Buffer.concat([buffer, bytes]);
    while (buffer.length >= 2) {
      const opcode = buffer[0] & 15; let length = buffer[1] & 127; let offset = 2;
      if (length === 126) { if (buffer.length < 4) return; length = buffer.readUInt16BE(2); offset = 4; }
      else if (length === 127) { if (buffer.length < 10) return; length = Number(buffer.readBigUInt64BE(2)); offset = 10; }
      if (buffer.length < offset + length) return;
      if (opcode === 1) events.push(JSON.parse(buffer.subarray(offset, offset + length).toString()));
      buffer = buffer.subarray(offset + length);
    }
  });
  return socket;
}
try {
  for (let i = 0; i < 100; i++) { try { const status = await request('/api/desktop/status'); if (status.state === 'ready') break; } catch {} await wait(100); }
  assert.equal((await fetch(base + '/api/desktop/tasks')).status, 401);
  assert.equal((await fetch(base + '/ws/desktop')).status, 401);
  assert.equal((await fetch(base + '/api/desktop/tasks', { headers: { 'X-Local-Auth': pageToken } })).status, 401);
  assert.equal((await fetch(base + '/ws/desktop', { headers: { 'X-Local-Auth': pageToken } })).status, 401);
  assert.equal((await fetch(base + '/api/downloads', { headers: { 'X-Local-Auth': pageToken } })).status, 401);
  assert.equal((await fetch(base + '/api/downloads', { headers: { 'X-Local-Auth': token } })).status, 200);
  assert.equal((await fetch(base + '/__wx_channels_api/desktop_begin', { method: 'POST', headers: { 'X-Local-Auth': token, 'Content-Type': 'application/json' }, body: '{}' })).status, 401);
  assert.equal((await fetch(base + '/api/v1/certificate/install', { method: 'POST', headers: { 'X-Local-Auth': token } })).status, 404);
  assert.equal((await fetch(base + '/api/v1/proxy/restart', { method: 'POST', headers: { 'X-Local-Auth': token } })).status, 404);
  assert.equal((await fetch(base + '/api/queue', { method: 'POST', headers: { 'X-Local-Auth': token } })).status, 404);
  assert.equal((await fetch(base + '/api/downloads', { method: 'DELETE', headers: { 'X-Local-Auth': token } })).status, 405);
  webSocket = await connectEvents();
  assert.equal((await request('/api/desktop/status')).mode, 'test_fixture');
  const conflict = launch();
  await Promise.race([new Promise(resolve => conflict.once('exit', resolve)), wait(2000).then(() => { conflict.kill(); throw new Error('conflicting listener must exit'); })]);
  assert.equal((await request('/api/desktop/status')).state, 'ready');
  await Promise.all(Array.from({ length: 5 }, (_, i) => request('/__wx_channels_api/download_video', { videoId: `video-${i}`, title: '同名中文视频', author: 'EVEING', videoUrl: `http://127.0.0.1:${serverPort}/video?task=${i}`, resolution: '1080p', forceSave: true })));
  const first = await tasksUntil(tasks => tasks.filter(t => t.status === 'completed').length === 5);
  assert.equal(new Set(first.map(t => t.filePath)).size, 5, 'same-name downloads must not overwrite');
  assert.equal(maximumConcurrentVideos, concurrency, 'configured independent download concurrency must be respected');
  const hash = bytes => createHash('sha256').update(bytes).digest('hex');
  for (const task of first) assert.equal(hash(await readFile(task.filePath)), hash(payload));
  const begun = await request('/__wx_channels_api/desktop_begin', { videoId: 'page-original', title: '页面原始视频', author: 'EVEING', quality: '原始视频', totalBytes: payload.length });
  // Run the actual injected page uploader against the real handlers so response
  // format mismatches cannot be hidden by a separately implemented test client.
  const page = vm.createContext({ window: { __WX_LOCAL_TOKEN__: pageToken, __LUMIVEN_MANAGED__: true }, Blob, FormData,
    console: { log() {}, warn() {} }, fetch: (route, options) => fetch(base + route, options) });
  vm.runInContext(await readFile(new URL('../assets/inject/download.js', import.meta.url), 'utf8'), page);
  await page.__lumiven_save_blob__({ id: 'page-original', title: '页面原始视频', nickname: 'EVEING' }, '原始视频', new Blob([payload]), begun.id);
  const second = await tasksUntil(tasks => tasks.filter(t => t.status === 'completed').length === 6);
  assert.equal(hash(await readFile(second.find(t => t.id === begun.id).filePath)), hash(payload));
  await request('/__wx_channels_api/batch_start', { videos: [{ id: 'batch-1', title: '批量视频', author: 'EVEING', url: `http://127.0.0.1:${serverPort}/video`, resolution: '1080p' }], forceRedownload: true });
  await tasksUntil(tasks => tasks.filter(t => t.status === 'completed').length === 7);
  await request('/__wx_channels_api/download_video', { videoId: 'failed', title: '失败测试', author: 'EVEING', videoUrl: `http://127.0.0.1:${serverPort}/failure`, forceSave: true });
  const final = await tasksUntil(tasks => tasks.some(t => t.status === 'failed'));
  assert.equal(final.filter(t => t.status === 'completed').length, 7);
  await request('/__wx_channels_api/download_video', { videoId: 'interrupted', title: '断网测试', author: 'EVEING', videoUrl: `http://127.0.0.1:${serverPort}/interrupted`, forceSave: true });
  await tasksUntil(tasks => tasks.filter(t => t.status === 'failed').length === 2);
  for (let i = 0; i < 30 && events.filter(event => event.type === 'task_failed').length < 2; i++) await wait(50);
  assert.equal(events.filter(event => event.type === 'task_completed').length, 7);
  assert.equal(events.filter(event => event.type === 'task_failed').length, 2);
  assert.equal(events.some(event => event.type === 'task_updated' && event.task.status === 'downloading'), true);
  webSocket.destroy();
  await request('/__wx_channels_api/download_video', { videoId: 'crashed', title: '异常退出测试', author: 'EVEING', videoUrl: `http://127.0.0.1:${serverPort}/stall`, forceSave: true });
  await tasksUntil(tasks => tasks.some(task => task.videoId === 'crashed'));
  child.kill();
  await exit();
  child = launch();
  for (let i = 0; i < 100; i++) { try { if ((await request('/api/desktop/status')).state === 'ready') break; } catch {} await wait(100); }
  const restored = await request('/api/desktop/tasks');
  assert.equal(restored.tasks.filter(t => t.status === 'completed').length, 7);
  assert.equal(restored.tasks.filter(t => t.status === 'failed').length, 3);
  assert.match(restored.tasks.find(t => t.videoId === 'crashed').error, /中断/);
  await request('/__wx_channels_api/batch_start', { videos: [1, 2].map(i => ({ id: `batch-stop-${i}`, title: `停止批量${i}`, author: 'EVEING', url: `http://127.0.0.1:${serverPort}/stall?batch=${i}`, resolution: '1080p' })), forceRedownload: true });
  await tasksUntil(tasks => tasks.filter(task => task.videoId.startsWith('batch-stop-') && task.status === 'downloading').length === 2);
  const stoppingAt = Date.now();
  await request('/api/desktop/shutdown', {});
  await exit();
  assert.ok(Date.now() - stoppingAt < 12000, 'batch shutdown must finish within shell timeout');
  child = launch();
  for (let i = 0; i < 100; i++) { try { if ((await request('/api/desktop/status')).state === 'ready') break; } catch {} await wait(100); }
  const stopped = await request('/api/desktop/tasks');
  assert.equal(stopped.tasks.filter(t => t.status === 'completed').length, 7);
  assert.equal(stopped.tasks.filter(t => t.status === 'failed').length, 5);
  assert.equal(stopped.tasks.filter(task => task.videoId.startsWith('batch-stop-') && task.status === 'failed').length, 2);
  await request('/api/desktop/shutdown', {});
  await exit();
  console.log(JSON.stringify({ passed: true, directory, completed: 7, failed: 5, independentPaths: 5, maximumConcurrentVideos, bytesVerified: true, pageUploaderVerified: true, restartVerified: true, abnormalExitVerified: true, networkInterruptionVerified: true, portConflictVerified: true, webSocketEventsVerified: true, batchShutdownVerified: true, authenticated: true, systemChanges: false }));
} finally {
  webSocket?.destroy();
  child.kill();
  source.close();
}
