"""Cognitohazard deterministic sprite rig. Blender 4.5.3, 1 BU = 1 wpx.
Only this module owns render state. PNGs are Blender's straight-alpha output.
"""
from pathlib import Path
import bpy, math, json, re, os, random
from mathutils import Vector
PINNED_VERSION = (4, 5, 3)
assert bpy.app.version == PINNED_VERSION, (bpy.app.version, PINNED_VERSION)
ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT.parent / 'cognitohazard-v-1'
OUT = Path(os.environ.get('COG_OUTPUT', str(PROJECT / 'assets')))
SEED = 1729
SCALE = 4
BODY = (.95,)*3
FOLD = (.72,)*3
METAL = (.43,.46,.47)
EDGE = (.60,.62,.61)
DARK = (.22,.24,.25)
POLY = (.31,.32,.30)
OLIVE = (.44,.45,.37)
CANVAS = (.57,.54,.45)
WOOD = (.46,.38,.29)
CYAN = (.40,.76,.85)
MATS = {}

def linear(c):
    return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4

def mat(name, rgb, ao=True):
    if name in MATS: return MATS[name]
    m=bpy.data.materials.new(name); m.use_nodes=True
    m.diffuse_color=(*[linear(v) for v in rgb],1)
    m.line_color=(*[linear(v*.35) for v in rgb],1)
    n=m.node_tree.nodes; n.clear(); links=m.node_tree.links
    out=n.new('ShaderNodeOutputMaterial'); diffuse=n.new('ShaderNodeBsdfDiffuse')
    diffuse.inputs['Color'].default_value=m.diffuse_color
    diffuse.inputs['Roughness'].default_value=0
    if ao:
        a=n.new('ShaderNodeAmbientOcclusion'); a.inputs['Color'].default_value=m.diffuse_color
        a.inputs['Distance'].default_value=1.0; a.samples=16
        links.new(a.outputs['Color'],diffuse.inputs['Color'])
    links.new(diffuse.outputs[0],out.inputs['Surface']); MATS[name]=m
    return m

def setup(w,h,outline=True):
    global MATS
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MATS={}
    random.seed(SEED)
    s=bpy.context.scene
    s.render.engine='BLENDER_EEVEE_NEXT'
    s.eevee.taa_render_samples=64
    s.render.resolution_x=round(w*4); s.render.resolution_y=round(h*4)
    s.render.resolution_percentage=100; s.render.film_transparent=True
    s.render.image_settings.file_format='PNG'; s.render.image_settings.color_mode='RGBA'
    s.render.image_settings.color_depth='8'; s.render.image_settings.compression=15
    s.render.filter_size=1.5; s.render.use_file_extension=True
    s.view_settings.view_transform='Standard'; s.view_settings.look='None'
    s.view_settings.exposure=0; s.view_settings.gamma=1
    if not s.world: s.world=bpy.data.worlds.new('Ambient')
    s.world.use_nodes=True; s.world.node_tree.nodes['Background'].inputs[0].default_value=(.005,.005,.005,1)
    s.world.node_tree.nodes['Background'].inputs[1].default_value=.1
    bpy.ops.object.camera_add(location=(0,0,200),rotation=(0,0,0))
    c=bpy.context.object; c.data.type='ORTHO'; c.data.ortho_scale=w
    # Blender uses the longest image dimension for ortho_scale on portrait canvases.
    c.data.sensor_fit='HORIZONTAL'; c.data.clip_end=500; s.camera=c
    bpy.ops.object.light_add(type='SUN',location=(0,0,100),rotation=(0,0,0))
    sun=bpy.context.object.data; sun.energy=math.pi; sun.angle=0; sun.use_shadow=False
    s.render.use_freestyle=outline; s.render.line_thickness_mode='ABSOLUTE'; s.render.line_thickness=1
    fs=bpy.context.view_layer.freestyle_settings
    ls=fs.linesets[0] if fs.linesets else fs.linesets.new('Silhouette and material borders')
    if ls.linestyle is None:
        ls.linestyle=bpy.data.linestyles.new('Cognitohazard outline')
    for attr in ('select_silhouette','select_border','select_material_boundary'):
        setattr(ls,attr,True)
    for attr in ('select_crease','select_edge_mark','select_external_contour','select_suggestive_contour','select_ridge_valley'):
        setattr(ls,attr,False)
    ls.linestyle.color=(linear(.35),)*3; ls.linestyle.thickness=4
    ls.linestyle.caps='ROUND'; ls.linestyle.use_chaining=True
    if not ls.linestyle.color_modifiers:
        mod=ls.linestyle.color_modifiers.new('Material-relative outline','MATERIAL')
        mod.material_attribute='LINE'
    s.render.threads_mode='FIXED'; s.render.threads=4
    return s

def poly(name,pts,z,depth,color,bevel=.12):
    m=mat(str(color),color)
    N=len(pts); verts=[(x,y,z) for x,y in pts]+[(x,y,z+depth) for x,y in pts]
    faces=[tuple(reversed(range(N))),tuple(range(N,2*N))]
    faces += [(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)]
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.materials.append(m)
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
    if bevel:
        mod=obj.modifiers.new('Soft manufactured edge','BEVEL'); mod.width=bevel; mod.segments=3
        mod.affect='EDGES'
    return obj

def box(name,x,y,w,h,z=1,depth=.6,color=METAL,r=.4):
    r=min(r,w/2,h/2)
    pts=[]
    for cx,cy,start in [(x+w/2-r,y+h/2-r,0),(x-w/2+r,y+h/2-r,90),(x-w/2+r,y-h/2+r,180),(x+w/2-r,y-h/2+r,270)]:
        for i in range(5):
            a=math.radians(start+i*22.5); pts.append((cx+r*math.cos(a),cy+r*math.sin(a)))
    return poly(name,pts,z,depth,color,min(.12,depth*.2))

def oval(name,x,y,rx,ry,z=1,depth=.7,color=BODY):
    return poly(name,[(x+rx*math.cos(i*math.tau/48),y+ry*math.sin(i*math.tau/48)) for i in range(48)],z,depth,color,.16)

def limb(name,a,b,width,z=1,color=BODY):
    dx=b[0]-a[0]; dy=b[1]-a[1]; L=math.hypot(dx,dy)
    o=box(name,0,0,L+width,width,z,.65,color,width/2)
    o.rotation_euler.z=math.atan2(dy,dx); o.location.x=(a[0]+b[0])/2; o.location.y=(a[1]+b[1])/2
    return o

def fit(w,h,inset=3):
    objs=[o for o in bpy.context.scene.objects if o.type=='MESH']
    bpy.context.view_layer.update()
    points=[o.matrix_world @ Vector(v) for o in objs for v in o.bound_box]
    lo=[min(v[i] for v in points) for i in range(2)]; hi=[max(v[i] for v in points) for i in range(2)]
    scale=min((w-2*inset)/(hi[0]-lo[0]),(h-2*inset)/(hi[1]-lo[1]))
    for o in objs:
        o.location.x=(o.location.x-(lo[0]+hi[0])/2)*scale
        o.location.y=(o.location.y-(lo[1]+hi[1])/2)*scale
        o.scale.x*=scale; o.scale.y*=scale
    bpy.context.view_layer.update()

def catalogue():
    src=(PROJECT/'sim/GearCatalog.cs').read_text()
    rows=re.findall(r'new\s+GearItem\(\s*(\d+)\s*,\s*"([^"]+)"\s*,\s*(\d+)\s*,\s*(\d+)',src)
    assert rows, 'No catalogue rows parsed'
    return [(int(i),n,int(w),int(h)) for i,n,w,h in rows]

def render(path,w,h,source,tinted=False,layer='prop',anchors=None,body_circle=False):
    if os.environ.get('COG_ONLY') and os.environ['COG_ONLY'] != path:
        return
    target=OUT/path; target.parent.mkdir(parents=True,exist_ok=True)
    s=bpy.context.scene; s.render.filepath=str(target)
    bpy.ops.render.render(write_still=True)
    entry={'path':path,'world_size':[w,h],'texture_size':[round(w*4),round(h*4)],'pivot':[w/2,h/2],
           'anchors':anchors or {},'tinted':tinted,'layer':layer,'source':source,'alpha':'straight',
           'blender_version':'.'.join(map(str,PINNED_VERSION)),'seed':SEED}
    if body_circle: entry['body_circle_radius']=11
    manifest=OUT/'manifest.json'; data=json.loads(manifest.read_text()) if manifest.exists() else []
    data=[e for e in data if e['path']!=path]+[entry]
    manifest.write_text(json.dumps(sorted(data,key=lambda e:e['path']),indent=2)+'\n')
    print('COG_RENDERED',path,flush=True)
