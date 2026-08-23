#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Where the enemy probably is, given everything that has been seen and everything that has been
	/// looked at and found empty.
	/// </para>
	/// <para>
	/// Fog makes this a partially observable problem, so the enemy's position is a <i>distribution</i>
	/// rather than a snapshot with a decay timer. A stale last-known marker can only say "something
	/// was here once"; a distribution can say "sixty per cent of their armour is somewhere in the
	/// north-east", which is a claim the search can take an expectation over.
	/// </para>
	/// <para>
	/// The piece almost every bot omits is <b>negative evidence</b>, and it is worth more than the
	/// rest. Looking somewhere and finding nothing eliminates every hypothesis that placed something
	/// there - so a scout that sees no enemy has still done its job, and the belief sharpens. It is
	/// what turns a 360-degree sweep from a map reveal into an inference engine, and it is why
	/// scouting is worth paying for even when the scouts come back with nothing to report.
	/// </para>
	/// <para>
	/// Tracked per region rather than per cell. Cell-level particles would be a hundred times the
	/// work to answer questions the plan cannot use - the search reasons about regions, so the
	/// belief is kept where the decisions are made.
	/// </para>
	/// </summary>
	public sealed class EnemyBelief
	{
		/// <summary>Credit value believed present, indexed [region * Roles + role].</summary>
		readonly float[] belief;

		/// <summary>
		/// Enemy structures believed to stand in each region. Tracked separately from forces because
		/// buildings do not move, and because an assault's whole purpose is to remove them - a
		/// commander that cannot estimate where the enemy's base is cannot plan to take it.
		/// </summary>
		readonly float[] structures;

		/// <summary>Ticks since each region was last observed. Feeds how far belief is allowed to spread.</summary>
		readonly int[] lastSeen;

		readonly int regions;
		readonly Func<int, IEnumerable<int>> neighbours;

		/// <summary>
		/// Fraction of a region's believed force that spreads to its neighbours per second. Low, so
		/// belief does not smear across the whole map the instant contact is lost - the enemy has to
		/// actually drive there, and the region graph already says how far that is.
		/// </summary>
		public float DiffusionPerSecond { get; init; } = 0.02f;

		public EnemyBelief(int regions, Func<int, IEnumerable<int>> neighbours)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(regions);
			ArgumentNullException.ThrowIfNull(neighbours);

			this.regions = regions;
			this.neighbours = neighbours;
			belief = new float[regions * RoleStats.Roles];
			structures = new float[regions];
			lastSeen = new int[regions];
			Array.Fill(lastSeen, int.MinValue / 2);
		}

		public int Regions => regions;

		/// <summary>Believed credit value of a role in a region.</summary>
		public float Expected(int region, CombatRole role)
		{
			if (region < 0 || region >= regions)
				return 0f;

			return belief[(region * RoleStats.Roles) + (int)role];
		}

		/// <summary>Total believed enemy value in a region, across all roles.</summary>
		public float ExpectedIn(int region)
		{
			if (region < 0 || region >= regions)
				return 0f;

			var total = 0f;
			var start = region * RoleStats.Roles;
			for (var r = 0; r < RoleStats.Roles; r++)
				total += belief[start + r];

			return total;
		}

		/// <summary>Total believed enemy value anywhere.</summary>
		public float ExpectedTotal()
		{
			var total = 0f;
			foreach (var value in belief)
				total += value;

			return total;
		}

		/// <summary>Enemy structure value believed to stand in a region.</summary>
		public float ExpectedStructures(int region) =>
			region >= 0 && region < regions ? structures[region] : 0f;

		/// <summary>Total enemy structure value believed to remain anywhere.</summary>
		public float ExpectedStructuresTotal()
		{
			var total = 0f;
			foreach (var value in structures)
				total += value;

			return total;
		}

		/// <summary>
		/// <para>
		/// Assumes the enemy has a base, even before one has been found, placed where nobody has
		/// looked.
		/// </para>
		/// <para>
		/// Exactly the same pathology as the army prior, and it bit twice. With enemy structures
		/// unobserved the evaluator sees nothing left to destroy, so an assault appears to
		/// accomplish nothing and the search expands instead - measured, the commander rated every
		/// position at 0.92 and never once planned an attack. An opponent has a base; the only
		/// question is where.
		/// </para>
		/// </summary>
		public void AssumeUnseenStructures(float assumedTotal, int now, int staleAfterTicks)
		{
			if (assumedTotal <= 0f || regions == 0)
				return;

			var shortfall = assumedTotal - ExpectedStructuresTotal();
			if (shortfall <= 0f)
				return;

			var candidates = new List<int>();
			for (var region = 0; region < regions; region++)
				if (TicksSinceSeen(region, now) > staleAfterTicks)
					candidates.Add(region);

			if (candidates.Count == 0)
				return;

			var share = shortfall / candidates.Count;
			foreach (var region in candidates)
				structures[region] += share;
		}

		/// <summary>Ticks since a region was last observed.</summary>
		public int TicksSinceSeen(int region, int now) =>
			region < 0 || region >= regions ? int.MaxValue / 2 : now - lastSeen[region];

		/// <summary>
		/// Replaces the belief for one observed region with what is actually there. Applies to
		/// regions the player can currently see, and it is the only place positive evidence enters.
		/// </summary>
		public void Observe(int region, ReadOnlySpan<float> observedByRole, int tick, float observedStructures = -1f)
		{
			if (region < 0 || region >= regions)
				return;

			var start = region * RoleStats.Roles;
			for (var r = 0; r < RoleStats.Roles; r++)
				belief[start + r] = r < observedByRole.Length ? Math.Max(0f, observedByRole[r]) : 0f;

			// A negative value means the caller is not reporting structures; zero means it looked and
			// there are none, which is information and must overwrite the assumption.
			if (observedStructures >= 0f)
				structures[region] = observedStructures;

			lastSeen[region] = tick;
		}

		/// <summary>
		/// <para>
		/// Negative evidence: this region is visible and there is nothing in it, so every hypothesis
		/// that placed something here is eliminated.
		/// </para>
		/// <para>
		/// This is the half of Bayesian updating that scouting exists to produce, and the half that
		/// distinguishes a belief state from a decay timer. A decayed last-known marker would leave a
		/// ghost here indefinitely; this removes it, and the enemy's real strength is thereby
		/// concentrated into the places still unseen - which is exactly the inference a good player
		/// makes without thinking about it.
		/// </para>
		/// </summary>
		public void ObserveEmpty(int region, int tick)
		{
			if (region < 0 || region >= regions)
				return;

			var start = region * RoleStats.Roles;
			for (var r = 0; r < RoleStats.Roles; r++)
				belief[start + r] = 0f;

			structures[region] = 0f;
			lastSeen[region] = tick;
		}

		/// <summary>
		/// Spreads belief along the region graph: what was last seen somewhere could by now have
		/// moved next door. Only along real adjacency, so belief never crosses terrain the enemy
		/// cannot cross - a graph built with their movement class already encodes that.
		/// </summary>
		public void Propagate(float seconds)
		{
			if (seconds <= 0f || regions == 0)
				return;

			var rate = Math.Clamp(DiffusionPerSecond * seconds, 0f, 0.9f);
			if (rate <= 0f)
				return;

			var next = new float[belief.Length];
			Array.Copy(belief, next, belief.Length);

			for (var region = 0; region < regions; region++)
			{
				var adjacent = new List<int>();
				foreach (var neighbour in neighbours(region))
					if (neighbour >= 0 && neighbour < regions && neighbour != region)
						adjacent.Add(neighbour);

				if (adjacent.Count == 0)
					continue;

				var start = region * RoleStats.Roles;
				for (var role = 0; role < RoleStats.Roles; role++)
				{
					var here = belief[start + role];
					if (here <= 0f)
						continue;

					// Static defences do not move, so belief about them must not either. A pillbox
					// believed at a choke is still at that choke a minute later.
					if ((CombatRole)role == CombatRole.Defense)
						continue;

					var moving = here * rate;
					next[start + role] -= moving;

					var share = moving / adjacent.Count;
					foreach (var neighbour in adjacent)
						next[(neighbour * RoleStats.Roles) + role] += share;
				}
			}

			Array.Copy(next, belief, belief.Length);
		}

		/// <summary>
		/// Writes the belief into a state's enemy force vector, so the search plans against what is
		/// believed rather than only against what is currently visible.
		/// </summary>
		public void ApplyTo(PlayerState enemy)
		{
			ArgumentNullException.ThrowIfNull(enemy);

			enemy.BaseIntegrity = 0f;

			for (var region = 0; region < regions && region < enemy.RegionCount; region++)
			{
				for (var role = 0; role < RoleStats.Roles; role++)
					enemy.SetForce(region, (CombatRole)role, Expected(region, (CombatRole)role));

				enemy.Structures[region] = structures[region];
				enemy.BaseIntegrity += structures[region];
			}

			enemy.PeakBaseIntegrity = Math.Max(enemy.PeakBaseIntegrity, enemy.BaseIntegrity);
		}

		/// <summary>
		/// <para>
		/// Ensures the belief accounts for an enemy that exists whether or not it has been seen, by
		/// distributing an assumed strength across the regions still unexplored.
		/// </para>
		/// <para>
		/// Without this the belief state has a serious pathology: at the start of a match nothing has
		/// been observed, so the believed enemy army is zero, and every "am I ahead" comparison reads
		/// total dominance. <b>The commander concludes it is winning because it cannot see anyone.</b>
		/// Measured, a fresh commander rated its position at 0.93 win probability while blind, which
		/// made every plan look equally good and left it unable to choose between them.
		/// </para>
		/// <para>
		/// The prior is not knowledge and does not pretend to be: it says only that an opponent
		/// exists, is probably comparable in strength, and must be somewhere that has not been
		/// looked at. As reconnaissance covers the map, the unexplored set shrinks and the same
		/// assumed strength concentrates into fewer places - so scouting sharpens the estimate rather
		/// than merely revealing units, which is exactly the value scouting has.
		/// </para>
		/// </summary>
		public void AssumeUnseen(float assumedTotal, int now, int staleAfterTicks)
		{
			if (assumedTotal <= 0f || regions == 0)
				return;

			var believed = ExpectedTotal();
			var shortfall = assumedTotal - believed;
			if (shortfall <= 0f)
				return;

			// Somewhere not looked at recently. If the whole map is under observation there is
			// nowhere left for an unseen army to be, and the belief is simply what was seen.
			var candidates = new List<int>();
			for (var region = 0; region < regions; region++)
				if (TicksSinceSeen(region, now) > staleAfterTicks)
					candidates.Add(region);

			if (candidates.Count == 0)
				return;

			// Spread as armour, which is the load-bearing assumption for whether an attack is safe.
			// Assuming the unseen enemy is infantry would make every assault look cheap.
			var share = shortfall / candidates.Count;
			foreach (var region in candidates)
			{
				var index = (region * RoleStats.Roles) + (int)CombatRole.Armor;
				belief[index] += share;
			}
		}

		/// <summary>
		/// The region most likely to hold enemy force that has not been seen recently - where a scout
		/// is worth sending, because that is where the belief is both large and stale.
		/// </summary>
		public int MostUncertainRegion(int now)
		{
			var best = -1;
			var bestScore = 0f;

			for (var region = 0; region < regions; region++)
			{
				var staleness = Math.Min(TicksSinceSeen(region, now), 25 * 300) / (float)(25 * 300);
				var mass = ExpectedIn(region);

				// Both matter: somewhere unseen for ten minutes with nothing believed in it is not
				// worth a scout, and neither is somewhere crawling with enemy that was seen a second
				// ago.
				var score = staleness * (1f + mass);
				if (score > bestScore)
				{
					bestScore = score;
					best = region;
				}
			}

			return best;
		}
	}
}
