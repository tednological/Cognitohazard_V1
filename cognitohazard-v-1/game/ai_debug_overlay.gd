extends RefCounted

## The F3 AI debug overlay (Guard_AI.md §9.2), as a pure function of the
## GetAiDebug() snapshot: main.gd calls it in its WORLD pass, and
## tests/editor_check.gd calls it inside a real _draw with a hand-built
## snapshot whose every section is populated, which is the only way the
## squad, group and sweep-node drawing gets exercised headlessly.
##
## Draws each guard's facing, posture/task/awareness, remaining nav path
## (grey) and assault route (orange), and his LKP; squad links (red) to the
## caller and the rally ring; sweep groups (violet, green for the exit group)
## and the node each is walking to; every sweep node, blue just seen, red once
## an unseen minute has passed. Guards the player cannot see are drawn too,
## which is why it is a debug view and off by default.
##
## Postures are recognised by NAME (from SimBridge.GuardStateNames), so this
## file mirrors no ordinals. Returns what it parsed, "consumed" being how many
## ints it read: the harness asserts that equals the snapshot's length.

const FX: float = 256.0
const DBG_STATE := [Color(0.55, 0.80, 1.0), Color(1.0, 0.75, 0.30), Color(1.0, 0.35, 0.30),
	Color(0.80, 0.55, 1.0)]
const DBG_NAV := Color(0.70, 0.70, 0.80, 0.45)
const DBG_ROUTE := Color(1.0, 0.55, 0.20, 0.90)
const DBG_SQUAD := Color(1.0, 0.35, 0.30, 0.70)
const DBG_GROUP := Color(0.80, 0.55, 1.0, 0.80)
const DBG_EXIT := Color(0.45, 1.0, 0.55, 0.85)
const DBG_FRESH := Color(0.25, 0.55, 0.95, 0.55)
const DBG_STALE := Color(1.0, 0.30, 0.25, 0.85)
const C_SIGNAL := Color(0.90, 0.32, 0.28)


static func draw(ci: CanvasItem, d: PackedInt32Array, font: Font,
		state_names: PackedStringArray, task_names: PackedStringArray) -> Dictionary:
	var out := {"guards": 0, "squads": 0, "groups": 0, "nodes": 0, "consumed": 0,
		"intel_age": -1, "compromised": false}
	var i: int = 0

	var guard_n: int = d[i]
	i += 1
	out["guards"] = guard_n
	var guard_pos: Array[Vector2] = []
	for _g in range(guard_n):
		var pos := Vector2(d[i] / FX, d[i + 1] / FX)
		var facing: float = d[i + 2] / 65536.0 * TAU
		var state: int = d[i + 3]
		var task: int = d[i + 4]
		var aw: int = d[i + 5]
		var has_lkp: bool = d[i + 6] == 1
		var lkp := Vector2(d[i + 7] / FX, d[i + 8] / FX)
		var squad: int = d[i + 9]
		var group: int = d[i + 10]
		var nav_n: int = d[i + 11]
		var route_n: int = d[i + 12]
		var afraid: bool = d[i + 13] == 1
		i += 14
		guard_pos.append(pos)
		var prev: Vector2 = pos
		for _k in range(nav_n):
			var q := Vector2(d[i] / FX, d[i + 1] / FX)
			ci.draw_line(prev, q, DBG_NAV, 1.0)
			prev = q
			i += 2
		prev = pos
		for _k in range(route_n):
			var q2 := Vector2(d[i] / FX, d[i + 1] / FX)
			ci.draw_line(prev, q2, DBG_ROUTE, 2.0)
			ci.draw_circle(q2, 2.5, DBG_ROUTE)
			prev = q2
			i += 2
		var state_name: String = state_names[state] if state < state_names.size() else "?"
		if state_name == "Down" or state_name == "Dead":
			continue
		var col: Color = DBG_STATE[state] if state < DBG_STATE.size() else Color.WHITE
		ci.draw_line(pos, pos + Vector2.from_angle(facing) * 26.0, col, 1.5)
		if has_lkp and state_name != "Relaxed":
			ci.draw_line(pos, lkp, Color(col, 0.35), 1.0)
			ci.draw_line(lkp + Vector2(-4, -4), lkp + Vector2(4, 4), col, 1.5)
			ci.draw_line(lkp + Vector2(-4, 4), lkp + Vector2(4, -4), col, 1.5)
		var task_name: String = task_names[task] if task < task_names.size() else "?"
		var tag: String = "%s/%s %d" % [state_name.to_lower(), task_name.to_lower(), aw / 10]
		if squad >= 0:
			tag += " s%d" % squad
		if group >= 0:
			tag += " g%d" % group
		if afraid:
			tag += " AFRAID"
		ci.draw_string(font, pos + Vector2(-30.0, 28.0), tag, HORIZONTAL_ALIGNMENT_LEFT, -1, 9, col)

	var has_intel: bool = d[i] == 1
	var intel := Vector2(d[i + 1] / FX, d[i + 2] / FX)
	var intel_age: int = d[i + 3]
	out["compromised"] = d[i + 4] == 1
	var has_focus: bool = d[i + 5] == 1
	var focus := Vector2(d[i + 6] / FX, d[i + 7] / FX)
	i += 8
	out["intel_age"] = intel_age if has_intel else -1
	if has_intel:
		ci.draw_arc(intel, 10.0, 0.0, TAU, 20, C_SIGNAL, 2.0)
		ci.draw_string(font, intel + Vector2(12.0, 4.0), "intel %.1fs" % (intel_age / 60.0),
			HORIZONTAL_ALIGNMENT_LEFT, -1, 9, C_SIGNAL)
	if has_focus:
		ci.draw_arc(focus, 14.0, 0.0, TAU, 20, DBG_GROUP, 1.0)

	var squad_n: int = d[i]
	i += 1
	out["squads"] = squad_n
	for _s in range(squad_n):
		var anchor: int = d[i]
		var go: bool = d[i + 1] == 1
		var rally := Vector2(d[i + 2] / FX, d[i + 3] / FX)
		var squad_m: int = d[i + 4]
		i += 5
		var link: Color = Color(DBG_SQUAD, 0.35) if go else DBG_SQUAD
		if not go:
			ci.draw_arc(rally, 40.0, 0.0, TAU, 24, DBG_SQUAD, 1.0)
		for k in range(squad_m):
			var member: int = d[i + k]
			if member < guard_pos.size() and anchor < guard_pos.size() and member != anchor:
				ci.draw_line(guard_pos[member], guard_pos[anchor], link, 1.0)
		i += squad_m

	var group_n: int = d[i]
	i += 1
	out["groups"] = group_n
	var group_targets: Array = []
	for _gg in range(group_n):
		var exit_group: bool = d[i] == 1
		var target: int = d[i + 1]
		var group_m: int = d[i + 4]
		i += 5
		var gcol: Color = DBG_EXIT if exit_group else DBG_GROUP
		var lead: int = d[i] if group_m > 0 else -1
		for k in range(1, group_m):
			var member2: int = d[i + k]
			if member2 < guard_pos.size() and lead >= 0 and lead < guard_pos.size():
				ci.draw_line(guard_pos[lead], guard_pos[member2], gcol, 2.0)
		if lead >= 0 and lead < guard_pos.size():
			group_targets.append([guard_pos[lead], target, gcol])
		i += group_m

	var node_n: int = d[i]
	i += 1
	out["nodes"] = node_n
	var node_pos: Array[Vector2] = []
	for _k in range(node_n):
		var q3 := Vector2(d[i] / FX, d[i + 1] / FX)
		var stale: int = d[i + 2]
		var claimed: int = d[i + 3]
		var authored: bool = d[i + 4] == 1
		i += 5
		node_pos.append(q3)
		var heat: float = clampf(stale / 3600.0, 0.0, 1.0)
		var ncol: Color = DBG_FRESH.lerp(DBG_STALE, heat)
		ci.draw_rect(Rect2(q3 - Vector2(4, 4), Vector2(8, 8)), ncol)
		if authored:
			ci.draw_arc(q3, 7.5, 0.0, TAU, 16, ncol, 1.5)
		if claimed >= 0:
			ci.draw_rect(Rect2(q3 - Vector2(6, 6), Vector2(12, 12)), Color(1, 1, 1, 0.7), false, 1.0)
	for t in group_targets:
		var tgt: int = t[1]
		if tgt >= 0 and tgt < node_pos.size():
			ci.draw_line(t[0], node_pos[tgt], Color(t[2], 0.45), 1.0)
	out["consumed"] = i
	return out
