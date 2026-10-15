"""Draws the Kamehameha HUD icon: a glowing blue energy ball with crackling rays (pixels computed directly).

    blender --background --factory-startup --python blender/Kamehameha/build_kamehameha_icon.py

Output: Assets/Kamehameha/kamehameha_icon.png (256x256, transparent background).
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import common  # noqa: E402

SIZE = 256


def pixel(x, y, rays):
    dx = (x + 0.5) / SIZE * 2 - 1
    dy = (y + 0.5) / SIZE * 2 - 1
    r = math.sqrt(dx * dx + dy * dy)
    a = math.atan2(dy, dx)
    core = max(0.0, 1 - r / 0.28) ** 1.5            # white-hot center
    ball = max(0.0, 1 - r / 0.55) ** 0.8            # blue ball
    glow = max(0.0, 1 - r / 0.95) ** 2.5            # soft halo
    ray = 0.0
    for angle, width, length in rays:               # crackling spikes
        d = abs((a - angle + math.pi) % (2 * math.pi) - math.pi)
        if r < length:
            ray = max(ray, max(0.0, 1 - d / width) * (1 - r / length) * 0.9)
    alpha = min(1.0, core + ball + glow * 0.8 + ray)
    rr = min(1.0, 0.25 * ball + 0.9 * core + 0.3 * glow + 0.5 * ray)
    gg = min(1.0, 0.6 * ball + 0.95 * core + 0.55 * glow + 0.75 * ray)
    bb = min(1.0, 1.0 * ball + 1.0 * core + 0.95 * glow + 1.0 * ray)
    return rr, gg, bb, alpha


def main():
    rng = random.Random(9001)
    rays = [(rng.uniform(-math.pi, math.pi), rng.uniform(0.03, 0.08), rng.uniform(0.7, 0.98)) for _ in range(11)]
    image = bpy.data.images.new("kamehameha_icon", SIZE, SIZE, alpha=True)
    pixels = []
    for y in range(SIZE):
        for x in range(SIZE):
            pixels.extend(pixel(x, y, rays))
    image.pixels = pixels
    path = os.path.join(common.assets_dir("Kamehameha"), "kamehameha_icon.png")
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print(f"RESULT wrote {path}")


main()
