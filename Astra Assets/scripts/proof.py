import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c
from render_actors import torso
c.setup(56,56); torso(); c.render('actors/actor_torso.png',56,56,'render_actors.py',True,'torso',body_circle=True)
