"""Makes the Dirt Blocks assets: a 16x16 pixel-art dirt texture and an icon of a block wearing it.

    blender --background --factory-startup --python blender/DirtBlocks/build_dirt.py

Outputs in Assets/DirtBlocks: dirt.png (the in-game block texture) and dirt_block_icon.png.
The texture is generated here (random speckles in a few browns), not copied from Minecraft.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import common  # noqa: E402
from mathutils import Matrix  # noqa: E402

KEY = "DirtBlocks"
PALETTE = [  # sRGB browns, dark to light
    (0.35, 0.24, 0.15), (0.45, 0.31, 0.2), (0.53, 0.37, 0.25), (0.58, 0.42, 0.29), (0.66, 0.5, 0.36),
]


def make_texture(path):
    rng = random.Random(1337)
    image = bpy.data.images.new("dirt", 16, 16, alpha=False)
    image.colorspace_settings.name = "sRGB"
    pixels = []
    for y in range(16):
        for x in range(16):
            roll = rng.random()
            shade = 2 if roll < 0.45 else 1 if roll < 0.7 else 3 if roll < 0.88 else 0 if roll < 0.95 else 4
            r, g, b = PALETTE[shade]
            pixels.extend((r, g, b, 1.0))
    image.pixels = pixels  # 8-bit sRGB image: values are stored as given
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return image


def main():
    assets = common.assets_dir(KEY)
    image = make_texture(os.path.join(assets, "dirt.png"))

    scene = common.fresh_scene("DirtBlock")
    mesh = bpy.data.meshes.new("DirtBlock")
    bm = bmesh.new()
    bm.loops.layers.uv.new()  # create_cube only writes UVs into an existing layer
    bmesh.ops.create_cube(bm, size=1.0, calc_uvs=True)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("DirtBlock", mesh)
    scene.collection.objects.link(obj)

    mat = bpy.data.materials.new("Dirt")
    if mat.node_tree is None and hasattr(mat, "use_nodes"):
        mat.use_nodes = True
    nodes = mat.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"  # crisp pixels
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.9
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    mesh.materials.append(mat)

    common.render_icon(scene, os.path.join(assets, "dirt_block_icon.png"), ortho_scale=1.85,
                       look_at=(0, 0, 0), cam_offset=(1.5, -1.5, 1.25))
    common.save_blend(os.path.join(common.ROOT, "blender", KEY, "dirt_block.blend"), scene)
    print(f"RESULT wrote {sorted(os.listdir(assets))}")


main()
