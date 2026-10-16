"""Unwraps the Megazord parts into one atlas and bakes a hand-painted-style texture for the game's toon shader.

The game's own cosmetics use painterly textures: flat color areas with soft gradient shading, darker creases, light rim
highlights on edges and thin dark linework. This bakes the same ingredients from the model's geometry: ambient occlusion
in the creases, bevel-based edge highlights, a vertical gradient, brush-like noise and thin ink lines.
"""
import math
import os

import bpy
from mathutils import Vector

import common

SIZE = 2048


def _mix(nt, blend, fac, a, b, x, y):
    """A color Mix node. fac/a/b are either a socket to link or a constant (float / RGBA tuple)."""
    node = nt.nodes.new("ShaderNodeMix")
    node.data_type = "RGBA"
    node.blend_type = blend
    node.location = (x, y)
    for index, value in ((0, fac), (6, a), (7, b)):
        if hasattr(value, "node"):
            nt.links.new(value, node.inputs[index])
        else:
            node.inputs[index].default_value = value
    return node.outputs[2]


def _map(nt, value, from_min, from_max, to_min, to_max, x, y):
    node = nt.nodes.new("ShaderNodeMapRange")
    node.location = (x, y)
    node.clamp = True
    nt.links.new(value, node.inputs["Value"])
    node.inputs["From Min"].default_value = from_min
    node.inputs["From Max"].default_value = from_max
    node.inputs["To Min"].default_value = to_min
    node.inputs["To Max"].default_value = to_max
    return node.outputs["Result"]


def _bake_material(mat, rgb_srgb, line_strength, image):
    if hasattr(mat, "use_nodes"):
        mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    def new(kind, x, y):
        node = nt.nodes.new(kind)
        node.location = (x, y)
        return node

    lin = tuple(common.srgb_to_linear(c) for c in rgb_srgb)
    geo = new("ShaderNodeNewGeometry", -1400, 300)
    texco = new("ShaderNodeTexCoord", -1400, 0)
    base = new("ShaderNodeRGB", -1200, 500)
    base.outputs[0].default_value = (*lin, 1.0)

    ao = new("ShaderNodeAmbientOcclusion", -950, 500)
    ao.samples = 16
    ao.inside = False
    ao.inputs["Distance"].default_value = 0.25
    nt.links.new(base.outputs[0], ao.inputs["Color"])

    # Edge highlights: where the bevel-smoothed normal departs from the face normal.
    bevel = new("ShaderNodeBevel", -1200, 250)
    bevel.samples = 10
    bevel.inputs["Radius"].default_value = 0.035
    dot = new("ShaderNodeVectorMath", -950, 250)
    dot.operation = "DOT_PRODUCT"
    nt.links.new(bevel.outputs["Normal"], dot.inputs[0])
    nt.links.new(geo.outputs["Normal"], dot.inputs[1])
    edge = _map(nt, dot.outputs["Value"], 1.0, 0.82, 0.0, 0.35, -700, 250)

    # Thin ink lines: the cell edges of a coarse voronoi pattern.
    voro = new("ShaderNodeTexVoronoi", -1200, 0)
    voro.feature = "DISTANCE_TO_EDGE"
    voro.inputs["Scale"].default_value = 2.4
    nt.links.new(texco.outputs["Object"], voro.inputs["Vector"])
    line = _map(nt, voro.outputs["Distance"], 0.0, 0.03, line_strength, 0.0, -950, 0)

    # Brush noise and a soft vertical gradient (darker low on the figure).
    noise = new("ShaderNodeTexNoise", -1200, -250)
    noise.inputs["Scale"].default_value = 16.0
    noise.inputs["Detail"].default_value = 5.0
    nt.links.new(texco.outputs["Object"], noise.inputs["Vector"])
    noise_v = _map(nt, noise.outputs["Fac"], 0.0, 1.0, 0.97, 1.03, -950, -250)
    sep = new("ShaderNodeSeparateXYZ", -1200, -500)
    nt.links.new(texco.outputs["Object"], sep.inputs[0])
    grad = _map(nt, sep.outputs["Z"], 0.0, 4.6, 0.92, 1.04, -950, -500)
    shade = new("ShaderNodeMath", -700, -350)
    shade.operation = "MULTIPLY"
    nt.links.new(noise_v, shade.inputs[0])
    nt.links.new(grad, shade.inputs[1])
    shade_col = new("ShaderNodeCombineColor", -500, -350)
    for name in ("Red", "Green", "Blue"):
        nt.links.new(shade.outputs[0], shade_col.inputs[name])

    soft_ao = _mix(nt, "MIX", 0.45, (*lin, 1.0), ao.outputs["Color"], -450, 300)   # only part of the occlusion: the game shader shades too
    c1 = _mix(nt, "MULTIPLY", 1.0, soft_ao, shade_col.outputs[0], -300, 200)
    c2 = _mix(nt, "SCREEN", edge, c1, (0.45, 0.45, 0.45, 1.0), -100, 200)
    c3 = _mix(nt, "MULTIPLY", line, c2, (0.30, 0.30, 0.32, 1.0), 100, 200)

    emit = new("ShaderNodeEmission", 300, 200)
    nt.links.new(c3, emit.inputs["Color"])
    out = new("ShaderNodeOutputMaterial", 500, 200)
    nt.links.new(emit.outputs[0], out.inputs["Surface"])

    img = new("ShaderNodeTexImage", 300, -100)
    img.image = image
    img.select = True
    nt.nodes.active = img


# How strongly ink lines show per material (metals and dark armor get more, the red/yellow/blue paint less).
LINE = {"Red": 0.0, "Black": 0.0, "Blue": 0.0, "Yellow": 0.0, "Pink": 0.0, "Silver": 0.10, "Gold": 0.0,
        "Visor": 0.0, "White": 0.0, "Green": 0.0, "Steel": 0.14}


def unwrap(scene, objs):
    bpy.context.window.scene = scene
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(62), island_margin=0.003)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake(scene, objs, materials, colors, order, out_path):
    """Unwraps, bakes and saves the atlas; returns the image."""
    unwrap(scene, objs)
    image = bpy.data.images.get("ZordTex")
    if image:
        bpy.data.images.remove(image)
    image = bpy.data.images.new("ZordTex", SIZE, SIZE, alpha=False)
    image.colorspace_settings.name = "sRGB"
    for name, mat in zip(order, materials):
        _bake_material(mat, colors[name], LINE.get(name, 0.2), image)

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.bake(type="EMIT", margin=12, use_clear=True)
    image.filepath_raw = out_path
    image.file_format = "PNG"
    image.save()
    return image


def textured_material(image):
    """A material showing the baked atlas (for the preview icon)."""
    mat = bpy.data.materials.get("ZordTextured") or bpy.data.materials.new("ZordTextured")
    if hasattr(mat, "use_nodes"):
        mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.7
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    return mat
