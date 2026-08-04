#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CatClawMusic 主页背景图生成器 (纯 PIL 版，无需 numpy)
- 5 个品牌色各一张，竖屏 1080x2340，暗色玻璃拟态风格
- 色值取自 CatClawMusic.Maui/Resources/Styles/Colors.xaml
"""
import math
import os
import random
from PIL import Image, ImageDraw, ImageChops, ImageFilter, ImageEnhance

W, H = 1080, 2340
BASE_DARK = (8, 11, 26)          # WindowBackgroundColor #080B1A
OUT_DIR = os.path.join(os.path.dirname(__file__), "home-backgrounds")
os.makedirs(OUT_DIR, exist_ok=True)


def hex2rgb(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def make_radial_sprite(R, color):
    """返回 RGBA 径向辉光精灵（中心亮、边缘透明）"""
    img = Image.new("RGBA", (2 * R, 2 * R), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    step = 2
    for i in range(R, 0, -step):
        t = 1 - i / R
        a = int(255 * t * t)
        d.ellipse([R - i, R - i, R + i, R + i],
                  fill=(color[0], color[1], color[2], a))
    return img


def add_glow(base, cx, cy, radius, color, strength=1.0):
    spr = make_radial_sprite(radius, color)
    if strength != 1.0:
        rgb = Image.new("RGB", spr.size, (0, 0, 0))
        rgb.paste(spr, (0, 0), spr)
        rgb = ImageEnhance.Brightness(rgb).enhance(strength)
        full = Image.new("RGB", base.size, (0, 0, 0))
        full.paste(rgb, (int(cx - radius), int(cy - radius)))
    else:
        full = Image.new("RGB", base.size, (0, 0, 0))
        full.paste(spr, (int(cx - radius), int(cy - radius)), spr)
    return ImageChops.screen(base, full)


def vgrad(top, bottom):
    g = Image.new("RGB", (1, H))
    px = g.load()
    for y in range(H):
        t = y / (H - 1)
        r = int(top[0] * (1 - t) + bottom[0] * t)
        gg = int(top[1] * (1 - t) + bottom[1] * t)
        b = int(top[2] * (1 - t) + bottom[2] * t)
        px[0, y] = (r, gg, b)
    return g.resize((W, H), Image.BILINEAR)


def layer_from_draw(draw_fn):
    """用黑底图层执行 draw_fn(draw)，返回模糊后的发光层"""
    layer = Image.new("RGB", (W, H), (0, 0, 0))
    draw_fn(ImageDraw.Draw(layer))
    blur = layer.filter(ImageFilter.GaussianBlur(7))
    return ImageChops.screen(layer, blur)


def motif_equalizer(base, color):
    rng = random.Random(11)
    yt, yb = H * 0.58, H * 0.90
    n = 26

    def fn(d):
        for i in range(n):
            x = int((i + 0.5) / n * W)
            hgt = rng.uniform(0.35, 1.0)
            y_top = yb - (yb - yt) * hgt
            bw = rng.uniform(14, 22)
            d.line([(x, y_top), (x, yb)], fill=color, width=int(bw), joint="curve")

    return ImageChops.screen(base, layer_from_draw(fn))


def motif_ripples(base, color, cx, cy):
    def fn(d):
        n = 16
        for i in range(1, n + 1):
            r = 120 * i
            w = max(1, int(7 * (1 - i / (n + 1))) + 1)
            d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=color, width=w)

    base = ImageChops.screen(base, layer_from_draw(fn))
    return add_glow(base, cx, cy, 90, color, 0.6)


def motif_waveform(base, color):
    def fn(d):
        for k in range(4):
            yc = H * (0.30 + k * 0.02)
            amp = 70 + k * 18
            freq = (2 * math.pi) / (W * (0.55 + 0.12 * k))
            phase = k * 1.3
            pts = []
            for x in range(0, W, 4):
                y = yc + amp * math.sin(freq * x + phase) + 0.4 * amp * math.sin(freq * 2.3 * x)
                pts.append((x, y))
            d.line(pts, fill=color, width=5, joint="curve")

    return ImageChops.screen(base, layer_from_draw(fn))


def motif_pulse(base, color, cx, cy):
    def fn(d):
        n = 12
        for i in range(1, n + 1):
            r = 150 * i
            w = max(1, int(8 * (1 - i / (n + 1))) + 1)
            d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=color, width=w)

    base = ImageChops.screen(base, layer_from_draw(fn))
    return add_glow(base, cx, cy, 70, color, 0.9)


def motif_stars(base, color):
    rng = random.Random(23)
    base = add_glow(base, W * 0.30, H * 0.22, 360, color, 0.35)
    base = add_glow(base, W * 0.78, H * 0.40, 300, (90, 70, 200), 0.25)
    # 星点
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dl = ImageDraw.Draw(layer)
    for _ in range(220):
        px = rng.randint(0, W - 1)
        py = rng.randint(0, H - 1)
        rad = rng.uniform(0.6, 2.2)
        a = rng.uniform(0.2, 0.9)
        spr = make_radial_sprite(int(rad * 4), (235, 240, 255))
        layer.paste(spr, (int(px - rad * 4), int(py - rad * 4)), spr)
    star_rgb = Image.new("RGB", (W, H), (0, 0, 0))
    star_rgb.paste(layer, (0, 0), layer)
    base = ImageChops.screen(base, star_rgb)
    # 猫爪星座：四趾 + 掌
    pad = W * 0.5
    pts = [
        (pad + 0.00 * W, H * 0.46),
        (pad + 0.10 * W, H * 0.40),
        (pad + 0.20 * W, H * 0.42),
        (pad + 0.14 * W, H * 0.52),
        (pad + 0.09 * W, H * 0.58),
        (pad + 0.10 * W, H * 0.66),
    ]
    line_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dl2 = ImageDraw.Draw(line_layer)
    for (x0, y0), (x1, y1) in zip(pts, pts[1:] + [pts[0]]):
        dl2.line([(x0, y0), (x1, y1)], fill=(color[0], color[1], color[2], 90), width=3)
        s = make_radial_sprite(26, (200, 190, 255))
        line_layer.paste(s, (int(x0 - 26), int(y0 - 26)), s)
    ll_rgb = Image.new("RGB", (W, H), (0, 0, 0))
    ll_rgb.paste(line_layer, (0, 0), line_layer)
    return ImageChops.screen(base, ll_rgb)


def add_particles(base, color, n=60):
    rng = random.Random(7)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dl = ImageDraw.Draw(layer)
    for _ in range(n):
        px = rng.randint(0, W - 1)
        py = rng.randint(0, H - 1)
        rad = rng.randint(1, 4)
        spr = make_radial_sprite(rad * 4, color)
        layer.paste(spr, (int(px - rad * 4), int(py - rad * 4)), spr)
    rgb = Image.new("RGB", (W, H), (0, 0, 0))
    rgb.paste(layer, (0, 0), layer)
    return ImageChops.screen(base, rgb)


def vignette(base):
    R = max(W, H) // 2
    v = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(v)
    for i in range(R, 0, -3):
        a = int(255 * (i / R) ** 1.6)
        d.ellipse([W / 2 - i, H / 2 - i, W / 2 + i, H / 2 + i], fill=a)
    return ImageChops.multiply(base, v.convert("RGB"))


def finalize(base, name):
    base = base.filter(ImageFilter.GaussianBlur(0.4))
    nimg = Image.effect_noise((W, H), 10).convert("RGB")
    nimg = ImageEnhance.Brightness(nimg).enhance(0.05)
    base = ImageChops.add(base, nimg)
    base = base.convert("RGB")
    path = os.path.join(OUT_DIR, f"home-bg-{name}.png")
    base.save(path, "PNG")
    print("saved", path, base.size)


def build(cfg):
    top = hex2rgb(cfg["top"])
    col = hex2rgb(cfg["color"])
    base = vgrad(tuple(int(top[c] * 0.45 + BASE_DARK[c] * 0.55) for c in range(3)), BASE_DARK)
    base = add_glow(base, int(W * cfg["gx"]), int(H * cfg["gy"]), cfg["gr"], col, cfg["gs"])
    base = add_glow(base, W // 2, int(H * 0.08), 420, top, 0.4)
    motif = cfg["motif"]
    if motif == "equalizer":
        base = motif_equalizer(base, col)
    elif motif == "ripples":
        base = motif_ripples(base, col, int(W * 0.5), int(H * 0.42))
    elif motif == "waveform":
        base = motif_waveform(base, col)
    elif motif == "pulse":
        base = motif_pulse(base, col, int(W * 0.5), int(H * 0.50))
    elif motif == "stars":
        base = motif_stars(base, col)
    if cfg.get("particles", True):
        base = add_particles(base, col, cfg.get("pn", 60))
    base = vignette(base)
    finalize(base, cfg["name"])


CONFIGS = [
    {"name": "violet", "color": "#512BD4", "top": "#7A5CFF",
     "gx": 0.5, "gy": 0.30, "gr": 520, "gs": 0.7, "motif": "equalizer", "pn": 70},
    {"name": "lavender", "color": "#8C7BFF", "top": "#C9BEFF",
     "gx": 0.5, "gy": 0.42, "gr": 560, "gs": 0.55, "motif": "ripples", "pn": 60},
    {"name": "cyan", "color": "#55D6FF", "top": "#9CEBFF",
     "gx": 0.5, "gy": 0.32, "gr": 500, "gs": 0.6, "motif": "waveform", "pn": 55},
    {"name": "pink", "color": "#FF7AAE", "top": "#FFB3CF",
     "gx": 0.5, "gy": 0.50, "gr": 520, "gs": 0.6, "motif": "pulse", "pn": 65},
    {"name": "indigo", "color": "#190649", "top": "#3A1E8F",
     "gx": 0.35, "gy": 0.25, "gr": 480, "gs": 0.55, "motif": "stars", "particles": False},
]

if __name__ == "__main__":
    for c in CONFIGS:
        build(c)
    print("ALL DONE ->", OUT_DIR)
