"""Draws every Villager Space Program texture as original 16x16 pixel art.

Nothing here is copied from Minecraft: each block is a hand-described lookalike
built from noise, frames and simple shapes, so the textures are ours to keep.

Outputs (into GameData/VillagerSpaceProgram/PluginData/Textures):
  block_<name>.png     48x16 atlas: side | top | bottom
  villager_<job>.png   64x64 atlas of 16x16 cells (see VILLAGER CELLS below)
  sky_sun.png (the Sun block), glass.png (villager helmets)

Run:  python tools/make_textures.py
"""
import os
import random

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "GameData", "VillagerSpaceProgram", "PluginData", "Textures")
S = 16


def clamp(v):
    return max(0, min(255, int(round(v))))


def rgba(c, a=255):
    if len(c) == 4:
        return tuple(clamp(x) for x in c)
    return (clamp(c[0]), clamp(c[1]), clamp(c[2]), a)


def mul(c, f):
    return rgba((c[0] * f, c[1] * f, c[2] * f), c[3] if len(c) == 4 else 255)


def add(c, d):
    return rgba((c[0] + d, c[1] + d, c[2] + d), c[3] if len(c) == 4 else 255)


class Tile:
    def __init__(self, fill=(0, 0, 0, 0)):
        self.px = [[rgba(fill) for _ in range(S)] for _ in range(S)]

    def set(self, x, y, c):
        if 0 <= x < S and 0 <= y < S:
            self.px[y][x] = rgba(c)

    def get(self, x, y):
        return self.px[y % S][x % S]

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def frame(self, c, inset=0):
        a, b = inset, S - 1 - inset
        for i in range(a, b + 1):
            self.set(i, a, c)
            self.set(i, b, c)
            self.set(a, i, c)
            self.set(b, i, c)

    def copy(self):
        t = Tile()
        t.px = [row[:] for row in self.px]
        return t

    def image(self):
        im = Image.new("RGBA", (S, S))
        for y in range(S):
            for x in range(S):
                im.putpixel((x, y), self.px[y][x])
        return im


def noise(rng, base, amt):
    t = Tile()
    for y in range(S):
        for x in range(S):
            t.set(x, y, add(rgba(base), rng.uniform(-amt, amt)))
    return t


def voronoi(rng, n, edge, cell_color):
    """Irregular cells with dark edges (cobblestone, glowstone)."""
    pts = [(rng.uniform(0, S), rng.uniform(0, S)) for _ in range(n)]
    cols = [cell_color(rng) for _ in range(n)]
    t = Tile()
    for y in range(S):
        for x in range(S):
            ds = []
            for i, (px, py) in enumerate(pts):
                dx = min(abs(x + 0.5 - px), S - abs(x + 0.5 - px))
                dy = min(abs(y + 0.5 - py), S - abs(y + 0.5 - py))
                ds.append(((dx * dx + dy * dy) ** 0.5, i))
            ds.sort()
            if ds[1][0] - ds[0][0] < 1.1:
                t.set(x, y, add(rgba(edge), rng.uniform(-6, 6)))
            else:
                t.set(x, y, add(cols[ds[0][1]], rng.uniform(-8, 8)))
    return t


# ---------------------------------------------------------------- blocks

def stone(rng, base=125):
    t = noise(rng, (base, base, base), 10)
    for _ in range(14):
        t.set(rng.randrange(S), rng.randrange(S), (base - 25, base - 25, base - 25))
    return t


def cobblestone(rng):
    return voronoi(rng, 9, (62, 62, 62), lambda r: rgba((r.randint(105, 150),) * 3))


def stone_bricks(rng):
    t = noise(rng, (122, 122, 122), 6)
    for y in range(S):
        g = y // 4
        joints = (7, 15) if g % 2 == 0 else (3, 11)
        for x in range(S):
            if y % 4 == 3 or x in joints:
                t.set(x, y, (78, 78, 78))
            elif y % 4 == 0:
                t.set(x, y, add(t.get(x, y), 14))
    return t


def smooth_stone(rng):
    t = noise(rng, (160, 160, 160), 4)
    t.frame((118, 118, 118))
    return t


def oak_planks(rng):
    base = (162, 130, 78)
    t = noise(rng, base, 7)
    for y in range(S):
        g = y // 4
        seam = (rng.randrange(2, 14) if g % 2 else 8)
        for x in range(S):
            if y % 4 == 3:
                t.set(x, y, (112, 88, 52))
            elif x == seam and y % 4 != 0:
                t.set(x, y, (125, 98, 58))
    for _ in range(18):
        x, y = rng.randrange(S), rng.randrange(S)
        if y % 4 != 3:
            t.set(x, y, (140, 110, 64))
    return t


def oak_log_side(rng):
    t = Tile()
    for x in range(S):
        col = (104, 83, 50) if x % 3 else (78, 62, 37)
        for y in range(S):
            t.set(x, y, add(rgba(col), rng.uniform(-8, 8)))
    for _ in range(10):
        x, y = rng.randrange(S), rng.randrange(S)
        for k in range(rng.randint(2, 5)):
            t.set(x, y + k, (66, 52, 30))
    return t


def oak_log_top(rng):
    t = Tile()
    for y in range(S):
        for x in range(S):
            d = max(abs(x - 7.5), abs(y - 7.5))
            if d > 6.6:
                c = (104, 83, 50)
            else:
                c = (182, 147, 92) if int(d) % 2 == 0 else (150, 118, 70)
            t.set(x, y, add(rgba(c), rng.uniform(-5, 5)))
    return t


def metal_block(rng, base, dark, light):
    t = noise(rng, base, 4)
    t.frame(dark)
    for i in range(1, S - 1):
        t.set(i, 1, light)
        t.set(1, i, light)
        t.set(i, S - 2, mul(rgba(base), 0.88))
        t.set(S - 2, i, mul(rgba(base), 0.88))
    for y in (5, 10):
        for x in range(3, S - 3):
            t.set(x, y, mul(rgba(base), 0.93))
    for _ in range(5):
        t.set(rng.randrange(3, 13), rng.randrange(3, 13), light)
    return t


def iron_block(rng):
    return metal_block(rng, (220, 220, 222), (165, 165, 170), (245, 245, 245))


def gold_block(rng):
    return metal_block(rng, (248, 216, 64), (196, 140, 22), (255, 250, 160))


def diamond_block(rng):
    t = metal_block(rng, (104, 228, 220), (38, 156, 156), (205, 255, 250))
    for _ in range(6):
        x, y = rng.randrange(3, 12), rng.randrange(3, 12)
        t.set(x, y, (230, 255, 255))
        t.set(x + 1, y + 1, (60, 190, 185))
    return t


def copper_block(rng):
    t = metal_block(rng, (194, 108, 78), (148, 74, 54), (228, 146, 104))
    for _ in range(7):
        t.set(rng.randrange(2, 14), rng.randrange(2, 14), (96, 172, 140))
    return t


def redstone_block(rng):
    t = noise(rng, (168, 24, 10), 10)
    for _ in range(26):
        t.set(rng.randrange(S), rng.randrange(S), (232, 44, 22))
    for _ in range(14):
        t.set(rng.randrange(S), rng.randrange(S), (112, 12, 6))
    t.frame((120, 14, 6))
    return t


def iron_ore(rng):
    t = stone(rng)
    for _ in range(5):
        x, y = rng.randrange(1, 14), rng.randrange(1, 14)
        for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
            if rng.random() < 0.85:
                t.set(x + dx, y + dy, (216, 176, 146))
        t.set(x + 1, y + 1, (174, 134, 104))
    return t


FONT = {
    "T": ["###", ".#.", ".#.", ".#.", ".#."],
    "N": ["#..#", "##.#", "#.##", "#..#", "#..#"],
}


def tnt_side(rng):
    t = Tile()
    for y in range(S):
        for x in range(S):
            if 4 <= y <= 11:
                c = (226, 226, 222)
            else:
                c = (214, 66, 28) if x % 2 == 0 else (176, 48, 20)
            t.set(x, y, add(rgba(c), rng.uniform(-5, 5)))
    x = 2
    for ch in "TNT":
        rows = FONT[ch]
        for dy, row in enumerate(rows):
            for dx, v in enumerate(row):
                if v == "#":
                    t.set(x + dx, 5 + dy, (24, 24, 24))
        x += len(rows[0]) + 1
    return t


def tnt_top(rng):
    t = noise(rng, (196, 62, 30), 6)
    t.rect(5, 5, 10, 10, (150, 150, 150))
    t.rect(6, 6, 9, 9, (90, 90, 90))
    t.rect(7, 7, 8, 8, (40, 40, 40))
    t.frame((150, 40, 18))
    return t


def tnt_bottom(rng):
    t = noise(rng, (172, 50, 22), 6)
    t.frame((130, 34, 14))
    return t


def furnace_front_lit(rng):
    t = stone(rng, 112)
    t.frame((80, 80, 80))
    t.rect(4, 2, 11, 3, (50, 50, 50))
    t.rect(4, 7, 11, 13, (28, 26, 24))
    for x in range(5, 11):
        h = rng.randint(2, 5)
        for k in range(h):
            y = 12 - k
            t.set(x, y, (255, 214, 72) if k < 2 else (250, 132, 30))
    t.rect(3, 6, 12, 6, (70, 70, 70))
    return t


def furnace_top(rng):
    t = smooth_stone(rng)
    t.rect(5, 5, 10, 10, (128, 128, 128))
    return t


def bookshelf_side(rng):
    t = oak_planks(rng)
    colors = [(140, 40, 40), (50, 80, 140), (60, 110, 50), (150, 120, 60),
              (100, 60, 120), (176, 150, 110), (40, 100, 110)]
    for top, bottom in ((1, 6), (9, 14)):
        x = 1
        while x < 15:
            w = rng.choice((1, 2, 2))
            c = rng.choice(colors)
            start = top + rng.choice((0, 0, 1, 2))
            for dx in range(w):
                if x + dx >= 15:
                    break
                for y in range(start, bottom + 1):
                    t.set(x + dx, y, add(rgba(c), rng.uniform(-6, 6) - (12 if dx else 0)))
                t.set(x + dx, start + 1, add(rgba(c), 40))
            x += w
        t.rect(0, top - 1, 15, top - 1, (112, 88, 52))
    t.rect(0, 15, 15, 15, (112, 88, 52))
    return t


def daylight_top(rng):
    t = Tile((58, 66, 82))
    for cy in range(4):
        for cx in range(4):
            x0, y0 = cx * 4, cy * 4
            t.rect(x0 + 1, y0 + 1, x0 + 2, y0 + 2, add(rgba((150, 176, 204)), rng.uniform(-10, 10)))
            t.set(x0 + 1, y0 + 1, (205, 225, 240))
    t.frame((104, 83, 50))
    return t


def daylight_side(rng):
    t = oak_planks(rng)
    t.rect(0, 0, 15, 2, (58, 66, 82))
    return t


def white_wool(rng):
    t = noise(rng, (234, 236, 236), 6)
    for _ in range(30):
        x, y = rng.randrange(S), rng.randrange(S)
        t.set(x, y, (208, 210, 212))
        t.set(x + 1, y, (220, 222, 224))
    return t


def dispenser_front(rng):
    t = stone(rng, 118)
    t.frame((82, 82, 82))
    t.rect(5, 5, 10, 10, (60, 60, 60))
    t.rect(6, 6, 9, 9, (22, 22, 22))
    t.set(6, 6, (40, 40, 40))
    return t


def note_block(rng):
    t = noise(rng, (102, 66, 46), 6)
    t.frame((62, 40, 28))
    t.frame((82, 54, 38), 1)
    for _ in range(20):
        t.set(rng.randrange(2, 14), rng.randrange(2, 14), (118, 78, 54))
    t.rect(6, 6, 9, 9, (54, 36, 26))
    return t


def observer_face(rng):
    t = noise(rng, (92, 92, 94), 5)
    t.frame((60, 60, 62))
    t.rect(2, 4, 6, 9, (28, 28, 30))
    t.rect(9, 4, 13, 9, (28, 28, 30))
    t.rect(3, 5, 5, 6, (60, 60, 62))
    t.rect(10, 5, 12, 6, (60, 60, 62))
    t.rect(4, 12, 11, 12, (40, 40, 42))
    return t


def observer_top(rng):
    t = noise(rng, (110, 110, 112), 5)
    t.frame((70, 70, 72))
    for y in range(3, 13):
        t.set(7, y, (160, 160, 165))
        t.set(8, y, (160, 160, 165))
    for k in range(4):
        t.set(7 - k, 3 + k, (160, 160, 165))
        t.set(8 + k, 3 + k, (160, 160, 165))
    return t


def chest_side(rng):
    t = noise(rng, (162, 106, 40), 7)
    t.frame((72, 46, 20))
    t.rect(1, 5, 14, 5, (90, 58, 24))
    t.rect(7, 4, 8, 7, (200, 200, 205))
    t.set(7, 7, (120, 120, 125))
    t.set(8, 7, (120, 120, 125))
    return t


def chest_top(rng):
    t = noise(rng, (170, 112, 44), 7)
    t.frame((72, 46, 20))
    t.frame((140, 92, 36), 1)
    return t


def packed_ice(rng):
    t = noise(rng, (142, 182, 240), 8)
    for _ in range(4):
        x, y = rng.randrange(S), rng.randrange(S)
        for k in range(rng.randint(3, 7)):
            t.set(x + k, y - k, (196, 222, 255))
    return t


def obsidian(rng):
    t = noise(rng, (20, 16, 32), 5)
    for _ in range(18):
        x, y = rng.randrange(S), rng.randrange(S)
        t.set(x, y, rng.choice([(60, 40, 92), (92, 72, 132), (40, 28, 62)]))
    return t


def glowstone(rng):
    return voronoi(rng, 10, (122, 82, 40),
                   lambda r: rgba(r.choice([(255, 230, 140), (250, 200, 100), (232, 170, 80)])))


def quartz_block(rng):
    t = noise(rng, (236, 230, 222), 3)
    t.frame((212, 202, 192))
    for i in range(1, 15):
        t.set(i, 1, (246, 242, 236))
    return t


def piston_side(rng):
    t = stone(rng, 120)
    t.rect(0, 0, 15, 3, (160, 130, 80))
    t.rect(0, 3, 15, 3, (112, 88, 52))
    t.frame((80, 80, 80))
    return t


def piston_top(rng):
    t = oak_planks(rng)
    t.rect(5, 5, 10, 10, (188, 188, 192))
    t.rect(6, 6, 9, 9, (150, 150, 156))
    return t


def redstone_lamp(rng):
    t = noise(rng, (232, 170, 90), 8)
    for y in range(S):
        for x in range(S):
            if x % 5 == 0 or y % 5 == 0:
                t.set(x, y, (110, 70, 40))
            elif (x + y) % 3 == 0:
                t.set(x, y, (255, 232, 150))
    return t


def atlas(side, top=None, bottom=None):
    top = top or side
    bottom = bottom or top
    im = Image.new("RGBA", (S * 3, S))
    im.paste(side.image(), (0, 0))
    im.paste(top.image(), (S, 0))
    im.paste(bottom.image(), (S * 2, 0))
    return im


def build_blocks():
    out = {}

    def r(name):
        return random.Random("vsp-" + name)

    out["iron_block"] = atlas(iron_block(r("iron")))
    out["gold_block"] = atlas(gold_block(r("gold")))
    out["diamond_block"] = atlas(diamond_block(r("diamond")))
    out["copper_block"] = atlas(copper_block(r("copper")))
    out["redstone_block"] = atlas(redstone_block(r("redstone")))
    out["iron_ore"] = atlas(iron_ore(r("ore")))
    out["tnt"] = atlas(tnt_side(r("tnt")), tnt_top(r("tnt_t")), tnt_bottom(r("tnt_b")))
    out["furnace"] = atlas(furnace_front_lit(r("furnace")), furnace_top(r("furnace_t")))
    out["oak_planks"] = atlas(oak_planks(r("oak")))
    out["oak_log"] = atlas(oak_log_side(r("log")), oak_log_top(r("log_t")))
    out["bookshelf"] = atlas(bookshelf_side(r("books")), oak_planks(r("oak")))
    out["daylight_detector"] = atlas(daylight_side(r("day_s")), daylight_top(r("day")), oak_planks(r("oak")))
    out["white_wool"] = atlas(white_wool(r("wool")))
    out["smooth_stone"] = atlas(smooth_stone(r("smooth")))
    out["stone_bricks"] = atlas(stone_bricks(r("bricks")))
    out["cobblestone"] = atlas(cobblestone(r("cobble")))
    out["dispenser"] = atlas(dispenser_front(r("disp")), furnace_top(r("furnace_t")))
    out["note_block"] = atlas(note_block(r("note")))
    out["observer"] = atlas(observer_face(r("obs")), observer_top(r("obs_t")))
    out["chest"] = atlas(chest_side(r("chest")), chest_top(r("chest_t")))
    out["packed_ice"] = atlas(packed_ice(r("ice")))
    out["obsidian"] = atlas(obsidian(r("obsidian")))
    out["glowstone"] = atlas(glowstone(r("glow")))
    out["quartz_block"] = atlas(quartz_block(r("quartz")))
    out["piston"] = atlas(piston_side(r("piston")), piston_top(r("piston_t")), stone(r("piston_b"), 120))
    out["redstone_lamp"] = atlas(redstone_lamp(r("lamp")))
    return out


# ---------------------------------------------------------------- sky + glass

def sun():
    t = Tile((255, 228, 88))
    t.rect(2, 2, 13, 13, (255, 244, 168))
    t.rect(4, 4, 11, 11, (255, 255, 232))
    return t.image()


def glass():
    t = Tile((200, 232, 240, 28))
    t.frame((222, 242, 246, 255))
    for k in range(4):
        t.set(3 + k, 6 - k, (245, 255, 255, 190))
        t.set(9 + k, 12 - k, (245, 255, 255, 150))
    return t.image()


# ---------------------------------------------------------------- villagers
# VILLAGER CELLS (index = row * 4 + col in the 64x64 atlas):
#   0 face   1 head side/back   2 head top   3 nose
#   4 robe front   5 robe side/back   6 arms   7 legs

SKIN = (189, 139, 114)
SKIN_DARK = (158, 110, 88)

JOBS = {
    # trait -> (robe, trim, extra)
    "villager": ((101, 74, 52), (70, 50, 35), None),
    "pilot": ((122, 86, 56), (222, 182, 62), "monocle"),
    "engineer": ((101, 74, 52), (40, 40, 45), "apron"),
    "scientist": ((122, 56, 142), (222, 190, 82), None),
    "tourist": ((80, 136, 60), (55, 95, 40), None),
}


def villager(job):
    robe, trim, extra = JOBS[job]
    rng = random.Random("vsp-villager-" + job)
    cells = []

    face = noise(rng, SKIN, 4)
    face.rect(2, 6, 13, 6, (74, 50, 36))
    for ex in (3, 10):
        face.rect(ex, 7, ex + 2, 8, (236, 236, 236))
        face.rect(ex + (1 if ex == 3 else 0), 7, ex + (2 if ex == 3 else 1), 8, (64, 142, 64))
    face.rect(0, 14, 15, 15, mul(rgba(SKIN), 0.92))
    if extra == "monocle":
        for (x, y) in ((9, 6), (10, 6), (11, 6), (12, 6), (13, 6), (9, 9), (10, 9), (11, 9), (12, 9), (13, 9),
                       (9, 7), (9, 8), (13, 7), (13, 8), (13, 10), (13, 11)):
            face.set(x, y, (222, 182, 62))
    if extra == "apron":
        face.rect(2, 6, 6, 9, (20, 20, 22))
        face.rect(0, 5, 15, 5, (20, 20, 22))
    cells.append(face)

    side = noise(rng, SKIN, 4)
    side.rect(0, 14, 15, 15, mul(rgba(SKIN), 0.9))
    if extra == "apron":
        side.rect(0, 5, 15, 5, (20, 20, 22))
    cells.append(side)

    top = noise(rng, mul(rgba(SKIN), 0.95), 4)
    cells.append(top)

    cells.append(noise(rng, SKIN_DARK, 4))

    front = noise(rng, robe, 6)
    front.rect(0, 0, 15, 1, mul(rgba(robe), 0.75))
    front.rect(6, 0, 9, 15, trim)
    front.rect(0, 9, 15, 9, mul(rgba(robe), 0.7))
    if extra == "apron":
        front.rect(2, 2, 13, 15, (40, 40, 45))
        front.rect(3, 2, 3, 15, (60, 60, 66))
        front.rect(12, 2, 12, 15, (60, 60, 66))
    front.rect(0, 15, 15, 15, mul(rgba(robe), 0.7))
    cells.append(front)

    back = noise(rng, robe, 6)
    back.rect(0, 15, 15, 15, mul(rgba(robe), 0.7))
    back.rect(0, 9, 15, 9, mul(rgba(robe), 0.8))
    cells.append(back)

    arms = noise(rng, mul(rgba(robe), 0.92), 6)
    arms.rect(5, 2, 10, 13, SKIN)
    arms.rect(5, 2, 10, 2, SKIN_DARK)
    arms.rect(5, 13, 10, 13, SKIN_DARK)
    cells.append(arms)

    cells.append(noise(rng, (80, 60, 46), 5))

    im = Image.new("RGBA", (S * 4, S * 4), (0, 0, 0, 0))
    for i, c in enumerate(cells):
        im.paste(c.image(), ((i % 4) * S, (i // 4) * S))
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    n = 0
    for name, im in build_blocks().items():
        im.save(os.path.join(OUT, "block_" + name + ".png"))
        n += 1
    for job in JOBS:
        villager(job).save(os.path.join(OUT, "villager_" + job + ".png"))
        n += 1
    sun().save(os.path.join(OUT, "sky_sun.png"))
    glass().save(os.path.join(OUT, "glass.png"))
    n += 2
    print("wrote %d textures to %s" % (n, os.path.normpath(OUT)))


if __name__ == "__main__":
    main()
