import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c
for i in range(3):
    c.setup(56,56)
    c.box('Probe',0,0,8.4,11.2,2,1,c.BODY if i!=1 else c.DARK,2)
    c.render(f'probe_{i}.png',56,56,Path(__file__).name,i!=1,'torso')
