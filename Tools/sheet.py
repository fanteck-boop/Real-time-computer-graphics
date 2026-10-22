"""Contact sheet / before-after helper: python sheet.py out.png cols tileW title img1[:label] img2[:label] ..."""
import sys
from PIL import Image, ImageDraw, ImageFont


def font(sz):
    for f in ("C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/arial.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


def sheet(paths, labels, cols, tw, out, title=None, bg=(24, 24, 28)):
    ims = [Image.open(p).convert("RGB") for p in paths]
    th = int(ims[0].height * tw / ims[0].width)
    ims = [i.resize((tw, th), Image.LANCZOS) for i in ims]
    cap = 34
    rows = (len(ims) + cols - 1) // cols
    top = 52 if title else 0
    pad = 8
    W = cols * tw + (cols + 1) * pad
    H = top + rows * (th + cap) + (rows + 1) * pad
    S = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(S)
    if title:
        d.text((pad + 4, 12), title, fill=(235, 235, 235), font=font(26))
    for i, im in enumerate(ims):
        r, c = divmod(i, cols)
        x = pad + c * (tw + pad)
        y = top + pad + r * (th + cap + pad)
        S.paste(im, (x, y))
        d.text((x + 2, y + th + 6), labels[i], fill=(220, 220, 220), font=font(18))
    S.save(out)
    print("wrote", out, S.size)


if __name__ == "__main__":
    out, cols, tw, title = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
    paths, labels = [], []
    for a in sys.argv[5:]:
        p, _, l = a.partition("::")
        paths.append(p)
        labels.append(l or p.rsplit("/", 1)[-1])
    sheet(paths, labels, cols, tw, out, title or None)
