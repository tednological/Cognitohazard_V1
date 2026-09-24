extends SceneTree

## Headless verification for game/audio.gd (spec §7.4).
##
## The sim harness cannot cover this: audio lives in game/ and tests/ never
## imports game/. So it gets its own runner:
##   Godot --headless --path . --script res://tests/audio_check.gd
## Exits non-zero on failure.
##
## It checks that every sound actually renders to audible, finite samples of the
## right length, and that the master lowpass curve matches the spec formula. A
## synthesis bug here is otherwise silent — literally.

var _pass: int = 0
var _fail: int = 0


func _check(name: String, ok: bool, detail: String = "") -> void:
	if ok:
		_pass += 1
		print("  PASS  %s" % name)
	else:
		_fail += 1
		print("  FAIL  %s\n          %s" % [name, detail])


var _done: bool = false

## Seconds still to wait before quitting, or negative before the checks have
## run. The wiring check PLAYS sounds, and the audio server releases a stopped
## voice's playback on its own mixing thread: quitting on the frame they were
## played intermittently left two playbacks and their stream alive past exit
## ("ObjectDB instances leaked"). Time, not frames: a headless frame is not
## throttled, so a few of them can pass before the mixer runs once.
var _quit_in: float = -1.0


## Checks run on the first processed frame, not in _initialize(). A node added
## to the tree during _initialize() does not get _ready() until the tree starts
## processing, so the voice pool would still be empty and the wiring check would
## fail against working code.
func _process(delta: float) -> bool:
	if _quit_in >= 0.0:
		_quit_in -= delta
		if _quit_in < 0.0:
			quit(0 if _fail == 0 else 1)
			return true
		return false
	if _done:
		return true
	_done = true

	print("audio harness - spec 7.4")
	print()

	var audio_script: GDScript = load("res://game/audio.gd")
	var a: Node = audio_script.new()
	a._setup_bus()

	_check_lowpass(a)
	_check_sounds(a)
	_check_wiring(audio_script)

	print()
	print("%d passed, %d failed" % [_pass, _fail])
	_quit_in = 0.3
	return false


## cutoff = 400 + 17600 * pow(world_scale, 0.7)
func _check_lowpass(a: Node) -> void:
	print("  -- master lowpass --")
	var cases := [
		[1.0, 18000.0],
		[0.62, 12994.7],
		[0.18, 5699.1],
		[0.0, 400.0],
	]
	for c in cases:
		a.update_world_scale(c[0])
		var got: float = a._lpf.cutoff_hz
		var want: float = c[1]
		_check("cutoff at scale %0.2f" % c[0], absf(got - want) < 60.0,
			"expected ~%0.0f Hz, got %0.0f Hz" % [want, got])

	# The closing filter is the whole point: dilation must audibly darken.
	a.update_world_scale(1.0)
	var open_hz: float = a._lpf.cutoff_hz
	a.update_world_scale(0.18)
	var closed_hz: float = a._lpf.cutoff_hz
	_check("dilation closes the filter by >3x", open_hz > closed_hz * 3.0,
		"%0.0f Hz open vs %0.0f Hz dilated" % [open_hz, closed_hz])


func _check_sounds(a: Node) -> void:
	print("  -- synthesis --")
	var names := {
		a.SHOT: "shot", a.ESHOT: "guard shot", a.WALL: "wall hit",
		a.FLESH: "flesh", a.DRY: "dry click", a.RELOAD: "reload",
		a.PICKUP: "pickup", a.SUBDUE: "subdue", a.DEGRADE: "degrade",
		a.DESTROY: "destroy", a.DEATH: "death", a.NOTICE: "notice",
		a.ALERT: "alert", a.BODYFOUND: "body found",
		a.GLASS: "glass", a.DOOR_OPEN: "door open", a.DOOR_CLOSE: "door close",
		a.ZAP: "zap", a.BOOM: "boom", a.TINK: "grenade tink", a.THROW: "throw",
		a.RADIO_KEY: "radio key-up", a.RADIO_SENT: "radio call through",
		a.RADIO_CUT: "radio cut off", a.COMPROMISED: "compromised klaxon",
		a.LAMP: "lamp shattering", a.SWITCH: "light switch",
	}

	for id in names:
		var stream: AudioStreamWAV = a._get_stream(id, 1.0)
		var label: String = names[id]

		if stream == null:
			_check("%s renders" % label, false, "null stream")
			continue

		var n: int = stream.data.size() / 2
		var peak: float = 0.0
		var energy: float = 0.0
		var finite: bool = true

		for i in range(n):
			var lo: int = stream.data[i * 2]
			var hi: int = stream.data[i * 2 + 1]
			var u: int = lo | (hi << 8)
			if u >= 32768:
				u -= 65536
			var v: float = float(u) / 32767.0
			if not is_finite(v):
				finite = false
			peak = maxf(peak, absf(v))
			energy += v * v

		var seconds: float = float(n) / float(a.SR)
		_check("%s renders samples" % label, n > 0, "empty")
		_check("%s is audible" % label, peak > 0.02, "peak %0.4f" % peak)
		_check("%s is finite" % label, finite, "NaN or inf in buffer")
		_check("%s does not clip flat" % label, peak <= 1.0, "peak %0.4f" % peak)
		# %e is not a GDScript conversion - it passes --check-only and then errors
		# once per call at runtime. Scale into a readable range instead.
		_check("%s has energy" % label, energy / maxf(1.0, float(n)) > 1e-7,
			"mean square %0.6f" % (energy / maxf(1.0, float(n)) * 1000.0))
		_check("%s has sane duration" % label, seconds > 0.01 and seconds < 6.0,
			"%0.3f s" % seconds)

	print("  -- time scaling --")
	# The prototype's five impacts scale with the world clock, and GLASS with
	# them (a pane is an impact too).
	# The expected stretch is derived from the recipe rather than assumed to be
	# 1/k: every sound carries a fixed 0.03 s tail that does not scale, so a
	# short sound like the wall hit stretches by less than 4x and that is
	# correct. Hardcoding 4x would have failed a working synth.
	for id in [a.SHOT, a.ESHOT, a.WALL, a.FLESH, a.SUBDUE, a.GLASS, a.ZAP, a.BOOM, a.LAMP]:
		var full: AudioStreamWAV = a._get_stream(id, 1.0)
		var slow: AudioStreamWAV = a._get_stream(id, 0.25)
		var ratio: float = float(slow.data.size()) / float(full.data.size())
		var want: float = _recipe_span(a, id, 0.25) / _recipe_span(a, id, 1.0)
		_check("sound %d stretches as its recipe predicts" % id,
			absf(ratio - want) < 0.03, "expected %0.3f, got %0.3f" % [want, ratio])
		_check("sound %d audibly stretches" % id, ratio > 2.5,
			"stretch ratio %0.2f" % ratio)

	# And these must NOT scale.
	for id in [a.DRY, a.RELOAD, a.PICKUP, a.DEGRADE, a.NOTICE, a.DOOR_OPEN, a.DOOR_CLOSE,
			a.TINK, a.THROW, a.SWITCH]:
		var full2: AudioStreamWAV = a._get_stream(id, 1.0)
		var slow2: AudioStreamWAV = a._get_stream(id, 0.25)
		_check("sound %d is unaffected by dilation" % id,
			full2.data.size() == slow2.data.size(),
			"%d vs %d bytes" % [full2.data.size(), slow2.data.size()])

	print("  -- caching --")
	var first: AudioStreamWAV = a._get_stream(a.SHOT, 1.0)
	var second: AudioStreamWAV = a._get_stream(a.SHOT, 1.0)
	_check("streams are cached, not re-rendered", first == second)

	a.free()


## Rendering correct samples is useless if they never reach a voice. This
## exercises the real path: _ready builds the bus and the player pool, and
## play() must hand a stream to one of them.
func _check_wiring(audio_script: GDScript) -> void:
	print("  -- playback wiring --")
	var host := Node.new()
	root.add_child(host)

	var b: Node = audio_script.new()
	host.add_child(b)

	_check("bus was created", AudioServer.get_bus_index("Sfx") >= 0)
	_check("voice pool exists", b._players.size() == b.VOICES,
		"%d voices" % b._players.size())

	b.play(b.SHOT, 1.0)
	_check("play() dispatches to a voice", b.played == 1, "played %d" % b.played)
	_check("the voice received a stream", b._players[0].stream != null)
	_check("the voice is on the Sfx bus", b._players[0].bus == "Sfx",
		b._players[0].bus)

	# Round-robin so rapid fire does not cut itself off.
	b.play(b.SHOT, 1.0)
	_check("consecutive sounds use different voices",
		b._players[0].stream != null and b._players[1].stream != null)

	b.enabled = false
	var before: int = b.played
	b.play(b.SHOT, 1.0)
	_check("disabling audio suppresses playback", b.played == before)

	for p in b._players:
		p.stop()
	host.queue_free()


func _recipe_span(a: Node, id: int, k: float) -> float:
	var total: float = 0.0
	for l in a._recipe(id, k):
		total = maxf(total, l["at"] + l["dur"] + 0.03)
	return total
