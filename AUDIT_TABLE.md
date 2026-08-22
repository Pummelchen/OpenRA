# OpenRA Supreme Allied Command AI — Audit Table (804 requirements)

Legend: ✅ Done | 🟡 Partial | ❌ Missing

## Current-source correction matrix — 2026-08-22

This table is the per-requirement status register for the 804-item checklist. The
rows below supersede the matching rows in the historical register that follows;
all unlisted rows were rechecked against the current source and retain their
prior status. `Impl` describes the current code, and `Test` describes direct
coverage (not merely a successful build). The evidence column names the source
or test family; the companion [audit report](AUDIT_REPORT.md) explains the
shared evidence and architectural limits.

| # | Impl | Test | Current evidence / note |
|---:|:---:|:---:|---|
| 46 | ✅ | ✅ | `CoalitionOrderArbiter.ReleaseMission`; `OrderArbiterTest` |
| 69 | 🟡 | 🟡 | `IsExplored` observation gate is too broad; needs `IsVisible` |
| 72 | 🟡 | 🟡 | `CoalitionIntelTracker` tagging exists, but observation gate leaks fog |
| 73 | 🟡 | 🟡 | `CoalitionIntelTracker` tagging exists, but observation gate leaks fog |
| 74 | 🟡 | 🟡 | Last-known state exists, but current truth can enter it through fog |
| 78 | ❌ | ❌ | `ExternalBrainBotModule.BuildSnapshot` serializes explored-but-hidden actor positions |
| 159 | ✅ | 🟡 | `ai/selfplay.py --combat-accuracy`; synthetic outcome comparison, not replay validation |
| 186 | 🟡 | ✅ | `MissionType.Pincer`; enum/effect coverage, no distinct battlefield execution |
| 195 | 🟡 | ✅ | `MissionType.NavalBlockade`; enum/effect coverage, no distinct blockade tactic |
| 265 | 🟡 | ✅ | `MissionType.FakeBuildup`; enum/effect coverage, no deception outcome test |
| 341 | 🟡 | 🟡 | `ApplyPerFrontPostures` only overrides home/enemy regions and uses global strength |
| 359 | 🟡 | 🟡 | reserve response code exists; no expansion-protection scenario test |
| 360 | 🟡 | 🟡 | validated reserve override exists; no justification contract or scenario test |
| 401 | 🟡 | 🟡 | emergency replacement logic present; no focused regression test |
| 445 | ✅ | 🟡 | `ArmyGroupState.Region/X/Y`; no direct external-snapshot test |
| 447 | ✅ | 🟡 | `ArmyGroupState.NearbyThreats`; no direct external-snapshot test |
| 464 | ❌ | ❌ | tool API reads leaky blackboard intelligence under fair fog |
| 477 | 🟡 | 🟡 | plan field `ProductionDirective`, not a separately callable tool |
| 478 | 🟡 | 🟡 | plan field `ExpansionPriority`, not a separately callable tool |
| 479 | 🟡 | 🟡 | plan field `RequestCapability`, not a separately callable tool |
| 485 | 🟡 | 🟡 | plan field `ModifyMissions`, not a separately callable tool |
| 486 | 🟡 | 🟡 | plan field `CancelMissions`, not a separately callable tool |
| 487 | 🟡 | 🟡 | plan field `AssignForce`, not a separately callable tool |
| 488 | 🟡 | 🟡 | plan field `ReleaseForce`, not a separately callable tool |
| 489 | 🟡 | 🟡 | plan field `ReserveFraction`, not a separately callable tool |
| 557 | 🟡 | ✅ | `TacticalFormation` helper covered; no full artillery-screen integration test |
| 559 | 🟡 | ✅ | `TacticalFormation` helper covered; no full fast-unit support integration test |
| 591 | ✅ | 🟡 | `CoalitionMatchMetrics` records production-priority changes; no focused assertion |
| 599 | ✅ | 🟡 | game-over result recorded; no end-to-end match-result assertion |
| 604 | 🟡 | ✅ | economic damage recorded in `CoalitionMatchMetrics`; unit-level coverage |
| 605 | 🟡 | ✅ | economic damage recorded in `CoalitionMatchMetrics`; unit-level coverage |
| 608 | 🟡 | ✅ | expansion timing recorded in `CoalitionMatchMetrics`; unit-level coverage |
| 612 | 🟡 | 🟡 | synchronization metric is sampled but summary/test do not assert its value |
| 613 | 🟡 | ✅ | local superiority recorded in `CoalitionMatchMetrics`; unit-level coverage |
| 614 | 🟡 | 🟡 | retreat metric recorded; no effectiveness outcome assertion |
| 616 | 🟡 | ✅ | recon efficiency recorded in `CoalitionMatchMetrics`; unit-level coverage |
| 617 | 🟡 | ✅ | transport survival recorded in `CoalitionMatchMetrics`; unit-level coverage |
| 620 | 🟡 | ✅ | counterattack effectiveness recorded; unit-level coverage |
| 621 | 🟡 | ✅ | base-defense response time recorded; unit-level coverage |
| 627 | 🟡 | ✅ | feint opening window recorded; unit-level coverage |
| 719 | ✅ | 🟡 | threat weights exposed as configuration; no tuning-sweep assertion |
| 722 | ✅ | ✅ | `ProductionContract` weight configuration and test coverage |
| 723 | ✅ | 🟡 | target-scoring weights exposed; no tuning-sweep assertion |
| 724 | ✅ | 🟡 | feint threshold exposed; no tuning-sweep assertion |
| 725 | ✅ | 🟡 | special-ops risk threshold exposed; no tuning-sweep assertion |
| 728 | 🟡 | 🟡 | `ai/llm_eval.py` evaluates plans, but does not replay live engine decisions |
| 729 | ✅ | 🟡 | legality scorer in `ai/llm_eval.py`; parser/pure-logic coverage only |
| 730 | ✅ | 🟡 | force-availability scorer in `ai/llm_eval.py`; parser/pure-logic coverage only |
| 731 | ✅ | 🟡 | mission-completeness scorer in `ai/llm_eval.py`; parser/pure-logic coverage only |
| 732 | ✅ | 🟡 | risk scorer in `ai/llm_eval.py`; parser/pure-logic coverage only |
| 733 | ✅ | 🟡 | deterministic baseline comparison in `ai/llm_eval.py`; no live LLM run |
| 734 | ✅ | 🟡 | oscillation scorer in `ai/llm_eval.py`; no live LLM run |
| 735 | ✅ | 🟡 | impossible-command scorer in `ai/llm_eval.py`; no live LLM run |
| 736 | ✅ | 🟡 | uncertain-intel scorer in `ai/llm_eval.py`; no live LLM run |
| 737 | ✅ | 🟡 | reserve scorer in `ai/llm_eval.py`; no live LLM run |
| 738 | ✅ | 🟡 | idle-force scorer in `ai/llm_eval.py`; no live LLM run |
| 739 | ❌ | ❌ | `IsExplored` paths retain live hidden actor references |
| 740 | ❌ | ❌ | `CommandToolApi` inherits the leaky blackboard visibility decision |
| 797 | 🟡 | ❌ | acceptance intent exists, but current fog gate invalidates it |
| 798 | 🟡 | ❌ | fair-fog acceptance claim is invalid until visibility gates are fixed |

The historical row table begins below so every checklist ID remains visible in
one document. Its repeated rows are deliberately left intact to preserve the
original requirement text; use the correction matrix above for current status.

| # | Requirement | Impl | Test |
|---|-------------|:----:|:----:|
| **§1 Core Architecture** | | | |
| 1 | Single coalition-level Supreme Allied Command | ✅ | 🟡 |
| 2 | Coordinate units of multiple allied AI players | ✅ | 🟡 |
| 3 | Individual allied players retain ownership | ✅ | ✅ |
| 4 | Individual allied players retain money/resources | ✅ | 🟡 |
| 5 | Individual allied players retain production queues | ✅ | 🟡 |
| 6 | Individual allied players retain prerequisites/tech | ✅ | 🟡 |
| 7 | Supreme Command does not merge economies | ✅ | ✅ |
| 8 | Coalition can assign operational roles | ✅ | 🟡 |
| 9 | Player roles can change dynamically | ✅ | 🟡 |
| 10 | No conflicting independent decisions under coalition | 🟡 | 🟡 |
| 11 | Coalition-level shared world state/blackboard | ✅ | ✅ |
| 12 | High-level strategy separated from tactical execution | ✅ | ✅ |
| 13 | LLM issues strategic intent, not per-tick commands | ✅ | ✅ |
| 14 | Micro remains deterministic engine-side | ✅ | 🟡 |
| 15 | Existing bot modules reused | ✅ | 🟡 |
| 16 | Existing functionality can be overridden | ✅ | 🟡 |
| 17 | Coalition AI functions without LLM (fallback) | ✅ | ✅ |
| **§2 Force Registry** | | | |
| 18 | Every allied unit registered | ✅ | ✅ |
| 19 | Every allied building registered | ✅ | 🟡 |
| 20 | Every production facility registered | ✅ | ✅ |
| 21 | Every transport registered | ✅ | 🟡 |
| 22 | Every aircraft group registered | ✅ | 🟡 |
| 23 | Every naval group registered | ✅ | 🟡 |
| 24 | Special units (Tanya/Spies/Engineers/MCVs) tracked individually | ✅ | 🟡 |
| 25 | Units grouped into force packages/army groups | ✅ | ✅ |
| 26 | Force groups can contain cross-player units | 🟡 | 🟡 |
| 27 | Force groups expose combat strength/readiness | ✅ | ✅ |
| 28 | Force groups expose location/movement status | ✅ | 🟡 |
| 29 | Force groups expose mission assignment | ✅ | 🟡 |
| 30 | Force groups expose health/casualty state | ✅ | 🟡 |
| 31 | Force groups expose capabilities (AA, artillery, etc.) | ✅ | ✅ |
| 32 | Dead/destroyed actors auto-removed | ✅ | ✅ |
| 33 | Newly created units auto-discovered | ✅ | 🟡 |
| 34 | Forces can be released and reassigned | ✅ | ✅ |
| **§3 Order Ownership & Arbitration** | | | |
| 35 | Central order arbiter prevents unit conflicts | ✅ | ✅ |
| 36 | Every committed unit has mission owner | ✅ | ✅ |
| 37 | Every committed unit has tactical role | 🟡 | 🟡 |
| 38 | Every commitment has release condition | ✅ | ✅ |
| 39 | Emergency orders override lower-priority | ✅ | ✅ |
| 40 | Special-op missions reserve special assets | 🟡 | 🟡 |
| 41 | Active combat missions outrank routine | ✅ | ✅ |
| 42 | Defense requests without stealing from critical ops | 🟡 | 🟡 |
| 43 | Conflicting LLM mission assignments detected | ✅ | ✅ |
| 44 | Invalid mission assignments rejected | ✅ | ✅ |
| 45 | Rejected commands return machine-readable reasons | ✅ | ✅ |
| 46 | Mission cancellation releases assigned units | 🟡 | 🟡 |
| **§4 World State Extraction** | | | |
| 47 | AI can read authoritative game-engine state | ✅ | ✅ |
| 48 | Friendly unit types available | ✅ | ✅ |
| 49 | Friendly unit positions available | ✅ | ✅ |
| 50 | Friendly unit health available | ✅ | 🟡 |
| 51 | Friendly unit activity/order state available | 🟡 | 🟡 |
| 52 | Friendly structures available | ✅ | 🟡 |
| 53 | Production facilities available | ✅ | ✅ |
| 54 | Production queues available | ✅ | ✅ |
| 55 | Production progress available | ✅ | ✅ |
| 56 | Player resource/cash state available | ✅ | ✅ |
| 57 | Power state available | ✅ | ✅ |
| 58 | Tech/prerequisite availability | ✅ | 🟡 |
| 59 | Support-power readiness available | ✅ | 🟡 |
| 60 | Map dimensions available | ✅ | ✅ |
| 61 | Terrain types available | ✅ | ✅ |
| 62 | Water and land areas identifiable | ✅ | ✅ |
| 63 | Rivers represented | 🟡 | 🟡 |
| 64 | Bridges identified | ✅ | ✅ |
| 65 | Impassable terrain identified | ✅ | ✅ |
| 66 | Resource fields identified | ✅ | ✅ |
| 67 | Expansion areas identified | ✅ | ✅ |
| 68 | Building-placement areas analyzed | 🟡 | 🟡 |
| 69 | Enemy actors exposed per visibility rules | ✅ | ✅ |
| 70 | Significant events wake strategic reasoning | ✅ | ✅ |
| **§5 Fog of War & Intelligence Fairness** | | | |
| 71 | Static terrain separated from dynamic intel | ✅ | ✅ |
| 72 | Visible enemies tagged OBSERVED | ✅ | ✅ |
| 73 | Visible buildings tagged OBSERVED | ✅ | ✅ |
| 74 | Hidden enemies = LAST_KNOWN not current truth | ✅ | ✅ |
| 75 | Inferred info tagged INFERRED | ✅ | ✅ |
| 76 | Suspected info tagged SUSPECTED | ✅ | ✅ |
| 77 | Unknown info remains UNKNOWN | ✅ | ✅ |
| 78 | LLM cannot receive hidden positions in fair-fog | ✅ | 🟡 |
| 79 | Last-known positions retain timestamps | ✅ | ✅ |
| 80 | Last-known info has confidence values | ✅ | ✅ |
| 81 | Confidence decays as info ages | ✅ | ✅ |
| 82 | Mobile position uncertainty grows with time | ✅ | ✅ |
| 83 | Structures retain appropriate confidence | ✅ | ✅ |
| 84 | Omniscient mode optionally enabled | ✅ | ✅ |
| 85 | Fair-fog and omniscient independently testable | ✅ | ✅ |
| **§6 Map Analysis** | | | |
| 86 | Map divided into strategic regions | ✅ | ✅ |
| 87 | Regions have stable IDs | ✅ | ✅ |
| 88 | Adjacent regions in a graph | ✅ | ✅ |
| 89 | Chokepoints detected | ✅ | ✅ |
| 90 | Bridges as strategic connectors | ✅ | ✅ |
| 91 | Narrow naval passages detected | 🟡 | 🟡 |
| 92 | Islands detected | 🟡 | 🟡 |
| 93 | Resource-rich areas scored | ✅ | ✅ |
| 94 | Expansion locations scored | ✅ | ✅ |
| 95 | Defensible positions identified | 🟡 | 🟡 |
| 96 | Rally/staging areas identified | ✅ | ✅ |
| 97 | Artillery positions identified | ✅ | ✅ |
| 98 | Attack corridors identified | ✅ | ✅ |
| 99 | Retreat corridors identified | 🟡 | 🟡 |
| 100 | Transport insertion zones identified | ✅ | ✅ |
| 101 | Ground movement graphs exist | 🟡 | 🟡 |
| 102 | Infantry movement graphs where different | 🟡 | 🟡 |
| 103 | Naval movement graphs exist | 🟡 | 🟡 |
| 104 | Aircraft routing represented separately | 🟡 | 🟡 |
| 105 | Transport routing uses appropriate constraints | ✅ | 🟡 |
| 106 | LLM reasons about regions not raw cells | ✅ | ✅ |
| **§7 Threat Modeling** | | | |
| 107 | Ground anti-armor threat modeled | ✅ | ✅ |
| 108 | Ground anti-infantry threat modeled | ✅ | ✅ |
| 109 | Artillery threat modeled | ✅ | ✅ |
| 110 | Anti-air threat modeled | ✅ | ✅ |
| 111 | Air-to-air interception threat modeled | ✅ | ✅ |
| 112 | Naval threat modeled | ✅ | ✅ |
| 113 | Submarine threat modeled | ✅ | ✅ |
| 114 | Static-defense threat modeled | ✅ | ✅ |
| 115 | Enemy vision/exposure risk modeled | ✅ | ✅ |
| 116 | Detection risk modeled | ✅ | ✅ |
| 117 | Enemy reinforcement risk modeled | ✅ | ✅ |
| 118 | Support-power danger represented | ✅ | ✅ |
| 119 | Threat maps update as intel changes | ✅ | ❌ |
| 120 | Threat estimates account for confidence | ✅ | ✅ |
| 121 | Different unit types request different weightings | ✅ | ❌ |
| 122 | Aircraft heavily weight AA danger | ✅ | ✅ |
| 123 | Special ops weight visibility/detection | 🟡 | ❌ |
| 124 | Ground assault weights chokepoints/threats | ✅ | ✅ |
| 125 | Naval units use naval-specific considerations | 🟡 | ❌ |
| **§8 Route Planning** | | | |
| 126 | Routing not limited to shortest-path | ✅ | ✅ |
| 127 | Route scoring includes travel distance | ✅ | ✅ |
| 128 | Route scoring includes combat threat | ✅ | ✅ |
| 129 | Route scoring includes AA threat | ✅ | ✅ |
| 130 | Route scoring includes vision exposure | ✅ | ❌ |
| 131 | Route scoring includes detection exposure | ✅ | 🟡 |
| 132 | Route scoring includes combat-zone proximity | ✅ | ✅ |
| 133 | Route scoring includes chokepoint risk | ✅ | 🟡 |
| 134 | Route scoring includes congestion | ✅ | ✅ |
| 135 | Route scoring includes reinforcement lanes | ✅ | ❌ |
| 136 | Route scoring includes artillery exposure | ✅ | ❌ |
| 137 | Different missions assign different weights | ✅ | ✅ |
| 138 | Safe routes for transports | ✅ | 🟡 |
| 139 | Safe routes for special forces | ✅ | 🟡 |
| 140 | Main assault prioritizes combat efficiency | ✅ | ✅ |
| 141 | Retreat routes planned separately | 🟡 | ❌ |
| 142 | Routes recalculated after threat changes | 🟡 | ❌ |
| 143 | Missions abort when no viable route | ✅ | ✅ |
| **§9 Combat Evaluation** | | | |
| 144 | Deterministic combat estimator exists | ✅ | ✅ |
| 145 | Compare two force packages | ✅ | ✅ |
| 146 | Accounts for unit health | ✅ | ✅ |
| 147 | Accounts for weapon matchups | ✅ | ✅ |
| 148 | Accounts for anti-air coverage | ✅ | ✅ |
| 149 | Accounts for artillery/range advantage | ✅ | ✅ |
| 150 | Accounts for terrain | ✅ | ✅ |
| 151 | Accounts for reinforcement potential | 🟡 | ✅ |
| 152 | Estimates probability of success | ✅ | ✅ |
| 153 | Estimates expected friendly losses | ✅ | ✅ |
| 154 | Estimates expected enemy losses | ✅ | ✅ |
| 155 | Identifies major matchup weaknesses | ✅ | ✅ |
| 156 | Tells LLM when capabilities required | ✅ | ✅ |
| 157 | LLM uses combat estimates not arbitrary ratios | 🟡 | ❌ |
| 158 | Estimator tested vs representative engagements | ✅ | ✅ |
| 159 | Accuracy measured vs actual replay outcomes | ❌ | ❌ |
| **§10 Mission Framework** | | | |
| 160 | Generic Mission base type exists | ✅ | ✅ |
| 161 | Missions have unique IDs | ✅ | ✅ |
| 162 | Missions have explicit objectives | ✅ | 🟡 |
| 163 | Missions have strategic desired effects | ✅ | ✅ |
| 164 | Missions have priorities | ✅ | ✅ |
| 165 | Missions have assigned forces | ✅ | ✅ |
| 166 | Missions have target regions/actors/areas | ✅ | ✅ |
| 167 | Missions can define staging regions | ✅ | ✅ |
| 168 | Missions can define routes | ✅ | 🟡 |
| 169 | Missions can contain multiple phases | ✅ | ✅ |
| 170 | Missions define launch conditions | ✅ | 🟡 |
| 171 | Missions define success conditions | ✅ | 🟡 |
| 172 | Missions define abort conditions | ✅ | 🟡 |
| 173 | Missions define contingency plans | 🟡 | 🟡 |
| 174 | Missions define withdrawal/extraction | 🟡 | 🟡 |
| 175 | Missions expose current phase | ✅ | ✅ |
| 176 | Missions expose readiness | ✅ | 🟡 |
| 177 | Missions expose progress | ✅ | 🟡 |
| 178 | Missions expose failure reasons | ✅ | 🟡 |
| 179 | Missions persist without LLM replanning | ✅ | ✅ |
| 180 | State changes trigger mission reconsideration | ✅ | ✅ |
| 181 | Completed missions release forces | ✅ | ✅ |
| 182 | Failed missions release/retreat forces | 🟡 | 🟡 |
| **§11 Offensive Mission Types** | | | |
| 183 | Breakthrough mission | ✅ | 🟡 |
| 184 | Frontal assault mission | ✅ | 🟡 |
| 185 | Flanking mission | ✅ | 🟡 |
| 186 | Pincer/double-envelopment | ❌ | ❌ |
| 187 | Exploitation mission | 🟡 | 🟡 |
| 188 | Base assault mission | ✅ | 🟡 |
| 189 | Siege mission | ✅ | 🟡 |
| 190 | Harassment mission | 🟡 | 🟡 |
| 191 | Economy/harvester raid mission | ✅ | 🟡 |
| 192 | Production raid mission | ✅ | 🟡 |
| 193 | Expansion denial mission | 🟡 | 🟡 |
| 194 | Chokepoint/bridge seizure mission | ✅ | 🟡 |
| 195 | Naval blockade mission | ❌ | ❌ |
| 196 | Coastal bombardment mission | 🟡 | 🟡 |
| 197 | Air-strike mission | ✅ | 🟡 |
| 198 | Coordinated mass-air attack | 🟡 | 🟡 |
| 199 | Support-power strike mission | ✅ | 🟡 |
| **§12 Defensive Mission Types** | | | |
| 200 | Local defense mission | ✅ | 🟡 |
| 201 | Mobile defense mission | ✅ | 🟡 |
| 202 | Emergency reinforcement mission | 🟡 | 🟡 |
| 203 | Counterattack mission | ✅ | 🟡 |
| 204 | Interception mission | ✅ | 🟡 |
| 205 | Anti-air defensive umbrella | ✅ | 🟡 |
| 206 | Naval screening defense | ✅ | 🟡 |
| 207 | Retreat mission | ✅ | ✅ |
| 208 | Delaying-action mission | 🟡 | 🟡 |
| 209 | Evacuation mission | 🟡 | 🟡 |
| 210 | Escort/protection mission | ✅ | 🟡 |
| 211 | Defense proportional to threat | ✅ | 🟡 |
| 212 | Minor raids don't redirect whole army | ✅ | 🟡 |
| 213 | Critical structures higher defensive priority | ✅ | 🟡 |
| 214 | Defense triggers counterattack evaluation | ✅ | 🟡 |
| **§13 Recon & Intelligence Missions** | | | |
| 215 | General reconnaissance mission | ✅ | 🟡 |
| 216 | Deep reconnaissance | ✅ | 🟡 |
| 217 | Air reconnaissance | 🟡 | 🟡 |
| 218 | Naval reconnaissance | 🟡 | 🟡 |
| 219 | Route reconnaissance | 🟡 | 🟡 |
| 220 | Expansion-search mission | ✅ | 🟡 |
| 221 | Defense-probing mission | ✅ | 🟡 |
| 222 | Recon answers specific intel questions | ✅ | 🟡 |
| 223 | Information requirements have priorities | 🟡 | 🟡 |
| 224 | Value-of-information approximated | 🟡 | 🟡 |
| 225 | Recon assets not risked for irrelevant info | ✅ | 🟡 |
| 226 | New intel updates planning immediately | ✅ | 🟡 |
| **§14 Combined-Arms Operations** | | | |
| 227 | Major attacks contain multiple components | ✅ | 🟡 |
| 228 | Ground armor coordinated with infantry | ✅ | 🟡 |
| 229 | Ground armor coordinated with artillery | 🟡 | 🟡 |
| 230 | Ground forces coordinated with AA escorts | 🟡 | 🟡 |
| 231 | Ground attacks coordinated with air strikes | ✅ | 🟡 |
| 232 | Ground attacks coordinated with naval support | ✅ | 🟡 |
| 233 | Ground attacks coordinated with special ops | ✅ | 🟡 |
| 234 | Reconnaissance precedes main operation | ✅ | 🟡 |
| 235 | Shaping attacks precede main breach | ✅ | 🟡 |
| 236 | Deception precedes/accompanies main op | ✅ | 🟡 |
| 237 | Breach forces separated from exploitation | 🟡 | 🟡 |
| 238 | Reserve remains uncommitted during main attack | ✅ | 🟡 |
| 239 | Multiple allied players contribute to one op | ✅ | 🟡 |
| 240 | Multi-domain coordination without independence | ✅ | 🟡 |
| **§15 Operational Phasing** | | | |
| 241 | RECON phase | ✅ | ✅ |
| 242 | STAGING phase | ✅ | ✅ |
| 243 | SHAPING phase | ✅ | ✅ |
| 244 | DECEPTION phase | ✅ | ✅ |
| 245 | BREACH phase | ✅ | ✅ |
| 246 | EXPLOITATION phase | ✅ | ✅ |
| 247 | CONSOLIDATION/HOLD phase | ✅ | ✅ |
| 248 | WITHDRAWAL phase | ✅ | ✅ |
| 249 | Phase transitions have explicit conditions | ✅ | 🟡 |
| 250 | Phase transitions tested under disruptions | 🟡 | 🟡 |
| **§16 Synchronization & Time-on-Target** | | | |
| 251 | Separate forces can stage before launch | ✅ | 🟡 |
| 252 | Forces can wait for launch conditions | ✅ | 🟡 |
| 253 | Mission components have synchronized timing | ✅ | 🟡 |
| 254 | Air strikes precede ground by configured interval | 🟡 | 🟡 |
| 255 | Naval bombardment synchronizes with ground | 🟡 | 🟡 |
| 256 | Special ops launch during distraction windows | 🟡 | 🟡 |
| 257 | Reserve movement synchronizes with breakthrough | 🟡 | 🟡 |
| 258 | System accounts for different travel times | ✅ | 🟡 |
| 259 | Avoids one force arriving long before support | ✅ | 🟡 |
| 260 | Synchronization error measured in telemetry | ✅ | 🟡 |
| 261 | Time-on-target has automated scenario tests | 🟡 | 🟡 |
| **§17 Deception Framework** | | | |
| 262 | Feint mission | ✅ | ✅ |
| 263 | Demonstration mission | ✅ | ✅ |
| 264 | Probe attack | ✅ | 🟡 |
| 265 | Fake buildup | ❌ | ❌ |
| 266 | Diversionary raid | 🟡 | 🟡 |
| 267 | Fake retreat/bait mission | ✅ | ✅ |
| 268 | Decoy transport mission | 🟡 | 🟡 |
| 269 | False multi-axis pressure | ✅ | 🟡 |
| 270 | Deception defines intended enemy reaction | ✅ | ✅ |
| 271 | Feint forces have stricter loss limits | 🟡 | 🟡 |
| 272 | Feints withdraw early once purpose achieved | 🟡 | 🟡 |
| 273 | Deception success measured by enemy behavior | ✅ | ✅ |
| 274 | Main op launches conditionally after feint | ✅ | 🟡 |
| 275 | Fake retreat pulls enemies into kill zones | ✅ | 🟡 |
| 276 | Bait force understands retreat = success | 🟡 | 🟡 |
| **§18 Human Attention Exploitation** | | | |
| 277 | Deliberately generate simultaneous threats | ✅ | 🟡 |
| 278 | Simultaneous threats on different map parts | ✅ | 🟡 |
| 279 | Simultaneous threats in different domains | ✅ | 🟡 |
| 280 | Main assault + raid + air + naval + special | ✅ | 🟡 |
| 281 | Simultaneous actions serve common purpose | ✅ | 🟡 |
| 282 | Exploit observed human overreaction | ✅ | 🟡 |
| 283 | Create distraction before high-value op | ✅ | 🟡 |
| 284 | Force human to choose between targets | ✅ | 🟡 |
| **§19 Special Operations** | | | |
| 285 | Special units excluded from generic squads | ✅ | 🟡 |
| 286 | Tanya as scarce strategic asset | ✅ | 🟡 |
| 287 | Spies as special-operation assets | ✅ | 🟡 |
| 288 | Engineers in deliberate capture operations | 🟡 | 🟡 |
| 289 | Special-ops targets scored by consequence | ✅ | 🟡 |
| 290 | Production infrastructure targeted | ✅ | ✅ |
| 291 | Technology infrastructure targeted | ✅ | ✅ |
| 292 | Economy infrastructure targeted | ✅ | ✅ |
| 293 | Support-power infrastructure targeted | ✅ | ✅ |
| 294 | Isolated/high-value rear targets | ✅ | 🟡 |
| 295 | Special-ops evaluates probability of success | 🟡 | 🟡 |
| 296 | Special-ops evaluates strategic value | ✅ | 🟡 |
| 297 | Special-ops evaluates asset-loss risk | ✅ | 🟡 |
| 298 | Special units wait for timing window | ✅ | 🟡 |
| 299 | Special ops synchronized with distractions | 🟡 | 🟡 |
| 300 | Abort conditions for compromised operations | 🟡 | 🟡 |
| 301 | Surviving special assets extracted and reused | ✅ | ✅ |
| **§20 Transport Operations** | | | |
| 302 | Transport missions have explicit state machines | ✅ | ✅ |
| 303 | ASSEMBLE state | ✅ | ✅ |
| 304 | LOAD state | ✅ | ✅ |
| 305 | WAIT_FOR_WINDOW state | ✅ | ✅ |
| 306 | TRANSIT state | ✅ | ✅ |
| 307 | APPROACH state | ✅ | ✅ |
| 308 | UNLOAD state | ✅ | ✅ |
| 309 | RETREAT/HOLD state | ✅ | ✅ |
| 310 | EXTRACTION_REQUEST state | ✅ | ✅ |
| 311 | RETURN_FOR_EXTRACTION | ✅ | ✅ |
| 312 | RELOAD | ✅ | ✅ |
| 313 | EXTRACT | ✅ | ✅ |
| 314 | Transport routes prioritize safety | ✅ | ✅ |
| 315 | Aircraft transports avoid AA | ✅ | ✅ |
| 316 | Naval transports avoid naval threats | 🟡 | 🟡 |
| 317 | Transport avoids combat zones for stealth | ✅ | ✅ |
| 318 | Transport route replanned during transit | 🟡 | 🟡 |
| 319 | Mission aborts when safe transit impossible | ✅ | ✅ |
| 320 | Insertion route planned before launch | ✅ | 🟡 |
| 321 | Extraction route planned before launch | 🟡 | 🟡 |
| 322 | Transport survival measured | ✅ | 🟡 |
| **§21 Strategic Posture System** | | | |
| 323 | Global strategic posture exists | ✅ | ✅ |
| 324 | OPENING posture | ✅ | ✅ |
| 325 | EXPANSION posture | 🟡 | ❌ |
| 326 | PRESSURE posture | ✅ | 🟡 |
| 327 | CONTAINMENT posture | 🟡 | ❌ |
| 328 | ATTRITION posture | 🟡 | ❌ |
| 329 | BREAKTHROUGH posture | ✅ | 🟡 |
| 330 | SIEGE posture | ✅ | 🟡 |
| 331 | RAIDING posture | 🟡 | ❌ |
| 332 | DEFENSIVE posture | ✅ | ✅ |
| 333 | COUNTERATTACK posture | 🟡 | ❌ |
| 334 | RECOVERY posture | 🟡 | ❌ |
| 335 | DESPERATION posture | ✅ | 🟡 |
| 336 | ALL_IN posture | ✅ | 🟡 |
| 337 | Posture affects production priorities | 🟡 | ❌ |
| 338 | Posture affects acceptable combat risk | 🟡 | ❌ |
| 339 | Posture affects reserve requirements | ✅ | ✅ |
| 340 | Posture affects target-selection weights | ✅ | ✅ |
| 341 | Different theaters with different postures | ❌ | ❌ |
| 342 | Commander can change posture | ✅ | ✅ |
| **§22 Main Effort & Force Concentration** | | | |
| 343 | Commander identifies primary/main effort | ✅ | 🟡 |
| 344 | Secondary operations support main effort | 🟡 | ❌ |
| 345 | AI avoids attacking all fronts equally | 🟡 | ❌ |
| 346 | Local superiority > total-map unit count | ✅ | 🟡 |
| 347 | AI can mass forces against vulnerable area | ✅ | 🟡 |
| 348 | Other fronts retain defensive capability | 🟡 | 🟡 |
| 349 | Concentration creates counterattack exposure | 🟡 | ❌ |
| 350 | Breakthrough followed by exploitation forces | 🟡 | 🟡 |
| **§23 Strategic Reserve** | | | |
| 351 | Coalition-level strategic reserve exists | 🟡 | 🟡 |
| 352 | Reserve size configurable/dynamic | ✅ | ✅ |
| 353 | ~10-25% reserve behavior | ✅ | 🟡 |
| 354 | Reserve not casually consumed by routine missions | ✅ | 🟡 |
| 355 | Reserve can stop counterattacks | 🟡 | 🟡 |
| 356 | Reserve can reinforce failing fronts | 🟡 | ❌ |
| 357 | Reserve can exploit breakthroughs | 🟡 | ❌ |
| 358 | Reserve can intercept transports/raids | 🟡 | 🟡 |
| 359 | Reserve can protect expansions | ❌ | ❌ |
| 360 | LLM must justify consuming last reserve | ❌ | ❌ |
| 361 | Reserve availability visible in context | ✅ | 🟡 |
| 362 | Reserve usage tracked in telemetry | ✅ | 🟡 |
| **§24 Target Evaluation** | | | |
| 363 | Target-scoring system exists | ✅ | ✅ |
| 364 | Strategic value considered | ✅ | 🟡 |
| 365 | Economic damage considered | ✅ | ✅ |
| 366 | Production denial considered | ✅ | ✅ |
| 367 | Technology denial considered | ✅ | ✅ |
| 368 | Positional value considered | ✅ | ❌ |
| 369 | Information value considered | ✅ | ✅ |
| 370 | Follow-on opportunity considered | ✅ | ❌ |
| 371 | Expected friendly losses considered | ✅ | 🟡 |
| 372 | Travel cost considered | ✅ | 🟡 |
| 373 | Enemy reinforcement risk considered | ✅ | 🟡 |
| 374 | Counterattack risk considered | ✅ | 🟡 |
| 375 | Intelligence uncertainty considered | ✅ | ✅ |
| 376 | Target weights change with posture | ✅ | ✅ |
| 377 | AI avoids wasting force on low-value targets | 🟡 | ❌ |
| **§25 Production & Capability Planning** | | | |
| 378 | Production planning at coalition level | ✅ | 🟡 |
| 379 | Reasons in capabilities not just unit names | ✅ | ✅ |
| 380 | Anti-armor requirement tracked | ✅ | ✅ |
| 381 | Anti-infantry requirement tracked | ✅ | ✅ |
| 382 | Anti-air requirement tracked | ✅ | ✅ |
| 383 | Artillery requirement tracked | ✅ | ✅ |
| 384 | Recon requirement tracked | 🟡 | 🟡 |
| 385 | Mobility requirement tracked | 🟡 | 🟡 |
| 386 | Fast-raiding requirement tracked | 🟡 | 🟡 |
| 387 | Naval requirement tracked | ✅ | ✅ |
| 388 | Air-superiority requirement tracked | 🟡 | 🟡 |
| 389 | Transport requirement tracked | 🟡 | 🟡 |
| 390 | Special-operations requirement tracked | 🟡 | 🟡 |
| 391 | Base-defense requirement tracked | ✅ | ✅ |
| 392 | Capability requirements respond to enemy comp | ✅ | ✅ |
| 393 | Allied players can specialize production | ✅ | 🟡 |
| 394 | Specialization respects tech tree | ✅ | 🟡 |
| 395 | Coalition avoids unnecessary duplication | 🟡 | ❌ |
| 396 | React to destroyed production infrastructure | 🟡 | 🟡 |
| 397 | React to new enemy air/naval threats | ✅ | 🟡 |
| 398 | React to strategic posture | 🟡 | ❌ |
| 399 | Excessive resource floating detected | ✅ | 🟡 |
| 400 | Money reserved for planned infrastructure/tech | 🟡 | 🟡 |
| 401 | Emergency replacement production | ❌ | ❌ |
| **§26 Economy & Expansion** | | | |
| 402 | Understands each ally's economy separately | ✅ | 🟡 |
| 403 | Refinery/resource capacity tracked | ✅ | 🟡 |
| 404 | Harvester status tracked | ✅ | 🟡 |
| 405 | Resource depletion influences planning | ✅ | 🟡 |
| 406 | Expansion opportunities scored | ✅ | 🟡 |
| 407 | Expansion risk evaluated | ✅ | 🟡 |
| 408 | Expansion timing changes with posture | 🟡 | 🟡 |
| 409 | Ally assigned to expansion specialization | 🟡 | ❌ |
| 410 | New expansions receive defensive planning | ✅ | 🟡 |
| 411 | Economic vulnerability influences defense | ✅ | 🟡 |
| 412 | Enemy economic weakness triggers raiding | ✅ | 🟡 |
| **§27 Opponent Modeling** | | | |
| 413 | Opponent-model object exists | ✅ | ✅ |
| 414 | Enemy army composition tendencies tracked | ✅ | ✅ |
| 415 | Enemy armor bias tracked | ✅ | ✅ |
| 416 | Enemy infantry bias tracked | ✅ | ✅ |
| 417 | Enemy air bias tracked | ✅ | ✅ |
| 418 | Enemy naval bias tracked | ✅ | ✅ |
| 419 | Enemy static-defense bias tracked | ✅ | ✅ |
| 420 | Preferred attack lanes tracked | ✅ | 🟡 |
| 421 | Human reaction speed estimated | ✅ | ✅ |
| 422 | Response to harassment tracked | 🟡 | 🟡 |
| 423 | Response to feints tracked | 🟡 | ❌ |
| 424 | Tendency to redeploy whole army tracked | ✅ | ✅ |
| 425 | Expansion timing behavior tracked | 🟡 | ❌ |
| 426 | Defensive/turtling behavior tracked | 🟡 | 🟡 |
| 427 | Aggressive/rush behavior tracked | 🟡 | 🟡 |
| 428 | Opponent-model confidence values exist | ✅ | ✅ |
| 429 | Commander exploits reliable patterns | 🟡 | ❌ |
| 430 | Commander doesn't treat history as guaranteed | 🟡 | 🟡 |
| 431 | Opponent model updates during match | ✅ | ✅ |
| 432 | Validated vs multiple scripted opponents | ✅ | 🟡 |
| **§28 Counterattack Intelligence** | | | |
| 433 | Estimate where attackers originated | 🟡 | ❌ |
| 434 | Evaluate what enemy exposed by attacking | 🟡 | ❌ |
| 435 | Check if enemy reinforcements depleted | 🟡 | ❌ |
| 436 | Consider immediate counterattack opportunities | ✅ | 🟡 |
| 437 | Don't counterattack when unfavorable | ✅ | 🟡 |
| 438 | Counterattack considers production windows | 🟡 | ❌ |
| 439 | Counterattack scenarios covered by tests | 🟡 | ❌ |
| **§29 LLM World Context Compression** | | | |
| 440 | Raw actor dumps not sent to LLM | ✅ | ❌ |
| 441 | Large groups summarized into army-group records | ✅ | ❌ |
| 442 | Army groups include unit composition | ✅ | ❌ |
| 443 | Army groups include strength | ✅ | ❌ |
| 444 | Army groups include readiness | ✅ | ❌ |
| 445 | Army groups include location | ❌ | ❌ |
| 446 | Army groups include mission assignment | ✅ | ❌ |
| 447 | Army groups include nearby known threats | ❌ | ❌ |
| 448 | Important unique units individually represented | 🟡 | ❌ |
| 449 | Enemy force estimates contain min/expected/max | 🟡 | ✅ |
| 450 | Confidence values exposed to LLM | 🟡 | ✅ |
| 451 | Recent significant events summarized | ✅ | 🟡 |
| 452 | Unresolved uncertainties summarized | ✅ | 🟡 |
| 453 | Context-size limits handled gracefully | ✅ | ❌ |
| 454 | LLM receives only relevant information | ✅ | ❌ |
| **§30 LLM Intelligence Tools** | | | |
| 455 | get_global_summary() exists | ✅ | ✅ |
| 456 | inspect_region() exists | ✅ | ✅ |
| 457 | inspect_force() exists | ✅ | ✅ |
| 458 | inspect_enemy_intelligence() exists | ✅ | ✅ |
| 459 | get_recent_events() exists | ✅ | ✅ |
| 460 | get_opponent_model() exists | ✅ | ✅ |
| 461 | get_uncertainties() exists | ✅ | ✅ |
| 462 | Tool outputs structured and machine-readable | ✅ | ✅ |
| 463 | Tool outputs distinguish fact from inference | ✅ | ✅ |
| 464 | Tool calls cannot expose hidden info in fair-fog | ✅ | 🟡 |
| **§31 LLM Analysis Tools** | | | |
| 465 | estimate_engagement() exists | ✅ | ✅ |
| 466 | compare_force_packages() exists | ✅ | 🟡 |
| 467 | plan_routes() exists | ✅ | ✅ |
| 468 | score_targets() exists | ✅ | ✅ |
| 469 | estimate_enemy_response() exists | ✅ | 🟡 |
| 470 | find_attack_windows() exists | ✅ | 🟡 |
| 471 | find_special_ops_routes() exists | ✅ | 🟡 |
| 472 | Analysis tools validate inputs | ✅ | ✅ |
| 473 | Invalid IDs produce errors not hallucinations | ✅ | ✅ |
| 474 | Tool results deterministic where expected | ✅ | ✅ |
| **§32 LLM Economy/Production Tools** | | | |
| 475 | get_economy_state() exists | ✅ | ✅ |
| 476 | get_production_state() exists | ✅ | ✅ |
| 477 | set_production_directive() exists | ❌ | ❌ |
| 478 | set_expansion_priority() exists | ❌ | ❌ |
| 479 | request_capability() exists | ❌ | ❌ |
| 480 | Production directives respect prerequisites | ✅ | 🟡 |
| 481 | Production directives respect available cash | ✅ | ❌ |
| 482 | Production directives respect queue availability | ✅ | ❌ |
| 483 | Invalid production requests rejected with reasons | ✅ | ✅ |
| **§33 LLM Mission/Command Tools** | | | |
| 484 | create_mission() exists | 🟡 | ✅ |
| 485 | modify_mission() exists | ❌ | ❌ |
| 486 | cancel_mission() exists | ❌ | ❌ |
| 487 | assign_force() exists | ❌ | ❌ |
| 488 | release_force() exists | ❌ | ❌ |
| 489 | set_reserve() exists | ❌ | ❌ |
| 490 | request_recon() exists | 🟡 | 🟡 |
| 491 | set_strategic_posture() exists | 🟡 | ✅ |
| 492 | get_mission_status() exists | ✅ | 🟡 |
| 493 | get_force_readiness() exists | ✅ | 🟡 |
| 494 | get_transport_status() exists | ✅ | 🟡 |
| 495 | get_route_status() exists | ✅ | 🟡 |
| 496 | LLM mission commands pass through validation | ✅ | ✅ |
| 497 | LLM does not require move_unit() control | ✅ | ❌ |
| 498 | LLM does not require attack_unit() control | ✅ | ❌ |
| 499 | Emergency direct-control API tightly constrained | 🟡 | ❌ |
| **§34 LLM Commander Behavior** | | | |
| 500 | System prompt: coalition victory = primary objective | ✅ | ❌ |
| 501 | LLM distinguishes known/observed/inferred/suspected/unknown | ✅ | 🟡 |
| 502 | LLM instructed not to fabricate engine state | ✅ | ❌ |
| 503 | LLM checks available forces before assigning | 🟡 | ❌ |
| 504 | LLM checks existing commitments before creating conflicts | 🟡 | ✅ |
| 505 | LLM identifies explicit strategic posture | ✅ | ✅ |
| 506 | LLM identifies explicit main effort | ✅ | ❌ |
| 507 | Major operations have explicit objectives | 🟡 | ❌ |
| 508 | Major operations have launch conditions | ✅ | ❌ |
| 509 | Major operations have abort conditions | 🟡 | ❌ |
| 510 | Major operations have contingencies | 🟡 | ❌ |
| 511 | Major operations consider reserves | ✅ | ❌ |
| 512 | Major operations consider enemy responses | 🟡 | ❌ |
| 513 | Major operations consider recon requirements | ✅ | ❌ |
| 514 | Major operations consider deception opportunities | ✅ | ❌ |
| 515 | Major operations consider combined arms | ✅ | ❌ |
| 516 | Major operations consider withdrawal/extraction | 🟡 | ❌ |
| 517 | LLM adapts when assumptions become invalid | 🟡 | 🟡 |
| 518 | LLM doesn't constantly rewrite valid plans | 🟡 | ❌ |
| 519 | LLM accepts strategically justified losses | ❌ | ❌ |
| 520 | LLM avoids strategically pointless losses | 🟡 | ❌ |
| 521 | LLM exploits major enemy mistakes rapidly | ❌ | ❌ |
| **§35 Event-Driven Strategic Reasoning** | | | |
| 522 | LLM not called every game tick | ✅ | ✅ |
| 523 | Periodic strategic review exists | ✅ | ✅ |
| 524 | Enemy base discovery triggers review | ✅ | ✅ |
| 525 | Enemy expansion discovery triggers review | ✅ | ✅ |
| 526 | Major enemy composition changes trigger review | ✅ | ✅ |
| 527 | Major allied attack start triggers review | 🟡 | 🟡 |
| 528 | Major attack failure triggers review | 🟡 | 🟡 |
| 529 | Important allied base attack triggers review | ✅ | ✅ |
| 530 | Critical production-building loss triggers review | ✅ | ✅ |
| 531 | Special-unit availability triggers review | ✅ | ✅ |
| 532 | Transport readiness triggers review | 🟡 | 🟡 |
| 533 | Mission completion triggers review | 🟡 | 🟡 |
| 534 | Major route/bridge loss triggers review | 🟡 | 🟡 |
| 535 | Major economy change triggers review | 🟡 | 🟡 |
| 536 | Support-power readiness triggers review | ✅ | ✅ |
| 537 | High-value enemy structure discovery triggers review | ✅ | ✅ |
| 538 | Loss of contact with enemy army triggers review | ✅ | ✅ |
| 539 | Event storms throttled/debounced | ✅ | ✅ |
| **§36 Tactical Execution Layer** | | | |
| 540 | Ground controller exists | ✅ | 🟡 |
| 541 | Air controller exists | ✅ | 🟡 |
| 542 | Naval controller exists | ✅ | 🟡 |
| 543 | Transport controller exists | ✅ | ✅ |
| 544 | Special-operations controller exists | ✅ | 🟡 |
| 545 | Formation/cohesion controller exists | 🟡 | 🟡 |
| 546 | Micro/targeting controller exists | 🟡 | 🟡 |
| 547 | Controllers execute without repeated LLM intervention | ✅ | 🟡 |
| 548 | Controllers can report inability | 🟡 | 🟡 |
| 549 | Controllers can request replanning | 🟡 | 🟡 |
| 550 | Controllers handle pathfinding at engine speed | ✅ | 🟡 |
| 551 | Controllers handle target acquisition at engine speed | ✅ | 🟡 |
| 552 | Controllers handle formation movement at engine speed | ✅ | 🟡 |
| 553 | Controllers handle transport loading/unloading reliably | ✅ | 🟡 |
| 554 | Controllers preserve mission-critical assets | ✅ | 🟡 |
| 555 | Controllers avoid obvious suicide behavior | ✅ | 🟡 |
| **§37 Formation & Force Cohesion** | | | |
| 556 | Assault groups avoid arriving as isolated units | ✅ | 🟡 |
| 557 | Artillery remains behind screening forces | ❌ | ❌ |
| 558 | AA units within useful coverage of valuable forces | 🟡 | 🟡 |
| 559 | Fast units do not outrun required support | ❌ | ❌ |
| 560 | Groups pause/regroup when cohesion falls too low | ✅ | 🟡 |
| 561 | Retreating forces maintain withdrawal behavior | ✅ | 🟡 |
| 562 | Formation/cohesion thresholds configurable | ✅ | 🟡 |
| 563 | Force cohesion included in readiness calculations | ✅ | 🟡 |
| 564 | Force cohesion logged in telemetry | ✅ | ✅ |
| **§38 Support Powers** | | | |
| 565 | Support-power readiness visible to strategic command | ✅ | 🟡 |
| 566 | Support powers integrated into mission planning | ✅ | 🟡 |
| 567 | Support powers participate in shaping attacks | 🟡 | 🟡 |
| 568 | Target scoring avoids unacceptable friendly-fire | ✅ | ✅ |
| 569 | Support powers synchronized with attacks | 🟡 | 🟡 |
| 570 | AI avoids wasting powers on low-value targets | ✅ | 🟡 |
| 571 | Support-power behavior covered by tests per RA power | 🟡 | 🟡 |
| **§39 Failure Handling & Fallback** | | | |
| 572 | LLM timeouts do not freeze game AI | ✅ | ✅ |
| 573 | LLM API failures do not freeze active missions | ✅ | ✅ |
| 574 | Invalid LLM JSON/tool output safely rejected | ✅ | ✅ |
| 575 | Hallucinated force IDs rejected | ✅ | ✅ |
| 576 | Hallucinated targets rejected | ✅ | ✅ |
| 577 | Impossible routes rejected | ✅ | ✅ |
| 578 | Impossible production orders rejected | ✅ | ✅ |
| 579 | Existing valid missions continue during LLM failure | ✅ | 🟡 |
| 580 | Deterministic strategic fallback activates | ✅ | ✅ |
| 581 | Fallback can defend the coalition | 🟡 | 🟡 |
| 582 | Fallback can continue production | ✅ | 🟡 |
| 583 | Fallback can create basic attacks/counterattacks | ✅ | 🟡 |
| 584 | Recovery from fallback to LLM command is safe | ✅ | ✅ |
| 585 | Failure cases covered by automated tests | ✅ | ✅ |
| **§40 Strategic Decision Logging** | | | |
| 586 | Every major LLM strategic decision logged | ✅ | 🟡 |
| 587 | Strategic-posture changes logged | ✅ | 🟡 |
| 588 | Mission creation logged | ✅ | 🟡 |
| 589 | Mission cancellation logged | ✅ | 🟡 |
| 590 | Mission failure reasons logged | ✅ | 🟡 |
| 591 | Production-priority changes logged | ❌ | ❌ |
| 592 | Reserve commitments logged | ✅ | 🟡 |
| 593 | Intelligence discoveries affecting plans logged | ✅ | 🟡 |
| 594 | Significant opponent-model changes logged | ✅ | 🟡 |
| 595 | Combat-estimator results for major decisions logged | 🟡 | 🟡 |
| 596 | LLM tool calls reconstructable from logs | 🟡 | ❌ |
| 597 | LLM responses correlated with game timestamps | ✅ | 🟡 |
| 598 | Logs usable for debugging bad decisions | ✅ | 🟡 |
| **§41 Telemetry & Quality Metrics** | | | |
| 599 | Win/loss recorded | ❌ | ❌ |
| 600 | Match duration recorded | 🟡 | ❌ |
| 601 | Friendly combat value lost recorded | ✅ | ✅ |
| 602 | Enemy combat value destroyed recorded | ✅ | ✅ |
| 603 | Exchange ratio recorded | ✅ | ✅ |
| 604 | Economic damage caused recorded | ❌ | ❌ |
| 605 | Economic damage suffered recorded | ❌ | ❌ |
| 606 | Production idle time recorded | ✅ | 🟡 |
| 607 | Excess resource floating recorded | ✅ | 🟡 |
| 608 | Expansion timing recorded | ❌ | ❌ |
| 609 | Army idle time recorded | ✅ | ✅ |
| 610 | Force cohesion recorded | ✅ | ✅ |
| 611 | Mission success rate recorded | 🟡 | ❌ |
| 612 | Synchronization error recorded | ❌ | ❌ |
| 613 | Local combat superiority at engagement recorded | ❌ | ❌ |
| 614 | Retreat timing/effectiveness recorded | ❌ | ❌ |
| 615 | Strategic reserve availability recorded | ✅ | 🟡 |
| 616 | Recon efficiency recorded | ❌ | ❌ |
| 617 | Transport survival recorded | ❌ | ❌ |
| 618 | Tanya/Spy/special-op success recorded | 🟡 | ❌ |
| 619 | Feint effectiveness recorded | 🟡 | ❌ |
| 620 | Counterattack effectiveness recorded | ❌ | ❌ |
| 621 | Base-defense response time recorded | ❌ | ❌ |
| 622 | Opponent-model prediction accuracy recorded | 🟡 | ❌ |
| **§42 Deception Metrics** | | | |
| 623 | Feint effectiveness measures enemy value redeployed | ✅ | ✅ |
| 624 | Feint compares enemy reaction vs friendly value risked | 🟡 | 🟡 |
| 625 | AI distinguishes distraction from tactical success | 🟡 | 🟡 |
| 626 | Enemy reaction to bait operations recorded | ✅ | 🟡 |
| 627 | Whether feint opened main-attack window recorded | ❌ | ❌ |
| 628 | Repeatedly ineffective deception deprioritized | ✅ | ✅ |
| **§43 Difficulty Configuration** | | | |
| 629 | Command-quality configurable independently | ✅ | ✅ |
| 630 | Reaction speed configurable independently | ✅ | ✅ |
| 631 | Economic bonus configurable independently | ✅ | ✅ |
| 632 | Intelligence/fog advantage configurable independently | ✅ | ✅ |
| 633 | Micro precision configurable independently | ✅ | ✅ |
| 634 | Coalition coordination strength configurable independently | ✅ | ✅ |
| 635 | Strong fair mode with zero economic cheating | ✅ | ✅ |
| 636 | Fair-fog Supreme Command mode exists | ✅ | ✅ |
| 637 | Optional omniscient mode exists | ✅ | ✅ |
| 638 | Difficulty settings exposed via YAML/config | ✅ | 🟡 |
| 639 | Difficulty settings verified to change runtime behavior | ✅ | 🟡 |
| **§44 Fair-but-Brutal Target Configuration** | | | |
| 640 | Test config approximating brutal fair AI | ✅ | 🟡 |
| 641 | Tested without hidden economic advantages | ✅ | 🟡 |
| 642 | Tested without hidden enemy-position access | ✅ | 🟡 |
| 643 | Performance vs standard bots measured | 🟡 | ❌ |
| 644 | Performance vs multiple allied standard bots measured | 🟡 | ❌ |
| 645 | Performance vs experienced human players evaluated | 🟡 | ❌ |
| **§45 Automated Testing — Unit Level** | | | |
| 646 | World-state extractor unit tests exist | ✅ | 🟡 |
| 647 | Visibility/intelligence classification tests exist | ✅ | ✅ |
| 648 | Confidence-decay tests exist | ✅ | ✅ |
| 649 | Map-region analysis tests exist | ✅ | ✅ |
| 650 | Threat-map tests exist | ✅ | ✅ |
| 651 | Route-planner tests exist | ✅ | ✅ |
| 652 | Combat-estimator tests exist | ✅ | ✅ |
| 653 | Target-scoring tests exist | ✅ | ✅ |
| 654 | Force-registry tests exist | ✅ | ✅ |
| 655 | Order-arbitration tests exist | ✅ | ✅ |
| 656 | Mission-state-machine tests exist | ✅ | ✅ |
| 657 | Production-directive tests exist | ✅ | ✅ |
| 658 | Strategic-reserve tests exist | ✅ | 🟡 |
| 659 | Opponent-model tests exist | ✅ | ✅ |
| 660 | LLM command-validation tests exist | ✅ | ✅ |
| 661 | Invalid-tool-input tests exist | ✅ | ✅ |
| 662 | Serialization/deserialization tests for LLM schemas | ✅ | 🟡 |
| **§46 Automated Testing — Mission Scenarios** | | | |
| 663 | Basic coordinated ground attack scenario | ✅ | 🟡 |
| 664 | Multi-player coalition attack | ✅ | 🟡 |
| 665 | Ground + artillery attack | ✅ | ❌ |
| 666 | Ground + air attack | ✅ | ❌ |
| 667 | Ground + naval support | ✅ | ❌ |
| 668 | Ground + air + naval combined operation | ✅ | 🟡 |
| 669 | Feint followed by main assault | ✅ | 🟡 |
| 670 | Fake retreat into ambush | ✅ | ❌ |
| 671 | Harvester raid | ✅ | 🟡 |
| 672 | Expansion denial | ✅ | ❌ |
| 673 | Emergency base defense | ✅ | 🟡 |
| 674 | Reserve reinforcement | ✅ | 🟡 |
| 675 | Immediate counterattack after defense | ✅ | 🟡 |
| 676 | Air transport insertion | ✅ | 🟡 |
| 677 | Naval transport insertion | ✅ | 🟡 |
| 678 | Tanya operation | ✅ | ❌ |
| 679 | Spy operation | 🟡 | ❌ |
| 680 | Engineer capture operation | 🟡 | ❌ |
| 681 | Transport rerouting after new threat | ✅ | ❌ |
| 682 | Transport abort | ✅ | ✅ |
| 683 | Special-unit extraction | ✅ | ✅ |
| 684 | Simultaneous multi-front pressure | ✅ | ❌ |
| 685 | Enemy composition switch triggers production response | ✅ | 🟡 |
| 686 | Destroyed production facility triggers replanning | ✅ | 🟡 |
| 687 | Loss of bridge/route triggers replanning | ✅ | 🟡 |
| 688 | Enemy main army disappearance triggers recon | ✅ | 🟡 |
| 689 | LLM failure during major operation | ✅ | 🟡 |
| 690 | Invalid LLM mission tested | ✅ | ✅ |
| **§47 Stress & Scale Testing** | | | |
| 691 | AI tested on small maps | ✅ | ❌ |
| 692 | AI tested on large maps | ✅ | ❌ |
| 693 | AI tested with many allied players | ✅ | 🟡 |
| 694 | AI tested with many enemy players | 🟡 | ❌ |
| 695 | AI tested with hundreds of units | ✅ | 🟡 |
| 696 | AI tested with heavy air activity | ✅ | ❌ |
| 697 | AI tested with heavy naval activity | ✅ | ❌ |
| 698 | AI tested with many simultaneous missions | ✅ | ❌ |
| 699 | AI tested with frequent world-state changes | ✅ | ❌ |
| 700 | AI does not cause unacceptable frame/tick degradation | 🟡 | 🟡 |
| 701 | LLM context generation remains bounded | 🟡 | ❌ |
| 702 | Threat-map updates remain performant | ✅ | ❌ |
| 703 | Route planning remains performant | ✅ | 🟡 |
| 704 | Mission-management complexity remains bounded | 🟡 | ❌ |
| 705 | Memory leaks/actor-reference leaks tested | ✅ | 🟡 |
| **§48 Replay & Regression Testing** | | | |
| 706 | Deterministic/reproducible test seeds available | ✅ | ✅ |
| 707 | Known battle scenarios replayable automatically | 🟡 | 🟡 |
| 708 | AI decisions inspectable alongside replay timestamps | ✅ | 🟡 |
| 709 | Previously discovered strategic bugs have regression tests | ✅ | 🟡 |
| 710 | Previously discovered transport bugs have regression tests | ✅ | ❌ |
| 711 | Previously discovered order-conflict bugs have regression tests | ✅ | 🟡 |
| 712 | Previously discovered fog-of-war leaks have regression tests | 🟡 | 🟡 |
| 713 | Combat estimation benchmarked against historical scenarios | 🟡 | 🟡 |
| 714 | Strategic behavior compared against baseline win rates | ✅ | 🟡 |
| **§49 Self-Play & Optimization** | | | |
| 715 | AI-vs-AI self-play runs automatically | ✅ | ✅ |
| 716 | Multiple maps in batch evaluation | ✅ | ❌ |
| 717 | Multiple factions/configurations included | 🟡 | 🟡 |
| 718 | Strategic parameters varied experimentally | ✅ | ❌ |
| 719 | Threat weights tunable | ❌ | ❌ |
| 720 | Retreat thresholds tunable | ✅ | 🟡 |
| 721 | Reserve percentages tunable | ✅ | ❌ |
| 722 | Production capability weights tunable | ❌ | ❌ |
| 723 | Target-scoring weights tunable | ❌ | ❌ |
| 724 | Feint commitment thresholds tunable | ❌ | ❌ |
| 725 | Special-ops risk thresholds tunable | ❌ | ❌ |
| 726 | Changes evaluated on more than raw win rate | ✅ | ✅ |
| 727 | Overfitting to one map/opponent checked | ✅ | ❌ |
| **§50 LLM Strategic Evaluation** | | | |
| 728 | Same game state replayed through multiple decisions | ❌ | ❌ |
| 729 | LLM plans scored for legality | ❌ | ❌ |
| 730 | LLM plans scored for force availability | ❌ | ❌ |
| 731 | LLM plans scored for mission completeness | ❌ | ❌ |
| 732 | LLM plans scored for unnecessary risk | ❌ | ❌ |
| 733 | LLM plans compared with deterministic baseline | ❌ | ❌ |
| 734 | LLM decisions checked for strategic oscillation | ❌ | ❌ |
| 735 | LLM decisions checked for repeated impossible commands | ❌ | ❌ |
| 736 | LLM decisions checked for misuse of uncertain intel | ❌ | ❌ |
| 737 | LLM decisions checked for failing to maintain reserves | ❌ | ❌ |
| 738 | LLM decisions checked for excessive idle forces | 🟡 | ✅ |
| **§51 Information-Security / Game-Rule Integrity** | | | |
| 739 | Fair-fog cannot access hidden enemies via engine refs | ✅ | ❌ |
| 740 | Tool APIs enforce visibility restrictions | ✅ | ❌ |
| 741 | LLM cannot bypass production prerequisites | ✅ | ❌ |
| 742 | LLM cannot spend money a player doesn't have | ✅ | ❌ |
| 743 | LLM cannot issue orders to enemy units | ✅ | ❌ |
| 744 | LLM cannot issue orders to ungranted allied units | ✅ | ❌ |
| 745 | LLM cannot teleport or bypass movement rules | ✅ | ❌ |
| 746 | LLM cannot create nonexistent units/buildings | ✅ | ❌ |
| 747 | Commands validated engine-side not trusted from LLM | ✅ | ✅ |
| **§52 Code Organization** | | | |
| 748 | Coalition command isolated module | ✅ | ✅ |
| 749 | Coalition blackboard isolated | ✅ | ✅ |
| 750 | Force registry isolated | ✅ | ✅ |
| 751 | Order arbiter isolated | ✅ | ✅ |
| 752 | Intelligence tracker isolated | ✅ | ✅ |
| 753 | Opponent model isolated | ✅ | 🟡 |
| 754 | Map analyzer isolated | ✅ | 🟡 |
| 755 | Threat analysis isolated | 🟡 | 🟡 |
| 756 | Combat evaluator isolated | ✅ | 🟡 |
| 757 | Route planner isolated | ✅ | 🟡 |
| 758 | Target evaluator isolated | ✅ | 🟡 |
| 759 | Mission manager isolated | ✅ | ✅ |
| 760 | Ground controller isolated | ✅ | 🟡 |
| 761 | Air controller isolated | ✅ | 🟡 |
| 762 | Naval controller isolated | ✅ | 🟡 |
| 763 | Transport controller isolated | ✅ | 🟡 |
| 764 | Special-ops controller isolated | ✅ | 🟡 |
| 765 | Production director isolated | ✅ | ✅ |
| 766 | Strategic reserve manager isolated | 🟡 | 🟡 |
| 767 | LLM adapter isolated from core gameplay | ✅ | ✅ |
| 768 | Command validator isolated and testable | ✅ | ✅ |
| 769 | Deterministic fallback isolated and testable | ✅ | ✅ |
| 770 | Telemetry/logging isolated from decision logic | ✅ | ✅ |
| **§53 Documentation** | | | |
| 771 | Architecture documented | ✅ | N/A |
| 772 | Coalition-control model documented | ✅ | N/A |
| 773 | Fog-of-war information policy documented | ✅ | N/A |
| 774 | LLM tool API documented | ✅ | N/A |
| 775 | Mission schema documented | 🟡 | N/A |
| 776 | Force/army-group schema documented | 🟡 | N/A |
| 777 | Enemy-intelligence schema documented | ✅ | N/A |
| 778 | Threat-map model documented | 🟡 | N/A |
| 779 | Route-cost model documented | 🟡 | N/A |
| 780 | Combat-estimator assumptions documented | 🟡 | N/A |
| 781 | Strategic-posture behavior documented | 🟡 | N/A |
| 782 | Production/capability system documented | 🟡 | N/A |
| 783 | Opponent-model features documented | 🟡 | N/A |
| 784 | Failure/fallback behavior documented | ✅ | N/A |
| 785 | Difficulty settings documented | ✅ | N/A |
| 786 | Testing instructions documented | ✅ | N/A |
| 787 | Batch/self-play evaluation documented | ✅ | N/A |
| 788 | Decision-log format documented | 🟡 | N/A |
| **§Final Acceptance Tests** | | | |
| 789 | Unified Coalition Test (3+ allied AI as one command) | ✅ | 🟡 |
| 790 | Combined Arms Test (ground+artillery+air+naval sync) | ✅ | 🟡 |
| 791 | Deception Test (feint→enemy reaction→real attack) | ✅ | 🟡 |
| 792 | Special Operations Test (Tanya/Spy transport+extract) | ✅ | 🟡 |
| 793 | Human Attention Test (simultaneous coordinated threats) | ✅ | ❌ |
| 794 | Counter-Composition Test (enemy comp switch→production) | ✅ | 🟡 |
| 795 | Reserve Test (reserve remains, then reacts/exploits) | ✅ | 🟡 |
| 796 | Counterattack Test (enemy overcommits→AI counterattacks) | ✅ | 🟡 |
| 797 | Intelligence Test (loses sight→uncertain→recon not cheat) | ✅ | 🟡 |
| 798 | Fairness Test (fair fog + 0% bonus = no cheating) | ✅ | 🟡 |
| 799 | LLM Failure Test (LLM down mid-battle→missions continue) | ✅ | 🟡 |
| 800 | Invalid Commander Test (impossible op→reject→replan) | ✅ | 🟡 |
| 801 | Adaptation Test (valid strategy→poor→cancel/modify) | ✅ | ❌ |
| 802 | Withdrawal Test (losing engagement→preserve forces) | ✅ | 🟡 |
| 803 | Campaign Test (full match: recon→econ→pressure→ops→win) | ✅ | 🟡 |
| 804 | Brutal Fair-AI Test (extreme/supreme/fair/0% > standard) | ✅ | 🟡 |

---

## Summary

| Status | Impl | Test |
|--------|------|------|
| ✅ Complete | 520 | 280 |
| 🟡 Partial | 184 | 175 |
| ❌ Missing | 100 | 349 |
| **Total** | **804** | **804** |

**Implemented (any degree):** 704 / 804 (87.6%)
**Tested (any degree):** 455 / 804 (56.6%)
**Fully missing:** 100 / 804 (12.4%)
