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

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Everything the commander needs to decide <i>what to do</i>, and nothing it needs to decide
	/// <i>how to do it</i>. A few hundred numbers: small enough to copy thousands of times per
	/// second, complete enough that a plan made from it means something.
	/// </para>
	/// <para>
	/// The "how" - which unit takes which route, who focuses fire on what - belongs to the execution
	/// layer, which already does it well. Putting any of it in here would make the search slower
	/// without making the plan better.
	/// </para>
	/// </summary>
	public sealed class AbstractState
	{
		/// <summary>Engine ticks per second. Everything here is in seconds; the engine is not.</summary>
		public const int TicksPerSecond = 25;

		public int Tick { get; set; }

		public PlayerState Self { get; }
		public PlayerState Enemy { get; }

		/// <summary>Number of regions in the map decomposition this state is expressed over.</summary>
		public int RegionCount { get; }

		/// <summary>
		/// Who holds each region, -1 (theirs) to +1 (mine). Distinct from force presence: a region
		/// can be controlled and empty, which is exactly the situation an undefended expansion is in.
		/// </summary>
		public float[] Control { get; }

		/// <summary>Ticks since each region was last seen. Feeds the value of scouting it again.</summary>
		public int[] VisibilityAge { get; }

		/// <summary>What each region is worth to hold: ore remaining, expansion sites, structures.</summary>
		public float[] Value { get; }

		public AbstractState(int regionCount)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(regionCount);

			RegionCount = regionCount;
			Self = new PlayerState(regionCount);
			Enemy = new PlayerState(regionCount);
			Control = new float[regionCount];
			VisibilityAge = new int[regionCount];
			Value = new float[regionCount];
		}

		/// <summary>
		/// A deep copy. The search makes one of these per node, so it allocates exactly six arrays
		/// and copies them - no dictionaries, no object graphs, nothing that would put a garbage
		/// collection between the commander and its decision.
		/// </summary>
		public AbstractState Clone()
		{
			var copy = new AbstractState(RegionCount) { Tick = Tick };
			Self.CopyTo(copy.Self);
			Enemy.CopyTo(copy.Enemy);
			Array.Copy(Control, copy.Control, RegionCount);
			Array.Copy(VisibilityAge, copy.VisibilityAge, RegionCount);
			Array.Copy(Value, copy.Value, RegionCount);
			return copy;
		}

		/// <summary>Seconds of game time this state represents.</summary>
		public float Seconds => Tick / (float)TicksPerSecond;
	}

	/// <summary>One side's economy, technology and armies.</summary>
	public sealed class PlayerState
	{
		public int RegionCount { get; }

		public float Cash { get; set; }

		/// <summary>Working harvesters. Income is derived from these, never set directly.</summary>
		public int Harvesters { get; set; }

		/// <summary>Refineries, which cap how much of the harvesters' income can actually be banked.</summary>
		public int Refineries { get; set; }

		/// <summary>
		/// Credits per second this player converts cash into army at. The *observed* rate, not queue
		/// capacity - a bot sitting on twenty thousand credits has enormous capacity and is not
		/// using it, and a model fed capacity predicts an army that never arrives.
		/// </summary>
		public float ProductionThroughput { get; set; }

		/// <summary>
		/// Credits per second this player is measured to be earning, and the harvester count that
		/// was earning it. The model starts from this pair rather than deriving income, so at the
		/// moment of extraction its forecast equals the observation exactly.
		/// </summary>
		public float ObservedIncomePerSecond { get; set; }

		/// <summary>Harvesters in service when <see cref="ObservedIncomePerSecond"/> was measured.</summary>
		public int ObservedHarvesters { get; set; }

		/// <summary>
		/// <para>
		/// Net credits per second this player's army has recently been gaining or losing - what is
		/// built, minus everything lost to fighting the model cannot see. Measured, not derived.
		/// </para>
		/// <para>
		/// Splitting these apart was tried and does not work under fog. Production is observable
		/// only as total spending, which includes refineries and harvesters that never join the
		/// army; losses are not observable at all when they happen out of vision. The net figure is
		/// the one quantity that <i>is</i> measurable, so it is the one the forecast is anchored to.
		/// </para>
		/// </summary>
		public float ArmyGrowthPerSecond { get; set; }

		/// <summary>Which nodes of the tech tree are unlocked, one bit each.</summary>
		public ulong TechBits { get; set; }

		/// <summary>
		/// Total credit value of structures that would end the game if lost. Zero is a loss, which
		/// is what gives the search a terminal condition to aim at rather than a metric to maximise.
		/// </summary>
		public float BaseIntegrity { get; set; }

		/// <summary>
		/// The most base this player has ever had. Current integrity means nothing on its own - four
		/// thousand credits of structures is a strong start and a catastrophic twenty minutes - so
		/// the signal that matters is the ratio to the peak.
		/// </summary>
		public float PeakBaseIntegrity { get; set; }

		/// <summary>
		/// Forces by region and role, in credits, indexed <c>[region * Roles + role]</c>. A flat
		/// array rather than a jagged one: one allocation, one copy, and it stays in cache while the
		/// search walks it.
		/// </summary>
		public float[] Forces { get; }

		public PlayerState(int regionCount)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(regionCount);
			RegionCount = regionCount;
			Forces = new float[regionCount * RoleStats.Roles];
		}

		/// <summary>The force in one region, as a span the combat model can read without copying.</summary>
		public Span<float> ForcesIn(int region) =>
			Forces.AsSpan(region * RoleStats.Roles, RoleStats.Roles);

		public float ForceValue(int region, CombatRole role) => Forces[(region * RoleStats.Roles) + (int)role];

		public void SetForce(int region, CombatRole role, float credits) =>
			Forces[(region * RoleStats.Roles) + (int)role] = Math.Max(0f, credits);

		public void AddForce(int region, CombatRole role, float credits) =>
			SetForce(region, role, ForceValue(region, role) + credits);

		/// <summary>Total credit value of everything in one region.</summary>
		public float ArmyValueIn(int region)
		{
			var total = 0f;
			var start = region * RoleStats.Roles;
			for (var r = 0; r < RoleStats.Roles; r++)
				total += Forces[start + r];

			return total;
		}

		/// <summary>Total credit value of the whole army.</summary>
		public float ArmyValue()
		{
			var total = 0f;
			foreach (var f in Forces)
				total += f;

			return total;
		}

		public void CopyTo(PlayerState other)
		{
			ArgumentNullException.ThrowIfNull(other);
			other.Cash = Cash;
			other.Harvesters = Harvesters;
			other.Refineries = Refineries;
			other.ProductionThroughput = ProductionThroughput;
			other.ObservedIncomePerSecond = ObservedIncomePerSecond;
			other.ObservedHarvesters = ObservedHarvesters;
			other.ArmyGrowthPerSecond = ArmyGrowthPerSecond;
			other.TechBits = TechBits;
			other.BaseIntegrity = BaseIntegrity;
			other.PeakBaseIntegrity = PeakBaseIntegrity;
			Array.Copy(Forces, other.Forces, Math.Min(Forces.Length, other.Forces.Length));
		}
	}
}
