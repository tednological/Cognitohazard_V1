#!/usr/bin/env python3
"""Validate delivered PNGs and build game-scale review sheets; never edits renders.
Install Pillow and numpy for normal Python, or use the Codex bundled runtime.
PNG cannot unambiguously declare association; test low-alpha fringe RGB evidence
and require Blender's straight-alpha provenance in the generated manifest.
"""
import json, re, sys, struct, colorsys, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
HERE=Path(__file__).resolve()
PROJECT=HERE.parents[1] if HERE.parent.name=='tools' else HERE.parents[2]/'cognitohazard-v-1'
ROOT=PROJECT.parent/'Astra Assets'
ASSETS=PROJECT/'assets'; PREVIEWS=ROOT/'previews'
NAMES=['glock','mp7','ak47','remington','saw','welrod','vss','photon','arc_lance','vulcan','tesla','frag','awm']
TINTS={'legs':'#B88F5C','torso':'#B88F5C','head':'#B88F5C','arms':'#B88F5C','armour':'#9EB2CC','body_prone':'#613833','surface':'#0D1116'}
FLOORS=['#0D1116','#161C23']

def rgb(v): return tuple(bytes.fromhex(v.lstrip('#')))

def tint(im,color):
    a=np.array(im).copy(); a[:,:,:3]=np.rint(a[:,:,:3].astype(float)*np.array(rgb(color))[None,None,:]/255).astype('uint8')
    return Image.fromarray(a)

def read_catalogue():
    return {int(i):(name,int(w),int(h)) for i,name,w,h in re.findall(r'new\s+GearItem\(\s*(\d+)\s*,\s*"([^"]+)"\s*,\s*(\d+)\s*,\s*(\d+)',(PROJECT/'sim/GearCatalog.cs').read_text())}

def validate(entries):
    errors=[]; catalog=read_catalogue(); paths={e['path'] for e in entries}
    def fail(p,msg): errors.append(f'{p}: {msg}')
    expected={f'actors/actor_legs_{i:02}.png' for i in range(8)}
    expected|={f'actors/actor_legs_{i:02}_detail.png' for i in range(8)}
    expected|={f'actors/{n}.png' for n in ['actor_torso','actor_torso_detail','actor_head','actor_head_detail','armour_light','armour_medium','armour_heavy']}
    expected|={f'actors/body_prone_{i}.png' for i in range(4)}
    expected|={f'weapons/held_{n}{suffix}.png' for n in NAMES for suffix in ['', '_arms']}
    expected|={f'props/{n}.png' for n in ['chest_full','chest_open','objective_case','objective_site_empty','cache_full','cache_taken','ground_bag','grenade','door_leaf_2','door_leaf_3','door_jamb']}
    expected|={'surfaces/floor_tile.png','surfaces/wall_tile.png'}
    expected|={f'items/item_{i}.png' for i in list(catalog)+['unknown']}
    for p in sorted(expected-paths): fail(p,'missing required manifest entry')
    disk={str(p.relative_to(ASSETS)) for p in ASSETS.rglob('*.png')}
    for p in disk-paths: fail(p,'PNG not in manifest')
    if len(entries)!=len(paths): fail('manifest','duplicate paths')
    reserved=[colorsys.rgb_to_hsv(*(v/255 for v in rgb(h)))[0] for h in ['#E65247','#EBB240','#66C2D9','#8CD9F2','#40B87A','#5CD166','#5799FF','#B86BFA','#FF9929']]
    for e in entries:
        p=e['path']; f=ASSETS/p
        if not f.exists(): fail(p,'missing PNG'); continue
        raw=f.read_bytes(); im=Image.open(f); a=np.array(im)
        if raw[24]!=8 or raw[25]!=6 or im.mode!='RGBA': fail(p,'must be RGBA 8-bit'); continue
        if im.size!=tuple(round(v*4) for v in e['world_size']): fail(p,'world size must be texture / 4')
        expected_size=None
        if p.startswith(('actors/actor_', 'actors/armour_', 'weapons/')): expected_size=(224,224)
        if p.startswith('actors/body_prone_'): expected_size=(112,112)
        prop_sizes={'chest_full':(80,64),'chest_open':(80,64),'objective_case':(80,64),'objective_site_empty':(80,64),'cache_full':(64,64),'cache_taken':(64,64),'ground_bag':(88,72),'grenade':(48,48),'door_leaf_2':(136,40),'door_leaf_3':(216,40),'door_jamb':(12,80),'floor_tile':(320,320),'wall_tile':(160,160)}
        expected_size=prop_sizes.get(f.stem,expected_size)
        if expected_size and im.size!=expected_size: fail(p,'contract canvas size differs')
        must_tint=e['layer'] in ['legs','torso','head','arms','armour','body_prone'] or p=='surfaces/floor_tile.png'
        if e['tinted']!=must_tint: fail(p,'tint flag disagrees with layer contract')
        if e.get('texture_size')!=list(im.size): fail(p,'texture_size disagrees')
        if e['pivot']!=[v/2 for v in e['world_size']]: fail(p,'pivot not centered')
        if not (ROOT/'scripts'/e['source']).is_file(): fail(p,'source script missing')
        alpha=a[:,:,3]; opaque=alpha>=250
        if not opaque.any(): fail(p,'empty render')
        if e.get('alpha')!='straight': fail(p,'missing straight-alpha renderer provenance')
        fringe=(alpha>0)&(alpha<100)
        if fringe.any() and not np.any(a[:,:,:3].max(2)[fringe]>alpha[fringe]+4): fail(p,'fringe appears premultiplied')
        if e['tinted'] and np.any(np.ptp(a[:,:,:3].astype(int),axis=2)[alpha>0]>2): fail(p,'tinted layer not greyscale')
        if e.get('body_circle_radius'):
            yy,xx=np.indices(alpha.shape); px,py=e['pivot']
            dist=((xx+.5)/4-px)**2+((yy+.5)/4-py)**2
            if np.any(opaque & (dist>121)): fail(p,f'body outside Ø22 ({int(np.sum(opaque & (dist>121)))} pixels)')
        if e['layer']=='held':
            anchor=e['anchors'].get('muzzle'); px,py=e['pivot']
            if anchor != [px+22,py]: fail(p,'muzzle must be exactly (+22,0)')
            if not p.endswith('frag.png'):
                # The outlined barrel end must reach the anchor, allowing 0.5 wpx line width.
                y=round(py*4); x=round((px+22)*4)
                if not np.any(alpha[y-4:y+5,x-3:x+3]>200): fail(p,'geometry does not reach muzzle anchor')
        if e['layer']=='surface':
            aa=a[:,:,:3].astype(int)
            if np.max(abs(aa[:,0]-aa[:,-1]))>2 or np.max(abs(aa[0]-aa[-1]))>2: fail(p,'tile edge mismatch')
            mean=aa.mean((0,1)); budget=.04 if 'floor' in p else .06
            if np.max(abs(aa-mean)/np.maximum(mean,1))>budget: fail(p,'surface contrast exceeds budget')
            if not np.all(alpha==255): fail(p,'surface not opaque')
        if e['layer']=='item':
            key=f.stem[5:]; w,h=(1,1) if key=='unknown' else catalog.get(int(key),('',0,0))[1:]
            if im.size!=(w*80,h*80): fail(p,'catalogue footprint differs')
            if np.any(alpha[:8]) or np.any(alpha[-8:]) or np.any(alpha[:,:8]) or np.any(alpha[:,-8:]): fail(p,'8-pixel inset infringed')
        allow= any(s in p for s in ['held_photon.png','held_arc_lance.png','held_tesla.png','objective_case','item_107.png','item_108.png','item_110.png','item_900.png'])
        unique=np.unique(a[:,:,:3][opaque],axis=0)
        for color in unique:
            h,s,v=colorsys.rgb_to_hsv(*(color/255))
            if s>.52 and v>.32 and any(min(abs(h-r),1-abs(h-r))<.035 for r in reserved):
                cyan=min(abs(h-.53),1-abs(h-.53))<.075
                if not (allow and cyan): fail(p,f'reserved saturated hue {tuple(color)}'); break
    return errors


def font(size=11):
    for path in ['/System/Library/Fonts/Menlo.ttc','/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf']:
        if Path(path).exists(): return ImageFont.truetype(path,size)
    return ImageFont.load_default()

def sprite(e,scale,floor):
    im=Image.open(ASSETS/e['path']).convert('RGBA')
    if e['tinted']: im=tint(im,floor if e['layer']=='surface' else TINTS[e['layer']])
    return im.resize(tuple(max(1,round(v*scale)) for v in e['world_size']),Image.Resampling.LANCZOS)

def paste_center(dst,im,x,y): dst.alpha_composite(im,(round(x-im.width/2),round(y-im.height/2)))

def atlas(entries,scale):
    cw=300; ch=142; cols=4; top=75
    sheet=Image.new('RGBA',(cols*cw,top+math.ceil(len(entries)/cols)*ch),(8,11,15,255)); d=ImageDraw.Draw(sheet)
    d.text((20,14),f'COGNITOHAZARD / ALL {len(entries)} LAYERS / {scale:.2f}x GAME SCALE',font=font(18),fill='#DFE3E8')
    d.text((20,43),'Left: shadow floor     Right: lit floor     Tints applied; no sprite enlargement.',font=font(11),fill='#929AA7')
    for i,e in enumerate(entries):
        x=(i%cols)*cw; y=top+(i//cols)*ch
        for k,floor in enumerate(FLOORS):
            d.rectangle((x+k*150+2,y,x+k*150+147,y+110),fill=floor)
            paste_center(sheet,sprite(e,scale,floor),x+k*150+75,y+55)
        d.text((x+5,y+116),Path(e['path']).stem,font=font(10),fill='#B9C1CC')
    sheet.convert('RGB').save(PREVIEWS/f'contact_all_{scale:.2f}x.png')

def actor(entries,weapon='ak47',armour=None,color='#B88F5C',frame=0):
    im=Image.new('RGBA',(224,224)); idx={e['path']:e for e in entries}
    files=[f'actors/actor_legs_{frame:02}.png',f'actors/actor_legs_{frame:02}_detail.png','actors/actor_torso.png','actors/actor_torso_detail.png']
    if armour: files+=[f'actors/armour_{armour}.png']
    files += [f'weapons/held_{weapon}_arms.png',f'weapons/held_{weapon}.png','actors/actor_head.png','actors/actor_head_detail.png']
    for p in files:
        layer=Image.open(ASSETS/p).convert('RGBA')
        if idx[p]['tinted']: layer=tint(layer,'#9EB2CC' if 'armour_' in p else color)
        im.alpha_composite(layer)
    return im

def review(entries,scale):
    W=1100; sheet=Image.new('RGBA',(W,960),'#080B0F'); d=ImageDraw.Draw(sheet)
    d.text((24,16),f'COGNITOHAZARD / READABILITY REVIEW / {scale:.2f}x',font=font(20),fill='#DEE3EC')
    d.text((24,48),'Actual game size. Top in each pair: shadow floor; bottom: lit floor.',font=font(12),fill='#A0A9B7')
    groups=[]
    actors=[('player',actor(entries,color='#D9DEEB'))]+[(n,actor(entries,armour=n if n!='unarmoured' else None)) for n in ['unarmoured','light','medium','heavy']]
    groups.append(('RIG / ARMOUR SILHOUETTES',actors))
    groups.append(('WEAPON CLASSES',[(n,actor(entries,n)) for n in NAMES]))
    groups.append(('DISTANCE-DRIVEN WALK / 8 FRAMES',[(str(i),actor(entries,frame=i)) for i in range(8)]))
    idx={Path(e['path']).stem:e for e in entries}
    props=['chest_full','chest_open','objective_case','objective_site_empty','cache_full','cache_taken','ground_bag','grenade','door_leaf_2','door_leaf_3']
    groups.append(('PROPS / STATES',[(n,Image.open(ASSETS/idx[n]['path']).convert('RGBA')) for n in props]))
    bodies=[]
    for i in range(4):
        base=Image.open(ASSETS/f'actors/body_prone_{i}.png').convert('RGBA')
        bodies += [(f'dead {i}',tint(base,'#613833')),(f'down {i}',tint(base,'#4C6B8F'))]
    groups.append(('UNARMED BODIES',bodies))
    y=90
    for label,images in groups:
        d.text((24,y),label,font=font(12),fill='#D0D6DF'); y+=24
        cell=(W-48)/len(images)
        for k,floor in enumerate(FLOORS):
            d.rectangle((24,y+k*68,W-24,y+(k+1)*68-2),fill=floor)
            for j,(name,im) in enumerate(images):
                r=im.resize((round(im.width/4*scale),round(im.height/4*scale)),Image.Resampling.LANCZOS)
                paste_center(sheet,r,24+(j+.5)*cell,y+k*68+31)
        for j,(name,_) in enumerate(images):
            short=name.replace('objective_','obj_').replace('chest_','').replace('cache_','rec_').replace('door_leaf_','door ')
            d.text((24+(j+.5)*cell,y+139),short,font=font(9),fill='#AAB3C0',anchor='mt')
        y+=166
    sheet.convert('RGB').save(PREVIEWS/f'contact_world_{scale:.2f}x.png')


def main():
    entries=json.loads((ASSETS/'manifest.json').read_text()); errors=validate(entries)
    if errors:
        print('\n'.join('FAIL '+e for e in errors)); print(f'{len(errors)} validation failures'); return 1
    PREVIEWS.mkdir(exist_ok=True)
    for scale in [1.,1.35]: atlas(entries,scale); review(entries,scale)
    print(f'PASS: {len(entries)} PNGs; {len(read_catalogue())} catalogue icons + unknown; sizes, alpha, greyscale, body bounds, muzzle anchors, tiling, contrast, insets and palette.')
    print('Contact sheets: '+str(PREVIEWS))
    return 0
if __name__=='__main__': sys.exit(main())
