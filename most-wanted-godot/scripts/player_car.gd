class_name PlayerCar
extends CarBase
## Oyuncu aracı: klavye girişi, nitro, çarpışma bildirimi, nitro parçacıkları.

signal hit_police(car: Node)

var nitro_amount: float = 1.0   # 0..1
var controls_enabled: bool = true
var particles: Array[GPUParticles3D] = []
var _steer_smooth: float = 0.0


func setup(c: Dictionary, k: String) -> void:
	super.setup(c, k)
	contact_monitor = true
	max_contacts_reported = 6
	body_entered.connect(_on_body_entered)
	_build_particles()
	add_to_group("player")


func _build_particles() -> void:
	var half: float = get_meta("half_length", 2.2)
	for s in [-0.45, 0.45]:
		var p := GPUParticles3D.new()
		var pm := ParticleProcessMaterial.new()
		pm.direction = Vector3(0, 0.1, -1)
		pm.spread = 8.0
		pm.initial_velocity_min = 6.0
		pm.initial_velocity_max = 10.0
		pm.gravity = Vector3.ZERO
		pm.scale_min = 0.25
		pm.scale_max = 0.5
		var grad := Gradient.new()
		grad.set_color(0, Color(0.4, 0.7, 1.0, 1.0))
		grad.set_color(1, Color(0.1, 0.2, 1.0, 0.0))
		var gt := GradientTexture1D.new()
		gt.gradient = grad
		pm.color_ramp = gt
		p.process_material = pm
		var q := QuadMesh.new()
		q.size = Vector2(0.4, 0.4)
		var qm := StandardMaterial3D.new()
		qm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		qm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		qm.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
		qm.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
		qm.vertex_color_use_as_albedo = true
		qm.albedo_color = Color(0.6, 0.8, 2.0)
		q.material = qm
		p.draw_pass_1 = q
		p.amount = 40
		p.lifetime = 0.25
		p.local_coords = false
		p.emitting = false
		p.position = Vector3(s, 0.45, -half - 0.1)
		add_child(p)
		particles.append(p)


func _physics_process(delta: float) -> void:
	if controls_enabled:
		var acc := Input.get_action_strength("accelerate")
		var rev := Input.get_action_strength("brake")
		throttle = acc - rev
		var s_target := Input.get_action_strength("steer_left") - Input.get_action_strength("steer_right")
		_steer_smooth = move_toward(_steer_smooth, s_target, delta * (6.0 if absf(s_target) > absf(_steer_smooth) else 9.0))
		steer_in = _steer_smooth
		handbrake = Input.is_action_pressed("handbrake")
		var want_nitro := Input.is_action_pressed("nitro") and nitro_amount > 0.0 and acc > 0.1
		nitro_on = want_nitro
		brake_in = 0.0
	else:
		throttle = 0.0
		steer_in = 0.0
		brake_in = 1.0
		handbrake = false
		nitro_on = false
	var cap: float = cfg.get("nitro", 3.0)
	if nitro_on:
		nitro_amount = maxf(0.0, nitro_amount - delta / cap)
	else:
		# drift ve hız nitro doldurur (Most Wanted gibi)
		var side := absf(linear_velocity.dot(global_basis.x))
		var gain := 0.015 + (0.06 if side > 4.0 and speed_kmh > 50 else 0.0) + (0.02 if speed_kmh > 150 else 0.0)
		nitro_amount = minf(1.0, nitro_amount + gain * delta)
	for p in particles:
		p.emitting = nitro_on
	super.drive(delta)


func _on_body_entered(body: Node) -> void:
	if body.is_in_group("police"):
		hit_police.emit(body)
