class_name HUD
extends CanvasLayer
## Arayüz: hız göstergesi, nitro, para, aranma yıldızları, mini harita, bildirimler, menüler.

var main
var speed_label: Label
var unit_label: Label
var nitro_bar: ProgressBar
var money_label: Label
var stars: StarRow
var pursuit_label: Label
var pursuit_bar: ProgressBar
var race_label: Label
var toast_box: VBoxContainer
var big_label: Label
var big_timer: float = 0.0
var minimap: Minimap
var help_label: Label
var garage_hint: Label
var menu_root: Control
var menu_kind: String = ""
var garage_view: String = ""


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	layer = 5
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(root)

	# hız (sağ alt)
	speed_label = _label(root, "0", 64, Color.WHITE)
	speed_label.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
	speed_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	speed_label.offset_left = -260; speed_label.offset_top = -150; speed_label.offset_right = -90; speed_label.offset_bottom = -70
	unit_label = _label(root, "km/s", 22, Color(1, 0.8, 0.3))
	unit_label.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
	unit_label.offset_left = -85; unit_label.offset_top = -110; unit_label.offset_right = -20; unit_label.offset_bottom = -80
	nitro_bar = ProgressBar.new()
	nitro_bar.show_percentage = false
	nitro_bar.max_value = 1.0
	nitro_bar.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
	nitro_bar.offset_left = -300; nitro_bar.offset_top = -60; nitro_bar.offset_right = -30; nitro_bar.offset_bottom = -40
	_style_bar(nitro_bar, Color(0.2, 0.6, 1.0))
	root.add_child(nitro_bar)
	var nl := _label(root, "NİTRO", 16, Color(0.5, 0.8, 1))
	nl.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
	nl.offset_left = -300; nl.offset_top = -82; nl.offset_right = -200; nl.offset_bottom = -62

	# para (sağ üst)
	money_label = _label(root, "", 30, Color(0.4, 1, 0.5))
	money_label.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
	money_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	money_label.offset_left = -400; money_label.offset_top = 20; money_label.offset_right = -30; money_label.offset_bottom = 60

	# yıldızlar (üst orta)
	stars = StarRow.new()
	stars.set_anchors_and_offsets_preset(Control.PRESET_CENTER_TOP)
	stars.offset_left = -130; stars.offset_right = 130; stars.offset_top = 14; stars.offset_bottom = 60
	root.add_child(stars)
	pursuit_bar = ProgressBar.new()
	pursuit_bar.show_percentage = false
	pursuit_bar.max_value = 1.0
	pursuit_bar.set_anchors_and_offsets_preset(Control.PRESET_CENTER_TOP)
	pursuit_bar.offset_left = -160; pursuit_bar.offset_right = 160; pursuit_bar.offset_top = 66; pursuit_bar.offset_bottom = 80
	_style_bar(pursuit_bar, Color(1, 0.2, 0.2))
	root.add_child(pursuit_bar)
	pursuit_label = _label(root, "", 20, Color.WHITE)
	pursuit_label.set_anchors_and_offsets_preset(Control.PRESET_CENTER_TOP)
	pursuit_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	pursuit_label.offset_left = -300; pursuit_label.offset_right = 300; pursuit_label.offset_top = 84; pursuit_label.offset_bottom = 110

	# yarış bilgisi (sol üst)
	race_label = _label(root, "", 22, Color(1, 0.9, 0.5))
	race_label.position = Vector2(24, 20)
	race_label.size = Vector2(500, 160)

	# bildirimler (sağ)
	toast_box = VBoxContainer.new()
	toast_box.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
	toast_box.offset_left = -560; toast_box.offset_top = 80; toast_box.offset_right = -30; toast_box.offset_bottom = 400
	toast_box.alignment = BoxContainer.ALIGNMENT_BEGIN
	toast_box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(toast_box)

	# büyük mesaj
	big_label = _label(root, "", 84, Color.WHITE)
	big_label.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	big_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	big_label.offset_left = -500; big_label.offset_right = 500; big_label.offset_top = -160; big_label.offset_bottom = -50

	# mini harita (sol alt)
	minimap = Minimap.new()
	minimap.hud = self
	minimap.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
	minimap.offset_left = 20; minimap.offset_top = -280; minimap.offset_right = 280; minimap.offset_bottom = -20
	root.add_child(minimap)

	help_label = _label(root, "WASD/Oklar: Sür   Boşluk: El freni   Shift: Nitro   C: Kamera   E: Garaj   J: Yarış/İş   R: Düzelt   Esc: Duraklat", 14, Color(1, 1, 1, 0.6))
	help_label.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	help_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	help_label.offset_top = -24; help_label.offset_bottom = -4
	garage_hint = _label(root, "[E] Garaja gir", 28, Color(1, 0.8, 0.2))
	garage_hint.set_anchors_and_offsets_preset(Control.PRESET_CENTER_BOTTOM)
	garage_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	garage_hint.offset_left = -200; garage_hint.offset_right = 200; garage_hint.offset_top = -140; garage_hint.offset_bottom = -100

	menu_root = Control.new()
	menu_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	menu_root.visible = false
	add_child(menu_root)

	Game.toast_requested.connect(show_toast)


func _label(parent: Control, text: String, size: int, color: Color) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	l.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.85))
	l.add_theme_constant_override("outline_size", maxi(4, size / 8))
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(l)
	return l


func _style_bar(bar: ProgressBar, c: Color) -> void:
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0, 0, 0, 0.55)
	bg.set_corner_radius_all(4)
	var fg := StyleBoxFlat.new()
	fg.bg_color = c
	fg.set_corner_radius_all(4)
	bar.add_theme_stylebox_override("background", bg)
	bar.add_theme_stylebox_override("fill", fg)


func _process(delta: float) -> void:
	if main == null or main.player == null or not is_instance_valid(main.player):
		return
	var p: PlayerCar = main.player
	speed_label.text = str(int(p.speed_kmh))
	nitro_bar.value = p.nitro_amount
	money_label.text = "$ %s" % _fmt_money(Game.money)
	var pol: PoliceManager = main.police
	stars.level = pol.heat_level()
	stars.flash = pol.state == "pursuit"
	stars.queue_redraw()
	match pol.state:
		"pursuit":
			pursuit_bar.visible = true
			_style_bar(pursuit_bar, Color(1, 0.2, 0.2))
			pursuit_bar.value = pol.pursuit_bar()
			pursuit_label.text = "TAKİP  %s   Birim: %d" % [RaceManager.fmt_time(pol.pursuit_time), pol.units.size()]
		"cooldown":
			pursuit_bar.visible = true
			_style_bar(pursuit_bar, Color(0.2, 0.6, 1))
			pursuit_bar.value = pol.pursuit_bar()
			pursuit_label.text = "SOĞUMA — saklan!"
		_:
			pursuit_bar.visible = false
			pursuit_label.text = ""
	race_label.text = main.races.info_text()
	garage_hint.visible = p.global_position.distance_to(main.garage_pos) < 16.0 and menu_kind == ""
	if big_timer > 0.0:
		big_timer -= delta
		if big_timer <= 0.0:
			big_label.text = ""
	minimap.queue_redraw()


static func _fmt_money(v: int) -> String:
	var s := str(v)
	var out := ""
	var c := 0
	for i in range(s.length() - 1, -1, -1):
		out = s[i] + out
		c += 1
		if c % 3 == 0 and i > 0:
			out = "." + out
	return out


func show_toast(text: String, color: Color) -> void:
	var panel := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0, 0, 0, 0.6)
	sb.border_color = color
	sb.border_width_left = 4
	sb.content_margin_left = 12; sb.content_margin_right = 12; sb.content_margin_top = 6; sb.content_margin_bottom = 6
	panel.add_theme_stylebox_override("panel", sb)
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", 18)
	l.add_theme_color_override("font_color", color)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	panel.add_child(l)
	toast_box.add_child(panel)
	while toast_box.get_child_count() > 5:
		toast_box.get_child(0).free()
	var tw := panel.create_tween()
	tw.tween_interval(4.0)
	tw.tween_property(panel, "modulate:a", 0.0, 0.8)
	tw.tween_callback(panel.queue_free)


func big_message(text: String, color: Color, duration: float = 2.5) -> void:
	big_label.text = text
	big_label.add_theme_color_override("font_color", color)
	big_timer = duration


# ------------------------------------------------------------------ menüler

func _open_menu(kind: String) -> VBoxContainer:
	_clear_menu()
	menu_kind = kind
	menu_root.visible = true
	get_tree().paused = true
	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, 0.6)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	menu_root.add_child(dim)
	var panel := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0.06, 0.06, 0.08, 0.95)
	sb.border_color = Color(1, 0.7, 0.1)
	sb.set_border_width_all(2)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(20)
	panel.add_theme_stylebox_override("panel", sb)
	panel.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	panel.custom_minimum_size = Vector2(900 if kind == "garage" else 560, 0)
	panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	panel.grow_vertical = Control.GROW_DIRECTION_BOTH
	menu_root.add_child(panel)
	var vb := VBoxContainer.new()
	vb.add_theme_constant_override("separation", 8)
	panel.add_child(vb)
	return vb


func _clear_menu() -> void:
	for c in menu_root.get_children():
		c.queue_free()


func close_menu() -> void:
	_clear_menu()
	menu_root.visible = false
	menu_kind = ""
	get_tree().paused = false


func _title(vb: Control, text: String) -> void:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", 34)
	l.add_theme_color_override("font_color", Color(1, 0.75, 0.15))
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vb.add_child(l)


func _button(parent: Control, text: String, cb: Callable, enabled: bool = true) -> Button:
	var b := Button.new()
	b.text = text
	b.disabled = not enabled
	b.add_theme_font_size_override("font_size", 20)
	b.custom_minimum_size = Vector2(0, 40)
	b.pressed.connect(cb)
	parent.add_child(b)
	return b


func _text(parent: Control, text: String, size: int = 18, color: Color = Color.WHITE) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	parent.add_child(l)
	return l


func _unhandled_input(event: InputEvent) -> void:
	if menu_kind == "":
		return
	if event.is_action_pressed("pause") or (menu_kind == "jobs" and event.is_action_pressed("menu_jobs")) or (menu_kind == "garage" and event.is_action_pressed("garage")):
		close_menu()
		get_viewport().set_input_as_handled()


func toggle_pause() -> void:
	if menu_kind != "":
		close_menu()
		return
	var vb := _open_menu("pause")
	_title(vb, "DURAKLATILDI")
	_text(vb, "Para: $%s   Kazanılan yarış: %d\nKaçış: %d   Yakalanma: %d   Toplam ödül: $%s" % [
		_fmt_money(Game.money), Game.races_won, Game.escapes, Game.busted, _fmt_money(Game.total_bounty)])
	_button(vb, "Devam Et", close_menu)
	_button(vb, "Oyunu Kaydet", func(): Game.save_game(); Game.toast("Oyun kaydedildi", Color(0.4, 1, 0.5)))
	_button(vb, "Garaja Işınlan", func():
		close_menu()
		if main.police.heat_level() > 0 or main.races.active:
			Game.toast("Takip/yarış sırasında olmaz!", Color(1, 0.3, 0.3))
		else:
			main.respawn_at_garage())
	_button(vb, "Kaydet ve Çık", func(): Game.save_game(); get_tree().quit())


func open_jobs() -> void:
	if menu_kind != "":
		close_menu()
		return
	var vb := _open_menu("jobs")
	_title(vb, "YARIŞLAR VE İŞLER")
	if main.races.active:
		_text(vb, "Şu an aktif bir etkinlik var.", 18, Color(1, 0.8, 0.4))
		_button(vb, "Etkinliği İptal Et", func(): close_menu(); main.races.cancel())
	if main.police.heat_level() > 0:
		_text(vb, "Polis takibindeyken etkinlik başlatılamaz!", 18, Color(1, 0.3, 0.3))
	var ok: bool = main.police.heat_level() == 0
	for i in RaceManager.RACES.size():
		var r: Dictionary = RaceManager.RACES[i]
		var t := "Sprint" if r["type"] == "sprint" else "Devre (%d tur)" % r["laps"]
		_button(vb, "%s  —  %s  —  Ödül $%s" % [r["name"], t, _fmt_money(r["prize"])], func(): close_menu(); main.races.start_race(i), ok)
	_text(vb, "İşler", 22, Color(0.4, 1, 0.5))
	_button(vb, "Teslimat İşi (zamana karşı)", func(): close_menu(); main.races.start_delivery(), ok)
	_button(vb, "Kapat", close_menu)


func open_garage(view: String = "") -> void:
	garage_view = view if view != "" else Game.current
	var vb := _open_menu("garage")
	_title(vb, "GARAJ")
	_text(vb, "Para: $%s" % _fmt_money(Game.money), 22, Color(0.4, 1, 0.5))
	var hb := HBoxContainer.new()
	hb.add_theme_constant_override("separation", 20)
	vb.add_child(hb)
	var list := VBoxContainer.new()
	list.custom_minimum_size = Vector2(330, 0)
	hb.add_child(list)
	for id in Game.CARS:
		var c: Dictionary = Game.CARS[id]
		var mark := ""
		if id == Game.current:
			mark = "  [SEÇİLİ]"
		elif Game.owned.has(id):
			mark = "  [SAHİP]"
		var b := _button(list, "%s  $%s%s" % [c["name"], _fmt_money(c["price"]), mark], func(): open_garage(id))
		if id == garage_view:
			b.add_theme_color_override("font_color", Color(1, 0.8, 0.2))
	var det := VBoxContainer.new()
	det.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hb.add_child(det)
	var id: String = garage_view
	var base: Dictionary = Game.CARS[id]
	var cfg := Game.get_car_config(id)
	var owned := Game.owned.has(id)
	_text(det, base["name"], 28, Color(1, 0.85, 0.3))
	_text(det, "Güç: %d   Azami hız: %d km/s\nYol tutuş: %.2f   Nitro süresi: %.1f sn   Ağırlık: %d kg" % [
		int(cfg["power"]), int(cfg["top"]), cfg["grip"], cfg["nitro"], int(cfg["mass"])])
	var row := HBoxContainer.new()
	det.add_child(row)
	if not owned:
		_button(row, "Satın Al ($%s)" % _fmt_money(base["price"]), func():
			if Game.buy_car(id):
				Game.toast("%s satın alındı!" % base["name"], Color(0.4, 1, 0.5))
			else:
				Game.toast("Yetersiz para!", Color(1, 0.3, 0.3))
			open_garage(id), Game.money >= base["price"])
	else:
		_button(row, "Seç", func():
			Game.current = id
			Game.save_game()
			main.spawn_player(false)
			open_garage(id), id != Game.current)
		_button(row, "Sat ($%s)" % _fmt_money(int(base["price"] * 0.6)), func():
			if Game.sell_car(id):
				Game.toast("%s satıldı." % base["name"], Color(1, 0.8, 0.3))
				main.spawn_player(false)
			open_garage(""), Game.owned.size() > 1)
	if owned:
		_text(det, "Boya", 20, Color(0.8, 0.8, 1))
		var paints := HBoxContainer.new()
		det.add_child(paints)
		for col in Game.PAINTS:
			var pb := Button.new()
			pb.custom_minimum_size = Vector2(36, 36)
			var sb := StyleBoxFlat.new()
			sb.bg_color = col
			sb.set_corner_radius_all(18)
			pb.add_theme_stylebox_override("normal", sb)
			pb.add_theme_stylebox_override("hover", sb)
			pb.add_theme_stylebox_override("pressed", sb)
			var pc: Color = col
			pb.pressed.connect(func():
				Game.set_paint(id, pc)
				if id == Game.current:
					main.player.set_paint(pc)
				Game.toast("Boya uygulandı", pc))
			paints.add_child(pb)
		_text(det, "Performans Yükseltmeleri", 20, Color(0.8, 0.8, 1))
		var names := {"eng": "Motor", "nitro": "Nitro", "hand": "Süspansiyon / Yol Tutuş"}
		for k in names:
			var lvl: int = Game.owned[id].get(k, 0)
			var cost := Game.upgrade_cost(id, k)
			var label := "%s  Seviye %d/%d" % [names[k], lvl, Game.MAX_UPGRADE]
			if lvl < Game.MAX_UPGRADE:
				label += "  — Yükselt $%s" % _fmt_money(cost)
			var kk: String = k
			_button(det, label, func():
				if Game.upgrade(id, kk):
					Game.toast("%s yükseltildi!" % names[kk], Color(0.4, 1, 0.5))
					if id == Game.current:
						main.spawn_player(false)
				else:
					Game.toast("Yükseltme yapılamadı (para/seviye)", Color(1, 0.3, 0.3))
				open_garage(id), lvl < Game.MAX_UPGRADE and Game.money >= cost)
	_button(vb, "Garajdan Çık", close_menu)


# ------------------------------------------------------------------ iç sınıflar

class StarRow extends Control:
	var level: int = 0
	var flash: bool = false

	func _draw() -> void:
		var t := Time.get_ticks_msec() / 1000.0
		for i in 5:
			var c := Vector2(26 + i * 52, 23)
			var lit := i < level
			var col := Color(0.25, 0.25, 0.25, 0.6)
			if lit:
				col = Color(1, 0.15, 0.15) if (not flash or int(t * 3.0) % 2 == 0) else Color(0.3, 0.4, 1)
			var pts := PackedVector2Array()
			for k in 10:
				var r := 20.0 if k % 2 == 0 else 8.5
				var a := -PI / 2 + k * PI / 5
				pts.append(c + Vector2(cos(a), sin(a)) * r)
			draw_colored_polygon(pts, col)
			pts.append(pts[0])
			draw_polyline(pts, Color(0, 0, 0, 0.8), 1.5)


class Minimap extends Control:
	var hud
	var scale_m := 0.33   # piksel / metre

	func _draw() -> void:
		var main = hud.main
		if main == null or main.player == null:
			return
		var sz := size
		var center := sz * 0.5
		draw_rect(Rect2(Vector2.ZERO, sz), Color(0, 0, 0, 0.6))
		var p: Vector3 = main.player.global_position
		var yaw: float = main.player.global_rotation.y
		var city: City = main.city
		var xf := func(w: Vector3) -> Vector2:
			var d := Vector2(w.x - p.x, w.z - p.z)
			# oyuncu yönü yukarı bakacak şekilde döndür
			d = d.rotated(yaw)
			return center + Vector2(-d.x, -d.y) * scale_m
		# yollar
		for n in city.nodes.size():
			for m in city.neighbors[n]:
				if m > n:
					var w := 5.0 if (City.is_ring(n % City.N) and City.is_ring(m % City.N)) or (City.is_ring(n / City.N) and City.is_ring(m / City.N)) else 3.0
					draw_line(xf.call(city.nodes[n]), xf.call(city.nodes[m]), Color(0.6, 0.6, 0.65), w)
		# garaj
		_marker(xf.call(main.garage_pos), Color(1, 0.8, 0.2), "G")
		# hedef
		var tgt = main.races.current_target()
		if tgt != null:
			_marker(xf.call(tgt), Color(0.2, 1, 0.4), "")
		for t in main.traffic:
			if is_instance_valid(t):
				_dot(xf.call(t.global_position), Color(0.75, 0.75, 0.75), 2.5)
		for r in main.races.racers:
			if is_instance_valid(r):
				_dot(xf.call(r.global_position), Color(1, 0.6, 0.1), 4.0)
		var blink := int(Time.get_ticks_msec() / 250) % 2 == 0
		for u in main.police.units:
			if is_instance_valid(u):
				_dot(xf.call(u.global_position), Color(1, 0.1, 0.1) if blink else Color(0.2, 0.4, 1), 4.5)
		# oyuncu oku
		draw_colored_polygon(PackedVector2Array([center + Vector2(0, -9), center + Vector2(6, 7), center + Vector2(-6, 7)]), Color(1, 1, 1))
		draw_rect(Rect2(Vector2.ZERO, sz), Color(1, 0.7, 0.1, 0.8), false, 2.0)

	func _dot(v: Vector2, c: Color, r: float) -> void:
		if Rect2(Vector2.ZERO, size).has_point(v):
			draw_circle(v, r, c)

	func _marker(v: Vector2, c: Color, txt: String) -> void:
		var r := Rect2(Vector2(6, 6), size - Vector2(12, 12))
		v = Vector2(clampf(v.x, r.position.x, r.end.x), clampf(v.y, r.position.y, r.end.y))
		draw_circle(v, 7.0, c)
		if txt != "":
			draw_string(ThemeDB.fallback_font, v + Vector2(-4, 5), txt, HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Color.BLACK)
