#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CatClawMusic 主页背景图生成器
- 5 个品牌色各一张，竖屏 1080x2340，暗色玻璃拟态风格
- 真实色值取自 CatClawMusic.Maui/Resources/Styles/Colors.xaml
"""
import math
import os
import numpy as np
from PIL import Image, ImageFilter

W, H = 1080, 2340
BASE_DARK = np.array([8, 11, 26], dtype=np.float64)      # WindowBackgroundColor #080B1A
OUT_DIR = os.path.join(os.path.dirname(__file__), "home-backgrounds")
os.makedirs(OUT_DIR, exist_ok=True)


def hex2rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)], dtype=np.float64)


def make_base(top_tint, bottom_tint):
    """竖向渐变底：顶部偏 tint，底部回到深底"""
    yy = np.linspace(0, 1, H)[:, None]
    base = np.zeros((H, W, 3), dtype=np.float64)
    for c in range(3):
        base[:, :, c] = (BASE_DARK[c] * (1 - yy) + bottom_tint[c] * yy * 0.35
                         + top_tint[c] * (1 - yy) * 0.25)
    return base


def add_radial_glow(img, cx, cy, radius, color, strength):
    """高斯径向辉光"""
    ys, xs = np.mgrid[0:H, 0:W]
    d2 = (xs - cx) ** 2 + (ys - cy) ** 2
    g = np.exp(-d2 / (2 * radius * radius))
    img += np.outer(g, color) * strength
    return img


def add_vignette(img):
    ys, xs = np.mgrid[0:H, 0:W]
    cx, cy = W / 2, H * 0.42
    d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    maxd = math.hypot(W / 2, H / 2)
    v = 1.0 - 0.55 * (d / maxd) ** 2.2
    img *= v[..., None]
    return img


def add_particles(img, color, n=70, r=(1, 4), alpha=(0.15, 0.6)):
    ys, xs = np.mgrid[0:H, 0:W]
    rng = np.random.default_rng(7)
    for _ in range(n):
        px = rng.integers(0, W)
        py = rng.integers(0, H)
        rad = rng.integers(r[0], r[1] + 1)
        a = rng.uniform(alpha[0], alpha[1])
        d2 = (xs - px) ** 2 + (ys - py) ** 2
        g = np.exp(-d2 / (2 * rad * rad))
        img += np.outer(g, color) * a
    return img


def motif_equalizer(img, color):
    """竖向均衡器光柱（下半区）"""
    ys, xs = np.mgrid[0:H, 0:W]
    rng = np.random.default_rng(11)
    n = 26
    top = H * 0.58
    bot = H * 0.9
    for i in range(n):
        x = int((i + 0.5) / n * W)
        hgt = rng.uniform(0.35, 1.0)
        y_top = bot - (bot - top) * hgt
        # x 方向高斯柱
        sx = rng.uniform(10, 16)
        xg = np.exp(-((xs - x) ** 2) / (2 * sx * sx))
        # y 方向从 y_top 到 bot 渐隐
        yg = np.clip((bot - ys) / (bot - y_top), 0, 1)
        yg = yg ** 1.5
        col = (xg * yg)[:, :, None] * color * 0.5
        img += col
    return img


def motif_ripples(img, color, cx, cy):
    """同心声波涟漪"""
    ys, xs = np.mgrid[0:H, 0:W]
    d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    spacing = 120.0
    rings = (np.sin(d / spacing * 2 * np.pi) * 0.5 + 0.5) ** 6
    fade = np.exp(-d / (H * 0.7))
    img += rings[:, :, None] * color * fade[:, :, None] * 0.5
    # 中心亮核
    add_radial_glow(img, cx, cy, 90, color, 0.6)
    return img


def motif_waveform(img, color):
    """流动波形线"""
    ys, xs = np.mgrid[0:H, 0:W]
    x = xs.astype(np.float64)
    out = np.zeros((H, W), dtype=np.float64)
    for k in range(4):
        yc = H * (0.30 + k * 0.02)
        amp = 70 + k * 18
        freq = (2 * np.pi) / (W * (0.55 + 0.12 * k))
        phase = k * 1.3
        y_line = yc + amp * np.sin(freq * x + phase) + 30 * np.sin(freq * 2.3 * x)
        sigma = 5.5 + k
        out += np.exp(-((ys - y_line) ** 2) / (2 * sigma * sigma))
    out = np.clip(out, 0, 1)
    img += out[:, :, None] * color * 0.45
    return img


def motif_pulse(img, color, cx, cy):
    """脉冲环（心动/收藏）"""
    ys, xs = np.mgrid[0:H, 0:W]
    d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    spacing = 150.0
    rings = (np.sin(d / spacing * 2 * np.pi - 1.2) * 0.5 + 0.5) ** 4
    fade = np.exp(-d / (H * 0.55))
    img += rings[:, :, None] * color * fade[:, :, None] * 0.55
    add_radial_glow(img, cx, cy, 70, color, 0.9)
    return img


def motif_stars(img, color):
    """星空 + 猫爪星座 + 星云"""
    ys, xs = np.mgrid[0:H, 0:W]
    rng = np.random.default_rng(23)
    # 星云辉光
    add_radial_glow(img, W * 0.30, H * 0.22, 360, color, 0.35)
    add_radial_glow(img, W * 0.78, H * 0.40, 300, np.array([90, 70, 200.]), 0.25)
    # 星点
    for _ in range(220):
        px = rng.integers(0, W)
        py = rng.integers(0, H)
        rad = rng.uniform(0.6, 2.2)
        a = rng.uniform(0.2, 0.9)
        d2 = (xs - px) ** 2 + (ys - py) ** 2
        g = np.exp(-d2 / (2 * rad * rad))
        star = np.outer(g, np.array([235, 240, 255])) * a
        img += star
    # 猫爪 constellation（四趾 + 掌）：连线 + 亮星
    pad = W * 0.5
    paw = [
        (pad + 0.00 * W, H * 0.46),
        (pad + 0.10 * W, H * 0.40),
        (pad + 0.20 * W, H * 0.42),
        (pad + 0.14 * W, H * 0.52),
        (pad + 0.09 * W, H * 0.58),
    ]
    # 掌
    palm = (pad + 0.10 * W, H * 0.66)
    pts = paw + [palm]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:] + [pts[0]]):
        steps = 40
        for s in range(steps):
            t = s / steps
            x = int(x0 + (x1 - x0) * t)
            y = int(y0 + (y1 - y0) * t)
            d2 = (xs - x) ** 2 + (ys - y) ** 2
            g = np.exp(-d2 / (2 * 6 * 6))
            img += np.outer(g, color) * 0.25
    for (x, y) in pts:
        add_radial_glow(img, int(x), int(y), 26, np.array([200, 190, 255]), 0.8)
    return img


def finalize(img, name):
    img = np.clip(img, 0, 255).astype(np.uint8)
    pil = Image.fromarray(img, "RGB")
    pil = pil.filter(ImageFilter.GaussianBlur(0.4))
    # 轻微颗粒
    rng = np.random.default_rng(3)
    noise = rng.normal(0, 3, (H, W, 1)).astype(np.int16)
    arr = np.clip(pil.convert("RGB").astype(np.int16) + noise, 0, 255).astype(np.uint8)
    out = Image.fromarray(arr, "RGB")
    path = os.path.join(OUT_DIR, f"home-bg-{name}.png")
    out.save(path, "PNG")
    print("saved", path, out.size)


def build(cfg):
    top = hex2rgb(cfg["top"])
    bot = hex2rgb(cfg["color"])
    img = make_base(top, bot)
    # 主辉光
    add_radial_glow(img, W * cfg["gx"], H * cfg["gy"], cfg["gr"], bot, cfg["gs"])
    add_radial_glow(img, W * 0.5, H * 0.08, 420, top, 0.4)
    # 母题
    getattr(__import__("__main__"), "motif_" + cfg["motif"])(img, bot, *(cfg.get("motif_args", [])))
    if cfg.get("particles", True):
        add_particles(img, bot, n=cfg.get("pn", 60))
    add_vignette(img)
    finalize(img, cfg["name"])


CONFIGS = [
    {
        "name": "violet", "color": "#512BD4", "top": "#7A5CFF",
        "gx": 0.5, "gy": 0.30, "gr": 520, "gs": 0.7,
        "motif": "equalizer", "particles": True, "pn": 70,
    },
    {
        "name": "lavender", "color": "#8C7BFF", "top": "#C9BEFF",
        "gx": 0.5, "gy": 0.42, "gr": 560, "gs": 0.55,
        "motif": "ripples", "motif_args": [W * 0.5, H * 0.42], "particles": True, "pn": 60,
    },
    {
        "name": "cyan", "color": "#55D6FF", "top": "#9CEBFF",
        "gx": 0.5, "gy": 0.32, "gr": 500, "gs": 0.6,
        "motif": "waveform", "particles": True, "pn": 55,
    },
    {
        "name": "pink", "color": "#FF7AAE", "top": "#FFB3CF",
        "gx": 0.5, "gy": 0.50, "gr": 520, "gs": 0.6,
        "motif": "pulse", "motif_args": [W * 0.5, H * 0.50], "particles": True, "pn": 65,
    },
    {
        "name": "indigo", "color": "#190649", "top": "#3A1E8F",
        "gx": 0.35, "gy": 0.25, "gr": 480, "gs": 0.55,
        "motif": "stars", "particles": False,
    },
]

if __name__ == "__main__":
    for c in CONFIGS:
        build(c)
    print("ALL DONE ->", OUT_DIR)
