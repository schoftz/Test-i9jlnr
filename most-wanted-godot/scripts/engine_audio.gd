class_name EngineAudio
extends Node
## AudioStreamGenerator ile prosedürel motor sesi + polis sireni (dosya gerektirmez).

const RATE := 22050.0

var player: AudioStreamPlayer
var playback: AudioStreamGeneratorPlayback
var rpm: float = 900.0
var throttle: float = 0.0
var siren_vol: float = 0.0
var nitro: bool = false
var _phase: float = 0.0
var _phase2: float = 0.0
var _siren_phase: float = 0.0
var _siren_t: float = 0.0
var _gear_speed: float = 0.0
var _noise: float = 0.0


func _ready() -> void:
	var gen := AudioStreamGenerator.new()
	gen.mix_rate = RATE
	gen.buffer_length = 0.1
	player = AudioStreamPlayer.new()
	player.stream = gen
	player.volume_db = -8.0
	add_child(player)
	player.play()
	playback = player.get_stream_playback() as AudioStreamGeneratorPlayback


func update_from(car: CarBase, siren_dist: float) -> void:
	if car == null or not is_instance_valid(car):
		return
	# basit vites simülasyonu
	var sp := absf(car.forward_speed) * 3.6
	var gears := [0.0, 45.0, 85.0, 130.0, 180.0, 240.0, 400.0]
	var g := 1
	while g < gears.size() - 1 and sp > gears[g]:
		g += 1
	var lo: float = gears[g - 1]
	var hi: float = gears[g]
	var frac := clampf((sp - lo) / maxf(hi - lo, 1.0), 0.0, 1.0)
	var target := 1000.0 + frac * 6000.0 + (0.0 if g == 1 else 1200.0 * (1.0 - frac) * 0.3)
	if car.throttle > 0.1 and sp < 5.0:
		target = 3500.0
	rpm = lerpf(rpm, target, 0.15)
	throttle = lerpf(throttle, maxf(car.throttle, 0.0), 0.2)
	nitro = car.nitro_on
	siren_vol = clampf(1.0 - siren_dist / 120.0, 0.0, 1.0) if siren_dist < INF else 0.0


func _process(delta: float) -> void:
	if playback == null:
		return
	var frames := playback.get_frames_available()
	var base := rpm / 60.0 * 2.0      # 4 silindir: devir/60*2 ateşleme
	var inc := base / RATE
	var amp := 0.18 + throttle * 0.17
	for f in frames:
		_phase = fmod(_phase + inc, 1.0)
		_phase2 = fmod(_phase2 + inc * 0.5, 1.0)
		var saw := _phase * 2.0 - 1.0
		var pulse := 1.0 if _phase2 < 0.3 else -0.6
		_noise = lerpf(_noise, randf_range(-1.0, 1.0), 0.3)
		var s := (saw * 0.5 + pulse * 0.35 + _noise * (0.1 + throttle * 0.15)) * amp
		if nitro:
			s += _noise * 0.12
		if siren_vol > 0.0:
			_siren_t += 1.0 / RATE
			var sf := 750.0 + 450.0 * (0.5 + 0.5 * sin(_siren_t * TAU * 0.6))
			_siren_phase = fmod(_siren_phase + sf / RATE, 1.0)
			s += (1.0 if _siren_phase < 0.5 else -1.0) * 0.08 * siren_vol
		s = clampf(s, -1.0, 1.0)
		playback.push_frame(Vector2(s, s))
