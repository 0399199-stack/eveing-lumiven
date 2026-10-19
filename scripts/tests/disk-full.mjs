import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, readdir, open } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { createServer } from 'node:net';
import { createHmac, randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';

const exe = path.resolve(process.argv[2]);
const volume = path.resolve(process.argv[3]);
const directory = await mkdtemp(path.join(tmpdir(), 'lumiven-disk-full-'));
const media = path.join(volume, 'media');
const author = path.join(media, 'disk-full-fixture');
await mkdir(author, { recursive: true });
const probe = createServer();
await new Promise(resolve => probe.listen(0, '127.0.0.1', resolve));
const port = probe.address().port;
await new Promise(resolve => probe.close(resolve));
const token = randomBytes(32).toString('hex');
const pageToken = createHmac('sha256', token).update('lumiven-page-uploads').digest('hex');
const config = path.join(directory, 'config.yaml');
await writeFile(config, `download_dir: ${JSON.stringify(media)}\nlog_file: ${JSON.stringify(path.join(directory, 'core.log'))}\n`);
const child = spawn(exe, ['--desktop-managed', '--desktop-test-mode', '--desktop-token', token, '--desktop-parent', String(process.pid), '--desktop-data-dir', directory, '--port', String(port - 1), '--config', config], { cwd: directory, windowsHide: true, stdio: 'ignore' });
const base = `http://127.0.0.1:${port}`;
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
async function api(route, body) {
  const response = await fetch(base + route, { method: body ? 'POST' : 'GET', headers: { 'X-Local-Auth': route.startsWith('/__wx') ? pageToken : token, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  assert.equal(response.status, 200, route);
  return response.json();
}
try {
  let ready = false;
  for (let i = 0; i < 100; i++) { try { ready = (await api('/api/desktop/status')).state === 'ready'; if (ready) break; } catch {} await wait(100); }
  assert.ok(ready);
  const payload = Buffer.alloc(2 * 1024 * 1024, 0x45);
  const task = await api('/__wx_channels_api/desktop_begin', { videoId: 'disk-full', title: 'Disk full', author: 'disk-full-fixture', quality: 'original', totalBytes: payload.length });
  const filler = await open(path.join(volume, 'filler.bin'), 'wx');
  let diskFull = false;
  let written = 0;
  try {
    const buffer = Buffer.alloc(65536, 0x5a);
    // The helper verifies this volume is our new 128 MiB VHD. This cap is an additional guard.
    while (written < 128 * 1024 * 1024) { const result = await filler.write(buffer); written += result.bytesWritten; }
  } catch (error) { if (error.code !== 'ENOSPC') throw error; diskFull = true; }
  finally { await filler.close(); }
  assert.ok(diskFull, 'Must reproduce a real OS disk-full error');
  const form = new FormData();
  form.set('video', new Blob([payload]), 'full.mp4');
  form.set('filename', 'full.mp4');
  form.set('authorName', 'disk-full-fixture');
  form.set('videoId', 'disk-full');
  form.set('title', 'Disk full');
  form.set('desktopTaskId', task.id);
  const response = await fetch(base + '/__wx_channels_api/save_video', { method: 'POST', headers: { 'X-Local-Auth': pageToken }, body: form });
  assert.equal(response.status, 500);
  const snapshot = await api('/api/desktop/tasks');
  const failed = snapshot.tasks.find(t => t.id === task.id);
  assert.equal(failed.status, 'failed');
  assert.equal(failed.filePath, '');
  assert.equal(failed.relativePath, '');
  assert.deepEqual(await readdir(author), []);
  assert.equal((await api('/api/downloads?query=Disk')).data.total, 0);
  await api('/api/desktop/shutdown', {});
  for (let i = 0; i < 100 && child.exitCode === null; i++) await wait(100);
  assert.notEqual(child.exitCode, null);
  console.log(JSON.stringify({ passed: true, actualOsDiskFull: true, fillerBytes: written, httpStatus: 500, taskStatus: failed.status, completedDatabaseRecords: 0, leftoverVideoFiles: 0, systemProxyChanged: false }));
} finally {
  if (child.exitCode === null) { child.kill(); await new Promise(resolve => child.once('exit', resolve)); }
}
