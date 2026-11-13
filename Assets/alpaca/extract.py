# The alpaca of the Gravity Load icon without its load arrow.
# Run with a Python that has Pillow: python3 Assets/alpaca/extract.py (writes the files beside it).
# Gravity Load's own pixels everywhere but the band x 7-14 the arrow covers; that band redrawn by hand
# in the icon's own colours (black outline, soft black edge, brown shading, orange).
import os
from PIL import Image, ImageDraw, ImageFilter
R=os.path.join(os.path.dirname(os.path.abspath(__file__)),"../../Alpaca4d.Gh/Resources/")
OUT=os.path.dirname(os.path.abspath(__file__))+"/"
g=Image.open(R+"Gravity Load (Alpaca4d).png").convert("RGBA")
C={'.':(0,0,0,0),'K':(0,0,0,255),'k':(0,0,0,150),'b':(97,55,23,254),'o':(252,142,57,252)}
BAND_X=7
BAND={  # rows 10-23, x 7..14
10:"....kkKK",
11:"kkkKKKoo",
12:"KKbooooo",
13:"oooooooo",
14:"oooooooo",
15:"oooooooo",
16:"oooooooo",
17:"oooooooo",
18:"oKKKKKbo",
19:"K.....Kb",
20:"K.....Ko",
21:"k.....Kb",
22:"k.....KK",
23:"......kk",
}
col=g.copy()
for y in range(0,10):                       # the arrow above the back
    for x in range(8,13): col.putpixel((x,y),C['.'])
for y,row in BAND.items():
    for i,ch in enumerate(row): col.putpixel((BAND_X+i,y),C[ch])
# keep the frame rule of the new icons in mind: Gravity Load's alpaca touches it (row 23, column 23)
sil=Image.merge("RGBA",(Image.new("L",(24,24),0),)*3+(col.getchannel("A"),))
col.save(OUT+"gravity-alpaca.png"); sil.save(OUT+"gravity-alpaca-silhouette.png")
A=col.getchannel("A")
big=A.resize((240,240),Image.BICUBIC).filter(ImageFilter.GaussianBlur(2.0)).point(lambda v:255 if v>=118 else 0)
big_im=Image.merge("RGBA",(Image.new("L",(240,240),0),)*3+(big,)); big_im.save(OUT+"gravity-alpaca-silhouette-240.png")
back=big.resize((24,24),Image.BOX)
print("240 px mask boxed back to 24: mean alpha error",round(sum(abs(back.getpixel((x,y))-A.getpixel((x,y))) for y in range(24) for x in range(24))/576,1))
print("ink on the frame (rows 0/23, cols 0/23):",sum(1 for i in range(24) for (x,y) in ((i,0),(i,23),(0,i),(23,i)) if A.getpixel((x,y))>0))
k=16; pad=24
items=[("Gravity Load now",g),("original colours, no arrow",col),("silhouette, 24 px",sil),("240 px mask, to scale from",big_im),("240 px mask boxed to 24",Image.merge("RGBA",(Image.new("L",(24,24),0),)*3+(back,)))]
sh=Image.new("RGBA",(len(items)*(24*k+pad)+pad,24*k+120),(255,255,255,255)); d=ImageDraw.Draw(sh)
for i,(lab,im) in enumerate(items):
    x=pad+i*(24*k+pad)
    b=im.resize((24*k,24*k),Image.NEAREST if im.size==(24,24) else Image.LANCZOS)
    bg=Image.new("RGBA",b.size,(255,255,255,255)); bg.alpha_composite(b); dd=ImageDraw.Draw(bg)
    if im.size==(24,24):
        for j in range(25):
            dd.line([(j*k,0),(j*k,24*k)],fill=(218,218,228,255)); dd.line([(0,j*k),(24*k,j*k)],fill=(218,218,228,255))
    sh.alpha_composite(bg,(x,40)); d.text((x,14),lab,fill=(0,0,0,255))
    s1=im if im.size==(24,24) else im.resize((24,24),Image.BOX)
    sh.alpha_composite(s1,(x,24*k+56)); sh.alpha_composite(s1.resize((48,48),Image.NEAREST),(x+40,24*k+56))
sh.save(OUT+"preview.png")
