"""Builds the Power Ranger helmet (a cosmetic hat) and its menu icon.

Run it through the Blender MCP (execute_blender_code with runpy.run_path) or headless:

    blender --background --factory-startup --python blender/PowerRanger/build_helmet.py

Outputs Assets/PowerRanger/helmet.obj/.mtl and helmet_icon.png.
The helmet is modelled in the game's model space (Unity: head centre at (0, 1.535, -0.07), face towards +Z),
which in Blender (X, Y, Z) = (-Ux, -Uz, Uy) is a head centred at (0, 0.07, 1.535) looking towards -Y.
The shell is white and tinted in game with the chosen color; visor, trim and ears have fixed colors.
Decoration pieces are parametric patches on the shell (exact edges, outward normals), not selections of faces.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import common  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

KEY = "PowerRanger"
NAME = "helmet"
COLORS = {
    "Shell": (1.0, 1.0, 1.0),
    "Visor": (0.03, 0.03, 0.05),
    "Silver": (0.74, 0.77, 0.83),
    "Gold": (0.93, 0.72, 0.16),
}
ORDER = ["Shell", "Visor", "Silver", "Gold"]
SHELL, VISOR, SILVER, GOLD = range(4)

C = Vector((0.0, 0.07, 1.535))  # head centre
R = 0.30                         # shell radius


def surface_dir(az, el):
    """Unit direction: az 0 = straight ahead (-Y), positive towards +X; el = elevation above the horizon."""
    return Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))


def point_on(direction, lift):
    d = direction.normalized()
    return C + Vector((d.x * (R + lift), d.y * (R - 0.005 + lift), d.z * (R + 0.005 + lift)))


def grid_patch(points_fn, nu, nv, material):
    """A quad grid over (u, v) in [0,1]^2 whose vertex positions come from points_fn(u, v); normals made to face outward."""
    bm = bmesh.new()
    verts = [[bm.verts.new(points_fn(i / nu, j / nv)) for j in range(nv + 1)] for i in range(nu + 1)]
    for i in range(nu):
        for j in range(nv):
            bm.faces.new((verts[i][j], verts[i + 1][j], verts[i + 1][j + 1], verts[i][j + 1]))
    bm.normal_update()
    # Quads share one winding, so either every face points outward or every face points inward.
    if sum(f.normal.dot(f.calc_center_median() - C) for f in bm.faces) < 0:
        for f in bm.faces:
            f.normal_flip()
    return common.set_material(bm, material)


def shell_patch(material):
    """The dome: a sphere cut along a ring so its bottom edge is a clean horizontal circle."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=96, v_segments=48, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * R, v.co.y * (R - 0.005), v.co.z * (R + 0.005))) + C
    cut = C.z - R * math.sin(1.0)  # keep elevations above about -57 degrees
    drop = [f for f in bm.faces if f.calc_center_median().z < cut]
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return common.set_material(bm, material)


def visor_half_width(el):
    # Wide across the brow, narrowing towards the cheeks.
    t = max(0.0, min(1.0, (el + 0.2) / 0.62))
    return 0.50 + (1.12 - 0.50) * t


def visor_patch(grow, lift, material):
    el0, el1 = -0.2 - grow, 0.42 + grow

    def fn(u, v):
        el = el0 + (el1 - el0) * v
        w = visor_half_width(min(max(el, -0.2), 0.42)) + grow
        az = -w + 2 * w * u
        return point_on(surface_dir(az, el), lift)

    return grid_patch(fn, 64, 24, material)


def rect_patch(az_half, el0, el1, lift, material, nu=40, nv=24):
    def fn(u, v):
        return point_on(surface_dir(-az_half + 2 * az_half * u, el0 + (el1 - el0) * v), lift)

    return grid_patch(fn, nu, nv, material)


def ridge_patch(width, theta0, theta1, lift, material):
    """A strip along the top, running over the crown from front (theta0) to back (theta1)."""
    def fn(u, v):
        x = -width + 2 * width * u
        theta = theta0 + (theta1 - theta0) * v
        d = Vector((x, -math.cos(theta), math.sin(theta)))
        return point_on(d, lift)

    return grid_patch(fn, 4, 40, material)


def merge_keep_normals(scene, name, parts, materials):
    """Like common.merge but without recalculating normals, which would flip open patches inward."""
    merged = bmesh.new()
    for bm in parts:
        mesh = bpy.data.meshes.new("tmp")
        bm.to_mesh(mesh)
        bm.free()
        merged.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
    return common.new_object(scene, name, merged, materials, smooth=True)


def build(scene, materials):
    parts = [shell_patch(SHELL)]

    # Silver frame around the visor, then the visor itself on top.
    parts.append(visor_patch(grow=0.08, lift=0.006, material=SILVER))
    parts.append(visor_patch(grow=0.0, lift=0.012, material=VISOR))

    # Chin guard plate with three vents.
    parts.append(rect_patch(0.62, -0.98, -0.40, 0.006, SILVER))
    for el0 in (-0.52, -0.68, -0.84):
        parts.append(rect_patch(0.36, el0 - 0.028, el0 + 0.028, 0.012, VISOR, nu=16, nv=4))

    # A gold ridge along the top.
    parts.append(ridge_patch(0.045, 0.45, math.pi - 0.45, 0.010, GOLD))

    # Ear discs.
    for sign in (-1, 1):
        m = Matrix.Translation((sign * (R + 0.012), 0.07, 1.535)) @ Matrix.Rotation(math.pi / 2, 4, "Y")
        parts.append(common.cylinder(0.085, 0.085, 0.03, m, segments=28, material=SILVER))
        m2 = Matrix.Translation((sign * (R + 0.030), 0.07, 1.535)) @ Matrix.Rotation(math.pi / 2, 4, "Y")
        parts.append(common.cylinder(0.05, 0.05, 0.012, m2, segments=24, material=GOLD))

    return merge_keep_normals(scene, "Helmet", parts, materials)


def main():
    scene = common.fresh_scene("Helmet")
    materials = common.make_materials("PR_", COLORS, ORDER, roughness=0.35)
    helmet = build(scene, materials)

    assets = common.assets_dir(KEY)
    tris = common.export_obj(helmet, os.path.join(assets, f"{NAME}.obj"), os.path.join(assets, f"{NAME}.mtl"),
                             COLORS, ORDER, "Power Ranger helmet - generated by build_helmet.py")
    print(f"RESULT {NAME}: {tris} triangles")

    # Red shell for the icon.
    materials[SHELL].diffuse_color = (*(common.srgb_to_linear(c) for c in (0.86, 0.09, 0.11)), 1.0)
    bsdf = materials[SHELL].node_tree.nodes.get("Principled BSDF") if materials[SHELL].node_tree else None
    if bsdf:
        bsdf.inputs["Base Color"].default_value = materials[SHELL].diffuse_color
    helmet.rotation_euler = (0, 0, math.radians(-20))
    common.render_icon(scene, os.path.join(assets, f"{NAME}_icon.png"), ortho_scale=0.8, look_at=tuple(C),
                       cam_offset=(0, -1.0, 0.05))
    helmet.rotation_euler = (0, 0, 0)


main()
