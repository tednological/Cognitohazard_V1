"""Headless entry point. Sources also run individually with Blender -b -P."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common
import render_actors, render_weapons, render_props, render_surfaces, render_items
for module in [render_actors,render_weapons,render_props,render_surfaces,render_items]:
    print('COG_STAGE',module.__name__,flush=True)
    module.main()
print('COG_COMPLETE',flush=True)
