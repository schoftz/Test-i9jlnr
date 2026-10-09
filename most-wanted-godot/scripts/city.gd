class_name City
extends Node3D
## Prosedürel şehir: ızgara sokaklar, dış otoban halkası, binalar, lambalar, ağaçlar.
## Yol grafiği (düğümler + komşular) trafik / polis / yarış yapay zekası için kullanılır.

const N := 8            # her eksende düğüm sayısı (0..7)
const B := 100.0        # blok aralığı (m)
const ROAD_W := 14.0
const HIGHWAY_W := 24.0
const LANE := 3.5

var nodes: Array[Vector3] = []
var neighbors: Array = []   # Array[Array[int]]
var window_mats: Array[StandardMaterial3D] = []
var lamp_lights: Array[OmniLight3D] = []
var lamp_bulb_mat: StandardMaterial3D
var rng := RandomNumberGenerator.new()


func _ready() -> void:
	rng.seed = 1337
	_build_graph()
	_build_ground()
	_build_roads()
	_build_blocks()
	_build_lamps()
	_build_guardrails()
	_build_outskirts()


static func idx(i: int, j: int) -> int:
	return j * N + i


func node_pos(i: int, j: int) -> Vector3:
	return nodes[idx(i, j)]


static func coord(i: int) -> float:
	return (i - (N - 1) * 0.5) * B


static func is_ring(i: int) -> bool:
	return i == 0 or i == N - 1


static func road_width(i: int) -> float:
	return HIGHWAY_W if is_ring(i) else ROAD_W


func extent() -> float:
	return coord(N - 1)


func _build_graph() -> void:
	nodes.clear()
	neighbors.clear()
	for j in N:
		for i in N:
			nodes.append(Vector3(coord(i), 0, coord(j)))
			var nb: Array[int] = []
			if i > 0: nb.append(idx(i - 1, j))
			if i < N - 1: nb.append(idx(i + 1, j))
			if j > 0: nb.append(idx(i, j - 1))
			if j < N - 1: nb.append(idx(i, j + 1))
			neighbors.append(nb)


func nearest_node(p: Vector3) -> int:
	var best := 0
	var bd := INF
	for n in nodes.size():
		var d := nodes[n].distance_squared_to(Vector3(p.x, 0, p.z))
		if d < bd:
			bd = d
			best = n
	return best


func lane_point(from_n: int, to_n: int, lane_offset: float = LANE) -> Vector3:
	var a := nodes[from_n]
	var b := nodes[to_n]
	var d := (b - a).normalized()
	var right := d.cross(Vector3.UP)
	var i: int = to_n % N
	var j: int = to_n / N
	var off := lane_offset
	# otobanda daha geniş şerit
	if (absf(d.x) > 0.5 and is_ring(j)) or (absf(d.z) > 0.5 and is_ring(i)):
		off = lane_offset * 1.6
	return b + right * off


func pick_next(prev_n: int, cur_n: int) -> int:
	var opts: Array = neighbors[cur_n].duplicate()
	if opts.size() > 1:
		opts.erase(prev_n)
	return opts[rng.randi() % opts.size()]


# ---------------------------------------------------------------- yapım

func _static_box(size: Vector3, pos: Vector3, mat: Material, collide: bool = true) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = mat
	mi.position = pos
	add_child(mi)
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


func _noise_tex(base: Color, var_amt: float, size: int = 128) -> ImageTexture:
	var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
	for y in size:
		for x in size:
			var v := rng.randf_range(-var_amt, var_amt)
			img.set_pixel(x, y, Color(base.r + v, base.g + v, base.b + v))
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


func _build_ground() -> void:
	var grass := StandardMaterial3D.new()
	grass.albedo_texture = _noise_tex(Color(0.18, 0.3, 0.12), 0.04)
	grass.uv1_scale = Vector3(200, 200, 1)
	grass.roughness = 1.0
	var mi := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(3000, 3000)
	mi.mesh = pm
	mi.material_override = grass
	add_child(mi)
	var sb := StaticBody3D.new()
	var cs := CollisionShape3D.new()
	cs.shape = WorldBoundaryShape3D.new()
	sb.add_child(cs)
	add_child(sb)


func _build_roads() -> void:
	var asphalt := StandardMaterial3D.new()
	asphalt.albedo_texture = _noise_tex(Color(0.11, 0.11, 0.12), 0.03)
	asphalt.uv1_triplanar = true
	asphalt.uv1_world_triplanar = true
	asphalt.uv1_scale = Vector3(0.25, 0.25, 0.25)
	asphalt.roughness = 0.55
	asphalt.metallic_specular = 0.6
	var asphalt2 := asphalt.duplicate()
	var line_y := make_line_mat(Color(0.95, 0.75, 0.1))
	var line_w := make_line_mat(Color(0.9, 0.9, 0.9))
	var ext := extent()
	var total := ext * 2.0 + HIGHWAY_W
	for k in N:
		var w := road_width(k)
		var c := coord(k)
		# yatay (X boyunca) ve dikey (Z boyunca) şeritler
		_static_box(Vector3(total, 0.04, w), Vector3(0, 0.02, c), asphalt, false)
		_static_box(Vector3(w, 0.04, total), Vector3(c, 0.025, 0), asphalt2, false)
		# orta çizgi
		_static_box(Vector3(total, 0.01, 0.25), Vector3(0, 0.05, c), line_y, false)
		_static_box(Vector3(0.25, 0.01, total), Vector3(c, 0.052, 0), line_y, false)
		if is_ring(k):
			for s in [-1.0, 1.0]:
				_static_box(Vector3(total, 0.01, 0.18), Vector3(0, 0.05, c + s * w * 0.25), line_w, false)
				_static_box(Vector3(0.18, 0.01, total), Vector3(c + s * w * 0.25, 0.051, 0), line_w, false)


static func make_line_mat(c: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.emission_enabled = true
	m.emission = c
	m.emission_energy_multiplier = 0.15
	m.roughness = 0.4
	return m


func _window_texture(lit_color: Color, lit_ratio: float) -> Array:
	var size := 64
	var alb := Image.create(size, size, false, Image.FORMAT_RGBA8)
	var emi := Image.create(size, size, false, Image.FORMAT_RGBA8)
	var cells := 4
	var cs := size / cells
	for cy in cells:
		for cx in cells:
			var lit := rng.randf() < lit_ratio
			var warm := lit_color * rng.randf_range(0.6, 1.1)
			for y in cs:
				for x in cs:
					var px := cx * cs + x
					var py := cy * cs + y
					var is_win := x > 2 and x < cs - 3 and y > 3 and y < cs - 2
					if is_win:
						alb.set_pixel(px, py, Color(0.12, 0.16, 0.22))
						emi.set_pixel(px, py, warm if lit else Color(0.02, 0.02, 0.03))
					else:
						alb.set_pixel(px, py, Color(1, 1, 1))
						emi.set_pixel(px, py, Color(0, 0, 0))
	alb.generate_mipmaps()
	emi.generate_mipmaps()
	return [ImageTexture.create_from_image(alb), ImageTexture.create_from_image(emi)]


func _build_blocks() -> void:
	var walk := StandardMaterial3D.new()
	walk.albedo_texture = _noise_tex(Color(0.45, 0.44, 0.42), 0.05)
	walk.uv1_triplanar = true
	walk.uv1_world_triplanar = true
	walk.uv1_scale = Vector3(0.5, 0.5, 0.5)
	var park := StandardMaterial3D.new()
	park.albedo_color = Color(0.2, 0.38, 0.14)
	var roof := CarBase.make_mat(Color(0.18, 0.18, 0.2), 0.0, 0.9)
	var tints := [Color(0.55, 0.52, 0.5), Color(0.35, 0.38, 0.45), Color(0.6, 0.5, 0.4), Color(0.25, 0.27, 0.3), Color(0.7, 0.7, 0.72)]
	var lit_cols := [Color(1.0, 0.8, 0.45), Color(0.7, 0.85, 1.0), Color(1.0, 0.9, 0.7)]
	for t in tints.size():
		var tex := _window_texture(lit_cols[t % lit_cols.size()], 0.45)
		var m := StandardMaterial3D.new()
		m.albedo_color = tints[t]
		m.albedo_texture = tex[0]
		m.emission_enabled = true
		m.emission_texture = tex[1]
		m.emission = Color(1, 1, 1)
		m.emission_energy_multiplier = 0.0
		m.uv1_triplanar = true
		m.uv1_world_triplanar = true
		m.uv1_scale = Vector3(1.0 / 16.0, 1.0 / 16.0, 1.0 / 16.0)
		m.roughness = 0.5
		m.metallic = 0.2
		window_mats.append(m)
	var trees: Array[Transform3D] = []
	for j in N - 1:
		for i in N - 1:
			var x0 := coord(i) + road_width(i) * 0.5
			var x1 := coord(i + 1) - road_width(i + 1) * 0.5
			var z0 := coord(j) + road_width(j) * 0.5
			var z1 := coord(j + 1) - road_width(j + 1) * 0.5
			var cx := (x0 + x1) * 0.5
			var cz := (z0 + z1) * 0.5
			var sx := x1 - x0
			var sz := z1 - z0
			var edge: bool = i == 0 or j == 0 or i == N - 2 or j == N - 2
			var is_park := (i == 3 and j == 3) or (edge and rng.randf() < 0.3)
			_static_box(Vector3(sx, 0.2, sz), Vector3(cx, 0.1, cz), walk)
			if is_park:
				_static_box(Vector3(sx - 6, 0.05, sz - 6), Vector3(cx, 0.22, cz), park, false)
				for t in 14:
					trees.append(Transform3D(Basis(), Vector3(cx + rng.randf_range(-sx * 0.4, sx * 0.4), 0.2, cz + rng.randf_range(-sz * 0.4, sz * 0.4))))
				continue
			# 2x2 bina
			var dist_center := Vector2(cx, cz).length()
			var hmax := lerpf(110.0, 25.0, clampf(dist_center / 330.0, 0.0, 1.0))
			for by in 2:
				for bx in 2:
					var w := sx * 0.5 - 6.0 - rng.randf_range(0, 6)
					var d := sz * 0.5 - 6.0 - rng.randf_range(0, 6)
					var h := rng.randf_range(10.0, hmax)
					h = snappedf(h, 4.0)
					var px := cx + (bx - 0.5) * sx * 0.5
					var pz := cz + (by - 0.5) * sz * 0.5
					_static_box(Vector3(w, h, d), Vector3(px, 0.2 + h * 0.5, pz), window_mats[rng.randi() % window_mats.size()])
					_static_box(Vector3(w + 0.6, 0.6, d + 0.6), Vector3(px, 0.2 + h + 0.3, pz), roof, false)
					if h > 60 and rng.randf() < 0.5:
						_static_box(Vector3(w * 0.5, 8.0, d * 0.5), Vector3(px, 0.2 + h + 4.0, pz), window_mats[0], false)
			# kaldırım ağaçları
			for t in 4:
				var tx := cx + (sx * 0.5 - 2.0) * (1 if t % 2 == 0 else -1)
				var tz := cz + rng.randf_range(-sz * 0.4, sz * 0.4)
				trees.append(Transform3D(Basis(), Vector3(tx, 0.2, tz)))
	_build_trees(trees)


func _build_trees(xforms: Array[Transform3D]) -> void:
	var trunk := CylinderMesh.new()
	trunk.top_radius = 0.18
	trunk.bottom_radius = 0.25
	trunk.height = 3.0
	trunk.material = CarBase.make_mat(Color(0.3, 0.2, 0.12), 0.0, 1.0)
	var crown := SphereMesh.new()
	crown.radius = 2.0
	crown.height = 3.6
	crown.material = CarBase.make_mat(Color(0.12, 0.35, 0.1), 0.0, 0.9)
	var mm1 := MultiMesh.new()
	mm1.transform_format = MultiMesh.TRANSFORM_3D
	mm1.mesh = trunk
	mm1.instance_count = xforms.size()
	var mm2 := MultiMesh.new()
	mm2.transform_format = MultiMesh.TRANSFORM_3D
	mm2.mesh = crown
	mm2.instance_count = xforms.size()
	for n in xforms.size():
		var t := xforms[n]
		var s := rng.randf_range(0.8, 1.3)
		mm1.set_instance_transform(n, Transform3D(Basis().scaled(Vector3(s, s, s)), t.origin + Vector3(0, 1.5 * s, 0)))
		mm2.set_instance_transform(n, Transform3D(Basis().scaled(Vector3(s, s, s)), t.origin + Vector3(0, 4.2 * s, 0)))
	for mm in [mm1, mm2]:
		var mmi := MultiMeshInstance3D.new()
		mmi.multimesh = mm
		add_child(mmi)
	# ağaç gövdesi çarpışması
	for t in xforms:
		var sb := StaticBody3D.new()
		sb.position = t.origin + Vector3(0, 1.5, 0)
		var cs := CollisionShape3D.new()
		var sh := CylinderShape3D.new()
		sh.radius = 0.3
		sh.height = 3.0
		cs.shape = sh
		sb.add_child(cs)
		add_child(sb)


func _build_lamps() -> void:
	lamp_bulb_mat = CarBase.make_emissive(Color(1.0, 0.85, 0.55), 0.5)
	var pole_mat := CarBase.make_mat(Color(0.2, 0.2, 0.22), 0.8, 0.4)
	var pole := CylinderMesh.new()
	pole.top_radius = 0.1
	pole.bottom_radius = 0.14
	pole.height = 8.0
	pole.material = pole_mat
	var bulb := BoxMesh.new()
	bulb.size = Vector3(0.6, 0.2, 1.2)
	bulb.material = lamp_bulb_mat
	var poles: Array[Vector3] = []
	for j in N:
		for i in N:
			var p := node_pos(i, j)
			var off := road_width(i) * 0.5 + 1.5
			var offz := road_width(j) * 0.5 + 1.5
			poles.append(p + Vector3(off, 0, offz))
			# yol ortasında ara lambalar
			if i < N - 1:
				poles.append(p + Vector3(B * 0.5, 0, -offz))
			if j < N - 1:
				poles.append(p + Vector3(-off, 0, B * 0.5))
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = pole
	mm.instance_count = poles.size()
	var mmb := MultiMesh.new()
	mmb.transform_format = MultiMesh.TRANSFORM_3D
	mmb.mesh = bulb
	mmb.instance_count = poles.size()
	for n in poles.size():
		mm.set_instance_transform(n, Transform3D(Basis(), poles[n] + Vector3(0, 4.0, 0)))
		mmb.set_instance_transform(n, Transform3D(Basis(), poles[n] + Vector3(0, 8.0, 0)))
		if n % 2 == 0:
			var l := OmniLight3D.new()
			l.light_color = Color(1.0, 0.8, 0.5)
			l.omni_range = 22.0
			l.light_energy = 0.0
			l.omni_attenuation = 1.2
			l.position = poles[n] + Vector3(0, 7.6, 0)
			l.visible = false
			add_child(l)
			lamp_lights.append(l)
	for m in [mm, mmb]:
		var mmi := MultiMeshInstance3D.new()
		mmi.multimesh = m
		add_child(mmi)


func _build_guardrails() -> void:
	var rail := CarBase.make_mat(Color(0.7, 0.72, 0.75), 0.9, 0.3)
	var ext := extent()
	var o := ext + HIGHWAY_W * 0.5 + 0.6
	var len := o * 2.0 + 1.0
	for s in [-1.0, 1.0]:
		_static_box(Vector3(len, 0.9, 0.3), Vector3(0, 0.45, s * o), rail)
		_static_box(Vector3(0.3, 0.9, len), Vector3(s * o, 0.45, 0), rail)
	# otoban iç bariyerleri (beton, kavşaklarda açık)
	var concrete := CarBase.make_mat(Color(0.6, 0.6, 0.58), 0.0, 0.9)
	var inner := ext - HIGHWAY_W * 0.5 - 0.4
	for k in N - 1:
		var mid := (coord(k) + coord(k + 1)) * 0.5
		var seg := B - ROAD_W - 6.0
		for s in [-1.0, 1.0]:
			_static_box(Vector3(seg, 0.8, 0.4), Vector3(mid, 0.4, s * inner), concrete)
			_static_box(Vector3(0.4, 0.8, seg), Vector3(s * inner, 0.4, mid), concrete)


func _build_outskirts() -> void:
	# şehir dışı: tepeler ve ağaç kuşağı
	var hill_mat := CarBase.make_mat(Color(0.22, 0.3, 0.16), 0.0, 1.0)
	var ext := extent() + 60.0
	var trees: Array[Transform3D] = []
	for n in 40:
		var rad := rng.randf_range(40, 120)
		var hp := _outskirt_point(ext + rad * 0.5 + 40.0, ext + 450.0)
		var mi := MeshInstance3D.new()
		var sm := SphereMesh.new()
		sm.radius = rad
		sm.height = rad * rng.randf_range(0.5, 0.9)
		mi.mesh = sm
		mi.material_override = hill_mat
		mi.position = hp
		add_child(mi)
	for n in 160:
		trees.append(Transform3D(Basis(), _outskirt_point(ext - 35.0, ext + 80.0)))
	_build_trees(trees)


func _outskirt_point(min_d: float, max_d: float) -> Vector3:
	var d := rng.randf_range(min_d, max_d)
	var t := rng.randf_range(-max_d, max_d)
	match rng.randi() % 4:
		0: return Vector3(d, 0, t)
		1: return Vector3(-d, 0, t)
		2: return Vector3(t, 0, d)
	return Vector3(t, 0, -d)


func set_night(amount: float) -> void:
	for m in window_mats:
		m.emission_energy_multiplier = amount * 2.2
	lamp_bulb_mat.emission_energy_multiplier = 0.3 + amount * 6.0
	for l in lamp_lights:
		l.visible = amount > 0.15
		l.light_energy = amount * 2.5
