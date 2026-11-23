"""Contact sheet for the exercise 5 frames.
python Tools/ex5_contact_sheet.py <out.png> <cols> <frame1.png> [frame2.png ...]
Each tile gets its file name (minus prefix) as a label."""
import sys, os
from PIL import Image, ImageDraw, ImageFont

out, cols, files = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
ims = [Image.open(f).convert('RGB') for f in files]
w, h = ims[0].size
tw, th = w // 2, h // 2
rows = (len(ims) + cols - 1) // cols
sheet = Image.new('RGB', (cols * tw, rows * th), (0, 0, 0))
try:
    font = ImageFont.truetype('arial.ttf', 20)
except OSError:
    font = ImageFont.load_default()
d = ImageDraw.Draw(sheet)
for i, (im, f) in enumerate(zip(ims, files)):
    x, y = (i % cols) * tw, (i // cols) * th
    sheet.paste(im.resize((tw, th), Image.LANCZOS), (x, y))
    label = os.path.splitext(os.path.basename(f))[0].split('_', 1)[-1]
    d.rectangle([x, y, x + 12 * len(label) + 12, y + 28], fill=(0, 0, 0))
    d.text((x + 6, y + 3), label, fill=(255, 255, 255), font=font)
sheet.save(out)
print('wrote', out, sheet.size)
