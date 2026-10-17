#!/usr/bin/env python3
# A page to compare detailed models with their baked low-poly copies, side by side, turned and zoomed together (docs/model-baking.md).
#   python3 tools/bake/viewer.py <out.html> <view dir or <name>_view.json>...
# Each model is the <name>_high.glb / <name>_low.glb / <name>_view.json that tools/bake/bake.py writes into its conf's WORK/view.
# The models are embedded, so the page opens from disk (three.js comes from unpkg, so it needs the internet).
import base64, glob, json, os, sys
out, srcs = sys.argv[1], sys.argv[2:]
views = []
for s in srcs: views += sorted(glob.glob(os.path.join(s, '*_view.json'))) if os.path.isdir(s) else [s]
data, meta = {}, {}
for v in views:
    m = json.load(open(v)); n = m['name']; d = os.path.dirname(v)
    for h in ('high', 'low'): data[n + '_' + h] = base64.b64encode(open(f'{d}/{n}_{h}.glb', 'rb').read()).decode()
    meta[n] = {'label': n, 'tints': m.get('tints', {}), 'px': m.get('px', 256), 'head': m.get('head'), 'glow': m.get('glow', False)}
html=r'''<!doctype html><html><head><meta charset="utf-8"><title>__TITLE__</title>
<style>
html,body{margin:0;height:100%;background:#1b1d21;color:#ddd;font:14px system-ui,sans-serif;overflow:hidden}
#bar{position:absolute;top:0;left:0;right:0;min-height:44px;flex-wrap:wrap;row-gap:4px;box-sizing:border-box;padding-top:5px!important;padding-bottom:5px!important;display:flex;gap:14px;align-items:center;padding:0 14px;background:#121317;border-bottom:1px solid #2a2d33;z-index:2}
#bar button{background:#2a2d33;color:#ddd;border:1px solid #3a3e46;border-radius:4px;padding:5px 11px;cursor:pointer;font:inherit}
#bar button.on{background:#c2621f;border-color:#e07a30;color:#fff}
#bar label{display:flex;gap:5px;align-items:center;cursor:pointer}
#views{position:absolute;top:45px;bottom:0;left:0;right:0;display:flex}
.v{flex:1;position:relative;border-right:1px solid #2a2d33}
.v canvas{display:block;width:100%;height:100%}
.tag{position:absolute;top:10px;left:12px;background:#0009;padding:6px 10px;border-radius:4px;pointer-events:none;line-height:1.4}
.tag b{color:#fff}
#help{position:absolute;bottom:10px;left:50%;transform:translateX(-50%);background:#0009;padding:5px 12px;border-radius:4px;color:#aaa;font-size:12px;pointer-events:none}
</style>
<script type="importmap">{"imports":{"three":"https://unpkg.com/three@0.169.0/build/three.module.js","three/addons/":"https://unpkg.com/three@0.169.0/examples/jsm/"}}</script>
</head><body>
<div id="bar"><span style="color:#999">Model:</span><span id="kinds" style="display:flex;flex-wrap:wrap;gap:4px;flex:1 1 600px"></span>
<span style="flex:1"></span>
<label><input type="checkbox" id="wire"> wireframe</label>
<label><input type="checkbox" id="spin"> turntable</label>
<button id="head">head</button><button id="whole">whole</button></div>
<div id="views"><div class="v" id="L"><div class="tag" id="tl"></div></div><div class="v" id="R"><div class="tag" id="tr"></div></div></div>
<div id="help">drag: rotate · scroll: zoom · right-drag: pan · both views move together</div>
<script type="module">
import * as THREE from 'three';
import {OrbitControls} from 'three/addons/controls/OrbitControls.js';
import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
const DATA=__DATA__, META=__META__, KINDS=Object.keys(META);
const loader=new GLTFLoader();let FOCUS={y:1,d:5},first=true;
function bytes(b64){const s=atob(b64),a=new Uint8Array(s.length);for(let i=0;i<s.length;i++)a[i]=s.charCodeAt(i);return a.buffer;}
function makeView(el){
  const r=new THREE.WebGLRenderer({antialias:true});r.setPixelRatio(devicePixelRatio);r.outputColorSpace=THREE.SRGBColorSpace;r.toneMapping=THREE.NeutralToneMapping;
  el.prepend(r.domElement);
  const s=new THREE.Scene();s.background=new THREE.Color(0x8f9ba8);
  s.add(new THREE.HemisphereLight(0xc8d4e4,0x4a4036,1.4));
  const sun=new THREE.DirectionalLight(0xfff1dd,2.6);sun.position.set(4,8,-5);s.add(sun);
  const fill=new THREE.DirectionalLight(0xaabbdd,0.6);fill.position.set(-5,3,4);s.add(fill);
  const g=new THREE.Mesh(new THREE.CircleGeometry(2.5,48),new THREE.MeshStandardMaterial({color:0x4d4a40,roughness:1}));g.rotation.x=-Math.PI/2;s.add(g);
  const c=new THREE.PerspectiveCamera(35,1,0.02,100);
  return {el,r,s,c,root:null};
}
const L=makeView(document.getElementById('L')),R=makeView(document.getElementById('R'));
const cam=new THREE.PerspectiveCamera(35,1,0.02,200);cam.position.set(3,3,5);
const target=new THREE.Vector3(0,1,0);
const cl=new OrbitControls(cam,L.r.domElement),cr=new OrbitControls(cam,R.r.domElement);
for(const c of [cl,cr]){c.target=target;c.enableDamping=true;}
function tris(o){let n=0;o.traverse(m=>{if(m.isMesh){const g=m.geometry;n+=(g.index?g.index.count:g.attributes.position.count)/3;}});return Math.round(n);}
async function load(kind){
  for(const [v,h] of [[L,'high'],[R,'low']]){
    const gl=await loader.parseAsync(bytes(DATA[kind+'_'+h]),'');
    if(v.root)v.s.remove(v.root);
    v.root=gl.scene;
    v.root.traverse(m=>{if(m.isMesh){
      const mats=Array.isArray(m.material)?m.material:[m.material];
      for(const mt of mats){const t=META[kind].tints[mt.name]; if(t&&mt.map)mt.color.setRGB(...t);}
    }});
    v.s.add(v.root);
    if(h==='high'){const b=new THREE.Box3().setFromObject(v.root),s=b.getSize(new THREE.Vector3());FOCUS.y=(b.min.y+b.max.y)/2;FOCUS.d=Math.max(s.x,s.y,s.z)*1.9+1;if(first){first=false;frame(FOCUS.y,FOCUS.d,0.6);}}
    const n=tris(v.root);
    document.getElementById(h==='high'?'tl':'tr').innerHTML=h==='high'
      ?`<b>Detailed</b> (the model now)<br>${n.toLocaleString()} triangles, flat colours + game wood grain`
      :`<b>Baked low-poly</b> (what ships)<br>${n.toLocaleString()} triangles, one ${META[kind].px} px colour / normal${META[kind].glow?' / glow':''} set`;
  }
  setWire();
}
function setWire(){const w=document.getElementById('wire').checked;for(const v of [L,R])v.root&&v.root.traverse(m=>{if(m.isMesh)(Array.isArray(m.material)?m.material:[m.material]).forEach(x=>x.wireframe=w);});}
document.getElementById('wire').onchange=setWire;
const kb=document.getElementById('kinds');let cur=KINDS[0];
for(const k of KINDS){const b=document.createElement('button');b.textContent=META[k].label;b.onclick=()=>{cur=k;[...kb.children].forEach(x=>x.classList.toggle('on',x===b));first=true;load(k);};kb.appendChild(b);}
kb.children[0].classList.add('on');
function frame(y,d,h){target.set(0,y,0);const dir=cam.position.clone().sub(target);dir.y=0;dir.normalize();cam.position.set(dir.x*d,y+h,dir.z*d);}
document.getElementById('head').onclick=()=>frame(META[cur].head??FOCUS.y*1.6,2.6,0.4);
document.getElementById('whole').onclick=()=>frame(FOCUS.y,FOCUS.d,0.6);
function size(){document.getElementById('views').style.top=(document.getElementById('bar').offsetHeight+1)+'px';for(const v of [L,R]){const w=v.el.clientWidth,h=v.el.clientHeight;v.r.setSize(w,h,false);}cam.aspect=L.el.clientWidth/L.el.clientHeight;cam.updateProjectionMatrix();}
addEventListener('resize',size);size();
const clock=new THREE.Clock();
(function tick(){requestAnimationFrame(tick);const dt=clock.getDelta();
  if(document.getElementById('spin').checked){const p=cam.position.clone().sub(target);p.applyAxisAngle(new THREE.Vector3(0,1,0),dt*0.5);cam.position.copy(target).add(p);}
  cl.update();cr.update();for(const v of [L,R])v.r.render(v.s,cam);})();
load(cur);
</script></body></html>'''
html = html.replace('__DATA__', json.dumps(data)).replace('__META__', json.dumps(meta)).replace('__TITLE__', 'Detailed vs baked: ' + ', '.join(meta))
open(out, 'w').write(html); print(out, len(meta), 'models', len(html) // 1024, 'KB')
