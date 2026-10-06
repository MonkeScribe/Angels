"""
Angels sprite renderer  (Blender 4.2+)
 
Renders a plane from every angle on a sphere around it, then packs the
images into one sprite sheet + a JSON file the game can read.
 
HOW TO USE
  1. Import the plane into Blender and SAVE the .blend file
     (images are written next to it).
  2. Scripting tab -> Open -> pick this file (or paste it into a new text).
  3. Edit the SETTINGS block below if needed, then press Run Script.
  4. Tip: do a quick test first with STEP = 45, and check the files:
       az000_el+00.png  must be the plane head-on (propeller toward you),
       az090_el+00.png  its left side, nose pointing LEFT in the picture,
       az180_el+00.png  its tail-on, fin pointing up.
     If az000 is not head-on, change NOSE_YAW_OFFSET below.
 
Nothing in your scene is changed permanently: the script makes its own
temporary camera and sun, and puts every setting back when it's done.
"""
 
import bpy
import math
import os
import json
import numpy as np
from mathutils import Euler, Vector
 
# =============================== SETTINGS ===============================
PLANE_OBJECTS      = []        # object names of the plane ([] = every visible mesh)
NOSE_YAW_OFFSET    = 90        # makes az000 the NOSE-ON view. With 0 the sheet starts at the plane's right side, so az 0-180
                               # covers right side -> nose -> left side and the whole rear half is missing.
                               # With 90: az 0 = nose-on, 90 = left side, 180 = tail-on, and the game flips these for the
                               # right side. (Still not nose-on? try 0, 180 or 270; the plane may face another way.)
STEP               = 15        # degrees between shots (must divide 90: 5, 10, 15, 30, 45, 90)
MIRROR             = True      # plane is left/right symmetric: render half, flip in-game
FRAME_SIZE         = 256       # pixels per frame (square)
MARGIN             = 1.05      # 1.0 = plane just touches the frame edge at its widest angle
 
LIGHTING           = "script"  # "script" = sun that follows the camera (consistent sprites)
                               # "scene"  = use the lights already in your scene
SUN_STRENGTH       = 4.0
SUN_OFFSET_UP      = 25        # degrees the light comes from above the camera
SUN_OFFSET_SIDE    = 30        # degrees the light comes from the side (negative = other side)
 
HIDE_DURING_RENDER = []        # parts of object names to hide, e.g. ["prop", "blade"]
SAMPLES            = 32        # render quality (EEVEE samples)
STANDARD_COLORS    = True      # True = colours match the textures (best for games)
 
OUT_DIR            = "//sprites/"   # "//" = folder next to the .blend file
MAKE_SPRITE_SHEET  = True
SHEET_NAME         = "spitfire"
# ========================================================================
 
 
scene = bpy.context.scene
 
 
def log(msg):
    print(f"[sprites] {msg}")
 
 
def collect_plane_meshes():
    if PLANE_OBJECTS:
        objs = []
        for name in PLANE_OBJECTS:
            o = bpy.data.objects.get(name)
            if o is None:
                raise RuntimeError(f"No object called '{name}' (check PLANE_OBJECTS)")
            objs += [o] + list(o.children_recursive)
        meshes = [o for o in objs if o.type == "MESH"]
    else:
        meshes = [o for o in scene.objects
                  if o.type == "MESH" and o.visible_get() and not o.hide_render]
    if not meshes:
        raise RuntimeError("No visible mesh objects found to render")
    return meshes
 
 
def bounding_sphere(objs):
    """Centre of the plane and the radius that contains every vertex."""
    dg = bpy.context.evaluated_depsgraph_get()
    chunks = []
    for o in objs:
        eo = o.evaluated_get(dg)
        me = eo.to_mesh()
        n = len(me.vertices)
        if n:
            co = np.empty(n * 3, dtype=np.float32)
            me.vertices.foreach_get("co", co)
            co = co.reshape(-1, 3).astype(np.float64)
            m = np.array(eo.matrix_world)
            chunks.append(co @ m[:3, :3].T + m[:3, 3])
        eo.to_mesh_clear()
    pts = np.concatenate(chunks)
    centre = (pts.min(axis=0) + pts.max(axis=0)) / 2
    radius = float(np.linalg.norm(pts - centre, axis=1).max())
    return Vector(centre), radius
 
 
def set_engine():
    r = scene.render
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            r.engine = eng
            scene.eevee.taa_render_samples = SAMPLES
            return
        except (TypeError, AttributeError):
            continue
    log(f"EEVEE not available, rendering with {r.engine}")
 
 
def main():
    if 90 % STEP != 0:
        raise RuntimeError("STEP must divide 90 evenly (5, 10, 15, 30, 45 or 90)")
    if OUT_DIR.startswith("//") and not bpy.data.filepath:
        raise RuntimeError("Save the .blend file first (images are saved next to it)")
 
    out = bpy.path.abspath(OUT_DIR)
    os.makedirs(out, exist_ok=True)
 
    r = scene.render
    vs = scene.view_settings
    saved = dict(
        engine=r.engine, rx=r.resolution_x, ry=r.resolution_y,
        pct=r.resolution_percentage, transparent=r.film_transparent,
        fmt=r.image_settings.file_format, mode=r.image_settings.color_mode,
        path=r.filepath, view=vs.view_transform, look=vs.look, camera=scene.camera,
    )
    hidden = []
    temp_objects = []
 
    try:
        # ---------- frame the plane ----------
        meshes = collect_plane_meshes()
        centre, radius = bounding_sphere(meshes)
        log(f"Plane centre {tuple(round(v, 2) for v in centre)}, radius {radius:.2f} m")
 
        # ---------- render settings ----------
        set_engine()
        r.resolution_x = r.resolution_y = FRAME_SIZE
        r.resolution_percentage = 100
        r.film_transparent = True
        r.image_settings.file_format = "PNG"
        r.image_settings.color_mode = "RGBA"
        if STANDARD_COLORS:
            vs.view_transform = "Standard"
            vs.look = "None"
 
        # ---------- temporary camera ----------
        dist = radius * 4
        cam_data = bpy.data.cameras.new("SpriteCam")
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = 2 * radius * MARGIN
        cam_data.clip_start = max(0.001, dist - radius * 1.5)
        cam_data.clip_end = dist + radius * 1.5
        cam = bpy.data.objects.new("SpriteCam", cam_data)
        scene.collection.objects.link(cam)
        temp_objects.append(cam)
        scene.camera = cam
        cam.rotation_mode = "XYZ"
 
        # ---------- lighting ----------
        sun = None
        if LIGHTING == "script":
            for o in scene.objects:
                if o.type == "LIGHT" and not o.hide_render:
                    o.hide_render = True
                    hidden.append(o)
            sun_data = bpy.data.lights.new("SpriteSun", "SUN")
            sun_data.energy = SUN_STRENGTH
            sun = bpy.data.objects.new("SpriteSun", sun_data)
            scene.collection.objects.link(sun)
            temp_objects.append(sun)
            sun.rotation_mode = "XYZ"
        sun_offset = Euler((math.radians(-SUN_OFFSET_UP),
                            math.radians(SUN_OFFSET_SIDE), 0)).to_matrix()
 
        # ---------- hide parts (e.g. propeller blades) ----------
        frags = [f.lower() for f in HIDE_DURING_RENDER]
        for o in scene.objects:
            if frags and any(f in o.name.lower() for f in frags) and not o.hide_render:
                o.hide_render = True
                hidden.append(o)
 
        # ---------- render every angle ----------
        yaws = list(range(0, (180 if MIRROR else 360 - STEP) + 1, STEP))
        pitches = list(range(90, -91, -STEP))          # top row = view from above
        shots = [(y, p) for p in pitches for y in yaws]
        log(f"Rendering {len(shots)} frames ({len(yaws)} x {len(pitches)}) to {out}")
 
        frames = []
        for i, (yaw, pitch) in enumerate(shots, 1):
            rot = Euler((math.radians(90 - pitch), 0,
                         math.radians(yaw + NOSE_YAW_OFFSET)), "XYZ")
            forward = rot.to_matrix() @ Vector((0, 0, -1))
            cam.rotation_euler = rot
            cam.location = centre - forward * dist
            if sun:
                sun.rotation_euler = (rot.to_matrix() @ sun_offset).to_euler("XYZ")
                sun.location = cam.location
 
            name = f"az{yaw:03d}_el{pitch:+03d}.png"
            path = os.path.join(out, name)
            r.filepath = path
            bpy.ops.render.render(write_still=True)
            frames.append(dict(az=yaw, el=pitch, file=name, path=path,
                               col=yaws.index(yaw), row=pitches.index(pitch)))
            log(f"{i}/{len(shots)}  {name}")
 
        # ---------- sprite sheet + manifest ----------
        if MAKE_SPRITE_SHEET:
            make_sheet(frames, len(yaws), len(pitches), out)
 
        log("Done!")
 
    finally:
        for o in temp_objects:
            data = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if isinstance(data, bpy.types.Camera):
                bpy.data.cameras.remove(data)
            elif isinstance(data, bpy.types.Light):
                bpy.data.lights.remove(data)
        for o in hidden:
            o.hide_render = False
        r.engine = saved["engine"]
        r.resolution_x, r.resolution_y = saved["rx"], saved["ry"]
        r.resolution_percentage = saved["pct"]
        r.film_transparent = saved["transparent"]
        r.image_settings.file_format = saved["fmt"]
        r.image_settings.color_mode = saved["mode"]
        r.filepath = saved["path"]
        vs.view_transform = saved["view"]
        vs.look = saved["look"]
        scene.camera = saved["camera"]
 
 
def make_sheet(frames, cols, rows, out):
    S = FRAME_SIZE
    W, H = cols * S, rows * S
    if max(W, H) > 4096:
        log(f"Warning: sheet is {W}x{H}; some devices can't load textures over 4096 px")
 
    sheet = np.zeros((H, W, 4), dtype=np.float32)
    for f in frames:
        img = bpy.data.images.load(f["path"], check_existing=False)
        px = np.empty(S * S * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        bpy.data.images.remove(img)
        tile = px.reshape(S, S, 4)                     # Blender stores rows bottom-up
        y0 = H - (f["row"] + 1) * S
        x0 = f["col"] * S
        sheet[y0:y0 + S, x0:x0 + S] = tile
 
    sheet_file = f"{SHEET_NAME}_sheet.png"
    img = bpy.data.images.new(f"{SHEET_NAME}_sheet", W, H, alpha=True)
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw = os.path.join(out, sheet_file)
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
 
    manifest = dict(
        image=sheet_file,
        frameSize=S,
        step=STEP,
        columns=cols,
        rows=rows,
        mirrored=MIRROR,
        layout=("column = azimuth / step (az 0 = nose-on, increasing counter-clockwise "
                "seen from above, 90 = the plane's left side, 180 = tail-on); row = "
                "(90 - elevation) / step (row 0 = seen from directly above). With "
                "mirrored=true, azimuths over 180 use frame (360 - az) flipped "
                "horizontally."),
        noseYawOffset=NOSE_YAW_OFFSET,
        frames=[dict(az=f["az"], el=f["el"], x=f["col"] * S, y=f["row"] * S)
                for f in frames],
    )
    with open(os.path.join(out, f"{SHEET_NAME}_sheet.json"), "w") as fh:
        json.dump(manifest, fh, indent=1)
    log(f"Sprite sheet {W}x{H} saved as {sheet_file} (+ .json)")
 
 
main()