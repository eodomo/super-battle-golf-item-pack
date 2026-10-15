"""Shared helpers for the item model scripts.

Each item script builds its model from code in its own scene, then calls export_obj and render_icon.
Run them headless (never touches an open Blender session):

    blender --background --factory-startup --python blender/<Item>/build_<item>.py

Model conventions: Blender Z up, meters. The OBJ is written Y-up; Kd colors in the .mtl are sRGB.
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def assets_dir(item_key):
    path = os.path.join(ROOT, "Assets", item_key)
    os.makedirs(path, exist_ok=True)
    return path


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def fresh_scene(name):
    old = bpy.data.scenes.get(name)
    if old:
        for o in list(old.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.scenes.remove(old)
    return bpy.data.scenes.new(name)


def make_materials(prefix, colors, order, roughness=0.5):
    mats = []
    for name in order:
        rgb = tuple(srgb_to_linear(c) for c in colors[name])
        mat = bpy.data.materials.get(prefix + name) or bpy.data.materials.new(prefix + name)
        mat.diffuse_color = (*rgb, 1.0)
        if mat.node_tree is None and hasattr(mat, "use_nodes"):
            mat.use_nodes = True
        if mat.node_tree:
            bsdf = mat.node_tree.nodes.get("Principled BSDF")
            if bsdf:
                bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
                bsdf.inputs["Roughness"].default_value = roughness
        mats.append(mat)
    return mats


def set_material(bm, index):
    for f in bm.faces:
        f.material_index = index
    return bm


def new_object(scene, name, bm, materials, smooth=True):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in materials:
        mesh.materials.append(m)
    for poly in mesh.polygons:
        poly.use_smooth = smooth
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


def merge(scene, name, parts, materials):
    """Joins bmesh parts (all in world space) into one object."""
    merged = bmesh.new()
    for bm in parts:
        mesh = bpy.data.meshes.new("tmp")
        bm.to_mesh(mesh)
        bm.free()
        merged.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
    bmesh.ops.recalc_face_normals(merged, faces=merged.faces)
    return new_object(scene, name, merged, materials)


def cylinder(radius1, radius2, depth, matrix, segments=20, material=0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                          radius1=radius1, radius2=radius2, depth=depth, matrix=matrix)
    return set_material(bm, material)


def sphere(radius, matrix, segments=16, rings=10, material=0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius, matrix=matrix)
    return set_material(bm, material)


def box(size, matrix, material=0, bevel=0.0):
    """Box of the given size; bevel (meters) rounds off its edges."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * 0.45), segments=2, affect="EDGES", profile=0.5)
    bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
    return set_material(bm, material)


def prism(profile, width, material=0, x=0.0, bevel=0.0):
    """Extrudes a side-view outline [(y, z), ...] across X (centered on x), e.g. a gun's receiver or stock."""
    bm = bmesh.new()
    front = [bm.verts.new((x - width / 2, y, z)) for y, z in profile]
    face = bm.faces.new(front)
    result = bmesh.ops.extrude_face_region(bm, geom=[face])
    moved = [v for v in result["geom"] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=Vector((width, 0, 0)), verts=moved)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES", profile=0.5, clamp_overlap=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return set_material(bm, material)


def export_obj(obj, obj_path, mtl_path, colors, order, title):
    """Writes a triangulated, Y-up OBJ (Blender X,Y,Z -> OBJ X,Z,-Y) with split normals."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    corner_normals = mesh.corner_normals
    lines = [f"# {title}", f"mtllib {os.path.basename(mtl_path)}"]
    for v in mesh.vertices:
        co = obj.matrix_world @ v.co
        lines.append(f"v {co.x:.5f} {co.z:.5f} {-co.y:.5f}")
    normal_index = {}
    normals = []
    tris_by_mat = {i: [] for i in range(len(order))}
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
        lines.append(f"usemtl {order[mi]}")
        for t in tris:
            lines.append("f " + " ".join(f"{v}//{n}" for v, n in t))
    with open(obj_path, "w", newline="\n") as f:
        f.write("\n".join(lines) + "\n")

    mtl = ["# Kd values are sRGB"]
    for name in order:
        r, g, b = colors[name]
        mtl += [f"newmtl {name}", f"Kd {r:.4f} {g:.4f} {b:.4f}", ""]
    with open(mtl_path, "w", newline="\n") as f:
        f.write("\n".join(mtl))
    return sum(1 for _ in mesh.loop_triangles)


def render_icon(scene, path, ortho_scale, look_at, cam_offset, size=256):
    cam_data = bpy.data.cameras.new("IconCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam = bpy.data.objects.new("IconCam", cam_data)
    scene.collection.objects.link(cam)
    look_at = Vector(look_at)
    cam.location = look_at + Vector(cam_offset)
    cam.rotation_euler = (look_at - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    sun_data = bpy.data.lights.new("IconSun", "SUN")
    sun_data.energy = 3.0
    sun = bpy.data.objects.new("IconSun", sun_data)
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(30))
    scene.collection.objects.link(sun)

    world = bpy.data.worlds.get("Item_IconWorld") or bpy.data.worlds.new("Item_IconWorld")
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
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=scene.name)


def save_blend(path, scene):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Write the objects rather than the scene: copying a scene made in background mode can crash Blender 5.2.
    bpy.data.libraries.write(path, set(scene.objects), fake_user=True)
