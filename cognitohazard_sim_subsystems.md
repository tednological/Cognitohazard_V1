# Cognitohazard: Supporting Simulation Subsystems
### Everything `resolve()` depends on, beyond the reaction table

*Companion to "Dialogue System Foundations." That report defines the wheel, the NPC state vector and the baked reaction table. This one defines the systems the table reads from and writes to.*

---

## 0. Rules every subsystem follows

- **Integer-only, tick-driven, deterministic.** No floats and no wall-clock time. All randomness is seeded from `(world_seed, entity_id, tick)`. Every subsystem is covered by the replay hash.
- **One tag vocabulary.** Records, facts, secrets, regulations and credentials all draw from one registry of tags. If two subsystems name the same thing differently, that's a bug.
- **No inference rules.** NPCs never derive C from A and B. Belief is set membership. Every subsystem that reasons about facts uses lookup, not logic.
- **Must-flip rule.** Each subsystem has at least one CI scenario where turning the subsystem off changes a dialogue outcome or a tell. If a subsystem can't flip an outcome, the player can't feel it, so it gets cut.
- **Player surface.** For each subsystem, name the verb, tell or UI element that exposes it. If it has none, the subsystem isn't finished.

---

## 1. Shared infrastructure (build first)

### 1.1 Tick scheduler

- A single priority queue keyed by `(tick, priority, entity_id, seq)`. The composite key makes ordering fully deterministic when two jobs land on the same tick.
- Every deferred behavior is a queued job: Verify checks, propagation hops, token expiry, schedule transitions, state decay.
- **Player surface:** none directly. This is the substrate the other subsystems run on.

### 1.2 Tag registry

- A single data file of every tag: `id`, `category` (fact / credential / regulation / secret / zone / role), `display_name`, `sensitivity`, and `negation_of` where one applies.
- Load-time validation fails the build on any unknown tag.
- **Why it matters:** the knowledge model, record tags, leverage and regulations all compare tags. A typo is a silent logic bug, so the registry catches it at load.

---

## 2. Knowledge / belief model

**Purpose:** gives `Deceive`, `Present` and Verify something to check against.

### Schema

```
Belief { fact_tag, source: {witnessed | told:<entity> | record:<id> | institutional}, tick_learned, confidence_band: 0..3 }
NPC.beliefs : Map<fact_tag, Belief>
```

### Behavior

- **Institutional beliefs** come from the NPC's role: a Guard knows the badge roster for their zone, a Clerk knows the filing schedule. These are authored once per role, not per NPC.
- **Witnessed** beliefs come from perception (the stealth AI's sight and hearing). They start at confidence band 3.
- **Told** beliefs come from dialogue and social propagation. Their confidence band depends on the teller's Rapport and the listener's Suspicion.
- **Claim classification** (used by the reaction table):
  - `contradicts_knowledge` if the NPC holds the negation tag with confidence ≥ 2.
  - `consistent_with_knowledge` if the NPC holds the fact itself.
  - `unverifiable_now` otherwise.
- **Contradictions accumulate.** A second contradiction under the same identity adds a flat Suspicion bonus. Lies compound.

### Player surface

- Whether `Deceive` works at all.
- Dossiers reveal selected beliefs ("knows Director Vance by sight").
- **Tell:** a pause or a double take when a claim is `unverifiable_now` instead of consistent.

### Must-flip scenario

Deceive with "I'm Vance's deputy" to (a) a Clerk who knows Vance's actual deputy and (b) a new hire. The outcomes must differ.

### Failure modes

- **Belief bloat:** hundreds of NPCs × hundreds of facts. Store institutional beliefs by reference to the role, not per NPC. Only per-NPC deltas get stored.
- **Negation:** each fact that can be lied about needs an explicit negation entry in the registry. Don't try to derive negation.

---

## 3. Org graph (chain of command)

**Purpose:** gives meaning to `implicates_superior`, escalation targets, override authority and leverage direction.

### Schema

```
OrgNode { npc_id, superior_id?, department_tag, rank }
Department { tag, head_id, zones[] }
```

### Behavior

- **Escalate** walks one edge up. It targets the superior's current location (from schedules), and the superior gets the escalation as a told belief.
- **Override:** an NPC can countermand a Comply token granted by someone at lower rank in the same department.
- **Relation classes** from the reaction report are computed by graph distance: `implicates_self`, `implicates_superior` (any ancestor), `implicates_peer_rival` (same parent, or a tagged rival).
- **Vacancies:** a superior who is removed (captured, killed, discredited) leaves the node empty. Escalations then go to the next ancestor, which takes longer. Removing a supervisor is therefore a strategy.

### Player surface

- Who arrives when things go wrong.
- **Leverage direction:** records that implicate a superior are the strongest Present plays.
- Org charts are a findable record class, and they fit the bureaucratic horror tone.

### Must-flip scenario

The same incriminating record presented to the implicated person's subordinate versus their peer produces different outcomes.

---

## 4. Identity / cover ledger

**Purpose:** Rapport, the authority gap and contradiction tracking all key on the player's *current identity*.

### Schema

```
Identity { id, name, role_claimed, tier, credentials[], status: {clean | questioned | burned}, burned_tick? }
ClaimLog : Map<(npc_id, identity_id), [ (claim_tag, tick) ]>
```

### Behavior

- **Switching identity** requires a credential item and a disguise change, and it takes time. Anyone who sees the switch gets the witnessed belief `identity_A == identity_B`, which burns both.
- **Self-contradiction:** each new claim is checked against the ClaimLog for that NPC and identity. Contradicting yourself costs more Suspicion than contradicting the NPC's knowledge. Liars get caught on inconsistency.
- **Burn propagation:** when an identity is burned, it becomes a fact tag (`burned:identity_A`). That fact spreads like any other belief (§7), so a cover dies gradually as the news reaches people, not instantly everywhere.
- **Status `questioned`:** set when any NPC schedules Verify on this identity. This drives the HUD warning.

### Player surface

- An identity card in the HUD showing status and pending verifications, with a countdown only if the player has intel on the verifier.
- Using the wrong identity with the wrong NPC is a readable mistake.

### Must-flip scenario

The player gives the same NPC two conflicting claims across two conversations. The second conversation must escalate where a fresh NPC would comply.

---

## 5. Zone authority tiers

**Purpose:** a credential's weight depends on where it's used.

### Schema

```
Zone { id, authority_tier, controlling_department, regulations_active[], restricted_credentials[] }
effective_tier = identity.tier + zone.modifier_for(identity.role_claimed)
```

### Behavior

- **Home-turf rule:** an identity claiming a role from the controlling department gets +1, and an outsider gets −1. An Auditor in Records is strong; an Auditor in Containment is an outsider.
- **Restricted credentials** are rejected outright in certain zones, whatever the tier. A containment wing ignores administrative badges, for example.
- Global events can change a zone's tier. Lockdown raises every zone's tier by 1, so every credential gets weaker.

### Player surface

- Zone boundaries on the map show a tier badge once the player has scouted them.
- **Planning:** which identity to wear for which part of the route.

### Must-flip scenario

The same identity and the same Cite produce Comply in the home zone and Stall one door over.

---

## 6. Overheard conversation (partial-audibility fragments)

**Purpose:** conversations have bystanders. A Menacing register risks more than the target.

### Schema

```
Utterance { speaker, target, verb, register, object_class, fragment_tags[], loudness: {whisper | normal | raised} }
Heard { listener, utterance_id, quarters_heard: 0..4 }
```

### Behavior

- Reuse the stealth AI's hearing-through-walls attenuation. Register sets loudness: Menacing is raised, Familiar is quieter.
- Audibility is quantized to quarters.
- **Bystander reaction:** apply the same table row at `delta × quarters_heard / 4 × 40 / 100`. The bystander learns up to `quarters_heard` of the fragment tags. The tags are ordered by salience, so the most incriminating word is heard first. This is a formula, not authored content, so it adds zero table rows.
- **Partial hearing:** at `quarters_heard == 1`, the bystander learns only the verb class (for example, "someone threatened someone"). That still moves their Suspicion.

### Player surface

- A visible hearing radius while the wheel is open (the same visual language as the stealth noise rings).
- **Pulling someone aside** (Request with a person as the object) becomes a real tactic.

### Must-flip scenario

Coerce a Clerk in a Menacing register with a Guard in the next room, versus a Procedural register. The Guard's stealth state after the conversation must differ.

---

## 7. Social propagation

**Purpose:** news, rumors and burned identities spread between NPCs, not only through alarms.

### Behavior

- Each propagation tick, pairs of NPCs who are co-located and free may exchange **one** belief each. The belief chosen is their highest-salience recent one.
- **Caps (to protect the player's head start):**
  - At most one hop per NPC per shift for any given belief.
  - A belief only crosses departments at designated shared spaces (canteen, elevator lobby, security desk).
  - Each hop drops the confidence band by 1. A band-0 belief does not spread.
- Salience order: `burned identity > violence witnessed > threat overheard > anomalous claim > routine`.
- **Determinism:** pairing uses a fixed order, `(zone_id, npc_id)`. No random encounters.

### Player surface

- **Outrunning the news:** a cover burned in the east wing still works in the west wing until the canteen at lunch.
- A rumor log surfaces what the player has overheard NPCs saying to each other. Eavesdropping is itself an intel source.

### Must-flip scenario

A cover is burned at tick T. An NPC two departments away must still Comply shortly after T, and must Escalate after a shared-space encounter.

### Failure mode

Without the caps, the whole site knows everything within minutes, and stealth prep stops mattering. The caps are the design, not a tuning afterthought.

---

## 8. Schedules / routines

**Purpose:** gives Request, Verify and Escalate physical meaning. People are somewhere, doing something.

### Schema

```
Shift { id, start_tick, end_tick }
Routine { npc_id, shift_id, waypoints: [(zone, activity, duration)] }
Activity ∈ {post, patrol, desk, break, meeting, terminal}
```

### Behavior

- **Verify needs a terminal.** A scheduled Verify job runs at the NPC's next `terminal` activity or when they reach their desk, not after an abstract delay. The cover's half-life becomes something the player can see: "they'll check when they get back to their desk."
- **Interrupting a routine** (pulling someone into a conversation, a distraction, a false order) delays their Verify. That's a real counterplay loop.
- `break` and `meeting` activities put NPCs together, which is where propagation happens.
- **Shift change** swaps NPCs, resets conversational memory and Stress, and is the natural reset point for a burned identity.

### Player surface

- Routines are learnable by watching (Shadows of Doubt style) and from schedule records (duty rosters as findable documents).
- The Verify clock becomes a person walking to a desk, which is readable without a UI timer.

### Must-flip scenario

A Verify scheduled for a Clerk whose routine is interrupted by a hallway Request must fire later than in the uninterrupted baseline.

---

## 9. Obligation ledger

**Purpose:** Comply grants something that can expire, be countermanded or be revoked, so access is never a permanent key.

### Schema

```
Token { id, grant: {access:<zone> | item:<id> | silence | escort | info:<fact>}, grantor, holder_identity,
        expiry_tick, revocable_if: {grantor_susp >= N | superior_countermand | identity_burned} }
```

### Behavior

- Every COMPLY outcome produces exactly one token, defined in the table row's `grant` column. That column is a new addition to the reaction-table schema.
- A token is valid only for the identity it was granted to. Switching identity invalidates it.
- **Revocation fires an event.** A revoked access token in a restricted zone counts as trespass right away.
- **The Silence grant** ("I didn't see you") is what coercion buys. It holds until the grantor's Fear decays below a threshold, and then they talk. Coercion buys time, not permanence.

### Player surface

- An inventory of active tokens with expiry indicators.
- The Silence grant's decay creates a loop: return to reinforce the threat, or finish the job before it expires.

### Must-flip scenario

Access granted by a Clerk is countermanded when their Supervisor learns of it through propagation. The player's next door interaction must fail.

---

## 10. Leverage derivation and capture

**Purpose:** Coerce needs leverage. Capture is Coerce under physical control, not a separate system.

### Leverage

- **Secret tags** on NPCs (`secret:affair`, `secret:embezzlement`, `secret:anomalous_exposure`) come from the same tag registry.
- Leverage is derived automatically, never authored per NPC: a leverage item exists when the player holds a record whose tags match an NPC's secret. The object class is `personal`, `professional` or `institutional`, taken from the secret's category.
- **Loyalty scales Coerce:** high-Loyalty NPCs resist personal leverage but give in to institutional leverage ("this goes to the Director").

### Capture

- **Entering capture:** approach an unaware NPC from behind, or an unarmed NPC whose Fear is 80 or higher. The NPC is flagged `captured`.
- While captured, the wheel shows a restricted verb set: Coerce, Request (info or escort), Deceive and Withdraw (release). Present and Cite are disabled; bureaucracy doesn't apply at gunpoint.
- Captured NPCs get a **Fear floor** of 60 and a **Rapport ceiling** of 20.
- **Release is the dangerous part.** A released NPC's state resumes normal decay, and they become a witness to the capture, the highest-salience belief after a burned identity. The player has to choose between a Silence token, restraints (a physical prop), or accepting the witness.

### Player surface

- Hostage-escort through checkpoints (Request escort plus Deceive to third parties).
- Interrogation for facts the dossier can't give you.

### Must-flip scenario

The same NPC released with versus without a Silence token. The floor alarm state must differ one shift later.

---

## 11. Regulation registry

**Purpose:** `Cite` needs regulations as data the player can learn and use.

### Schema

```
Regulation { id, text_display, scope: {zones[], roles[]}, grants: [effect], overridden_by: [event_ids], known_by_default: bool }
```

### Behavior

- **The player must learn a regulation before Citing it.** Sources are regulation documents, overheard NPCs, and the starting kit that comes with an Auditor identity.
- Citing a regulation the NPC's role doesn't respect, or one currently overridden by an event, falls into the `inadmissible` relation class.
- **Regulations conflict on purpose.** Containment directives override administrative ones. Mastery means knowing which rule beats which in which zone.

### Player surface

- A rulebook screen, and a sub-wheel that only shows regulations valid in the current zone and event state.
- Bureaucratic horror as a mechanic: winning an argument with the right paragraph.

### Must-flip scenario

Citing an access regulation before versus after a Lockdown event.

---

## 12. Dossier / intel store

**Purpose:** tracks what the player knows. It's the ledger of the information economy, and it gates the wheel's prediction arrows.

### Schema

```
Dossier { npc_id, known: { role, temperament?, beliefs_revealed[], secrets_revealed[], routine_revealed?, superior? } }
```

### Behavior

- Each field is revealed by a specific kind of evidence:

| Field | Revealed by |
|---|---|
| Temperament | Personnel file |
| Routine | Duty roster, or watching the NPC for one full cycle |
| Beliefs | Overheard conversations |
| Secrets | Records matching their secret tags |
| Superior | Org chart record, or seeing an escalation |

- **The prediction arrows** on the wheel appear only for verbs whose outcome can be computed from revealed fields. With temperament unknown, the arrows show a range, not a single value.
- Dossiers persist across shifts but can go **stale**: a belief revealed before a global event is flagged as possibly outdated.

### Player surface

The whole intel screen. It's what turns preparation into power.

### Must-flip scenario

This is a telemetry test, not an outcome test: players with full dossiers should choose verbs with measurably higher success rates. If they don't, the arrows are unreadable.

---

## 13. Dependency graph

```
Tick scheduler ──┬─> everything
Tag registry ────┼─> Knowledge, Records, Leverage, Regulations, Identity
                 │
Knowledge ───────┼─> Claim classification, Propagation, Dossier
Org graph ───────┼─> Relation classes, Escalate, Tokens (countermand)
Identity ────────┼─> Authority gap, Tokens, Burn propagation
Zone tiers ──────┼─> Authority gap, Regulations
Schedules ───────┼─> Verify timing, Propagation (co-location), Escalate targeting
Overheard ───────┼─> Knowledge (told beliefs), Stealth AI states
Propagation ─────┼─> Knowledge, Identity burn spread
Tokens ──────────┼─> Access, Silence loop
Leverage/Capture ┼─> Coerce objects, captured wheel state
Regulations ─────┼─> Cite objects
Dossier ─────────┴─> Wheel prediction arrows
```

---

## 14. Scope honesty

| Subsystem | Size | Cut risk | If cut, what breaks |
|---|---|---|---|
| Tick scheduler | S | Never | Everything |
| Tag registry | S | Never | Silent logic bugs |
| Knowledge model | M | Never | Deceive stops meaning anything |
| Org graph | S | Never | Present loses its best plays |
| Identity ledger | M | Never | No cover, no Rapport |
| Zone tiers | S | Low | Credentials feel flat |
| Schedules | M | Low | Verify becomes an abstract timer |
| Tokens | S | Low | Access becomes permanent keys |
| Regulations | S | Medium | Cite reduces to "use credential" |
| Dossier | M | Medium | Preparation has no payoff; the information economy weakens |
| Overheard | M | Medium | Register only matters to the target |
| Leverage / capture | M | Medium | Coerce needs hand-authored leverage |
| Propagation | L | **Highest** | Consequences stay local, which is still a shippable game |

Propagation is the most expensive, the hardest to tune, and the most likely to produce outcomes that feel unfair. Build it last. If time runs out, fall back to shift-change resets and event-based alarms, which already cover the minimum.
