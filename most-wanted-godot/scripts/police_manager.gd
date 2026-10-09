class_name PoliceManager
extends Node
## Aranma seviyesi (1-5 yıldız), polis takibi, barikatlar, soğuma ve yakalanma.

const PATROL_COUNT := 3

var main
var units: Array[AICar] = []
var heat: float = 0.0
var state: String = "none"         # none / pursuit / cooldown
var pursuit_time: float = 0.0
var cooldown_timer: float = 0.0
var unseen_timer: float = 0.0
var busted_timer: float = 0.0
var spawn_timer: float = 0.0
var roadblock_timer: float = 20.0
var check_timer: float = 0.0
var infraction_cooldown: float = 0.0
var cars_hit: int = 0
var enabled: bool = true


func heat_level() -> int:
	if state == "none":
		return 0
	return clampi(int(heat), 1, 5)


func cooldown_needed() -> float:
	return 8.0 + heat_level() * 4.0


func spawn_patrol() -> void:
	_spawn_unit("patrol", main.world.random_node_between(main.player.global_position, 120.0, 400.0))


func _spawn_unit(mode: String, n: int) -> AICar:
	var nb: Array = main.city.neighbors[n]
	var to: int = nb[randi() % nb.size()]
	var car := AICar.new()
	var c: Dictionary = Game.POLICE_CAR.duplicate()
	c["power"] = float(c["hp"]) * 24.0 * (1.0 + heat_level() * 0.1)
	c["top"] = 230.0 + heat_level() * 15.0
	var pm := Game.model_path_for("police_cvpi")
	if pm != "":
		c["model"] = pm
	car.setup(c, "police")
	car.init_on_graph(main.city, n, to)
	car.target_speed = 55.0
	car.mode = mode
	main.add_child(car)
	var a: Vector3 = main.city.nodes[n]
	var b: Vector3 = main.city.nodes[to]
	var dir := (b - a).normalized()
	var fd := Vector3(dir.x, 0, dir.z).normalized()
	car.place(a + dir * 12.0 + fd.cross(Vector3.UP) * float(World.LANES[main.world.edge_kind(n, to)][0]), atan2(dir.x, dir.z))
	car.add_to_group("police")
	if mode == "chase":
		car.target = main.player
		car.set_siren(true)
	units.append(car)
	return car


func on_player_hit_police(_car: Node) -> void:
	if not enabled:
		return
	if infraction_cooldown > 0.0:
		return
	infraction_cooldown = 1.0
	cars_hit += 1
	if state == "none":
		_start_pursuit("Polis aracına çarptın!")
		heat = maxf(heat, 1.5)
	else:
		heat = minf(5.99, heat + 0.3)
		Game.toast("Polise çarpma! Aranma seviyesi artıyor", Color(1, 0.3, 0.3))


func _start_pursuit(reason: String) -> void:
	if state == "pursuit":
		return
	state = "pursuit"
	heat = maxf(heat, 1.0)
	pursuit_time = 0.0
	unseen_timer = 0.0
	cooldown_timer = 0.0
	cars_hit = 0
	Game.toast("TAKİP BAŞLADI: " + reason, Color(1, 0.2, 0.2))
	for u in units:
		if u.mode == "patrol" and is_instance_valid(u):
			u.mode = "chase"
			u.target = main.player
			u.set_siren(true)


func _process(delta: float) -> void:
	if main == null or main.player == null:
		return
	infraction_cooldown = maxf(0.0, infraction_cooldown - delta)
	var player: PlayerCar = main.player
	var cars: Array = main.all_vehicles()
	for u in units.duplicate():
		if not is_instance_valid(u):
			units.erase(u)
			continue
		u.all_cars = cars
		if u.target != player and u.mode == "chase":
			u.target = player
	if not enabled:
		return
	check_timer -= delta
	var seen := false
	var close_count := 0
	if check_timer <= 0.0:
		check_timer = 0.2
		for u in units:
			var d: float = u.global_position.distance_to(player.global_position)
			if d < 9.0:
				close_count += 1
			var view := 160.0 if state == "pursuit" else (90.0 if state == "cooldown" else 70.0)
			if d < view and _line_of_sight(u, player):
				seen = true
				if state == "none" and player.speed_kmh > 110.0:
					_start_pursuit("Aşırı hız!")
		match state:
			"pursuit":
				if seen:
					unseen_timer = 0.0
				else:
					unseen_timer += 0.2
					if unseen_timer > 4.0:
						state = "cooldown"
						cooldown_timer = 0.0
						Game.toast("Görüş dışındasın — saklan!", Color(0.4, 0.8, 1))
			"cooldown":
				if seen:
					state = "pursuit"
					unseen_timer = 0.0
					Game.toast("Tekrar görüldün!", Color(1, 0.3, 0.3))
		# yakalanma
		if state != "none" and close_count > 0 and player.speed_kmh < 12.0:
			busted_timer += 0.2 * (1.5 if close_count >= 2 else 1.0)
			if busted_timer >= 3.0:
				_busted()
				return
		else:
			busted_timer = maxf(0.0, busted_timer - 0.3)
	match state:
		"pursuit":
			pursuit_time += delta
			heat = minf(5.99, heat + delta * 0.022)
			_maintain_units(delta)
			roadblock_timer -= delta
			if heat_level() >= 3 and roadblock_timer <= 0.0:
				roadblock_timer = 40.0 - heat_level() * 3.0
				_spawn_roadblock()
		"cooldown":
			pursuit_time += delta
			cooldown_timer += delta
			if cooldown_timer >= cooldown_needed():
				_escaped()
		"none":
			_maintain_patrols()
	_cleanup()


func _line_of_sight(u: Node3D, p: Node3D) -> bool:
	var space := (u as Node3D).get_world_3d().direct_space_state
	var q := PhysicsRayQueryParameters3D.create(u.global_position + Vector3.UP * 1.5, p.global_position + Vector3.UP * 1.0)
	q.exclude = [u.get_rid(), p.get_rid()]
	var hit := space.intersect_ray(q)
	if hit.is_empty():
		return true
	return hit.collider is VehicleBody3D


func _maintain_units(delta: float) -> void:
	spawn_timer -= delta
	var want := mini(2 + int(heat_level() * 1.4), 9)
	var chasing := 0
	for u in units:
		if u.mode == "chase":
			chasing += 1
	if chasing < want and spawn_timer <= 0.0:
		spawn_timer = 4.0
		_spawn_unit("chase", main.world.random_node_between(main.player.global_position, 150.0, 330.0))


func _maintain_patrols() -> void:
	var patrols := 0
	for u in units:
		if u.mode == "patrol":
			patrols += 1
	if patrols < PATROL_COUNT:
		spawn_patrol()


func _spawn_roadblock() -> void:
	var p: PlayerCar = main.player
	var fwd := p.linear_velocity
	fwd.y = 0
	if fwd.length() < 5.0:
		return
	fwd = fwd.normalized()
	var probe := p.global_position + fwd * 200.0
	var n: int = main.city.nearest_node(probe)
	var np: Vector3 = main.city.nodes[n]
	if np.distance_to(p.global_position) < 90.0:
		return
	# barikatı oyuncunun geleceği yönde, kavşaktan önce kur
	var to_player := (p.global_position - np)
	to_player.y = 0
	var axis := to_player.normalized()
	var center := np + axis * 20.0
	var across := axis.cross(Vector3.UP)
	var count := 3
	for m in main.world.neighbors[n]:
		if main.world.edge_kind(n, m) == "hw":
			count = 5
	for k in count:
		var car := _spawn_unit("roadblock", n)
		car.place(center + across * (k - (count - 1) * 0.5) * 4.8, atan2(across.x, across.z))
		car.set_siren(true)
		car.set_meta("ttl", 45.0)
	Game.toast("İLERİDE BARİKAT!", Color(1, 0.5, 0.1))


func _cleanup() -> void:
	var pp: Vector3 = main.player.global_position
	for u in units.duplicate():
		var d: float = u.global_position.distance_to(pp)
		var remove := false
		if u.has_meta("ttl"):
			var ttl: float = u.get_meta("ttl") - get_process_delta_time()
			u.set_meta("ttl", ttl)
			remove = ttl <= 0.0 and d > 60.0
		if state == "none" and u.mode != "patrol" and d > 120.0:
			remove = true
		if d > 450.0 or u.global_position.y < -20.0:
			remove = true
		if remove:
			units.erase(u)
			u.queue_free()


func _end_pursuit() -> void:
	state = "none"
	heat = 0.0
	busted_timer = 0.0
	for u in units:
		if u.mode == "chase":
			u.mode = "patrol"
			u.target = null
			u.set_siren(false)
			var n: int = main.world.nearest_node(u.global_position)
			u.init_on_graph(main.world, n, main.world.pick_next(-1, n))


func _escaped() -> void:
	var lvl := heat_level()
	var bounty := int(lvl * 1500 + pursuit_time * 25 + cars_hit * 250)
	Game.escapes += 1
	Game.total_bounty += bounty
	Game.add_money(bounty)
	Game.toast("KAÇTIN! Ödül: $%d" % bounty, Color(0.3, 1, 0.4))
	main.hud.big_message("KAÇTIN!", Color(0.3, 1, 0.4))
	_end_pursuit()


func _busted() -> void:
	var lvl := heat_level()
	var fine := 750 * lvl
	Game.busted += 1
	Game.add_money(-fine)
	main.hud.big_message("YAKALANDIN!", Color(1, 0.2, 0.2))
	Game.toast("Ceza: $%d" % fine, Color(1, 0.3, 0.3))
	_end_pursuit()
	for u in units.duplicate():
		units.erase(u)
		u.queue_free()
	main.respawn_at_garage()


func clear_all() -> void:
	_end_pursuit()


func nearest_siren_distance() -> float:
	if main == null or main.player == null:
		return INF
	var best := INF
	for u in units:
		if is_instance_valid(u) and u.get_meta("siren", false):
			best = minf(best, u.global_position.distance_to(main.player.global_position))
	return best


func pursuit_bar() -> float:
	if state == "cooldown":
		return cooldown_timer / cooldown_needed()
	if state == "pursuit":
		return clampf(1.0 - unseen_timer / 4.0, 0.0, 1.0)
	return 0.0
