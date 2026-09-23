"""Fast bpy configuration check; does not render or write production assets."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c
import bpy
for w,h in [(56,56),(20,40),(80,80)]:
    s=c.setup(w,h)
    assert s.render.engine=='BLENDER_EEVEE_NEXT'
    assert s.render.resolution_x==w*4 and s.render.resolution_y==h*4
    assert s.camera.data.ortho_scale==w
    assert s.render.film_transparent
    assert s.view_settings.view_transform=='Standard'
    assert bpy.context.view_layer.freestyle_settings.linesets[0].linestyle is not None
    c.box('Configuration proof',0,0,8,8,color=c.BODY)
print('PASS: pinned Blender, clean-scene reset, EEVEE, dimensions, camera, alpha, Standard and Freestyle initialization.')
