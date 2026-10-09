extends Node3D
## Ana sahne: dünya, oyuncu, kamera, trafik, polis, yarışlar, HUD, grafik kalitesi.

const DAY_LENGTH := 1200.0      # gündüz saatlerinde tam gün süresi (sn); gece 4 kat hızlı geçer
const QUALITY_NAMES := ["Düşük", "Orta", "Yüksek"]

var world: World
var city: World                 # eski isim (polis / yarış kodu için)
var player: PlayerCar
var camera: Camera3D
var cam_mode: int = 0
var hud: HUD
var police: PoliceManager
var races: RaceManager
var env: Environment
var sun: DirectionalLight3D
var sky_mat: ProceduralSkyMaterial
var time_of_day: float = 10.5
var traffic: Array[AICar] = []
var traffic_target: int = 14
var garage_pos: Vector3
var garage_node: int
var garage_spawn_from: int
var engine_audio: EngineAudio
var _cam_fov: float = 72.0
var _cam_pos: Vector3
var _shake: float = 0.0
var _slow_timer: float = 0.0
var _all_cache: Array = []
var _screenshot_path := ""
var _frames := 0


func _ready() -> void:
	randomize()
	var args := OS.get_cmdline_user_args()
	for a in args:
		if a.ends_with(".png"):
			_screenshot_path = a
	_build_environment()
	world = World.new()
	world.name = "World"
	add_child(world)
	city = world
	var grid: Array = world.areas["Merkez"]["grid"]
	garage_node = grid[1][2]
	garage_spawn_from = grid[1][1]
	garage_pos = world.nodes[garage_node].lerp(world.nodes[grid[1][3]], 0.4) + Vector3(World.HALF["city"] + 6.0, 0, 0)
	_build_garage()
	camera = Camera3D.new()
	camera.fov = 72.0
	camera.far = 3000.0
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
	apply_quality(Game.quality)
	# geliştirici argümanları (ekran görüntüsü testleri): spawn=x,y,z,yaw  time=saat  map
	for a in args:
		if a.begins_with("spawn="):
			var v := a.substr(6).split_floats(",")
			player.place(Vector3(v[0], v[1], v[2]), v[3])
			_cam_pos = player.global_position + Vector3(0, 4, -8)
		elif a.begins_with("time="):
			time_of_day = a.substr(5).to_float()
			Game.day_cycle = false
		elif a == "map":
			hud.call_deferred("toggle_map")
		elif a.begins_with("edge="):
			# n'inci verilen tipteki kenarın başına yerleş (ör. edge=hw,40)
			var parts := a.substr(5).split(",")
			var want := parts[0]
			var idx := int(parts[1])
			var cnt := 0
			for k in world.edge_type:
				if world.edge_type[k] == want:
					if cnt == idx:
						var na: int = k.x
						var nb: int = k.y
						var d := world.nodes[nb] - world.nodes[na]
						player.place(world.lane_point(nb, na, 0) * 0.0 + world.nodes[na] + Vector3(d.x, 0, d.z).normalized().cross(Vector3.UP) * float(World.LANES[want][0]), atan2(d.x, d.z))
						_cam_pos = player.global_position + Vector3(0, 4, -8)
						break
					cnt += 1
		elif a == "cam2":
			cam_mode = 2
	for n in traffic_target:
		_spawn_traffic(true)
	Game.toast("Hoş geldin! J: Yarışlar/İşler  •  E: Garaj  •  M/Tab: Harita  •  F: FPS", Color(1, 0.8, 0.2))


# ------------------------------------------------------------------ ortam

func _build_environment() -> void:
	env = Environment.new()
	env.background_mode = Environment.BG_SKY
	var sky := Sky.new()
	sky_mat = ProceduralSkyMaterial.new()
	sky_mat.sky_curve = 0.12
	sky_mat.ground_curve = 0.05
	sky_mat.sun_angle_max = 30.0
	sky.sky_material = sky_mat
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_sky_contribution = 0.85
	env.ambient_light_color = Color(0.75, 0.8, 0.9)
	env.ambient_light_energy = 1.0
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.tonemap_exposure = 1.0
	env.tonemap_white = 1.0
	env.glow_enabled = true
	env.glow_intensity = 0.35
	env.glow_strength = 0.9
	env.glow_bloom = 0.0
	env.glow_hdr_threshold = 1.2
	env.glow_blend_mode = Environment.GLOW_BLEND_MODE_ADDITIVE
	env.ssao_enabled = false
	env.ssr_enabled = false
	env.sdfgi_enabled = false
	env.fog_enabled = true
	env.fog_mode = Environment.FOG_MODE_EXPONENTIAL
	env.fog_density = 0.00025
	env.fog_sky_affect = 0.15
	env.fog_aerial_perspective = 0.0
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)
	sun = DirectionalLight3D.new()
	sun.shadow_enabled = true
	sun.light_energy = 1.4
	sun.shadow_blur = 1.0
	sun.directional_shadow_max_distance = 200.0
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS
	add_child(sun)


func apply_quality(q: int) -> void:
	q = clampi(q, 0, 2)
	Game.quality = q
	var vp := get_viewport()
	var hidpi := DisplayServer.screen_get_scale() > 1.5 or DisplayServer.window_get_size().x > 2400
	var scales := [0.6, 0.75, 1.0]
	var s: float = scales[q]
	if hidpi:
		s *= 0.75
	vp.scaling_3d_mode = Viewport.SCALING_3D_MODE_FSR if s < 0.99 else Viewport.SCALING_3D_MODE_BILINEAR
	vp.scaling_3d_scale = s
	vp.msaa_3d = Viewport.MSAA_DISABLED if q < 2 else Viewport.MSAA_2X
	vp.screen_space_aa = Viewport.SCREEN_SPACE_AA_FXAA
	sun.shadow_enabled = q > 0
	sun.directional_shadow_max_distance = [120.0, 200.0, 300.0][q]
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS if q < 2 else DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	env.glow_enabled = q > 0
	env.ssao_enabled = q == 2
	camera.far = [1200.0, 2000.0, 3000.0][q]
	traffic_target = [8, 14, 18][q]
	Game.save_game()


func _update_day_night(delta: float) -> void:
	if Game.day_cycle:
		var night_hours := time_of_day < 6.5 or time_of_day > 19.5
		time_of_day = fmod(time_of_day + delta * 24.0 / DAY_LENGTH * (4.0 if night_hours else 1.0), 24.0)
	var elev := sin((time_of_day - 6.0) / 24.0 * TAU)        # -1..1
	var day := clampf(elev * 3.0 + 0.25, 0.0, 1.0)
	var night := 1.0 - day
	var dusk := clampf(1.0 - absf(elev) * 3.5, 0.0, 1.0) * day
	var sun_elev := maxf(elev, 0.12) if day > 0.0 else 0.6
	sun.rotation = Vector3(-asin(clampf(sun_elev, 0.05, 1.0)) , deg_to_rad(35.0) + (time_of_day - 12.0) / 12.0 * PI * 0.8, 0)
	sun.light_energy = lerpf(0.12, 1.4, day)
	sun.light_color = Color(1.0, 0.97, 0.92).lerp(Color(1.0, 0.62, 0.38), dusk) if day > 0.0 else Color(0.55, 0.65, 1.0)
	sky_mat.sky_top_color = Color(0.03, 0.05, 0.12).lerp(Color(0.26, 0.5, 0.86), day)
	sky_mat.sky_horizon_color = Color(0.1, 0.12, 0.2).lerp(Color(0.68, 0.78, 0.9), day).lerp(Color(0.95, 0.62, 0.4), dusk * 0.7)
	sky_mat.ground_horizon_color = sky_mat.sky_horizon_color
	sky_mat.ground_bottom_color = Color(0.12, 0.13, 0.12).lerp(Color(0.35, 0.4, 0.3), day)
	sky_mat.sky_energy_multiplier = lerpf(0.5, 1.0, day)
	env.ambient_light_energy = lerpf(0.45, 1.0, day)
	env.fog_light_color = sky_mat.sky_horizon_color
	world.set_night(night)
	if player and player.headlight:
		player.headlight.light_energy = lerpf(5.0, 0.0, day)
		player.headlight.visible = night > 0.1


# ------------------------------------------------------------------ oyuncu

func spawn_player(at_garage: bool) -> void:
	var old_xf := Transform3D()
	var had := false
	if player and is_instance_valid(player):
		old_xf = player.global_transform
		had = true
		player.queue_free()
	player = PlayerCar.new()
	player.name = "Player"
	player.setup(Game.get_car_config(Game.current), "player")
	add_child(player)
	if at_garage or not had:
		respawn_at_garage()
	else:
		player.place(old_xf.origin, old_xf.basis.get_euler().y)
	player.hit_police.connect(police.on_player_hit_police)
	_cam_pos = player.global_position + Vector3(0, 4, -8)


func respawn_at_garage() -> void:
	var p := world.lane_point(garage_spawn_from, garage_node)
	var d := world.nodes[garage_node] - world.nodes[garage_spawn_from]
	player.place(p - d.normalized() * 30.0, atan2(d.x, d.z))
	_cam_pos = player.global_position + Vector3(0, 4, -8)


func reset_to_road() -> void:
	var n := world.nearest_node(player.global_position)
	var nb: Array = world.neighbors[n]
	var to: int = nb[0]
	var best := -2.0
	var fwd := player.global_basis.z
	for m in nb:
		var dd := (world.nodes[m] - world.nodes[n]).normalized().dot(fwd)
		if dd > best:
			best = dd
			to = m
	var d := world.nodes[to] - world.nodes[n]
	var lane_off: float = World.LANES[world.edge_kind(n, to)][0]
	var lp := world.nodes[n] + d.normalized().cross(Vector3.UP) * lane_off
	player.place(lp, atan2(d.x, d.z))


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("camera"):
		cam_mode = (cam_mode + 1) % 3
	elif event.is_action_pressed("reset_car"):
		reset_to_road()
		Game.toast("Araç yola alındı", Color(0.8, 0.8, 0.8))
	elif event.is_action_pressed("garage"):
		if player.global_position.distance_to(garage_pos) < 18.0:
			if police.heat_level() > 0:
				Game.toast("Takip sırasında garaja giremezsin!", Color(1, 0.3, 0.3))
			elif races.active:
				Game.toast("Yarış sırasında garaja giremezsin!", Color(1, 0.3, 0.3))
			else:
				hud.open_garage()
		else:
			Game.toast("Garaj Merkez'de, haritada sarı G ile işaretli", Color(1, 0.85, 0.3))
	elif event.is_action_pressed("menu_jobs"):
		hud.open_jobs()
	elif event.is_action_pressed("pause"):
		hud.toggle_pause()
	elif event.is_action_pressed("map"):
		hud.toggle_map()
	elif event.is_action_pressed("fps"):
		hud.toggle_fps()


func _process(delta: float) -> void:
	_update_day_night(delta)
	_update_camera(delta)
	engine_audio.update_from(player, police.nearest_siren_distance())
	if _screenshot_path != "":
		_frames += 1
		if _frames == 200:
			get_viewport().get_texture().get_image().save_png(_screenshot_path)
			get_tree().quit()


func _physics_process(delta: float) -> void:
	_manage_traffic(delta)


func _update_camera(delta: float) -> void:
	if player == null or not is_instance_valid(player):
		return
	var p := player.global_position
	var vel := player.linear_velocity
	var flat_v := Vector3(vel.x, 0, vel.z)
	var fwd := player.global_basis.z
	fwd.y = 0
	fwd = fwd.normalized()
	var look_dir := fwd
	if flat_v.length() > 5.0 and player.forward_speed > 0:
		look_dir = fwd.lerp(flat_v.normalized(), 0.35).normalized()
	var speed := vel.length()
	var target_fov := 70.0 + clampf(speed * 0.22, 0.0, 16.0) + (12.0 if player.nitro_on else 0.0)
	_cam_fov = lerpf(_cam_fov, target_fov, clampf(delta * 3.0, 0.0, 1.0))
	camera.fov = _cam_fov
	_shake = 0.05 if player.nitro_on else move_toward(_shake, 0.0, delta)
	var shake_v := Vector3(randf_range(-1, 1), randf_range(-1, 1), 0) * _shake
	var L: float = player.dims.z
	match cam_mode:
		0, 2:
			var dist := (3.6 + L) if cam_mode == 0 else (7.0 + L)
			var height := 2.3 if cam_mode == 0 else 4.0
			dist += clampf(speed * 0.025, 0.0, 2.0)
			var desired := p - look_dir * dist + Vector3.UP * height
			_cam_pos = _cam_pos.lerp(desired, clampf(delta * 7.0, 0.0, 1.0))
			# kamera yerin / yolun altına inmesin
			_cam_pos.y = maxf(_cam_pos.y, p.y + 1.0)
			camera.global_position = _cam_pos + shake_v
			camera.look_at(p + Vector3.UP * 1.1 + look_dir * 3.0, Vector3.UP)
		1:
			camera.global_transform = player.global_transform * Transform3D(Basis(Vector3.UP, PI), Vector3(0, player.dims.y * 0.62, L * 0.5 - 0.6))
			camera.global_position += shake_v * 0.3
			_cam_pos = camera.global_position


# ------------------------------------------------------------------ trafik

func _spawn_traffic(anywhere: bool) -> void:
	var pp := player.global_position if player else Vector3.ZERO
	var n := world.random_node_between(pp, 60.0 if anywhere else 160.0, 380.0)
	var nb: Array = world.neighbors[n]
	var to: int = nb[randi() % nb.size()]
	var car := AICar.new()
	var c: Dictionary
	if not Game.pack_cars.is_empty() and randf() < 0.6:
		c = Game.get_car_config(Game.pack_cars[randi() % Game.pack_cars.size()])
	else:
		c = Game.TRAFFIC_BODIES[randi() % Game.TRAFFIC_BODIES.size()].duplicate()
	c["color"] = Color.from_hsv(randf(), randf_range(0.05, 0.7), randf_range(0.25, 0.95))
	c["hp"] = 180
	c["power"] = 4200.0
	c["top"] = 170.0
	c["grip"] = 2.8
	c["mass"] = 1400.0
	c["drive"] = "FWD"
	car.setup(c, "traffic")
	var kind := world.edge_kind(n, to)
	car.init_on_graph(world, n, to)
	car.lane = randi() % world.lane_count(n, to)
	car.target_speed = randf_range(85.0, 115.0) if kind == "hw" else randf_range(40.0, 60.0)
	if kind == "hw":
		car.target_speed -= car.lane * 10.0
	add_child(car)
	var a := world.nodes[n]
	var b := world.nodes[to]
	var dir := (b - a).normalized()
	var off: float = World.LANES[kind][mini(car.lane, World.LANES[kind].size() - 1)]
	car.place(a + dir * minf(15.0, a.distance_to(b) * 0.4) + Vector3(dir.x, 0, dir.z).normalized().cross(Vector3.UP) * off, atan2(dir.x, dir.z))
	car.linear_velocity = dir * car.target_speed / 3.6 * 0.6
	car.add_to_group("traffic")
	traffic.append(car)


func _manage_traffic(delta: float) -> void:
	_slow_timer -= delta
	if _slow_timer > 0.0:
		return
	_slow_timer = 0.5
	_all_cache = all_vehicles()
	for t in traffic:
		t.all_cars = _all_cache
	var pp := player.global_position
	for t in traffic.duplicate():
		if not is_instance_valid(t):
			traffic.erase(t)
			continue
		var d: float = t.global_position.distance_to(pp)
		t.set_far(d > 220.0)
		if d > 480.0 or t.global_position.y < -20.0:
			traffic.erase(t)
			t.queue_free()
	var spawned := 0
	while traffic.size() < traffic_target and spawned < 2:
		_spawn_traffic(false)
		spawned += 1
	world.update_lights(pp)


func all_vehicles() -> Array:
	var arr: Array = []
	arr.append_array(traffic)
	arr.append_array(police.units)
	arr.append_array(races.racers)
	if player:
		arr.append(player)
	return arr


func _build_garage() -> void:
	var ring := MeshInstance3D.new()
	var tm := TorusMesh.new()
	tm.inner_radius = 5.5
	tm.outer_radius = 6.2
	ring.mesh = tm
	ring.material_override = CarVisual.emissive(Color(1.0, 0.75, 0.1), 2.0)
	ring.position = garage_pos + Vector3(0, 0.3, 0)
	add_child(ring)
	# garaj binası
	var bld := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = Vector3(10, 6, 14)
	bld.mesh = bm
	bld.material_override = CarBase.make_mat(Color(0.25, 0.27, 0.3), 0.3, 0.6)
	bld.position = garage_pos + Vector3(12, 3.2, 0)
	add_child(bld)
	var lbl := Label3D.new()
	lbl.text = "GARAJ [E]"
	lbl.font_size = 96
	lbl.outline_size = 16
	lbl.modulate = Color(1, 0.8, 0.2)
	lbl.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	lbl.position = garage_pos + Vector3(0, 6, 0)
	add_child(lbl)
