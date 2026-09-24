"""Validate the map-kit contract and build review-only sheets/room lighting demo."""
from pathlib import Path
import json, hashlib, base64, io, argparse
import numpy as np
from PIL import Image, ImageDraw, ImageFont
HERE=Path(__file__).resolve()
ROOT=HERE.parents[2]/'Astra Assets' if HERE.parent.name=='tools' else HERE.parents[1]
DEFAULT=ROOT.parent/'cognitohazard-v-1/assets/tilesets'
FONT='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
BOLD='/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'

def font(n,bold=False):
    try: return ImageFont.truetype(BOLD if bold else FONT,n)
    except OSError: return ImageFont.load_default()


def validate(root):
    hashes={}; summaries={}
    for theme in ('industrial','scientific'):
        manifest=json.loads((root/(theme+'.json')).read_text())
        assets=manifest['assets']; assert len(assets)==55, (theme,len(assets))
        assert len({e['id'] for e in assets})==55
        assert sorted(e['connection_mask'] for e in assets if e['category']=='wall')==list(range(16))
        emit_count=0
        for e in assets:
            maps={}
            for mode,path in e['maps'].items():
                file=root/path; im=Image.open(file)
                assert im.mode=='RGBA' and list(im.size)==e['texture_size'],path
                assert e['texture_size']==[v*4 for v in e['world_size']],path
                assert file.read_bytes()[24:26]==bytes([8,6]),path
                maps[mode]=np.array(im); hashes[path]=hashlib.sha256(file.read_bytes()).hexdigest()
            a,n,m=[maps[k] for k in ('albedo','normal','emission')]
            # Shader-only pass substitutions must preserve the exact silhouette.
            assert np.array_equal(a[:,:,3],n[:,:,3]) and np.array_equal(a[:,:,3],m[:,:,3]),e['id']
            visible=a[:,:,3]>250
            assert visible.any(),e['id']
            vectors=n[:,:,:3].astype(float)/127.5-1
            norms=np.linalg.norm(vectors[visible],axis=-1)
            # Antialiasing averages vectors at bevels; consumers renormalize.
            assert abs(np.median(norms)-1)<.025 and np.percentile(norms,5)>.70 and norms.max()<1.02,(e['id'],'invalid normal vectors')
            assert np.median(n[:,:,2][visible])>=250,(e['id'],'normals should face viewer')
            emits=bool(np.max(m[:,:,:3])>5)
            assert emits==e['emits_light'],(e['id'],'emission state mismatch')
            emit_count+=emits
            if e['tileable']:
                assert a[:,:,3].min()==255,e['id']
                for data in (a,n,m):
                    assert abs(data[:,0].astype(int)-data[:,-1]).max()<=2,(e['id'],'horizontal seam')
                    assert abs(data[0].astype(int)-data[-1]).max()<=2,(e['id'],'vertical seam')
                rgb=a[:,:,:3].astype(float)
                assert np.max(abs(rgb-np.mean(rgb,axis=(0,1)))/np.mean(rgb,axis=(0,1)))<=.04,(e['id'],'floor contrast')
            if e['category']=='prop':
                assert not np.any(a[0,:,3]) and not np.any(a[-1,:,3]) and not np.any(a[:,0,3]) and not np.any(a[:,-1,3]),(e['id'],'clipped prop')
        # Verify every compatible wall port, including alpha and normal data.
        walls=[e for e in assets if e['category']=='wall']
        for mode in ('albedo','normal','emission'):
            images={e['connection_mask']:np.array(Image.open(root/e['maps'][mode])).astype(int) for e in walls}
            for bit,opposite,edge,other in ((1,4,lambda a:a[0],lambda a:a[-1]),(2,8,lambda a:a[:,-1],lambda a:a[:,0])):
                for mask,a in images.items():
                    if not mask & bit: continue
                    for neighbor,b in images.items():
                        if neighbor & opposite:
                            assert np.max(abs(edge(a)-other(b)))<=2,(theme,mode,mask,neighbor,'wall port seam')
        summaries[theme]={'assets':55,'textures':165,'emissive_assets':emit_count,'walls':16,'floors':8}
    disk={str(p.relative_to(root)) for p in root.rglob('*.png')}
    assert disk==set(hashes),('unregistered or missing tileset PNGs',sorted(disk^set(hashes)))
    return dict(status='pass',summary=summaries,sha256=hashes)


def contact(root,theme,assets,out):
    w=1120; cellw=140; cellh=125
    sheet=Image.new('RGB',(w,110+7*cellh),(14,19,24)); d=ImageDraw.Draw(sheet)
    d.text((24,18),theme.upper()+' / MAP KIT',font=font(26,True),fill=(222,229,231))
    d.text((24,56),'55 assets  ·  20 px grid  ·  color + normal + emission  ·  Blender 4.5.3',font=font(14),fill=(137,159,171))
    for i,e in enumerate(assets):
        x=(i%8)*cellw; y=100+(i//8)*cellh
        d.rounded_rectangle((x+6,y+3,x+134,y+117),6,fill=(23,29,35))
        im=Image.open(root/e['maps']['albedo']); im.thumbnail((108,88),Image.Resampling.LANCZOS)
        sheet.paste(im,(x+(cellw-im.width)//2,y+8+(88-im.height)//2),im)
        d.text((x+9,y+96),e['id'].replace('_',' '),font=font(10),fill=(184,200,208))
    sheet.save(out/(theme+'_catalog.png'))
    for zoom in (1,1.35):
        sheet=Image.new('RGB',(1120,110+7*115),(14,19,24)); d=ImageDraw.Draw(sheet)
        d.text((24,20),f'{theme.upper()} / {zoom:.2f}x WORLD SCALE',font=font(24,True),fill=(222,229,231))
        d.text((24,58),'View at 100% image size. Tiles and props share their actual relative scale.',font=font(14),fill=(137,159,171))
        for i,e in enumerate(assets):
            x=(i%8)*140; y=100+(i//8)*115
            im=Image.open(root/e['maps']['albedo']).resize(tuple(round(v*zoom) for v in e['world_size']),Image.Resampling.LANCZOS)
            sheet.paste(im,(x+(140-im.width)//2,y+(96-im.height)//2),im)
            d.text((x+8,y+97),e['id'].replace('_',' '),font=font(10),fill=(152,174,185))
        sheet.save(out/(theme+f'_world_{zoom:.2f}x.png'))


def room(root,theme,assets):
    # Review composition only. No map, collision, or stealth simulation is changed.
    lookup={e['id']:e for e in assets}; scale=2; size=(960,640)
    layers={k:Image.new('RGBA',size,(128,128,255,0) if k=='normal' else (0,0,0,0)) for k in ('albedo','normal','emission')}
    cache={}
    def place(name,x,y,rotation=0):
        e=lookup[name]
        for mode,dest in layers.items():
            key=(name,mode,rotation)
            if key not in cache:
                im=Image.open(root/e['maps'][mode]).resize(tuple(v*scale for v in e['world_size']),Image.Resampling.LANCZOS)
                if rotation:
                    im=im.rotate(rotation,expand=True)
                    if mode=='normal':
                        data=np.array(im); nx=data[:,:,0].copy(); ny=data[:,:,1].copy()
                        if rotation==90: data[:,:,0]=255-ny; data[:,:,1]=nx
                        elif rotation==180: data[:,:,0]=255-nx; data[:,:,1]=255-ny
                        elif rotation==270: data[:,:,0]=ny; data[:,:,1]=255-nx
                        im=Image.fromarray(data)
                cache[key]=im
            im=cache[key]; dest.alpha_composite(im,(round(x*scale-im.width/2),round(y*scale-im.height/2)))
    for y in range(40,320,80):
        for x in range(40,480,80): place('floor_plates' if x<240 else 'floor_plain',x,y)
    # Connected perimeter and a room partition with two-cell doorway.
    cells={(x,y) for x in range(24) for y in range(16) if x in (0,23) or y in (0,15)}
    cells|={(12,y) for y in range(1,15) if y not in (7,8)}
    for x,y in sorted(cells):
        mask=sum(bit for bit,dx,dy in ((1,0,-1),(2,1,0),(4,0,1),(8,-1,0)) if (x+dx,y+dy) in cells)
        place('wall_%02d'%mask,x*20+10,y*20+10)
    place('door_open',250,160,90)
    for x,y in ((90,32),(370,32),(90,288),(370,288)): place('light_strip',x,y)
    for y in (100,140,180,220): place('marking_lane',170,y,90)
    for x,y in ((170,70),(170,250),(310,160)): place('marking_chevron',x,y,90 if x==170 else 0)
    for y in range(60,281,20): place('pipe_straight',30,y)
    place('pipe_valve',30,140)
    if theme=='industrial':
        layout=[('generator',75,85),('compressor',130,85),('transformer',75,145),('electrical_cabinet',210,60),
                ('pressure_tank',78,215),('pump',126,215),('control_console',208,270),('workbench',305,60),
                ('tool_cart',350,62),('vent_fan',427,58),('cargo_crate',308,225),('cargo_crate',345,225),
                ('barrel_cluster',420,245),('pallet',305,270),('cable_reel',421,105)]
    else:
        layout=[('lab_bench',77,70),('microscope',126,70),('centrifuge',77,124),('sample_rack',126,124),
                ('fume_hood',76,205),('gas_cylinders',125,209),('sterilizer',205,60),('medical_bed',202,244),
                ('analysis_console',320,63),('server_rack',430,65),('cryopod',307,226),('cryopod',344,226),
                ('containment_chamber',420,225),('robot_arm',420,145),('specimen_freezer',304,278)]
    for name,x,y in layout: place(name,x,y)
    return layers


def relight(layers,theme):
    a=np.array(layers['albedo']).astype(float)/255
    n=np.array(layers['normal'])[:,:,:3].astype(float)/127.5-1
    n/=np.maximum(np.linalg.norm(n,axis=-1,keepdims=True),1e-6)
    em=np.array(layers['emission']).astype(float)/255
    h,w=a.shape[:2]; yy,xx=np.mgrid[:h,:w]
    illumination=np.zeros((h,w,3))+.34
    colors=[(1,.66,.32),(.5,.72,1)] if theme=='industrial' else [(.45,.8,1),(.66,1,.9)]
    for i,(cx,cy) in enumerate(((180,90),(740,100),(180,550),(740,550))):
        dx=cx-xx; dy=yy-cy; dz=100
        dist=np.sqrt(dx*dx+dy*dy+dz*dz)
        ndotl=np.maximum(0,(n[:,:,0]*dx+n[:,:,1]*dy+n[:,:,2]*dz)/dist)
        attenuation=1.9/(1+(dist/240)**2)
        illumination+=ndotl[:,:,None]*attenuation[:,:,None]*np.array(colors[i%2])
    rgb=np.clip(a[:,:,:3]*illumination+em[:,:,:3]*em[:,:,3:4]*.8,0,1)
    return Image.fromarray((rgb*255).astype('uint8'))


def uri(im):
    b=io.BytesIO(); im.save(b,format='PNG'); return 'data:image/png;base64,'+base64.b64encode(b.getvalue()).decode()


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--assets',type=Path,default=DEFAULT); parser.add_argument('--compare',type=Path)
    args=parser.parse_args(); root=args.assets; out=ROOT/'previews/tilesets'; out.mkdir(parents=True,exist_ok=True)
    report=validate(root)
    if args.compare:
        other=validate(args.compare)
        assert report['sha256']==other['sha256'],'Texture reproducibility failure'
        for theme in ('industrial','scientific'): assert (root/(theme+'.json')).read_bytes()==(args.compare/(theme+'.json')).read_bytes(),'Manifest differs'
        report['reproducibility']='330 PNGs and both manifests byte-identical'
    payload={}; rooms=[]
    for theme in ('industrial','scientific'):
        entries=json.loads((root/(theme+'.json')).read_text())['assets']; contact(root,theme,entries,out)
        layers=room(root,theme,entries)
        for mode,im in layers.items(): im.save(out/(theme+'_room_'+mode+'.png'))
        lit=relight(layers,theme); lit.save(out/(theme+'_room_lit.png')); rooms.append(lit)
        payload[theme]={mode:uri(im) for mode,im in layers.items()}
    overview=Image.new('RGB',(1008,1500),(11,16,22)); d=ImageDraw.Draw(overview)
    for i,(theme,im) in enumerate(zip(('industrial','scientific'),rooms)):
        y=i*744
        d.text((24,y+22),theme.upper()+' / '+('SERVICE SECTOR' if i==0 else 'CONTAINMENT LAB'),font=font(26,True),fill=(227,234,238))
        d.text((24,y+59),'Assembled asset preview · illustrative lighting · 55 modular pieces per theme',font=font(14),fill=(145,168,180))
        overview.paste(im,(24,y+88))
    overview.save(out/'two_tilesets.png')
    template=(ROOT/'scripts/tileset_viewer.html').read_text()
    (out/'lighting_preview.html').write_text(template.replace('__ASSET_DATA__',json.dumps(payload)))
    (out/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k!='sha256'},indent=2))

if __name__=='__main__': main()
