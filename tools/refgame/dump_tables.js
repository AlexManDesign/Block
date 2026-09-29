// Evaluated in the page: serialise every JSON-friendly top-level value of main.js.
(() => {
  const out = {}, skipped = [];
  const bc = window.__bc;
  for (const k of Object.keys(bc)) {
    let v; try { v = bc[k]; } catch (e) { continue; }
    if (typeof v === 'function' || v == null) continue;
    if (v instanceof Node || v instanceof Window || (typeof WebGL2RenderingContext !== 'undefined' && v instanceof WebGL2RenderingContext)) continue;
    if (ArrayBuffer.isView(v)) { if (v.length <= 70000) out[k] = { __typed: v.constructor.name, data: Array.from(v) }; else skipped.push(k); continue; }
    try {
      const stack = [];
      const s = JSON.stringify(v, function (key, val) {
        if (typeof val === 'function') return undefined;
        if (val && typeof val === 'object') {
          if (val instanceof Node || val instanceof Worker || val instanceof WebGLProgram || val instanceof WebGLBuffer || val instanceof WebGLTexture || val instanceof WebGLVertexArrayObject) return undefined;
          while (stack.length && stack[stack.length - 1] !== this) stack.pop();
          if (stack.includes(val)) return '[cycle]';
          stack.push(val);
          if (val instanceof Map) return { __map: [...val.entries()].slice(0, 5000) };
          if (val instanceof Set) return { __set: [...val].slice(0, 5000) };
          if (ArrayBuffer.isView(val)) return val.length <= 70000 ? { __typed: val.constructor.name, data: Array.from(val) } : '[big typed]';
        }
        return val;
      });
      if (s !== undefined && s.length < 4000000) out[k] = JSON.parse(s); else skipped.push(k);
    } catch (e) { skipped.push(k + ':' + e.message); }
  }
  out.__skipped = skipped;
  return JSON.stringify(out);
})()
