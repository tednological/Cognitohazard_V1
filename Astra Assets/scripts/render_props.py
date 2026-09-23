"""Floor props and double-door leaves, authored horizontally."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c
from render_weapons import grenade

def chest(opened=False):
    warm=(.60,.51,.38)
    c.box('Crate',0,0,15,10.8,1,1.1,warm,1)
    if opened:
        c.box('Empty cavity',0,-1,11.9,6.4,2.2,.1,c.DARK,.5)
        c.box('Raised lid',0,4.4,14.7,2.4,2.3,.8,(.43,.38,.31),.6)
    else:
        c.box('Lid inset',0,0,12.4,8.3,2.2,.3,(.67,.59,.45),.6)
        for x in [-4.6,4.6]: c.box('Strap',x,0,1.2,10.8,2.6,.2,c.WOOD,.2)
        c.box('Latch',0,-4.5,2.2,1.3,2.8,.3,c.EDGE,.3)

def objective(empty=False):
    if empty:
        for side in [-1,1]: c.box('Dock rail',0,side*4.2,14,1.1,1,.4,c.METAL,.3)
        c.box('Dock latch',-6,0,1,8,1,.3,c.DARK,.2)
        return
    c.box('Hard case',0,0,15,9.8,1,1.3,c.DARK,1.6)
    c.box('Top shell',0,0,12.5,7.6,2.4,.4,c.METAL,.9)
    c.box('Recessed handle',0,5.2,5.3,1.1,1,.6,c.EDGE,.3)
    c.box('Objective seal',0,0,1,7.1,2.9,.2,c.CYAN,.2)
    for x in [-5.7,5.7]: c.box('Case latch',x,-4,1.5,1.4,2.6,.3,c.EDGE,.2)

def cache(taken=False):
    c.poly('Document tray',[(-5,-4),(5,-4),(5,3),(2.8,5),(-5,5)],1,.7,c.WOOD,.2)
    c.box('Interior',0,0,7.8,6.5,1.8,.1,c.DARK,.3)
    if not taken:
        for j in range(3): c.box('File card',-.45+j*.4,-.6+j*.6,6.6,5,2+j*.18,.1,(.74-j*.04,.71-j*.04,.60-j*.04),.2)
        c.box('File tab',2.1,3.4,2,1.1,2.7,.15,c.CANVAS,.2)

def bag(size=0):
    col=(.46,.49,.52) if size==0 else c.CANVAS
    c.oval('Soft duffel',0,0,8.1,5.9,1,1.3,col)
    c.box('Flap',0,.8,12,6.9,2.3,.4,col,2)
    for x in [-4.3,4.3]:
        c.box('Canvas strap',x,0,1.2,10.8,2.8,.2,c.DARK,.25)
        c.box('Buckle',x,-2,1.7,1.4,3,.2,c.EDGE,.25)
    c.box('Carry handle',0,5.5,4.3,1,2,.4,c.DARK,.4)

def main():
    specs=[('chest_full',20,16,lambda:chest()),('chest_open',20,16,lambda:chest(True)),
           ('objective_case',20,16,lambda:objective()),('objective_site_empty',20,16,lambda:objective(True)),
           ('cache_full',16,16,lambda:cache()),('cache_taken',16,16,lambda:cache(True)),
           ('ground_bag',22,18,bag),('grenade',12,12,grenade)]
    for name,w,h,fn in specs:
        c.setup(w,h); fn(); c.render(f'props/{name}.png',w,h,Path(__file__).name)
    for cells,w in [(2,34),(3,54)]:
        c.setup(w,10)
        c.box('Double door leaf',0,0,w,10,1,1,c.WOOD,0)
        for side in [-1,1]:
            c.box('Door panel',side*w/4,0,w/2-3,7,2,.25,(.52,.43,.33),.45)
            c.box('Handle',side*1.3,0,.65,2.8,2.4,.3,c.EDGE,.15)
        c.box('Centre seam',0,0,.6,10,2.5,.1,c.DARK,0)
        c.render(f'props/door_leaf_{cells}.png',w,10,Path(__file__).name,False,'door',{'hinge':[0,5]})
    c.setup(3,20)
    c.box('Steel jamb',0,0,3,20,1,1,(.24,.27,.33),0)
    c.render('props/door_jamb.png',3,20,Path(__file__).name,False,'door')
if __name__=='__main__': main()
