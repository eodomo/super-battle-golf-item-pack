"""Renders the menu icons for the Ranger suit and boots (small stylised models built from primitives).

Run it through the Blender MCP (execute_blender_code with runpy.run_path) or headless:

    blender --background --factory-startup --python blender/PowerRanger/build_icons.py

Outputs Assets/PowerRanger/suit_icon.png and boots_icon.png.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import common  # noqa: E402
from mathutils import Matrix  # noqa: E402

KEY = "PowerRanger"
COLORS = {
    "Suit": (0.86, 0.09, 0.11),
    "White": (0.90, 0.91, 0.94),
    "Gold": (0.93, 0.72, 0.16),
    "Silver": (0.72, 0.76, 0.82),
}
ORDER = ["Suit", "White", "Gold", "Silver"]
SUIT, WHITE, GOLD, SILVER = range(4)


def at(x=0.0, y=0.0, z=0.0):
    return Matrix.Translation((x, y, z))


def limb(x0, z0, x1, z1, radius, material, y=0.0):
    """A cylinder from (x0, z0) to (x1, z1) in the front plane."""
    dx, dz = x1 - x0, z1 - z0
    length = math.hypot(dx, dz)
    angle = math.atan2(dx, dz)  # rotation about Y that tips +Z towards +X
    m = at((x0 + x1) / 2, y, (z0 + z1) / 2) @ Matrix.Rotation(angle, 4, "Y")
    return common.cylinder(radius, radius, length, m, segments=20, material=material)


def suit_parts():
    parts = []
    # Torso, belt and buckle.
    parts.append(common.box((0.30, 0.17, 0.30), at(z=0.0), material=SUIT, bevel=0.04))
    parts.append(common.box((0.31, 0.175, 0.045), at(z=-0.14), material=GOLD, bevel=0.01))
    parts.append(common.box((0.07, 0.03, 0.06), at(y=-0.09, z=-0.14), material=SILVER, bevel=0.008))
    # White chevron on the chest (two bars meeting in a V).
    for sign in (-1, 1):
        m = at(sign * 0.065, -0.088, 0.065) @ Matrix.Rotation(-sign * math.radians(38), 4, "Y")
        parts.append(common.box((0.15, 0.012, 0.035), m, material=WHITE, bevel=0.004))
    # Arms with white gloves.
    for sign in (-1, 1):
        parts.append(limb(sign * 0.17, 0.10, sign * 0.30, -0.10, 0.052, SUIT))
        parts.append(common.sphere(0.064, at(sign * 0.315, 0, -0.13), segments=20, rings=12, material=WHITE))
        parts.append(limb(sign * 0.285, -0.075, sign * 0.305, -0.11, 0.058, GOLD))
    # Legs.
    for sign in (-1, 1):
        parts.append(limb(sign * 0.07, -0.16, sign * 0.08, -0.40, 0.065, SUIT))
    return parts


def boots_parts():
    """One boot seen from the side (toes to the right)."""
    parts = []
    for y, dx, dz in ((0.0, 0.0, 0.0),):
        parts.append(common.cylinder(0.082, 0.082, 0.09, at(dx - 0.05, y, dz + 0.075), segments=24, material=SUIT))     # cuff
        parts.append(common.cylinder(0.086, 0.086, 0.015, at(dx - 0.05, y, dz + 0.03), segments=24, material=GOLD))    # gold band
        parts.append(common.box((0.28, 0.15, 0.16), at(dx, y, dz - 0.03), material=WHITE, bevel=0.035))                # boot body
        parts.append(common.sphere(0.08, at(dx + 0.12, y, dz - 0.06), segments=20, rings=12, material=WHITE))          # toe
        parts.append(common.box((0.35, 0.17, 0.03), at(dx + 0.03, y, dz - 0.125), material=SILVER, bevel=0.012))       # sole
    return parts




def render(scene, name, parts, materials, size, look_at, scale, tilt):
    obj = common.merge(scene, name, parts, materials, flat_materials=())
    obj.rotation_euler = tilt
    common.render_icon(scene, os.path.join(common.assets_dir(KEY), f"{name}_icon.png"), ortho_scale=scale,
                       look_at=look_at, cam_offset=(0, -1.0, 0.05), size=size)
    bpy.data.objects.remove(obj, do_unlink=True)


def main():
    scene = common.fresh_scene("RangerIcons")
    materials = common.make_materials("PRI_", COLORS, ORDER, roughness=0.4)
    render(scene, "suit", suit_parts(), materials, 256, (0, 0, -0.02), 0.95, (math.radians(-8), 0, math.radians(-16)))
    materials = common.make_materials("PRI_", COLORS, ORDER, roughness=0.4)
    render(scene, "boots", boots_parts(), materials, 256, (0.0, 0, 0.0), 0.5, (math.radians(-12), 0, math.radians(-32)))
    print("RESULT icons done")


main()
