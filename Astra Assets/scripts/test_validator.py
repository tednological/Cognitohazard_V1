"""Fault-injection checks on temporary copies; delivered PNGs are never edited."""
import copy, json, shutil, tempfile
from pathlib import Path
import numpy as np
from PIL import Image
import check_assets as v

def main():
    entries=json.loads((v.ASSETS/'manifest.json').read_text())
    assert not v.validate(entries), 'First validate the unmodified production batch'
    original=v.ASSETS
    with tempfile.TemporaryDirectory(prefix='cog-validator-') as td:
        fixture=Path(td)/'assets';shutil.copytree(original,fixture);v.ASSETS=fixture
        def bad_entry(path,edit,expected):
            bad=copy.deepcopy(entries);e=next(e for e in bad if e['path']==path);edit(e)
            errors=v.validate(bad)
            assert any(expected in e for e in errors), (expected,errors)
        def bad_png(path,edit,expected):
            p=fixture/path;raw=p.read_bytes();im=Image.open(p).convert('RGBA');edit(im).save(p)
            try:
                errors=v.validate(entries)
                assert any(expected in e for e in errors), (expected,errors)
            finally:p.write_bytes(raw)
        bad_entry('weapons/held_glock.png',lambda e:e['anchors'].update(muzzle=[49,28]),'muzzle must be exactly')
        bad_entry('actors/actor_head.png',lambda e:e.update(tinted=False),'tint flag disagrees')
        bad_entry('actors/actor_head.png',lambda e:e.update(world_size=[55,56]),'world size must')
        bad_png('actors/actor_head.png',lambda im:im.convert('RGB'),'must be RGBA')
        def mark(im,xy,color): im.putpixel(xy,color);return im
        bad_png('actors/actor_head.png',lambda im:mark(im,(112,112),(255,120,255,255)),'not greyscale')
        bad_png('actors/actor_head.png',lambda im:mark(im,(0,0),(240,240,240,255)),'body outside')
        bad_png('items/item_100.png',lambda im:mark(im,(4,4),(100,100,100,255)),'inset infringed')
        bad_png('items/item_100.png',lambda im:mark(im,(80,80),(230,60,50,255)),'reserved saturated hue')
        bad_png('surfaces/floor_tile.png',lambda im:mark(im,(0,10),(0,0,0,255)),'tile edge mismatch')
        bad_png('surfaces/floor_tile.png',lambda im:mark(im,(100,100),(80,80,80,255)),'contrast exceeds')
        missing=[e for e in entries if e['path']!='items/item_903.png']
        assert any('missing required' in e for e in v.validate(missing))
    v.ASSETS=original
    print('PASS: 11 fault-injection cases rejected; production renders unchanged.')
if __name__=='__main__':main()
