# Cognitohazard: Build Order
### From an empty repo to a site that argues back, with a fun check at every step

*Third in the series, after "Dialogue System Foundations" and "Supporting Simulation Subsystems." Those two say **what** to build. This one says **in what order**, and how to know at each step whether it's worth continuing.*

---

## 0. The core problem this order solves

A deep sim is a well-known trap. You can spend a year building systems that are all correct and all invisible, then find out in the first playtest that the moment-to-moment experience is a menu. The order below is designed around one rule:

> **Every phase ends in something playable, and that playable thing has to answer one specific question about fun. If the answer is no, you change the design before building the next layer, not after.**

The next four rules follow from that one.

- **Build in the HTML testbed first, and port to Godot after the fun is proven.** You already have a working browser testbed with a headless Node rig. It's the fastest place to iterate. Port a subsystem to Godot once its phase's fun check passes, not before.
- **Build vertically, not horizontally.** Each phase adds a thin slice through every layer (sim, table, UI, feedback), not a complete layer. One role with all 8 verbs beats 6 roles with 2 verbs.
- **Feel before depth.** A shallow system that feels great gets deeper easily. A deep system that feels bad is expensive to fix, because every layer above it assumes the bad feel.
- **Content comes last.** Hand-write a few rows and lines per phase. The baking pipeline goes in once the table schema has stopped changing, because every schema change invalidates baked content.

---

## 1. What "fun" means for this game

Fun has to be something you can test, or the fun checks become opinions. This game's fun sits at four time scales, and each phase targets one or more of them.

| Loop | Time scale | The fun | What kills it |
|---|---|---|---|
| **Moment** | 1–5 s | The wheel snaps, the NPC visibly flinches, dilated time hums | Menu lag, reactions with no visible response, text walls |
| **Conversation** | 10–60 s | "I have exactly the right record for this person" | Obvious best choice every time; outcomes that feel random |
| **Infiltration** | 5–20 min | A lie buying you ten minutes, and racing the clock you started | Consequences too slow to feel, or too fast to plan around |
| **Site** | 1+ hours | Your plan working across a whole building of people | Spreading news wiping out careful prep; nothing you do lasting |

The game's specific promise is that **knowing things is power.** Every phase should make that feel more true. If a phase makes it feel less true, that's a design red flag, even if the phase is technically done.

---

## 2. Phase 0: Tabletop prototype (before code)

**Build:** a paper version. You GM, and 2–3 players are infiltrators. The NPCs are index cards with role, temperament and the four state values. A printed wheel. Record cards with a prose side and a tag side. You resolve each move with the reaction math from the foundations report, done by hand on a calculator.

**Why this is worth doing:** you've run tabletop games for 11 years, so this is the cheapest high-quality test available to you. A tabletop session tests the design of the social system with none of the cost of building UI. Ask these questions:

- Do players use all 8 verbs, or do 2–3 dominate?
- Do players naturally think in terms of "which record for which person"?
- Does the worked example (Present an implicating record to a Careerist Clerk under Audit) feel clever to the player, or just like a lookup?
- Is the four-axis state (Suspicion, Fear, Stress, Rapport) something players can reason about, or is it mush?

**Fun check:** at the end of a session, can players describe a moment where they felt clever because of something they knew? If they only describe moments where they got lucky, the resolution math needs work before any code.

**Caveat:** a human GM smooths over bad rules automatically, so tabletop is generous. Be strict with yourself: resolve by the formula even when your GM instincts disagree, and write down every time you wanted to override it. Those notes are your first list of table bugs.

**Size:** S. Two or three sessions.

---

## 3. Phase 1: One clerk, one room, the wheel

**Prerequisite:** entity registry and prop store milestones from the existing sim plan, plus the tick scheduler and tag registry.

**Build:**
- The wheel UI in the testbed: 8 fixed slots, the object sub-wheel, the register modifier.
- One NPC role (Clerk) with the full dynamic state vector.
- A hand-written table for Clerk × 8 verbs × 3 registers (about 100 rows).
- 5 records with prose and tags.
- `resolve()` with the four outcomes.
- **Tells:** at least 3 visible reactions per outcome band (posture, glance, hand motion) and a bark line. The procedural sprites and hitstop system you already have can drive these.

**Explicitly not built yet:** identity, verification, schedules, events, other NPCs.

**Fun check (moment loop):** open the wheel 20 times in a row. Does it feel good to select a verb and see the Clerk react? Specifically:
- Is wheel open → selection → reaction under about 300 ms of perceived latency?
- Can you tell the Clerk's state from their body without reading numbers?
- Do the dilated-time drain and the "burn a record to think longer" decision create pressure, or just annoyance?

**Kill/pivot criteria:** if the wheel feels like a menu even with good tells, try a smaller wheel (4 verbs with a context-sensitive second ring) before building anything else. Everything in later phases goes through this interaction.

**Size:** M.

---

## 4. Phase 2: The lie and the clock

**Build:**
- Identity ledger (one cover identity, plus the player's true identity).
- Knowledge model with institutional beliefs for Clerk and one new role, Guard.
- Claim classification, so Deceive works.
- Schedules for 2–4 NPCs in a small multi-room map.
- Deferred verification: the Clerk walks to a terminal to check your claim.
- Hook post-dialogue Suspicion bands into the existing five-state stealth AI.

**The scenario:** get a record out of a Records office. The Clerk can be lied to, but will verify at their next terminal visit. A Guard patrols nearby.

**Fun check (infiltration loop):** this is the most important check in the whole plan. Does lying feel like borrowing time?
- When the player lies and sees the Clerk head for the terminal, do they feel pressure?
- Do players come up with counterplay without being told: interrupting the Clerk, pulling them into another conversation, finishing faster?
- Is the lie clock readable without a UI timer?

**Kill/pivot criteria:** if players don't notice the verification or don't feel its pressure, the half-life mechanic needs to be louder (a visible walk, an audible "let me just check…") before the rest of the game leans on it. The other phases assume this tension works.

**Lock before leaving this phase:** the GDScript vs. C# decision. Porting to Godot starts after this phase, and the sim core language should be settled before that happens.

**Size:** M–L.

---

## 5. Phase 3: The institution

**Build:**
- Org graph, with a Supervisor above the Clerk.
- Zone authority tiers across 3 zones.
- Obligation tokens: Comply grants something with an expiry and revocation.
- Regulation registry with 5–8 regulations, some conflicting.
- Roles: add Supervisor and Auditor (4 total).
- The first 3 global events: Floor Alarm, Audit Announced, Shift Change.

**The scenario:** a small department with 6–8 NPCs across 3 zones. The objective needs access to the third zone, which requires either a credential you don't have or a Supervisor's approval.

**Fun check (conversation + infiltration):**
- Do players start planning routes by identity and zone, not just by line of sight?
- Does Audit Announced change how players approach conversations, without needing explanation?
- Do at least 2 distinct solutions emerge between different playtesters (for example, one uses Cite + regulation, another uses Present + an implicating record)?

**Warning sign:** if every tester finds the same solution, the table has a dominant strategy. Check the outcome distribution per verb in the headless suite before adding more content.

**Size:** L. This is the biggest phase.

---

## 6. Phase 4: Intel is power

**Build:**
- Dossier store.
- Personnel files, duty rosters and org charts as findable record types.
- Prediction arrows on the wheel, gated by dossier fields.
- Stale-intel flags after events.

**Why here and not earlier:** the arrows only make sense once there are enough systems for predictions to be non-obvious. In Phase 1 they'd just state the obvious; after Phase 3 they're real intel.

**Fun check (the core promise):** run a paired playtest.
- Group A plays Phase 3's scenario with dossiers available. Group B plays it without.
- Group A should succeed more *and* report feeling clever. If A succeeds more but reports feeling like they followed instructions, the arrows are too explicit. Switch them to ranges or partial information.

**This is the point where the game either works or doesn't.** If knowing things doesn't feel like power after this phase, the core promise has failed and needs rethinking before going further.

**Size:** M.

---

## 7. Phase 5: Talking and violence mix

**Build:**
- Capture (Coerce under physical control), with the restricted captured wheel.
- Leverage derived from secret tags.
- Silence tokens with Fear decay.
- Overheard conversation using the existing hearing-through-walls system.
- Body discovery connecting to global events.

**Why now:** combat already exists and is fun in the testbed. This phase joins the two halves of the game. Combat is a failure state per the design, but failure needs to flow into the social system, not end it.

**Fun check:**
- When a stealth approach goes loud, do players reach for the wheel (capture, coerce a witness) instead of reloading a save?
- Does the Silence-token decay create a satisfying "tie up loose ends" loop, or just a chore?
- Does register choice now feel risky because of who might overhear?

**Kill/pivot criteria:** if capture is always better than conversation, the captured wheel is too strong. Raise the release risk.

**Size:** M.

---

## 8. Phase 6: The site talks back

**Build:**
- Social propagation, with all the caps from the subsystems report.
- The rest of the global events (Body Discovered, Containment Breach, Lockdown, Purge Order).
- Remaining roles (Technician, Containment Specialist), reaching 6 total.
- A full floor: 20–30 NPCs across 2 departments with shared spaces.

**Why last among the systems:** propagation touches everything and is the hardest to tune. Tuning it before the other systems settle means re-tuning it every time they change.

**Fun check (site loop):**
- **"Outrunning the news" moments:** does a burned cover in one wing still work in another, and do players notice and exploit that?
- **Fairness:** when a player gets caught through propagation, can they trace why? ("The Guard I scared talked to the Clerk at lunch.") If the answer is "it just happened," the rumor log or tells need to surface the chain.

**Kill/pivot criteria:** if propagation makes the game feel unfair despite the caps, cut it back to shift-change resets and event alarms. That fallback was defined in the subsystems report and is still a shippable game.

**Size:** L.

---

## 9. Phase 7: Content at scale

**Build:**
- The baking pipeline: enumerate keys → draft with an LLM teacher or fine-tuned Laya → designer review → export → CI invariants.
- Authored prose pools for every `line_bucket`, with template slots for concrete nouns.
- Offline QA on records: do the tags match the prose?
- The Godot port of any remaining testbed systems.

**Why last:** the table schema will keep changing through Phases 1–6. Every schema change would invalidate hundreds of baked rows. Hand-written rows are cheap to change; baked ones aren't.

**Fun check:** play a full floor with no debug overlays. Does anything in the prose feel repetitive within one hour? If yes, add more variants to the most-heard buckets. Telemetry will show which ones.

**Size:** L, but it can run in parallel with polish.

---

## 10. Summary

| Phase | Adds | Question it answers | Loop tested | Size |
|---|---|---|---|---|
| 0 | Paper prototype | Is the social math interesting to humans? | Conversation | S |
| 1 | Wheel, one Clerk, tells | Does the wheel feel good? | Moment | M |
| 2 | Identity, knowledge, verification, schedules | Does lying feel like borrowing time? | Infiltration | M–L |
| 3 | Org, zones, tokens, regulations, events | Do multiple solutions emerge? | Conversation + infiltration | L |
| 4 | Dossiers, prediction arrows | Does knowing things feel like power? | All | M |
| 5 | Capture, leverage, overheard, silence | Do talking and violence connect? | Infiltration | M |
| 6 | Propagation, full events, full floor | Does the site feel alive and fair? | Site | L |
| 7 | Baking, prose, Godot port | Does it hold up for an hour? | All | L |

---

## 11. What's most likely to go wrong

| Risk | Where it shows up | Early warning | Response |
|---|---|---|---|
| **The wheel feels like a menu** | Phase 1 | Testers read labels instead of watching the NPC | Stronger tells, faster feedback, or a smaller wheel |
| **One verb dominates** | Phases 0, 3 | Outcome distribution per verb in the headless suite | Add costs to the dominant verb; don't add more verbs |
| **The sim is invisible** | Phases 3, 6 | Testers can't explain why something happened | Rumor log, tells, the "must-flip" CI rule |
| **Preparation doesn't pay off** | Phase 4 | Paired playtest shows no gap | Revisit arrow design; make intel sources more rewarding to find |
| **Scope creep** | Every phase | A phase takes twice as long as its size suggests | Cut from the "cut risk: medium/high" list in the subsystems report; never cut Phases 1–2 |
| **Porting drains momentum** | After Phase 2 | Weeks of Godot work with no new fun checks | Port only what has passed its check; keep new systems in the testbed |
| **Content debt** | Phase 7 | Prose volume too large to author well | Reduce line buckets by merging registers for low-traffic combinations |

The strongest counterargument to this entire plan is that it's still too big for a solo developer. Phases 3 and 6 are each large. If time becomes the binding limit, the shippable minimum is **Phases 0–4 on a single department**. That's a complete game about lying to a small bureaucracy, and it tests the core promise. Everything after that is expansion.
