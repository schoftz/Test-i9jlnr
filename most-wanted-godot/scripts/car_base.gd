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


var dims: Vector3 = Vector3(1.8, 1.4, 4.5)
var visual: Dictionary = {}


func setup(c: Dictionary, k: String) -> void:
	cfg = c
	kind = k
	mass = cfg.get("mass", 1300.0)
	center_of_mass_mode = RigidBody3D.CENTER_OF_MASS_MODE_CUSTOM
	center_of_mass = Vector3(0, 0.35, 0.05)
	angular_damp = 1.2
	linear_damp = 0.05
	can_sleep = false
	visual = CarVisual.build(self, cfg, kind)
	body_mat = visual["paint"]
	for m in visual["lights"]:
		light_mats.append(m)
	for m in visual["siren_mats"]:
		siren_mats.append(m)
	for l in visual["siren_lights"]:
		siren_lights.append(l)
	headlight = visual["headlight"]
	dims = visual["dims"]
	_build_collision()
	_build_wheels()


static func make_mat(c: Color, metallic: float = 0.0, rough: float = 0.6) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.metallic = metallic
	m.roughness = rough
	return m


static func make_emissive(c: Color, energy: float = 3.0) -> StandardMaterial3D:
	return CarVisual.emissive(c, energy)


func _build_collision() -> void:
	var W := dims.x
	var H := dims.y
	var L := dims.z
	var cs := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = Vector3(W * 0.95, H * 0.42, L * 0.96)
	cs.shape = shape
	cs.position = Vector3(0, 0.3 + H * 0.21, 0)
	add_child(cs)
	var cs2 := CollisionShape3D.new()
	var shape2 := BoxShape3D.new()
	shape2.size = Vector3(W * 0.8, H * 0.38, L * 0.45)
	cs2.shape = shape2
	cs2.position = Vector3(0, 0.3 + H * 0.42 + H * 0.17, -L * 0.05)
	add_child(cs2)
	set_meta("half_length", L * 0.5)


func _build_wheels() -> void:
	var grip: float = cfg.get("grip", 2.8)
	var drive_type: String = cfg.get("drive", "RWD")
	var r: float = visual["wheel_radius"]
	var pos: Array = visual["wheels"]
	var show: bool = visual["show_wheels"]
	for i in 4:
		var front := i < 2
		var p: Vector3 = pos[i]
		var w := VehicleWheel3D.new()
		w.position = p
		w.wheel_radius = r
		w.wheel_rest_length = 0.12
		w.suspension_travel = 0.18
		w.suspension_stiffness = 50.0
		w.suspension_max_force = mass * 12.0
		w.damping_compression = 1.8
		w.damping_relaxation = 2.4
		w.wheel_friction_slip = grip
		w.wheel_roll_influence = 0.12
		w.use_as_steering = front
		w.use_as_traction = (not front) or drive_type == "AWD" or (front and drive_type == "FWD")
		if drive_type == "FWD" and not front:
			w.use_as_traction = false
		add_child(w)
		var vis := CarVisual.make_wheel(r, front, signf(p.x))
		vis.visible = show
		w.add_child(vis)
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
