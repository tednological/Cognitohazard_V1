# Cognitohazard — RPG Extension Plan

**Status:** milestones 7, 8 and 9 complete and green. 10 and 11 are BUILT in
the form CLAUDE.md describes, which departs from this plan: nine worn slots,
of which only the weapons, vest and backpack change a stat (the apparel is
cosmetic until sim/ has a reader for it), and a campaign of contract pay, a
shop, a stash and mission select (`cognitohazard_campaign_plan.md`). §1's
SimLint rule keeping campaign state out of sim/ is in `tests/SimLint.cs`.
Milestone 12 (the balance pass) is a measurement, and has not been done.
**Relationship to `cognitohazard_port_spec.md`:** this document extends it. Where
the two disagree, the amendments in §8 below are authoritative and the port spec
is to be edited to match. Everything the port spec says about *architecture*
(§1.1 layer split, §3.3 sim purity, §4 determinism) still binds completely.

**What this adds:** meta-progression across missions — health and armour,
four weapon archetypes, seven gear slots, and a campaign layer that pays for
them.

---

## 0. Decisions already taken

These were decided by the project owner and are not open for re-litigation
without a new decision.

| Decision | Choice | Consequence |
|---|---|---|
| Survivability model | **Health pool + armour pool** | Port spec §7.1's "no health, no armour" is void. §8 amends it. |
| Lethality target | 1–2 shots unarmoured, 4–5 armoured | Drives every number in §3. |
| Progression currency | **Contract pay**, not records | Records keep their current meaning exactly. No §6.3 deviation, no filing desk. |
| Genre target | **Extraction shooter** | Long time-in-level. High survivability is the goal, not a balance failure. Resolves risks 9.1 and 9.2 in favour of the player living longer. |

The earlier "absorb plates" proposal is superseded by the health pool in §3,
which generalises it: plates were a pool of N free hits; this is a pool of N
damage points, which lets weapons differ by damage.

---

## 1. Architecture — the part that is not negotiable

**`sim/` consumes a resolved `Loadout`. A campaign layer produces one.**

```
meta   game/campaign.gd     inventory · pay · unlocks · save file · shop UI
          │ produces
          ▼
data   sim/Loadout.cs       plain integer structs, hashed, embedded in replays
          │ consumed by
          ▼
sim    sim/SimWorld         reads weapon and gear stats instead of Tune.*
```

Persistent inventory, pay balances, unlock state and save files live **outside
`sim/`**. If the sim reads campaign state directly it acquires a dependency on a
save file, and a replay stops being reproducible on another machine — which
would cost us the replay divergence detection that already exists and works.

`SimLint` gains a rule at milestone 11 enforcing this: no `sim/` file may
reference campaign, inventory, save or unlock concepts.

### What becomes loadout-driven

The weapon path is tightly localised today — one `Fire()` method and
`StepPlayer()`, about twenty `Tune.*` references, all in `sim/SimWorld.cs`. Those
become lookups into the active `Loadout`. The default loadout ships with today's
exact constants, so **default play is bit-identical except for the state hash**,
which changes because the loadout is folded into it.

---

## 2. Health and armour

### Model

Two integer pools. Damage lands on armour first, one-for-one, and the remainder
carries into health. No division, no percentages, no float — this feeds the
state hash and must stay exact.

```
effective_hp = health + armour

damage D arrives:
    absorbed = min(D, armour)
    armour  -= absorbed
    health  -= (D - absorbed)
    dead when health <= 0
```

- **Base health is 100** for the player and for every guard. It is not gear.
- **Armour comes from the torso slot** and is spent permanently for the mission.
- **Armour refills free between missions.** It is durable gear, not ammunition;
  progression means *better armour*, not *restocking*. A repair cost is a later
  dial if the economy needs a sink.

### Armour tiers

| Tier | Armour | Effective HP | Walk speed | Footstep radius |
|---|---|---|---|---|
| None | 0 | 100 | 196 px/s | 170 px |
| Light weave | 50 | 150 | 196 px/s | 170 px |
| Medium carrier | 100 | 200 | 180 px/s | 195 px |
| Heavy plate | 150 | 250 | 160 px/s | 240 px |

Heavier armour costs mobility and makes you louder. Both feed systems that
already exist: `SpeedWalk` and `NoiseWalkRadius` in §8.3's hearing model. Armour
therefore trades survivability against detectability, which keeps the stealth
layer meaningful instead of letting armour strictly dominate.

### Taking a hit is loud

Surviving a shot must not be free. Any damage taken by the player:

- raises the floor alarm to 2 (same as a gunshot, §8.3)
- applies hitstop and heavy shake
- emits a distinct audible event

Armour reaching zero emits a second, louder event. Getting shot and walking away
should read as a disaster survived, not a resource ticking down.

---

## 3. Weapons

Base health 100; shots-to-kill shown against each armour tier.

| Weapon | Damage | Mag | Cadence | Spread | **Gunshot radius** | none | Light | Medium | Heavy |
|---|---|---|---|---|---|---|---|---|---|
| **Pistol** | 55 | 8 | 0.17 s | tight | **400 px** | 2 | 3 | 4 | 5 |
| **SMG** | 50 | 30 | 0.07 s | wide, heat climbs fast | 700 px | 2 | 3 | 4 | 5 |
| **Rifle** | 100 | 5 | 0.55 s | very tight, fast round | 800 px | 1 | 2 | 2 | 3 |
| **Shotgun** | 22 × 7 pellets | 2 | 0.90 s | very wide cone | **950 px** | 1 | 1 | 2 | 2 |
| *Guard rifle* | 50 | — | 0.80 s | ±0.045 rad | 640 px | 2 | 3 | 4 | 5 |

Guards use the guard rifle, so **the player dies in 2 shots unarmoured and 5 in
heavy plate** — the brief, exactly.

Three things worth noticing about this table:

**Gunshot radius is currently a flat 640 px for everything** (`Tune.GunRange`).
Making it per-weapon is what turns weapon choice into a stealth decision: the
pistol is the quiet weapon, not the weak one. A pistol kill wakes a quarter of
the floor a shotgun kill does.

**The shotgun needs no damage-falloff curve.** Its 154 damage assumes all seven
pellets connect, which only happens at point-blank; at range the cone spreads and
one or two pellets land for 22–44. The falloff emerges from the pellet spread
that already has to be implemented. This is the only weapon needing a real change
to `Fire()` — multi-projectile emission.

**SMG and pistol have near-identical shots-to-kill.** They are separated by
cadence, accuracy, heat growth and noise, not lethality. The SMG is the weapon
you use because you have already been caught; the pistol is the one you use
because you have not.

---

## 3A. Weapon system (expanded)

Supersedes the four generic archetypes in §3. Five named weapons, each with
attachment slots. The §3 damage figures and shots-to-kill table still govern;
this section says which weapon carries them and how attachments move them.

### Weapons

Ordinals are part of the replay format and the state hash. **Append, never
reorder or reuse.**

| id | Weapon | Class | Damage | Mag | Cadence | Gunshot radius | Character |
|---|---|---|---|---|---|---|---|
| 0 | **Glock** | Pistol | 55 | 17 | 0.17 s | 400 px | quiet, accurate, the stealth default |
<!-- AK dropped from 100 to 70 during implementation: at 100 it one-shot every
     unarmoured target while carrying 30 rounds at 7/s, which made every other
     weapon pointless. 70 gives 2 unarmoured / 4 through heavy plate. -->
| 1 | **MP7** | SMG | 50 | 40 | 0.07 s | 700 px | fast and wide; the "already caught" weapon |
| 2 | **AK-47** | Assault rifle | **70** | 30 | 0.14 s | 800 px | punchy, heavy recoil growth |
| 3 | **Remington** | Shotgun | 22 × 7 | 8 | 0.90 s | 950 px | ~560 px reach, emergent falloff |
| 4 | **SAW** | Machine gun | 45 | 200 | 0.055 s | 1000 px | belt-fed, slow to carry, loudest thing on the level |

### Attachment slots

| Slot | Options | Moves |
|---|---|---|
| **Sight** | irons · red dot · holo · scope | `SpreadBase` down; scope also narrows at range |
| **Grip** | none · rubber · tactical · angled | `SpreadPerHeat` — how fast accuracy decays under fire |
| **Rail** | none · laser · flashlight · foregrip | laser → `SpreadBase`; flashlight → vision radius **and enemy detection of you**; foregrip → `HeatPerShot` |
| **Magazine** | standard · extended · drum · quick-release | `Magazine` up, `ReloadTicks` up; quick-release trades capacity for speed |
| **Ammo** | standard · subsonic · hollow point · AP | `Damage`, `GunshotRadius`, `BulletSpeed` |
| **Stock** *(optional)* | none · light · heavy | `SpreadPerHeat` down, movement speed down |

Not every weapon takes every slot — each weapon carries a **slot mask**. The
Glock has no stock and no grip; the SAW's grip is an integral bipod.

### The two attachments that matter most

**Subsonic ammo** cuts `GunshotRadius` hard and costs damage. It is the only way
to fire without waking the floor, and it directly buys the stealth axis the whole
weapon table is built on.

**The flashlight** extends the player's visibility polygon *and* makes the player
easier for guards to see — a bonus to the `q` term in §8.2's stimulus. A genuine
two-sided trade in a game where information is the resource, and it plugs into
perception maths that already exists.

### Resolution rules

Same discipline as §4: **additive integer deltas onto the base spec, applied in
fixed slot order, then clamped.** No multiplication, no float, no percentages.
`Loadout` grows from one id to seven; all seven are hashed and all seven travel
in the replay:

```
loadout: weapon=2 sight=1 grip=3 rail=1 mag=2 ammo=1 stock=2
```

An unknown id in any slot resolves to "none", the way the level parser is total.

### Customisation menu

A screen in `game/`, built like the level editor: pick a weapon, cycle each slot,
see the **resolved** stats live next to the base ones so the trade is visible at
the moment of choosing. Attachments owned come from the campaign layer (§11); the
menu never touches `sim/`.

### Milestone placement

This replaces milestone 9. It depends on milestone 8, because ammo types are a
damage mechanic and shipping them before damage exists would mean building the
interesting half of the system twice.

---

## 4. Gear slots

| Slot | Modifies | Existing system it plugs into |
|---|---|---|
| **Primary** | weapon spec (§3) | `Fire()` |
| **Torso** | armour pool, speed, footstep radius | §2, §8.3 hearing |
| **Boots** | walk/sneak speed, footstep radius | `SpeedWalk`, `SpeedSneak`, `NoiseWalkRadius` |
| **Optics** | visibility polygon radius (now 430 px), awareness readouts | §9 presentation |
| **Gloves** | reload time, subdue reach and arc | `ReloadTicks`, `SubdueReach`, `SubdueRearArc` |
| **Pack** | record capacity, cache dwell time | §2.3, `CacheDwellTicks` |
| **Implant** | burn ladder — stage frames, jolt duration | §6.1 parasite |

All gear effects are **additive integer deltas applied to a base spec, then
clamped**. No multiplicative stacking, no float. Two items modifying the same
stat resolve in slot order, which is fixed.

`RecordStore.Held` is currently **unbounded**. A Pack capacity would turn the
record economy into a real inventory decision, but it changes §2.3 semantics —
flagged as opt-in, not assumed.

---

## 5. Milestones

Each ends the way every milestone in this project ends: all harnesses green, a
report of anything that felt different, and an explicit list of any constant
there was a temptation to change.

| # | Milestone | Done when |
|---|---|---|
| **7** ✅ | **Loadout spine** | `Loadout` is hashed and embedded in replays; `Fire()`/`StepPlayer()` read specs not `Tune`; the default loadout reproduces today's numbers; goldens rebaked in the same commit; swapping a weapon makes a recorded replay diverge at the expected tick |
| **8** ✅ | **Health and armour** | pools implemented per §2; damage events in `EventLog`; taking a hit raises alarm 2 + hitstop + audio; armour break is distinct; HUD shows both pools; armour resets on restart |
| **9** ✅ | **Weapon system** (§3A) | five named weapons; six attachment slots with per-weapon masks; deltas resolve additively in fixed order; the shots-to-kill table verified cell by cell; subsonic provably alerts fewer guards than standard; customisation menu |
| **10** | **Gear slots** | slot and item tables; each item changes exactly the stat it claims and nothing else; stacking is order-deterministic; unknown item ids fall back safely, the way the level parser is total |
| **11** | **Campaign layer** | pay on completion, plus low-alarm and no-kill bonuses; inventory, unlocks, text save format; `SimLint` extended so `sim/` can never reference campaign state |
| **12** | **Balance pass** | §8.6 detection curve re-measured and still within 10%; time-to-kill measured per armour tier against §3; dilation usage measured (see §9.1); every constant worth changing reported and left unchanged |

---

## 6. Tests that must change

These currently encode assumptions this plan voids. They are to be **updated
deliberately at the milestone that breaks them**, never deleted:

| Test | File | Milestone | Becomes |
|---|---|---|---|
| `"one hit kills a guard"` | `tests/Systems.cs` | 8 ✅ | became `"a guard dies to pistol fire"`; two rounds, and the test now tracks its target because a survivor hunts |
| all four golden hashes | `tests/Goldens.cs` | 7, 8 ✅ | rebaked twice: loadout entering the hash, then health and armour |

**`§8.6`'s detection curve does not need changing.** It measures time-to-*engage*,
and the harness already pins the player alive during measurement, so it never
depended on one-shot lethality. Verified against `tests/Systems.cs`. The felt
difficulty of the game changes; that acceptance test does not.

---

## 7. What this plan does not add

Stated explicitly, in the spirit of port spec §0:

- No dialogue, factions, or the fact/proposition model.
- No filing desk. Records keep exactly their current meaning — fuel, score and
  loss condition. Degraded records remain "a smaller number" and the §6.3 design
  gap stays open.
- No enemy armour tiers at milestone 8. Guards get health, not armour. Armoured
  elites are a later decision.
- No damage falloff curves, no critical hits, no hit locations.
- No experience points or levels. Progression is **gear**, acquired with pay.

---

## 8. Port spec amendments

To be made as deliberate edits to `cognitohazard_port_spec.md` and `CLAUDE.md`
at milestone 8, not left as drift:

**§7.1** — replace:
> One hit kills the player. One hit kills a guard. No health, no armour.

with:
> Actors have a 100-point health pool. Armour is a second pool that absorbs
> damage first and is spent for the mission. Weapons carry damage values; see
> the RPG extension plan §3 for the shots-to-kill table.

**§0** — the "do not add systems, do not add hooks for later" prohibition gains
an explicit carve-out naming this document as sanctioned post-port work.

---

## 9. Risks

### 9.1 Health blunts the parasite — ACCEPTED

The dilation mechanic's value came from one bullet ending you. At five shots of
survivability, burning a record to slow time competes with simply absorbing the
mistake.

**Ruled on by the project owner: this is intended.** The target genre is an
extraction shooter, where the player spends a long time in the level and dying
quickly is a failure of design, not a feature. Survivability is the point.

The consequence to accept deliberately: dilation is demoted from a survival
mechanic to a tactical one. Milestone 12 should still *measure* dilation usage,
but a low number is no longer a defect to fix by lowering armour — it is a
signal that the parasite needs a reason to exist that is not "or you die", and
that is a design question for later, not a tuning one.

### 9.2 Time-to-kill against a 0.8 s guard cooldown — ACCEPTED

Five shots at one shot per 0.8 s is four seconds of standing in the open before
dying. Under the extraction-shooter framing this is the floor, not the ceiling —
if anything milestone 12 may find it too short rather than too spongy.

### 9.3 Shooting becomes more attractive than sneaking

Guards at 100 HP die to two pistol shots rather than one. That is two gunshot
events rather than one, so it is *louder* than before, which pushes back toward
stealth. But the player is also far harder to kill, which pushes the other way.
Net effect unknown until milestone 12.

### 9.4 Existing replays stop verifying at milestone 7

Every golden hash rebakes when the loadout enters the state hash. Recorded
replays from before milestone 7 will report `DIVERGED` — correct behaviour, and
exactly what the detector is for, but **archive anything worth keeping first**.

---

## 9A. Findings from implementation

Recorded as they were discovered; each wants a decision at the milestone named.

**Any gunshot raises the floor alarm, regardless of weapon — FIXED at
milestone 9.** The alarm is now radius-gated: it rises only if a guard was
actually within earshot. Without this, subsonic ammo would have been decorative,
because every shot woke the floor regardless of how quiet it was. This is a
deliberate departure from prototype parity.

Original finding:

`GunshotHeard` calls `Alarm.Raise(2)` unconditionally, before the radius check.
The weapon's gunshot radius controls only DIRECTED attention — who gets the
92-point spike and the last-known position. So "the pistol is quiet" means it
does not send guards to you; it does not mean nobody notices. If subsonic ammo
is to be genuinely stealthy, the alarm raise probably needs to become
radius-gated too.

**A guard that survives a round knows where it came from (milestone 8).**
`HurtGuard` routes a full gunshot stimulus at the shooter's position. This was
not in the plan; it is the obvious reading of being shot and not dying, but it
makes non-lethal hits much louder than they were.

**A replay shorter than the checkpoint interval used to verify vacuously
(milestone 7).** `Verify()` reported OK when it had compared nothing. It now
reports how many checkpoints it actually compared, the CLI calls zero
INCONCLUSIVE with exit code 2, and recordings get a closing checkpoint on the
tick the run ends.

---

## 10. Open questions

Not blocking, but they want answers before the milestone that needs them:

1. **Armour tier count and values** (§2) — the table is a starting point sized to
   hit the 1–2 / 4–5 brief. Risk 9.1 may force it down.
2. **Pack capacity** (§4) — opt-in. Does the record economy want an inventory
   limit?
3. **Enemy armour** (§7) — armoured elites, or do guards stay at flat 100 HP?
4. **Weapon unlock order** (§11) — which weapon does a new player start with, and
   what is the pay curve? Pistol is the obvious start: it is the quiet one, and
   it is the one the sim already implements.
