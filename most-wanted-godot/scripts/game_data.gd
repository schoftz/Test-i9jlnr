extends Node
## Global oyun verisi: para, araç kataloğu, sahip olunan araçlar, kayıt/yükleme.

signal money_changed(value: int)
signal toast_requested(text: String, color: Color)

const SAVE_PATH := "user://most_wanted_save.json"
const MAX_UPGRADE := 3

## style: 0 = hatchback, 1 = sedan/muscle, 2 = süper spor
const CARS := {
	"kompakt": {"name": "Kompakt GT", "price": 0, "power": 5200.0, "top": 190.0, "grip": 2.7, "nitro": 3.0, "mass": 1150.0, "style": 0, "color": Color(0.85, 0.85, 0.9)},
	"sokak": {"name": "Sokak R32", "price": 12000, "power": 6400.0, "top": 215.0, "grip": 2.9, "nitro": 3.5, "mass": 1250.0, "style": 0, "color": Color(0.1, 0.35, 0.85)},
	"kas": {"name": "Kas Mustang V8", "price": 22000, "power": 8200.0, "top": 235.0, "grip": 2.6, "nitro": 4.0, "mass": 1450.0, "style": 1, "color": Color(0.9, 0.55, 0.05)},
	"turbo": {"name": "Turbo Supra", "price": 35000, "power": 8600.0, "top": 250.0, "grip": 3.0, "nitro": 4.5, "mass": 1350.0, "style": 1, "color": Color(0.85, 0.1, 0.1)},
	"gt": {"name": "BMW M3 GTR Tarzı", "price": 55000, "power": 9800.0, "top": 270.0, "grip": 3.2, "nitro": 5.0, "mass": 1300.0, "style": 2, "color": Color(0.55, 0.6, 0.7)},
	"egzotik": {"name": "Egzotik Lambo", "price": 85000, "power": 11500.0, "top": 300.0, "grip": 3.4, "nitro": 5.0, "mass": 1400.0, "style": 2, "color": Color(0.95, 0.85, 0.1)},
	"efsane": {"name": "Efsane Carrera GT", "price": 120000, "power": 12800.0, "top": 320.0, "grip": 3.6, "nitro": 6.0, "mass": 1300.0, "style": 2, "color": Color(0.15, 0.15, 0.17)},
}

const PAINTS := [
	Color(0.85, 0.85, 0.9), Color(0.08, 0.08, 0.09), Color(0.85, 0.1, 0.1), Color(0.1, 0.35, 0.85),
	Color(0.95, 0.85, 0.1), Color(0.9, 0.55, 0.05), Color(0.1, 0.7, 0.3), Color(0.55, 0.6, 0.7),
	Color(0.5, 0.1, 0.7), Color(0.1, 0.8, 0.85),
]

var money: int = 8000
var owned: Dictionary = {}   # car_id -> {"color": [r,g,b], "eng": 0, "nitro": 0, "hand": 0}
var current: String = "kompakt"
var races_won: int = 0
var total_bounty: int = 0
var escapes: int = 0
var busted: int = 0


func _ready() -> void:
	_ensure_inputs()
	load_game()


func _ensure_inputs() -> void:
	# Yedek: project.godot içindeki giriş haritası bozulursa çalışma zamanında oluştur.
	var map := {
		"accelerate": [KEY_W, KEY_UP], "brake": [KEY_S, KEY_DOWN],
		"steer_left": [KEY_A, KEY_LEFT], "steer_right": [KEY_D, KEY_RIGHT],
		"handbrake": [KEY_SPACE], "nitro": [KEY_SHIFT], "camera": [KEY_C],
		"garage": [KEY_E], "menu_jobs": [KEY_J], "pause": [KEY_ESCAPE], "reset_car": [KEY_R],
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
	var base: Dictionary = CARS[id].duplicate()
	var up: Dictionary = owned.get(id, {"eng": 0, "nitro": 0, "hand": 0})
	var eng: int = up.get("eng", 0)
	var nit: int = up.get("nitro", 0)
	var hand: int = up.get("hand", 0)
	base["id"] = id
	base["power"] = base["power"] * (1.0 + 0.12 * eng)
	base["top"] = base["top"] * (1.0 + 0.05 * eng)
	base["nitro"] = base["nitro"] * (1.0 + 0.3 * nit)
	base["grip"] = base["grip"] * (1.0 + 0.08 * hand)
	if up.has("color"):
		var c: Array = up["color"]
		base["color"] = Color(c[0], c[1], c[2])
	return base


func upgrade_cost(id: String, kind: String) -> int:
	var lvl: int = owned.get(id, {}).get(kind, 0)
	return int(2000 + CARS[id]["price"] * 0.08) * (lvl + 1)


func buy_car(id: String) -> bool:
	if owned.has(id):
		return false
	var price: int = CARS[id]["price"]
	if money < price:
		return false
	money -= price
	var c: Color = CARS[id]["color"]
	owned[id] = {"color": [c.r, c.g, c.b], "eng": 0, "nitro": 0, "hand": 0}
	money_changed.emit(money)
	save_game()
	return true


func sell_car(id: String) -> bool:
	if not owned.has(id) or owned.size() <= 1:
		return false
	var refund := int(CARS[id]["price"] * 0.6)
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
		"total_bounty": total_bounty, "escapes": escapes, "busted": busted,
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
						if CARS.has(k) and o[k] is Dictionary:
							var e: Dictionary = o[k]
							owned[k] = {"color": e.get("color", [1, 1, 1]), "eng": int(e.get("eng", 0)),
								"nitro": int(e.get("nitro", 0)), "hand": int(e.get("hand", 0))}
				current = str(parsed.get("current", current))
				races_won = int(parsed.get("races_won", 0))
				total_bounty = int(parsed.get("total_bounty", 0))
				escapes = int(parsed.get("escapes", 0))
				busted = int(parsed.get("busted", 0))
	if owned.is_empty():
		var c: Color = CARS["kompakt"]["color"]
		owned["kompakt"] = {"color": [c.r, c.g, c.b], "eng": 0, "nitro": 0, "hand": 0}
	if not owned.has(current):
		current = owned.keys()[0]
