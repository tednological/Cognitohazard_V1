"""Thirteen neutral weapons; shared geometry for held and inventory renders."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c
NAMES=['glock','mp7','ak47','remington','saw','welrod','vss','photon','arc_lance','vulcan','tesla','frag','awm']
LENGTHS=[13,18,23,24,29,14,24,23,30,29,24,5,30]

def grenade(x=0,y=0):
    c.oval('Olive grenade body',x,y,3.3,3.7,1,.8,c.OLIVE)
    c.box('Lever',x+.5,y,1,6.4,2,.3,c.METAL,.2)
    c.box('Fuse cap',x,y+3.1,2.4,1.5,1.8,.4,c.DARK,.3)

def weapon(idx,held=False):
    if idx==11:
        grenade(9,-2.5) if held else grenade()
        return
    L=LENGTHS[idx]; back=22-L if held else -L/2; tip=22 if held else L/2
    mid=(back+tip)/2
    heavy=idx in (4,8,9); width=3.8 if heavy else 2.5
    # Every real barrel ends at +22; receiver lengths grow backwards from it.
    receiver_end=tip-(L*.25 if idx not in (0,5) else 1.7)
    c.box('Barrel', (receiver_end+tip)/2,0,tip-receiver_end,.95 if not heavy else 1.5,1,.8,c.METAL,.1)
    if idx in (5,6):
        c.box('Integral suppressor',(receiver_end+tip)/2,0,tip-receiver_end,2.2,1,.9,c.DARK,.5)
    c.box('Receiver',(back+receiver_end)/2+.9,0,receiver_end-back-1.8,width,1,1,c.METAL,.45)
    if idx in (0,5):
        c.poly('Pistol grip',[(back+.8,-.5),(back+3.3,-.5),(back+2.6,-4.5),(back+.2,-4.5)],.9,.65,c.POLY,.15)
        c.box('Slide',mid+.1,0,L*.66,1.7,2,.3,c.EDGE,.2)
    else:
        stock_end=back+L*.23
        c.poly('Stock',[(back,-2.3),(back+1,-2.3),(stock_end,-.8),(stock_end,.8),(back+1,2.3),(back,2.3)],.9,.8,c.WOOD if idx in (2,3) else c.POLY,.15)
        c.box('Butt pad',back+.35,0,.7,4.7,1,.8,c.DARK,.15)
        if not held:
            c.poly('Grip',[(back+L*.36,-.8),(back+L*.45,-.8),(back+L*.43,-4.7),(back+L*.35,-4.5)],.9,.7,c.POLY,.15)
        c.box('Handguard',tip-L*.31,0,L*.2,width+.4,1.5,.7,c.WOOD if idx in (2,3) else c.POLY,.4)
        if idx in (2,6):
            c.poly('Curved magazine',[(mid-1,-1),(mid+2,-1),(mid+2.7,-4.8),(mid+4,-6.5),(mid+1.2,-6.8),(mid-.3,-4.7)],.8,.6,c.DARK,.2)
        if idx==1:
            c.box('Compact magazine',mid-1,-3,2.1,4.5,.8,.8,c.DARK,.25)
        if idx==4:
            c.box('Belt box',mid,-3.5,6.6,5.8,1,.8,c.OLIVE,.6)
            for j in range(4): c.box('Feed round',mid+j*.85-1.3,1.8,.55,2.1,2.1,.2,c.CANVAS,.15)
        if idx==9:
            for y in [-1.7,0,1.7]: c.box('Rotary barrel',(receiver_end+tip)/2,y,tip-receiver_end,1,1,1,c.EDGE,.15)
            for x in [tip-1.0,tip-4.5]: c.box('Barrel collar',x,0,.9,5.1,2,.3,c.DARK,.2)
            c.oval('Rotary drum',mid,-2,3.6,4,1,1,c.DARK)
        if idx in (6,12):
            c.box('Optic',mid+.5,0,6.0,1.7,2.7,.7,c.DARK,.6)
            c.box('Objective bell',mid+3,0,1.6,2.5,2.8,.7,c.METAL,.6)
        if idx in (7,8,10):
            c.box('Energy cell',mid,0,3.0,1.3,2.3,.25,c.CYAN,.3)
            count=3 if idx==7 else 5
            for j in range(count):
                c.box('Emitter rib',receiver_end-1+j*.85,0,.45,width+.8,2.2,.25,c.EDGE,.15)
        if idx==3:
            c.box('Pump slide',tip-6,0,5,3.1,1.8,.55,c.WOOD,.45)
        if idx in (4,12):
            for side in [-1,1]: c.limb('Folded bipod',(tip-5,side),(tip-2.5,side*2.5),.55,1,c.DARK)
    c.box('Front sight',tip-.7,0,.6,1.1,2.5,.2,c.DARK,.1)

def arms(idx):
    # Hands remain in the body circle; the short pistol reaches forward from them.
    grip=8.2 if idx in (0,5,11) else 5.7
    c.limb('Left sleeve',(-.7,4.3),(4.0,4.2),3.1,3,c.BODY)
    c.limb('Left forearm',(4.0,4.2),(grip,1.7),2.5,3,c.BODY)
    c.limb('Right sleeve',(-.7,-4.3),(3.2,-4.5),3.1,3,c.BODY)
    c.limb('Right forearm',(3.2,-4.5),(grip,-1.6),2.5,3,c.BODY)

def main():
    for idx,name in enumerate(NAMES):
        c.setup(56,56); weapon(idx,True)
        # Untinted gloves live in the weapon layer.
        for y in [-1.5,1.5]: c.oval('Glove',8 if idx in (0,5,11) else 5.6,y,1.05,.9,4,.4,c.DARK)
        c.render(f'weapons/held_{name}.png',56,56,Path(__file__).name,False,'held',{'muzzle':[50,28]})
        c.setup(56,56); arms(idx)
        c.render(f'weapons/held_{name}_arms.png',56,56,Path(__file__).name,True,'arms',body_circle=True)
if __name__=='__main__': main()
