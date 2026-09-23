"""Catalogue-driven inventory renders; 8 texture-pixel safe inset."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import cog_common as c
from render_weapons import weapon
from render_actors import armour
from render_props import objective, bag

def attachment(i):
    if i in [301,302,303]:
        c.box('Mount',0,0,9 if i==303 else 5,3.8,1,.5,c.DARK,.5)
        c.box('Optic housing',0,0,12 if i==303 else 4,2.8 if i!=302 else 4.5,1.5,1,c.METAL,.7)
        for x in ([-4.8,4.8] if i==303 else [-1,1]):
            c.box('Glass bezel',x,0,1,3.2,2.5,.4,c.EDGE,.3)
        c.box('Glass',3.8 if i==303 else 0,0,1.1,1.8,3,.1,(.25,.29,.30),.25)
    elif i in [311,312,313,323]:
        pts=[(-2,-5),(1,-5),(2,4),(-1.5,4)] if i!=313 else [(-4,-3),(4,-3),(4,1),(-4,4)]
        c.poly('Polymer grip',pts,1,1,c.POLY,.4)
        for j in range(3): c.box('Grip groove',0,-2+j*1.6,2.9,.35,2.1,.1,c.DARK,.1)
    elif i in [321,322]:
        c.box('Rail lamp',0,0,10 if i==322 else 6,3.8,1,1,c.METAL,.8)
        c.box('Front lens',4 if i==322 else 2,0,1.2,4.6,1.5,1,c.EDGE,.5)
        c.box('Mount clamp',-1,0,2,5,1.7,.3,c.DARK,.3)
    elif i==331:
        c.poly('Extended curved magazine',[(-2,8),(2,8),(2,0),(3.6,-7),(.3,-8),(-1.4,-1)],1,1,c.METAL,.5)
        c.box('Witness slot',0,3,.6,6,2.1,.1,c.DARK,.1)
    elif i==332:
        c.oval('Drum magazine',0,-1,5,5,1,1,c.DARK)
        c.oval('Drum inset',0,-1,3.4,3.4,2.1,.3,c.METAL)
        c.box('Feed tower',0,4,3.2,5,1,1,c.METAL,.4)
    elif i==333:
        c.box('Magazine base',0,0,5,7,1,1,c.METAL,.5)
        c.box('Pull loop',0,-4,4,2.4,1,.7,c.CANVAS,.7)
    elif i in [341,342,343]:
        for j in range(3):
            x=-3+j*3
            c.box('Case',x,-1,1.9,5.8,1,.7,c.CANVAS,.3)
            if i==341: c.oval('Heavy round nose',x,2.5,.85,2.2,1,.7,c.METAL)
            elif i==342:
                c.box('Hollow bullet',x,2.4,1.8,2.2,1,.7,c.METAL,.3)
                c.oval('Open cup',x,3.2,.56,.4,1.8,.15,c.DARK)
            else: c.poly('Penetrator',[(x-.8,1.7),(x+.8,1.7),(x,5.1)],1,.7,c.EDGE,.1)
            c.box('Rim',x,-3.7,2.2,.6,1.8,.15,c.EDGE,.1)
    elif i in [351,352]:
        c.poly('Shoulder stock',[(-7,-3),(-5,-3),(4,-1),(7,-1),(7,1),(-5,3),(-7,3)],1,1,c.POLY,.4)
        c.box('Butt pad',-6,0,1.5,6.4,1.8,.3,c.DARK,.4)
        if i==352: c.box('Cheek riser',-2,1,7,2,2,.5,c.CANVAS,.5)
    else: raise ValueError(i)

def apparel(i):
    if i in [401,402]:
        c.oval('Headwear shell',0,0,6.5,5.5,1,1.2,c.OLIVE)
        if i==401: c.oval('Cap brim',5,0,4,4,1,.5,c.CANVAS)
        else:
            for s in [-1,1]: c.box('Helmet side rail',0,s*4.6,7,1,2.3,.3,c.DARK,.3)
            c.box('Crown band',0,0,1.2,9,2.4,.2,c.CANVAS,.3)
    elif i in [501,502,503]:
        if i==501: bag(1)
        else:
            c.box('Backpack',0,0,12,16 if i==503 else 12,1,1.5,c.CANVAS,2.4)
            c.box('Top flap',0,4,11,5,2.6,.4,c.OLIVE,1)
            for s in [-1,1]:
                c.box('Side pouch',s*6,0,3,7,1.4,.8,c.OLIVE,1)
                c.box('Strap',s*3,0,1,13,3,.2,c.DARK,.2)
            c.box('Front pocket',0,-3,5,4.5,3,.5,c.CANVAS,.7)
    elif i in [601,602]:
        for s in [-1,1]:
            c.oval('Sole',s*3.4,0,2.9,5.5,1,.6,c.DARK)
            c.oval('Boot upper',s*3.4,.4,2.35,4.7,1.7,1,c.POLY if i==602 else c.CANVAS)
            c.box('Ankle opening',s*3.4,2.8,3,2.6,2.8,.2,c.DARK,.8)
            for j in range(3): c.box('Lace',s*3.4,-1+j*.8,2.5,.3,2.9,.1,c.EDGE,.1)
    elif i in [701,702]:
        c.poly('Shirt silhouette',[(-5,-6),(5,-6),(5,2),(9,0),(10,4),(5,7),(2,7),(0,5),(-2,7),(-5,7),(-10,4),(-9,0),(-5,2)],1,.9,c.OLIVE if i==701 else c.CANVAS,.4)
        c.box('Placket',0,0,.6,10,2,.1,c.DARK,.1)
        for s in [-1,1]: c.box('Chest pocket',s*2.8,1.5,2.8,3,2,.25,c.CANVAS,.3)
    elif i in [901,902,903]:
        c.poly('Trousers',[(-5,8),(5,8),(5,-8),(1,-8),(0,1),(-1,-8),(-5,-8)],1,1,c.CANVAS if i==901 else c.OLIVE,.35)
        c.box('Waistband',0,6.8,10,1.3,2.1,.3,c.DARK,.3)
        if i!=901:
            for s in [-1,1]: c.box('Cargo pocket',s*3.7,2,3,3.5,2.2,.3,c.CANVAS,.4)
        if i==903:
            for s in [-1,1]: c.box('Knee and shin pad',s*3,-3.5,3.3,6.6,2.4,.5,c.POLY,.7)
    elif i in [801,802]:
        for s in [-1,1]:
            x=s*3
            c.box('Glove or guard',x,0,4,7 if i==802 else 5,1,.8,c.CANVAS if i==801 else c.POLY,1)
            if i==801:
                for j in range(3): c.box('Finger',x-1+j,.0-3.1, .75,2.3,1,.6,c.CANVAS,.3)
                c.limb('Thumb',(x+s*1.6,0),(x+s*2.4,-1.8),1.3,1,c.CANVAS)
            else: c.box('Hard plate',x,0,2.8,5,2,.3,c.METAL,.5)
    else: raise ValueError(i)

def main():
    for i,name,w,h in c.catalogue()+[(-1,'unknown',1,1)]:
        c.setup(w*20,h*20)
        if 100<=i<=112: weapon(i-100)
        elif 201<=i<=203: armour(i-201,True)
        elif 300<=i<400: attachment(i)
        elif i==900: objective()
        elif i==-1:
            c.box('Unidentified wrapped parcel',0,0,10,10,1,1,c.CANVAS,1.4)
            for x in [-2,2]: c.box('Binding',x,0,.6,10,2.1,.2,c.DARK,.2)
        else: apparel(i)
        c.fit(w*20,h*20,3.25)
        c.render(f'items/item_{i if i!=-1 else "unknown"}.png',w*20,h*20,Path(__file__).name,False,'item')
if __name__=='__main__': main()
