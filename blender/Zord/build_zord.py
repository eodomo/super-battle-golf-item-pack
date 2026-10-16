"""Builds the Mighty Morphin Megazord (a giant robot you climb into) as separate body parts the game can pose.

Run it through the Blender MCP (execute_blender_code with runpy.run_path) or headless:

    blender --background --factory-startup --python blender/Zord/build_zord.py

Outputs Assets/Zord/zord.obj/.mtl/_icon.png and blender/Zord/zord.blend.
The OBJ has one `o` group per part with vertices in the standing pose, plus `#pivot name x y z` comments
(OBJ coordinates) for the joint each part rotates about. Geometry is written in Unity space (x right, y up,
z forward; it faces +z) and converted to Blender space only when a part is created.

Modeled after the classic toy: a red Tyrannosaurus chest with silver teeth, black helmet with red crescent horns,
huge black and yellow wing shoulders, the giant curved Power Sword in its right hand (+x), the black Mastodon shield on
its left arm, a yellow Sabertooth Tiger right leg and a blue Triceratops left leg.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402,F401
import bpy  # noqa: E402,F401
import common  # noqa: E402
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib  # noqa: E402
import zord_texture  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

KEY = "Zord"
NAME = "zord"
COLORS = {
    "Red": (0.86, 0.09, 0.11),
    "Black": (0.10, 0.10, 0.12),
    "Blue": (0.10, 0.30, 0.90),
    "Yellow": (0.97, 0.80, 0.10),
    "Pink": (0.95, 0.40, 0.65),
    "Silver": (0.74, 0.77, 0.83),
    "Gold": (0.93, 0.72, 0.16),
    "Visor": (1.00, 0.90, 0.35),
    "White": (0.95, 0.95, 0.97),
    "Green": (0.12, 0.72, 0.32),
    "Steel": (0.36, 0.38, 0.44),
}
ORDER = ["Red", "Black", "Blue", "Yellow", "Pink", "Silver", "Gold", "Visor", "White", "Green", "Steel"]
RED, BLACK, BLUE, YELLOW, PINK, SILVER, GOLD, VISOR, WHITE, GREEN, STEEL = range(11)

ALONG = Matrix.Rotation(math.pi / 2, 4, "X")  # a cylinder's Z axis becomes Unity +z (Blender -Y)
AXIS_Z = Vector((0, -1, 0))  # Unity z axis in Blender space


def conv(p):
    """Unity-space point -> Blender-space point."""
    return Vector((-p[0], -p[2], p[1]))


def place(pos, scale=(1, 1, 1)):
    sx, sy, sz = scale
    return Matrix.Translation(conv(pos)) @ Matrix.Diagonal((sx, sz, sy, 1.0))


def vcyl(r0, r1, y0, y1, x=0.0, z=0.0, sxz=(1.0, 1.0), mat=RED, seg=16):
    """Vertical (tapered) cylinder from y0 (radius r0) up to y1 (radius r1)."""
    return common.cylinder(r0, r1, y1 - y0, place((x, (y0 + y1) / 2, z), (sxz[0], 1, sxz[1])), seg, mat)


def xcyl(r, x0, x1, y, z, mat=BLACK, seg=14):
    """Cylinder along Unity x from x0 to x1."""
    m = Matrix.Translation(conv(((x0 + x1) / 2, y, z))) @ Matrix.Rotation(math.pi / 2, 4, "Y")
    return common.cylinder(r, r, abs(x1 - x0), m, seg, mat)


def zcyl(r0, r1, z0, z1, x=0.0, y=0.0, mat=BLACK, seg=14):
    """Cylinder along Unity z from z0 (radius r0) to z1 (radius r1)."""
    m = Matrix.Translation(conv((x, y, (z0 + z1) / 2))) @ ALONG
    return common.cylinder(r0, r1, z1 - z0, m, seg, mat)


def ball(r, pos, scale=(1, 1, 1), mat=RED, seg=18, rings=12):
    return common.sphere(r, place(pos, scale), seg, rings, mat)


def block(size, pos, mat, bevel=0.0):
    """A box centered on pos (size in Unity x, y, z)."""
    return common.box((size[0], size[2], size[1]), Matrix.Translation(conv(pos)), mat, bevel)


def poly(points, z0, z1, mat, bevel=0.0):
    """Extrudes an outline given as Unity (x, y) points across z from z0 to z1."""
    bm = bmesh.new()
    verts = [bm.verts.new(conv((x, y, z0))) for x, y in points]
    face = bm.faces.new(verts)
    result = bmesh.ops.extrude_face_region(bm, geom=[face])
    moved = [v for v in result["geom"] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=Vector((0, -(z1 - z0), 0)), verts=moved)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES", profile=0.5, clamp_overlap=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return common.set_material(bm, mat)


def mirror(points):
    return [(-x, y) for x, y in reversed(points)]


def scaled(points, cx, cy, k):
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in points]


def side(points, sx):
    """The outline for the +x side as given, or mirrored for the -x side."""
    return points if sx > 0 else mirror(points)


def tilt(bms, pivot, roll):
    """Rotates parts about the Unity z axis through pivot; positive roll tips an upright part's top towards -x."""
    m = Matrix.Translation(conv(pivot)) @ Matrix.Rotation(-roll, 4, AXIS_Z) @ Matrix.Translation(-conv(pivot))
    for bm in bms:
        bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return bms


def bend(bms, pivot, angle):
    """Swings parts forward about the Unity x axis through pivot (a hanging limb ends up pointing forward and down)."""
    p = Vector(pivot)
    c, s = math.cos(-angle), math.sin(-angle)
    for bm in bms:
        for v in bm.verts:
            u = Vector((-v.co.x, v.co.z, -v.co.y)) - p        # Blender -> Unity
            u = Vector((u.x, u.y * c - u.z * s, u.y * s + u.z * c)) + p
            v.co = conv(u)
    return bms


def cone_up(pos, h, w, mat=GOLD):
    return common.cylinder(w, 0.0, h, Matrix.Translation(conv((pos[0], pos[1] + h / 2, pos[2]))), 8, mat)


def cone_fwd(pos, length, w, mat=SILVER, pitch=0.0):
    m = Matrix.Translation(conv(pos)) @ Matrix.Rotation(pitch, 4, "X") @ Matrix.Translation((0, -length / 2, 0)) @ ALONG
    return common.cylinder(w, 0.0, length, m, 8, mat)


def cone_down(pos, length, w, mat=WHITE):
    return common.cylinder(w, 0.0, length, Matrix.Translation(conv((pos[0], pos[1] - length / 2, pos[2]))) @ Matrix.Rotation(math.pi, 4, "X"), 6, mat)


def sword(x, y0, z):
    """The Power Sword with its grip at (x, y0, z), blade up, leaning outward. Returns its parts."""
    parts = []
    # Grip: black, ridged with steel rings, a gold pommel with a spike below.
    parts.append(vcyl(0.055, 0.055, y0 - 0.34, y0 + 0.30, x=x, z=z, mat=BLACK, seg=12))
    for i in range(5):
        parts.append(vcyl(0.065, 0.065, y0 - 0.30 + 0.115 * i, y0 - 0.26 + 0.115 * i, x=x, z=z, mat=STEEL, seg=12))
    parts.append(ball(0.075, (x, y0 - 0.36, z), (1, 1.1, 1), GOLD, 12, 8))
    parts.append(cone_down((x, y0 - 0.42, z), 0.14, 0.04, GOLD))
    # Guard: a gold crossguard with ends that sweep up, and a red gem.
    parts.append(block((0.34, 0.10, 0.16), (x, y0 + 0.34, z), GOLD, 0.03))
    for sx in (-1, 1):
        parts.append(block((0.10, 0.16, 0.12), (x + 0.20 * sx, y0 + 0.38, z), GOLD, 0.02))
    parts.append(ball(0.05, (x, y0 + 0.34, z + 0.09), (1, 1, 0.6), RED, 10, 6))
    # Hilt panel: an ornate gold shield shape at the base of the blade.
    base = y0 + 0.40
    panel = [(-0.19, base), (0.19, base), (0.22, base + 0.30), (0.0, base + 0.52), (-0.22, base + 0.30)]
    parts.append(poly([(x + px, py) for px, py in panel], z - 0.035, z + 0.035, GOLD, 0.012))
    parts.append(poly([(x + px, py) for px, py in scaled(panel, 0.0, base + 0.25, 0.5)], z + 0.034, z + 0.05, RED))
    # Blade: broad and slightly curved, tapering to a point; silver with a darker fuller.
    b = base + 0.38
    k = 0.8  # blade length factor
    blade = [(-0.20, b), (0.20, b), (0.265, b + 0.5 * k), (0.27, b + 1.2 * k), (0.225, b + 1.9 * k), (0.12, b + 2.45 * k),
             (-0.03, b + 2.78 * k), (-0.115, b + 2.35 * k), (-0.21, b + 1.5 * k), (-0.245, b + 0.6 * k)]
    parts.append(poly([(x + px, py) for px, py in blade], z - 0.03, z + 0.03, SILVER, 0.01))
    fuller = [(-0.05, b + 0.1), (0.05, b + 0.1), (0.06, b + 1.5 * k), (0.0, b + 2.3 * k), (-0.06, b + 1.4 * k)]
    parts.append(poly([(x + px, py) for px, py in fuller], z + 0.029, z + 0.04, STEEL))
    parts.append(poly([(x + px, py) for px, py in scaled(fuller, 0.0, b + 1.2 * k, 0.35)], z + 0.039, z + 0.045, BLUE))
    parts.append(block((0.30, 0.05, 0.075), (x, b + 0.03, z), GOLD, 0.01))
    return tilt(parts, (x, y0, z), -0.30)


def build_parts(scene, mats):
    parts = {}
    pivots = {}

    def add(name, pivot, bms):
        parts[name] = common.merge(scene, name, bms, mats)
        pivots[name] = pivot

    # ---- Pelvis: red hip armor split by a black strip, a silver belt with blue triangles and a yellow buckle ----
    pelvis = [
        block((0.92, 0.36, 0.58), (0, 1.42, 0), RED, 0.07),
        block((0.17, 0.40, 0.60), (0, 1.42, 0), BLACK, 0.02),
        block((0.40, 0.22, 0.46), (0, 1.20, 0), BLACK, 0.04),
        block((1.00, 0.11, 0.64), (0, 1.64, 0), SILVER, 0.03),
        block((0.26, 0.13, 0.07), (0, 1.64, 0.34), YELLOW, 0.015),
        block((0.15, 0.09, 0.09), (0, 1.64, 0.32), GOLD, 0.01),
    ]
    for sx in (-1, 1):
        pelvis.append(poly(side([(0.17, 1.70), (0.27, 1.70), (0.22, 1.58)], sx), 0.31, 0.37, BLUE))
        pelvis.append(block((0.30, 0.20, 0.06), (0.30 * sx, 1.38, 0.31), STEEL, 0.015))
        pelvis.append(block((0.20, 0.30, 0.50), (0.50 * sx, 1.38, 0), RED, 0.05))
        pelvis.append(block((0.08, 0.20, 0.30), (0.56 * sx, 1.36, 0.0), SILVER, 0.02))
    add("pelvis", (0, 1.30, 0), pelvis)

    # ---- Torso ----
    torso = [
        vcyl(0.24, 0.28, 1.52, 1.84, mat=BLACK, sxz=(1.2, 0.85)),                                  # waist
        poly([(-0.42, 1.80), (0.42, 1.80), (0.52, 2.20), (-0.52, 2.20)], -0.30, 0.34, RED, 0.05),  # lower chest
        poly([(-0.52, 2.20), (0.52, 2.20), (0.64, 2.72), (-0.64, 2.72)], -0.36, 0.40, RED, 0.06),  # upper chest
        block((1.00, 0.18, 0.10), (0, 2.24, 0.36), GOLD, 0.02),                                    # lip between the jaws
        block((0.96, 0.24, 0.08), (0, 2.44, 0.40), BLACK),                                         # inside of the mouth
        vcyl(0.18, 0.16, 2.70, 2.80, mat=BLACK),                                                   # neck
        # Cockpit on the back: a black plate, a blue canopy and a silver frame.
        block((0.90, 0.64, 0.14), (0, 2.34, -0.42), BLACK, 0.04),
        ball(0.24, (0, 2.40, -0.52), (1.25, 0.95, 0.6), BLUE, 20, 10),
        block((0.62, 0.06, 0.08), (0, 2.12, -0.50), SILVER, 0.015),
    ]
    for i in range(3):
        torso.append(block((0.035, 0.26, 0.04), (-0.10 + 0.10 * i, 2.00, 0.35), YELLOW, 0.008))   # yellow bars
    torso.append(block((0.50, 0.05, 0.04), (0, 1.88, 0.34), STEEL))
    for i in range(7):
        torso.append(cone_down((-0.45 + 0.15 * i, 2.62, 0.40), 0.26, 0.075, SILVER))                # upper teeth
    for i in range(6):
        torso.append(common.cylinder(0.075, 0.0, 0.22, Matrix.Translation(conv((-0.375 + 0.15 * i, 2.35, 0.40))), 6, SILVER))
    wing = [(0.64, 2.28), (1.36, 2.38), (1.48, 3.20), (0.80, 3.06)]
    mark = [(0.84, 2.50), (1.32, 2.56), (1.34, 2.92), (1.20, 2.80), (1.08, 2.94), (0.96, 2.80), (0.86, 2.92)]
    for sx in (-1, 1):
        torso.append(ball(0.055, (0.62 * sx, 2.46, 0.36), (1, 1, 0.7), VISOR, 10, 6))              # chest lights
        torso.append(ball(0.05, (0.46 * sx, 1.96, 0.34), (1, 1, 0.7), BLUE, 10, 6))
        # Wing shoulders: a big black plate flaring up and out, edged in yellow with red chevron markings.
        pts = side(wing, sx)
        torso.append(poly(pts, -0.20, 0.22, BLACK, 0.04))
        torso.append(poly(scaled(pts, 1.07 * sx, 2.78, 0.88), 0.22, 0.27, YELLOW, 0.012))
        mpts = side(mark, sx)
        torso.append(poly(mpts, 0.27, 0.31, RED))
        torso.append(poly(scaled(mpts, 1.09 * sx, 2.72, 0.5), 0.31, 0.33, BLACK))
        torso.append(block((0.74, 0.14, 0.36), (1.00 * sx, 2.30, 0.02), YELLOW, 0.03))              # underside trim
        torso.append(xcyl(0.12, 0.60 * sx, 0.84 * sx, 2.44, 0.0, STEEL))                            # shoulder joint
    add("torso", (0, 1.55, 0), torso)

    # ---- Head ----
    head = [
        block((0.44, 0.48, 0.44), (0, 2.88, 0), BLACK, 0.08),
        block((0.30, 0.30, 0.07), (0, 2.80, 0.22), SILVER, 0.02),                                  # face plate
        block((0.34, 0.07, 0.10), (0, 3.12, 0.04), RED, 0.02),
        cone_up((0, 3.14, 0.02), 0.55, 0.05, GOLD),                                                 # gold spike
        block((0.07, 0.22, 0.10), (0, 3.24, 0.02), GOLD, 0.01),
    ]
    for i in range(3):
        head.append(block((0.02, 0.22, 0.02), (-0.06 + 0.06 * i, 2.72, 0.265), STEEL))             # grille
    eye = [(0.05, 2.92), (0.17, 2.97), (0.17, 3.02), (0.05, 2.98)]
    horn = [(0.14, 3.02), (0.34, 3.00), (0.52, 3.16), (0.56, 3.52), (0.44, 3.36), (0.34, 3.22), (0.14, 3.16)]
    for sx in (-1, 1):
        head.append(poly(side(eye, sx), 0.215, 0.245, VISOR))
        head.append(block((0.06, 0.34, 0.36), (0.24 * sx, 2.88, 0), RED, 0.015))                     # cheek guards
        head.append(block((0.04, 0.20, 0.12), (0.27 * sx, 2.82, 0.0), GOLD, 0.01))
        head.append(poly(side(horn, sx), -0.07, 0.07, RED, 0.012))
    add("head", (0, 2.62, 0), head)

    # ---- Arms. Right (+x): silver arm, black forearm, the Power Sword. Left (-x): the Mastodon shield. ----
    for name, sx in (("L", -1), ("R", 1)):
        x = 0.86 * sx
        add("upperArm" + name, (x, 2.38, 0), [
            ball(0.19, (x, 2.36, 0), (1.0, 0.9, 1.0), BLACK),
            block((0.34, 0.54, 0.38), (x, 2.08, 0), SILVER, 0.07),
            block((0.36, 0.07, 0.40), (x, 1.88, 0), BLACK, 0.015),
            xcyl(0.05, x - 0.20, x + 0.20, 2.14, 0.22, STEEL),
            vcyl(0.045, 0.045, 1.86, 2.34, x=x + 0.20 * sx, z=-0.14, mat=STEEL),
            ball(0.14, (x, 1.86, 0), (1, 1, 1), BLACK, 14, 8),
        ])
        fore = [
            block((0.36, 0.60, 0.42), (x, 1.58, 0), BLACK, 0.07),
            block((0.38, 0.07, 0.44), (x, 1.80, 0), YELLOW, 0.012),
            block((0.38, 0.05, 0.44), (x, 1.72, 0), RED, 0.01),
            block((0.30, 0.22, 0.05), (x, 1.46, 0.22), STEEL, 0.015),
            ball(0.05, (x, 1.46, 0.26), (1, 1, 0.6), GOLD, 8, 6),
        ]
        # The forearm is bent forward at the elbow, so the fist is out in front and can hold things clear of the arm.
        elbow = (x, 1.88, 0.0)
        a = math.radians(64)
        bend(fore, elbow, a)
        wy, wz = 1.88 - 0.66 * math.cos(a), 0.66 * math.sin(a)
        # A fist closed around a vertical grip: the palm behind it, four finger bars wrapped across its front, a thumb on top.
        fore.append(block((0.30, 0.40, 0.14), (x, wy - 0.02, wz - 0.11), BLACK, 0.05))               # palm
        for i in range(4):
            fore.append(block((0.31, 0.075, 0.17), (x, wy - 0.17 + 0.105 * i, wz + 0.07), BLACK, 0.025))  # fingers
        fore.append(block((0.09, 0.09, 0.20), (x - 0.15 * sx, wy + 0.16, wz + 0.03), BLACK, 0.03))   # thumb
        fore.append(block((0.30, 0.06, 0.32), (x, wy + 0.14, wz), YELLOW, 0.012))                    # cuff
        if name == "L":
            # The black Mastodon shield: a pointed shield with a red edge, a yellow rim and silver tusks.
            outline = [(-0.42, 0.50), (0.42, 0.50), (0.48, 0.0), (0.34, -0.42), (0.0, -0.58), (-0.34, -0.42), (-0.48, 0.0)]
            cx, cy, cz = x - 0.14, wy + 0.16, wz + 0.22
            pts = [(cx + px, cy + py) for px, py in outline]
            fore.append(poly(pts, cz, cz + 0.08, RED, 0.02))
            fore.append(poly(scaled(pts, cx, cy, 0.94), cz + 0.07, cz + 0.12, YELLOW, 0.012))
            fore.append(poly(scaled(pts, cx, cy, 0.84), cz + 0.11, cz + 0.17, BLACK, 0.02))
            fore.append(poly(scaled(pts, cx, cy, 0.64), cz + 0.165, cz + 0.19, STEEL))
            fore.append(poly(scaled(pts, cx, cy, 0.60), cz + 0.185, cz + 0.20, BLACK))
            fore.append(block((0.34, 0.05, 0.03), (cx, cy + 0.30, cz + 0.215), GREEN, 0.01))
            fore.append(block((0.05, 0.22, 0.03), (cx, cy + 0.18, cz + 0.215), GREEN, 0.01))
            for tx in (-1, 1):
                fore.append(cone_fwd((cx + 0.20 * tx, cy - 0.22, cz + 0.2), 0.30, 0.05, SILVER, pitch=0.5))
        else:
            add("sword", (x, wy, wz), sword(x, wy, wz))
        add("foreArm" + name, (x, 1.88, 0), fore)

    # ---- Legs: silver thighs; right = yellow Sabertooth Tiger, left = blue Triceratops ----
    for name, sx, col in (("L", -1, BLUE), ("R", 1, YELLOW)):
        hx = 0.32 * sx
        add("thigh" + name, (hx, 1.30, 0.0), [
            block((0.44, 0.60, 0.48), (hx, 1.00, 0.0), SILVER, 0.08),
            block((0.05, 0.40, 0.34), (hx + 0.23 * sx, 1.02, 0.0), BLACK, 0.01),
            block((0.30, 0.10, 0.36), (hx, 0.78, 0.04), BLACK, 0.03),
            zcyl(0.04, 0.04, -0.26, -0.02, x=hx - 0.10, y=1.00, mat=STEEL),
            zcyl(0.04, 0.04, -0.26, -0.02, x=hx + 0.10, y=1.00, mat=STEEL),
            block((0.34, 0.06, 0.40), (hx, 1.24, 0.0), BLACK, 0.01),
        ])
        shin = [
            poly([(hx - 0.27, 0.76), (hx + 0.27, 0.76), (hx + 0.22, 0.14), (hx - 0.22, 0.14)], -0.28, 0.30, col, 0.05),
            block((0.58, 0.05, 0.62), (hx, 0.30, 0.01), BLACK, 0.01),
            block((0.58, 0.05, 0.62), (hx, 0.56, 0.01), BLACK, 0.01),
            poly([(hx - 0.17, 0.78), (hx + 0.17, 0.78), (hx + 0.20, 0.52), (hx - 0.20, 0.52)], 0.29, 0.36, SILVER, 0.02),
            block((0.14, 0.14, 0.05), (hx, 0.66, 0.38), RED, 0.01),
            block((0.26, 0.40, 0.12), (hx, 0.44, -0.34), SILVER, 0.03),
            block((0.18, 0.30, 0.04), (hx, 0.40, 0.31), STEEL, 0.01),
        ]
        if name == "R":
            shin.append(xcyl(0.17, hx + 0.27, hx + 0.36, 0.38, -0.08, BLACK, seg=18))              # the Sabertooth's wheel
            shin.append(xcyl(0.08, hx + 0.36, hx + 0.38, 0.38, -0.08, SILVER, seg=12))
        add("shin" + name, (hx, 0.74, 0.0), shin)
        foot = [
            block((0.46, 0.22, 0.68), (hx, 0.13, 0.18), col, 0.06),
            block((0.48, 0.05, 0.72), (hx, 0.03, 0.18), BLACK, 0.01),
        ]
        if name == "R":
            # Sabertooth tiger head: a broad muzzle, ears, glowing eyes, a nose and two long fangs.
            foot += [
                block((0.34, 0.18, 0.26), (hx, 0.13, 0.62), YELLOW, 0.05),
                block((0.12, 0.07, 0.07), (hx, 0.20, 0.77), BLACK, 0.015),
                block((0.08, 0.06, 0.05), (hx + 0.12, 0.28, 0.50), RED),
                block((0.08, 0.06, 0.05), (hx - 0.12, 0.28, 0.50), RED),
                cone_up((hx + 0.14, 0.24, 0.30), 0.14, 0.05, YELLOW),
                cone_up((hx - 0.14, 0.24, 0.30), 0.14, 0.05, YELLOW),
                cone_down((hx + 0.11, 0.08, 0.72), 0.18, 0.032, SILVER),
                cone_down((hx - 0.11, 0.08, 0.72), 0.18, 0.032, SILVER),
                block((0.40, 0.06, 0.22), (hx, 0.30, 0.10), SILVER, 0.015),
            ]
        else:
            # Triceratops head: a frill, a nose horn and two brow horns.
            foot += [
                poly([(hx - 0.24, 0.24), (hx + 0.24, 0.24), (hx + 0.18, 0.44), (hx, 0.50), (hx - 0.18, 0.44)], 0.02, 0.10, col, 0.02),
                block((0.30, 0.16, 0.26), (hx, 0.13, 0.62), col, 0.05),
                cone_fwd((hx, 0.22, 0.72), 0.22, 0.045, SILVER, pitch=0.5),
                cone_fwd((hx + 0.14, 0.30, 0.46), 0.34, 0.045, SILVER, pitch=0.55),
                cone_fwd((hx - 0.14, 0.30, 0.46), 0.34, 0.045, SILVER, pitch=0.55),
                block((0.08, 0.06, 0.04), (hx + 0.10, 0.28, 0.54), WHITE),
                block((0.08, 0.06, 0.04), (hx - 0.10, 0.28, 0.54), WHITE),
            ]
        add("foot" + name, (hx, 0.12, 0.0), foot)
    return parts, pivots


def export_parts(parts, pivots, obj_path, mtl_path):
    """Writes every part as an OBJ group with UVs (the atlas) and one material."""
    lines = [f"# {KEY}", f"mtllib {os.path.basename(mtl_path)}"]
    voff = noff = toff = 0
    tris_total = 0
    for name, obj in parts.items():
        mesh = obj.data
        mesh.calc_loop_triangles()
        corner_normals = mesh.corner_normals
        uv_data = mesh.uv_layers.active.data
        bp = conv(pivots[name])
        lines.append(f"#pivot {name} {bp.x:.5f} {bp.z:.5f} {-bp.y:.5f}")
        lines.append(f"o {name}")
        for v in mesh.vertices:
            co = obj.matrix_world @ v.co
            lines.append(f"v {co.x:.5f} {co.z:.5f} {-co.y:.5f}")
        normal_index = {}
        normals = []
        uv_index = {}
        uvs = []
        faces = []
        for tri in mesh.loop_triangles:
            entry = []
            for vi, li in zip(tri.vertices, tri.loops):
                n = corner_normals[li].vector
                nkey = (round(n.x, 4), round(n.z, 4), round(-n.y, 4))
                if nkey not in normal_index:
                    normal_index[nkey] = len(normals) + 1
                    normals.append(nkey)
                uv = uv_data[li].uv
                ukey = (round(uv.x, 5), round(uv.y, 5))
                if ukey not in uv_index:
                    uv_index[ukey] = len(uvs) + 1
                    uvs.append(ukey)
                entry.append((vi + 1 + voff, uv_index[ukey] + toff, normal_index[nkey] + noff))
            faces.append(entry)
        for u, v in uvs:
            lines.append(f"vt {u:.5f} {v:.5f}")
        for n in normals:
            lines.append(f"vn {n[0]:.4f} {n[1]:.4f} {n[2]:.4f}")
        lines.append("usemtl Zord")
        for t in faces:
            lines.append("f " + " ".join(f"{v}/{vt}/{n}" for v, vt, n in t))
        tris_total += len(faces)
        voff += len(mesh.vertices)
        noff += len(normals)
        toff += len(uvs)
    with open(obj_path, "w", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    with open(mtl_path, "w", newline="\n") as f:
        f.write("# The Megazord is painted by zord_tex.png (see zord_texture.py)\nnewmtl Zord\nKd 1.0 1.0 1.0\n")
    return tris_total


def main():
    importlib.reload(zord_texture)
    scene = common.fresh_scene("Zord")
    mats = common.make_materials("Zord_", COLORS, ORDER, roughness=0.4)
    parts, pivots = build_parts(scene, mats)
    out = common.assets_dir(KEY)
    image = zord_texture.bake(scene, list(parts.values()), mats, COLORS, ORDER, os.path.join(out, NAME + "_tex.png"))
    tris = export_parts(parts, pivots, os.path.join(out, NAME + ".obj"), os.path.join(out, NAME + ".mtl"))
    print(f"RESULT Megazord: {len(parts)} parts, {tris} triangles")
    # The icon shows the textured model.
    painted = zord_texture.textured_material(image)
    for obj in parts.values():
        obj.data.materials.clear()
        obj.data.materials.append(painted)
    common.render_icon(scene, os.path.join(out, NAME + "_icon.png"), 5.4, (0, 0.0, 2.2), (1.2, -8.0, 1.0))
    common.save_blend(os.path.join(common.ROOT, "blender", KEY, NAME + ".blend"), scene)


main()
