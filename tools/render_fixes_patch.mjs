#!/usr/bin/env node
// Applies the render fixes to a Voxel Forge single-file build by exact text replacement.
// Every replacement must match the expected number of times, otherwise nothing is written,
// so it is safe to try on other builds: it either applies cleanly or tells you which anchor moved.
//
//   node tools/render_fixes_patch.mjs <in.html> <out.html> [--only=A,B,...]
//
// Groups (all by default):
//   A scheduler progress guarantee   B underwater tint only on faces touching water
//   C texel-edge UV inset            D lightmap FLIP_Y                E mesher lookup tables
//   F duplicate generation jobs      G glass/water pass order         H merged flat water tops
//   J X-Ray camera uniforms          K isFluidWater table + fast sky column
//   V build label
import fs from 'node:fs';
const [inPath, outPath, ...rest] = process.argv.slice(2);
if (!inPath || !outPath) { console.error('usage: node tools/render_fixes_patch.mjs <in.html> <out.html> [--only=A,B,...]'); process.exit(2); }
let src = fs.readFileSync(inPath, 'utf8');
const onlyArg = rest.find(a => a.startsWith('--only='));
const only = (onlyArg ? onlyArg.slice(7) : 'A,B,C,D,E,F,G,H,J,K,V').split(',');
function rep(group, from, to, n = 1) {
  if (!only.includes(group)) return;
  const cnt = src.split(from).length - 1;
  if (cnt !== n) throw new Error(`[${group}] expected ${n} match(es), got ${cnt}: ${from.slice(0, 90)}`);
  src = src.split(from).join(to);
}

// A. Background scheduler: guarantee forward progress (at least one unit of each kind per frame),
//    so an inflated cost estimate can never lock streaming/lighting forever.
rep('A', 'if(!startup&&performance.now()+est>=deadline)break;const done=takeGenDone()',
         'if(!startup&&integrated>0&&performance.now()+est>=deadline)break;const done=takeGenDone()');
rep('A', 'if(!startup&&performance.now()+est>=deadline)break;const done=takeMeshDone()',
         'if(!startup&&uploaded>0&&performance.now()+est>=deadline)break;const done=takeMeshDone()');
rep('A', 'if(!workerEngineFailed&&performance.now()<deadline-.35)fillGenJobsMain(pcx,pcz,deadline);',
         'if(!workerEngineFailed&&(performance.now()<deadline-.35||!genJobs.length))fillGenJobsMain(pcx,pcz,deadline);');
rep('A', 'for(let i=0;i<count&&genPending.size<GEN_QUEUE_LIMIT;i++){if(Number.isFinite(deadline)&&performance.now()>deadline-1)break;',
         'for(let i=0;i<count&&genPending.size<GEN_QUEUE_LIMIT;i++){if(i>0&&Number.isFinite(deadline)&&performance.now()>deadline-1)break;');
rep('A', ' if(performance.now()<deadline-.25){\n  prioritizeWorkerQueues(pcx,pcz);',
         ' if(performance.now()<deadline-.25||!meshPending.size){\n  prioritizeWorkerQueues(pcx,pcz);');
rep('A', 'if(!startup&&performance.now()+est>=deadline)break;const ts=performance.now();if(queueChunkMesh(c))sent++',
         'if(!startup&&sent>0&&performance.now()+est>=deadline)break;const ts=performance.now();if(queueChunkMesh(c))sent++');
rep('A', ' if(timed&&performance.now()>deadline-2)return 0;\n',
         ' if(timed&&performance.now()>deadline-2){if(lightDirtyCount()<256&&++lightStarveFrames<4)return 0;deadline=performance.now()+1.5}\n lightStarveFrames=0;\n');
rep('A', 'const lightDirtyQueue=[],lightDirtyKeys=new Set();let lightDirtyHead=0,',
         'const lightDirtyQueue=[],lightDirtyKeys=new Set();let lightStarveFrames=0,lightDirtyHead=0,');

// C. Per-tile texture-array layers + NEAREST + CLAMP: the 3.5% UV inset only squashes the edge texels.
rep('C', 'vec2 f=mix(vec2(0.035),vec2(0.965),fract(vUV));', 'vec2 f=fract(vUV);', 4);
rep('C', 'vec2 f=mix(vec2(0.035),vec2(0.965),fract(q));', 'vec2 f=fract(q);', 1);

// D. The lightmap is a raw Uint8Array upload; UNPACK_FLIP_Y_WEBGL is left =true by the atlas/entity/item
//    image uploads, which flips the sky axis on the GPU (open sky renders at minimum light, caves at daylight).
rep('D', 'gl.bindTexture(gl.TEXTURE_2D,mainLightmapTex);gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,16,16,gl.RGBA,gl.UNSIGNED_BYTE,mainLightmapPixels)',
         'gl.bindTexture(gl.TEXTURE_2D,mainLightmapTex);gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,16,16,gl.RGBA,gl.UNSIGNED_BYTE,mainLightmapPixels)');

// E. Mesher: the per-cell classifiers are long `id===B.X||...` chains evaluated ~6x per cell per axis.
//    Precompute 256-entry tables once per core, and skip the meta-map string lookup for air/solid cells.
rep('E', 'function xrayClass(id){', 'function xrayClassSlow(id){');
rep('E', 'function axisLogId(id){', 'function axisLogIdSlow(id){');
rep('E', 'function isGreedySolid(id){if(isVirtual(id))return false;const b=def(id);return !!(id!==B.AIR&&b&&b.solid&&!b.transparent&&!b.cutout&&!b.plant&&!b.waterPlant&&!b.special&&!axisLogId(id))}',
         'function isGreedySolidSlow(id){if(isVirtual(id))return false;const b=def(id);return !!(id!==B.AIR&&b&&b.solid&&!b.transparent&&!b.cutout&&!b.plant&&!b.waterPlant&&!b.special&&!axisLogIdSlow(id))}');
rep('E', 'const greedyMask=new Int32Array(C*C);',
         'const greedyMask=new Int32Array(C*C);\n  const XRAY_LUT=new Uint8Array(256),AXISLOG_LUT=new Uint8Array(256),GREEDY_LUT=new Uint8Array(256);for(let q=0;q<256;q++){XRAY_LUT[q]=xrayClassSlow(q);AXISLOG_LUT[q]=axisLogIdSlow(q)?1:0;GREEDY_LUT[q]=isGreedySolidSlow(q)?1:0}\n  function xrayClass(id){return XRAY_LUT[id]|0}\n  function axisLogId(id){return AXISLOG_LUT[id]===1}\n  function isGreedySolid(id){return GREEDY_LUT[id]===1}');
rep('E', "const id=at(x,y,z),m=metaMap.get(x+','+y+','+z)||{},vb=isVirtual(id)?vdef(metaMap,x,y,z):null,b=vb||def(id);if(!b||id===B.AIR||isGreedySolid(id))continue;",
         "const id=at(x,y,z);if(id===B.AIR||isGreedySolid(id))continue;const m=metaMap.get(x+','+y+','+z)||{},vb=isVirtual(id)?vdef(metaMap,x,y,z):null,b=vb||def(id);if(!b)continue;");

// F. genPending is cleared when the worker replies, but the result then waits in genDone for integration;
//    fillGenJobsMain re-queues those chunks, so >50% of generation jobs were duplicates.
rep('F', 'genDone=[],meshDone=[],genPending=new Set(),', 'genDone=[],meshDone=[],genPending=new Set(),genDoneKeys=new Set(),');
rep('F', 'avgGenWorkerMs=avgGenWorkerMs?avgGenWorkerMs*.9+(+m.ms||0)*.1:(+m.ms||0);genDone.push(m);',
         'avgGenWorkerMs=avgGenWorkerMs?avgGenWorkerMs*.9+(+m.ms||0)*.1:(+m.ms||0);genDoneKeys.add(ckey(m.cx,m.cz));genDone.push(m);');
rep('F', 'function takeGenDone(){return takeDone(genDone,0)}',
         'function takeGenDone(){const m=takeDone(genDone,0);if(m)genDoneKeys.delete(ckey(m.cx,m.cz));return m}');
rep('F', 'const k=ckey(cx,cz);if(chunks.has(k)||genPending.has(k))return false;const es=editsByChunk.get(k)',
         'const k=ckey(cx,cz);if(chunks.has(k)||genPending.has(k)||genDoneKeys.has(k))return false;const es=editsByChunk.get(k)');

// B. The "below sea level" blue tint was applied to every fragment under y=63.5, so all caves looked flooded.
//    The mesher now flags faces that touch water (+1024 in aShadeAlpha); the tint is kept only for those
//    (sea floor, submerged plants, the water itself), so the ocean keeps its look and caves do not turn blue.
rep('B', "out vec2 vUV;flat out vec3 vMat;out vec3 vLightDist;out vec3 vWorld;\nvoid main(){vec3 w=aPos;gl_Position=uVP*vec4(w,1.0);vUV=aUV;float cls=floor(aShadeAlpha/512.0);float q=aShadeAlpha-cls*512.0;",
         "out vec2 vUV;flat out vec3 vMat;out vec3 vLightDist;out vec3 vWorld;flat out float vSub;\nvoid main(){vec3 w=aPos;gl_Position=uVP*vec4(w,1.0);vUV=aUV;float sub=floor(aShadeAlpha/1024.0);vSub=sub;float r0=aShadeAlpha-sub*1024.0;float cls=floor(r0/512.0);float q=r0-cls*512.0;");
rep('B', "precision highp float;in vec2 vUV;flat in vec3 vMat;in vec3 vLightDist;in vec3 vWorld;",
         "precision highp float;in vec2 vUV;flat in vec3 vMat;in vec3 vLightDist;in vec3 vWorld;flat in float vSub;");
rep('B', "out vec2 vUV;flat out vec2 vMat;out vec3 vLightDist;out vec3 vWorld;\nvoid main(){vec3 w=aPos;gl_Position=uVP*vec4(w,1.0);vUV=aUV;float shade=mod(aShadeAlpha,2.0);",
         "out vec2 vUV;flat out vec2 vMat;out vec3 vLightDist;out vec3 vWorld;flat out float vSub;\nvoid main(){vec3 w=aPos;gl_Position=uVP*vec4(w,1.0);vUV=aUV;vSub=floor(aShadeAlpha/1024.0);float shade=mod(aShadeAlpha,2.0);");
rep('B', "precision highp float;in vec2 vUV;flat in vec2 vMat;in vec3 vLightDist;in vec3 vWorld;",
         "precision highp float;in vec2 vUV;flat in vec2 vMat;in vec3 vLightDist;in vec3 vWorld;flat in float vSub;");
rep('B', "if(uFogFar>30.0&&vWorld.y<63.5){", "if(uFogFar>30.0&&vSub>0.5&&vWorld.y<63.5){", 2);
rep('B', "vUV=aUV;vTile=aTile;float cls=floor(aShadeAlpha/512.0);float q=aShadeAlpha-cls*512.0;",
         "vUV=aUV;vTile=aTile;float sub=floor(aShadeAlpha/1024.0);float r0=aShadeAlpha-sub*1024.0;float cls=floor(r0/512.0);float q=r0-cls*512.0;");
rep('B', "packedSA=shade+a8*2+(cls?512:0)", "packedSA=shade+a8*2+(cls?512:0)+meshSubFlag", 2);
rep('B', " const WATER_VIS_H=[0,1/9,2/9,3/9,4/9,5/9,6/9,7/9,8/9],", " let meshSubFlag=0;\n const WATER_VIS_H=[0,1/9,2/9,3/9,4/9,5/9,6/9,7/9,8/9],");
rep('B', "packed=FLUID_FACE_SHADE[2]+a8*2,", "packed=FLUID_FACE_SHADE[2]+a8*2+1024,");
rep('B', "key=id|((((L>>4)&15)>>2)<<8)|(((L&15)>>2)<<10);mask[n++]=sign*key",
         "key=id|((((L>>4)&15)>>2)<<8)|(((L&15)>>2)<<10)|(connectedWaterAt(lp[0],lp[1]+y0,lp[2])?4096:0);mask[n++]=sign*key");
rep('B', "quad(verts,inds,p,du,dv,faceTile(id,axis,sign),sign>0?SHADE_POS[axis]:SHADE_NEG[axis],1,sign<0,faceUV(axis,sign,w,h),L[0],L[1],xrayClass(id));",
         "meshSubFlag=key&4096?1024:0;quad(verts,inds,p,du,dv,faceTile(id,axis,sign),sign>0?SHADE_POS[axis]:SHADE_NEG[axis],1,sign<0,faceUV(axis,sign,w,h),L[0],L[1],xrayClass(id));meshSubFlag=0;");
rep('B', "const id=at(x,y,z);if(id===B.AIR||isGreedySolid(id))continue;const m=metaMap.get(x+','+y+','+z)||{},vb=isVirtual(id)?vdef(metaMap,x,y,z):null,b=vb||def(id);if(!b)continue;",
         "const id=at(x,y,z);if(id===B.AIR||isGreedySolid(id))continue;const m=metaMap.get(x+','+y+','+z)||{},vb=isVirtual(id)?vdef(metaMap,x,y,z):null,b=vb||def(id);if(!b)continue;meshSubFlag=(isFluidWater(id)||b.waterPlant||m.waterlogged||connectedWaterAt(x+1,y,z)||connectedWaterAt(x-1,y,z)||connectedWaterAt(x,y+1,z)||connectedWaterAt(x,y-1,z)||connectedWaterAt(x,y,z+1)||connectedWaterAt(x,y,z-1))?1024:0;");
rep('B', "emitFace(v,i,id,x,y,z,f,b.alpha??1,tile)}}return out?[ov.length-s0[0]", "emitFace(v,i,id,x,y,z,f,b.alpha??1,tile)}}meshSubFlag=0;return out?[ov.length-s0[0]");

// G. Translucent pass order: glass used to write depth before the water pre-pass, so water behind (stained)
//    glass vanished. Now: glass colour without depth -> depth only for (nearly) opaque glass texels (frames)
//    -> water pre-pass + colour -> full glass depth for everything drawn afterwards (clouds, outline...).
//    Back faces are culled in these passes (glass cubes are closed), so the far side of a pane is not doubled.
rep('G', "flat in vec3 vMat;in vec3 vLightDist;in vec3 vWorld;flat in float vSub;", "flat in vec3 vMat;in vec3 vLightDist;in vec3 vWorld;flat in float vSub;uniform float uAlphaCut;");
rep('G', "float a=t.a*vMat.z;if(a<0.12)discard;", "float a=t.a*vMat.z;if(a<uAlphaCut)discard;");
rep('G', "hand:gl.getUniformLocation(terrainProg,'uHeldLight')};", "hand:gl.getUniformLocation(terrainProg,'uHeldLight'),alphaCut:gl.getUniformLocation(terrainProg,'uAlphaCut')};");
rep('G', "gl.useProgram(terrainProg);gl.uniform1i(U.atlas,0);", "gl.useProgram(terrainProg);gl.uniform1f(U.alphaCut,.12);gl.uniform1i(U.atlas,0);");
rep('G', "  restoreTerrainProgram();\n  gl.enable(gl.BLEND);gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);gl.depthMask(true);\n  transDraws=drawAlphaRenderList(transRenderList,transRenderCount,'trans');\n  gl.disable(gl.BLEND);\n",
         "  restoreTerrainProgram();\n  gl.enable(gl.BLEND);gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);gl.depthMask(false);gl.enable(gl.CULL_FACE);\n  transDraws=drawAlphaRenderList(transRenderList,transRenderCount,'trans');\n  gl.disable(gl.BLEND);gl.depthMask(true);\n  drawTransDepth(.9);gl.disable(gl.CULL_FACE);\n");
rep('G', "  drawMainClouds(eye,fog,underwater);", "  drawTransDepth(.12);\n  drawMainClouds(eye,fog,underwater);");
rep('G', "function bindWaterfallDepth(){",
         "function drawTransDepth(cut){if(!transRenderCount)return;restoreTerrainProgram();gl.uniform1f(U.alphaCut,cut);gl.colorMask(false,false,false,false);gl.depthMask(true);gl.depthFunc(gl.LESS);gl.disable(gl.BLEND);gl.enable(gl.CULL_FACE);let lastVAO=null;for(let i=0;i<transRenderCount;i++){const m=transRenderList[i]?.trans;if(!m)continue;if(lastVAO!==m.vao){gl.bindVertexArray(m.vao);lastVAO=m.vao}gl.drawElements(gl.TRIANGLES,m.count,m.indexType||gl.UNSIGNED_INT,m.indexOffset||0);drawCalls++}gl.colorMask(true,true,true,true);gl.disable(gl.CULL_FACE);gl.uniform1f(U.alphaCut,.12)}\nfunction bindWaterfallDepth(){");

// H. Flat water surfaces (source water, level 8/9 at all four corners, same light) are merged per section
//    layer into one quad instead of one quad per block: ocean chunks go from 256 top quads to a handful.
rep('H', "function waterSideQuad(v,i,x,y,z,f,h0,h1){",
         "const waterTopDone=new Uint8Array(4096),waterMergeMask=new Int32Array(256);\n" +
         "function waterTopQuadMerged(v,i,x,y,z,w,d,pl){const wb=def(B.WATER),alpha=wb?wb.alpha??.62:.62,yy=y+WATER_VIS_H[8]-.001,n=v.length/8,tile=faceTile(B.WATER,1,1),uv=faceUV(1,1,d,w),a8=Math.max(0,Math.min(255,Math.round(alpha*255))),packed=FLUID_FACE_SHADE[2]+a8*2+1024,pts=[[x,yy,z],[x,yy,z+d],[x+w,yy,z],[x+w,yy,z+d]];for(let k=0;k<4;k++)v.push(pts[k][0],pts[k][1],pts[k][2],uv[k][0],uv[k][1],tile,packed,pl);i.push(n,n+1,n+2,n+2,n+1,n+3)}\n" +
         "function waterTopFlat(x,y,z){if(minecraftWaterCellAt(x,y+1,z))return false;const H=WATER_VIS_H[8],hc=minecraftWaterHeightAt(x,y,z);if(hc!==H)return false;const hn=minecraftWaterHeightAt(x,y,z-1),hs=minecraftWaterHeightAt(x,y,z+1),hw=minecraftWaterHeightAt(x-1,y,z),he=minecraftWaterHeightAt(x+1,y,z),e=1e-9;return Math.abs(minecraftWaterCornerAverage(hc,hn,hw,x-1,y,z-1)-H)<e&&Math.abs(minecraftWaterCornerAverage(hc,hn,he,x+1,y,z-1)-H)<e&&Math.abs(minecraftWaterCornerAverage(hc,hs,hw,x-1,y,z+1)-H)<e&&Math.abs(minecraftWaterCornerAverage(hc,hs,he,x+1,y,z+1)-H)<e}\n" +
         "function mergeWaterTops(v,i,si){waterTopDone.fill(0);const y0=si*16,M=waterMergeMask;for(let ly=0;ly<16;ly++){const y=y0+ly;let any=0;for(let z=0;z<16;z++)for(let x=0;x<16;x++){const id=at(x,y,z);let k=0;if(isFluidWater(id)&&waterTopFlat(x,y,z)){k=1+(packedLight(x,y+1,z)&255);any=1}M[z*16+x]=k}if(!any)continue;for(let z=0;z<16;z++)for(let x=0;x<16;){const k=M[z*16+x];if(!k){x++;continue}let w=1;while(x+w<16&&M[z*16+x+w]===k)w++;let d=1;outer:for(;z+d<16;d++)for(let q=0;q<w;q++)if(M[(z+d)*16+x+q]!==k)break outer;waterTopQuadMerged(v,i,x,y,z,w,d,k-1);for(let dz=0;dz<d;dz++)for(let q=0;q<w;q++){M[(z+dz)*16+x+q]=0;waterTopDone[ly*256+(z+dz)*16+x+q]=1}x+=w}}}\n" +
         "function waterSideQuad(v,i,x,y,z,f,h0,h1){");
rep('H', "function emitMinecraftWater(v,i,x,y,z,id){", "function emitMinecraftWater(v,i,x,y,z,id,topDone=false){");
rep('H', "  waterSideQuad(v,i,x,y,z,5,1,1);\n  return\n }",
         "  waterSideQuad(v,i,x,y,z,5,1,1);\n  return\n }\n if(topDone){const H=WATER_VIS_H[8];waterSideQuad(v,i,x,y,z,0,H,H);waterSideQuad(v,i,x,y,z,1,H,H);waterSideQuad(v,i,x,y,z,4,H,H);waterSideQuad(v,i,x,y,z,5,H,H);return}");
rep('H', "if(isFluidWater(id)){emitFluidWater(wv,wi,id,x,y,z);continue}", "if(isFluidWater(id)){emitMinecraftWater(wv,wi,x,y,z,id,waterTopDone[(y-y0)*256+z*16+x]===1);continue}");
rep('H', "greedySection(ov,oi,si);", "greedySection(ov,oi,si);mergeWaterTops(wv,wi,si);");

// J. In X-Ray mode only the X-Ray program got this frame's camera uniforms; glass and water are drawn with
//    terrainProg afterwards and used the matrix from the last non-X-Ray frame.
rep('J', "  const transStart=performance.now();", "  if(xrayActive)bindTerrainCommon(eye,fog,fogNear,fogFar,sun);\n  const transStart=performance.now();");

// K. Hot helpers. Mesher: isFluidWater is a 9-way comparison chain called for every cell -> table.
//    Lighting: directSkyColumn called getBlock + getBlockMeta twice per cell for all 384 cells; read the
//    chunk's section arrays directly, skip air, and look up meta only where the chunk has meta maps.
rep('K', " function isFluidWater(id){return id===B.WATER||id===B.WATER_FALLING||id===B.FLOW7||id===B.FLOW6||id===B.FLOW5||id===B.FLOW4||id===B.FLOW3||id===B.FLOW2||id===B.FLOW1}",
         " const FLUIDW_LUT=new Uint8Array(256);for(let q=0;q<256;q++)FLUIDW_LUT[q]=(q===B.WATER||q===B.WATER_FALLING||q===B.FLOW7||q===B.FLOW6||q===B.FLOW5||q===B.FLOW4||q===B.FLOW3||q===B.FLOW2||q===B.FLOW1)?1:0;\n function isFluidWater(id){return FLUIDW_LUT[id]===1}");
rep('K', "function directSkyColumn(x,z,cache=null){const key=(x|0)+','+(z|0);if(cache&&cache.has(key))return cache.get(key);const a=new Uint8Array(WORLD_H);let sv=15;for(let iy=WORLD_H-1;iy>=0;iy--){const id=getBlock(x,WORLD_MIN_Y+iy,z);if(lightStopsState(id,getBlockMeta(x,WORLD_MIN_Y+iy,z)))sv=0;else if(sv>0&&lightAttenuationState(id,getBlockMeta(x,WORLD_MIN_Y+iy,z)))sv=Math.max(0,sv-2);a[iy]=sv}if(cache)cache.set(key,a);return a}",
         "function directSkyColumn(x,z,cache=null){const key=(x|0)+','+(z|0);if(cache&&cache.has(key))return cache.get(key);const a=new Uint8Array(WORLD_H);x=Math.floor(x);z=Math.floor(z);const cx=Math.floor(x/CHUNK),cz=Math.floor(z/CHUNK),c=chunkFastGet(cx,cz);let sv=15;if(!c){for(let iy=WORLD_H-1;iy>=0;iy--){const id=getBlock(x,WORLD_MIN_Y+iy,z);if(lightStopsState(id,getBlockMeta(x,WORLD_MIN_Y+iy,z)))sv=0;else if(sv>0&&lightAttenuationState(id,getBlockMeta(x,WORLD_MIN_Y+iy,z)))sv=Math.max(0,sv-2);a[iy]=sv}}else{const ck=c.key||ckey(cx,cz),mm=metaByChunk.get(ck),em=editsByChunk.get(ck),gm=generatedMetaByChunk.get(ck),lx=x-cx*CHUNK,lz=z-cz*CHUNK,secs=c.sections;for(let si=SECTION_COUNT-1;si>=0;si--){const sec=secs[si],base=si*16;if(!sec||!sec.blocks){for(let k=15;k>=0;k--)a[base+k]=sv;continue}const blk=sec.blocks;for(let k=15;k>=0;k--){const iy=base+k,id=blk[(k*CHUNK+lz)*CHUNK+lx];if(id!==B.AIR){let meta=null;if(mm||gm){const lk=metaLocalKey(x,WORLD_MIN_Y+iy,z);meta=em&&em.has(lk)?(mm?mm.get(lk)||null:null):((mm&&mm.get(lk))||(gm&&gm.get(lk))||null)}if(lightStopsState(id,meta))sv=0;else if(sv>0&&lightAttenuationState(id,meta))sv=Math.max(0,sv-2)}a[iy]=sv}}}if(cache)cache.set(key,a);return a}");

// L. Early-Z: chunk opaque geometry is emitted bottom section first, so hidden caves/aquifers under the
//    surface were shaded before the surface that covers them. Store opaque quads top-first.
rep('L', "function setChunkOpaqueCPU(c,verts,inds){const v=verts instanceof Float32Array?verts:new Float32Array(verts||[]),i=inds instanceof Uint32Array?inds:new Uint32Array(inds||[]);",
         "function setChunkOpaqueCPU(c,verts,inds){const v=verts instanceof Float32Array?verts:new Float32Array(verts||[]),i0=inds instanceof Uint32Array?inds:new Uint32Array(inds||[]),i=reverseQuadOrder(i0);");
rep('L', "function clearChunkOpaqueCPU(c){", "function reverseQuadOrder(src){const n=src.length/6|0;if(n*6!==src.length||n<2)return src;const r=new Uint32Array(src.length);for(let q=0;q<n;q++){const s=(n-1-q)*6,d=q*6;r[d]=src[s];r[d+1]=src[s+1];r[d+2]=src[s+2];r[d+3]=src[s+3];r[d+4]=src[s+4];r[d+5]=src[s+5]}return r}\nfunction clearChunkOpaqueCPU(c){");

// V. Build label shown in the HUD.
rep('V', "ENGINE_BUILD='0.93.59-water67-complete-mesh-guards'", "ENGINE_BUILD='0.93.60-render-fixes'");

fs.writeFileSync(outPath, src);
console.log('wrote', outPath, 'groups', only.join(','));
