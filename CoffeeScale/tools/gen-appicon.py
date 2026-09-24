#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 AlphaSun 手冲咖啡 App 图标（iOS + MacCatalyst 资源目录）。

做法：用 PIL 在 1024x1024 主图上绘制「暖色咖啡 + 奶霜杯 + 热气」的极简图标，
再按 Apple 各尺寸向下重采样（LANCZOS），写入各自的 AppIcon.appiconset/
（含 Contents.json）。无需外部图片素材，离线可复现。

依赖：Pillow
用法：python tools/gen-appicon.py
"""
import os
import math
import json
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IOS_DIR = os.path.join(ROOT, "src", "CoffeeScale.iOS", "Assets.xcassets", "AppIcon.appiconset")
MAC_DIR = os.path.join(ROOT, "src", "CoffeeScale.MacCatalyst", "Assets.xcassets", "AppIcon.appiconset")
# 桌面版（Windows/Linux/macOS 原生）资源目录：macOS .app bundle 用 icns，Linux .desktop 用 png
DESKTOP_DIR = os.path.join(ROOT, "src", "CoffeeScale.Avalonia", "Assets")

# 暖色「仪式感」调色板（与 DESIGN.md 一致）
CARAMEL = (216, 160, 102)
ESPRESSO = (46, 26, 15)
CREAM = (251, 243, 228)
COFFEE = (60, 35, 20)
STEAM = (255, 246, 234)


def make_master(size: int = 1024) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # 1) 斜向暖色渐变背景（不透明）
    for y in range(size):
        t = y / size
        r = int(CARAMEL[0] * (1 - t) + ESPRESSO[0] * t)
        g = int(CARAMEL[1] * (1 - t) + ESPRESSO[1] * t)
        b = int(CARAMEL[2] * (1 - t) + ESPRESSO[2] * t)
        d.line([(0, y), (size, y)], fill=(r, g, b, 255))

    # 2) 中心柔光（让杯子更突出）
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    cx = cy = size / 2
    gr = int(size * 0.42)
    gd.ellipse([cx - gr, cy - gr, cx + gr, cy + gr], fill=(255, 236, 205, 70))
    glow = glow.filter(ImageFilter.GaussianBlur(size * 0.18))
    img = Image.alpha_composite(img, glow)
    d = ImageDraw.Draw(img)

    s = size
    # 3) 杯托（椭圆）
    saucer_w = s * 0.62
    saucer_h = s * 0.12
    scx, scy = cx, s * 0.66
    d.ellipse([scx - saucer_w / 2, scy - saucer_h / 2,
               scx + saucer_w / 2, scy + saucer_h / 2],
              fill=CREAM + (235,), outline=(120, 85, 55, 90), width=max(1, int(s * 0.004)))

    # 4) 杯身（上宽下窄的圆角梯形）
    cup_top_w = s * 0.46
    cup_bot_w = s * 0.34
    cup_top = s * 0.30
    cup_bot = s * 0.64
    cup_pts = [
        (cx - cup_top_w / 2, cup_top),
        (cx + cup_top_w / 2, cup_top),
        (cx + cup_bot_w / 2, cup_bot),
        (cx - cup_bot_w / 2, cup_bot),
    ]
    d.polygon(cup_pts, fill=CREAM + (255,))
    d.line([cup_pts[0], cup_pts[1]], fill=(120, 85, 55, 110), width=max(2, int(s * 0.008)))

    # 5) 咖啡液面（杯口内的深色椭圆）
    surf_w = cup_top_w * 0.86
    surf_h = s * 0.05
    d.ellipse([cx - surf_w / 2, cup_top - surf_h / 2,
               cx + surf_w / 2, cup_top + surf_h / 2], fill=COFFEE + (255,))

    # 6) 杯耳（右侧弧）
    handle_r = s * 0.10
    hcx = cx + cup_top_w / 2 - s * 0.01
    hcy = (cup_top + cup_bot) / 2
    d.arc([hcx - handle_r, hcy - handle_r * 1.6,
            hcx + handle_r, hcy + handle_r * 1.6],
          start=70, end=290, fill=CREAM + (255,), width=max(6, int(s * 0.035)))

    # 7) 热气（两道半透明波浪）
    for off in (-s * 0.06, s * 0.06):
        x = cx + off
        top = s * 0.12
        bot = cup_top - s * 0.04
        steps = 24
        pts = []
        for i in range(steps + 1):
            ty = top + (bot - top) * i / steps
            wob = math.sin(i / steps * math.pi * 2.2) * s * 0.022
            pts.append((x + wob, ty))
        d.line(pts, fill=STEAM + (150,), width=max(3, int(s * 0.012)), joint="curve")

    # 压平为不透明：用同一张渐变背景作为底色合成，再转 RGB。
    # App Store 要求 1024 图标无 alpha；其余小图标同样保持不透明，避免 iOS 生成时警告。
    bg = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bd = ImageDraw.Draw(bg)
    for y in range(size):
        t = y / size
        r = int(CARAMEL[0] * (1 - t) + ESPRESSO[0] * t)
        g = int(CARAMEL[1] * (1 - t) + ESPRESSO[1] * t)
        b = int(CARAMEL[2] * (1 - t) + ESPRESSO[2] * t)
        bd.line([(0, y), (size, y)], fill=(r, g, b, 255))
    img = Image.alpha_composite(bg, img).convert("RGB")
    return img


def downscale(master: Image.Image, px: int) -> Image.Image:
    return master.resize((px, px), Image.LANCZOS)


# iOS 通用图标集（Xcode 14+ universal 格式）
IOS_IMAGES = [
    ("icon-20@2x.png", 40),
    ("icon-20@3x.png", 60),
    ("icon-29.png", 29),
    ("icon-29@2x.png", 58),
    ("icon-29@3x.png", 87),
    ("icon-40.png", 40),
    ("icon-40@2x.png", 80),
    ("icon-40@3x.png", 120),
    ("icon-60@2x.png", 120),
    ("icon-60@3x.png", 180),
    ("icon-76.png", 76),
    ("icon-76@2x.png", 152),
    ("icon-83.5@2x.png", 167),
    ("icon-1024.png", 1024),
]

# iOS 图标集（经典 iphone / ipad / ios-marketing 三档 idiom）
# ⚠️ 不要用 "idiom": "universal"：那是 Xcode 14+ 的新格式，actool 在
#    --minimum-deployment-target 14.0 下不认这套描述，会静默不产出 Assets.car
#    （2026-09-25 首个 IPA 装真机是空白图标，即此坑）。经典 idiom 对 iOS 14+ 始终有效。
IOS_CONTENTS = {
    "images": [
        # iPhone
        {"idiom": "iphone", "size": "20x20", "scale": "2x", "filename": "icon-20@2x.png"},
        {"idiom": "iphone", "size": "20x20", "scale": "3x", "filename": "icon-20@3x.png"},
        {"idiom": "iphone", "size": "29x29", "scale": "1x", "filename": "icon-29.png"},
        {"idiom": "iphone", "size": "29x29", "scale": "2x", "filename": "icon-29@2x.png"},
        {"idiom": "iphone", "size": "29x29", "scale": "3x", "filename": "icon-29@3x.png"},
        {"idiom": "iphone", "size": "40x40", "scale": "2x", "filename": "icon-40@2x.png"},
        {"idiom": "iphone", "size": "40x40", "scale": "3x", "filename": "icon-40@3x.png"},
        {"idiom": "iphone", "size": "60x60", "scale": "2x", "filename": "icon-60@2x.png"},
        {"idiom": "iphone", "size": "60x60", "scale": "3x", "filename": "icon-60@3x.png"},
        # iPad
        {"idiom": "ipad", "size": "29x29", "scale": "1x", "filename": "icon-29.png"},
        {"idiom": "ipad", "size": "29x29", "scale": "2x", "filename": "icon-29@2x.png"},
        {"idiom": "ipad", "size": "40x40", "scale": "1x", "filename": "icon-40.png"},
        {"idiom": "ipad", "size": "40x40", "scale": "2x", "filename": "icon-40@2x.png"},
        {"idiom": "ipad", "size": "76x76", "scale": "1x", "filename": "icon-76.png"},
        {"idiom": "ipad", "size": "76x76", "scale": "2x", "filename": "icon-76@2x.png"},
        {"idiom": "ipad", "size": "83.5x83.5", "scale": "2x", "filename": "icon-83.5@2x.png"},
        # App Store
        {"idiom": "ios-marketing", "size": "1024x1024", "scale": "1x", "filename": "icon-1024.png"},
    ],
    "info": {"author": "xcode", "version": 1},
}

# MacCatalyst 图标集（mac idiom）
MAC_IMAGES = [
    ("icon-16.png", 16),
    ("icon-16@2x.png", 32),
    ("icon-32.png", 32),
    ("icon-32@2x.png", 64),
    ("icon-128.png", 128),
    ("icon-128@2x.png", 256),
    ("icon-256.png", 256),
    ("icon-256@2x.png", 512),
    ("icon-512.png", 512),
    ("icon-512@2x.png", 1024),
]

MAC_CONTENTS = {
    "images": [
        {"idiom": "mac", "size": "16x16", "scale": "1x", "filename": "icon-16.png"},
        {"idiom": "mac", "size": "16x16", "scale": "2x", "filename": "icon-16@2x.png"},
        {"idiom": "mac", "size": "32x32", "scale": "1x", "filename": "icon-32.png"},
        {"idiom": "mac", "size": "32x32", "scale": "2x", "filename": "icon-32@2x.png"},
        {"idiom": "mac", "size": "128x128", "scale": "1x", "filename": "icon-128.png"},
        {"idiom": "mac", "size": "128x128", "scale": "2x", "filename": "icon-128@2x.png"},
        {"idiom": "mac", "size": "256x256", "scale": "1x", "filename": "icon-256.png"},
        {"idiom": "mac", "size": "256x256", "scale": "2x", "filename": "icon-256@2x.png"},
        {"idiom": "mac", "size": "512x512", "scale": "1x", "filename": "icon-512.png"},
        {"idiom": "mac", "size": "512x512", "scale": "2x", "filename": "icon-512@2x.png"},
    ],
    "info": {"author": "xcode", "version": 1},
}


def write_set(img_dir, images, contents):
    os.makedirs(img_dir, exist_ok=True)
    master = make_master(1024)
    cache = {}
    for fname, px in images:
        if px not in cache:
            cache[px] = downscale(master, px)
        cache[px].save(os.path.join(img_dir, fname), "PNG")
        print("  wrote %s (%dx%d)" % (fname, px, px))
    with open(os.path.join(img_dir, "Contents.json"), "w", encoding="utf-8") as f:
        json.dump(contents, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("  wrote Contents.json (%d icons)" % len(images))


def write_desktop_icons(master: Image.Image):
    """桌面版图标：macOS .app 用 AppIcon.icns，Linux .desktop / 窗口图标用 PNG。"""
    os.makedirs(DESKTOP_DIR, exist_ok=True)
    icns = os.path.join(DESKTOP_DIR, "AppIcon.icns")
    master.save(icns, format="ICNS",
                sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
    print("  wrote AppIcon.icns")
    for px in (256, 512):
        p = os.path.join(DESKTOP_DIR, "icon-%d.png" % px)
        downscale(master, px).save(p, "PNG")
        print("  wrote icon-%d.png" % px)


if __name__ == "__main__":
    print("==> iOS AppIcon set")
    write_set(IOS_DIR, IOS_IMAGES, IOS_CONTENTS)
    print("==> MacCatalyst AppIcon set")
    write_set(MAC_DIR, MAC_IMAGES, MAC_CONTENTS)
    print("==> Desktop icons (macOS icns + Linux png)")
    write_desktop_icons(make_master(1024))
    print("done.")
