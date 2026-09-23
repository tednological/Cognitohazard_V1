"""Seamless bounded periodic concrete. No grid and no outlined tile edge."""
import sys, math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c
import bpy

def surface(size,wall=False):
    # Analytic periodic value field. One-pixel edge guard makes edge matching exact.
    # Keep linear-light values in float storage: 8-bit linear quantization at
    # these dark wall values exceeds the contrast budget after sRGB conversion.
    n=size*4; im=bpy.data.images.new('Periodic concrete',width=n,height=n,alpha=True,float_buffer=True)
    im.colorspace_settings.name='Non-Color'
    pixels=[]
    base=(.161,.180,.220) if wall else (.965,.965,.965)
    for y in range(n):
        for x in range(n):
            u=x/(n-1); v=y/(n-1)
            k=(.003 if wall else .010)*(math.sin(math.tau*(3*u+2*v))*.5+math.sin(math.tau*(7*u-5*v))*.3+math.cos(math.tau*(13*u+11*v))*.2)
            # Borders remain equal and continuous through a narrow fade, no frame.
            k*=min(1,x/4,(n-1-x)/4,y/4,(n-1-y)/4)
            pixels.extend([c.linear(min(1,max(0,t+k))) for t in base]+[1])
    im.pixels.foreach_set(pixels); im.pack()
    material=bpy.data.materials.new('Bounded concrete'); material.use_nodes=True
    nodes=material.node_tree.nodes; nodes.clear(); links=material.node_tree.links
    tex=nodes.new('ShaderNodeTexImage'); tex.image=im; tex.interpolation='Linear'; tex.extension='REPEAT'
    em=nodes.new('ShaderNodeEmission'); out=nodes.new('ShaderNodeOutputMaterial')
    links.new(tex.outputs['Color'],em.inputs['Color']); links.new(em.outputs[0],out.inputs[0])
    bpy.ops.mesh.primitive_plane_add(size=size+1,location=(0,0,0))
    ob=bpy.context.object; ob.data.materials.append(material)
    # UV centers at the exact camera canvas bounds, with plane outside the view.
    for loop in ob.data.uv_layers.active.data:
        loop.uv=( (loop.uv.x-.5)*(size+1)/size+.5, (loop.uv.y-.5)*(size+1)/size+.5 )


def main():
    for name,size,wall in [('floor_tile',80,False),('wall_tile',40,True)]:
        c.setup(size,size,False); surface(size,wall)
        c.render(f'surfaces/{name}.png',size,size,Path(__file__).name,not wall,'surface')
if __name__=='__main__': main()
