"""Shared actor, walk cycle, armour and unarmed prone poses."""
import sys, math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c

def torso():
    c.box('Jacket shoulder silhouette',-1.5,0,8.4,11.2,2,1.2,c.BODY,2)
    c.box('Rear jacket seam',-4.6,0,1,8.3,3.25,.12,c.FOLD,.3)

def head():
    c.oval('Head facing right',2.2,0,5.7,5.7,5,1.2,c.BODY)
    c.box('Forward brow',6.1,0,1.5,5.4,6.2,.3,(.84,)*3,.6)

def armour(tier,item=False):
    col=c.OLIVE if item else c.BODY
    fold=c.POLY if item else c.FOLD
    widths=[12.6,16.5,21.0]; width=widths[tier]
    c.box('Carrier shell',-1.5,0,9+1.1*tier,10.2+1.2*tier,3,1,col,1.3)
    for side in [-1,1]:
        c.box('Proud shoulder plate',-.6,side*(width/2-1.9),6+1.0*tier,3.8,3.3,1.1,col,1.2)
    if tier>=1:
        c.box('Spine plate',-3.5,0,2.2,8,4.1,.2,fold,.6)
    if tier==2:
        for side in [-1,1]: c.box('Heavy wing',-3.2,side*8.6,5,3,3.7,.6,fold,.6)


def main():
    source=Path(__file__).name
    for i in range(8):
        c.setup(56,56)
        phase=math.sin(i*math.tau/8)*3.1
        for side in [-1,1]:
            c.limb('Trouser leg',(-2,side*2.8),(-4+side*phase,side*4.4),3.5,1,c.BODY)
            c.box('Knee fold',-3+side*phase*.55,side*3.8,1,2.1,1.7,.1,c.FOLD,.3)
        c.render(f'actors/actor_legs_{i:02}.png',56,56,source,True,'legs',body_circle=True)
        c.setup(56,56)
        for side in [-1,1]:
            c.box('Boot',-4+side*phase,side*4.4,3.7,3.1,1.9,.8,c.DARK,.8)
        c.render(f'actors/actor_legs_{i:02}_detail.png',56,56,source,False,'legs_detail',body_circle=True)
    for name,fn,gray,layer in [('actor_torso',torso,True,'torso'),('actor_head',head,True,'head')]:
        c.setup(56,56); fn(); c.render(f'actors/{name}.png',56,56,source,gray,layer,body_circle=True)
    c.setup(56,56)
    for side in [-1,1]: c.box('Harness',-1.8,side*3.9,6.2,.7,3.6,.2,c.DARK,.2)
    c.box('Harness back buckle',-4,0,1.2,2.2,3.8,.2,c.METAL,.2)
    c.render('actors/actor_torso_detail.png',56,56,source,False,'torso_detail',body_circle=True)
    c.setup(56,56)
    c.box('Headset band',.9,0,1.0,9.9,6.6,.2,c.POLY,.2)
    for side in [-1,1]: c.box('Earpiece',1,side*4.9,2.5,1.5,6.8,.5,c.DARK,.5)
    c.render('actors/actor_head_detail.png',56,56,source,False,'head_detail',body_circle=True)
    for tier,name in enumerate(['light','medium','heavy']):
        c.setup(56,56); armour(tier)
        c.render(f'actors/armour_{name}.png',56,56,source,True,'armour')
    for i in range(4):
        c.setup(28,28)
        c.box('Prone torso',-1,0,12.2,8.3,1,.9,c.BODY,2)
        c.oval('Prone head',6,0,4.2,4.2,2,.9,c.BODY)
        c.limb('Left leg',(-5,2),(-9,3+i*.7),2.7,.6,c.FOLD)
        c.limb('Right leg',(-5,-2),(-8.8+i*.7,-5-i*.35),2.8,.6,c.BODY)
        c.limb('Left arm',(0,3),(3-i*2.0,7+i*.45),2.5,1,c.BODY)
        c.limb('Right arm',(0,-3),(4-i*1.4,-7+i*.6),2.5,1,c.BODY)
        c.render(f'actors/body_prone_{i}.png',28,28,source,True,'body_prone')
if __name__=='__main__': main()
