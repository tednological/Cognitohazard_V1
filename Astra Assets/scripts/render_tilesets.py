"""Two deterministic map kits. Run with Blender 4.5.3; see TILESETS.md.
Every PNG, including normal/emission companions, is a direct Blender render.
"""
import sys, os, json, math
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
import cog_common as c

ROOT = c.ROOT
OUT = Path(os.environ.get('COG_TILESET_OUTPUT', str(c.PROJECT / 'assets/tilesets')))
THEMES = ('industrial', 'scientific')
PALETTES = {
    'industrial': dict(shell=(.34,.36,.35), trim=(.49,.48,.42), dark=(.16,.18,.19),
                       accent=(.55,.43,.25), light=(1,.66,.27), floor=(.115,.126,.139)),
    'scientific': dict(shell=(.48,.55,.57), trim=(.69,.73,.71), dark=(.17,.23,.26),
                       accent=(.27,.48,.54), light=(.38,.84,1), floor=(.13,.15,.17)),
}
P = {}
EMITTERS = {}

def box(name,x,y,w,h,z=1,d=.6,col=None,r=.3):
    ob=c.box(name,x,y,w,h,z,d,col or P['shell'],r)
    ob.modifiers[0].width=min(.35,d*.4)
    return ob

def disc(name,x,y,r,z=1,d=.6,col=None):
    return c.oval(name,x,y,r,r,z,d,col or P['shell'])

def line(name,a,b,width=1,z=2,col=None):
    ob=c.limb(name,a,b,width,z,col or P['trim'])
    return ob

def glow(ob, rgb=None):
    # Material copies allow emission to be switched independently of housings.
    m=ob.data.materials[0].copy(); m.name='Emitter '+ob.name
    ob.data.materials[0]=m; EMITTERS[m.name]=rgb or P['light']
    return ob

def bolts(w,h,z=3):
    for x in (-w/2+1.4,w/2-1.4):
        for y in (-h/2+1.4,h/2-1.4): disc('Fastener',x,y,.48,z,.15,P['trim'])

def chassis(w,h):
    box('Dark silhouette',0,0,w,h,1,1.2,P['dark'],1.2)
    box('Painted casing',0,0,w-1.6,h-1.6,2.2,1.1,P['shell'],.9)
    bolts(w-1,h-1,3.4)

def grille(x,y,w,h,z=3.4):
    box('Recess',x,y,w,h,z,.2,P['dark'])
    for j in range(max(2,int(h/2))):
        yy=y-h/2+.7+j*(h-1.4)/max(1,int(h/2)-1)
        box('Vent slat',x,yy,w-1,.55,z+.3,.3,P['trim'],.1)

def screen(x,y,w,h,z=3.5):
    box('Screen bezel',x,y,w+1.2,h+1.2,z,.6,P['dark'])
    box('Glass screen',x,y,w,h,z+.6,.2,P['accent'])
    for j in range(3): glow(box('Telemetry',x-w*.1,y+h*.25-j*h*.25,w*.55,.4,z+.9,.1,P['trim'],.1),tuple(v*.4 for v in P['light']))

def floor(kind):
    # All edges share an uninterrupted flat material; details are low contrast.
    base=P['floor']; lighter=tuple(v+.002 for v in base); darker=tuple(v-.002 for v in base)
    box('Continuous substrate',0,0,82,82,0,.25,base,0)
    if kind=='plain':
        for x,y,w,h in [(-18,12,12,.3),(20,-22,9,.3),(8,27,5,.3)]: box('Wear',x,y,w,h,.3,.05,lighter,0)
    elif kind=='raised':
        box('Raised service deck',0,0,64,64,.3,.2,lighter,1)
        for x in (-28,28):
            for y in (-28,28): disc('Deck mounting',x,y,1,.55,.08,darker)
        for y in range(-24,25,8):
            for x in range(-24,25,8):
                ob=box('Anti slip tread',x,y,2,.5,.55,.07,darker,.1); ob.rotation_euler.z=math.pi/4
    elif kind=='plates':
        for x,y in [(-20,-20),(20,-20),(-20,20),(20,20)]:
            box('Large inset panel',x,y,37,37,.3,.1,lighter,.8)
            for dx in (-16,16): disc('Inset bolt',x+dx,y-16,.45,.42,.05,darker)
    elif kind=='grate':
        box('Grate bed',0,0,65,65,.3,.15,darker,.8)
        for j in range(-30,31,4): box('Crossbar',j,0,1.3,62,.5,.16,lighter,.1)
        for j in (-29,0,29): box('Support',0,j,62,.8,.7,.1,lighter,.1)
    elif kind=='drain':
        box('Drain recess',0,0,9,68,.3,.1,darker,.5)
        for y in range(-31,32,3): box('Drain slot',0,y,6,1,.5,.1,lighter,.1)
    elif kind=='conduit':
        for x in (-2,2): box('Service channel',x,0,.7,70,.3,.08,darker,.1)
        for y in (-28,0,28): box('Channel clip',0,y,8,.8,.45,.1,lighter,.1)
    elif kind=='inset':
        box('Access recess',0,0,29,29,.3,.1,darker,2)
        box('Access lid',0,0,26,26,.45,.12,lighter,1.6)
        for x in (-10,10): box('Flush handle',x,0,1,5,.6,.1,darker,.2)
    elif kind=='worn':
        for j in range(17):
            x=math.sin(j*17)*32; y=math.cos(j*11)*32
            ob=box('Scuff',x,y,2+j%5,.3,.3,.05,lighter,0); ob.rotation_euler.z=j*.71


def wall(mask):
    # Cardinal ports N=1,E=2,S=4,W=8; arms extend past frame to avoid alpha seams.
    box('Wall core',0,0,14,14,1,5,P['dark'],.6)
    box('Wall cap',0,0,11,11,6,.8,P['shell'],.5)
    for bit,x,y,w,h in [(1,0,7,14,8),(2,7,0,8,14),(4,0,-7,14,8),(8,-7,0,8,14)]:
        if mask & bit:
            box('Connected wall',x,y,w,h,1,5,P['dark'],0)
            box('Connected cap',x,y,11 if x==0 else 8,11 if y==0 else 8,6,.8,P['shell'],0)
    if mask in (0,1,2,4,8): box('Service cap',0,0,5,5,6.9,.25,P['trim'],.4)


def pipe(kind):
    directions={'straight':[1,4],'elbow':[1,2],'tee':[1,2,4],'cross':[1,2,4,8],'valve':[1,4]}[kind]
    for bit in directions:
        end={1:(0,12),2:(12,0),4:(0,-12),8:(-12,0)}[bit]
        line('Pipe casing',(0,0),end,4.2,1,P['dark'])
        line('Pipe crown',(0,0),end,2.8,1.8,P['accent'])
        x,y=end[0]*.65,end[1]*.65
        box('Union collar',x,y,5 if not x else 1.6,5 if not y else 1.6,2.7,.6,P['trim'])
    disc('Junction',0,0,2.3,2,1,P['accent'])
    if kind=='valve':
        disc('Valve rim',0,0,5,3,1,P['trim']); disc('Valve recess',0,0,3.7,4,.2,P['dark'])
        for a in range(0,360,90):
            ang=math.radians(a); line('Valve spoke',(0,0),(3.7*math.cos(ang),3.7*math.sin(ang)),.9,4.3,P['accent'])
        disc('Valve hub',0,0,1.3,4.5,.5,P['trim'])


def door(kind):
    box('Threshold',0,0,40,16,.2,.4,P['dark'],0)
    for x in (-18,18): box('Door jamb',x,0,4,18,1,4,P['shell'],.5)
    if kind=='open':
        for x in (-15,15): box('Retracted leaf',x,0,2.6,11,1,2,P['trim'])
    else:
        for x in (-8,8):
            box('Armoured leaf',x,0,15.2,11,1,2,P['shell'],.5)
            box('Inset plate',x,0,12,7,3,.4,P['trim'])
            box('Grip',x*.23,0,.8,3,3.6,.4,P['dark'])
        if kind=='sealed':
            box('Locking bar',0,0,29,2,4,.7,P['accent'])
            disc('Lock actuator',0,0,2.6,4.8,.5,P['dark'])
    glow(box('Door status',-18,0,1,3,5.1,.2,P['trim']),P['light'])


def fixture(kind):
    broken=kind=='broken'; off=kind=='off'
    if kind in ('strip','off','broken'):
        chassis(32,8)
        box('Lamp well',0,0,26,4,3.4,.3,P['dark'])
        for x,w in ([(-8,7),(7,6)] if broken else [(0,24)]):
            ob=box('Diffuser',x,0,w,2.6,3.8,.5,P['accent'] if off or broken else P['trim'])
            if not (off or broken): glow(ob)
        for x in (-11,0,11): box('Guard rib',x,0,.65,5,4.5,.35,P['shell'])
        if broken: line('Exposed wire',(-3,-1),(3,1),.4,4,P['trim'])
    elif kind=='beacon':
        disc('Beacon base',0,0,7,1,1.3,P['dark']); disc('Metal rim',0,0,5.8,2.3,1,P['trim'])
        glow(disc('Beacon lens',0,0,4.4,3.3,1,P['accent']))
        for x in (-3,3): box('Protective cage',x,0,.6,10,4.5,.4,P['shell'])
    elif kind=='flood':
        chassis(18,18); box('Reflector',0,0,13,13,3.4,.2,P['dark'],2)
        for x in (-3,3):
            for y in (-3,3): glow(box('LED bank',x,y,4,4,3.8,.5,P['trim']))


def decal(kind):
    col=P['accent']
    if kind=='lane':
        for y in (-6,6): box('Lane stripe',0,y,40,1,0,.05,col,0)
    elif kind=='corner':
        box('Corner stripe',-7,0,1,16,0,.05,col,0); box('Corner stripe',0,7,16,1,0,.05,col,0)
    elif kind=='chevron':
        for x in (-10,0,10):
            line('Chevron',(x-3,4),(x+1,0),1,0,col); line('Chevron',(x+1,0),(x-3,-4),1,0,col)
    elif kind=='boundary':
        for x in range(-18,19,6): box('Boundary dash',x,0,3,2,0,.05,col,0)


def industrial(kind):
    if kind=='generator':
        chassis(34,24); grille(-8,0,10,17)
        box('Motor cover',6,1,12,16,3.5,2,P['accent'],2)
        for y in (-5,-2,1,4,7): box('Cooling fin',6,y,13,.75,5.6,.5,P['trim'])
        screen(6,-8,7,2)
    elif kind=='compressor':
        chassis(32,24)
        for y in (-5,5):
            box('Pressure tank',0,y,25,7,3.5,3,P['accent'],3.5)
            for x in (-8,8): box('Tank strap',x,y,1.5,7.3,6.6,.4,P['trim'])
        disc('Pressure dial',12,0,2.5,4,.8,P['trim'])
    elif kind=='transformer':
        chassis(28,28); box('Core',0,0,15,18,3.5,3,P['accent'])
        for x in (-9,9):
            for y in range(-10,11,4): disc('Insulator',x,y,2,3.6,2,P['trim'])
        for y in (-6,0,6): box('Core rib',0,y,14,1.2,6.6,.3,P['dark'])
    elif kind=='control_console':
        chassis(34,18); screen(-6,1,15,9)
        for x in (6,10,14):
            for y in (-3,1,5): disc('Switch',x,y,.85,3.7,.5,P['accent'])
    elif kind=='electrical_cabinet':
        chassis(24,32); box('Cabinet door',0,0,19,27,3.5,.4,P['trim'])
        grille(0,8,14,7,4); box('Handle',7,-4,1,5,4,.7,P['dark'])
        box('Service label',-2,-6,5,6,4,.2,P['accent'])
    elif kind=='barrel_cluster':
        for x,y in [(-7,-5),(7,-5),(0,8)]:
            disc('Drum rim',x,y,6.5,1,3,P['dark']); disc('Drum lid',x,y,5.5,4,.5,P['accent'])
            disc('Bung',x+2,y+2,.8,4.6,.3,P['trim'])
    elif kind=='pallet':
        for y in (-8,8): box('Pallet runner',0,y,28,3,1,1,P['dark'])
        for x in (-12,-6,0,6,12): box('Pallet slat',x,0,4,24,2,1,P['accent'])
    elif kind=='cargo_crate':
        chassis(28,28)
        for x in (-9,9): box('Cargo band',x,0,2,26,3.5,.7,P['trim'])
        for y in (-9,9): box('Cargo rib',0,y,26,2,3.5,.7,P['accent'])
        box('Cargo seal',0,0,7,8,3.5,.4,P['dark'])
    elif kind=='workbench':
        chassis(38,20)
        box('Worktop',0,0,33,15,3.5,.7,P['trim'])
        box('Vice base',11,1,7,8,4.3,.5,P['dark']); box('Vice jaw',11,1,8,2,4.9,1,P['accent'])
        line('Wrench',(-10,-4),(-4,3),1.1,4.4,P['dark']); disc('Tool head',-4,3,2,4.5,.5,P['dark'])
    elif kind=='vent_fan':
        chassis(28,28); disc('Duct',0,0,11,3.5,.3,P['dark'])
        for a in range(0,360,90):
            ob=box('Fan blade',0,0,12,5,4,.5,P['trim'],2); ob.location.x=4; ob.rotation_euler.z=math.radians(a)
            # Rotate offset as well as blade around fan hub.
            ob.location=(4*math.cos(math.radians(a)),4*math.sin(math.radians(a)),0)
        disc('Hub',0,0,3,4.6,.5,P['accent'])
        for x in (-7,0,7): box('Safety grille',x,0,.6,22,5.2,.4,P['shell'])
    elif kind=='cable_reel':
        disc('Reel flange',0,0,13,1,1,P['accent']); disc('Cable coil',0,0,10.5,2,2,P['dark'])
        for r in (4,6,8,10):
            for j in range(48):
                a=j*math.tau/48; b=(j+1)*math.tau/48
                line('Coiled cable',(r*math.cos(a),r*math.sin(a)),(r*math.cos(b),r*math.sin(b)),.5,4,P['shell'])
        disc('Spindle',0,0,2.5,4.5,1,P['trim'])
    elif kind=='pump':
        chassis(34,20); disc('Impeller casing',7,0,8,3.5,2,P['accent'])
        box('Motor',-8,0,12,12,3.5,2,P['shell'])
        for x in (-12,-9,-6,-3): box('Motor fin',x,0,.8,13,5.5,.4,P['trim'])
        line('Outlet',(7,0),(15,6),3,5.6,P['trim'])
    elif kind=='tool_cart':
        chassis(24,18)
        for y in (-5,0,5):
            box('Drawer',0,y,19,3.7,3.5,.4,P['accent']); box('Drawer pull',0,y,7,.7,4,.5,P['trim'])
        for x in (-10,10):
            for y in (-9,9): disc('Caster',x,y,1.7,1,.8,P['dark'])
    elif kind=='pressure_tank':
        disc('Tank foot',0,0,15,1,1,P['dark']); disc('Tank vessel',0,0,13,2,4,P['shell'])
        disc('Inspection lid',0,0,7,6,.8,P['accent']); bolts(12,12,7)
        line('Feed pipe',(0,0),(15,0),2,7,P['trim']); disc('Gauge',0,5,2,7,.4,P['trim'])


def scientific(kind):
    if kind=='lab_bench':
        chassis(38,20); box('Ceramic worktop',0,0,34,16,3.5,.6,P['trim'])
        box('Sink well',10,0,8,10,4.2,.3,P['dark'],1)
        line('Faucet',(14,4),(10,4),1,4.6,P['shell'])
        for x in (-12,-7): disc('Sample dish',x,2,2.3,4.2,.4,P['accent'])
        box('Instrument mat',-7,-4,11,3,4.2,.2,P['shell'])
    elif kind=='microscope':
        box('Scope foot',0,-3,12,17,1,1.5,P['dark'],2)
        box('Stage',0,0,13,9,2.6,1,P['trim']); box('Slide',0,0,7,3,3.7,.2,P['accent'])
        box('Scope arm',0,6,4,10,4,2,P['shell'],1)
        disc('Optics turret',0,3,3,6,1,P['dark']); line('Eyepiece',(0,7),(0,10),2,7,P['trim'])
        for x in (-7,7): disc('Focus knob',x,2,1.6,4,1,P['dark'])
    elif kind=='centrifuge':
        chassis(26,26); disc('Rotor well',0,1,9,3.5,.4,P['dark'])
        disc('Rotor',0,1,7.5,4,.7,P['trim'])
        for j in range(8):
            a=j*math.tau/8; disc('Tube socket',5*math.cos(a),1+5*math.sin(a),1.2,4.8,.3,P['accent'])
        screen(0,-10,9,2)
    elif kind=='cryopod':
        chassis(22,38); box('Insulated surround',0,2,17,29,3.5,2,P['trim'],6)
        box('Observation window',0,2,12,22,5.6,.4,P['dark'],5)
        box('Inner canister',0,2,6,16,6,.3,P['accent'],3)
        for x in (-8,8): glow(box('Cold indicator',x,2,.75,20,5.7,.2,P['trim']))
        screen(0,-14,8,3)
    elif kind=='containment_chamber':
        chassis(34,34); disc('Containment ring',0,0,13,3.5,1,P['trim'])
        disc('Sealed window',0,0,10.8,4.6,.3,P['dark'])
        disc('Specimen cradle',0,0,5,5,.5,P['accent'])
        for x,y in [(-9,0),(9,0),(0,9),(0,-9)]: glow(disc('Field emitter',x,y,1.2,5,.4,P['trim']))
        box('Specimen',0,0,3,6,5.6,.5,P['shell'],1)
    elif kind=='server_rack':
        chassis(26,36)
        for y in (-12,-6,0,6,12):
            box('Compute tray',0,y,21,4.8,3.5,.6,P['dark']); grille(-2,y,13,3,4.2)
            glow(box('Status LED',8,y,1,1,4.4,.2,P['trim']),tuple(v*.6 for v in P['light']))
    elif kind=='analysis_console':
        chassis(36,22); screen(0,3,27,9)
        box('Keyboard recess',-3,-6,21,4,3.5,.3,P['dark'])
        for x in range(-11,7,3): box('Key group',x,-6,1.7,2,3.9,.3,P['trim'])
        disc('Trackball',12,-6,2.4,3.6,.6,P['accent'])
    elif kind=='sample_rack':
        chassis(28,20); box('Rack bed',0,0,23,15,3.5,.5,P['trim'])
        for x in (-8,-3,3,8):
            for y in (-4,4):
                disc('Tube collar',x,y,2.3,4.1,.5,P['dark']); disc('Sample cap',x,y,1.6,4.7,.6,P['accent'])
    elif kind=='medical_bed':
        chassis(22,38); box('Mattress',0,0,17,32,3.5,1.5,P['trim'],2)
        box('Headrest',0,11,14,7,5.1,.6,P['shell'],1.4)
        for y in (-6,3): box('Restraint',0,y,18,1.4,5.2,.4,P['accent'])
        for x in (-10,10): box('Bed rail',x,0,1,23,5,.8,P['shell'])
    elif kind=='fume_hood':
        chassis(36,26); box('Work cavity',0,0,29,18,3.5,.5,P['dark'])
        box('Sash handle',0,-7,28,1.4,5,.4,P['trim'])
        for x in (-8,0,8): disc('Reagent jar',x,1,2.5,4.2,1,P['accent'])
        glow(box('Task light',0,9,25,1,4.3,.4,P['trim']))
        grille(0,11,24,2,4)
    elif kind=='sterilizer':
        chassis(28,28); disc('Pressure door',0,2,10,3.5,1,P['trim'])
        disc('Door inset',0,2,7.5,4.6,.4,P['dark']); box('Lock handle',0,2,11,2,5.1,.8,P['shell'])
        screen(0,-10,12,2)
    elif kind=='gas_cylinders':
        box('Cylinder rack',0,0,27,23,1,1,P['dark'])
        for x in (-8,0,8):
            box('Cylinder',x,0,6,20,2,2.4,P['shell'],3)
            box('ID collar',x,5,6.2,2,4.5,.4,P['accent']); disc('Regulator',x,10,1.5,4.6,.6,P['trim'])
    elif kind=='robot_arm':
        disc('Manipulator base',-7,-5,9,1,1.5,P['dark']); disc('Turntable',-7,-5,6,2.6,1,P['trim'])
        line('Lower arm',(-7,-5),(4,5),5,4,P['shell']); disc('Elbow',4,5,3.5,4.8,1,P['accent'])
        line('Upper arm',(4,5),(11,-1),3,6,P['trim'])
        for y in (-3,1): line('Gripper',(11,-1),(15,y),1,7,P['dark'])
    elif kind=='specimen_freezer':
        chassis(26,34); box('Freezer lid',0,0,21,29,3.5,1,P['trim'])
        box('Door gasket',0,0,16,24,4.6,.2,P['dark']); box('Insulated panel',0,0,14,22,4.9,.4,P['shell'])
        box('Pull handle',8,0,1.2,8,5.4,.6,P['dark']); screen(0,9,8,3,z=5.5)


def specs(theme):
    entries=[]
    def add(name,category,size,arg=None): entries.append(dict(id=name,category=category,world_size=list(size),arg=name if arg is None else arg))
    for k in ('plain','plates','grate','drain','conduit','inset','worn','raised'): add('floor_'+k,'floor',(80,80),k)
    for mask in range(16): add('wall_%02d'%mask,'wall',(20,20),mask)
    for k in ('straight','elbow','tee','cross','valve'): add('pipe_'+k,'pipe',(20,20),k)
    for k in ('closed','open','sealed'): add('door_'+k,'door',(40,20),k)
    for k in ('strip','off','broken','beacon','flood'): add('light_'+k,'light',(40,20) if k in ('strip','off','broken') else (20,20),k)
    for k in ('lane','corner','chevron','boundary'): add('marking_'+k,'decal',(40,20) if k!='corner' else (20,20),k)
    kinds=(
        'generator compressor transformer control_console electrical_cabinet barrel_cluster pallet cargo_crate workbench vent_fan cable_reel pump tool_cart pressure_tank'
        if theme=='industrial' else
        'lab_bench microscope centrifuge cryopod containment_chamber server_rack analysis_console sample_rack medical_bed fume_hood sterilizer gas_cylinders robot_arm specimen_freezer')
    for k in kinds.split(): add(k,'prop',(40,40))
    return entries


def pass_material(name,mode,rgb=(0,0,0)):
    m=bpy.data.materials.new(name); m.use_nodes=True
    n=m.node_tree.nodes; n.clear(); link=m.node_tree.links
    out=n.new('ShaderNodeOutputMaterial'); em=n.new('ShaderNodeEmission')
    if mode=='normal':
        geo=n.new('ShaderNodeNewGeometry'); mul=n.new('ShaderNodeVectorMath'); mul.operation='MULTIPLY'
        # OpenGL tangent convention: +Y points UP on the sprite (Blender world +Y).
        mul.inputs[1].default_value=(.5,.5,.5)
        add=n.new('ShaderNodeVectorMath'); add.operation='ADD'; add.inputs[1].default_value=(.5,.5,.5)
        link.new(geo.outputs['Normal'],mul.inputs[0]); link.new(mul.outputs[0],add.inputs[0]); link.new(add.outputs[0],em.inputs[0])
    else: em.inputs[0].default_value=(*[c.linear(v) for v in rgb],1)
    link.new(em.outputs[0],out.inputs[0]); return m


def render_asset(entry,theme,collection,index):
    name=entry['id']; w,h=entry['world_size']; category=entry['category']
    global EMITTERS; EMITTERS={}
    before=set(bpy.context.scene.objects)
    arg=entry['arg']
    if category=='prop': (industrial if theme=='industrial' else scientific)(arg)
    else: {'floor':floor,'wall':wall,'pipe':pipe,'door':door,'light':fixture,'decal':decal}[category](arg)
    objects=[o for o in bpy.context.scene.objects if o not in before]
    if category=='decal':
        for ob in objects:
            for modifier in list(ob.modifiers): ob.modifiers.remove(modifier)
    asset=bpy.data.collections.new(name); collection.children.link(asset)
    asset.asset_mark(); asset.asset_data.description=f'{theme} {category}; {w} x {h} world pixels; regenerate with render_tilesets.py'
    for ob in objects:
        for col in list(ob.users_collection): col.objects.unlink(ob)
        asset.objects.link(ob)
    # Isolate collection: all preceding assets remain hidden until library assembly.
    s=bpy.context.scene
    c.tileset_camera(w,h)
    normal=pass_material('Normal data','normal'); black=pass_material('No emission','emission')
    em_mats={key:pass_material('Emission '+key,'emission',value) for key,value in EMITTERS.items()}
    if category=='floor':
        # Floors need extremely quiet color detail. Use neutral albedo in the
        # beauty pass; their geometry still supplies the runtime normal map.
        for ob in objects:
            for mat in ob.data.materials:
                nodes=mat.node_tree.nodes; out=next(n for n in nodes if n.type=='OUTPUT_MATERIAL')
                em=nodes.new('ShaderNodeEmission'); em.inputs[0].default_value=mat.diffuse_color
                mat.node_tree.links.new(em.outputs[0],out.inputs[0])
    originals={ob.name:list(ob.data.materials) for ob in objects}
    paths={}
    for mode in ('albedo','normal','emission'):
        if mode!='albedo':
            for ob in objects:
                ob.data.materials.clear()
                for mat in originals[ob.name]: ob.data.materials.append(normal if mode=='normal' else em_mats.get(mat.name,black))
        target=OUT/theme/mode/(name+'.png'); target.parent.mkdir(parents=True,exist_ok=True)
        c.tileset_render(target,mode)
        paths[mode]=f'{theme}/{mode}/{name}.png'
    for ob in objects:
        ob.data.materials.clear()
        for mat in originals[ob.name]: ob.data.materials.append(mat)
    asset.hide_render=True
    # Sources are laid out on a generous regular grid for the Blender asset library.
    gx=(index%8)*100; gy=-(index//8)*100
    asset.instance_offset=(gx,gy,0)
    for ob in objects: ob.location.x+=gx; ob.location.y+=gy
    result={k:v for k,v in entry.items() if k!='arg'}
    result.update(theme=theme,texture_size=[w*4,h*4],pivot=[w/2,h/2],maps=paths,
                  alpha='straight',source='render_tilesets.py',blender_version='4.5.3',seed=c.SEED,
                  tileable=category=='floor',rotation_degrees=[0,90,180,270],
                  placement='decoration' if category in ('floor','decal','pipe','light') else 'requires matching map collision',
                  emits_light=bool(EMITTERS))
    if category=='wall':
        result['connection_mask']=arg
        result['suggested_occluder_rects']=[[3,3,14,14]]+[
            rect for bit,rect in ((1,[3,0,14,10]),(2,[10,3,10,14]),(4,[3,10,14,10]),(8,[0,3,10,14])) if arg & bit]
    elif category=='pipe': result['connection_mask']={'straight':5,'elbow':3,'tee':7,'cross':15,'valve':5}[arg]
    elif category=='door': result['suggested_occluder_rects']=[] if arg=='open' else [[4,4,32,12]]
    if category=='light':
        result['light_anchor']=[w/2,h/2]; result['suggested_light']={'color':P['light'],'radius_world':140 if arg!='beacon' else 80,'enabled':bool(EMITTERS)}
        result['state_group']='strip' if arg in ('strip','off','broken') else arg
    print('TILESET_RENDERED',theme,name,flush=True)
    return result


def main():
    global P
    only=os.environ.get('COG_TILESET_ONLY'); theme_filter=os.environ.get('COG_TILESET_THEME')
    for theme in THEMES:
        if theme_filter and theme_filter!=theme: continue
        P=PALETTES[theme]; c.setup(80,80,False)
        root=bpy.data.collections.new(theme.title()+' map kit'); bpy.context.scene.collection.children.link(root)
        entries=[]
        for i,entry in enumerate(specs(theme)):
            if only and only!=entry['id']: continue
            entries.append(render_asset(entry,theme,root,i))
        OUT.mkdir(parents=True,exist_ok=True)
        manifest=OUT/(theme+'.json')
        if only and manifest.exists():
            old=json.loads(manifest.read_text())['assets']; entries=[e for e in old if e['id']!=only]+entries
        manifest.write_text(json.dumps({'schema':1,'theme':theme,'cell_world':20,'texture_scale':4,'normal_convention':'OpenGL +Y up; linear data',
            'emission_usage':'Add emission.rgb * emission.a after diffuse lighting; no baked light spill.',
            'assets':entries},indent=2)+'\n')
        if not only and 'COG_TILESET_OUTPUT' not in os.environ:
            for col in root.children: col.hide_render=False
            c.tileset_camera(800,700); bpy.context.scene.camera.location.x=350; bpy.context.scene.camera.location.y=-300
            for area in bpy.context.screen.areas:
                if area.type=='VIEW_3D':
                    area.spaces.active.region_3d.view_distance=650
                    area.spaces.active.region_3d.view_location=(350,-300,0)
            dest=ROOT/'blend'/f'{theme}_tileset.blend'; dest.parent.mkdir(exist_ok=True)
            bpy.context.scene['tileset_manifest']=f'assets/tilesets/{theme}.json'
            bpy.context.preferences.filepaths.save_version=0
            bpy.ops.wm.save_as_mainfile(filepath=str(dest),compress=True)
    print('TILESETS_COMPLETE',flush=True)

if __name__=='__main__': main()
