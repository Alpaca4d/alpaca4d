# base_alpaca_logo.png: the Gravity Load alpaca (gravity-alpaca.png) with its orange taken out, so only
# the black outline is left. base_alpaca_logo-colourful.png: that outline filled with the colours of the
# Alpaca4d logo (Assets/Logo.png), mapped onto this shape by a thin-plate spline through 15 matching points.
# Run with a Python that has Pillow and numpy: python3 Assets/alpaca/colourful.py
import os
import numpy as np, collections
from PIL import Image, ImageDraw
A=os.path.dirname(os.path.abspath(__file__))+"/"
grav=Image.open(A+"gravity-alpaca.png").convert("RGBA")
logo=Image.open(os.path.join(A,"..","Logo.png")).convert("RGBA")
LA=np.asarray(logo).astype(int)
O=np.array([252,142,57.])
# 1. unmix each pixel into black + orange: t = share of orange
T=np.zeros((24,24)); AL=np.zeros((24,24))
for y in range(24):
    for x in range(24):
        r,g,b,a=grav.getpixel((x,y)); AL[y,x]=a
        if a==0: continue
        c=np.array([r,g,b],float)
        t=0.0 if r-b<40 else float(np.clip(c@O/(O@O),0,1))
        T[y,x]=t
base=Image.new("RGBA",(24,24),(0,0,0,0))
for y in range(24):
    for x in range(24):
        a=int(round(AL[y,x]*(1-T[y,x])))
        if a>0: base.putpixel((x,y),(0,0,0,a))
base.save(A+"base_alpaca_logo.png")
# 2. thin-plate spline: Gravity alpaca (24 px units) -> logo (24 px units)
P=np.array([(17.5,1.5),(23,5.5),(16.5,4.0),(21,8),(14.5,10.5),(20.5,14),(4,12.5),(1,15),(10.5,18.5),(15,22.5),(13.5,19.5),(6,22.5),(3.5,20),(17.5,18),(19,3)])
Q=np.array([(16.7,1.9),(19.8,5.0),(15.2,3.8),(18,6.5),(13.5,10.8),(18.1,14.7),(6.1,12),(3.6,15.8),(10.5,17),(14.6,22),(13,19.5),(7.6,22.2),(5.5,20),(16,19.5),(17.8,3.2)])
def U(r): 
    with np.errstate(divide='ignore',invalid='ignore'):
        v=r*r*np.log(r*r); return np.nan_to_num(v)
n=len(P); K=U(np.linalg.norm(P[:,None]-P[None],axis=2))
Pm=np.hstack([np.ones((n,1)),P]); L=np.zeros((n+3,n+3)); L[:n,:n]=K; L[:n,n:]=Pm; L[n:,:n]=Pm.T
W=np.linalg.solve(L,np.vstack([Q,np.zeros((3,2))]))
def warp(pts):
    k=U(np.linalg.norm(pts[:,None]-P[None],axis=2)); return k@W[:n]+np.hstack([np.ones((len(pts),1)),pts])@W[n:]
# the logo's facet colours: frequent colours, opaque, not the dark facet lines
flat=LA.reshape(-1,4); op=flat[flat[:,3]>250][:,:3]
cnt=collections.Counter(map(tuple,op[::7]))
pal=[c for c,k in cnt.most_common(60) if k>300 and max(c)>90]
pal=np.array(pal,float)
def nearest(c): return pal[np.argmin(((pal-c)**2).sum(1))]
fill={}
s=6
for y in range(24):
    for x in range(24):
        if T[y,x]<=0: continue
        sub=np.array([(x+(i+.5)/s,y+(j+.5)/s) for j in range(s) for i in range(s)])
        q=warp(sub)*1600/24
        votes=collections.Counter()
        for qx,qy in q:
            ix,iy=int(np.clip(qx,0,1599)),int(np.clip(qy,0,1599))
            r,g,b,a=LA[iy,ix]
            if a<200 or max(r,g,b)<90: continue          # outside, or a facet line
            votes[tuple(nearest(np.array([r,g,b])))]+=1
        if votes: fill[x,y]=votes.most_common(1)[0][0]
        else: fill[x,y]=(235,108,63)
col=Image.new("RGBA",(24,24),(0,0,0,0))
for y in range(24):
    for x in range(24):
        a=AL[y,x]
        if a==0: continue
        t=T[y,x]; F=np.array(fill.get((x,y),(0,0,0)),float)
        c=(1-t)*np.zeros(3)+t*F
        col.putpixel((x,y),tuple(int(round(v)) for v in c)+(int(a),))
col.save(A+"base_alpaca_logo-colourful.png")
print("palette",len(pal)); print(collections.Counter(fill.values()).most_common(10))
