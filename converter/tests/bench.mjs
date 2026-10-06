// Performance: the same functions as plain JS and as compiled WASM.
// Usage: node bench.mjs [filter]
import { launch, compilerPage, runPage } from './harness.mjs';

const cases = {
  numeric: `function sumSquares(n){const s={total:0};for(let i=0;i<n;i++)s.total+=i*i;return s.total;}
function hashLoop(n){let x=123456789;for(let i=0;i<n;i++){x^=x<<13;x^=x>>>17;x^=x<<5;}return x>>>0;}
function run(){let a=0;for(let k=0;k<5;k++)a+=sumSquares(2000000)+hashLoop(2000000);return a;}`,
  locals: `function work(n){let a=0,b=1;for(let i=0;i<n;i++){const t=(a+b)%1000003;a=b;b=t;if((i&1023)===0)b+=i%7;}return a+b;}
function run(){return work(3000000);}`,
  particles: `function makeParticles(n){const ps=[];for(let i=0;i<n;i++)ps.push({x:i%100,y:(i*7)%100,vx:1,vy:-1});return ps;}
function step(ps){for(let i=0;i<ps.length;i++){const p=ps[i];p.x+=p.vx*0.016;p.y+=p.vy*0.016;if(p.x<0||p.x>100)p.vx=-p.vx;if(p.y<0||p.y>100)p.vy=-p.vy;}}
function run(){const ps=makeParticles(10000);for(let f=0;f<60;f++)step(ps);let s=0;for(const p of ps)s+=p.x+p.y;return s;}`,
  mesher: `function mesh(size){const vox=new Uint8Array(size*size*size);for(let i=0;i<vox.length;i++)vox[i]=(i*2654435761>>>28)&1;
const out=[];for(let z=1;z<size-1;z++)for(let y=1;y<size-1;y++)for(let x=1;x<size-1;x++){const i=x+size*(y+size*z);if(!vox[i])continue;if(!vox[i+1])out.push(x,y,z,0);if(!vox[i-1])out.push(x,y,z,1);if(!vox[i+size])out.push(x,y,z,2);}return out.length;}
function run(){let n=0;for(let k=0;k<3;k++)n+=mesh(32);return n;}`,
  noise: `function hash(x,y){let h=Math.imul(x,374761393)+Math.imul(y,668265263)|0;h=Math.imul(h^(h>>>13),1274126177);return((h^(h>>>16))>>>0)/4294967296;}
function smooth(t){return t*t*(3-2*t);}
function noise(x,y){const xi=Math.floor(x),yi=Math.floor(y),xf=x-xi,yf=y-yi;const a=hash(xi,yi),b=hash(xi+1,yi),c=hash(xi,yi+1),d=hash(xi+1,yi+1);const u=smooth(xf),v=smooth(yf);return a+(b-a)*u+(c-a)*v+(a-b-c+d)*u*v;}
function run(){let s=0;for(let y=0;y<300;y++)for(let x=0;x<300;x++)s+=noise(x*0.37,y*0.21);return s;}`,
  classes: `class Vec{constructor(x,y,z){this.x=x;this.y=y;this.z=z;}add(o){return new Vec(this.x+o.x,this.y+o.y,this.z+o.z);}len(){return Math.sqrt(this.x*this.x+this.y*this.y+this.z*this.z);}}
function run(){let v=new Vec(0,0,0);const d=new Vec(0.1,0.2,0.3);let s=0;for(let i=0;i<200000;i++){v=v.add(d);s+=v.len();}return s;}`,
  closures: `function makeCounter(){let n=0;return{inc(){n++;},get(){return n;}};}
function run(){const c=makeCounter();const fs=[];for(let i=0;i<100;i++)fs.push(x=>x+i);let s=0;for(let k=0;k<20000;k++){c.inc();s+=fs[k%100](k);}return s+c.get();}`,
  strings: `function build(n){let s='';for(let i=0;i<n;i++)s+=String.fromCharCode(97+i%26);return s;}
function count(s){let c=0;for(let i=0;i<s.length;i++)if(s.charCodeAt(i)===97)c++;return c;}
function run(){let t=0;for(let k=0;k<20;k++)t+=count(build(20000));return t;}`,
};

const filter = process.argv[2] ? new RegExp(process.argv[2], 'i') : null;
const browser = await launch();
const { compile } = await compilerPage(browser);
const rows = [];
for (const [name, code] of Object.entries(cases)) {
  if (filter && !filter.test(name)) continue;
  const html = `<!doctype html><html><head><meta charset="utf-8"></head><body><script>${code}\nwindow.bench=function(){var t=performance.now();var r=run();return [r,performance.now()-t];};<\/script></body></html>`;
  const time = async h => { const r = await runPage(browser, h); try { r.page.setDefaultTimeout(120000); await r.page.evaluate(() => window.bench()); const runs = []; for (let i = 0; i < 3; i++) runs.push(await r.page.evaluate(() => window.bench())); runs.sort((a, b) => a[1] - b[1]); return runs[1]; } catch (e) { return [null, NaN, e.message.split('\n')[0]]; } finally { await r.page.close(); } };
  const js = await time(html);
  const c = await compile(html);
  if (!c.ok) { rows.push([name, js[1].toFixed(1), 'COMPILE ERROR', c.error]); continue; }
  const wasm = await time(c.output);
  const same = JSON.stringify(js[0]) === JSON.stringify(wasm[0]);
  rows.push([name, js[1].toFixed(1), wasm[1].toFixed(1), (wasm[1] / js[1]).toFixed(1) + 'x', same ? 'ok' : `MISMATCH ${js[0]} vs ${wasm[0]} ${wasm[2] || ''}`]);
}
console.log('case       js ms    wasm ms   wasm/js  result');
for (const r of rows) console.log(r[0].padEnd(10), String(r[1]).padStart(7), String(r[2]).padStart(10), String(r[3]).padStart(9), ' ', r[4] || '');
await browser.close();
