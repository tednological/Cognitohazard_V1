# Cognitohazard — Dialogue System Foundations
### Radial wheel, NPC simulation, and the baked reaction table

---

## 0. Design contract

Every choice below is checked against these rules:

- **Deterministic.** The same state plus the same input gives the same outcome. Resolution uses integer math only, with no floats and no runtime models, so the replay-hash test covers dialogue.
- **Legible.** A player who has the intel can predict an NPC's reaction. Randomness never decides an outcome; it only picks which prose variant plays.
- **Depth comes from verb × object × register, not verb count.** There are 8 verbs, fixed forever. Depth grows by adding objects, roles, and events.
- **The model drafts; the table decides.** Laya (or an LLM teacher) proposes table rows offline. A human reviews them. The shipped game reads only the baked table.
- **Information is the currency.** Knowing how an NPC will react is itself intel the player earns.

---

## 1. The radial wheel

### 1.1 The eight verbs

Verbs sit in fixed compass positions. Opposite positions have opposite effects, so a player's thumb learns the social geometry.

| Pos | Verb | What it does | Takes an object? | Opposite |
|---|---|---|---|---|
| N | **Cite** | Invoke regulation or authority ("Per Directive 14…") | Regulation / credential | Defer |
| S | **Defer** | Comply, agree, stall; lowers the temperature | No | Cite |
| E | **Present** | Show a record as evidence | Record | Deceive |
| W | **Deceive** | Assert a false claim (identity, fact, order) | Claim | Present |
| NE | **Coerce** | Apply leverage | Leverage item | Appeal |
| SW | **Appeal** | Rapport: shared duty, fear, sympathy | Motive | Coerce |
| SE | **Request** | Ask for access, a record, or a person | Target | Withdraw |
| NW | **Withdraw** | Exit the conversation, with or without a pretext | Optional pretext | Request |

The axes carry meaning:

- **N–S** is authority versus submission.
- **E–W** is truth versus falsehood.
- **NE–SW** is pressure versus rapport.
- **SE–NW** is engaging versus leaving.

### 1.2 Sub-wheel: objects

- Choosing a verb that takes an object opens a sub-wheel of at most 8 objects, filtered by the sim for relevance to this NPC.
- If more than 8 qualify, the sub-wheel pages. The sort order is deterministic: relevance score, then record ID.
- Objects come from the player's inventory of records, credentials, leverage, and learned claims. The wheel never offers something the player doesn't hold.

### 1.3 Register: a modifier, not more slots

- There are three registers: **Procedural** (by the book), **Familiar** (personal, informal) and **Menacing** (implied consequence).
- The player selects register with a held modifier or shoulder button while the wheel is open. The register defaults to the player's current cover identity.
- Register multiplies depth without adding slots. The combinations are 8 verbs × objects × 3 registers.

### 1.4 Time

- The wheel does not pause the game. While it's open, the world runs at dilated speed and drains the dilation meter.
- Burning a held record buys conversation time, using the existing degradation ladder (intact → degraded → destroyed, newest first).
- This creates a core tension. The record you burn to think longer may be the one you needed to Present.

---

## 2. The NPC model

Each NPC is split into static identity, which is authored per NPC, and dynamic state, which the sim changes.

### 2.1 Static identity

| Field | Type | Purpose |
|---|---|---|
| `role` | enum | Institutional position. Sets authority, knowledge and verification ability. **Keys the reaction table.** |
| `temperament` | enum | Personality archetype. Applies multipliers to table output. |
| `authority_tier` | int 0–4 | Compared against the player's credential tier |
| `loyalty` | int 0–100 | To the institution versus self-interest. Scales Coerce and Appeal effects. |
| `knowledge` | tag set | What the NPC can check a claim against (faces, regulations, schedules) |
| `can_verify` | bool | Has terminal access, so can schedule deferred verification |
| `armed` | bool | Gates the Hostile outcome |
| `baseline` | state vector | The resting values the dynamic state decays toward |

### 2.2 Dynamic state (the emotional values)

Four integer axes, each 0–100:

| Axis | Meaning | Rises from | Falls from |
|---|---|---|---|
| **Suspicion** | Belief that the player is not who they claim | Inconsistent claims, failed verification, alarms | Consistent cover, Defer, time |
| **Fear** | Threat to self, position or safety | Coerce, Menacing register, incriminating records, breaches | Appeal, time, all-clear events |
| **Stress** | Load from the environment; amplifies other deltas | Global events, alarms, audits | Shift change, time |
| **Rapport** | Warmth toward the player's *current identity* | Appeal, Familiar register, successful Requests | Coerce, exposure |

Two design notes:

- **Stress is an amplifier, not a direct driver.** Fear and Suspicion changes scale by `(100 + Stress) / 100`, rounded down. That's how global events change conversations without a table row per event.
- **Rapport belongs to the identity, not the player.** If a cover burns, Rapport built under it collapses, and a new identity starts at baseline.

### 2.3 Roles (starting set of 6)

| Role | Tier | Can verify | Armed | Respects |
|---|---|---|---|---|
| Clerk | 1 | Yes | No | Procedural citations, paperwork |
| Guard | 1 | No | Yes | Credentials, chain of command |
| Technician | 1 | Partial | No | Safety and containment protocol |
| Supervisor | 2 | Yes | No | Directives, audits |
| Auditor | 3 | Yes | No | Admissible evidence only |
| Containment Specialist | 2 | Yes | Yes | Containment directives above everything |

### 2.4 Temperaments (starting set of 6)

These apply as multipliers on the table's deltas and outcome thresholds.

| Temperament | Suspicion × | Fear × | Rapport × | Special rule |
|---|---|---|---|---|
| Zealot | 1.5 | 0.75 | 0.5 | Escalation threshold −15 |
| Careerist | 1.0 | 1.5 | 1.0 | Authority-gap bonus doubled |
| Burnout | 0.5 | 0.5 | 1.25 | Defer resets Suspicion by an extra −10 |
| Idealist | 1.0 | 1.0 | 1.5 | Coerce inverts its Rapport delta, doubled |
| Paranoid | 1.25 | 1.25 | 0.75 | Suspicion decay halved |
| Opportunist | 1.0 | 0.75 | 0.75 | Coerce Fear ×2; Appeal ignored unless leverage is held |

Multipliers are stored as integer percentages (150, 75…) so the math stays integer.

---

## 3. Records: the hybrid model

A record has two layers, and they must agree.

- **Prose body.** What the player reads: a memo, a personnel file, an incident report, a requisition. It's written for humans and carries the deduction.
- **Tag set.** What the sim reads:

| Tag | Example | Used for |
|---|---|---|
| `class` | `incident_report` | Which roles care |
| `implicates:<entity>` | `implicates:npc_director_vance` | Relation class at runtime |
| `authorizes:<zone/tier>` | `authorizes:sublevel_3` | Access Requests and Cite |
| `admissible:<tier>` | `admissible:3` | Whether an Auditor accepts it |
| `classification` | `2` | Who is allowed to see it (showing it can itself be a violation) |
| `integrity` | `intact / degraded` | Degraded records drop admissibility by one tier and trigger Verify more often |
| `forgery` | `true/false` | Whether verification exposes it |

### 3.1 Relation classes

This keeps the table bounded. The table is never keyed on a specific record. At runtime, the sim computes the record's **relation to this NPC** from its tags:

| Relation class | Condition |
|---|---|
| `implicates_self` | Implicates this NPC |
| `implicates_superior` | Implicates someone above them in the chain |
| `implicates_peer_rival` | Implicates a peer or rival |
| `authorizes_player` | Grants the player's current identity access this NPC controls |
| `inadmissible` | Admissibility tier below the NPC's authority tier |
| `irrelevant` | None of the above |

The same approach applies to the other verbs' objects. A Claim is classed as `consistent_with_knowledge`, `unverifiable_now`, or `contradicts_knowledge`. A Leverage item is classed as `personal`, `professional`, or `institutional`.

### 3.2 Keeping prose and tags consistent

- Every tag must be supported by text the player can read. If a record is tagged `implicates:vance`, the prose names Vance or gives a traceable identifier (badge number, room, signature).
- **Offline QA:** a classifier pass (Laya or an LLM judge) asks typed questions per tag: "Does this text support `implicates:vance`?" and "Does it state facts not present in the tag set?" Failures go back to the writer.
- **In-game:** once a record has been read, the phrases that back its tags get a subtle highlight. The player can see why the sim thinks the record implicates someone.

---

## 4. Global events

Events change NPC state in bulk, by role and zone, through a single event bus.

### 4.1 Event schema

```
Event {
  id, scope: {site | zone | role-set}, origin_zone,
  role_deltas: { role -> Δ(Susp, Fear, Stress, Rapport) },
  regulation_overrides: [ e.g. "access_permits_suspended" ],
  propagation_delay_ticks_per_zone,
  duration_ticks, decay_curve
}
```

### 4.2 Starting event set

| Event | Main effect | Regulation change |
|---|---|---|
| Floor alarm (levels 1–3) | Stress +10/+25/+40, Suspicion +5/+15/+30 for Guards | Level 3: Request for access auto-refused |
| Body discovered | Fear +20 in zone, Suspicion +20 for Guards, Supervisors | Identity checks mandatory |
| Audit announced | Careerist and Supervisor Fear +25; Auditors gain effective authority +1 | Records carrying `admissible` are weighted up |
| Containment breach | Stress +40 site-wide; Containment Specialists ignore non-containment Cites | Most Cites invalid except containment directives |
| Lockdown | Every Request refused; Withdraw raises Suspicion | Exits sealed |
| Shift change | New NPCs arrive at baseline; Stress −20 | Conversation memory lost for departed NPCs |
| Purge order | Fear +30 for Clerks; records of a class become dangerous to hold | Presenting that class escalates |

### 4.3 Propagation and decay

- Events spread zone to zone with a delay. This is the propagating alarm state that replaces the global detection boolean. The player can outrun news.
- Dynamic state decays toward baseline each N ticks with integer steps. The Paranoid temperament halves Suspicion decay.
- **Integration with the stealth AI:** when a conversation ends, Suspicion bands map onto the five existing AI states:

| Suspicion | Stealth state after dialogue |
|---|---|
| 0–39 | Patrol/Sentry |
| 40–59 | Curious |
| 60–79 | Search |
| 80–89 | Hunt |
| 90+ | Engage |

---

## 5. The reaction table

### 5.1 Key and size

- **Key:** `verb × object_relation_class × register × role`
- **Size:** 8 verbs × about 4 relation classes on average × 3 registers × 6 roles ≈ **576 rows**. One designer can review that.
- Temperament and Stress are deliberately **not** in the key. They're applied as multipliers afterward. Putting them in the key would push the table past 20,000 rows and make it unreviewable.

### 5.2 Row schema

| Column | Type | Meaning |
|---|---|---|
| `d_susp, d_fear, d_stress, d_rapport` | int | Base deltas |
| `authority_weight` | int % | How much the player's tier over the NPC's tier matters for this action |
| `verify_trigger` | bool | Whether this action can make the NPC schedule a check |
| `escalate_spike` | int | A Suspicion delta at or above this forces Escalate regardless of total |
| `tell` | enum | Animation/bark key that telegraphs the reaction |
| `line_bucket` | key | Links to the authored prose pool |

### 5.3 Resolution pipeline (pseudocode)

```
resolve(npc, verb, object, register, world):
  rel   = classify_relation(object, npc)          # §3.1, tag logic only
  row   = TABLE[verb][rel][register][npc.role]
  t     = TEMPERAMENT[npc.temperament]

  # 1. Temperament multipliers (integer %)
  dS = row.d_susp   * t.susp_pct   / 100
  dF = row.d_fear   * t.fear_pct   / 100
  dR = row.d_rapport* t.rapp_pct   / 100
  dT = row.d_stress

  # 2. Stress amplification
  amp = 100 + npc.stress
  dS = dS * amp / 100
  dF = dF * amp / 100

  # 3. Apply and clamp 0..100
  npc.state += (dS, dF, dT, dR)

  # 4. Outcome: first rule that matches, in order
  if npc.susp >= 90 and npc.armed:                 return HOSTILE
  if npc.susp >= 70 - t.escalate_mod
     or dS >= row.escalate_spike:                  return ESCALATE
  if row.verify_trigger and npc.can_verify
     and npc.susp >= 40:                           schedule_verify(npc, claim_or_record)
  gap   = player.identity_tier - npc.authority_tier
  score = npc.fear*2 + npc.rapport*2
        + gap * row.authority_weight
        - npc.susp*3
  if score >= t.comply_threshold:                  return COMPLY
  return STALL
```

The outcomes are `COMPLY`, `STALL`, `ESCALATE` and `HOSTILE`. Verification can be attached to any of them.

### 5.4 Deferred verification (cover half-life)

- `schedule_verify` queues a check event at `now + base_delay / (susp_band + 1)`. Higher Suspicion means a faster check.
- When the check fires, the sim compares the claim or record against ground truth. A forgery, a false identity, or a degraded record below the needed admissibility produces a Suspicion spike (+40) and an alarm event that starts in that NPC's zone.
- This is the cover half-life. Lies work now and get audited later. The player's clock is visible if they have the intel (see §6).

### 5.5 Worked example

**Setup:**
- The player's cover is Auditor (tier 3), and an Audit Announced event is active (Clerk Stress 60).
- The NPC is a Records Clerk (tier 1), temperament Careerist, starting state Susp 20, Fear 30, Rapport 30.

**Action:** Present an intact incident report tagged `implicates:supervisor_ohm` (Ohm is the Clerk's supervisor), in the Procedural register.

**Resolution:**

1. The relation class is `implicates_superior`.
2. The table row gives base deltas dS +5, dF +20, dR +5, authority_weight 15.
3. Careerist multiplies Fear by 1.5, so dF becomes 30.
4. Stress 60 gives an amplifier of 160, so dS becomes 8 and dF becomes 48.
5. The new state is Susp 28, Fear 78, Rapport 35.
6. No escalation (Susp 28 is below 70). Verify doesn't trigger because Susp is below 40.
7. The authority gap is 2, but Careerist doubles its bonus: 2 × 15 × 2 = 60.
8. Score = 78×2 + 35×2 + 60 − 28×3 = 156 + 70 + 60 − 84 = **202**. Careerist's comply threshold is 150, so the outcome is **COMPLY**.

**What the player sees:** the Clerk hands over the sublevel key and the prose plays from `present.implicates_superior.procedural.clerk.comply`.

**The same move, one change at a time:**
- **Menacing register:** a higher Suspicion delta pushes the score under threshold. The outcome is STALL, and Verify is queued.
- **Degraded record:** admissibility drops. The Clerk complies, but queues Verify, so the cover now has a clock.
- **Zealot Clerk:** escalation threshold −15 and Fear ×0.75. The outcome is STALL, trending toward ESCALATE if pushed again.

---

## 6. Legibility as intel

This is what makes the table part of the game, not just backend data.

- The reaction table is fixed and learnable. Players will learn its broad shape through play, which is intended.
- **Personnel files reveal temperament.** Once the player has read an NPC's file, the wheel shows predicted reaction arrows on each verb (Suspicion up/down, Fear up/down) derived from the baked row plus multipliers.
- **Without intel, the wheel shows nothing.** Talking to strangers is a gamble; talking to someone you've researched is a plan. This turns the dialogue system into a consumer of the information economy.
- **Tells** (fidgeting, glancing at the phone, a hand moving to the holster) telegraph state bands during the conversation, so observant players get partial reads without the file.

---

## 7. Baking the table

### 7.1 Pipeline

1. **Enumerate** every key (about 576).
2. **Draft:** for each key, build a text description from the role dossier, the verb, the object class and the register. Ask Laya (fine-tuned) or an LLM teacher typed questions:
   - `score`: how much does this raise the NPC's Suspicion? (none / slight / moderate / strong / severe → maps to 0 / 5 / 15 / 30 / 50)
   - the same for Fear, Rapport, Stress
   - `noul`: would this NPC want to check the claim later? → `verify_trigger`
   - `choice`: which tell fits?
3. **Review:** a designer edits every row. The model is a drafting aid, not an authority.
4. **Export** to a Godot `Resource` or CSV that the sim loads at start.
5. **CI checks** (headless, Node or C# rig):
   - Coverage: every key has a row.
   - Invariants, for example: Coerce never lowers Fear; Menacing never gives more Rapport than Familiar for the same key; Defer never raises Suspicion except under Lockdown.
   - A scenario suite with replay hashes. Changing one row must show which scenarios changed outcome.

### 7.2 Model caveats (from the earlier analysis)

- Laya's base checkpoints are near chance at zero-shot typed decisions. Expect to fine-tune on a few hundred hand-labeled rows first, or use an LLM teacher and treat Laya as optional.
- Its ordinal `score` type is its weakest. Mapping to 5 coarse bins makes this less of a problem.
- It ships overconfident. Its confidence can flag rows for extra review, but don't use it as a probability.

---

## 8. Where this breaks: the strongest counterarguments

| Risk | Why it's real | Mitigation |
|---|---|---|
| **Dominant verb** | If one verb, like Defer, is always safe, players spam it and depth collapses | Every verb has a cost: Defer burns dilation time and gains nothing; Withdraw during Lockdown raises Suspicion. CI measures outcome distribution per verb across scenarios. |
| **Redundant axes** | Fear and Stress may move together in practice, which makes one of them dead weight | Log axis correlation in the headless suite. If r > 0.85 across scenarios, merge them. |
| **Table rigidity** | 576 rows is manageable; a 12th role or a 4th register pushes toward 1,500 | Add roles only if they change *what an NPC knows or can verify*, not just flavor. Flavor goes in temperament. |
| **Prose and tag mismatch** | Players read a record as implicating someone the tags don't, then feel cheated | Highlight backing phrases (§3.2); QA pass on every record. |
| **Eight verbs is a lot early** | New players face a full wheel with no intel | Gate verbs by credential/cover. Coerce needs leverage held; Cite needs a known regulation. Empty slots gray out. |
| **Predictability kills tension** | A fully legible system can feel solved | Hidden state is the uncertainty: an unresearched NPC's temperament, pending Verify events, events propagating from other zones. The rules stay fixed; what the player knows changes. |
| **Real-time wheel pressure** | Dilated time and reading prose conflict with accessibility | Offer an assist mode with a full pause. Balance the default around dilation. |

---

## 9. Build order (slots into the existing sim milestones)

This work comes after the entity registry and prop store milestones.

1. **Schemas.** NPC static/dynamic data, record tags, event schema, table row format. Integer-only.
2. **Resolution core.** `resolve()` with a hand-written table for **one role** (Clerk) and all 8 verbs. Headless tests plus the replay hash.
3. **Event bus.** Three events (Alarm, Audit Announced, Shift Change) with propagation and decay. Hook Suspicion bands into the five-state stealth AI.
4. **Deferred verification.** Check scheduling and cover half-life.
5. **Baking pipeline.** Drafting → review → export → CI invariants. Fill in the remaining 5 roles.
6. **Wheel UI in the HTML testbed.** The 8-slot wheel, object sub-wheel, register modifier, dilation drain and intel arrows. Test the feel there before porting to Godot.
