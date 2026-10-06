// Shared Playwright helpers: compile HTML with the converter and run pages.
import { chromium } from '/opt/node-tools/node_modules/playwright/index.mjs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const CONVERTER = process.env.CONVERTER || path.join(ROOT, 'js-wasm-converter.html');

// Host-side logger: lives outside the compiled HTML, so it is never compiled.
const LOGGER = `(() => {
  const seen = new WeakSet();
  const fmt = (v, depth = 0) => {
    if (v === null) return 'null';
    switch (typeof v) {
      case 'undefined': return 'undefined';
      case 'number': return Object.is(v, -0) ? '-0' : String(v);
      case 'bigint': return v + 'n';
      case 'string': return depth ? JSON.stringify(v) : v;
      case 'boolean': return String(v);
      case 'symbol': return v.toString();
      case 'function': return '[function ' + v.name + ']';
    }
    if (depth > 4 || seen.has(v)) return '[circular]';
    seen.add(v);
    try {
      if (Array.isArray(v)) { const a = []; for (let i = 0; i < v.length; i++) a.push(i in v ? fmt(v[i], depth + 1) : '<hole>'); return '[' + a.join(',') + ']'; }
      if (ArrayBuffer.isView(v)) return v.constructor.name + '[' + Array.from(v).join(',') + ']';
      if (v instanceof Map) return 'Map{' + [...v].map(([k, x]) => fmt(k, depth + 1) + '=>' + fmt(x, depth + 1)).join(',') + '}';
      if (v instanceof Set) return 'Set{' + [...v].map(x => fmt(x, depth + 1)).join(',') + '}';
      if (v instanceof Error) return v.name + ': ' + v.message;
      if (v instanceof Promise) return '[promise]';
      const proto = Object.getPrototypeOf(v), name = proto === null ? 'null' : proto === Object.prototype ? '' : (proto.constructor?.name || '?');
      return name + '{' + Object.keys(v).map(k => k + ':' + fmt(v[k], depth + 1)).join(',') + '}';
    } finally { seen.delete(v); }
  };
  window.__out = [];
  window.log = (...a) => { window.__out.push(a.map(x => fmt(x)).join(' ')); };
  window.logError = e => { window.__out.push('THROW ' + (e && e.name)); };
})();`;

export async function launch() {
  return chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
}

export async function compilerPage(browser) {
  const page = await browser.newPage();
  await page.goto('file://' + CONVERTER);
  return {
    page,
    compile: (html, files = [], entry = 'index.html') => page.evaluate(({ html, files, entry }) => {
      try {
        const r = createOwnCompiler()({ html, entry, files });
        return { ok: true, output: r.output, wasmBytes: r.wasmBytes, functions: r.functions, numeric: r.numericFunctions, typed: r.typedFunctions };
      } catch (e) { return { ok: false, error: String(e && e.message || e) }; }
    }, { html, files, entry }),
  };
}

export async function runPage(browser, html, { waitMs = 0, timeout = 20000 } = {}) {
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('UNCAUGHT ' + (e.name || 'Error')));
  await page.addInitScript(LOGGER);
  try {
    await page.setContent(html, { timeout });
    await page.evaluate(t => Promise.race([window.__done, new Promise((_, no) => setTimeout(() => no(new Error('timeout')), t))]), timeout).catch(e => errors.push('DONE-ERROR ' + e.message.split('\n')[0]));
    if (waitMs) await page.waitForTimeout(waitMs);
    const out = await page.evaluate(() => window.__out);
    return { out: [...out, ...errors], page };
  } catch (e) {
    return { out: [...errors, 'RUN-ERROR ' + e.message.split('\n')[0]], page };
  }
}

export const wrap = code => `<!doctype html><html><head><meta charset="utf-8"></head><body><div id="app"></div><button id="btn">b</button>\n<script>\n${code}\n</script></body></html>`;
