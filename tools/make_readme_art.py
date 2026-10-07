"""Draws the README artwork from the mod's own textures.

Outputs (into docs/art):
  banner.png                 the big pixel-art banner
  villager_<job>.png         front-view villager sprites, one per job
  head_<job>.png             villager heads, for section headings
  block_<name>.png           isometric block icons
  cube_sun/mun/kerbin.png    the three sky blocks
  divider.png                a strip of every block
  gene.png, crew.png, vab.png  crops of in-game screenshots

Run after make_textures.py:  python tools/make_readme_art.py
"""
import os
import random
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
TEX = os.path.join(ROOT, "GameData", "VillagerSpaceProgram", "PluginData", "Textures")
SHOTS = os.path.join(ROOT, "screenshots")
OUT = os.path.join(ROOT, "docs", "art")

sys.path.insert(0, HERE)
import make_textures as mt  # noqa: E402

JOBS = ["pilot", "engineer", "scientist", "tourist", "villager"]
BLOCKS = ["oak_planks", "iron_block", "furnace", "tnt", "white_wool", "obsidian", "observer",
          "bookshelf", "daylight_detector", "dispenser", "redstone_lamp", "note_block",
          "smooth_stone", "oak_log", "chest", "packed_ice", "glowstone", "quartz_block",
          "stone_bricks", "copper_block", "gold_block", "diamond_block", "iron_ore",
          "redstone_block", "piston", "cobblestone"]
NEAREST = Image.NEAREST


def up(img, k):
    return img.resize((img.width * k, img.height * k), NEAREST)


def shade(img, f):
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            px[x, y] = (min(255, int(r * f)), min(255, int(g * f)), min(255, int(b * f)), a)
    return img


def outline(img, color=(26, 20, 18, 255)):
    src = img.load()
    out = img.copy()
    dst = out.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            if src[x, y][3] != 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and src[nx, ny][3] > 100:
                    dst[x, y] = color
                    break
    return out


def block_faces(name):
    atlas = Image.open(os.path.join(TEX, "block_" + name + ".png")).convert("RGBA")
    return atlas.crop((0, 0, 16, 16)), atlas.crop((16, 0, 32, 16)), atlas.crop((32, 0, 48, 16))


# ---------------------------------------------------------------- isometric blocks

def iso(side, top, k=4, shades=(1.0, 0.8, 0.62), right=None):
    """A Minecraft-style isometric block: top face plus two shaded side faces."""
    t = 16 * k
    right = right or side
    faces = [
        (up(top, k), shades[0], (0.5, 1, -t / 2, -0.5, 1, t / 2)),
        (up(side, k), shades[1], (1, 0, 0, -0.5, 1, -t / 2)),
        (up(right, k), shades[2], (1, 0, -t, 0.5, 1, -1.5 * t)),
    ]
    out = Image.new("RGBA", (2 * t, 2 * t), (0, 0, 0, 0))
    for tex, f, data in (faces[1], faces[2], faces[0]):
        face = shade(tex, f).transform((2 * t, 2 * t), Image.AFFINE, data, resample=NEAREST,
                                       fillcolor=(0, 0, 0, 0))
        out.alpha_composite(face)
    return out


def sky_faces(kind):
    rng = random.Random("vsp-readme-" + kind)
    if kind == "sun":
        s = Image.open(os.path.join(TEX, "sky_sun.png")).convert("RGBA")
        return s, s
    if kind == "mun":
        t = mt.noise(rng, (176, 178, 184), 6)
        for (x, y, w) in ((2, 2, 4), (9, 1, 3), (10, 8, 5), (2, 10, 3), (6, 6, 2), (12, 13, 2), (0, 7, 2)):
            t.rect(x, y, x + w - 1, y + w - 1, (120, 122, 130))
            t.rect(x, y, x + w - 1, y, (98, 100, 108))
        return t.image(), t.image()
    # kerbin: oceans, grassy continents, sandy coasts, a polar cap on top
    def face(seed, cap):
        r = random.Random(seed)
        t = mt.noise(r, (62, 104, 222), 8)
        for _ in range(3):
            cx, cy, rad = r.randrange(16), r.randrange(16), r.uniform(2.5, 4.5)
            for y in range(16):
                for x in range(16):
                    d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5 + r.uniform(-0.8, 0.8)
                    if d < rad:
                        t.set(x, y, mt.add(mt.rgba((92, 158, 54) if d < rad - 1.2 else (214, 204, 150)), r.uniform(-8, 8)))
        if cap:
            for y in range(16):
                for x in range(16):
                    if ((x - 7.5) ** 2 + (y - 7.5) ** 2) ** 0.5 < 5 + r.uniform(-1, 1):
                        t.set(x, y, (240, 244, 248))
        return t.image()
    return face(kind + "1", False), face(kind + "top", True)


# ---------------------------------------------------------------- villagers

def villager(job, helmet=False):
    """Front view, 1 px = 1 Minecraft pixel: head 16x16, robe, crossed arms, legs."""
    atlas = Image.open(os.path.join(TEX, "villager_" + job + ".png")).convert("RGBA")

    def cell(i):
        return atlas.crop(((i % 4) * 16, (i // 4) * 16, (i % 4) * 16 + 16, (i // 4) * 16 + 16))

    ox, top = 4, 3
    img = Image.new("RGBA", (26, 50), (0, 0, 0, 0))
    img.alpha_composite(cell(7).crop((0, 0, 8, 8)), (ox, top + 36))
    img.alpha_composite(shade(cell(7).crop((8, 0, 16, 8)), 0.85), (ox + 8, top + 36))
    img.alpha_composite(cell(4).resize((16, 20), NEAREST), (ox, top + 16))
    img.alpha_composite(cell(0), (ox, top))
    nose = cell(3).crop((6, 0, 10, 7))
    nose.alpha_composite(shade(nose.crop((3, 0, 4, 7)), 0.8), (3, 0))
    img.alpha_composite(nose, (ox + 6, top + 8))
    arms = cell(6).resize((18, 7), NEAREST)
    img.alpha_composite(arms, (ox - 1, top + 19))
    if helmet:
        d = ImageDraw.Draw(img)
        g = Image.new("RGBA", img.size, (0, 0, 0, 0))
        gd = ImageDraw.Draw(g)
        gd.rectangle((ox - 2, top - 2, ox + 17, top + 17), fill=(190, 230, 245, 70))
        img.alpha_composite(g)
        d.rectangle((ox - 2, top - 2, ox + 17, top + 17), outline=(226, 246, 250, 255))
        for i in range(4):
            d.point((ox + 1 + i, top + 4 - i), fill=(250, 255, 255, 230))
    return outline(img)


def head(job):
    v = villager(job)
    return v.crop((3, 2, 23, 20))


# ---------------------------------------------------------------- pixel font

FONT = {
    "A": [" ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
    "B": ["#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### "],
    "C": [" ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### "],
    "D": ["#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### "],
    "E": ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####"],
    "F": ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    "],
    "G": [" ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####"],
    "H": ["#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
    "I": ["###", " # ", " # ", " # ", " # ", " # ", "###"],
    "J": ["  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  "],
    "K": ["#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #"],
    "L": ["#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####"],
    "M": ["#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #"],
    "N": ["#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #"],
    "O": [" ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
    "P": ["#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    "],
    "R": ["#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #"],
    "S": [" ####", "#    ", "#    ", " ### ", "    #", "    #", "#### "],
    "T": ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  "],
    "U": ["#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
    "V": ["#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  "],
    "W": ["#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #"],
    "Y": ["#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  "],
    ".": ["  ", "  ", "  ", "  ", "  ", "##", "##"],
    ",": ["  ", "  ", "  ", "  ", "  ", " #", "# "],
    "'": ["#", "#", " ", " ", " ", " ", " "],
    " ": ["   "] * 7,
}


def text(s, k, color, shadow=None):
    w = sum(len(FONT[c][0]) + 1 for c in s) - 1
    img = Image.new("RGBA", ((w + 1) * k, 8 * k), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    layers = ([(1, shadow)] if shadow else []) + [(0, color)]
    for off, col in layers:
        x = 0
        for c in s:
            g = FONT[c]
            for gy, row in enumerate(g):
                for gx, ch in enumerate(row):
                    if ch == "#":
                        X, Y = (x + gx + off) * k, (gy + off) * k
                        d.rectangle((X, Y, X + k - 1, Y + k - 1), fill=col)
            x += len(g[0]) + 1
    return img


# ---------------------------------------------------------------- banner

def grass_side(rng):
    t = mt.noise(rng, (134, 96, 67), 8)
    for _ in range(14):
        t.set(rng.randrange(16), rng.randrange(4, 16), (110, 78, 54))
    for x in range(16):
        h = 3 + (1 if rng.random() < 0.4 else 0)
        for y in range(h):
            t.set(x, y, mt.add(mt.rgba((96, 160, 54)), rng.uniform(-10, 10)))
    return t.image()


def dirt(rng):
    t = mt.noise(rng, (134, 96, 67), 8)
    for _ in range(18):
        t.set(rng.randrange(16), rng.randrange(16), (104, 74, 50))
    return t.image()


def banner():
    W, H = 1280, 440
    rng = random.Random("vsp-banner")
    sky = Image.new("RGBA", (W // 4, H // 4))
    sp = sky.load()
    top, bot = (10, 12, 34), (78, 54, 126)
    for y in range(sky.height):
        f = y / (sky.height - 1)
        col = tuple(int(top[i] + (bot[i] - top[i]) * f ** 1.6) for i in range(3)) + (255,)
        for x in range(sky.width):
            sp[x, y] = col
    for _ in range(170):
        x, y = rng.randrange(sky.width), rng.randrange(int(sky.height * 0.8))
        sp[x, y] = rng.choice([(255, 255, 255, 255), (255, 240, 200, 255), (190, 210, 255, 255)])
    img = up(sky, 4)

    # Sun and Mun blocks in the sky
    sun_side, sun_top = sky_faces("sun")
    img.alpha_composite(iso(sun_side, sun_top, 5, (1.0, 0.9, 0.8)), (1088, 18))
    mun_side, mun_top = sky_faces("mun")
    img.alpha_composite(iso(mun_side, mun_top, 3), (742, 138))

    # Title
    img.alpha_composite(text("VILLAGER SPACE PROGRAM", 6, (255, 246, 214, 255), (60, 30, 70, 255)), (40, 34))
    img.alpha_composite(text("KERBAL SPACE PROGRAM, MADE OF BLOCKS.", 3, (176, 232, 255, 255), (40, 24, 60, 255)), (44, 104))

    ground = 376
    gside = up(grass_side(rng), 2)
    gdirt = up(dirt(rng), 2)
    for x in range(0, W, 32):
        img.alpha_composite(gside, (x, ground))
        img.alpha_composite(gdirt, (x, ground + 32))

    def side(name, half=False):
        s = up(block_faces(name)[0], 2)
        return s.crop((0, 0, 32, 16)) if half else s

    # Launch pad and the Kerbal X, block by block
    cx = 1000
    pad = side("smooth_stone", True)
    for x in range(cx - 96, cx + 96, 32):
        img.alpha_composite(pad, (x, ground - 16))
    y = ground - 16
    for name, half in (("furnace", False), ("iron_block", False), ("iron_block", False), ("iron_block", False),
                       ("smooth_stone", True), ("furnace", False), ("iron_block", False),
                       ("oak_planks", False), ("white_wool", True)):
        h = 16 if half else 32
        y -= h
        b = side(name, half)
        img.alpha_composite(b, (cx - 32, y))
        img.alpha_composite(shade(b, 0.82), (cx, y))
    for bx in (cx - 64, cx + 32):
        by = ground - 16
        for name, half in (("furnace", False), ("tnt", False), ("tnt", False), ("quartz_block", True)):
            h = 16 if half else 32
            by -= h
            img.alpha_composite(side(name, half), (bx, by))

    # The crew
    sprites = [villager("pilot", True), villager("engineer"), villager("scientist"), villager("tourist"),
               villager("villager")]
    for i, s in enumerate(sprites):
        s3 = up(s, 3)
        img.alpha_composite(s3, (70 + i * 104, ground - s3.height + 3))

    # "Hmm."
    bub = text("HMM.", 3, (40, 30, 30, 255))
    bw, bh = bub.width + 22, bub.height + 14
    bx, by = 548, 196
    d = ImageDraw.Draw(img)
    d.rectangle((bx, by, bx + bw, by + bh), fill=(250, 250, 244, 255), outline=(40, 30, 30, 255), width=3)
    d.polygon([(bx + 12, by + bh), (bx + 30, by + bh), (bx + 6, by + bh + 18)], fill=(250, 250, 244, 255))
    d.line([(bx + 12, by + bh + 1), (bx + 6, by + bh + 18), (bx + 30, by + bh + 1)], fill=(40, 30, 30, 255), width=3)
    img.alpha_composite(bub, (bx + 12, by + 9))
    return img


# ---------------------------------------------------------------- main

def crop_shot(name, box, out, scale=1):
    path = os.path.join(SHOTS, name)
    if not os.path.exists(path):
        return
    im = Image.open(path).convert("RGB").crop(box)
    if scale != 1:
        im = up(im, scale)
    im.save(os.path.join(OUT, out))


def main():
    os.makedirs(OUT, exist_ok=True)
    banner().save(os.path.join(OUT, "banner.png"))
    for job in JOBS:
        up(villager(job, job == "pilot"), 6).save(os.path.join(OUT, "villager_" + job + ".png"))
        up(head(job), 6).save(os.path.join(OUT, "head_" + job + ".png"))
    for name in BLOCKS:
        s, t, _ = block_faces(name)
        iso(s, t, 4).save(os.path.join(OUT, "block_" + name + ".png"))
    for kind in ("sun", "mun", "kerbin"):
        s, t = sky_faces(kind)
        shades = (1.0, 0.9, 0.8) if kind == "sun" else (1.0, 0.8, 0.62)
        iso(s, t, 5, shades).save(os.path.join(OUT, "cube_" + kind + ".png"))
    strip = Image.new("RGBA", (64 * len(BLOCKS), 64))
    for i, name in enumerate(BLOCKS):
        strip.alpha_composite(up(block_faces(name)[0], 4), (i * 64, 0))
    strip.save(os.path.join(OUT, "divider.png"))
    crop_shot("ksc.png", (194, 110, 542, 464), "gene.png", 2)
    crop_shot("rocket-on-pad.png", (1002, 570, 1248, 688), "crew.png", 3)
    crop_shot("rocket-in-vab.png", (0, 28, 250, 640), "parts-list.png")
    print("wrote README art to " + OUT)


if __name__ == "__main__":
    main()
