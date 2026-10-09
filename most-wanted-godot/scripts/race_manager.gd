class_name RaceManager
extends Node
## Sprint / devre yarışları (3 yapay zeka rakip) ve teslimat işleri.

const RACES := [
	{"name": "Merkez Sprinti", "type": "sprint", "laps": 1, "prize": 3000,
		"wp": [Vector3(-180, 0, -180), Vector3(-180, 0, 180), Vector3(0, 0, 180), Vector3(0, 0, -180), Vector3(180, 0, -180), Vector3(180, 0, 180)]},
	{"name": "Merkez Devresi", "type": "circuit", "laps": 2, "prize": 4500,
		"wp": [Vector3(-90, 0, -90), Vector3(90, 0, -90), Vector3(90, 0, 90), Vector3(-90, 0, 90)]},
	{"name": "Asma Köprü Sprinti (O-1)", "type": "sprint", "laps": 1, "prize": 7000,
		"wp": [Vector3(0, 0, 0), Vector3(430, 0, 0), Vector3(1000, 14, 250), Vector3(1520, 0, 300), Vector3(1840, 0, 300)]},
	{"name": "Tünel Sprinti (O-2)", "type": "sprint", "laps": 1, "prize": 7000,
		"wp": [Vector3(0, 0, 90), Vector3(0, 0, -450), Vector3(100, 0, -920), Vector3(-300, 0, -1400), Vector3(-300, 0, -1650)]},
	{"name": "Eski Yol Sprinti", "type": "sprint", "laps": 1, "prize": 6000,
		"wp": [Vector3(-300, 0, -1580), Vector3(-1060, 0, -600), Vector3(-560, 0, 60), Vector3(-180, 0, 0), Vector3(90, 0, 0)]},
	{"name": "Sahil Yolu Sprinti", "type": "sprint", "laps": 1, "prize": 4000,
		"wp": [Vector3(1680, 0, 380), Vector3(1920, 0, 300), Vector3(2290, 0, 300), Vector3(2300, 0, 1500)]},
	{"name": "Ülke Turu (tüm otoyollar)", "type": "circuit", "laps": 1, "prize": 16000,
		"wp": [Vector3(430, 0, 0), Vector3(1000, 14, 250), Vector3(1520, 0, 300), Vector3(1760, 0, 220), Vector3(1760, 0, -150),
			Vector3(1000, 10, -1200), Vector3(-300, 0, -1400), Vector3(100, 0, -920), Vector3(0, 0, -450), Vector3(0, 0, -180), Vector3(180, 0, 0)]},
]
const RACER_CARS := ["evo_ix", "skyline_r34", "bmw_m3", "porsche_911", "amg_gt", "audi_r8", "gallardo"]
const RACER_NAMES := ["Razor", "Bull", "Izzy", "Ronnie", "Kaze", "Webster"]

var main
var active: bool = false
var kind: String = ""              # race / delivery
var race_def: Dictionary = {}
var seq: Array[Vector3] = []
var seq_center: Array[Vector3] = []
var player_index: int = 0
var racers: Array[AICar] = []
var racer_names: Array[String] = []
var countdown: float = 0.0
var race_time: float = 0.0
var finish_order: Array[String] = []
var marker: MeshInstance3D
var marker_mat: StandardMaterial3D
var player_finished: bool = false
var finish_wait: float = 0.0

# teslimat
var delivery_target: Vector3
var delivery_time: float = 0.0
var delivery_reward: int = 0
var delivery_name: String = ""


func _ready() -> void:
	marker = MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = 11.0
	cm.bottom_radius = 11.0
	cm.height = 40.0
	cm.cap_top = false
	cm.cap_bottom = false
	marker.mesh = cm
	marker_mat = StandardMaterial3D.new()
	marker_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	marker_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	marker_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	marker_mat.albedo_color = Color(1.0, 0.75, 0.1, 0.25)
	marker.material_override = marker_mat
	marker.visible = false
	marker.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(marker)


func start_race(index: int) -> void:
	if active:
		cancel()
	race_def = RACES[index]
	kind = "race"
	main.police.clear_all()
	main.police.enabled = false
	var w = main.world
	var wps: Array = race_def["wp"]
	var wn: Array[int] = []
	for p in wps:
		wn.append(w.nearest_node(p))
	if race_def["type"] == "circuit":
		wn.append(wn[0])
	var path: Array[int] = [wn[0]]
	for k in range(1, wn.size()):
		var sub: Array[int] = w.find_path(wn[k - 1], wn[k])
		for q in range(1, sub.size()):
			path.append(sub[q])
	seq.clear()
	seq_center.clear()
	var laps: int = race_def["laps"]
	for l in laps:
		for k in range(1, path.size()):
			var ln := 1 if w.edge_kind(path[k - 1], path[k]) == "hw" else 0
			seq.append(w.lane_point(path[k - 1], path[k], ln))
			seq_center.append(w.nodes[path[k]])
	var pts: Array[Vector3] = [w.nodes[path[0]], w.nodes[path[1]]]
	# grid
	var dir := (pts[1] - pts[0]).normalized()
	var start := pts[0] + dir * 3.0
	var right := dir.cross(Vector3.UP)
	var yaw := atan2(dir.x, dir.z)
	var p: PlayerCar = main.player
	p.place(start + dir * 12.0 + right * 2.5, yaw)
	p.controls_enabled = false
	p.nitro_amount = 1.0
	_clear_racers()
	racer_names.clear()
	var names := RACER_NAMES.duplicate()
	names.shuffle()
	for r in 3:
		var car := AICar.new()
		var pool: Array = RACER_CARS.duplicate()
		pool.append_array(Game.pack_cars)
		var c := Game.get_car_config(pool[(index + r * 3) % pool.size()])
		c["color"] = Color.from_hsv(randf(), 0.8, 0.9)
		car.setup(c, "racer")
		main.add_child(car)
		var offs := [Vector3(-2.5, 0, 12), Vector3(2.5, 0, 4), Vector3(-2.5, 0, 4)]
		var o: Vector3 = offs[r]
		car.place(start + dir * o.z + right * o.x, yaw)
		car.setup_race(seq, randf_range(0.82, 0.95))
		car.mode = "idle"
		car.add_to_group("racers")
		racers.append(car)
		racer_names.append(names[r])
	player_index = 0
	countdown = 3.99
	race_time = 0.0
	finish_order.clear()
	player_finished = false
	finish_wait = 0.0
	active = true
	_update_marker()
	Game.toast("%s başlıyor! Ödül: $%d" % [race_def["name"], race_def["prize"]], Color(1, 0.8, 0.2))


func start_delivery() -> void:
	if active:
		cancel()
	kind = "delivery"
	var pp: Vector3 = main.player.global_position
	var n: int = main.world.random_node_between(pp, 600.0, 2200.0)
	delivery_target = main.world.nodes[n]
	var dist := delivery_target.distance_to(pp)
	delivery_time = dist / 14.0 + 15.0
	delivery_reward = int(dist * 4.0) + 500
	var goods := ["paketi", "yedek parçayı", "gizli zarfı", "pizza siparişini", "turbo kitini"]
	delivery_name = goods[randi() % goods.size()]
	active = true
	marker.visible = true
	marker.global_position = delivery_target + Vector3(0, 20, 0)
	marker_mat.albedo_color = Color(0.2, 1.0, 0.4, 0.25)
	Game.toast("İş: %s %.0f saniyede teslim et! ($%d)" % [delivery_name, delivery_time, delivery_reward], Color(0.3, 1, 0.5))


func cancel() -> void:
	if not active:
		return
	active = false
	marker.visible = false
	_clear_racers()
	main.player.controls_enabled = true
	main.police.enabled = true


func _clear_racers() -> void:
	for r in racers:
		if is_instance_valid(r):
			r.queue_free()
	racers.clear()


func _update_marker() -> void:
	if player_index < seq.size():
		marker.visible = true
		marker.global_position = seq_center[player_index] + Vector3(0, 20, 0)
		var last := player_index == seq.size() - 1
		marker_mat.albedo_color = Color(0.2, 1.0, 0.3, 0.3) if last else Color(1.0, 0.75, 0.1, 0.25)
	else:
		marker.visible = false


func _process(delta: float) -> void:
	if not active:
		return
	if kind == "delivery":
		_process_delivery(delta)
		return
	if countdown > 0.0:
		var before := int(countdown)
		countdown -= delta
		var now := int(countdown)
		if countdown <= 0.0:
			main.player.controls_enabled = true
			for r in racers:
				r.mode = "racer"
			main.hud.big_message("BAŞLA!", Color(0.3, 1, 0.3), 1.0)
		elif now != before or race_time == 0.0:
			main.hud.big_message(str(maxi(now, 1)), Color(1, 0.85, 0.2), 0.9)
		race_time = 0.0001
		return
	race_time += delta
	var p: PlayerCar = main.player
	var cars: Array = main.all_vehicles()
	# oyuncu kontrol noktası
	if not player_finished and player_index < seq.size():
		var hit := -1
		for q in range(player_index, mini(player_index + 4, seq.size())):
			if p.global_position.distance_to(seq_center[q]) < 18.0 or p.global_position.distance_to(seq[q]) < 12.0:
				hit = q
		if hit >= 0:
			player_index = hit + 1
			if player_index >= seq.size():
				player_finished = true
				finish_order.append("SEN")
				_on_player_finish()
			else:
				_update_marker()
	# rakipler
	var ppos := progress_of(player_index, p.global_position)
	for k in racers.size():
		var r: AICar = racers[k]
		if not is_instance_valid(r):
			continue
		r.all_cars = cars
		if r.race_finished:
			continue
		var rh := -1
		for q in range(r.race_index, mini(r.race_index + 3, seq.size())):
			if r.global_position.distance_to(seq[q]) < 12.0:
				rh = q
		if rh >= 0:
			r.race_index = rh + 1
			if r.race_index >= seq.size():
				r.race_finished = true
				finish_order.append(racer_names[k])
				continue
		var rp := progress_of(r.race_index, r.global_position)
		var diff := rp - ppos   # pozitif = rakip önde
		r.rubber = clampf(1.0 - diff / 3000.0, 0.85, 1.18)
		r.set_far(false)
		# yarış yolundan çok uzaklaştıysa geri koy
		if r.global_position.y < -10.0:
			r.place(seq[maxi(r.race_index - 1, 0)], r.global_rotation.y)
	if player_finished:
		finish_wait += delta
		if finish_wait > 4.0:
			cancel()


func progress_of(index: int, pos: Vector3) -> float:
	if index >= seq.size():
		return index * 1000.0 + 999.0
	var d := pos.distance_to(seq[index])
	return index * 1000.0 - d


func player_position() -> int:
	if player_finished:
		return finish_order.find("SEN") + 1
	var pp := progress_of(player_index, main.player.global_position)
	var pos := 1
	for r in racers:
		if is_instance_valid(r) and progress_of(r.race_index, r.global_position) > pp:
			pos += 1
	return pos


func _on_player_finish() -> void:
	var place := finish_order.size()
	var prize: int = race_def["prize"]
	var won := 0
	match place:
		1: won = prize
		2: won = int(prize * 0.4)
		3: won = int(prize * 0.15)
	if place == 1:
		Game.races_won += 1
	if won > 0:
		Game.add_money(won)
	marker.visible = false
	var t := "%d. oldun! +$%d" % [place, won] if won > 0 else "%d. oldun. Ödül yok." % place
	main.hud.big_message("BİTİŞ - %d." % place, Color(1, 0.85, 0.2) if place == 1 else Color.WHITE, 3.5)
	Game.toast(t + "  Süre: %s" % fmt_time(race_time), Color(1, 0.85, 0.2))


func _process_delivery(delta: float) -> void:
	delivery_time -= delta
	var p: PlayerCar = main.player
	if p.global_position.distance_to(delivery_target) < 16.0:
		Game.add_money(delivery_reward)
		Game.toast("Teslimat tamam! +$%d" % delivery_reward, Color(0.3, 1, 0.4))
		main.hud.big_message("TESLİM EDİLDİ", Color(0.3, 1, 0.4), 2.0)
		cancel()
	elif delivery_time <= 0.0:
		Game.toast("Süre doldu, teslimat başarısız.", Color(1, 0.3, 0.3))
		main.hud.big_message("BAŞARISIZ", Color(1, 0.3, 0.3), 2.0)
		cancel()


static func fmt_time(t: float) -> String:
	return "%d:%05.2f" % [int(t) / 60, fmod(t, 60.0)]


func info_text() -> String:
	if not active:
		return ""
	if kind == "delivery":
		var d: float = main.player.global_position.distance_to(delivery_target)
		return "TESLİMAT: %s\nKalan süre: %.1f sn\nMesafe: %d m" % [delivery_name, maxf(delivery_time, 0.0), int(d)]
	var s := "%s\nSıra: %d/4\n" % [race_def["name"], player_position()]
	if race_def["type"] == "circuit":
		var per := seq.size() / int(race_def["laps"])
		s += "Tur: %d/%d\n" % [mini(player_index / per + 1, race_def["laps"]), race_def["laps"]]
	else:
		s += "Kontrol noktası: %d/%d\n" % [player_index, seq.size()]
	s += "Süre: %s" % fmt_time(race_time)
	return s


func current_target() -> Variant:
	if not active:
		return null
	if kind == "delivery":
		return delivery_target
	if player_index < seq.size():
		return seq_center[player_index]
	return null
