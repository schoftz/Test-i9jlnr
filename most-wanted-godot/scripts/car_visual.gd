class_name CarVisual
extends RefCounted
## Araç görselleri:
##  1) res://models/custom/<id>.glb varsa gerçek model yüklenir (otomatik ölçek/merkez/yön).
##  2) Yoksa araç silüetine göre "loft" edilmiş yumuşak prosedürel gövde üretilir
##     (çamurluk boşlukları, kabin/cam, far/stop şekilleri, kanat, ayna, egzoz).
## Yerel eksen: ileri = +Z, yukarı = +Y, zemin ~ y=0.

## Silüet tablosu (yükseklikler toplam yüksekliğin oranı, u: -1 arka .. +1 ön)
const PROFILES := {
	"hatch":    {"nose": 0.50, "hood": 0.64, "deck": 0.68, "tail": 0.64, "cr": -0.93, "cf": 0.30, "ws": 0.30, "rw": 0.10, "clear": 0.15, "r": 0.33, "wb": 0.62},
	"sedan":    {"nose": 0.47, "hood": 0.61, "deck": 0.65, "tail": 0.61, "cr": -0.62, "cf": 0.30, "ws": 0.28, "rw": 0.25, "clear": 0.15, "r": 0.34, "wb": 0.58},
	"coupe":    {"nose": 0.45, "hood": 0.59, "deck": 0.64, "tail": 0.62, "cr": -0.66, "cf": 0.26, "ws": 0.30, "rw": 0.38, "clear": 0.13, "r": 0.34, "wb": 0.58},
	"911":      {"nose": 0.40, "hood": 0.53, "deck": 0.66, "tail": 0.62, "cr": -0.86, "cf": 0.22, "ws": 0.32, "rw": 0.58, "clear": 0.12, "r": 0.34, "wb": 0.53},
	"longhood": {"nose": 0.45, "hood": 0.60, "deck": 0.64, "tail": 0.62, "cr": -0.80, "cf": -0.05, "ws": 0.33, "rw": 0.50, "clear": 0.12, "r": 0.36, "wb": 0.58},
	"super":    {"nose": 0.40, "hood": 0.50, "deck": 0.72, "tail": 0.68, "cr": -0.50, "cf": 0.50, "ws": 0.38, "rw": 0.30, "clear": 0.11, "r": 0.36, "wb": 0.60},
	"wedge":    {"nose": 0.33, "hood": 0.47, "deck": 0.74, "tail": 0.72, "cr": -0.42, "cf": 0.58, "ws": 0.48, "rw": 0.36, "clear": 0.11, "r": 0.36, "wb": 0.60},
	"van":      {"nose": 0.48, "hood": 0.55, "deck": 0.95, "tail": 0.95, "cr": -0.97, "cf": 0.55, "ws": 0.18, "rw": 0.03, "clear": 0.18, "r": 0.35, "wb": 0.62},
	"suv":      {"nose": 0.52, "hood": 0.64, "deck": 0.70, "tail": 0.70, "cr": -0.92, "cf": 0.34, "ws": 0.25, "rw": 0.08, "clear": 0.22, "r": 0.38, "wb": 0.60},
}
const WIDE_BODIES := ["super", "wedge", "longhood"]
const WHEEL_WORDS := ["wheel", "tire", "tyre", "rim", "whl", "reifen", "felge", "rueda", "roue", "tekerlek"]

static var _shared := {}


static func _mat(key: String, c: Color, metallic: float, rough: float) -> StandardMaterial3D:
	if _shared.has(key):
		return _shared[key]
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.metallic = metallic
	m.roughness = rough
	_shared[key] = m
	return m


static func emissive(c: Color, energy: float) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.emission_enabled = true
	m.emission = c
	m.emission_energy_multiplier = energy
	return m


static func paint_mat(c: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.metallic = 0.6
	m.roughness = 0.28
	m.clearcoat_enabled = true
	m.clearcoat = 1.0
	m.clearcoat_roughness = 0.05
	return m


static func glass_mat() -> StandardMaterial3D:
	if _shared.has("glass"):
		return _shared["glass"]
	var m := StandardMaterial3D.new()
	m.albedo_color = Color(0.04, 0.06, 0.08)
	m.metallic = 0.8
	m.roughness = 0.04
	m.clearcoat_enabled = true
	m.clearcoat = 1.0
	_shared["glass"] = m
	return m


## Ana giriş: aracı kurar ve sonuç bilgisini döndürür.
static func build(car: Node3D, cfg: Dictionary, kind: String) -> Dictionary:
	var res := {"lights": [], "siren_mats": [], "siren_lights": [], "headlight": null}
	var body: String = cfg.get("body", "sedan")
	var prof: Dictionary = PROFILES.get(body, PROFILES["sedan"])
	var L: float = cfg.get("len", 4.5)
	var W: float = cfg.get("wid", 1.8)
	var H: float = cfg.get("hgt", 1.4)
	var r: float = prof["r"]
	var wb_half: float = L * prof["wb"] * 0.5
	var wz_f := wb_half + L * (0.02 if body in ["911"] else 0.0) - (0.06 if body == "911" else 0.0)
	var wz_r := -wb_half + (0.12 if body == "911" else 0.0)
	var wx := W * 0.5 - 0.16
	var col: Color = cfg.get("color", Color.WHITE)
	var pm := paint_mat(col)
	res["paint"] = pm
	res["dims"] = Vector3(W, H, L)
	res["wheel_radius"] = r
	res["wheels"] = [Vector3(wx, r + 0.1, wz_f), Vector3(-wx, r + 0.1, wz_f), Vector3(wx, r + 0.1, wz_r), Vector3(-wx, r + 0.1, wz_r)]
	res["show_wheels"] = true
	var model_path: String = cfg.get("model", "")
	var root := Node3D.new()
	root.name = "Visual"
	car.add_child(root)
	res["root"] = root
	if cfg.has("pack_file"):
		var node := pack_instance(cfg["pack_file"], cfg["pack_index"])
		if node:
			var info0 := _fit_scene(node, "", L, col)
			if not info0.is_empty():
				root.add_child(info0["node"])
				res["show_wheels"] = info0["had_wheels"]
				res["dims"] = info0["dims"]
				res["paint"] = info0.get("paint", pm)
				_add_lights(root, res, info0["dims"].z, info0["dims"].x, prof, info0["dims"].y, kind, cfg, true)
				return res
	if model_path != "":
		var info := _load_model(model_path, L, col)
		if not info.is_empty():
			var m: Node3D = info["node"]
			root.add_child(m)
			res["show_wheels"] = info["had_wheels"]
			var d: Vector3 = info["dims"]
			res["dims"] = d
			res["paint"] = info.get("paint", pm)
			_add_lights(root, res, d.z, d.x, prof, d.y, kind, cfg, true)
			return res
	_build_procedural(root, res, cfg, prof, L, W, H, r, wz_f, wz_r, pm, kind)
	return res


# ------------------------------------------------------------ prosedürel gövde

static func _ss(e: float, n: float) -> float:
	return signf(e) * pow(absf(e), 2.0 / n)


static func _smooth(a: float, b: float, x: float) -> float:
	return smoothstep(a, b, x)


static func _profile_top(u: float, p: Dictionary, H: float) -> float:
	var cr: float = p["cr"]
	var cf: float = p["cf"]
	var top: float
	if u >= cf:
		var t := clampf((u - cf) / maxf(1.0 - cf, 0.01), 0.0, 1.0)
		# kaput -> burun: sona doğru yuvarlak düşüş
		top = lerpf(p["hood"], p["nose"], pow(_smooth(0.35, 1.0, t), 1.4))
	elif u <= cr:
		var t2 := clampf((cr - u) / maxf(1.0 + cr, 0.01), 0.0, 1.0)
		top = lerpf(p["deck"], p["tail"], _smooth(0.3, 1.0, t2))
	else:
		top = lerpf(p["deck"], p["hood"], (u - cr) / (cf - cr))
	return top * H


static func _build_procedural(root: Node3D, res: Dictionary, cfg: Dictionary, p: Dictionary, L: float, W: float, H: float, r: float, wz_f: float, wz_r: float, pm: StandardMaterial3D, kind: String) -> void:
	var body: String = cfg.get("body", "sedan")
	var wide: bool = body in WIDE_BODIES
	var clear: float = p["clear"]
	var M := 18
	var rings: Array = []
	var belt_at := []
	var steps := 64
	for s in steps + 1:
		var u := -1.0 + 2.0 * float(s) / steps
		var z := u * L * 0.5
		var e := sqrt(maxf(0.0, 1.0 - pow(absf(u), 7.0)))
		e = maxf(e, 0.18)
		var top := _profile_top(u, p, H)
		var bot := clear + 0.06
		# çamurluk boşlukları
		for wz in [wz_f, wz_r]:
			var dz: float = absf(z - wz)
			var ar := r + 0.07
			if dz < ar:
				bot = maxf(bot, (r + 0.1) + sqrt(ar * ar - dz * dz) * 0.92 - 0.02)
		# uçlarda alt kenar yükselir (tampon kavisi)
		bot = lerpf(bot, maxf(bot, top - 0.25), _smooth(0.86, 1.0, absf(u)))
		var hw := W * 0.5 * (0.80 + 0.20 * e)
		# çamurluk şişkinliği (geniş gövdelerde belirgin)
		for wz in [wz_f, wz_r]:
			var g := exp(-pow((z - wz) / (r * 1.6), 2.0))
			hw += g * (0.045 if wide else 0.02)
		# ön/arka uç yuvarlatma
		top = lerpf(bot + 0.05, top, 0.55 + 0.45 * e)
		var cy := (top + bot) * 0.5
		var hy := maxf((top - bot) * 0.5, 0.02)
		var ring := PackedVector3Array()
		for k in M:
			var th := TAU * float(k) / M
			var cx := cos(th)
			var sy := sin(th)
			var x := hw * _ss(cx, 5.0)
			var y := cy + hy * _ss(sy, 4.0)
			x *= 1.0 - 0.07 * maxf(0.0, sy) - 0.06 * maxf(0.0, -sy)
			ring.append(Vector3(x, y, z))
		rings.append(ring)
		belt_at.append([z, top, hw])
	var st_body := _loft(rings, true)
	var mi := MeshInstance3D.new()
	mi.mesh = st_body
	mi.material_override = pm
	root.add_child(mi)
	# ---- kabin / cam
	var cr: float = p["cr"]
	var cf: float = p["cf"]
	var crings: Array = []
	var csteps := 24
	for s in csteps + 1:
		var c := float(s) / csteps
		var u := lerpf(cr, cf, c)
		var z := u * L * 0.5
		var belt := _profile_top(u, p, H)
		var f := _smooth(0.0, p["rw"], c) * (1.0 - _smooth(1.0 - p["ws"], 1.0, c))
		if p["rw"] < 0.05:
			f = (1.0 if c > 0.0 else 0.0) * (1.0 - _smooth(1.0 - p["ws"], 1.0, c))
		var top := belt - 0.03 + (H - belt + 0.03) * f
		var bot := belt - 0.06
		var hw_b := W * 0.5 * 0.94
		var hw_t := W * 0.5 * (0.70 if body != "van" else 0.86)
		var ring := PackedVector3Array()
		for k in M:
			var th := TAU * float(k) / M
			var cx := cos(th)
			var sy := sin(th)
			var y := (top + bot) * 0.5 + (top - bot) * 0.5 * _ss(sy, 6.0)
			var fy := clampf((y - bot) / maxf(top - bot, 0.01), 0.0, 1.0)
			var x := lerpf(hw_b, hw_t, fy) * _ss(cx, 6.0)
			ring.append(Vector3(x, y, z))
		crings.append(ring)
	var cab := _loft_split(crings, H, pm, glass_mat())
	root.add_child(cab)
	# ---- detaylar
	var dark := _mat("dark", Color(0.03, 0.03, 0.035), 0.3, 0.7)
	var chrome := _mat("chrome", Color(0.8, 0.8, 0.82), 1.0, 0.15)
	var nose_y: float = p["nose"] * H
	var hood_y: float = p["hood"] * H
	var tail_y: float = p["tail"] * H
	# ön ızgara / hava girişi
	var grill_w := W * (0.62 if body in ["super", "wedge"] else 0.45)
	_box(root, Vector3(grill_w, 0.14 if body != "super" else 0.22, 0.06), Vector3(0, clear + 0.2, L * 0.5 - 0.04), dark)
	if body == "super":
		# R8 yan "blade" paneli
		for sgn in [-1.0, 1.0]:
			_box(root, Vector3(0.03, H * 0.32, L * 0.16), Vector3(sgn * (W * 0.5 - 0.02), H * 0.45, -L * 0.12), _mat("blade", Color(0.12, 0.12, 0.13), 0.9, 0.3))
	if body == "wedge":
		for sgn in [-1.0, 1.0]:
			_box(root, Vector3(0.04, 0.14, 0.5), Vector3(sgn * (W * 0.5 - 0.05), H * 0.62, -L * 0.1), dark)
	# farlar
	var head := emissive(Color(1.0, 0.97, 0.9), 3.0)
	var lights: String = cfg.get("lights", "slim")
	for sgn in [-1.0, 1.0]:
		var fz := L * 0.5 - 0.1
		match lights:
			"round":
				var sph := MeshInstance3D.new()
				var sm := SphereMesh.new()
				sm.radius = 0.13
				sm.height = 0.18
				sph.mesh = sm
				sph.material_override = head
				sph.position = Vector3(sgn * W * 0.33, nose_y + 0.06, L * 0.5 - 0.3)
				sph.rotation.x = -0.5
				root.add_child(sph)
			"quad":
				for q in 2:
					var cyl := _cyl(root, 0.075, 0.04, Vector3(sgn * (W * 0.24 + q * 0.17), nose_y - 0.02, fz + 0.02), head)
					cyl.rotation.x = PI * 0.5
			_:
				var hb := _box(root, Vector3(0.42, 0.075, 0.12), Vector3(sgn * W * 0.32, nose_y - 0.03, fz - 0.02), head)
				hb.rotation.y = sgn * 0.25
				hb.rotation.x = 0.35
	# stoplar
	var tail := emissive(Color(1.0, 0.03, 0.02), 2.2)
	res["lights"].append(tail)
	var tz := -L * 0.5 + 0.02
	var tail_kind: String = cfg.get("tail", "bar")
	match tail_kind:
		"round", "round4":
			for sgn in [-1.0, 1.0]:
				var n := 2 if tail_kind == "round4" else 1
				for q in n:
					var cyl := _cyl(root, 0.085, 0.04, Vector3(sgn * (W * 0.36 - q * 0.22), tail_y - 0.12, tz - 0.02), tail)
					cyl.rotation.x = PI * 0.5
		"slim":
			_box(root, Vector3(W * 0.82, 0.05, 0.05), Vector3(0, tail_y - 0.07, tz), tail)
		_:
			for sgn in [-1.0, 1.0]:
				_box(root, Vector3(0.45, 0.13, 0.05), Vector3(sgn * W * 0.31, tail_y - 0.12, tz), tail)
	# plaka
	_box(root, Vector3(0.52, 0.12, 0.02), Vector3(0, clear + 0.32, -L * 0.5 - 0.005), _mat("plate", Color(0.92, 0.92, 0.95), 0.0, 0.5))
	_box(root, Vector3(0.52, 0.12, 0.02), Vector3(0, clear + 0.25, L * 0.5 + 0.005), _mat("plate", Color(0.92, 0.92, 0.95), 0.0, 0.5))
	# aynalar
	var mz := lerpf(cr, cf, 0.86) * L * 0.5
	var my := _profile_top(lerpf(cr, cf, 0.86), p, H) + 0.08
	for sgn in [-1.0, 1.0]:
		_box(root, Vector3(0.16, 0.09, 0.12), Vector3(sgn * (W * 0.5 + 0.03), my, mz), pm)
	# egzoz
	for sgn in [-1.0, 1.0]:
		var ex := _cyl(root, 0.045, 0.2, Vector3(sgn * W * 0.25, clear + 0.12, -L * 0.5 + 0.02), chrome)
		ex.rotation.x = PI * 0.5
	# spoiler
	match cfg.get("spoiler", "none"):
		"wing":
			var wy := tail_y + (0.32 if body != "911" else 0.24)
			var wz := -L * 0.5 + 0.28
			_box(root, Vector3(W * 0.92, 0.04, 0.32), Vector3(0, wy, wz), pm)
			for sgn in [-1.0, 1.0]:
				_box(root, Vector3(0.04, 0.12, 0.34), Vector3(sgn * W * 0.46, wy + 0.04, wz), pm)
				_box(root, Vector3(0.05, wy - tail_y + 0.02, 0.1), Vector3(sgn * W * 0.3, (wy + tail_y) * 0.5, wz), dark)
		"lip":
			_box(root, Vector3(W * 0.78, 0.035, 0.16), Vector3(0, tail_y + 0.02, -L * 0.5 + 0.12), pm)
	_add_lights(root, res, L, W, p, H, kind, cfg, false)


static func _add_lights(root: Node3D, res: Dictionary, L: float, W: float, p: Dictionary, H: float, kind: String, cfg: Dictionary, from_model: bool) -> void:
	if from_model:
		var tail := emissive(Color(1.0, 0.03, 0.02), 1.5)
		res["lights"].append(tail)
	if kind == "player" or kind == "police" or kind == "racer":
		var hl := SpotLight3D.new()
		hl.light_color = Color(1.0, 0.95, 0.85)
		hl.light_energy = 3.0
		hl.spot_range = 40.0
		hl.spot_angle = 30.0
		hl.shadow_enabled = false
		hl.position = Vector3(0, p["nose"] * H, L * 0.5 + 0.2)
		hl.rotation = Vector3(deg_to_rad(-6), PI, 0)
		root.add_child(hl)
		res["headlight"] = hl
	if kind == "police":
		var bar_y := H + 0.06
		var bz: float = lerpf(p["cr"], p["cf"], 0.45) * L * 0.5
		_box(root, Vector3(1.2, 0.07, 0.28), Vector3(0, bar_y - 0.03, bz), _mat("dark", Color(0.03, 0.03, 0.035), 0.3, 0.7))
		var red := emissive(Color(1, 0.05, 0.05), 6.0)
		var blue := emissive(Color(0.1, 0.25, 1), 6.0)
		res["siren_mats"] = [red, blue]
		_box(root, Vector3(0.52, 0.11, 0.24), Vector3(0.3, bar_y + 0.04, bz), red)
		_box(root, Vector3(0.52, 0.11, 0.24), Vector3(-0.3, bar_y + 0.04, bz), blue)
		if not from_model:
			var white := _mat("police_white", Color(0.94, 0.94, 0.96), 0.4, 0.3)
			for sgn in [-1.0, 1.0]:
				_box(root, Vector3(0.012, 0.32, L * 0.42), Vector3(sgn * (W * 0.5 + 0.005), H * 0.42, 0.05), white)
			_box(root, Vector3(W * 0.7, 0.012, 0.8), Vector3(0, H + 0.002, bz), white)
			_box(root, Vector3(W * 0.95, 0.3, 0.15), Vector3(0, 0.45, L * 0.5 + 0.06), _mat("dark", Color(0.03, 0.03, 0.035), 0.3, 0.7))
		var lbl := Label3D.new()
		lbl.text = "POLİS"
		lbl.font_size = 40
		lbl.pixel_size = 0.006
		lbl.modulate = Color(0.1, 0.15, 0.4)
		lbl.position = Vector3(W * 0.5 + 0.03, H * 0.42, 0.05)
		lbl.rotation.y = PI * 0.5
		root.add_child(lbl)
		var lbl2: Label3D = lbl.duplicate()
		lbl2.position.x = -lbl.position.x
		lbl2.rotation.y = -PI * 0.5
		root.add_child(lbl2)
		for i in 2:
			var l := OmniLight3D.new()
			l.light_color = Color(1, 0.1, 0.1) if i == 0 else Color(0.15, 0.25, 1)
			l.omni_range = 12.0
			l.light_energy = 0.0
			l.shadow_enabled = false
			l.position = Vector3(0.6 if i == 0 else -0.6, bar_y + 0.4, bz)
			root.add_child(l)
			res["siren_lights"].append(l)


static func _box(parent: Node3D, size: Vector3, pos: Vector3, mat: Material) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = mat
	mi.position = pos
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	parent.add_child(mi)
	return mi


static func _cyl(parent: Node3D, radius: float, h: float, pos: Vector3, mat: Material) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = radius
	cm.bottom_radius = radius
	cm.height = h
	cm.radial_segments = 14
	cm.rings = 1
	mi.mesh = cm
	mi.material_override = mat
	mi.position = pos
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	parent.add_child(mi)
	return mi


## Halka dizisinden kapalı yüzey (uç kapaklı). Normaller dışa doğru hesaplanır.
static func _loft(rings: Array, caps: bool) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var n: int = rings.size()
	var m: int = (rings[0] as PackedVector3Array).size()
	for i in n - 1:
		var a: PackedVector3Array = rings[i]
		var b: PackedVector3Array = rings[i + 1]
		for k in m:
			var k2 := (k + 1) % m
			_tri(st, a[k], b[k], b[k2])
			_tri(st, a[k], b[k2], a[k2])
	if caps:
		for idx in [0, n - 1]:
			var ring: PackedVector3Array = rings[idx]
			var c := Vector3.ZERO
			for v in ring:
				c += v
			c /= m
			for k in m:
				if idx == 0:
					_tri(st, c, ring[(k + 1) % m], ring[k])
				else:
					_tri(st, c, ring[k], ring[(k + 1) % m])
	st.generate_normals()
	return st.commit()


## Godot ön yüz = saat yönü (kameradan bakınca). Yüz normalini dışa çevirmek için
## üçgeni ağırlık merkezine göre kontrol ederiz.
static func _tri(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3) -> void:
	var nrm := (c - a).cross(b - a)
	var center := (a + b + c) / 3.0
	var out := Vector3(center.x, center.y - 0.6, center.z * 0.15)
	if nrm.dot(out) < 0.0:
		var t := b
		b = c
		c = t
	st.add_vertex(a)
	st.add_vertex(b)
	st.add_vertex(c)


static func _loft_split(rings: Array, H: float, roof_mat: Material, glass: Material) -> MeshInstance3D:
	var st_roof := SurfaceTool.new()
	var st_glass := SurfaceTool.new()
	st_roof.begin(Mesh.PRIMITIVE_TRIANGLES)
	st_glass.begin(Mesh.PRIMITIVE_TRIANGLES)
	var n: int = rings.size()
	var m: int = (rings[0] as PackedVector3Array).size()
	for i in n - 1:
		var a: PackedVector3Array = rings[i]
		var b: PackedVector3Array = rings[i + 1]
		for k in m:
			var k2 := (k + 1) % m
			for tri in [[a[k], b[k], b[k2]], [a[k], b[k2], a[k2]]]:
				var p0: Vector3 = tri[0]
				var p1: Vector3 = tri[1]
				var p2: Vector3 = tri[2]
				var nrm := (p2 - p0).cross(p1 - p0).normalized()
				var cy := (p0.y + p1.y + p2.y) / 3.0
				var roof := absf(nrm.y) > 0.93 and cy > H - 0.1
				_tri(st_roof if roof else st_glass, p0, p1, p2)
	st_roof.generate_normals()
	st_glass.generate_normals()
	var mesh := st_glass.commit()
	mesh = st_roof.commit(mesh)
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	mi.set_surface_override_material(0, glass)
	if mesh.get_surface_count() > 1:
		mi.set_surface_override_material(1, roof_mat)
	return mi


# ------------------------------------------------------------ tekerlek

static func make_wheel(radius: float, front: bool, side: float) -> Node3D:
	var root := Node3D.new()
	var tire_mesh: Mesh
	var key := "tire_%.2f" % radius
	if _shared.has(key):
		tire_mesh = _shared[key]
	else:
		var cm := CylinderMesh.new()
		cm.top_radius = radius
		cm.bottom_radius = radius
		cm.height = 0.24
		cm.radial_segments = 24
		cm.rings = 1
		cm.material = _mat("tire", Color(0.04, 0.04, 0.04), 0.0, 0.85)
		tire_mesh = cm
		_shared[key] = cm
	var tire := MeshInstance3D.new()
	tire.mesh = tire_mesh
	tire.rotation.z = PI * 0.5
	root.add_child(tire)
	var rim_key := "rim_%.2f" % radius
	var rim_mesh: Mesh
	if _shared.has(rim_key):
		rim_mesh = _shared[rim_key]
	else:
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		var disc := CylinderMesh.new()
		disc.top_radius = radius * 0.72
		disc.bottom_radius = radius * 0.72
		disc.height = 0.03
		disc.radial_segments = 24
		st.append_from(disc, 0, Transform3D(Basis(), Vector3(0, 0.1, 0)))
		var hub := CylinderMesh.new()
		hub.top_radius = radius * 0.16
		hub.bottom_radius = radius * 0.16
		hub.height = 0.06
		st.append_from(hub, 0, Transform3D(Basis(), Vector3(0, 0.125, 0)))
		for s in 5:
			var spoke := BoxMesh.new()
			spoke.size = Vector3(0.05, 0.035, radius * 0.62)
			st.append_from(spoke, 0, Transform3D(Basis(Vector3.UP, TAU * s / 5.0), Vector3(0, 0.125, 0)).translated_local(Vector3(0, 0, radius * 0.33)))
		var arr := st.commit()
		arr.surface_set_material(0, _mat("rim", Color(0.72, 0.73, 0.76), 1.0, 0.22))
		rim_mesh = arr
		_shared[rim_key] = arr
	var rim := MeshInstance3D.new()
	rim.mesh = rim_mesh
	# jant dış tarafa baksın
	rim.rotation.z = -PI * 0.5 if side > 0 else PI * 0.5
	root.add_child(rim)
	return root


# ------------------------------------------------------------ GLB yükleyici

static var _pack_cache := {}


static func _load_scene(path: String) -> Node3D:
	var scene: Node3D = null
	if ResourceLoader.exists(path):
		var ps = load(path)
		if ps is PackedScene:
			scene = (ps as PackedScene).instantiate() as Node3D
	if scene == null:
		var doc := GLTFDocument.new()
		var state := GLTFState.new()
		if doc.append_from_file(ProjectSettings.globalize_path(path), state) == OK:
			scene = doc.generate_scene(state) as Node3D
	return scene


## Paket içindeki araç düğümleri: tek çocuklu sarmalayıcıları atlayıp ilk çok çocuklu seviyeye iner.
static func _pack_root(path: String) -> Node:
	if _pack_cache.has(path):
		return _pack_cache[path]
	var scene := _load_scene(path)
	var cur: Node = scene
	while cur != null and cur.get_child_count() == 1:
		cur = cur.get_child(0)
	_pack_cache[path] = cur
	return cur


static func _pack_children(path: String) -> Array:
	var r := _pack_root(path)
	var out: Array = []
	if r == null:
		return out
	for ch in r.get_children():
		if ch is Node3D and not _is_wheel(ch):
			var meshes: Array = []
			_collect(ch, Transform3D.IDENTITY, meshes)
			if not meshes.is_empty():
				out.append(ch)
	return out


static func pack_car_names(path: String) -> Array:
	var names: Array = []
	for ch in _pack_children(path):
		names.append(String(ch.name))
	return names


static func pack_instance(path: String, index: int) -> Node3D:
	var kids := _pack_children(path)
	if index < 0 or index >= kids.size():
		return null
	var src: Node3D = kids[index]
	var copy := src.duplicate() as Node3D
	copy.transform = Transform3D(src.transform.basis, Vector3.ZERO)
	return copy


static func _load_model(path: String, target_len: float, paint: Color) -> Dictionary:
	var scene := _load_scene(path)
	if scene == null:
		push_warning("Model yüklenemedi: " + path)
		return {}
	return _fit_scene(scene, path, target_len, paint)


static func _fit_scene(scene: Node3D, path: String, target_len: float, paint: Color) -> Dictionary:
	# AABB hesapla
	var meshes: Array = []
	_collect(scene, Transform3D.IDENTITY, meshes)
	if meshes.is_empty():
		return {}
	var had_wheels := false
	var aabb := AABB()
	var first := true
	for entry in meshes:
		var mi: MeshInstance3D = entry[0]
		var xf: Transform3D = entry[1]
		if _is_wheel(mi):
			mi.visible = false
			had_wheels = true
			continue
		var bb: AABB = xf * mi.get_aabb()
		if first:
			aabb = bb
			first = false
		else:
			aabb = aabb.merge(bb)
	# tekerlek gizleyince gövde kutusu yine tüm modeli kapsasın (görsel ölçek için)
	var holder := Node3D.new()
	holder.name = "Model"
	var pivot := Node3D.new()
	holder.add_child(pivot)
	pivot.add_child(scene)
	var rot := 0.0
	if aabb.size.x > aabb.size.z * 1.15:
		rot = PI * 0.5
	if path != "" and path.get_basename().ends_with("_ters"):
		rot += PI
	var long_len := maxf(aabb.size.x, aabb.size.z)
	var s := target_len / maxf(long_len, 0.001)
	var c := aabb.get_center()
	scene.position = -Vector3(c.x, aabb.position.y, c.z)
	pivot.rotation.y = rot
	pivot.scale = Vector3(s, s, s)
	var size := aabb.size * s
	var dims := Vector3(size.x, size.y, size.z)
	if absf(fmod(rot, PI)) > 0.1:
		dims = Vector3(size.z, size.y, size.x)
	var out := {"node": holder, "had_wheels": had_wheels, "dims": dims}
	# "paint/body/car" isimli malzemeleri boyanabilir yap
	for entry in meshes:
		var mi2: MeshInstance3D = entry[0]
		if mi2.mesh == null:
			continue
		for si in mi2.mesh.get_surface_count():
			var mat := mi2.get_active_material(si)
			if mat is BaseMaterial3D and _is_paint_name(mat.resource_name):
				var copy: BaseMaterial3D = mat.duplicate()
				copy.albedo_color = paint
				copy.albedo_texture = null
				mi2.set_surface_override_material(si, copy)
				out["paint"] = copy
	return out


static func _is_paint_name(n: String) -> bool:
	var l := n.to_lower()
	return l.contains("paint") or l.contains("carpaint") or l == "body" or l.contains("body_paint") or l.contains("boya")


static func _collect(node: Node, xf: Transform3D, out: Array) -> void:
	var t := xf
	if node is Node3D:
		t = xf * (node as Node3D).transform
	if node is MeshInstance3D and (node as MeshInstance3D).mesh != null:
		out.append([node, t])
	for ch in node.get_children():
		_collect(ch, t, out)


static func _is_wheel(n: Node) -> bool:
	var cur := n
	for depth in 3:
		if cur == null:
			break
		var nm := String(cur.name).to_lower()
		for w in WHEEL_WORDS:
			if nm.contains(w):
				return true
		cur = cur.get_parent()
	return false
