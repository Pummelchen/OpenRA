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

		/// <summary>Ticks since a region was last observed.</summary>
		public int TicksSinceSeen(int region, int now) =>
			region < 0 || region >= regions ? int.MaxValue / 2 : now - lastSeen[region];

		/// <summary>
		/// Replaces the belief for one observed region with what is actually there. Applies to
		/// regions the player can currently see, and it is the only place positive evidence enters.
		/// </summary>
		public void Observe(int region, ReadOnlySpan<float> observedByRole, int tick)
		{
			if (region < 0 || region >= regions)
				return;

			var start = region * RoleStats.Roles;
			for (var r = 0; r < RoleStats.Roles; r++)
				belief[start + r] = r < observedByRole.Length ? Math.Max(0f, observedByRole[r]) : 0f;

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

			for (var region = 0; region < regions && region < enemy.RegionCount; region++)
				for (var role = 0; role < RoleStats.Roles; role++)
					enemy.SetForce(region, (CombatRole)role, Expected(region, (CombatRole)role));
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
