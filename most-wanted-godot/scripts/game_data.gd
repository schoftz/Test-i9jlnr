extends Node
## Global oyun verisi: para, araç kataloğu, sahip olunan araçlar, kayıt/yükleme.

signal money_changed(value: int)
signal toast_requested(text: String, color: Color)

const SAVE_PATH := "user://most_wanted_save.json"
const MAX_UPGRADE := 3

## Araç kataloğu. Değerler gerçek araçlara yakın tutuldu (hp -> motor kuvveti, km/s azami hız).
## body: hatch / sedan / coupe / super / muscle  — prosedürel yedek gövdenin silüeti.
## Gerçek model için: res://models/custom/<id>.glb dosyasını koy (README'ye bak).
const CARS := {
	"golf_gti": {"name": "Volkswagen Golf GTI Mk5", "price": 0, "hp": 200, "top": 235.0, "grip": 2.7, "nitro": 3.0, "mass": 1340.0, "drive": "FWD",
		"body": "hatch", "len": 4.22, "wid": 1.76, "hgt": 1.47, "spoiler": "lip", "lights": "slim", "tail": "bar", "color": Color(0.85, 0.1, 0.1)},
	"evo_ix": {"name": "Mitsubishi Lancer Evolution IX", "price": 18000, "hp": 291, "top": 250.0, "grip": 3.0, "nitro": 3.5, "mass": 1410.0, "drive": "AWD",
		"body": "sedan", "len": 4.49, "wid": 1.77, "hgt": 1.45, "spoiler": "wing", "lights": "slim", "tail": "round", "color": Color(0.85, 0.85, 0.88)},
	"skyline_r34": {"name": "Nissan Skyline GT-R R34", "price": 32000, "hp": 330, "top": 265.0, "grip": 3.1, "nitro": 4.0, "mass": 1560.0, "drive": "AWD",
		"body": "coupe", "len": 4.6, "wid": 1.79, "hgt": 1.36, "spoiler": "wing", "lights": "slim", "tail": "round4", "color": Color(0.1, 0.25, 0.75)},
	"bmw_m3": {"name": "BMW M3 GTR (E46)", "price": 45000, "hp": 380, "top": 280.0, "grip": 3.2, "nitro": 4.5, "mass": 1350.0, "drive": "RWD",
		"body": "coupe", "len": 4.49, "wid": 1.78, "hgt": 1.37, "spoiler": "wing", "lights": "quad", "tail": "bar", "color": Color(0.6, 0.65, 0.75)},
	"porsche_911": {"name": "Porsche 911 GT3 (997)", "price": 70000, "hp": 415, "top": 310.0, "grip": 3.4, "nitro": 4.5, "mass": 1395.0, "drive": "RWD",
		"body": "911", "len": 4.45, "wid": 1.81, "hgt": 1.28, "spoiler": "wing", "lights": "round", "tail": "slim", "color": Color(0.95, 0.85, 0.1)},
	"amg_gt": {"name": "Mercedes-AMG GT", "price": 85000, "hp": 476, "top": 304.0, "grip": 3.4, "nitro": 5.0, "mass": 1615.0, "drive": "RWD",
		"body": "longhood", "len": 4.55, "wid": 1.94, "hgt": 1.29, "spoiler": "lip", "lights": "slim", "tail": "slim", "color": Color(0.15, 0.15, 0.16)},
	"audi_r8": {"name": "Audi R8 V10", "price": 110000, "hp": 525, "top": 316.0, "grip": 3.6, "nitro": 5.0, "mass": 1625.0, "drive": "AWD",
		"body": "super", "len": 4.43, "wid": 1.93, "hgt": 1.25, "spoiler": "lip", "lights": "slim", "tail": "slim", "color": Color(0.9, 0.9, 0.92)},
	"gallardo": {"name": "Lamborghini Gallardo", "price": 140000, "hp": 520, "top": 315.0, "grip": 3.7, "nitro": 6.0, "mass": 1520.0, "drive": "AWD",
		"body": "wedge", "len": 4.3, "wid": 1.9, "hgt": 1.17, "spoiler": "lip", "lights": "slim", "tail": "slim", "color": Color(0.95, 0.6, 0.05)},
}

## Garajda görünmeyen araçlar
const POLICE_CAR := {"name": "Polis Ford Crown Victoria", "hp": 330, "top": 250.0, "grip": 3.0, "nitro": 3.0, "mass": 1750.0, "drive": "RWD",
	"body": "sedan", "len": 5.38, "wid": 1.99, "hgt": 1.45, "spoiler": "none", "lights": "slim", "tail": "bar", "color": Color(0.05, 0.05, 0.07), "id": "police_cvpi"}
const TRAFFIC_BODIES := [
	{"body": "sedan", "len": 4.7, "wid": 1.82, "hgt": 1.47, "lights": "slim", "tail": "bar", "spoiler": "none"},
	{"body": "hatch", "len": 4.0, "wid": 1.72, "hgt": 1.5, "lights": "slim", "tail": "bar", "spoiler": "none"},
	{"body": "van", "len": 4.9, "wid": 1.9, "hgt": 1.9, "lights": "slim", "tail": "bar", "spoiler": "none"},
	{"body": "suv", "len": 4.6, "wid": 1.86, "hgt": 1.7, "lights": "slim", "tail": "bar", "spoiler": "none"},
]

const CUSTOM_DIR := "res://models/custom/"
var custom_cars: Dictionary = {}   # "custom_<dosya>" -> config (models/custom içindeki tanınmayan glb'ler)

const PAINTS := [
	Color(0.85, 0.85, 0.9), Color(0.08, 0.08, 0.09), Color(0.85, 0.1, 0.1), Color(0.1, 0.35, 0.85),
	Color(0.95, 0.85, 0.1), Color(0.9, 0.55, 0.05), Color(0.1, 0.7, 0.3), Color(0.55, 0.6, 0.7),
	Color(0.5, 0.1, 0.7), Color(0.1, 0.8, 0.85),
]

var money: int = 8000
var owned: Dictionary = {}   # car_id -> {"color": [r,g,b], "eng": 0, "nitro": 0, "hand": 0}
var current: String = "golf_gti"
var day_cycle: bool = true
var quality: int = 1          # 0 Düşük, 1 Orta, 2 Yüksek
var pack_cars: Array = []      # paketlerden gelen araç id listesi (trafikte de kullanılır)
var races_won: int = 0
var total_bounty: int = 0
var escapes: int = 0
var busted: int = 0


func _ready() -> void:
	_ensure_inputs()
	_scan_custom()
	load_game()


func _ensure_inputs() -> void:
	# Yedek: project.godot içindeki giriş haritası bozulursa çalışma zamanında oluştur.
	var map := {
		"accelerate": [KEY_W, KEY_UP], "brake": [KEY_S, KEY_DOWN],
		"steer_left": [KEY_A, KEY_LEFT], "steer_right": [KEY_D, KEY_RIGHT],
		"handbrake": [KEY_SPACE], "nitro": [KEY_SHIFT], "camera": [KEY_C],
		"garage": [KEY_E], "menu_jobs": [KEY_J], "map": [KEY_M, KEY_TAB], "fps": [KEY_F], "pause": [KEY_ESCAPE], "reset_car": [KEY_R],
	}
	for action in map:
		if not InputMap.has_action(action):
			InputMap.add_action(action)
		if InputMap.action_get_events(action).is_empty():
			for k in map[action]:
				var ev := InputEventKey.new()
				ev.physical_keycode = k
				InputMap.action_add_event(action, ev)


func add_money(amount: int) -> void:
	money = max(0, money + amount)
	money_changed.emit(money)
	save_game()


func toast(text: String, color: Color = Color.WHITE) -> void:
	toast_requested.emit(text, color)


func get_car_config(id: String) -> Dictionary:
	var base: Dictionary = (CARS[id] if CARS.has(id) else custom_cars[id]).duplicate()
	var up: Dictionary = owned.get(id, {"eng": 0, "nitro": 0, "hand": 0})
	var eng: int = up.get("eng", 0)
	var nit: int = up.get("nitro", 0)
	var hand: int = up.get("hand", 0)
	base["id"] = id
	base["power"] = float(base["hp"]) * 24.0 * (1.0 + 0.12 * eng)
	base["top"] = base["top"] * (1.0 + 0.05 * eng)
	base["nitro"] = base["nitro"] * (1.0 + 0.3 * nit)
	base["grip"] = base["grip"] * (1.0 + 0.08 * hand)
	base["model"] = model_path_for(id)
	if up.has("color"):
		var c: Array = up["color"]
		base["color"] = Color(c[0], c[1], c[2])
	return base


func all_car_ids() -> Array:
	return CARS.keys() + custom_cars.keys()


func car_info(id: String) -> Dictionary:
	return CARS[id] if CARS.has(id) else custom_cars[id]


## models/custom/<id>.glb (veya .gltf) varsa yolunu döndürür.
func model_path_for(id: String) -> String:
	if custom_cars.has(id):
		return custom_cars[id].get("model_file", "")
	for ext in ["glb", "gltf"]:
		for suffix in ["", "_ters"]:
			var p: String = CUSTOM_DIR + id + suffix + "." + ext
			if FileAccess.file_exists(p) or ResourceLoader.exists(p):
				return p
	return ""


func _scan_custom() -> void:
	custom_cars.clear()
	_scan_packs()
	var d := DirAccess.open(CUSTOM_DIR)
	if d == null:
		return
	for f in d.get_files():
		var ext := f.get_extension().to_lower()
		if ext != "glb" and ext != "gltf":
			continue
		var base_name := f.get_basename()
		var plain := base_name.trim_suffix("_ters")
		if CARS.has(plain):
			continue
		var id := "custom_" + plain
		custom_cars[id] = {"name": "Özel: " + plain.capitalize(), "price": 15000, "hp": 400, "top": 290.0, "grip": 3.3,
			"nitro": 4.5, "mass": 1450.0, "drive": "AWD", "body": "coupe", "len": 4.5, "wid": 1.9, "hgt": 1.3,
			"spoiler": "none", "lights": "slim", "tail": "slim", "color": Color(0.8, 0.8, 0.8), "model_file": CUSTOM_DIR + f}


const PACK_DIR := "res://models/packs/"


## models/packs/*.glb: tek dosyada çok araç. Her üst düzey çocuk düğüm ayrı araç olur.
func _scan_packs() -> void:
	pack_cars.clear()
	var d := DirAccess.open(PACK_DIR)
	if d == null:
		return
	for f in d.get_files():
		var ext := f.get_extension().to_lower()
		if ext != "glb" and ext != "gltf":
			continue
		var path := PACK_DIR + f
		var names: Array = CarVisual.pack_car_names(path)
		var i := 0
		for nm in names:
			var id := "pack_%s_%d" % [f.get_basename().to_lower().replace(" ", "_"), i]
			var price := 12000 + (i % 8) * 6000
			custom_cars[id] = {"name": str(nm).replace("_", " ").strip_edges(), "price": price, "hp": 260 + (i % 8) * 30,
				"top": 240.0 + (i % 8) * 8.0, "grip": 3.0 + (i % 8) * 0.06, "nitro": 4.0, "mass": 1350.0, "drive": "AWD" if i % 3 == 0 else "RWD",
				"body": "coupe", "len": 4.5, "wid": 1.8, "hgt": 1.35, "spoiler": "none", "lights": "slim", "tail": "slim",
				"color": Color(0.8, 0.8, 0.8), "pack_file": path, "pack_index": i}
			pack_cars.append(id)
			i += 1


func upgrade_cost(id: String, kind: String) -> int:
	var lvl: int = owned.get(id, {}).get(kind, 0)
	return int(2000 + car_info(id)["price"] * 0.08) * (lvl + 1)


func buy_car(id: String) -> bool:
	if owned.has(id):
		return false
	var price: int = car_info(id)["price"]
	if money < price:
		return false
	money -= price
	var c: Color = car_info(id)["color"]
	owned[id] = {"color": [c.r, c.g, c.b], "eng": 0, "nitro": 0, "hand": 0}
	money_changed.emit(money)
	save_game()
	return true


func sell_car(id: String) -> bool:
	if not owned.has(id) or owned.size() <= 1:
		return false
	var refund := int(car_info(id)["price"] * 0.6)
	owned.erase(id)
	money += refund
	if current == id:
		current = owned.keys()[0]
	money_changed.emit(money)
	save_game()
	return true


func upgrade(id: String, kind: String) -> bool:
	if not owned.has(id):
		return false
	var lvl: int = owned[id].get(kind, 0)
	if lvl >= MAX_UPGRADE:
		return false
	var cost := upgrade_cost(id, kind)
	if money < cost:
		return false
	money -= cost
	owned[id][kind] = lvl + 1
	money_changed.emit(money)
	save_game()
	return true


func set_paint(id: String, c: Color) -> void:
	if owned.has(id):
		owned[id]["color"] = [c.r, c.g, c.b]
		save_game()


func save_game() -> void:
	var data := {
		"money": money, "owned": owned, "current": current, "races_won": races_won,
		"total_bounty": total_bounty, "escapes": escapes, "busted": busted, "day_cycle": day_cycle, "quality": quality,
	}
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(data, "\t"))


func load_game() -> void:
	owned = {}
	if FileAccess.file_exists(SAVE_PATH):
		var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
		if f:
			var parsed = JSON.parse_string(f.get_as_text())
			if parsed is Dictionary:
				money = int(parsed.get("money", money))
				var o = parsed.get("owned", {})
				if o is Dictionary:
					for k in o:
						if (CARS.has(k) or custom_cars.has(k)) and o[k] is Dictionary:
							var e: Dictionary = o[k]
							owned[k] = {"color": e.get("color", [1, 1, 1]), "eng": int(e.get("eng", 0)),
								"nitro": int(e.get("nitro", 0)), "hand": int(e.get("hand", 0))}
				current = str(parsed.get("current", current))
				races_won = int(parsed.get("races_won", 0))
				total_bounty = int(parsed.get("total_bounty", 0))
				escapes = int(parsed.get("escapes", 0))
				busted = int(parsed.get("busted", 0))
				day_cycle = bool(parsed.get("day_cycle", true))
				quality = int(parsed.get("quality", 1))
	if owned.is_empty():
		var c: Color = CARS["golf_gti"]["color"]
		owned["golf_gti"] = {"color": [c.r, c.g, c.b], "eng": 0, "nitro": 0, "hand": 0}
	if not owned.has(current):
		current = owned.keys()[0]
