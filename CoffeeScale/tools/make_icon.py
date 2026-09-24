"""生成 CoffeeScale 咖啡杯应用图标（多分辨率 .ico）。
用法：python tools/make_icon.py
产物：src/CoffeeScale.Avalonia/app.ico（Win32 EXE 图标）、src/CoffeeScale.UI/Assets/app.ico（Avalonia 窗口图标资源）。
"""
import os
import math
from PIL import Image, ImageDraw


def draw_coffee_icon(size: int) -> Image.Image:
    scale = max(4, 1024 // size)
    S = size * scale
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = S / 2

    top_w, bot_w, cup_h = S * 0.44, S * 0.34, S * 0.42
    top_y = S * 0.34
    bot_y = top_y + cup_h
    tl = (cx - top_w / 2, top_y)
    tr = (cx + top_w / 2, top_y)
    bl = (cx - bot_w / 2, bot_y)
    br = (cx + bot_w / 2, bot_y)
    body = (236, 226, 210, 255)
    body_dark = (210, 196, 178, 255)
    outline = (120, 84, 50, 255)
    crema = (130, 92, 60, 255)
    coffee = (62, 38, 22, 255)

    # 托盘（杯碟）
    sw, sh, sy = S * 0.62, S * 0.07, bot_y + S * 0.01
    d.ellipse([cx - sw / 2, sy - sh / 2, cx + sw / 2, sy + sh * 1.5],
              fill=body_dark, outline=outline, width=max(2, S // 256))

    # 杯身
    d.polygon([tl, tr, br, bl], fill=body)

    rim_h, inset = S * 0.07, S * 0.025

    def rim():
        d.ellipse([tl[0], top_y - rim_h / 2, tr[0], top_y + rim_h],
                  fill=body, outline=outline, width=max(2, S // 256))
        d.ellipse([tl[0] + inset, top_y - rim_h / 2 + inset,
                   tr[0] - inset, top_y + rim_h - inset], fill=coffee)
        d.ellipse([tl[0] + inset * 2.5, top_y - rim_h / 2 + inset * 2,
                   tr[0] - inset * 4, top_y + rim_h - inset * 2], fill=crema)

    rim()

    # 杯柄（右侧 C 形弧）
    hcx, hcy, hr = tr[0] + S * 0.02, top_y + cup_h * 0.45, S * 0.10
    th = max(3, S // 170)
    bbox = [hcx - hr, hcy - hr, hcx + hr, hcy + hr]
    d.arc(bbox, start=-30, end=210, fill=body, width=th)
    d.arc(bbox, start=-30, end=210, fill=outline, width=max(1, th - 4))

    # 蒸汽（三缕波浪线）
    steam = (180, 170, 160, 200)
    sw = max(2, S // 200)
    for k, dx in enumerate((-0.16, 0, 0.16)):
        bx, by, amp = cx + dx * S, top_y - rim_h * 0.3, S * 0.018
        pts = [(bx + amp * math.sin(t * 0.9 + k), by - t * (S * 0.012)) for t in range(18)]
        for i in range(len(pts) - 1):
            d.line([pts[i], pts[i + 1]], fill=steam, width=sw)

    return img.resize((size, size), Image.LANCZOS)


def main():
    base = draw_coffee_icon(256)
    sizes = [(s, s) for s in (16, 24, 32, 48, 64, 128, 256)]
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    targets = [
        os.path.join(root, "src", "CoffeeScale.Avalonia", "app.ico"),
        os.path.join(root, "src", "CoffeeScale.UI", "Assets", "app.ico"),
    ]
    for p in targets:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        base.save(p, format="ICO", sizes=sizes)
        print(f"OK {p} ({os.path.getsize(p)} bytes)")


if __name__ == "__main__":
    main()
