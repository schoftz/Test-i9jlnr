class_name World
extends Node3D
## Prosedürel "küçük ülke":
##  - Merkez (yüksek binalı şehir), Sahilkent (sahil kasabası), Dağköy (köy)
##  - 3 otoyol (O-1 asma köprü, O-2 tünel, O-3 kemer köprü), kavşaklarda göbekli dönel kavşaklar
##  - Eski yol (kırsal), sahil yolu, nehir, deniz, tepeler, tarlalar
## Yol grafiği (düğüm + komşu + kenar tipi) trafik / polis / yarış yapay zekası için.

const LANES := {"hw": [2.3, 5.9, 9.5], "city": [3.5], "rb": [3.2], "road": [2.6]}
const HALF := {"hw": 12.5, "city": 7.0, "rb": 6.5, "road": 5.0}
const RIVER_X0 := 880.0
const RIVER_X1 := 1120.0
const SEA_X := 2560.0
const CELL := 20.0
const CHUNK := 600.0

var nodes: Array[Vector3] = []
var neighbors: Array = []
var edge_type: Dictionary = {}
var road_lines: Array = []          # [{"pts": PackedVector2Array, "type": String}]
var areas: Dictionary = {}          # isim -> {"center","grid","ni","nj"}
var roundabouts: Dictionary = {}    # isim -> {"center", "ring": Array[int]}
var labels: Array = []              # harita etiketleri [{"pos": Vector2, "text": String}]
var occ: Dictionary = {}
var window_mats: Array[ShaderMaterial] = []
var bulb_mats: Array[StandardMaterial3D] = []
var city_lights: Array[OmniLight3D] = []
var tunnel_lights: Array[OmniLight3D] = []
var road_mats: Dictionary = {}
var rng := RandomNumberGenerator.new()
var _night := 0.0

# ortak malzemeler
var m_grass: StandardMaterial3D
var m_concrete: StandardMaterial3D
var m_metal: StandardMaterial3D
var m_walk: StandardMaterial3D
var m_rock: StandardMaterial3D
var m_shoulder: StandardMaterial3D


func _ready() -> void:
	rng.seed = 20251
	_make_materials()
	_build_terrain()
	_build_network()
	_build_tunnel_ridge()
	_build_bridges()
	_build_nature()


# ================================================================ graf

func add_node(p: Vector3) -> int:
	nodes.append(p)
	neighbors.append([])
	return nodes.size() - 1


static func ekey(a: int, b: int) -> Vector2i:
	return Vector2i(mini(a, b), maxi(a, b))


func link(a: int, b: int, t: String) -> void:
	if a == b or (neighbors[a] as Array).has(b):
		return
	neighbors[a].append(b)
	neighbors[b].append(a)
	edge_type[ekey(a, b)] = t


func edge_kind(a: int, b: int) -> String:
	return edge_type.get(ekey(a, b), "city")


func lane_count(a: int, b: int) -> int:
	return (LANES[edge_kind(a, b)] as Array).size()


func lane_point(from_n: int, to_n: int, lane: int = 0) -> Vector3:
	var offs: Array = LANES[edge_kind(from_n, to_n)]
	var off: float = offs[clampi(lane, 0, offs.size() - 1)]
	var a := nodes[from_n]
	var b := nodes[to_n]
	var d := Vector3(b.x - a.x, 0, b.z - a.z).normalized()
	return b + d.cross(Vector3.UP) * off


func is_junction(n: int) -> bool:
	return (neighbors[n] as Array).size() > 2


func pick_next(prev_n: int, cur_n: int) -> int:
	var opts: Array = (neighbors[cur_n] as Array).duplicate()
	if opts.size() > 1:
		opts.erase(prev_n)
	return opts[rng.randi() % opts.size()]


func nearest_node(p: Vector3) -> int:
	var best := 0
	var bd := INF
	for n in nodes.size():
		var q := nodes[n]
		var d := (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) + (q.y - p.y) * (q.y - p.y) * 4.0
		if d < bd:
			bd = d
			best = n
	return best


## BFS ile en kısa düğüm yolu (kenar sayısı yerine mesafe ağırlıklı Dijkstra)
func find_path(a: int, b: int) -> Array[int]:
	var dist := {a: 0.0}
	var prev := {}
	var open: Array = [a]
	var done := {}
	while not open.is_empty():
		var bi := 0
		for i in open.size():
			if dist[open[i]] < dist[open[bi]]:
				bi = i
		var cur: int = open[bi]
		open.remove_at(bi)
		if cur == b:
			break
		if done.has(cur):
			continue
		done[cur] = true
		for nb in neighbors[cur]:
			var nd: float = dist[cur] + nodes[cur].distance_to(nodes[nb])
			if not dist.has(nb) or nd < dist[nb]:
				dist[nb] = nd
				prev[nb] = cur
				open.append(nb)
	var path: Array[int] = []
	if not prev.has(b) and a != b:
		return path
	var c := b
	path.append(c)
	while c != a:
		c = prev[c]
		path.push_front(c)
	return path


func random_node_between(from: Vector3, min_d: float, max_d: float) -> int:
	for tries in 60:
		var n := rng.randi() % nodes.size()
		var d := nodes[n].distance_to(from)
		if d > min_d and d < max_d:
			return n
	return rng.randi() % nodes.size()


# ================================================================ malzemeler / dokular

func _noise_img(w: int, h: int, base: Color, amt: float) -> Image:
	var img := Image.create(w, h, false, Image.FORMAT_RGB8)
	for y in h:
		for x in w:
			var v := rng.randf_range(-amt, amt)
			img.set_pixel(x, y, Color(base.r + v, base.g + v, base.b + v))
	return img


func _tex_mat(img: Image, rough: float = 0.9, world_scale: float = 0.0) -> StandardMaterial3D:
	img.generate_mipmaps()
	var m := StandardMaterial3D.new()
	m.albedo_texture = ImageTexture.create_from_image(img)
	m.roughness = rough
	m.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS_ANISOTROPIC
	if world_scale > 0.0:
		m.uv1_triplanar = true
		m.uv1_world_triplanar = true
		m.uv1_scale = Vector3.ONE * world_scale
	return m


func _make_materials() -> void:
	m_grass = _tex_mat(_noise_img(128, 128, Color(0.36, 0.52, 0.22), 0.05), 1.0, 0.05)
	m_rock = _tex_mat(_noise_img(64, 64, Color(0.45, 0.42, 0.38), 0.06), 1.0, 0.08)
	m_walk = _tex_mat(_noise_img(64, 64, Color(0.68, 0.66, 0.62), 0.04), 0.9, 0.4)
	m_shoulder = _tex_mat(_noise_img(64, 64, Color(0.5, 0.46, 0.38), 0.07), 1.0, 0.3)
	m_concrete = CarBase.make_mat(Color(0.74, 0.73, 0.7), 0.0, 0.85)
	m_metal = CarBase.make_mat(Color(0.78, 0.8, 0.83), 0.85, 0.3)
	for t in ["hw", "city", "rb", "road"]:
		road_mats[t] = _road_material(t)


func _road_material(t: String) -> StandardMaterial3D:
	var hw: float = HALF[t]
	var w := 128
	var h := 128
	var img := _noise_img(w, h, Color(0.17, 0.17, 0.18), 0.03)
	var white := Color(0.92, 0.92, 0.9)
	var yellow := Color(0.95, 0.75, 0.15)
	var px_per_m := w / (hw * 2.0)
	var line := func(m_off: float, width: float, col: Color, dashed: bool) -> void:
		var cx := (m_off + hw) * px_per_m
		var half := maxf(width * px_per_m * 0.5, 0.6)
		for y in h:
			if dashed and y > h * 0.45:
				continue
			for x in range(int(cx - half), int(ceil(cx + half))):
				if x >= 0 and x < w:
					img.set_pixel(x, y, col)
	match t:
		"hw":
			for s in [-1.0, 1.0]:
				line.call(s * 11.3, 0.25, white, false)
				line.call(s * 4.1, 0.18, white, true)
				line.call(s * 7.7, 0.18, white, true)
				line.call(s * 0.8, 0.2, yellow, false)
		"city":
			line.call(-0.15, 0.12, yellow, false)
			line.call(0.15, 0.12, yellow, false)
			line.call(-6.4, 0.18, white, false)
			line.call(6.4, 0.18, white, false)
		"rb":
			line.call(-6.0, 0.2, white, false)
			line.call(6.0, 0.2, white, false)
			line.call(0.0, 0.15, white, true)
		"road":
			line.call(0.0, 0.15, white, true)
			line.call(-4.6, 0.15, white, false)
			line.call(4.6, 0.15, white, false)
	var m := _tex_mat(img, 0.92)
	m.metallic_specular = 0.3
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	return m


# ================================================================ arazi

func _static_box(size: Vector3, pos: Vector3, mat: Material, collide: bool = true, vis_range: float = 0.0, parent: Node = null) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = mat
	mi.position = pos
	if vis_range > 0.0:
		mi.visibility_range_end = vis_range
		mi.visibility_range_end_margin = 50.0
	(parent if parent else self).add_child(mi)
	if collide:
		var sb := StaticBody3D.new()
		sb.position = pos
		var cs := CollisionShape3D.new()
		var sh := BoxShape3D.new()
		sh.size = size
		cs.shape = sh
		sb.add_child(cs)
		add_child(sb)
	return mi


func _build_terrain() -> void:
	# kara (nehrin batısı / doğusu), nehir yatağı, kumsal, deniz tabanı
	_static_box(Vector3(RIVER_X0 + 4000.0, 4.0, 8000.0), Vector3((RIVER_X0 - 4000.0) * 0.5, -2.0, 0), m_grass)
	_static_box(Vector3(2380.0 - RIVER_X1, 4.0, 8000.0), Vector3((RIVER_X1 + 2380.0) * 0.5, -2.0, 0), m_grass)
	_static_box(Vector3(RIVER_X1 - RIVER_X0, 4.0, 8000.0), Vector3((RIVER_X0 + RIVER_X1) * 0.5, -11.0, 0), m_rock)
	var sand := _tex_mat(_noise_img(64, 64, Color(0.86, 0.78, 0.58), 0.04), 1.0, 0.1)
	_static_box(Vector3(SEA_X - 2380.0, 4.0, 8000.0), Vector3((2380.0 + SEA_X) * 0.5, -2.05, 0), sand)
	_static_box(Vector3(4000.0, 4.0, 8000.0), Vector3(SEA_X + 2000.0, -8.0, 0), sand)
	var water := StandardMaterial3D.new()
	water.albedo_color = Color(0.12, 0.38, 0.55)
	water.metallic = 0.2
	water.roughness = 0.08
	for rect in [[RIVER_X0, RIVER_X1, -3.0], [SEA_X - 20.0, SEA_X + 4000.0, -0.6]]:
		var mi := MeshInstance3D.new()
		var pm := PlaneMesh.new()
		pm.size = Vector2(rect[1] - rect[0], 8000.0)
		mi.mesh = pm
		mi.material_override = water
		mi.position = Vector3((rect[0] + rect[1]) * 0.5, rect[2], 0)
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mi)
	# harita kenarı duvarları (görünmez)
	for wall in [[Vector3(10, 60, 8000), Vector3(-3400, 30, 0)], [Vector3(10, 60, 8000), Vector3(SEA_X + 300, 30, 0)],
			[Vector3(8000, 60, 10), Vector3(0, 30, -3200)], [Vector3(8000, 60, 10), Vector3(0, 30, 2800)]]:
		var sb := StaticBody3D.new()
		sb.position = wall[1]
		var cs := CollisionShape3D.new()
		var sh := BoxShape3D.new()
		sh.size = wall[0]
		cs.shape = sh
		sb.add_child(cs)
		add_child(sb)


func in_water(x: float) -> bool:
	return (x > RIVER_X0 - 10.0 and x < RIVER_X1 + 10.0) or x > 2370.0


# ================================================================ yol ağı

func _build_network() -> void:
	# --- yerleşimler
	build_city("Merkez", Vector3(0, 0, 0), 5, 5, 90.0, "downtown")
	build_city("Sahilkent", Vector3(1800, 0, 300), 4, 3, 80.0, "town")
	build_city("Dağköy", Vector3(-300, 0, -1650), 3, 3, 70.0, "village")
	# --- dönel kavşaklar
	build_roundabout("RA_Merkez_Dogu", Vector3(430, 0, 0))
	build_roundabout("RA_Merkez_Guney", Vector3(0, 0, -450))
	build_roundabout("RA_Sahil_Bati", Vector3(1520, 0, 300))
	build_roundabout("RA_Sahil_Guney", Vector3(1760, 0, -150))
	build_roundabout("RA_Dagkoy", Vector3(-300, 0, -1400))
	# --- şehir bağlantıları
	connect_ra_city("RA_Merkez_Dogu", "Merkez", 4, 2)
	connect_ra_city("RA_Merkez_Guney", "Merkez", 2, 0)
	connect_ra_city("RA_Sahil_Bati", "Sahilkent", 0, 1)
	connect_ra_city("RA_Sahil_Guney", "Sahilkent", 1, 0)
	connect_ra_city("RA_Dagkoy", "Dağköy", 1, 2)
	# --- O-1: Merkez -> Sahilkent (asma köprü)
	highway("RA_Merkez_Dogu", "RA_Sahil_Bati", [
		Vector3(530, 0, 40), Vector3(620, 0, 150), Vector3(700, 3, 235), Vector3(760, 9, 250), Vector3(820, 14, 250),
		Vector3(880, 14, 250), Vector3(1120, 14, 250), Vector3(1180, 14, 250), Vector3(1240, 9, 250),
		Vector3(1300, 3, 262), Vector3(1400, 0, 300)], "O-1  SAHİLKENT", "O-1  MERKEZ")
	# --- O-2: Merkez -> Dağköy (tünel)
	highway("RA_Merkez_Guney", "RA_Dagkoy", [
		Vector3(20, 0, -560), Vector3(80, 0, -680), Vector3(100, 0, -760), Vector3(100, 0, -820),
		Vector3(100, 0, -1020), Vector3(100, 0, -1080), Vector3(70, 0, -1200), Vector3(-120, 0, -1330)], "O-2  DAĞKÖY", "O-2  MERKEZ")
	# --- O-3: Dağköy -> Sahilkent (kemer köprü)
	highway("RA_Dagkoy", "RA_Sahil_Guney", [
		Vector3(-150, 0, -1480), Vector3(150, 0, -1520), Vector3(480, 0, -1440), Vector3(680, 3, -1260),
		Vector3(780, 8, -1205), Vector3(860, 10, -1200), Vector3(1140, 10, -1200), Vector3(1220, 8, -1195),
		Vector3(1330, 3, -1150), Vector3(1520, 0, -980), Vector3(1700, 0, -680), Vector3(1760, 0, -400)], "O-3  SAHİLKENT", "O-3  DAĞKÖY")
	# --- Eski yol: Merkez batı -> Dağköy (kırsal, iki şerit)
	var mg: Array = areas["Merkez"]["grid"]
	var ra: Dictionary = roundabouts["RA_Dagkoy"]
	var ring_w := _ring_attach(ra["ring"], Vector3(-650, 0, -1400))
	build_road([nodes[mg[0][2]], Vector3(-330, 0, 20), Vector3(-560, 0, 60), Vector3(-880, 0, -120), Vector3(-1060, 0, -600),
		Vector3(-980, 0, -1050), Vector3(-700, 0, -1380), nodes[ring_w]], "road", {"start": mg[0][2], "end": ring_w})
	# --- Sahil yolu
	var sg: Array = areas["Sahilkent"]["grid"]
	var j := add_node(Vector3(2290, 0, 300))
	build_road([nodes[sg[3][1]], Vector3(2100, 0, 300), nodes[j]], "road", {"start": sg[3][1], "end": j})
	build_road([Vector3(2260, 0, -900), Vector3(2300, 0, -500), Vector3(2280, 0, -100), nodes[j]], "road", {"end": j})
	build_road([nodes[j], Vector3(2310, 0, 700), Vector3(2270, 0, 1100), Vector3(2300, 0, 1500)], "road", {"start": j})
	labels.append({"pos": Vector2(2300, 1550), "text": "Sahil Yolu"})
	labels.append({"pos": Vector2(1000, 330), "text": "Asma Köprü"})
	labels.append({"pos": Vector2(1000, -1120), "text": "Kemer Köprü"})
	labels.append({"pos": Vector2(160, -920), "text": "Tünel"})


func build_city(name: String, center: Vector3, ni: int, nj: int, B: float, style: String) -> void:
	var grid: Array = []
	var x0 := center.x - (ni - 1) * B * 0.5
	var z0 := center.z - (nj - 1) * B * 0.5
	for i in ni:
		var col: Array = []
		for jj in nj:
			col.append(add_node(Vector3(x0 + i * B, 0, z0 + jj * B)))
		grid.append(col)
	for i in ni:
		for jj in nj:
			if i + 1 < ni:
				link(grid[i][jj], grid[i + 1][jj], "city")
			if jj + 1 < nj:
				link(grid[i][jj], grid[i][jj + 1], "city")
	var hw: float = HALF["city"]
	# sokak şeritleri
	for jj in nj:
		var a := Vector3(x0 - hw, 0, z0 + jj * B)
		var b := Vector3(x0 + (ni - 1) * B + hw, 0, z0 + jj * B)
		_strip(_resample([a, b], 6.0), "city", false, {"y": 0.0})
	for i in ni:
		var a := Vector3(x0 + i * B, 0, z0 - hw)
		var b := Vector3(x0 + i * B, 0, z0 + (nj - 1) * B + hw)
		_strip(_resample([a, b], 6.0), "city", false, {"y": 0.012})
	areas[name] = {"center": center, "grid": grid, "ni": ni, "nj": nj, "B": B}
	labels.append({"pos": Vector2(center.x, center.z - (nj - 1) * B * 0.5 - 40.0), "text": name})
	_mark_rect(Vector2(x0 - 60, z0 - 60), Vector2(x0 + (ni - 1) * B + 60, z0 + (nj - 1) * B + 60))
	_build_blocks(grid, ni, nj, B, x0, z0, style, center)


func _mark_rect(a: Vector2, b: Vector2) -> void:
	for cx in range(int(floor(a.x / CELL)), int(ceil(b.x / CELL)) + 1):
		for cz in range(int(floor(a.y / CELL)), int(ceil(b.y / CELL)) + 1):
			occ[Vector2i(cx, cz)] = true


func _mark_circle(p: Vector3, r: float) -> void:
	var cr := int(ceil(r / CELL))
	var c := Vector2i(int(floor(p.x / CELL)), int(floor(p.z / CELL)))
	for dx in range(-cr, cr + 1):
		for dz in range(-cr, cr + 1):
			if dx * dx + dz * dz <= (cr + 1) * (cr + 1):
				occ[c + Vector2i(dx, dz)] = true


func is_free(p: Vector3, margin: float = 0.0) -> bool:
	var cr := int(ceil(margin / CELL))
	var c := Vector2i(int(floor(p.x / CELL)), int(floor(p.z / CELL)))
	for dx in range(-cr, cr + 1):
		for dz in range(-cr, cr + 1):
			if occ.has(c + Vector2i(dx, dz)):
				return false
	return true


func build_roundabout(name: String, c: Vector3, radius: float = 34.0) -> void:
	var ring: Array[int] = []
	var count := 12
	for k in count:
		var a := TAU * k / count
		ring.append(add_node(c + Vector3(cos(a), 0, sin(a)) * radius))
	for k in count:
		link(ring[k], ring[(k + 1) % count], "rb")
	var pts := PackedVector3Array()
	for k in 72:
		var a := TAU * k / 72.0
		pts.append(c + Vector3(cos(a), 0, sin(a)) * radius)
	_strip(pts, "rb", true, {"y": 0.02})
	# göbek adası
	var island := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = radius - 6.8
	cm.bottom_radius = radius - 6.8
	cm.height = 0.5
	cm.radial_segments = 48
	island.mesh = cm
	island.material_override = m_grass
	island.position = c + Vector3(0, 0.25, 0)
	add_child(island)
	var sb := StaticBody3D.new()
	sb.position = island.position
	var cs := CollisionShape3D.new()
	var sh := CylinderShape3D.new()
	sh.radius = radius - 6.8
	sh.height = 0.5
	cs.shape = sh
	sb.add_child(cs)
	add_child(sb)
	# anıt
	var ob := _static_box(Vector3(2.5, 14, 2.5), c + Vector3(0, 7, 0), m_concrete)
	ob.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	_mark_circle(c, radius + 30.0)
	roundabouts[name] = {"center": c, "ring": ring}


func _ring_attach(ring: Array, toward: Vector3) -> int:
	var best: int = ring[0]
	var bd := INF
	for n in ring:
		var d := nodes[n].distance_to(toward)
		if d < bd:
			bd = d
			best = n
	return best


func connect_ra_city(ra_name: String, city: String, gi: int, gj: int) -> void:
	var ra: Dictionary = roundabouts[ra_name]
	var cn: int = areas[city]["grid"][gi][gj]
	var rn := _ring_attach(ra["ring"], nodes[cn])
	build_road([nodes[cn], nodes[rn]], "city", {"start": cn, "end": rn})


func highway(ra_a: String, ra_b: String, ctrl: Array, sign_a: String, sign_b: String) -> void:
	var a := _ring_attach(roundabouts[ra_a]["ring"], ctrl[0])
	var b := _ring_attach(roundabouts[ra_b]["ring"], ctrl[ctrl.size() - 1])
	var pts: Array = [nodes[a]]
	pts.append_array(ctrl)
	pts.append(nodes[b])
	var line := build_road(pts, "hw", {"start": a, "end": b, "rails": true, "median": true, "poles": true})
	_sign(line, 18, sign_a, false)
	_sign(line, line.size() - 19, sign_b, true)


## Kontrol noktalarından yumuşak yol üretir; mesh + çarpışma + graf düğümleri.
func build_road(ctrl: Array, t: String, opts: Dictionary) -> PackedVector3Array:
	var pts := _catmull(ctrl, 6.0)
	_strip(pts, t, false, opts)
	# graf
	var spacing := 45.0 if t == "hw" else 40.0
	var prev: int = opts.get("start", -1)
	if prev < 0:
		prev = add_node(pts[0])
	var acc := 0.0
	for i in range(1, pts.size() - 1):
		acc += pts[i].distance_to(pts[i - 1])
		var remain := pts[i].distance_to(pts[pts.size() - 1])
		if acc >= spacing and remain > spacing * 0.6:
			var n := add_node(pts[i])
			link(prev, n, t)
			prev = n
			acc = 0.0
	var last: int = opts.get("end", -1)
	if last < 0:
		last = add_node(pts[pts.size() - 1])
	link(prev, last, t)
	for p in pts:
		_mark_circle(p, HALF[t] + 12.0)
	return pts


func _resample(ctrl: Array, spacing: float) -> PackedVector3Array:
	var out := PackedVector3Array()
	for i in ctrl.size() - 1:
		var a: Vector3 = ctrl[i]
		var b: Vector3 = ctrl[i + 1]
		var n := maxi(1, int(a.distance_to(b) / spacing))
		for k in n:
			out.append(a.lerp(b, float(k) / n))
	out.append(ctrl[ctrl.size() - 1])
	return out


func _catmull(ctrl: Array, spacing: float) -> PackedVector3Array:
	var out := PackedVector3Array()
	var n := ctrl.size()
	for i in n - 1:
		var p0: Vector3 = ctrl[maxi(i - 1, 0)]
		var p1: Vector3 = ctrl[i]
		var p2: Vector3 = ctrl[i + 1]
		var p3: Vector3 = ctrl[mini(i + 2, n - 1)]
		var steps := maxi(1, int(p1.distance_to(p2) / spacing))
		for k in steps:
			var t := float(k) / steps
			out.append(p1.cubic_interpolate(p2, p0, p3, t))
	out.append(ctrl[n - 1])
	return out


func _sign(line: PackedVector3Array, idx: int, text: String, reverse: bool) -> void:
	if line.size() < 3:
		return
	idx = clampi(idx, 1, line.size() - 2)
	var p := line[idx]
	var t := (line[idx + 1] - line[idx - 1])
	t.y = 0
	t = t.normalized()
	if reverse:
		t = -t
	var right := t.cross(Vector3.UP)
	var pos := p + right * (HALF["hw"] + 3.0)
	var board_mat := CarBase.make_mat(Color(0.05, 0.4, 0.2), 0.2, 0.5)
	var post := _static_box(Vector3(0.3, 6.5, 0.3), pos + Vector3(0, 3.25, 0), m_metal, true)
	var board := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = Vector3(7.0, 2.2, 0.2)
	board.mesh = bm
	board.material_override = board_mat
	board.position = pos + Vector3(0, 6.6, 0) - right * 2.5
	board.basis = Basis.looking_at(t, Vector3.UP)
	add_child(board)
	var lbl := Label3D.new()
	lbl.text = text
	lbl.font_size = 96
	lbl.pixel_size = 0.012
	lbl.outline_size = 0
	lbl.modulate = Color(1, 1, 1)
	lbl.position = board.position - t * 0.12
	lbl.basis = Basis.looking_at(t, Vector3.UP)
	lbl.visibility_range_end = 400.0
	add_child(lbl)
	post.visibility_range_end = 600.0


# ================================================================ yol mesh

func _add_tri(st: SurfaceTool, col: PackedVector3Array, a: Vector3, b: Vector3, c: Vector3, ua: Vector2, ub: Vector2, uc: Vector2, hint: Vector3) -> void:
	var nrm := (c - a).cross(b - a)
	if nrm.dot(hint) < 0.0:
		var tv := b
		b = c
		c = tv
		var tu := ub
		ub = uc
		uc = tu
		nrm = -nrm
	nrm = nrm.normalized()
	st.set_normal(nrm)
	st.set_uv(ua)
	st.add_vertex(a)
	st.set_normal(nrm)
	st.set_uv(ub)
	st.add_vertex(b)
	st.set_normal(nrm)
	st.set_uv(uc)
	st.add_vertex(c)
	col.append(a)
	col.append(b)
	col.append(c)


func _quad(st: SurfaceTool, col: PackedVector3Array, a: Vector3, b: Vector3, c: Vector3, d: Vector3, ua: Vector2, ub: Vector2, uc: Vector2, ud: Vector2, hint: Vector3) -> void:
	# a-b üst kenar (i), d-c (i+1)
	_add_tri(st, col, a, b, c, ua, ub, uc, hint)
	_add_tri(st, col, a, c, d, ua, uc, ud, hint)


## Bir yol çizgisinden parça parça (chunk) mesh + çarpışma üretir.
func _strip(pts: PackedVector3Array, t: String, closed: bool, opts: Dictionary) -> void:
	var hw: float = HALF[t]
	var n := pts.size()
	var y_off: float = opts.get("y", 0.0)
	var rails: bool = opts.get("rails", false)
	var median: bool = opts.get("median", false)
	var poles: bool = opts.get("poles", false)
	# yön ve sağ vektörleri
	var rights := PackedVector3Array()
	var dists := PackedFloat32Array()
	var acc := 0.0
	for i in n:
		var a := pts[(i - 1 + n) % n] if (closed or i > 0) else pts[i]
		var b := pts[(i + 1) % n] if (closed or i < n - 1) else pts[i]
		var d := b - a
		d.y = 0
		rights.append(d.normalized().cross(Vector3.UP))
		if i > 0:
			acc += pts[i].distance_to(pts[i - 1])
		dists.append(acc)
	var total := acc
	var segs := n if closed else n - 1
	var chunk := 50
	var s0 := 0
	var pole_xf: Array[Transform3D] = []
	var pillar_xf: Array[Transform3D] = []
	while s0 < segs:
		var s1 := mini(s0 + chunk, segs)
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		var st_side := SurfaceTool.new()
		st_side.begin(Mesh.PRIMITIVE_TRIANGLES)
		var st_rail := SurfaceTool.new()
		st_rail.begin(Mesh.PRIMITIVE_TRIANGLES)
		var st_sh := SurfaceTool.new()
		st_sh.begin(Mesh.PRIMITIVE_TRIANGLES)
		var shoulder: bool = opts.get("shoulder", t == "road" or (t == "city" and opts.has("start")))
		var has_sh := false
		var col := PackedVector3Array()
		var dummy := PackedVector3Array()
		var has_side := false
		var has_rail := false
		for s in range(s0, s1):
			var i := s
			var k := (s + 1) % n
			var pi := pts[i] + Vector3(0, 0.05 + y_off, 0)
			var pk := pts[k] + Vector3(0, 0.05 + y_off, 0)
			var ri := rights[i]
			var rk := rights[k]
			var vi := dists[i] / 12.0
			var vk := (dists[k] if k > i else total + pts[i].distance_to(pts[k])) / 12.0
			_quad(st, col, pi - ri * hw, pi + ri * hw, pk + rk * hw, pk - rk * hw,
				Vector2(0, vi), Vector2(1, vi), Vector2(1, vk), Vector2(0, vk), Vector3.UP)
			if shoulder and pts[i].y < 0.5:
				has_sh = true
				for sgn in [-1.0, 1.0]:
					var e0: Vector3 = pi + ri * hw * sgn
					var e1: Vector3 = pk + rk * hw * sgn
					var o0: Vector3 = pi + ri * (hw + 2.2) * sgn - Vector3(0, 0.035, 0)
					var o1: Vector3 = pk + rk * (hw + 2.2) * sgn - Vector3(0, 0.035, 0)
					_quad(st_sh, dummy, e0, o0, o1, e1, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector3.UP)
			var elevated := pts[i].y > 0.6 or pts[k].y > 0.6
			if elevated:
				has_side = true
				var dn := Vector3(0, -1.6, 0)
				for sgn in [-1.0, 1.0]:
					var ea: Vector3 = pi + ri * hw * sgn
					var eb: Vector3 = pk + rk * hw * sgn
					_quad(st_side, dummy, ea, eb, eb + dn, ea + dn, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, ri * sgn)
				_quad(st_side, dummy, pi - ri * hw + dn, pi + ri * hw + dn, pk + rk * hw + dn, pk - rk * hw + dn,
					Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector3.DOWN)
				if s % 6 == 0 and pts[i].y > 2.0 and not (pts[i].x > RIVER_X0 - 5.0 and pts[i].x < RIVER_X1 + 5.0):
					var h := pts[i].y + 10.0
					pillar_xf.append(Transform3D(Basis(Vector3.UP, atan2(ri.x, ri.z)).scaled(Vector3(hw * 1.2, h, 2.0)), pts[i] + Vector3(0, -1.6 - h * 0.5, 0)))
			var near_end := dists[i] < 45.0 or total - dists[i] < 45.0
			if rails and not near_end:
				has_rail = true
				for sgn in [-1.0, 1.0]:
					var ra: Vector3 = pi + ri * (hw + 0.2) * sgn
					var rb: Vector3 = pk + rk * (hw + 0.2) * sgn
					var up1 := Vector3(0, 0.25, 0)
					var up2 := Vector3(0, 0.95, 0)
					_quad(st_rail, col, ra + up2, rb + up2, rb + up1, ra + up1, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, -ri * sgn)
					_quad(st_rail, dummy, ra + up2, rb + up2, rb + up1, ra + up1, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, ri * sgn)
			if median and not near_end:
				has_side = true
				for sgn in [-1.0, 1.0]:
					var ma: Vector3 = pi + ri * 0.35 * sgn
					var mb: Vector3 = pk + rk * 0.35 * sgn
					var up := Vector3(0, 0.85, 0)
					_quad(st_side, col, ma + up, mb + up, mb, ma, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, ri * sgn)
				_quad(st_side, dummy, pi - ri * 0.35 + Vector3(0, 0.85, 0), pi + ri * 0.35 + Vector3(0, 0.85, 0),
					pk + rk * 0.35 + Vector3(0, 0.85, 0), pk - rk * 0.35 + Vector3(0, 0.85, 0), Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector3.UP)
			if poles and not near_end and s % 10 == 0:
				pole_xf.append(Transform3D(Basis(Vector3.UP, atan2(ri.x, ri.z)), pi + Vector3(0, 0.85, 0)))
		var holder := Node3D.new()
		add_child(holder)
		var mi := MeshInstance3D.new()
		mi.mesh = st.commit()
		mi.material_override = road_mats[t]
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		holder.add_child(mi)
		if has_side:
			var ms := MeshInstance3D.new()
			ms.mesh = st_side.commit()
			ms.material_override = m_concrete
			holder.add_child(ms)
		if has_sh:
			var msh := MeshInstance3D.new()
			msh.mesh = st_sh.commit()
			msh.material_override = m_shoulder
			msh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			holder.add_child(msh)
		if has_rail:
			var mr := MeshInstance3D.new()
			mr.mesh = st_rail.commit()
			mr.material_override = m_metal
			mr.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			mr.visibility_range_end = 900.0
			holder.add_child(mr)
		var sb := StaticBody3D.new()
		var cs := CollisionShape3D.new()
		var shape := ConcavePolygonShape3D.new()
		shape.backface_collision = true
		shape.set_faces(col)
		cs.shape = shape
		sb.add_child(cs)
		holder.add_child(sb)
		s0 = s1
	var line2 := PackedVector2Array()
	for p in pts:
		line2.append(Vector2(p.x, p.z))
	if closed:
		line2.append(Vector2(pts[0].x, pts[0].z))
	road_lines.append({"pts": line2, "type": t})
	if not pole_xf.is_empty():
		_highway_poles(pole_xf)
	if not pillar_xf.is_empty():
		var bm := BoxMesh.new()
		bm.size = Vector3(1, 1, 1)
		bm.material = m_concrete
		_multimesh(bm, pillar_xf, 1500.0)


func _multimesh(mesh: Mesh, xf: Array[Transform3D], vis_range: float, shadows: bool = true) -> void:
	# alanı parçalara bölerek (chunk) çiz: görünürlük aralığı ve frustum culling için
	var buckets := {}
	for t in xf:
		var key := Vector2i(int(floor(t.origin.x / CHUNK)), int(floor(t.origin.z / CHUNK)))
		if not buckets.has(key):
			buckets[key] = []
		buckets[key].append(t)
	for key in buckets:
		var arr: Array = buckets[key]
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = mesh
		mm.instance_count = arr.size()
		for i in arr.size():
			mm.set_instance_transform(i, arr[i])
		var mmi := MultiMeshInstance3D.new()
		mmi.multimesh = mm
		mmi.visibility_range_end = vis_range
		mmi.visibility_range_end_margin = 60.0
		if not shadows:
			mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mmi)


func _highway_poles(xf: Array[Transform3D]) -> void:
	var pole := CylinderMesh.new()
	pole.top_radius = 0.08
	pole.bottom_radius = 0.11
	pole.height = 11.0
	pole.radial_segments = 8
	pole.material = CarBase.make_mat(Color(0.42, 0.44, 0.46), 0.6, 0.5)
	var arm := BoxMesh.new()
	arm.size = Vector3(7.0, 0.15, 0.25)
	arm.material = m_metal
	var bulb_mat := CarVisual.emissive(Color(1.0, 0.9, 0.7), 0.2)
	bulb_mats.append(bulb_mat)
	var bulb := BoxMesh.new()
	bulb.size = Vector3(0.9, 0.18, 0.45)
	bulb.material = bulb_mat
	var p_xf: Array[Transform3D] = []
	var a_xf: Array[Transform3D] = []
	var b_xf: Array[Transform3D] = []
	for t in xf:
		p_xf.append(t.translated(Vector3(0, 5.5, 0)))
		a_xf.append(t.translated(Vector3(0, 11.0, 0)))
		var right := t.basis.x
		b_xf.append(Transform3D(t.basis, t.origin + Vector3(0, 10.85, 0) + right * 3.3))
		b_xf.append(Transform3D(t.basis, t.origin + Vector3(0, 10.85, 0) - right * 3.3))
	_multimesh(pole, p_xf, 1000.0, false)
	_multimesh(arm, a_xf, 800.0, false)
	_multimesh(bulb, b_xf, 800.0, false)


# ================================================================ binalar

const BUILDING_SHADER := """
shader_type spatial;
// Bina cephesi: pencereler dünya koordinatından hesaplanır (doku yok, UV yok).
uniform vec3 tint = vec3(0.8);
uniform vec3 lit_color = vec3(1.0, 0.85, 0.55);
uniform float night = 0.0;
uniform float glass = 0.0;
varying vec3 wpos;
varying vec3 wnrm;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
	vec3 n = abs(wnrm);
	if (n.y > 0.7) {
		ALBEDO = tint * 0.45;
		ROUGHNESS = 0.9;
	} else {
		vec2 uv = n.x > n.z ? wpos.zy : wpos.xy;
		vec2 g = uv / vec2(3.2, 3.5);
		vec2 cell = floor(g);
		vec2 f = fract(g);
		float win = step(0.18, f.x) * step(f.x, 0.82) * step(0.28, f.y) * step(f.y, 0.86);
		if (glass > 0.5) {
			win = step(0.05, f.x) * step(0.12, f.y) * step(f.y, 0.94);
		}
		vec3 wincol = vec3(0.2, 0.3, 0.4);
		ALBEDO = mix(tint, wincol, win);
		ROUGHNESS = mix(0.8, 0.12, win);
		METALLIC = win * 0.35;
		float on = step(0.5, hash(cell + floor(wpos.xz * 0.02) * 7.0));
		EMISSION = lit_color * win * on * night * (0.5 + 0.5 * hash(cell * 1.7 + 3.0));
	}
}
"""
var _bshader: Shader


func _window_material(tint: Color, lit: Color, glass: bool) -> Material:
	if _bshader == null:
		_bshader = Shader.new()
		_bshader.code = BUILDING_SHADER
	var m := ShaderMaterial.new()
	m.shader = _bshader
	m.set_shader_parameter("tint", Vector3(tint.r, tint.g, tint.b))
	m.set_shader_parameter("lit_color", Vector3(lit.r, lit.g, lit.b))
	m.set_shader_parameter("glass", 1.0 if glass else 0.0)
	window_mats.append(m)
	return m


func _build_blocks(grid: Array, ni: int, nj: int, B: float, x0: float, z0: float, style: String, center: Vector3) -> void:
	var hw: float = HALF["city"]
	var mats: Array = []
	match style:
		"downtown":
			mats = [_window_material(Color(0.82, 0.8, 0.76), Color(1, 0.85, 0.55), false),
				_window_material(Color(0.6, 0.66, 0.74), Color(0.8, 0.9, 1.0), true),
				_window_material(Color(0.9, 0.86, 0.78), Color(1, 0.8, 0.5), false),
				_window_material(Color(0.45, 0.55, 0.62), Color(0.8, 0.9, 1.0), true)]
		_:
			mats = [_window_material(Color(0.96, 0.92, 0.82), Color(1, 0.8, 0.5), false),
				_window_material(Color(0.95, 0.85, 0.7), Color(1, 0.8, 0.5), false),
				_window_material(Color(0.85, 0.92, 0.95), Color(1, 0.85, 0.6), false),
				_window_material(Color(0.98, 0.8, 0.72), Color(1, 0.8, 0.5), false)]
	var roof_mat := CarBase.make_mat(Color(0.35, 0.35, 0.37), 0.0, 0.9)
	var tile_mats := [CarBase.make_mat(Color(0.7, 0.3, 0.2), 0.0, 0.8), CarBase.make_mat(Color(0.45, 0.25, 0.2), 0.0, 0.8), CarBase.make_mat(Color(0.3, 0.35, 0.45), 0.0, 0.8)]
	var tree_xf: Array[Transform3D] = []
	var lamp_xf: Array[Transform3D] = []
	for i in ni - 1:
		for jj in nj - 1:
			var bx0 := x0 + i * B + hw
			var bx1 := x0 + (i + 1) * B - hw
			var bz0 := z0 + jj * B + hw
			var bz1 := z0 + (jj + 1) * B - hw
			var cx := (bx0 + bx1) * 0.5
			var cz := (bz0 + bz1) * 0.5
			var sx := bx1 - bx0
			var sz := bz1 - bz0
			_static_box(Vector3(sx, 0.2, sz), Vector3(cx, 0.1, cz), m_walk)
			var park := style == "downtown" and i == 1 and jj == 2
			if park:
				_static_box(Vector3(sx - 6, 0.06, sz - 6), Vector3(cx, 0.23, cz), m_grass, false)
				for t in 12:
					tree_xf.append(Transform3D(Basis(), Vector3(cx + rng.randf_range(-sx * 0.4, sx * 0.4), 0.2, cz + rng.randf_range(-sz * 0.4, sz * 0.4))))
				continue
			match style:
				"downtown":
					var dist := Vector2(cx - center.x, cz - center.z).length()
					var hmax := lerpf(150.0, 40.0, clampf(dist / 220.0, 0.0, 1.0))
					for by in 2:
						for bx in 2:
							var w := sx * 0.5 - 5.0 - rng.randf_range(0, 5)
							var d := sz * 0.5 - 5.0 - rng.randf_range(0, 5)
							var h := snappedf(rng.randf_range(18.0, hmax), 3.5)
							var px := cx + (bx - 0.5) * sx * 0.5
							var pz := cz + (by - 0.5) * sz * 0.5
							_static_box(Vector3(w, h, d), Vector3(px, 0.2 + h * 0.5, pz), mats[rng.randi() % mats.size()], true, 3000.0)
							_static_box(Vector3(w + 0.6, 0.8, d + 0.6), Vector3(px, 0.2 + h + 0.4, pz), roof_mat, false, 1500.0)
							if h > 70 and rng.randf() < 0.6:
								_static_box(Vector3(w * 0.6, h * 0.15, d * 0.6), Vector3(px, 0.2 + h + h * 0.075, pz), mats[rng.randi() % mats.size()], false, 3000.0)
				_:
					var per := 3 if style == "town" else 2
					for by in 2:
						for bx in per:
							if style == "village" and rng.randf() < 0.3:
								continue
							var w := sx / per - 8.0
							var d := sz * 0.5 - 8.0
							var h := rng.randf_range(4.0, 7.0) if style == "village" else rng.randf_range(5.0, 13.0)
							var px := bx0 + (bx + 0.5) * sx / per
							var pz := cz + (by - 0.5) * sz * 0.5
							_static_box(Vector3(w, h, d), Vector3(px, 0.2 + h * 0.5, pz), mats[rng.randi() % mats.size()], true, 1500.0)
							var roof := MeshInstance3D.new()
							var pm := PrismMesh.new()
							pm.size = Vector3(w + 1.0, 2.8, d + 1.0)
							roof.mesh = pm
							roof.material_override = tile_mats[rng.randi() % tile_mats.size()]
							roof.position = Vector3(px, 0.2 + h + 1.4, pz)
							roof.visibility_range_end = 1200.0
							add_child(roof)
					for t in 3:
						tree_xf.append(Transform3D(Basis(), Vector3(cx + rng.randf_range(-sx * 0.42, sx * 0.42), 0.2, cz + (sz * 0.5 - 3.0) * (1 if t % 2 == 0 else -1))))
	# lambalar + kavşak ışıkları
	for i in ni:
		for jj in nj:
			var p := nodes[grid[i][jj]]
			lamp_xf.append(Transform3D(Basis(), p + Vector3(hw + 1.5, 0, hw + 1.5)))
			if i < ni - 1:
				lamp_xf.append(Transform3D(Basis(), p + Vector3(B * 0.5, 0, -hw - 1.5)))
			if jj < nj - 1:
				lamp_xf.append(Transform3D(Basis(), p + Vector3(-hw - 1.5, 0, B * 0.5)))
			var l := OmniLight3D.new()
			l.light_color = Color(1.0, 0.82, 0.55)
			l.omni_range = 24.0
			l.light_energy = 0.0
			l.shadow_enabled = false
			l.position = p + Vector3(0, 7.5, 0)
			l.visible = false
			add_child(l)
			city_lights.append(l)
	_city_lamps(lamp_xf)
	_trees(tree_xf, true)


func _city_lamps(xf: Array[Transform3D]) -> void:
	var pole := CylinderMesh.new()
	pole.top_radius = 0.08
	pole.bottom_radius = 0.12
	pole.height = 8.0
	pole.radial_segments = 8
	pole.material = CarBase.make_mat(Color(0.2, 0.2, 0.22), 0.8, 0.4)
	var bulb_mat := CarVisual.emissive(Color(1.0, 0.85, 0.55), 0.2)
	bulb_mats.append(bulb_mat)
	var bulb := BoxMesh.new()
	bulb.size = Vector3(0.5, 0.2, 1.0)
	bulb.material = bulb_mat
	var a: Array[Transform3D] = []
	var b: Array[Transform3D] = []
	for t in xf:
		a.append(t.translated(Vector3(0, 4.0, 0)))
		b.append(t.translated(Vector3(0, 8.0, 0)))
	_multimesh(pole, a, 700.0, false)
	_multimesh(bulb, b, 700.0, false)


# ================================================================ doğa

var _trunk_mesh: CylinderMesh
var _crown_meshes: Array = []


func _trees(xf: Array[Transform3D], _city: bool) -> void:
	if _trunk_mesh == null:
		_trunk_mesh = CylinderMesh.new()
		_trunk_mesh.top_radius = 0.18
		_trunk_mesh.bottom_radius = 0.28
		_trunk_mesh.height = 3.2
		_trunk_mesh.radial_segments = 6
		_trunk_mesh.material = CarBase.make_mat(Color(0.35, 0.25, 0.16), 0.0, 1.0)
		var cols := [Color(0.22, 0.45, 0.16), Color(0.3, 0.5, 0.18), Color(0.18, 0.38, 0.14)]
		for c in cols:
			var crown := SphereMesh.new()
			crown.radius = 2.3
			crown.height = 4.2
			crown.radial_segments = 10
			crown.rings = 6
			crown.material = CarBase.make_mat(c, 0.0, 0.9)
			_crown_meshes.append(crown)
	var t_xf: Array[Transform3D] = []
	var c_xf: Array = [[], [], []]
	for t in xf:
		var s := rng.randf_range(0.8, 1.4)
		var b := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(s, s, s))
		t_xf.append(Transform3D(b, t.origin + Vector3(0, 1.6 * s, 0)))
		c_xf[rng.randi() % 3].append(Transform3D(b, t.origin + Vector3(0, 4.6 * s, 0)))
	_multimesh(_trunk_mesh, t_xf, 700.0)
	for k in 3:
		var arr: Array[Transform3D] = []
		arr.assign(c_xf[k])
		if not arr.is_empty():
			_multimesh(_crown_meshes[k], arr, 900.0)


func _build_nature() -> void:
	# tepeler
	var hill_mat := m_grass
	var placed := 0
	for tries in 400:
		if placed >= 45:
			break
		var p := Vector3(rng.randf_range(-3200, 2300), 0, rng.randf_range(-3000, 2600))
		var r := rng.randf_range(60, 200)
		if in_water(p.x - r) or in_water(p.x + r) or in_water(p.x):
			continue
		if not is_free(p, r + 20.0):
			continue
		var hgt := r * rng.randf_range(0.35, 0.7)
		var mi := MeshInstance3D.new()
		var sm := SphereMesh.new()
		sm.radius = r
		sm.height = hgt * 2.0
		sm.radial_segments = 24
		sm.rings = 10
		mi.mesh = sm
		mi.material_override = hill_mat
		mi.position = p
		add_child(mi)
		var sb := StaticBody3D.new()
		sb.position = p
		var cs := CollisionShape3D.new()
		cs.shape = sm.create_convex_shape()
		sb.add_child(cs)
		add_child(sb)
		_mark_circle(p, r)
		placed += 1
	# tarlalar
	var crops := [Color(0.85, 0.75, 0.35), Color(0.55, 0.65, 0.25), Color(0.7, 0.55, 0.3), Color(0.45, 0.6, 0.2)]
	for tries in 120:
		var p := Vector3(rng.randf_range(-2800, 2200), 0, rng.randf_range(-2800, 2400))
		var sz := Vector2(rng.randf_range(80, 200), rng.randf_range(80, 200))
		if in_water(p.x - sz.x) or in_water(p.x + sz.x) or not is_free(p, maxf(sz.x, sz.y) * 0.6):
			continue
		var mi := MeshInstance3D.new()
		var pm := PlaneMesh.new()
		pm.size = sz
		mi.mesh = pm
		mi.material_override = CarBase.make_mat(crops[rng.randi() % crops.size()], 0.0, 1.0)
		mi.position = p + Vector3(0, 0.03, 0)
		mi.rotation.y = rng.randf() * PI
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mi)
	# ağaçlar
	var xf: Array[Transform3D] = []
	for tries in 9000:
		if xf.size() >= 3200:
			break
		var p := Vector3(rng.randf_range(-3200, 2340), 0, rng.randf_range(-3000, 2600))
		if in_water(p.x) or not is_free(p, 0.0):
			continue
		if p.x > -720 and p.x < 720 and p.z < -790 and p.z > -1050:
			continue
		xf.append(Transform3D(Basis(), p))
	_trees(xf, false)


# ================================================================ tünel ve köprüler

func _build_tunnel_ridge() -> void:
	var zc := -920.0
	var depth := 240.0
	var h := 60.0
	var tx := 100.0
	var half := 15.0
	_static_box(Vector3(tx - half + 700.0, h, depth), Vector3((-700.0 + tx - half) * 0.5, h * 0.5, zc), m_rock, true)
	_static_box(Vector3(700.0 - tx - half, h, depth), Vector3((700.0 + tx + half) * 0.5, h * 0.5, zc), m_rock, true)
	_static_box(Vector3(half * 2.0, h - 9.0, depth), Vector3(tx, 9.0 + (h - 9.0) * 0.5, zc), m_rock, true)
	# yumuşak dağ siluetleri
	for c in [[-450.0, 40.0, 170.0], [-150.0, 20.0, 130.0], [330.0, 20.0, 130.0], [560.0, 40.0, 150.0], [tx, 62.0, 45.0]]:
		var mi := MeshInstance3D.new()
		var sm := SphereMesh.new()
		sm.radius = c[2]
		sm.height = c[2] * 1.4
		mi.mesh = sm
		mi.material_override = m_grass
		mi.position = Vector3(c[0], c[1], zc)
		add_child(mi)
	# portal çerçeveleri
	for zz in [zc + depth * 0.5, zc - depth * 0.5]:
		_static_box(Vector3(half * 2.0 + 4.0, 3.0, 2.0), Vector3(tx, 10.5, zz), m_concrete, false)
		for s in [-1.0, 1.0]:
			_static_box(Vector3(2.0, 9.0, 2.0), Vector3(tx + s * (half + 1.0), 4.5, zz), m_concrete, false)
		var lbl := Label3D.new()
		lbl.text = "DAĞ TÜNELİ"
		lbl.font_size = 96
		lbl.pixel_size = 0.02
		lbl.position = Vector3(tx, 10.5, zz + signf(zz - zc) * 1.1)
		lbl.rotation.y = 0.0 if zz > zc else PI
		add_child(lbl)
	# tünel aydınlatması
	var strip := CarVisual.emissive(Color(1.0, 0.9, 0.7), 3.0)
	_static_box(Vector3(0.6, 0.1, depth), Vector3(tx - 6.0, 8.9, zc), strip, false)
	_static_box(Vector3(0.6, 0.1, depth), Vector3(tx + 6.0, 8.9, zc), strip, false)
	for k in 5:
		var l := OmniLight3D.new()
		l.light_color = Color(1.0, 0.85, 0.6)
		l.omni_range = 30.0
		l.light_energy = 1.5
		l.shadow_enabled = false
		l.position = Vector3(tx, 7.5, zc - depth * 0.5 + 24.0 + k * 48.0)
		add_child(l)
		tunnel_lights.append(l)
	_mark_rect(Vector2(-720, zc - depth * 0.5 - 20), Vector2(720, zc + depth * 0.5 + 20))


func _cable(points: PackedVector3Array, radius: float, out: Array[Transform3D]) -> void:
	for i in points.size() - 1:
		var a := points[i]
		var b := points[i + 1]
		var d := b - a
		var len := d.length()
		var y := d / len
		var x := y.cross(Vector3.FORWARD if absf(y.z) < 0.9 else Vector3.RIGHT).normalized()
		var z := x.cross(y)
		out.append(Transform3D(Basis(x * radius, y * len, z * radius), (a + b) * 0.5))


func _build_bridges() -> void:
	var steel := CarBase.make_mat(Color(0.75, 0.2, 0.15), 0.6, 0.4)
	var white_steel := CarBase.make_mat(Color(0.9, 0.9, 0.92), 0.6, 0.35)
	var cyl := CylinderMesh.new()
	cyl.top_radius = 1.0
	cyl.bottom_radius = 1.0
	cyl.height = 1.0
	cyl.radial_segments = 6
	cyl.rings = 1
	cyl.material = steel
	# ---- O-1 asma köprü (z=250, güverte y=14)
	var z := 250.0
	var deck := 14.05
	var hw: float = HALF["hw"]
	var top := deck + 46.0
	var cable_xf: Array[Transform3D] = []
	var hanger_xf: Array[Transform3D] = []
	for tx in [RIVER_X0, RIVER_X1]:
		for s in [-1.0, 1.0]:
			_static_box(Vector3(3.0, top + 11.0, 3.0), Vector3(tx, (top - 11.0) * 0.5, z + s * (hw + 2.5)), steel, true, 3000.0)
		_static_box(Vector3(3.0, 3.0, hw * 2.0 + 8.0), Vector3(tx, top - 2.0, z), steel, false, 3000.0)
		_static_box(Vector3(3.0, 2.5, hw * 2.0 + 8.0), Vector3(tx, top - 18.0, z), steel, false, 3000.0)
		_static_box(Vector3(3.0, 2.0, hw * 2.0 + 8.0), Vector3(tx, deck - 2.6, z), steel, false, 3000.0)
	for s in [-1.0, 1.0]:
		var zz: float = z + s * (hw + 2.5)
		var pts := PackedVector3Array()
		# yan açıklık
		for k in 11:
			var t := k / 10.0
			pts.append(Vector3(lerpf(RIVER_X0 - 140.0, RIVER_X0, t), lerpf(deck + 1.0, top, t * t), zz))
		# ana açıklık (parabol)
		for k in range(1, 31):
			var t := k / 30.0
			var x := lerpf(RIVER_X0, RIVER_X1, t)
			var y := deck + 4.0 + (top - deck - 4.0) * pow(2.0 * t - 1.0, 2.0)
			pts.append(Vector3(x, y, zz))
		for k in range(1, 11):
			var t := k / 10.0
			pts.append(Vector3(lerpf(RIVER_X1, RIVER_X1 + 140.0, t), lerpf(top, deck + 1.0, t * (2.0 - t)), zz))
		_cable(pts, 0.45, cable_xf)
		var x := RIVER_X0 + 8.0
		while x < RIVER_X1 - 4.0:
			var t := (x - RIVER_X0) / (RIVER_X1 - RIVER_X0)
			var y := deck + 4.0 + (top - deck - 4.0) * pow(2.0 * t - 1.0, 2.0)
			_cable(PackedVector3Array([Vector3(x, deck + 0.9, zz), Vector3(x, y, zz)]), 0.08, hanger_xf)
			x += 8.0
	_multimesh(cyl, cable_xf, 3000.0)
	var thin := cyl.duplicate()
	thin.material = white_steel
	_multimesh(thin, hanger_xf, 1200.0, false)
	# ---- O-3 kemer köprü (z=-1200, güverte y=10)
	z = -1200.0
	deck = 10.05
	var arch_xf: Array[Transform3D] = []
	var arch_h: Array[Transform3D] = []
	for s in [-1.0, 1.0]:
		var zz: float = z + s * (hw + 1.5)
		var pts := PackedVector3Array()
		for k in 41:
			var t := k / 40.0
			pts.append(Vector3(lerpf(RIVER_X0 - 10.0, RIVER_X1 + 10.0, t), deck - 12.0 + 48.0 * (1.0 - pow(2.0 * t - 1.0, 2.0)), zz))
		_cable(pts, 1.1, arch_xf)
		var x := RIVER_X0 + 16.0
		while x < RIVER_X1 - 10.0:
			var t := (x + 10.0 - RIVER_X0) / (RIVER_X1 - RIVER_X0 + 20.0)
			var y := deck - 12.0 + 48.0 * (1.0 - pow(2.0 * t - 1.0, 2.0))
			if y > deck + 1.0:
				_cable(PackedVector3Array([Vector3(x, deck + 0.9, zz), Vector3(x, y, zz)]), 0.12, arch_h)
			x += 12.0
	var white_cyl := cyl.duplicate()
	white_cyl.material = white_steel
	_multimesh(white_cyl, arch_xf, 3000.0)
	_multimesh(thin, arch_h, 1200.0, false)
	for x in [RIVER_X0 - 10.0, RIVER_X1 + 10.0]:
		_static_box(Vector3(8.0, 24.0, hw * 2.0 + 6.0), Vector3(x, -2.0, z), m_concrete, true, 3000.0)


# ================================================================ gece / gündüz

func set_night(amount: float) -> void:
	_night = amount
	for m in window_mats:
		m.set_shader_parameter("night", amount * 1.8)
	for m in bulb_mats:
		m.emission_energy_multiplier = 0.2 + amount * 5.0


## Yakındaki şehir ışıklarını aç (performans: sadece en yakın ~16 ışık)
func update_lights(p: Vector3) -> void:
	var on := _night > 0.2
	for l in city_lights:
		var vis := on and l.position.distance_squared_to(p) < 260.0 * 260.0
		l.visible = vis
		l.light_energy = _night * 2.2
