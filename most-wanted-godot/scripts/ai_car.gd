class_name AICar
extends CarBase
## Yapay zeka sürücüsü: trafik, polis (devriye / takip / barikat) ve yarışçı modları.

var city: City
var mode: String = "traffic"   # traffic, patrol, chase, roadblock, racer, idle
var prev_node: int = -1
var cur_node: int = 0          # hedef düğüm
var target_speed: float = 40.0 # km/h
var target: Node3D             # takip edilen araç
var all_cars: Array = []       # yakın araç listesi (main tarafından güncellenir)
var stuck_time: float = 0.0
var reverse_time: float = 0.0

# yarışçı
var race_points: Array[Vector3] = []
var race_index: int = 0
var race_lap: int = 0
var race_finished: bool = false
var skill: float = 1.0
var rubber: float = 1.0


func init_on_graph(c: City, from_n: int, to_n: int) -> void:
	city = c
	prev_node = from_n
	cur_node = to_n


func _physics_process(delta: float) -> void:
	match mode:
		"traffic", "patrol":
			_drive_traffic(delta)
		"chase":
			_drive_chase(delta)
		"roadblock", "idle":
			throttle = 0.0
			steer_in = 0.0
			brake_in = 1.0
		"racer":
			_drive_racer(delta)
	_unstick(delta)
	drive(delta)


func _unstick(delta: float) -> void:
	if mode == "roadblock" or mode == "idle":
		return
	if reverse_time > 0.0:
		reverse_time -= delta
		throttle = -1.0
		steer_in = -steer_in
		brake_in = 0.0
		return
	if throttle > 0.3 and speed_kmh < 3.0:
		stuck_time += delta
		if stuck_time > 2.0:
			stuck_time = 0.0
			reverse_time = 1.2
			if global_basis.y.y < 0.4:
				reset_upright()
	else:
		stuck_time = 0.0


func _car_ahead_dist() -> float:
	var fwd := global_basis.z
	var best := INF
	for c in all_cars:
		if c == self or not is_instance_valid(c):
			continue
		var d: Vector3 = c.global_position - global_position
		var along := d.dot(fwd)
		if along > 0.0 and along < 22.0 and absf(d.dot(global_basis.x)) < 2.6:
			best = minf(best, along)
	return best


func _drive_traffic(delta: float) -> void:
	if city == null:
		return
	var tp := city.lane_point(prev_node, cur_node)
	var flat := Vector3(global_position.x, 0, global_position.z)
	if flat.distance_to(tp) < 9.0:
		var nxt := city.pick_next(prev_node, cur_node)
		prev_node = cur_node
		cur_node = nxt
		tp = city.lane_point(prev_node, cur_node)
	steer_in = steer_toward(tp)
	var desired := target_speed
	# kavşağa yaklaşırken yavaşla
	var dist_node := flat.distance_to(city.nodes[cur_node])
	if dist_node < 25.0:
		desired = minf(desired, 28.0)
	var ahead := _car_ahead_dist()
	if ahead < 22.0:
		desired = minf(desired, (ahead - 7.0) * 3.0)
	_speed_control(desired)


func _speed_control(desired_kmh: float) -> void:
	if desired_kmh <= 1.0:
		throttle = 0.0
		brake_in = 1.0
		return
	var diff := desired_kmh - speed_kmh
	if diff > 0:
		throttle = clampf(diff / 15.0, 0.2, 1.0)
		brake_in = 0.0
	else:
		throttle = 0.0
		brake_in = clampf(-diff / 20.0, 0.0, 1.0)


func _drive_chase(_delta: float) -> void:
	if target == null or not is_instance_valid(target):
		mode = "patrol"
		return
	var tv: Vector3 = (target as RigidBody3D).linear_velocity
	var to_t: Vector3 = target.global_position - global_position
	var dist := to_t.length()
	# hedefin önüne hesaplı kesme (kutulama)
	var lead := clampf(dist / 40.0, 0.0, 1.2)
	var aim: Vector3 = target.global_position + tv * lead
	steer_in = steer_toward(aim)
	var local := global_transform.affine_inverse() * aim
	if local.z < -3.0 and dist < 25.0:
		# hedef arkada kaldı: geri vites ile dön
		throttle = -1.0
		steer_in = -steer_in
		brake_in = 0.0
		nitro_on = false
		return
	var tspeed := tv.length() * 3.6
	var desired := tspeed + 40.0 if dist > 15.0 else tspeed + 15.0
	desired = maxf(desired, 60.0)
	_speed_control(desired)
	# keskin dönüşte biraz yavaşla
	if absf(steer_in) > 0.8 and speed_kmh > 90.0:
		throttle = 0.3
	nitro_on = dist > 60.0 and absf(steer_in) < 0.3
	handbrake = absf(steer_in) > 0.95 and speed_kmh > 60.0


func setup_race(points: Array[Vector3], sk: float) -> void:
	race_points = points
	race_index = 0
	race_lap = 0
	race_finished = false
	skill = sk
	mode = "racer"


func _drive_racer(_delta: float) -> void:
	if race_finished or race_points.is_empty():
		throttle = 0.0
		brake_in = 1.0
		return
	var tp: Vector3 = race_points[race_index % race_points.size()]
	steer_in = steer_toward(tp)
	var nxt: Vector3 = race_points[(race_index + 1) % race_points.size()]
	var flat := Vector3(global_position.x, 0, global_position.z)
	var d := flat.distance_to(tp)
	# sonraki köşenin açısına göre fren
	var in_dir := (tp - flat).normalized()
	var out_dir := (nxt - tp).normalized()
	var corner := 1.0 - clampf(in_dir.dot(out_dir), 0.0, 1.0)
	var top: float = cfg.get("top", 220.0)
	var desired: float = top * skill * rubber
	if d < 60.0 and corner > 0.3:
		desired = minf(desired, lerpf(desired, 75.0, corner))
	var ahead := _car_ahead_dist()
	if ahead < 10.0:
		steer_in = clampf(steer_in + 0.4, -1.0, 1.0)
	_speed_control(desired)
	nitro_on = rubber > 1.05 and absf(steer_in) < 0.2
