"""Builds the Banana Peel item model in its own scene, then exports the game assets.

Run through the Blender MCP server (execute_blender_code). It never touches other
scenes and never saves the open .blend file; the model is written to its own file.

Outputs (in Assets/BananaPeel):
  banana_peel.obj / banana_peel.mtl  - mesh, Y-up, meters, Kd colors are sRGB
  banana_peel_icon.png               - 256x256 transparent HUD icon
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = r"C:\Users\Eric\Documents\SuperBattleGolfMods\ItemPack"
ASSETS = os.path.join(ROOT, "Assets", "BananaPeel")
BLEND_OUT = os.path.join(ROOT, "blender", "BananaPeel", "banana_peel.blend")
SCENE_NAME = "BananaPeel"

# sRGB colors (written to the .mtl as-is; converted to linear for Blender materials)
COLORS = {
    "PeelInner": (1.00, 0.94, 0.58),
    "PeelOuter": (1.00, 0.84, 0.12),
    "Stem": (0.36, 0.23, 0.09),
}
MATERIAL_ORDER = ["PeelInner", "PeelOuter", "Stem"]


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def make_material(name):
    rgb = tuple(srgb_to_linear(c) for c in COLORS[name])
    mat = bpy.data.materials.get("BP_" + name) or bpy.data.materials.new("BP_" + name)
    mat.diffuse_color = (*rgb, 1.0)
    if mat.node_tree is None and hasattr(mat, "use_nodes"):
        mat.use_nodes = True
    if mat.node_tree:
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
            bsdf.inputs["Roughness"].default_value = 0.55
    return mat


def new_object(scene, name, bm, materials):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in materials:
        mesh.materials.append(m)
    for poly in mesh.polygons:
        poly.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


def build_petal(angle_deg, length, width, base_h, curl, seed):
    """One peel strip hanging from the top of the stub, splayed on the ground, tip curling up."""
    nu, nv = 16, 6
    a = math.radians(angle_deg)
    d = Vector((math.cos(a), math.sin(a), 0.0))
    side = Vector((-math.sin(a), math.cos(a), 0.0))
    bm = bmesh.new()
    grid = []
    for i in range(nu + 1):
        u = i / nu
        r = 0.035 + length * u
        z = 0.012 + base_h * (1.0 - smoothstep(0.0, 0.45, u)) + curl * smoothstep(0.6, 1.0, u) ** 2
        w = width * math.sin(math.pi * min(1.0, 0.14 + u * 0.93)) ** 0.75
        wobble = 0.012 * math.sin(u * 7.0 + seed)  # slight organic sideways bend
        row = []
        for j in range(nv + 1):
            v = j / nv * 2.0 - 1.0
            cup = 0.35 * w * v * v  # edges lift, so the cream inside faces up
            p = d * r + side * (v * w + wobble) + Vector((0.0, 0.0, z + cup))
            row.append(bm.verts.new(p))
        grid.append(row)
    for i in range(nu):
        for j in range(nv):
            # winding gives +Z normals: original faces = inner (cream) side
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    for f in bm.faces:
        f.material_index = 0
    # Give the strip thickness so it is a closed shell (inside/outside sorted out later by normals)
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.014)
    return bm


def main():
    old = bpy.data.scenes.get(SCENE_NAME)
    if old:
        for o in list(old.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.scenes.remove(old)
    scene = bpy.data.scenes.new(SCENE_NAME)
    materials = [make_material(n) for n in MATERIAL_ORDER]

    parts = []

    # Peel strips, solidified so the outside (shell) is yellow and the inside is cream
    petals = [
        (12, 0.33, 0.070, 0.105, 0.070, 0.3),
        (98, 0.29, 0.064, 0.105, 0.085, 1.7),
        (188, 0.35, 0.074, 0.105, 0.060, 2.9),
        (274, 0.31, 0.066, 0.105, 0.080, 4.2),
    ]
    for idx, (ang, length, width, base_h, curl, seed) in enumerate(petals):
        parts.append(new_object(scene, f"Petal{idx}", build_petal(ang, length, width, base_h, curl, seed), materials))

    # Stub: the bunched-up top of the peel where the strips meet
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=14,
                          radius1=0.055, radius2=0.034, depth=0.11,
                          matrix=Matrix.Translation((0.0, 0.0, 0.065)))
    for f in bm.faces:
        f.material_index = 1
    parts.append(new_object(scene, "Stub", bm, materials))

    # Stem nub, tilted
    bm = bmesh.new()
    stem_matrix = Matrix.Translation((0.008, 0.0, 0.135)) @ Matrix.Rotation(math.radians(18), 4, "Y")
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=10,
                          radius1=0.021, radius2=0.016, depth=0.06, matrix=stem_matrix)
    for f in bm.faces:
        f.material_index = 2
    parts.append(new_object(scene, "Stem", bm, materials))

    # Merge everything into a single object (parts have identity transforms and no modifiers)
    merged = bmesh.new()
    for obj in parts:
        merged.from_mesh(obj.data)
    for obj in parts:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    bmesh.ops.recalc_face_normals(merged, faces=merged.faces)
    # Petal faces were all created as the cream inside; anything facing down or
    # sideways (the solidify shell and rim) is the yellow outside skin.
    for f in merged.faces:
        if f.material_index == 0 and f.normal.z < 0.25:
            f.material_index = 1
    peel = new_object(scene, "BananaPeel", merged, materials)

    # Validate the inner/outer split survived the normal recalculation
    counts = [0, 0, 0]
    for poly in peel.data.polygons:
        counts[poly.material_index] += 1

    export_obj(peel, os.path.join(ASSETS, "banana_peel.obj"), os.path.join(ASSETS, "banana_peel.mtl"))
    render_icon(scene, peel, os.path.join(ASSETS, "banana_peel_icon.png"))
    bpy.data.libraries.write(BLEND_OUT, {scene}, fake_user=True)

    dims = peel.dimensions
    return {
        "faces_per_material": dict(zip(MATERIAL_ORDER, counts)),
        "triangles": sum(len(p.vertices) - 2 for p in peel.data.polygons),
        "dimensions_m": [round(dims.x, 3), round(dims.y, 3), round(dims.z, 3)],
        "files": sorted(os.listdir(ASSETS)),
        "blend": BLEND_OUT,
    }


def export_obj(obj, obj_path, mtl_path):
    """Writes a triangulated, Y-up OBJ (Blender X,Y,Z -> OBJ X,Z,-Y) with split normals."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    corner_normals = mesh.corner_normals
    lines = ["# Banana Peel - generated by build_banana_peel.py", f"mtllib {os.path.basename(mtl_path)}"]
    for v in mesh.vertices:
        co = obj.matrix_world @ v.co
        lines.append(f"v {co.x:.5f} {co.z:.5f} {-co.y:.5f}")
    normal_index = {}
    normals = []
    tris_by_mat = {i: [] for i in range(len(MATERIAL_ORDER))}
    for tri in mesh.loop_triangles:
        entry = []
        for vi, li in zip(tri.vertices, tri.loops):
            n = corner_normals[li].vector
            key = (round(n.x, 4), round(n.z, 4), round(-n.y, 4))
            if key not in normal_index:
                normal_index[key] = len(normals) + 1
                normals.append(key)
            entry.append((vi + 1, normal_index[key]))
        tris_by_mat[tri.material_index].append(entry)
    for n in normals:
        lines.append(f"vn {n[0]:.4f} {n[1]:.4f} {n[2]:.4f}")
    for mi, tris in tris_by_mat.items():
        if not tris:
            continue
        lines.append(f"usemtl {MATERIAL_ORDER[mi]}")
        for t in tris:
            lines.append("f " + " ".join(f"{v}//{n}" for v, n in t))
    with open(obj_path, "w", newline="\n") as f:
        f.write("\n".join(lines) + "\n")

    mtl = ["# Kd values are sRGB"]
    for name in MATERIAL_ORDER:
        r, g, b = COLORS[name]
        mtl += [f"newmtl {name}", f"Kd {r:.4f} {g:.4f} {b:.4f}", ""]
    with open(mtl_path, "w", newline="\n") as f:
        f.write("\n".join(mtl))


def render_icon(scene, target, path):
    cam_data = bpy.data.cameras.new("IconCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 0.78
    cam = bpy.data.objects.new("IconCam", cam_data)
    scene.collection.objects.link(cam)
    look_at = Vector((0.0, 0.0, 0.05))
    cam.location = Vector((0.9, -0.9, 0.7))
    cam.rotation_euler = (look_at - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    sun_data = bpy.data.lights.new("IconSun", "SUN")
    sun_data.energy = 3.0
    sun = bpy.data.objects.new("IconSun", sun_data)
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(30))
    scene.collection.objects.link(sun)

    world = bpy.data.worlds.get("BP_IconWorld") or bpy.data.worlds.new("BP_IconWorld")
    world.color = (1.0, 1.0, 1.0)
    if world.node_tree is None and hasattr(world, "use_nodes"):
        world.use_nodes = True
    if world.node_tree:
        bg = world.node_tree.nodes.get("Background")
        if bg:
            bg.inputs["Color"].default_value = (1.0, 1.0, 1.0, 1.0)
            bg.inputs["Strength"].default_value = 0.9
    scene.world = world

    engines = {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
    scene.view_settings.view_transform = "Standard"
    scene.render.film_transparent = True
    scene.render.resolution_x = 256
    scene.render.resolution_y = 256
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=scene.name)


result = main()
