extends Node

## Synthesized audio (spec §7.4). No samples — every sound is generated from the
## prototype's synthesis parameters at runtime.
##
## Signal path mirrors the prototype exactly:
##     source -> per-voice filter -> per-voice gain envelope
##             -> master gain 0.42 -> MASTER LOWPASS -> out
##
## The master lowpass is the point of the whole file. Spec §7.4: "This closing
## filter under dilation sells the effect more than any visual does. Implement it
## first." Its cutoff tracks the smoothed world scale every frame.

const SR: int = 44100
const MASTER_GAIN: float = 0.42
const ENV_FLOOR: float = 0.0008          # exponentialRampToValueAtTime target
const VOICES: int = 16

# Sound ids.
enum {
	SHOT, ESHOT, WALL, FLESH, DRY, RELOAD, PICKUP, SUBDUE,
	DEGRADE, DESTROY, DEATH, NOTICE, ALERT, BODYFOUND,
	# Appended with glass and doors. Ids are only ever appended.
	GLASS, DOOR_OPEN, DOOR_CLOSE,
	# Appended with the specialist weapons.
	ZAP, BOOM, TINK, THROW,
	# Appended with Guard AI v2's radio (Guard_AI.md §9.2). None scale with the
	# world clock: a radio is not an impact.
	RADIO_KEY, RADIO_SENT, RADIO_CUT, COMPROMISED,
	# Appended with lighting (cognitohazard_lighting_plan.md §9.2). LAMP is an
	# impact and scales with the world clock; SWITCH is a hand on a wall.
	LAMP, SWITCH
}

## Only these scale with the world clock. Spec §7.4 says "every sound's
## frequencies scale", but the prototype applies ts() to exactly its five
## impacts — the others are fixed. Per spec §12, the prototype wins on feel. See
## the milestone report; this is a spec/prototype discrepancy, not an omission.
## GLASS joined them because it is an impact too: a pane shot out under
## dilation should ring out slow like the round that broke it. ZAP and BOOM are
## impacts too: a discharge or a blast under dilation is the moment to hear it.
const SCALES_WITH_TIME := [SHOT, ESHOT, WALL, FLESH, SUBDUE, GLASS, ZAP, BOOM, LAMP]

## Time-scale buckets for cached rendering. The prototype clamps at 0.25, so
## that is the floor here too.
const BUCKETS := [1.0, 0.8, 0.6, 0.45, 0.32, 0.25]

var _bus_idx: int = -1
var _lpf: AudioEffectLowPassFilter
var _players: Array[AudioStreamPlayer] = []
var _next_voice: int = 0
var _cache: Dictionary = {}
var _rng := RandomNumberGenerator.new()

var enabled: bool = true

## Count of sounds actually dispatched to a voice. Reported at exit: a silent
## game and a broken audio graph look identical otherwise.
var played: int = 0


func _ready() -> void:
	_rng.randomize()
	_setup_bus()
	_setup_voices()


func _setup_bus() -> void:
	_bus_idx = AudioServer.bus_count
	AudioServer.add_bus(_bus_idx)
	AudioServer.set_bus_name(_bus_idx, "Sfx")
	AudioServer.set_bus_send(_bus_idx, "Master")

	_lpf = AudioEffectLowPassFilter.new()
	_lpf.cutoff_hz = 18000.0
	AudioServer.add_bus_effect(_bus_idx, _lpf)

	AudioServer.set_bus_volume_db(_bus_idx, linear_to_db(MASTER_GAIN))


func _setup_voices() -> void:
	for i in range(VOICES):
		var p := AudioStreamPlayer.new()
		p.bus = "Sfx"
		add_child(p)
		_players.append(p)


## Master lowpass: cutoff = 400 + 17600 * pow(world_scale, 0.7), per frame.
func update_world_scale(scale: float) -> void:
	if _lpf == null:
		return
	var s: float = clampf(scale, 0.0, 1.0)
	_lpf.cutoff_hz = clampf(400.0 + 17600.0 * pow(s, 0.7), 20.0, 20000.0)


func play(sound: int, scale: float) -> void:
	if not enabled:
		return
	var k: float = 1.0
	if sound in SCALES_WITH_TIME:
		k = maxf(0.25, scale)
	var stream: AudioStreamWAV = _get_stream(sound, k)
	if stream == null:
		return
	var p: AudioStreamPlayer = _players[_next_voice]
	_next_voice = (_next_voice + 1) % VOICES
	p.stream = stream
	p.play()
	played += 1


# --------------------------------------------------------------- rendering

func _get_stream(sound: int, k: float) -> AudioStreamWAV:
	var bucket: float = _nearest_bucket(k)
	var key: String = "%d:%0.2f" % [sound, bucket]
	if _cache.has(key):
		return _cache[key]
	# Rendered on first use rather than all at load: the whole table at every
	# bucket is ~740k samples, and most of it is never reached in a given run.
	var stream: AudioStreamWAV = _render(_recipe(sound, bucket))
	_cache[key] = stream
	return stream


func _nearest_bucket(k: float) -> float:
	var best: float = BUCKETS[0]
	var best_d: float = absf(k - best)
	for b in BUCKETS:
		var d: float = absf(k - b)
		if d < best_d:
			best_d = d
			best = b
	return best


## A layer is {kind, at, dur, f0, f1, gain, filter, q, wave}.
func _recipe(sound: int, k: float) -> Array:
	match sound:
		SHOT:
			return [
				_noise(0.0, 0.11 / k, 3200.0 * k, 140.0 * k, 0.62),
				_tone(0.0, 0.10 / k, 200.0 * k, 58.0 * k, 0.42, "sine"),
			]
		ESHOT:
			return [
				_noise(0.0, 0.09 / k, 2300.0 * k, 180.0 * k, 0.30),
				_tone(0.0, 0.08 / k, 160.0 * k, 55.0 * k, 0.20, "sine"),
			]
		WALL:
			return [_noise(0.0, 0.05 / k, 5200.0 * k, 1100.0 * k, 0.17, "bandpass", 2.4)]
		FLESH:
			return [
				_noise(0.0, 0.20 / k, 820.0 * k, 90.0 * k, 0.45),
				_tone(0.0, 0.30 / k, 96.0 * k, 40.0 * k, 0.30, "sine"),
			]
		SUBDUE:
			return [_noise(0.0, 0.14 / k, 620.0 * k, 110.0 * k, 0.30)]
		DRY:
			return [_noise(0.0, 0.03, 3400.0, 2100.0, 0.28, "bandpass", 3.0)]
		RELOAD:
			# Three bandpass clicks at 0 ms, 260 ms, 980 ms.
			return [
				_noise(0.0, 0.04, 2600.0, 1400.0, 0.22, "bandpass", 3.0),
				_noise(0.26, 0.05, 1800.0, 700.0, 0.26, "bandpass", 2.5),
				_noise(0.98, 0.04, 3000.0, 1600.0, 0.24, "bandpass", 3.0),
			]
		PICKUP:
			return [
				_tone(0.0, 0.08, 680.0, 1020.0, 0.16, "triangle"),
				_tone(0.07, 0.10, 1020.0, 1360.0, 0.13, "triangle"),
			]
		DEGRADE:
			return [
				_tone(0.0, 0.55, 320.0, 132.0, 0.24, "sawtooth"),
				_noise(0.0, 0.35, 900.0, 180.0, 0.18),
			]
		DESTROY:
			return [
				_tone(0.0, 0.85, 210.0, 44.0, 0.32, "sawtooth"),
				_noise(0.0, 0.55, 700.0, 90.0, 0.24),
			]
		DEATH:
			return [
				_tone(0.0, 1.10, 180.0, 36.0, 0.34, "sawtooth"),
				_noise(0.0, 0.60, 1400.0, 80.0, 0.34),
			]
		NOTICE:
			return [_tone(0.0, 0.14, 520.0, 760.0, 0.16, "square")]
		ALERT:
			return [
				_tone(0.0, 0.16, 420.0, 300.0, 0.22, "square"),
				_tone(0.15, 0.20, 300.0, 220.0, 0.20, "square"),
			]
		GLASS:
			# A bright crack, then the tinkle of it falling: a burst of high
			# bandpassed noise and a scatter of short, high, detuned pings
			# spread over half a second. Scales with the world clock like
			# every other impact, so a pane going under dilation rings out slow.
			return [
				_noise(0.0, 0.09 / k, 7000.0 * k, 2600.0 * k, 0.42, "bandpass", 1.6),
				_noise(0.0, 0.05 / k, 1800.0 * k, 400.0 * k, 0.26),
				_tone(0.03 / k, 0.12 / k, 4100.0 * k, 3900.0 * k, 0.07, "triangle"),
				_tone(0.10 / k, 0.10 / k, 5300.0 * k, 5100.0 * k, 0.06, "triangle"),
				_tone(0.17 / k, 0.14 / k, 3600.0 * k, 3500.0 * k, 0.05, "triangle"),
				_noise(0.12 / k, 0.40 / k, 6200.0 * k, 3000.0 * k, 0.10, "bandpass", 3.0),
				_tone(0.28 / k, 0.12 / k, 4700.0 * k, 4600.0 * k, 0.04, "triangle"),
			]
		LAMP:
			# A bulb going: a thin high pop, the fizz of the filament dying,
			# and a few small shards -- glass's voice, pitched up and shorter.
			return [
				_noise(0.0, 0.04 / k, 8800.0 * k, 4200.0 * k, 0.34, "bandpass", 1.8),
				_tone(0.0, 0.05 / k, 2200.0 * k, 600.0 * k, 0.10, "square"),
				_noise(0.02 / k, 0.22 / k, 5200.0 * k, 2600.0 * k, 0.07, "bandpass", 6.0),
				_tone(0.06 / k, 0.08 / k, 6100.0 * k, 6000.0 * k, 0.04, "triangle"),
				_tone(0.13 / k, 0.07 / k, 5400.0 * k, 5300.0 * k, 0.03, "triangle"),
			]
		SWITCH:
			# A clack: a short hard click and the plate's small knock.
			return [
				_noise(0.0, 0.012, 3800.0, 2200.0, 0.30, "bandpass", 2.5),
				_tone(0.0, 0.04, 260.0, 180.0, 0.10, "square"),
			]
		DOOR_OPEN:
			# The latch, then a low hinge groan rising slightly.
			return [
				_noise(0.0, 0.03, 2400.0, 1200.0, 0.20, "bandpass", 3.0),
				_tone(0.04, 0.32, 150.0, 210.0, 0.10, "sawtooth"),
				_noise(0.04, 0.30, 700.0, 500.0, 0.06, "bandpass", 5.0),
			]
		DOOR_CLOSE:
			# The same groan falling, and a thud as it meets the frame.
			return [
				_tone(0.0, 0.22, 200.0, 140.0, 0.08, "sawtooth"),
				_noise(0.22, 0.10, 520.0, 90.0, 0.34),
				_tone(0.22, 0.14, 90.0, 50.0, 0.22, "sine"),
				_noise(0.23, 0.03, 2600.0, 1500.0, 0.14, "bandpass", 3.0),
			]
		ZAP:
			# A crack with a buzz under it: bright bandpassed noise for the
			# strike, a falling sawtooth for the charge leaving the air.
			return [
				_noise(0.0, 0.06 / k, 6400.0 * k, 3000.0 * k, 0.40, "bandpass", 1.4),
				_tone(0.0, 0.22 / k, 1400.0 * k, 90.0 * k, 0.18, "sawtooth"),
				_tone(0.0, 0.18 / k, 120.0 * k, 110.0 * k, 0.14, "square"),
				_noise(0.05 / k, 0.14 / k, 3800.0 * k, 900.0 * k, 0.16, "bandpass", 2.0),
			]
		BOOM:
			# Low, long and dirty: a sub thump, a roar of lowpassed noise, and
			# grit settling after it.
			return [
				_tone(0.0, 0.55 / k, 90.0 * k, 28.0 * k, 0.55, "sine"),
				_noise(0.0, 0.70 / k, 1600.0 * k, 70.0 * k, 0.60),
				_noise(0.0, 0.08 / k, 4200.0 * k, 900.0 * k, 0.30, "bandpass", 1.2),
				_noise(0.25 / k, 0.90 / k, 2600.0 * k, 1200.0 * k, 0.06, "bandpass", 2.5),
			]
		TINK:
			# Metal on concrete.
			return [
				_tone(0.0, 0.07, 2300.0, 2100.0, 0.10, "triangle"),
				_noise(0.0, 0.03, 3600.0, 2000.0, 0.10, "bandpass", 3.0),
			]
		THROW:
			# The pin, then the arm: a click and a short whoosh.
			return [
				_noise(0.0, 0.02, 3000.0, 2400.0, 0.16, "bandpass", 4.0),
				_noise(0.05, 0.16, 500.0, 1800.0, 0.12, "bandpass", 1.5),
			]
		RADIO_KEY:
			# Key-up: a click, then an open squelch. Narrow band, because it is
			# coming out of a radio speaker, not a throat.
			return [
				_tone(0.0, 0.02, 1800.0, 1700.0, 0.10, "square"),
				_noise(0.02, 0.16, 2400.0, 1900.0, 0.12, "bandpass", 1.6),
			]
		RADIO_SENT:
			# The call is through: two rising chirps over a thin hiss. It is
			# the sound of backup being on its way, so it must not be missable.
			return [
				_noise(0.0, 0.24, 2600.0, 2200.0, 0.05, "bandpass", 1.2),
				_tone(0.02, 0.07, 1300.0, 1400.0, 0.13, "triangle"),
				_tone(0.12, 0.08, 1800.0, 1950.0, 0.13, "triangle"),
			]
		RADIO_CUT:
			# Static that stops dead, and a low drop under it: the call did not
			# finish.
			return [
				_noise(0.0, 0.14, 3200.0, 2600.0, 0.18, "bandpass", 0.9),
				_tone(0.0, 0.14, 420.0, 160.0, 0.10, "square"),
			]
		COMPROMISED:
			# Two falling klaxon swells. The floor is hunting you now, for the
			# rest of the run.
			return [
				_tone(0.0, 0.34, 460.0, 330.0, 0.13, "square"),
				_tone(0.40, 0.34, 460.0, 330.0, 0.13, "square"),
				_tone(0.0, 0.74, 115.0, 82.0, 0.10, "sine"),
			]
		BODYFOUND:
			return [
				_tone(0.0, 0.30, 260.0, 180.0, 0.26, "square"),
				_tone(0.26, 0.45, 180.0, 120.0, 0.24, "square"),
			]
	return []


func _noise(at: float, dur: float, f0: float, f1: float, gain: float,
		filter_type: String = "lowpass", q: float = 1.0) -> Dictionary:
	return {"kind": "noise", "at": at, "dur": dur, "f0": maxf(50.0, f0),
		"f1": maxf(50.0, f1), "gain": gain, "filter": filter_type, "q": q}


func _tone(at: float, dur: float, f0: float, f1: float, gain: float,
		wave: String) -> Dictionary:
	return {"kind": "tone", "at": at, "dur": dur, "f0": maxf(20.0, f0),
		"f1": maxf(20.0, f1), "gain": gain, "wave": wave}


func _render(layers: Array) -> AudioStreamWAV:
	if layers.is_empty():
		return null

	var total: float = 0.0
	for l in layers:
		total = maxf(total, l["at"] + l["dur"] + 0.03)
	var n: int = int(total * SR) + 1
	var buf := PackedFloat32Array()
	buf.resize(n)

	for l in layers:
		if l["kind"] == "noise":
			_render_noise(buf, l)
		else:
			_render_tone(buf, l)

	return _to_wav(buf)


## White noise through a Chamberlin state-variable filter whose cutoff sweeps
## exponentially f0 -> f1, with an exponential gain envelope. The SVF is stable
## while cutoff stays below SR/6 (7350 Hz); the highest cutoff in the table is
## GLASS's 7000 Hz, which is why this renders at 44100 rather than a cheaper
## rate. A new recipe must stay under that ceiling.
func _render_noise(buf: PackedFloat32Array, l: Dictionary) -> void:
	var start: int = int(l["at"] * SR)
	var count: int = int(l["dur"] * SR)
	if count <= 0:
		return

	var f0: float = l["f0"]
	var f1: float = l["f1"]
	var gain: float = l["gain"]
	var freq_ratio: float = f1 / f0
	var env_ratio: float = ENV_FLOOR / gain
	var band_pass: bool = l["filter"] == "bandpass"
	var damp: float = clampf(1.0 / maxf(0.5, l["q"]), 0.0, 1.9)

	var low: float = 0.0
	var band: float = 0.0
	var n: int = buf.size()

	for i in range(count):
		var idx: int = start + i
		if idx >= n:
			break
		var t: float = float(i) / float(count)
		var fc: float = f0 * pow(freq_ratio, t)
		var f: float = clampf(2.0 * sin(PI * fc / SR), 0.0, 1.0)
		var env: float = gain * pow(env_ratio, t)

		var input: float = _rng.randf_range(-1.0, 1.0)
		low += f * band
		var high: float = input - low - damp * band
		band += f * high

		buf[idx] += (band if band_pass else low) * env


func _render_tone(buf: PackedFloat32Array, l: Dictionary) -> void:
	var start: int = int(l["at"] * SR)
	var count: int = int(l["dur"] * SR)
	if count <= 0:
		return

	var f0: float = l["f0"]
	var f1: float = l["f1"]
	var gain: float = l["gain"]
	var freq_ratio: float = f1 / f0
	var env_ratio: float = ENV_FLOOR / gain
	var wave: String = l["wave"]

	var phase: float = 0.0
	var n: int = buf.size()

	for i in range(count):
		var idx: int = start + i
		if idx >= n:
			break
		var t: float = float(i) / float(count)
		var fc: float = f0 * pow(freq_ratio, t)
		phase = fposmod(phase + fc / SR, 1.0)
		var env: float = gain * pow(env_ratio, t)

		var s: float
		match wave:
			"square":
				s = 1.0 if phase < 0.5 else -1.0
			"sawtooth":
				s = phase * 2.0 - 1.0
			"triangle":
				s = 1.0 - 4.0 * absf(phase - 0.5)
			_:
				s = sin(phase * TAU)

		buf[idx] += s * env


func _to_wav(buf: PackedFloat32Array) -> AudioStreamWAV:
	var n: int = buf.size()
	var bytes := PackedByteArray()
	bytes.resize(n * 2)

	for i in range(n):
		var v: int = int(clampf(buf[i], -1.0, 1.0) * 32767.0)
		var u: int = v & 0xFFFF
		bytes[i * 2] = u & 0xFF
		bytes[i * 2 + 1] = (u >> 8) & 0xFF

	var wav := AudioStreamWAV.new()
	wav.format = AudioStreamWAV.FORMAT_16_BITS
	wav.mix_rate = SR
	wav.stereo = false
	wav.data = bytes
	return wav
