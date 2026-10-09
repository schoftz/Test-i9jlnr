class_name CarBase
extends VehicleBody3D
## Tüm araçlar için ortak fizik + prosedürel gövde. İleri yön = +Z.

var cfg: Dictionary = {}
var kind: String = "player"   # player / traffic / police / racer

var throttle: float = 0.0     # -1..1
var steer_in: float = 0.0     # -1 (sağ) .. 1 (sol)
var brake_in: float = 0.0
var handbrake: bool = false
var nitro_on: bool = false

var speed_kmh: float = 0.0
var forward_speed: float = 0.0
var max_steer: float = 0.55
var wheels: Array[VehicleWheel3D] = []
var front_wheels: Array[VehicleWheel3D] = []
var rear_wheels: Array[VehicleWheel3D] = []
var body_mat: StandardMaterial3D
var light_mats: Array[StandardMaterial3D] = []
var siren_lights: Array[OmniLight3D] = []
var siren_mats: Array[StandardMaterial3D] = []
var headlight: SpotLight3D
var _siren_t: float = 0.0


func setup(c: Dictionary, k: String) -> void:
	cfg = c
	kind = k
	mass = cfg.get("mass", 1300.0)
	center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	center_of_mass = Vector3(0, 0.15, 0.1)
	angular_damp = 1.2
	linear_damp = 0.05
	can_sleep = false
	_build_body()
	_build_wheels()


static func make_mat(c: Color, metallic: float = 0.0, rough: float = 0.6) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.metallic = metallic
	m.roughness = rough
	return m


static func make_emissive(c: Color, energy: float = 3.0) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.emission_enabled = true
	m.emission = c
	m.emission_energy_multiplier = energy
	return m


func _box(size: Vector3, pos: Vector3, mat: Material, parent: Node3D = null) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = mat
	mi.position = pos
	(parent if parent else self).add_child(mi)
	return mi


func _build_body() -> void:
	var style: int = cfg.get("style", 0)
	var col: Color = cfg.get("color", Color.WHITE)
	if kind == "police":
		col = Color(0.05, 0.05, 0.07)
	body_mat = make_mat(col, 0.75, 0.25)
	body_mat.clearcoat_enabled = true
	body_mat.clearcoat = 1.0
	var glass := make_mat(Color(0.05, 0.07, 0.1), 0.9, 0.05)
	var dark := make_mat(Color(0.03, 0.03, 0.03), 0.2, 0.8)
	var length := 4.3
	var width := 1.85
	var body_h := 0.55
	var cab_len := 2.0
	var cab_h := 0.5
	var cab_z := -0.2
	var body_y := 0.65
	match style:
		0:
			length = 4.0; body_h = 0.6; cab_len = 2.2; cab_h = 0.55; cab_z = -0.3
		1:
			length = 4.6; body_h = 0.6; cab_len = 1.9; cab_h = 0.5; cab_z = -0.35
		2:
			length = 4.5; width = 1.95; body_h = 0.45; cab_len = 1.7; cab_h = 0.42; cab_z = -0.1; body_y = 0.58
	# alt gövde
	_box(Vector3(width, body_h, length), Vector3(0, body_y, 0), body_mat)
	# ön kaput eğimi hissi için ince üst parça
	_box(Vector3(width * 0.96, 0.08, length * 0.3), Vector3(0, body_y + body_h * 0.5 + 0.02, length * 0.3), body_mat)
	# kabin (cam) + tavan
	var cab_y := body_y + body_h * 0.5 + cab_h * 0.5
	_box(Vector3(width * 0.84, cab_h, cab_len), Vector3(0, cab_y, cab_z), glass)
	_box(Vector3(width * 0.8, 0.07, cab_len * 0.82), Vector3(0, cab_y + cab_h * 0.5 + 0.03, cab_z - 0.05), body_mat)
	# tampon / etek
	_box(Vector3(width + 0.04, 0.2, length + 0.06), Vector3(0, body_y - body_h * 0.5 + 0.05, 0), dark)
	# spoiler
	if style >= 1 or kind == "racer":
		_box(Vector3(width * 0.9, 0.05, 0.35), Vector3(0, body_y + body_h * 0.5 + 0.32, -length * 0.5 + 0.2), body_mat)
		_box(Vector3(0.06, 0.3, 0.2), Vector3(width * 0.35, body_y + body_h * 0.5 + 0.15, -length * 0.5 + 0.2), dark)
		_box(Vector3(0.06, 0.3, 0.2), Vector3(-width * 0.35, body_y + body_h * 0.5 + 0.15, -length * 0.5 + 0.2), dark)
	# farlar
	var head_mat := make_emissive(Color(1.0, 0.95, 0.8), 4.0)
	var tail_mat := make_emissive(Color(1.0, 0.05, 0.05), 3.0)
	light_mats.append(tail_mat)
	for s in [-1.0, 1.0]:
		_box(Vector3(0.4, 0.14, 0.05), Vector3(s * width * 0.33, body_y + 0.1, length * 0.5 + 0.01), head_mat)
		_box(Vector3(0.45, 0.12, 0.05), Vector3(s * width * 0.33, body_y + 0.12, -length * 0.5 - 0.01), tail_mat)
	if kind == "player" or kind == "police" or kind == "racer":
		headlight = SpotLight3D.new()
		headlight.light_color = Color(1.0, 0.95, 0.85)
		headlight.light_energy = 6.0
		headlight.spot_range = 45.0
		headlight.spot_angle = 32.0
		headlight.shadow_enabled = false
		headlight.position = Vector3(0, body_y + 0.15, length * 0.5 + 0.2)
		headlight.rotation = Vector3(deg_to_rad(-6), PI, 0)
		add_child(headlight)
	# polis: beyaz kapılar + tepe lambası
	if kind == "police":
		var white := make_mat(Color(0.92, 0.92, 0.95), 0.5, 0.3)
		for s in [-1.0, 1.0]:
			_box(Vector3(0.02, body_h * 0.8, length * 0.45), Vector3(s * (width * 0.5 + 0.01), body_y, 0), white)
		var bar_y := cab_y + cab_h * 0.5 + 0.13
		_box(Vector3(1.2, 0.08, 0.3), Vector3(0, bar_y - 0.06, cab_z), dark)
		var red := make_emissive(Color(1, 0.05, 0.05), 6.0)
		var blue := make_emissive(Color(0.1, 0.2, 1), 6.0)
		siren_mats = [red, blue]
		_box(Vector3(0.5, 0.12, 0.25), Vector3(0.3, bar_y, cab_z), red)
		_box(Vector3(0.5, 0.12, 0.25), Vector3(-0.3, bar_y, cab_z), blue)
		for i in 2:
			var l := OmniLight3D.new()
			l.light_color = Color(1, 0.1, 0.1) if i == 0 else Color(0.15, 0.25, 1)
			l.omni_range = 14.0
			l.light_energy = 0.0
			l.position = Vector3(0.6 if i == 0 else -0.6, bar_y + 0.3, cab_z)
			add_child(l)
			siren_lights.append(l)
	# çarpışma şekli
	var cs := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = Vector3(width, body_h + 0.25, length)
	cs.shape = shape
	cs.position = Vector3(0, body_y + 0.05, 0)
	add_child(cs)
	var cs2 := CollisionShape3D.new()
	var shape2 := BoxShape3D.new()
	shape2.size = Vector3(width * 0.84, cab_h, cab_len)
	cs2.shape = shape2
	cs2.position = Vector3(0, cab_y, cab_z)
	add_child(cs2)
	set_meta("half_length", length * 0.5)


func _build_wheels() -> void:
	var style: int = cfg.get("style", 0)
	var wx := 0.82 if style != 2 else 0.88
	var wz := 1.35 if style == 0 else 1.45
	var tire_mat := make_mat(Color(0.05, 0.05, 0.05), 0.0, 0.9)
	var rim_mat := make_mat(Color(0.75, 0.75, 0.78), 1.0, 0.2)
	var grip: float = cfg.get("grip", 2.8)
	for i in 4:
		var front := i < 2
		var side := 1.0 if i % 2 == 0 else -1.0
		var w := VehicleWheel3D.new()
		w.position = Vector3(side * wx, 0.5, wz if front else -wz)
		w.wheel_radius = 0.36
		w.wheel_rest_length = 0.15
		w.suspension_travel = 0.2
		w.suspension_stiffness = 45.0
		w.suspension_max_force = 12000.0
		w.damping_compression = 1.6
		w.damping_relaxation = 2.0
		w.wheel_friction_slip = grip
		w.wheel_roll_influence = 0.15
		w.use_as_steering = front
		w.use_as_traction = not front
		add_child(w)
		var tire := MeshInstance3D.new()
		var cm := CylinderMesh.new()
		cm.top_radius = 0.36
		cm.bottom_radius = 0.36
		cm.height = 0.26
		cm.radial_segments = 18
		tire.mesh = cm
		tire.material_override = tire_mat
		tire.rotation.z = PI * 0.5
		w.add_child(tire)
		var rim := MeshInstance3D.new()
		var rm := CylinderMesh.new()
		rm.top_radius = 0.22
		rm.bottom_radius = 0.22
		rm.height = 0.28
		rm.radial_segments = 6
		rim.mesh = rm
		rim.material_override = rim_mat
		rim.rotation.z = PI * 0.5
		w.add_child(rim)
		wheels.append(w)
		if front:
			front_wheels.append(w)
		else:
			rear_wheels.append(w)


func set_paint(c: Color) -> void:
	if body_mat:
		body_mat.albedo_color = c


func _physics_process(delta: float) -> void:
	drive(delta)


func drive(delta: float) -> void:
	var fwd := global_basis.z
	var vel := linear_velocity
	forward_speed = vel.dot(fwd)
	speed_kmh = vel.length() * 3.6
	var top: float = cfg.get("top", 200.0) / 3.6
	var power: float = cfg.get("power", 6000.0)
	if nitro_on:
		top *= 1.2
		power *= 1.7
	var ef := 0.0
	var br := brake_in * 30.0
	if throttle > 0.01:
		if forward_speed < -2.0:
			br = max(br, 35.0 * throttle)
		else:
			var falloff := clampf((top - forward_speed) / (top * 0.35), 0.0, 1.0)
			ef = throttle * power * falloff
	elif throttle < -0.01:
		if forward_speed > 2.0:
			br = max(br, 40.0 * -throttle)
		elif forward_speed > -12.0:
			ef = throttle * power * 0.6
	else:
		br = max(br, 1.5)
	engine_force = ef
	brake = br
	# hıza duyarlı direksiyon
	var speed_factor := lerpf(1.0, 0.28, clampf(absf(forward_speed) / 55.0, 0.0, 1.0))
	if handbrake:
		speed_factor = maxf(speed_factor, 0.7)
	var target_steer := steer_in * max_steer * speed_factor
	steering = move_toward(steering, target_steer, delta * 3.5)
	# drift: el freni arka tutuşu düşürür
	var grip: float = cfg.get("grip", 2.8)
	for w in rear_wheels:
		w.wheel_friction_slip = lerpf(w.wheel_friction_slip, grip * (0.32 if handbrake else 0.95), delta * (12.0 if handbrake else 2.5))
		w.brake = 18.0 if handbrake else 0.0
	for w in front_wheels:
		w.wheel_friction_slip = grip * 1.1
	# arcade yardımcıları
	var ground := _wheels_on_ground()
	if ground > 0:
		apply_central_force(-global_basis.y * vel.length_squared() * 1.2)
		if handbrake and absf(forward_speed) > 6.0:
			apply_torque(Vector3.UP * steer_in * mass * 2.8)
		# kayma açısına karşı hafif düzeltme (drift kontrolü)
		var side_v := vel.dot(global_basis.x)
		if not handbrake:
			apply_central_force(-global_basis.x * side_v * mass * 0.35)
		if nitro_on and forward_speed > 0:
			apply_central_force(fwd * mass * 3.0)
	else:
		# havada denge
		var up_err := global_basis.y.cross(Vector3.UP)
		apply_torque(up_err * mass * 8.0)
	_update_siren(delta)
	for m in light_mats:
		m.emission_energy_multiplier = 9.0 if (brake_in > 0.1 or throttle < -0.1 and forward_speed > 1.0) else 2.5


func _wheels_on_ground() -> int:
	var n := 0
	for w in wheels:
		if w.is_in_contact():
			n += 1
	return n


func set_siren(on: bool) -> void:
	set_meta("siren", on)


func _update_siren(delta: float) -> void:
	if siren_lights.is_empty():
		return
	var on: bool = get_meta("siren", false)
	_siren_t += delta
	var phase := int(_siren_t * 6.0) % 2
	for i in 2:
		var lit := on and phase == i
		siren_lights[i].light_energy = 6.0 if lit else 0.0
		siren_mats[i].emission_energy_multiplier = 10.0 if lit else (0.6 if on else 0.2)


func reset_upright() -> void:
	var yaw := global_rotation.y
	global_transform = Transform3D(Basis(Vector3.UP, yaw), global_position + Vector3.UP * 1.5)
	linear_velocity = Vector3.ZERO
	angular_velocity = Vector3.ZERO


func place(pos: Vector3, yaw: float) -> void:
	global_transform = Transform3D(Basis(Vector3.UP, yaw), pos + Vector3.UP * 0.6)
	linear_velocity = Vector3.ZERO
	angular_velocity = Vector3.ZERO


## Hedef noktaya doğru direksiyon değeri (-1..1)
func steer_toward(target: Vector3) -> float:
	var local := global_transform.affine_inverse() * target
	var ang := atan2(local.x, local.z)
	return clampf(ang * 2.2, -1.0, 1.0)
