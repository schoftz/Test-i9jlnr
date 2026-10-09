extends Node3D
## Ana sahne: dünyayı, oyuncuyu, kamerayı, trafiği, polisi, yarışları ve HUD'u kurar.

const DAY_LENGTH := 480.0       # saniye (tam gün)
const TRAFFIC_COUNT := 16
const PATROL_COUNT := 3

var city: City
var player: PlayerCar
var camera: Camera3D
var cam_mode: int = 0           # 0 takip, 1 tampon, 2 uzak
var hud: HUD
var police: PoliceManager
var races: RaceManager
var env: Environment
var sun: DirectionalLight3D
var sky_mat: ProceduralSkyMaterial
var time_of_day: float = 21.0   # gece başla: Most Wanted havası
var traffic: Array[AICar] = []
var garage_pos: Vector3
var garage_node: int
var engine_audio: EngineAudio
var _cam_vel_fov: float = 75.0
var _cam_pos: Vector3
var _shake: float = 0.0
var _traffic_timer: float = 0.0


func _ready() -> void:
	randomize()
	process_mode = Node.PROCESS_MODE_PAUSABLE
	_build_environment()
	city = City.new()
	city.name = "City"
	add_child(city)
	garage_node = City.idx(2, 3)
	garage_pos = city.nodes[garage_node] + Vector3(0, 0, 32.0)
	_build_garage_marker()
	camera = Camera3D.new()
	camera.fov = 75.0
	camera.far = 2500.0
	add_child(camera)
	camera.make_current()
	hud = HUD.new()
	hud.main = self
	add_child(hud)
	police = PoliceManager.new()
	police.main = self
	add_child(police)
	races = RaceManager.new()
	races.main = self
	add_child(races)
	engine_audio = EngineAudio.new()
	add_child(engine_audio)
	spawn_player(true)
	for n in TRAFFIC_COUNT:
		_spawn_traffic(true)
	for n in PATROL_COUNT:
		police.spawn_patrol()
	Game.toast("Rockport'a hoş geldin! J: Yarışlar/İşler, E: Garaj (sarı halka)", Color(1, 0.8, 0.2))


# ------------------------------------------------------------------ ortam

func _build_environment() -> void:
	env = Environment.new()
	env.background_mode = Environment.BG_SKY
	var sky := Sky.new()
	sky_mat = ProceduralSkyMaterial.new()
	sky.sky_material = sky_mat
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = 0.6
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.tonemap_exposure = 1.0
	env.tonemap_white = 6.0
	env.glow_enabled = true
	env.glow_intensity = 0.9
	env.glow_strength = 1.0
	env.glow_bloom = 0.08
	env.glow_hdr_threshold = 1.0
	env.glow_blend_mode = Environment.GLOW_BLEND_MODE_SOFTLIGHT
	env.ssao_enabled = true
	env.ssao_radius = 1.5
	env.ssao_intensity = 1.5
	env.ssr_enabled = true
	env.ssr_max_steps = 48
	env.fog_enabled = true
	env.fog_density = 0.0025
	env.fog_aerial_perspective = 0.4
	env.adjustment_enabled = true
	env.adjustment_saturation = 1.1
	env.adjustment_contrast = 1.05
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)
	sun = DirectionalLight3D.new()
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 250.0
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	add_child(sun)


func _update_day_night(delta: float) -> void:
	time_of_day = fmod(time_of_day + delta * 24.0 / DAY_LENGTH, 24.0)
	var t := time_of_day / 24.0
	# güneş açısı: 6'da doğar, 18'de batar
	var elev := sin((time_of_day - 6.0) / 24.0 * TAU)   # -1..1
	var day := clampf(elev * 3.0 + 0.2, 0.0, 1.0)
	var night := 1.0 - day
	sun.rotation = Vector3(-asin(clampf(elev, -1, 1)) if elev > 0.0 else -0.3, t * TAU + 0.6, 0)
	sun.light_energy = lerpf(0.06, 1.3, day)
	var dusk := clampf(1.0 - absf(elev) * 4.0, 0.0, 1.0)
	sun.light_color = Color(1.0, 0.95, 0.88).lerp(Color(1.0, 0.55, 0.3), dusk)
	if day <= 0.0:
		sun.light_color = Color(0.5, 0.6, 1.0)  # ay ışığı
	var top_day := Color(0.22, 0.45, 0.85)
	var hor_day := Color(0.65, 0.75, 0.9)
	var top_night := Color(0.01, 0.015, 0.05)
	var hor_night := Color(0.06, 0.07, 0.14)
	sky_mat.sky_top_color = top_night.lerp(top_day, day)
	sky_mat.sky_horizon_color = hor_night.lerp(hor_day, day).lerp(Color(0.95, 0.5, 0.3), dusk * 0.6)
	sky_mat.ground_horizon_color = sky_mat.sky_horizon_color
	sky_mat.ground_bottom_color = Color(0.05, 0.05, 0.05).lerp(Color(0.2, 0.2, 0.2), day)
	env.ambient_light_energy = lerpf(0.25, 0.8, day)
	env.fog_light_color = sky_mat.sky_horizon_color
	env.fog_density = lerpf(0.004, 0.0018, day)
	city.set_night(night)
	if player and player.headlight:
		player.headlight.light_energy = lerpf(8.0, 1.0, day)


# ------------------------------------------------------------------ oyuncu

func spawn_player(at_garage: bool) -> void:
	var old_pos := garage_pos
	var old_yaw := PI
	if player and is_instance_valid(player):
		old_pos = player.global_position
		old_yaw = player.global_rotation.y
		player.queue_free()
	player = PlayerCar.new()
	player.name = "Player"
	player.setup(Game.get_car_config(Game.current), "player")
	add_child(player)
	if at_garage:
		player.place(city.nodes[garage_node] + Vector3(-City.LANE, 0, 10.0), 0.0)
	else:
		player.place(old_pos, old_yaw)
	player.hit_police.connect(police.on_player_hit_police)
	_cam_pos = player.global_position + Vector3(0, 4, -8)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("camera"):
		cam_mode = (cam_mode + 1) % 3
	elif event.is_action_pressed("reset_car"):
		player.reset_upright()
		Game.toast("Araç düzeltildi", Color(0.8, 0.8, 0.8))
	elif event.is_action_pressed("garage"):
		if player.global_position.distance_to(garage_pos) < 16.0:
			if police.heat_level() > 0:
				Game.toast("Takip sırasında garaja giremezsin!", Color(1, 0.3, 0.3))
			elif races.active:
				Game.toast("Yarış sırasında garaja giremezsin!", Color(1, 0.3, 0.3))
			else:
				hud.open_garage()
		else:
			Game.toast("Garaj haritada sarı G ile işaretli", Color(1, 0.85, 0.3))
	elif event.is_action_pressed("menu_jobs"):
		hud.open_jobs()
	elif event.is_action_pressed("pause"):
		hud.toggle_pause()


func _physics_process(delta: float) -> void:
	_update_day_night(delta)
	_update_camera(delta)
	_manage_traffic(delta)
	engine_audio.update_from(player, police.nearest_siren_distance())


func _update_camera(delta: float) -> void:
	if player == null or not is_instance_valid(player):
		return
	var p := player.global_position
	var vel := player.linear_velocity
	var flat_v := Vector3(vel.x, 0, vel.z)
	var fwd := player.global_basis.z
	fwd.y = 0
	fwd = fwd.normalized()
	# hızla birlikte kamera hareket yönüne kayar
	var look_dir := fwd
	if flat_v.length() > 5.0 and player.forward_speed > 0:
		look_dir = fwd.lerp(flat_v.normalized(), 0.35).normalized()
	var speed := vel.length()
	var target_fov := 72.0 + clampf(speed * 0.25, 0.0, 18.0) + (14.0 if player.nitro_on else 0.0)
	_cam_vel_fov = lerpf(_cam_vel_fov, target_fov, delta * 3.0)
	camera.fov = _cam_vel_fov
	if player.nitro_on:
		_shake = 0.06
	else:
		_shake = move_toward(_shake, 0.0, delta)
	var shake_v := Vector3(randf_range(-1, 1), randf_range(-1, 1), 0) * _shake
	match cam_mode:
		0, 2:
			var dist := 7.0 if cam_mode == 0 else 11.0
			var height := 2.6 if cam_mode == 0 else 4.2
			dist += clampf(speed * 0.03, 0.0, 2.5)
			var desired := p - look_dir * dist + Vector3.UP * height
			_cam_pos = _cam_pos.lerp(desired, clampf(delta * 6.0, 0.0, 1.0))
			camera.global_position = _cam_pos + shake_v
			camera.look_at(p + Vector3.UP * 1.2 + look_dir * 3.0, Vector3.UP)
		1:
			camera.global_transform = player.global_transform * Transform3D(Basis(Vector3.UP, PI), Vector3(0, 1.05, 1.6))
			camera.global_position += shake_v * 0.3
			_cam_pos = camera.global_position


# ------------------------------------------------------------------ trafik

func _spawn_traffic(anywhere: bool) -> void:
	var n := _spawn_node(anywhere)
	var nb: Array = city.neighbors[n]
	var to: int = nb[randi() % nb.size()]
	var car := AICar.new()
	var ids := Game.CARS.keys()
	var c := Game.get_car_config(ids[randi() % 4])
	c["color"] = Color.from_hsv(randf(), randf_range(0.2, 0.8), randf_range(0.3, 0.9))
	c["power"] = c["power"] * 0.7
	car.setup(c, "traffic")
	car.init_on_graph(city, n, to)
	car.target_speed = randf_range(40.0, 60.0)
	add_child(car)
	var a := city.nodes[n]
	var b := city.nodes[to]
	var dir := (b - a).normalized()
	var start := a + dir * 20.0 + dir.cross(Vector3.UP) * City.LANE
	car.place(start, atan2(dir.x, dir.z))
	car.linear_velocity = dir * 8.0
	car.add_to_group("traffic")
	traffic.append(car)


func _spawn_node(anywhere: bool) -> int:
	var pp := player.global_position if player else Vector3.ZERO
	for tries in 30:
		var n := randi() % city.nodes.size()
		var d := city.nodes[n].distance_to(pp)
		if anywhere and d > 40.0:
			return n
		if d > 140.0 and d < 320.0:
			return n
	return randi() % city.nodes.size()


func _manage_traffic(delta: float) -> void:
	_traffic_timer -= delta
	var cars: Array = all_vehicles()
	for t in traffic:
		t.all_cars = cars
	if _traffic_timer > 0.0:
		return
	_traffic_timer = 1.0
	var pp := player.global_position
	for t in traffic.duplicate():
		if not is_instance_valid(t):
			traffic.erase(t)
			continue
		if t.global_position.distance_to(pp) > 380.0 or t.global_position.y < -20.0:
			traffic.erase(t)
			t.queue_free()
	while traffic.size() < TRAFFIC_COUNT:
		_spawn_traffic(false)


func all_vehicles() -> Array:
	var arr: Array = []
	arr.append_array(traffic)
	arr.append_array(police.units)
	arr.append_array(races.racers)
	if player:
		arr.append(player)
	return arr


func respawn_at_garage() -> void:
	player.place(city.nodes[garage_node] + Vector3(-City.LANE, 0, 10.0), 0.0)
	_cam_pos = player.global_position + Vector3(0, 4, -8)


func _build_garage_marker() -> void:
	var ring := MeshInstance3D.new()
	var tm := TorusMesh.new()
	tm.inner_radius = 5.5
	tm.outer_radius = 6.2
	ring.mesh = tm
	ring.material_override = CarBase.make_emissive(Color(1.0, 0.75, 0.1), 4.0)
	ring.position = garage_pos + Vector3(0, 0.3, 0)
	add_child(ring)
	var lbl := Label3D.new()
	lbl.text = "GARAJ\n[E]"
	lbl.font_size = 96
	lbl.outline_size = 16
	lbl.modulate = Color(1, 0.8, 0.2)
	lbl.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	lbl.position = garage_pos + Vector3(0, 6, 0)
	add_child(lbl)
	var light := OmniLight3D.new()
	light.light_color = Color(1, 0.75, 0.2)
	light.omni_range = 15
	light.light_energy = 2.0
	light.position = garage_pos + Vector3(0, 3, 0)
	add_child(light)
