"""Builds the Steam Whistle models: the brass whistle (held item + icon) and the winged tank engine dragon.

    blender --background --factory-startup --python blender/SteamWhistle/build_steam_whistle.py [-- --preview <png>]

Outputs in Assets/SteamWhistle:
  steam_whistle.obj/.mtl/_icon.png       held whistle and HUD icon
  thomas_dragon.obj/.mtl                 the engine body (with tail, horns and spines)
  thomas_wing_left.obj / _right.obj      wings, modeled around their hinge so the game can flap them

The dragon faces -Y in Blender (= +Z, forward, in Unity). Wing hinges and the mouth are mirrored in
src/Items/SteamWhistle/DragonAttacks.cs (Unity = (-x, z, -y) of the Blender values below).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import common  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

KEY = "SteamWhistle"
WING_HINGE = Vector((0.78, -0.2, 2.15))  # left wing (Blender +X side); right wing mirrors X
MOUTH = Vector((0.0, -2.3, 1.2))

COLORS = {
    "Blue": (0.13, 0.36, 0.80),
    "Red": (0.82, 0.10, 0.08),
    "Black": (0.08, 0.08, 0.09),
    "Charcoal": (0.24, 0.24, 0.26),
    "Face": (0.80, 0.80, 0.78),
    "White": (0.97, 0.97, 0.95),
    "Yellow": (0.96, 0.78, 0.15),
    "Grey": (0.55, 0.57, 0.60),
    "Dragon": (0.50, 0.07, 0.08),
    "Bone": (0.20, 0.14, 0.14),
}
DRAGON_ORDER = ["Blue", "Red", "Black", "Charcoal", "Face", "White", "Yellow", "Grey", "Dragon", "Bone"]
BLUE, RED, BLACK, CHARCOAL, FACE, WHITE, YELLOW, GREY, DRAGON, BONE = range(10)

WHISTLE_COLORS = {
    "Brass": (0.88, 0.66, 0.24),
    "DarkBrass": (0.45, 0.32, 0.12),
    "Handle": (0.80, 0.12, 0.08),
}
WHISTLE_ORDER = ["Brass", "DarkBrass", "Handle"]


def along(axis, x, y, z):
    """Matrix placing a cone/cylinder (built along Z) along the given axis, centered at (x, y, z)."""
    rot = {"X": Matrix.Rotation(math.radians(90), 4, "Y"), "Y": Matrix.Rotation(math.radians(-90), 4, "X"), "Z": Matrix.Identity(4)}[axis]
    return Matrix.Translation((x, y, z)) @ rot


def squash(x, y, z, sx, sy, sz):
    return Matrix.Translation((x, y, z)) @ Matrix.Diagonal((sx, sy, sz, 1.0))


def segment(a, b, r1, r2, material, segments=10):
    """Tapered cylinder from point a to point b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    return common.cylinder(r1, r2, d.length, Matrix.Translation((a + b) / 2) @ rot, segments=segments, material=material)


def build_body():
    parts = []
    # Frame, buffer beams and buffers.
    parts.append(common.box((1.7, 3.95, 0.15), Matrix.Translation((0, -0.07, 0.62)), BLACK))
    for y in (-2.12, 1.97):
        parts.append(common.box((1.8, 0.15, 0.32), Matrix.Translation((0, y, 0.6)), RED))
        for x in (-0.6, 0.6):
            parts.append(common.cylinder(0.09, 0.12, 0.22, along("Y", x, y + (-0.17 if y < 0 else 0.17), 0.6), 14, GREY))
    # Wheels (Thomas has blue wheels) with grey hubs.
    for y in (-1.3, -0.2, 0.9):
        for x in (-0.72, 0.72):
            parts.append(common.cylinder(0.42, 0.42, 0.12, along("X", x, y, 0.42), 24, BLUE))
            parts.append(common.cylinder(0.12, 0.12, 0.14, along("X", x * 1.02, y, 0.42), 12, GREY))
    # Boiler, smokebox and the face.
    parts.append(common.cylinder(0.55, 0.55, 2.15, along("Y", 0, -0.68, 1.45), 32, BLUE))
    parts.append(common.cylinder(0.58, 0.58, 0.45, along("Y", 0, -1.98, 1.45), 32, CHARCOAL))
    parts.append(common.cylinder(0.5, 0.5, 0.04, along("Y", 0, -2.22, 1.45), 32, FACE))
    # Face: eyes, pupils, angry brows, nose, open mouth (for breathing fire), cheeks.
    for side in (-1, 1):
        x = 0.17 * side
        parts.append(common.sphere(0.11, squash(x, -2.25, 1.6, 1, 0.55, 1), 16, 10, WHITE))
        parts.append(common.sphere(0.05, squash(x * 0.9, -2.31, 1.58, 1, 0.6, 1), 12, 8, BLACK))
        brow = Matrix.Translation((x, -2.27, 1.76)) @ Matrix.Rotation(math.radians(-20 * side), 4, "Y")
        parts.append(common.box((0.2, 0.04, 0.045), brow, CHARCOAL))
        parts.append(common.sphere(0.09, squash(0.3 * side, -2.23, 1.3, 1, 0.45, 0.8), 12, 8, FACE))
    parts.append(common.sphere(0.075, squash(0, -2.29, 1.44, 1, 0.8, 1), 12, 8, FACE))
    parts.append(common.sphere(0.16, squash(0, -2.25, 1.2, 1.2, 0.35, 0.6), 16, 10, BLACK))
    # Funnel (flared) and dome.
    parts.append(common.cylinder(0.16, 0.16, 0.45, along("Z", 0, -1.55, 2.15), 20, BLACK))
    parts.append(common.cylinder(0.16, 0.24, 0.14, along("Z", 0, -1.55, 2.44), 20, BLACK))
    parts.append(common.sphere(0.26, squash(0, -0.55, 1.98, 1, 1, 0.75), 18, 10, BLUE))
    # Side tanks with red lining and a yellow number one.
    for side in (-1, 1):
        parts.append(common.box((0.4, 1.95, 1.15), Matrix.Translation((0.65 * side, -0.62, 1.27)), BLUE))
        for z in (0.78, 1.76):
            parts.append(common.box((0.02, 1.85, 0.035), Matrix.Translation((0.851 * side, -0.62, z)), RED))
        for y in (-1.53, 0.29):
            parts.append(common.box((0.02, 0.035, 0.95), Matrix.Translation((0.851 * side, y, 1.27)), RED))
        parts.append(common.box((0.02, 0.07, 0.45), Matrix.Translation((0.856 * side, -0.62, 1.27)), YELLOW))
        one_flag = Matrix.Translation((0.856 * side, -0.68 if side > 0 else -0.56, 1.44)) @ Matrix.Rotation(math.radians(45 * side), 4, "X")
        parts.append(common.box((0.02, 0.06, 0.16), one_flag, YELLOW))
    # Cab with roof and windows, coal bunker.
    parts.append(common.box((1.7, 1.25, 1.6), Matrix.Translation((0, 0.98, 1.5)), BLUE))
    parts.append(common.box((1.85, 1.45, 0.1), Matrix.Translation((0, 0.98, 2.35)), BLACK))
    for side in (-1, 1):
        parts.append(common.box((0.03, 0.6, 0.45), Matrix.Translation((0.851 * side, 0.98, 1.9)), CHARCOAL))
        parts.append(common.box((0.35, 0.03, 0.35), Matrix.Translation((0.45 * side, 0.344, 1.95)), CHARCOAL))
    parts.append(common.box((1.6, 0.35, 0.9), Matrix.Translation((0, 1.78, 1.15)), BLUE))

    # Dragon bits: horns on the smokebox, spines along the back, a long tail.
    for side in (-1, 1):
        parts.append(segment((0.3 * side, -1.95, 1.9), (0.5 * side, -1.75, 2.45), 0.09, 0.015, BONE))
    for i, y in enumerate((-1.2, -0.85, -0.2, 0.15, 0.5, 0.85, 1.2)):
        z = 2.0 if y < 0.35 else 2.4
        h = 0.32 if i % 2 == 0 else 0.24
        parts.append(segment((0, y, z - 0.05), (0, y + 0.12, z + h), 0.08, 0.01, DRAGON, 8))
    tail = [(0, 1.95, 1.4), (0, 2.6, 1.2), (0, 3.2, 1.25), (0, 3.75, 1.55), (0, 4.15, 2.0)]
    for i in range(len(tail) - 1):
        parts.append(segment(tail[i], tail[i + 1], 0.24 - i * 0.05, 0.19 - i * 0.05, DRAGON, 12))
    spade = bmesh.new()
    tip = Vector(tail[-1])
    verts = [spade.verts.new(tip + Vector(v)) for v in ((0, -0.05, 0), (0.22, 0.15, 0.2), (0, 0.55, 0.45), (-0.22, 0.15, 0.2))]
    spade.faces.new(verts)
    bmesh.ops.solidify(spade, geom=list(spade.faces), thickness=0.05)
    parts.append(common.set_material(spade, DRAGON))
    return parts


def build_wing(side):
    """A bat wing spreading out from its hinge at the origin along +X (left) or -X (right)."""
    s = 1 if side == "left" else -1
    wrist = Vector((1.5 * s, -0.35, 0.55))
    tips = [Vector((4.1 * s, -0.75, 0.45)), Vector((3.7 * s, 0.55, 0.05)), Vector((2.7 * s, 1.45, -0.2)), Vector((1.3 * s, 1.75, -0.25))]
    root_trail = Vector((0.0, 1.1, 0.0))
    outline = []
    for i, tip in enumerate(tips):
        outline.append(tip)
        nxt = tips[i + 1] if i + 1 < len(tips) else root_trail
        mid = (tip + nxt) / 2
        outline.append(wrist + (mid - wrist) * 0.8)  # scalloped trailing edge
    outline.append(root_trail)

    membrane = bmesh.new()
    w = membrane.verts.new(wrist)
    h = membrane.verts.new(Vector((0, 0, 0)))
    ring = [membrane.verts.new(p) for p in outline]
    for a, b in zip(ring, ring[1:]):
        membrane.faces.new((w, a, b))
    membrane.faces.new((h, w, ring[-1]))
    bmesh.ops.solidify(membrane, geom=list(membrane.faces), thickness=0.05)
    parts = [common.set_material(membrane, DRAGON)]

    parts.append(segment((0, 0, 0), wrist, 0.13, 0.09, BONE))
    for i, tip in enumerate(tips):
        parts.append(segment(wrist, tip, 0.07, 0.025, BONE, 8))
    claw_dir = (tips[0] - wrist).normalized()
    parts.append(segment(tips[0], tips[0] + claw_dir * 0.35 + Vector((0, -0.1, 0.1)), 0.04, 0.0, BLACK, 8))
    return parts


def build_whistle():
    parts = []
    parts.append(common.cylinder(0.016, 0.016, 0.09, along("Z", 0, 0, 0.045), 14, 0))       # pipe from the hand
    parts.append(common.cylinder(0.03, 0.03, 0.02, along("Z", 0, 0, 0.1), 18, 1))          # valve body
    parts.append(common.cylinder(0.034, 0.034, 0.13, along("Z", 0, 0, 0.18), 22, 0))       # bell
    parts.append(common.cylinder(0.037, 0.037, 0.012, along("Z", 0, 0, 0.125), 22, 1))      # band
    parts.append(common.box((0.036, 0.008, 0.016), Matrix.Translation((0, -0.033, 0.145)), 1))  # mouth slit
    parts.append(common.sphere(0.036, squash(0, 0, 0.25, 1, 1, 0.6), 18, 10, 0))           # dome cap
    parts.append(common.cylinder(0.006, 0.006, 0.04, along("Z", 0, 0, 0.285), 8, 1))       # finial
    lever = Matrix.Translation((0.06, 0, 0.11)) @ Matrix.Rotation(math.radians(70), 4, "Y")
    parts.append(common.box((0.01, 0.012, 0.11), lever, 1))
    parts.append(common.sphere(0.016, Matrix.Translation((0.11, 0, 0.13)), 10, 8, 2))       # lever knob
    return parts


def export(obj, name, colors, order, title):
    assets = common.assets_dir(KEY)
    return common.export_obj(obj, os.path.join(assets, f"{name}.obj"), os.path.join(assets, f"{name}.mtl"), colors, order, title)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = argv[argv.index("--preview") + 1] if "--preview" in argv else None
    report = []

    scene = common.fresh_scene("SteamWhistle")
    whistle_mats = common.make_materials("SW_", WHISTLE_COLORS, WHISTLE_ORDER, roughness=0.3)
    whistle = common.merge(scene, "SteamWhistle", build_whistle(), whistle_mats)
    report.append(("steam_whistle", export(whistle, "steam_whistle", WHISTLE_COLORS, WHISTLE_ORDER, "Steam Whistle")))
    common.render_icon(scene, os.path.join(common.assets_dir(KEY), "steam_whistle_icon.png"), ortho_scale=0.36,
                       look_at=(0.02, 0, 0.15), cam_offset=(0.9, -0.9, 0.35))

    dragon_scene = common.fresh_scene("ThomasDragon")
    mats = common.make_materials("TD_", COLORS, DRAGON_ORDER, roughness=0.45)
    body = common.merge(dragon_scene, "ThomasDragon", build_body(), mats)
    report.append(("thomas_dragon", export(body, "thomas_dragon", COLORS, DRAGON_ORDER, "Thomas dragon body")))
    for side in ("left", "right"):
        wing = common.merge(dragon_scene, f"Wing_{side}", build_wing(side), mats)
        report.append((f"thomas_wing_{side}", export(wing, f"thomas_wing_{side}", COLORS, DRAGON_ORDER, f"Thomas dragon {side} wing")))
        # Place it at its hinge for the saved .blend and the preview (exports above use the hinge as origin).
        wing.location = WING_HINGE if side == "left" else Vector((-WING_HINGE.x, WING_HINGE.y, WING_HINGE.z))

    common.save_blend(os.path.join(common.ROOT, "blender", KEY, "steam_whistle.blend"), scene)
    common.save_blend(os.path.join(common.ROOT, "blender", KEY, "thomas_dragon.blend"), dragon_scene)
    if preview:
        common.render_icon(dragon_scene, preview, ortho_scale=11.0, look_at=(0, 0.6, 1.6), cam_offset=(-9, -11, 6), size=768)
    for name, tris in report:
        print(f"RESULT {name}: {tris} triangles")
    d = body.dimensions
    print(f"RESULT dragon body dimensions {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m")


main()
