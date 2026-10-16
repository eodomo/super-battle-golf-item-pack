"""Builds the Mighty Morphin Megazord (a giant robot you climb into) as separate body parts the game can pose.

Run it through the Blender MCP (execute_blender_code with runpy.run_path) or headless:

    blender --background --factory-startup --python blender/Zord/build_zord.py

Outputs Assets/Zord/zord.obj/.mtl/_icon.png and blender/Zord/zord.blend.
The OBJ has one `o` group per part with vertices in the standing pose, plus `#pivot name x y z` comments
(OBJ coordinates) for the joint each part rotates about. Geometry is written in Unity space (x right, y up,
z forward; it faces +z) and converted to Blender space only when a part is created.

Modeled after the classic toy: a red Tyrannosaurus chest with silver teeth, black helmet with red horns, huge black
and yellow wing shoulders, the giant Power Sword in its right hand (+x), the black Mastodon shield on its left arm,
a yellow Sabertooth Tiger right leg and a blue Triceratops left leg.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402,F401
import bpy  # noqa: E402,F401
import common  # noqa: E402
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
}
ORDER = ["Red", "Black", "Blue", "Yellow", "Pink", "Silver", "Gold", "Visor", "White"]
RED, BLACK, BLUE, YELLOW, PINK, SILVER, GOLD, VISOR, WHITE = range(9)

ALONG = Matrix.Rotation(math.pi / 2, 4, "X")  # a cylinder's Z axis becomes Unity +z (Blender -Y)
AXIS_X = Vector((1, 0, 0))  # Unity x -> Blender -x, so a rotation about Unity x is one about Blender x (sign flips handled by callers)
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


def ball(r, pos, scale=(1, 1, 1), mat=RED, seg=18, rings=12):
    return common.sphere(r, place(pos, scale), seg, rings, mat)


def block(size, pos, mat, bevel=0.0, roll=0.0):
    """A box centered on pos (size in Unity x, y, z), optionally rolled about Unity z (positive tips +x up)."""
    m = Matrix.Translation(conv(pos)) @ Matrix.Rotation(-roll, 4, AXIS_Z)
    return common.box((size[0], size[2], size[1]), m, mat, bevel)


def cone_up(pos, h, w, mat=GOLD, tilt=0.0, roll=0.0):
    """A cone standing on pos pointing up, leaning forward by tilt and sideways (towards +x) by roll (radians)."""
    m = (Matrix.Translation(conv(pos)) @ Matrix.Rotation(-roll, 4, AXIS_Z) @ Matrix.Rotation(tilt, 4, "X")
         @ Matrix.Translation((0, 0, h / 2)))
    return common.cylinder(w, 0.0, h, m, 8, mat)


def cone_fwd(pos, length, w, mat=SILVER, pitch=0.0):
    """A cone from pos pointing forward (+z), pitched up by `pitch` radians."""
    m = Matrix.Translation(conv(pos)) @ Matrix.Rotation(pitch, 4, "X") @ Matrix.Translation((0, -length / 2, 0)) @ ALONG
    return common.cylinder(w, 0.0, length, m, 8, mat)


def cone_down(pos, length, w, mat=WHITE):
    """A cone from pos pointing down."""
    return common.cylinder(w, 0.0, length, Matrix.Translation(conv((pos[0], pos[1] - length / 2, pos[2]))) @ Matrix.Rotation(math.pi, 4, "X"), 6, mat)


def build_parts(scene, mats):
    parts = {}
    pivots = {}

    def add(name, pivot, bms):
        parts[name] = common.merge(scene, name, bms, mats)
        pivots[name] = pivot

    # Pelvis: a red hip block split by a black strip, a silver belt with blue triangles and a yellow buckle.
    add("pelvis", (0, 1.30, 0), [
        block((0.86, 0.36, 0.54), (0, 1.44, 0), RED, 0.05),
        block((0.16, 0.38, 0.56), (0, 1.42, 0), BLACK),
        block((0.34, 0.20, 0.42), (0, 1.22, 0), BLACK, 0.03),
        block((0.92, 0.10, 0.58), (0, 1.64, 0), SILVER, 0.02),
        block((0.24, 0.12, 0.06), (0, 1.64, 0.31), YELLOW, 0.01),
        block((0.12, 0.08, 0.05), (0.22, 1.64, 0.31), BLUE),
        block((0.12, 0.08, 0.05), (-0.22, 1.64, 0.31), BLUE),
    ])

    # Torso: black waist, red lower chest with yellow bars, red upper chest with a mouth of silver teeth (the
    # Tyrannosaurus), huge black and yellow wing shoulders, a cockpit dome on the back.
    torso = [
        block((0.58, 0.20, 0.42), (0, 1.72, 0), BLACK, 0.02),
        block((0.94, 0.36, 0.64), (0, 1.99, 0.02), RED, 0.05),
        block((1.14, 0.46, 0.74), (0, 2.40, 0), RED, 0.06),
        block((0.96, 0.22, 0.06), (0, 2.42, 0.37), BLACK),                       # inside of the mouth
        vcyl(0.17, 0.15, 2.60, 2.70, mat=BLACK),                                   # neck
        # The cockpit on the back.
        block((0.86, 0.60, 0.12), (0, 2.30, -0.42), BLACK, 0.03),
        ball(0.22, (0, 2.36, -0.50), (1.2, 0.9, 0.6), BLUE, 20, 10),
    ]
    for i in range(3):
        torso.append(block((0.03, 0.22, 0.03), (-0.09 + 0.09 * i, 1.99, 0.35), YELLOW))   # yellow bars
    for i in range(6):
        x = -0.42 + 0.168 * i
        torso.append(cone_down((x, 2.60, 0.38), 0.24, 0.085, SILVER))                      # upper teeth
    for i in range(5):
        x = -0.336 + 0.168 * i
        torso.append(common.cylinder(0.085, 0.0, 0.22, Matrix.Translation(conv((x, 2.33, 0.38))), 6, SILVER))  # lower teeth
    for sx in (-1, 1):
        # Wing shoulders: a big black plate flaring up and out, edged in yellow, with red markings.
        torso.append(block((0.62, 0.30, 0.40), (0.96 * sx, 2.52, 0), YELLOW, 0.03, roll=0.0))
        torso.append(block((0.70, 0.70, 0.36), (0.98 * sx, 2.74, -0.02), BLACK, 0.04, roll=0.18 * sx))
        torso.append(block((0.56, 0.20, 0.05), (0.96 * sx, 2.50, 0.22), RED, 0.01))
        torso.append(block((0.34, 0.16, 0.05), (0.98 * sx, 2.74, 0.20), RED, 0.01))
        torso.append(block((0.40, 0.06, 0.05), (0.98 * sx, 2.96, 0.20), YELLOW))
    add("torso", (0, 1.55, 0), torso)

    # Head: black helmet, silver face mask, glowing eyes, red crescent horns and a gold center spike.
    head = [
        block((0.40, 0.44, 0.40), (0, 2.88, 0), BLACK, 0.06),
        block((0.28, 0.20, 0.06), (0, 2.82, 0.21), SILVER, 0.02),
        block((0.11, 0.06, 0.04), (0.09, 2.92, 0.23), VISOR),
        block((0.11, 0.06, 0.04), (-0.09, 2.92, 0.23), VISOR),
        block((0.06, 0.34, 0.34), (0.23, 2.88, 0), RED, 0.01),
        block((0.06, 0.34, 0.34), (-0.23, 2.88, 0), RED, 0.01),
        block((0.54, 0.12, 0.34), (0, 3.12, 0), RED, 0.02),
        cone_up((0, 3.16, 0.0), 0.42, 0.045, GOLD),
    ]
    for sx in (-1, 1):
        head.append(cone_up((0.22 * sx, 3.14, 0.0), 0.34, 0.085, RED, roll=0.20 * sx))
        head.append(cone_up((0.33 * sx, 3.42, 0.0), 0.20, 0.07, RED, roll=-0.55 * sx))   # curl of the horn
    add("head", (0, 2.62, 0), head)

    # Arms. Right (+x): silver arm, black forearm, the giant Power Sword. Left (-x): silver arm, black forearm, the Mastodon shield.
    for side, sx in (("L", -1), ("R", 1)):
        x = 0.84 * sx
        add("upperArm" + side, (x, 2.38, 0), [
            ball(0.18, (x, 2.36, 0), (1.0, 0.9, 1.0), BLACK),
            block((0.30, 0.50, 0.34), (x, 2.08, 0), SILVER, 0.04),
            block((0.32, 0.06, 0.36), (x, 1.90, 0), BLACK),
        ])
        fore = [
            block((0.30, 0.56, 0.36), (x, 1.62, 0), BLACK, 0.04),
            block((0.32, 0.07, 0.38), (x, 1.80, 0), YELLOW),
            block((0.32, 0.05, 0.38), (x, 1.72, 0), RED),
            ball(0.17, (x, 1.24, 0.06), (1.0, 1.05, 1.15), BLACK, 14, 10),              # fist
        ]
        if side == "L":
            # The black shield with a yellow rim and red edge, held out in front of the forearm.
            fore.append(block((0.92, 1.02, 0.10), (x - 0.08, 1.62, 0.40), RED, 0.04))
            fore.append(block((0.86, 0.96, 0.12), (x - 0.08, 1.62, 0.40), YELLOW, 0.04))
            fore.append(block((0.74, 0.84, 0.15), (x - 0.08, 1.62, 0.40), BLACK, 0.04))
            fore.append(block((0.30, 0.10, 0.16), (x - 0.08, 1.96, 0.42), VISOR))       # green-ish marking, shown as a light
            for i in range(3):
                fore.append(cone_fwd((x - 0.14 + 0.14 * i, 1.14, 0.12), 0.3, 0.06, BLACK, pitch=-0.5))   # claws
        else:
            # The Power Sword, upright in the fist: black ridged grip, gold hilt panel, a tall silver blade.
            fore.append(vcyl(0.05, 0.05, 0.98, 1.50, x=x, z=0.06, mat=BLACK))
            fore.append(ball(0.07, (x, 0.96, 0.06), (1, 1.2, 1), GOLD, 10, 6))
            fore.append(block((0.40, 0.10, 0.10), (x, 1.54, 0.06), BLACK, 0.02))        # crossguard
            fore.append(block((0.34, 0.70, 0.07), (x, 1.92, 0.06), GOLD, 0.01))         # hilt panel
            fore.append(block((0.46, 1.90, 0.055), (x, 3.10, 0.06), SILVER, 0.01))      # blade
            fore.append(block((0.28, 0.60, 0.056), (x, 4.30, 0.06), SILVER, 0.01))      # tapering tip
            fore.append(block((0.08, 1.80, 0.058), (x, 3.10, 0.06), BLUE))              # fuller
        add("foreArm" + side, (x, 1.88, 0), fore)

    # Legs: silver thighs; the right leg is the yellow Sabertooth Tiger and the left the blue Triceratops.
    for side, sx, col in (("L", -1, BLUE), ("R", 1, YELLOW)):
        hx = 0.32 * sx
        add("thigh" + side, (hx, 1.30, 0.0), [
            block((0.40, 0.56, 0.44), (hx, 1.02, 0.0), SILVER, 0.05),
            block((0.06, 0.40, 0.30), (hx + 0.22 * sx, 1.04, 0.0), BLACK),
            block((0.30, 0.10, 0.34), (hx, 0.76, 0.04), BLACK, 0.02),
        ])
        add("shin" + side, (hx, 0.74, 0.0), [
            block((0.48, 0.64, 0.52), (hx, 0.44, 0.02), col, 0.06),
            block((0.50, 0.05, 0.54), (hx, 0.30, 0.02), BLACK),
            block((0.50, 0.05, 0.54), (hx, 0.60, 0.02), BLACK),
            block((0.32, 0.20, 0.10), (hx, 0.66, 0.29), SILVER, 0.02),
            block((0.14, 0.14, 0.04), (hx, 0.66, 0.35), RED),
            block((0.22, 0.46, 0.10), (hx, 0.40, -0.28), SILVER, 0.02),
        ])
        foot = [
            block((0.44, 0.24, 0.66), (hx, 0.13, 0.18), col, 0.05),
            block((0.46, 0.05, 0.70), (hx, 0.03, 0.18), BLACK),
        ]
        if side == "R":
            # Sabertooth tiger head: snout, eyes, nose and two fangs.
            foot += [
                block((0.30, 0.16, 0.24), (hx, 0.12, 0.60), YELLOW, 0.04),
                block((0.10, 0.06, 0.06), (hx, 0.17, 0.73), BLACK),
                block((0.07, 0.06, 0.04), (hx + 0.12, 0.27, 0.46), RED),
                block((0.07, 0.06, 0.04), (hx - 0.12, 0.27, 0.46), RED),
                cone_down((hx + 0.10, 0.08, 0.70), 0.14, 0.03, SILVER),
                cone_down((hx - 0.10, 0.08, 0.70), 0.14, 0.03, SILVER),
            ]
        else:
            # Triceratops head: a bony frill, a nose horn and two brow horns.
            foot += [
                block((0.40, 0.14, 0.14), (hx, 0.30, 0.0), col, 0.03),
                block((0.28, 0.14, 0.22), (hx, 0.12, 0.60), col, 0.04),
                cone_fwd((hx, 0.20, 0.70), 0.18, 0.04, SILVER, pitch=0.5),
                cone_fwd((hx + 0.13, 0.28, 0.52), 0.30, 0.04, SILVER, pitch=0.6),
                cone_fwd((hx - 0.13, 0.28, 0.52), 0.30, 0.04, SILVER, pitch=0.6),
                block((0.10, 0.08, 0.04), (hx, 0.32, 0.50), WHITE),
            ]
        add("foot" + side, (hx, 0.12, 0.0), foot)
    return parts, pivots


def export_parts(parts, pivots, obj_path, mtl_path):
    lines = [f"# {KEY}", f"mtllib {os.path.basename(mtl_path)}"]
    voff = 0
    noff = 0
    tris_total = 0
    for name, obj in parts.items():
        mesh = obj.data
        mesh.calc_loop_triangles()
        corner_normals = mesh.corner_normals
        bp = conv(pivots[name])
        lines.append(f"#pivot {name} {bp.x:.5f} {bp.z:.5f} {-bp.y:.5f}")
        lines.append(f"o {name}")
        for v in mesh.vertices:
            co = obj.matrix_world @ v.co
            lines.append(f"v {co.x:.5f} {co.z:.5f} {-co.y:.5f}")
        normal_index = {}
        normals = []
        tris_by_mat = {i: [] for i in range(len(ORDER))}
        for tri in mesh.loop_triangles:
            entry = []
            for vi, li in zip(tri.vertices, tri.loops):
                n = corner_normals[li].vector
                key = (round(n.x, 4), round(n.z, 4), round(-n.y, 4))
                if key not in normal_index:
                    normal_index[key] = len(normals) + 1
                    normals.append(key)
                entry.append((vi + 1 + voff, normal_index[key] + noff))
            tris_by_mat[tri.material_index].append(entry)
        for n in normals:
            lines.append(f"vn {n[0]:.4f} {n[1]:.4f} {n[2]:.4f}")
        for mi, tris in tris_by_mat.items():
            if not tris:
                continue
            lines.append(f"usemtl {ORDER[mi]}")
            for t in tris:
                lines.append("f " + " ".join(f"{v}//{n}" for v, n in t))
            tris_total += len(tris)
        voff += len(mesh.vertices)
        noff += len(normals)
    with open(obj_path, "w", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    mtl = ["# Kd values are sRGB"]
    for name in ORDER:
        r, g, b = COLORS[name]
        mtl += [f"newmtl {name}", f"Kd {r:.4f} {g:.4f} {b:.4f}", ""]
    with open(mtl_path, "w", newline="\n") as f:
        f.write("\n".join(mtl))
    return tris_total


def main():
    scene = common.fresh_scene("Zord")
    mats = common.make_materials("Zord_", COLORS, ORDER, roughness=0.4)
    parts, pivots = build_parts(scene, mats)
    out = common.assets_dir(KEY)
    tris = export_parts(parts, pivots, os.path.join(out, NAME + ".obj"), os.path.join(out, NAME + ".mtl"))
    print(f"RESULT Megazord: {len(parts)} parts, {tris} triangles")
    common.render_icon(scene, os.path.join(out, NAME + "_icon.png"), 5.2, (0, 0.0, 2.1), (1.2, -8.0, 1.0))
    common.save_blend(os.path.join(common.ROOT, "blender", KEY, NAME + ".blend"), scene)


main()
