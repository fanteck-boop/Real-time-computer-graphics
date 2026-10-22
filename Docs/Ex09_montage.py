import sys
from PIL import Image
d=sys.argv[1]; names=sys.argv[2].split(","); out=sys.argv[3]; cols=int(sys.argv[4]) if len(sys.argv)>4 else 3
ims=[Image.open(f"{d}/{n}.png") for n in names]
w,h=ims[0].size; s=0.62; w2,h2=int(w*s),int(h*s)
rows=(len(ims)+cols-1)//cols
m=Image.new("RGB",(w2*cols,h2*rows))
for i,im in enumerate(ims): m.paste(im.resize((w2,h2)),((i%cols)*w2,(i//cols)*h2))
m.save(out)
