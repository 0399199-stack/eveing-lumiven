import { spawn, execFileSync } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, readdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { createServer } from 'node:net';
import { createHmac, randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';

const exe = path.resolve(process.argv[2]);
const directory = await mkdtemp(path.join(tmpdir(), 'lumiven-permission-'));
const media = path.join(directory, 'media');
const blocked = path.join(media, 'acl-fixture');
await mkdir(blocked, { recursive: true });
const sid = execFileSync('whoami.exe', ['/user', '/fo', 'csv', '/nh'], { encoding: 'utf8', windowsHide: true }).match(/S-1-5-[\d-]+/)[0];
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
let denied = false;
try {
  let ready = false;
  for (let i = 0; i < 100; i++) { try { ready = (await api('/api/desktop/status')).state === 'ready'; if (ready) break; } catch {} await wait(100); }
  assert.ok(ready);
  const task = await api('/__wx_channels_api/desktop_begin', { videoId: 'acl-denied', title: 'ACL denied', author: 'acl-fixture', quality: 'original', totalBytes: 4096 });
  execFileSync('icacls.exe', [blocked, '/deny', `*${sid}:(OI)(CI)(W)`], { windowsHide: true, stdio: 'pipe' });
  denied = true;
  await assert.rejects(writeFile(path.join(blocked, 'probe'), 'blocked'), error => error.code === 'EACCES' || error.code === 'EPERM');
  const form = new FormData();
  form.set('video', new Blob([Buffer.alloc(4096)]), 'acl.mp4');
  form.set('filename', 'acl.mp4');
  form.set('authorName', 'acl-fixture');
  form.set('videoId', 'acl-denied');
  form.set('title', 'ACL denied');
  form.set('desktopTaskId', task.id);
  const response = await fetch(base + '/__wx_channels_api/save_video', { method: 'POST', headers: { 'X-Local-Auth': pageToken }, body: form });
  assert.equal(response.status, 500);
  const snapshot = await api('/api/desktop/tasks');
  const failed = snapshot.tasks.find(t => t.id === task.id);
  assert.equal(failed.status, 'failed');
  assert.equal(failed.filePath, '');
  assert.equal(failed.relativePath, '');
  execFileSync('icacls.exe', [blocked, '/remove:d', `*${sid}`], { windowsHide: true, stdio: 'pipe' });
  denied = false;
  assert.deepEqual(await readdir(blocked), []);
  assert.equal((await api('/api/downloads?query=ACL')).data.total, 0);
  await api('/api/desktop/shutdown', {});
  for (let i = 0; i < 100 && child.exitCode === null; i++) await wait(100);
  assert.notEqual(child.exitCode, null);
  console.log(JSON.stringify({ passed: true, actualWindowsAclDenied: true, httpStatus: 500, taskStatus: failed.status, completedDatabaseRecords: 0, leftoverFiles: 0, systemProxyChanged: false, directory }));
} finally {
  if (denied) execFileSync('icacls.exe', [blocked, '/remove:d', `*${sid}`], { windowsHide: true, stdio: 'pipe' });
  if (child.exitCode === null) child.kill();
}
