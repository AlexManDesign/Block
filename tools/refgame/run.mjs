// Loads the reference WebGL2 game (reference/source/main.js) in headless Chromium.
import { chromium } from 'playwright';
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
const root = path.resolve(import.meta.dirname, 'site');
const types = { '.html': 'text/html', '.js': 'text/javascript', '.png': 'image/png', '.json': 'application/json' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(root, p);
  fs.readFile(f, (err, data) => { if (err) { res.writeHead(404); res.end(); return; } res.writeHead(200, { 'Content-Type': types[path.extname(f)] || 'application/octet-stream' }); res.end(data); });
}).listen(0);
const port = server.address().port;
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium', args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
const errors = [];
page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(m.type() + ': ' + m.text()); });
page.on('pageerror', e => errors.push('pageerror: ' + e.message + '\n' + (e.stack || '').split('\n').slice(0, 4).join('\n')));
page.on('requestfailed', r => errors.push('reqfail: ' + r.url()));
await page.goto(`http://localhost:${port}/index.html`);
await page.waitForTimeout(+(process.argv[2] || 4000));
const script = process.argv[3];
if (script) { const out = await page.evaluate(fs.readFileSync(script, 'utf8')); if (out !== undefined) console.log(typeof out === 'string' ? out : JSON.stringify(out)); }
if (process.argv[4]) await page.screenshot({ path: process.argv[4] });
console.error(errors.slice(0, 30).join('\n'));
await browser.close(); server.close();
