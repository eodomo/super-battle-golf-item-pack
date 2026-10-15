"""Builds the Sniper Rifle model: a recreation of Halo's SRS99 anti-materiel sniper rifle.

    blender --background --factory-startup --python blender/SniperRifle/build_sniper_rifle.py [-- --preview <png>]

Modeled from scratch to match the SRS99's silhouette: long boxy bullpup receiver with a two-tone finish,
thumbhole stock with cheek rest, magazine behind the pistol grip, angular barrel shroud, long exposed
barrel with a ported box muzzle brake, and a big high-mounted scope with a boxy eyepiece and side module.

Outputs Assets/SniperRifle/sniper_rifle.obj/.mtl/_icon.png and blender/SniperRifle/sniper_rifle.blend.
Forward is +Y (Blender), up is +Z, origin at the top of the grip.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common  # noqa: E402
from mathutils import Matrix  # noqa: E402

KEY = "SniperRifle"
NAME = "sniper_rifle"
COLORS = {
    "Dark": (0.16, 0.17, 0.18),
    "Light": (0.44, 0.46, 0.48),
    "Black": (0.06, 0.06, 0.07),
    "Steel": (0.56, 0.58, 0.61),
    "Lens": (0.16, 0.50, 0.62),
}
ORDER = ["Dark", "Light", "Black", "Steel", "Lens"]
DARK, LIGHT, BLACK, STEEL, LENS = range(5)


def along_y(x, y, z):
    return Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(-90), 4, "X")


def along_x(x, y, z):
    return Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(90), 4, "Y")


def at(x, y, z, tilt_deg=0.0):
    return Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(tilt_deg), 4, "X")


def build():
    p = []
    box, prism, cyl = common.box, common.prism, common.cylinder

    # --- Receiver: long boxy bullpup body, sloping down at the back, light grey side panels.
    p.append(prism([(-0.13, 0.02), (0.62, 0.02), (0.62, 0.105), (0.52, 0.14), (0.04, 0.14), (-0.13, 0.12)], 0.078, DARK, bevel=0.006))
    for side in (-1, 1):
        p.append(prism([(0.07, 0.042), (0.47, 0.042), (0.47, 0.1), (0.1, 0.1)], 0.004, LIGHT, x=side * 0.04))
        p.append(box((0.004, 0.09, 0.022), at(side * 0.041, -0.04, 0.085), BLACK))  # ejection port / vents
    p.append(box((0.04, 0.46, 0.018), at(0, 0.24, 0.149), BLACK, bevel=0.003))       # top rail
    for i in range(10):
        p.append(box((0.042, 0.012, 0.006), at(0, 0.03 + i * 0.045, 0.159), DARK))    # rail teeth
    p.append(cyl(0.012, 0.012, 0.03, along_x(-0.052, 0.0, 0.09), 12, STEEL))          # charging handle
    p.append(common.sphere(0.014, Matrix.Translation((-0.068, 0.0, 0.09)), 10, 8, STEEL))

    # --- Barrel shroud: angular, with lightening cut-outs, then the long exposed barrel.
    p.append(prism([(0.6, 0.03), (1.0, 0.042), (1.0, 0.094), (0.6, 0.108)], 0.066, DARK, bevel=0.005))
    for y in (0.7, 0.82, 0.94):
        for side in (-1, 1):
            p.append(box((0.004, 0.06, 0.03), at(side * 0.033, y, 0.07), BLACK))
    p.append(box((0.046, 0.36, 0.03), at(0, 0.79, 0.012), DARK, bevel=0.005))          # under-barrel spar
    p.append(cyl(0.031, 0.031, 0.07, along_y(0, 1.03, 0.068), 20, DARK))               # barrel collar
    p.append(cyl(0.024, 0.024, 0.26, along_y(0, 1.17, 0.068), 20, STEEL))              # barrel
    # --- Muzzle brake: big ported box with a cap.
    p.append(box((0.076, 0.17, 0.062), at(0, 1.36, 0.068), DARK, bevel=0.008))
    for y in (1.305, 1.355, 1.405):
        for side in (-1, 1):
            p.append(box((0.006, 0.028, 0.034), at(side * 0.037, y, 0.068), BLACK))
    p.append(cyl(0.026, 0.026, 0.012, along_y(0, 1.452, 0.068), 18, STEEL))
    p.append(cyl(0.012, 0.012, 0.014, along_y(0, 1.462, 0.068), 12, BLACK))

    # --- Pistol grip ahead of the magazine (bullpup), trigger and guard.
    p.append(prism([(0.14, 0.025), (0.205, 0.025), (0.17, -0.13), (0.1, -0.13)], 0.046, BLACK, bevel=0.006))
    p.append(box((0.03, 0.12, 0.012), at(0, 0.26, -0.055), DARK, bevel=0.003))
    p.append(box((0.03, 0.012, 0.08), at(0, 0.315, -0.018), DARK, bevel=0.003))
    p.append(box((0.008, 0.012, 0.035), at(0, 0.235, -0.01, 15), STEEL))
    # --- Magazine behind the grip, with a light base plate.
    p.append(box((0.052, 0.1, 0.16), at(0, 0.02, -0.06, -8), DARK, bevel=0.006))
    p.append(box((0.058, 0.108, 0.016), at(0, 0.031, -0.142, -8), LIGHT, bevel=0.004))

    # --- Thumbhole stock: upper beam with cheek rest, lower beam, butt with rubber pad.
    p.append(prism([(-0.12, 0.12), (-0.12, 0.06), (-0.6, 0.075), (-0.6, 0.13), (-0.2, 0.135)], 0.066, DARK, bevel=0.006))
    p.append(prism([(-0.13, 0.03), (-0.13, -0.1), (-0.21, -0.1), (-0.21, 0.03)], 0.066, DARK, bevel=0.006))
    p.append(prism([(-0.21, -0.065), (-0.6, -0.07), (-0.6, -0.115), (-0.21, -0.1)], 0.06, DARK, bevel=0.005))
    p.append(box((0.05, 0.3, 0.03), at(0, -0.39, 0.142), LIGHT, bevel=0.008))         # cheek rest
    p.append(prism([(-0.6, 0.135), (-0.6, -0.12), (-0.645, -0.125), (-0.645, 0.14)], 0.07, DARK, bevel=0.006))
    p.append(box((0.074, 0.02, 0.27), at(0, -0.655, 0.008), BLACK, bevel=0.006))      # butt pad

    # --- Scope: high mounts, big tube, flared objective, boxy eyepiece, side module, turret.
    sz = 0.245
    for y in (0.07, 0.36):
        p.append(box((0.034, 0.04, 0.09), at(0, y, 0.19), DARK, bevel=0.004))
        p.append(cyl(0.046, 0.046, 0.03, along_y(0, y, sz), 22, BLACK))               # rings
    p.append(cyl(0.04, 0.04, 0.44, along_y(0, 0.2, sz), 26, DARK))
    p.append(cyl(0.042, 0.062, 0.09, along_y(0, 0.465, sz), 28, DARK))
    p.append(cyl(0.064, 0.064, 0.02, along_y(0, 0.52, sz), 28, BLACK))
    p.append(cyl(0.056, 0.056, 0.006, along_y(0, 0.53, sz), 28, LENS))
    p.append(box((0.076, 0.11, 0.084), at(0, -0.06, sz), DARK, bevel=0.012))         # boxy eyepiece
    p.append(cyl(0.03, 0.03, 0.03, along_y(0, -0.125, sz), 20, BLACK))
    p.append(cyl(0.024, 0.024, 0.005, along_y(0, -0.141, sz), 20, LENS))
    p.append(box((0.026, 0.16, 0.05), at(0.052, 0.18, sz), LIGHT, bevel=0.006))       # side module
    p.append(cyl(0.012, 0.012, 0.006, along_x(0.066, 0.24, sz), 14, LENS))
    p.append(cyl(0.02, 0.02, 0.035, Matrix.Translation((0, 0.2, sz + 0.055)), 16, BLACK))  # elevation turret
    p.append(cyl(0.02, 0.02, 0.03, along_x(-0.055, 0.2, sz), 16, BLACK))              # windage turret
    return p


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = argv[argv.index("--preview") + 1] if "--preview" in argv else None

    scene = common.fresh_scene("SniperRifle")
    materials = common.make_materials("SR_", COLORS, ORDER, roughness=0.38)
    rifle = common.merge(scene, "SniperRifle", build(), materials)
    assets = common.assets_dir(KEY)
    tris = common.export_obj(rifle, os.path.join(assets, f"{NAME}.obj"), os.path.join(assets, f"{NAME}.mtl"),
                             COLORS, ORDER, "Sniper Rifle (SRS99 recreation) - generated by build_sniper_rifle.py")
    # Icon only (already exported): tilt the rifle so it lies diagonally across the square icon.
    rifle.rotation_euler = (math.radians(32), 0.0, 0.0)
    common.render_icon(scene, os.path.join(assets, f"{NAME}_icon.png"), ortho_scale=1.85,
                       look_at=(0, 0.33, 0.27), cam_offset=(1.5, -0.35, 0.25))
    if preview:
        rifle.rotation_euler = (0.0, 0.0, 0.0)
        common.render_icon(scene, preview, ortho_scale=2.4, look_at=(0, 0.45, 0.06), cam_offset=(1.6, -0.25, 0.3), size=1024)
    rifle.rotation_euler = (0.0, 0.0, 0.0)
    common.save_blend(os.path.join(common.ROOT, "blender", KEY, f"{NAME}.blend"), scene)
    d = rifle.dimensions
    print(f"RESULT {NAME}: {tris} triangles, dimensions {d.x:.3f} x {d.y:.3f} x {d.z:.3f} m")


main()
