#!/usr/bin/env python3
"""Sequential isolated Blender processes for Intel macOS EEVEE stability.
No image processing. Every PNG still comes from its class's bpy authoring script.
"""
import argparse, os, subprocess, sys, time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
NAMES=['glock','mp7','ak47','remington','saw','welrod','vss','photon','arc_lance','vulcan','tesla','frag','awm']

def jobs():
    # World readability proof first, then the rest of the full production set.
    actors=['actor_torso','actor_torso_detail','actor_head','actor_head_detail']
    actors += [f'actor_legs_{i:02}{s}' for i in range(8) for s in ['', '_detail']]
    actors += ['armour_'+n for n in ['light','medium','heavy']]+[f'body_prone_{i}' for i in range(4)]
    rows=[('render_actors.py',f'actors/{n}.png') for n in actors]
    rows += [('render_weapons.py',f'weapons/held_{n}{s}.png') for n in NAMES for s in ['', '_arms']]
    rows += [('render_props.py',f'props/{n}.png') for n in ['chest_full','chest_open','objective_case','objective_site_empty','cache_full','cache_taken','ground_bag','grenade','door_leaf_2','door_leaf_3','door_jamb']]
    rows += [('render_surfaces.py',f'surfaces/{n}.png') for n in ['floor_tile','wall_tile']]
    import re
    ids=re.findall(r'new\s+GearItem\(\s*(\d+)\s*,', (ROOT.parent/'cognitohazard-v-1/sim/GearCatalog.cs').read_text())
    rows += [('render_items.py',f'items/item_{i}.png') for i in ids+['unknown']]
    return rows

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--blender',required=True);ap.add_argument('--resume',action='store_true');ap.add_argument('--timeout',type=int,default=300)
    args=ap.parse_args(); out=Path(os.environ.get('COG_OUTPUT',str(ROOT.parent/'cognitohazard-v-1/assets')))
    logs=ROOT/'previews/render_logs';logs.mkdir(parents=True,exist_ok=True)
    rows=jobs()
    for i,(source,path) in enumerate(rows):
        if args.resume and (out/path).exists():
            print(f'SKIP {i+1}/{len(rows)} {path}',flush=True);continue
        env=os.environ.copy();env['COG_ONLY']=path;env['PYTHONUNBUFFERED']='1'
        log=logs/(Path(path).stem+'.log'); start=time.monotonic()
        print(f'START {i+1}/{len(rows)} {path}',flush=True)
        with log.open('w') as f:
            result=subprocess.run([args.blender,'-b','-t','4','--python-exit-code','1','-P',str(ROOT/'scripts'/source)],env=env,stdout=f,stderr=subprocess.STDOUT,timeout=args.timeout)
        if result.returncode or not (out/path).exists():
            print(log.read_text()[-4000:],flush=True);return 1
        print(f'DONE {i+1}/{len(rows)} {path} {time.monotonic()-start:.1f}s',flush=True)
    return 0
if __name__=='__main__':sys.exit(main())
