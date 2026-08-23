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
	/// The seven things a force can be, for the purpose of predicting a fight.
	/// </para>
	/// <para>
	/// The commander reasons about unit <i>types</i> when it decides what to build - "a mammoth
	/// counters armour twice as cost-efficiently as a heavy tank" is a production decision and needs
	/// the full per-type counter matrix. It reasons about <i>roles</i> when it predicts a battle,
	/// because a search that copies a per-type force vector for forty regions cannot run thousands
	/// of rollouts inside a fifteen-second budget. Seven roles across forty regions is 280 floats;
	/// thirty types across forty regions is 1,200, and the difference decides whether the search
	/// happens at all.
	/// </para>
	/// </summary>
	public enum CombatRole
	{
		Infantry,
		Armor,
		Artillery,
		AntiAir,
		Aircraft,
		Naval,
		Defense,
	}

	/// <summary>
	/// <para>
	/// Damage and durability per credit spent, by role - the reduction that makes Lanchester's
	/// square law applicable at all.
	/// </para>
	/// <para>
	/// The square law assumes a <i>homogeneous</i> force. Applying it to raw unit counts, comparing
	/// eight tanks against eight riflemen as though the numbers were commensurable, is the classic
	/// way to get a confidently wrong answer. Reducing both sides through the counter matrix first,
	/// into damage actually dealt against the composition actually present, is what restores the
	/// assumption the law needs.
	/// </para>
	/// <para>
	/// Everything is expressed per credit rather than per unit, so a force is a vector of what it
	/// cost. That keeps the model honest about trades - killing 1,000 credits of infantry with 1,000
	/// credits of tanks is an even exchange whatever the unit counts were - and it makes force
	/// strength directly comparable to income and production capacity.
	/// </para>
	/// </summary>
	public sealed class RoleStats
	{
		public const int Roles = 7;

		readonly float[] damagePerSecondPerCredit;
		readonly float[] hitPointsPerCredit;

		/// <summary>
		/// <paramref name="damagePerSecondPerCredit"/> is indexed
		/// <c>[attacker * Roles + defender]</c>.
		/// </summary>
		public RoleStats(float[] damagePerSecondPerCredit, float[] hitPointsPerCredit)
		{
			ArgumentNullException.ThrowIfNull(damagePerSecondPerCredit);
			ArgumentNullException.ThrowIfNull(hitPointsPerCredit);

			if (damagePerSecondPerCredit.Length != Roles * Roles)
				throw new ArgumentException($"Expected {Roles * Roles} entries.", nameof(damagePerSecondPerCredit));

			if (hitPointsPerCredit.Length != Roles)
				throw new ArgumentException($"Expected {Roles} entries.", nameof(hitPointsPerCredit));

			this.damagePerSecondPerCredit = damagePerSecondPerCredit;
			this.hitPointsPerCredit = hitPointsPerCredit;
		}

		/// <summary>Damage per second that one credit of <paramref name="attacker"/> deals to <paramref name="defender"/>.</summary>
		public float DamageVersus(CombatRole attacker, CombatRole defender) =>
			damagePerSecondPerCredit[((int)attacker * Roles) + (int)defender];

		/// <summary>Hit points bought by one credit spent on this role.</summary>
		public float HitPoints(CombatRole role) => hitPointsPerCredit[(int)role];

		/// <summary>
		/// Aggregates per-type profiles into per-role averages, weighted by cost so that the role's
		/// numbers reflect what a credit spent on it actually buys.
		/// </summary>
		public static RoleStats FromProfiles(IEnumerable<(UnitProfile Profile, CombatRole Role)> profiles)
		{
			ArgumentNullException.ThrowIfNull(profiles);

			var damage = new float[Roles * Roles];
			var hitPoints = new float[Roles];
			var weight = new float[Roles];

			foreach (var (profile, role) in profiles)
			{
				if (profile == null || profile.Cost <= 0)
					continue;

				var r = (int)role;
				weight[r] += profile.Cost;
				hitPoints[r] += profile.HitPoints;

				for (var d = 0; d < Roles; d++)
					damage[(r * Roles) + d] += profile.DamagePerSecondVersus((CombatRole)d);
			}

			for (var r = 0; r < Roles; r++)
			{
				if (weight[r] <= 0f)
					continue;

				hitPoints[r] /= weight[r];
				for (var d = 0; d < Roles; d++)
					damage[(r * Roles) + d] /= weight[r];
			}

			return new RoleStats(damage, hitPoints);
		}
	}

	/// <summary>
	/// What one unit type contributes, in the terms <see cref="RoleStats"/> aggregates. Kept
	/// separate from the engine's <c>UnitCombatProfile</c> so the model layer stays a pure function
	/// of numbers and can be tested without loading a ruleset.
	/// </summary>
	public sealed class UnitProfile
	{
		public string Type { get; init; } = "";
		public int Cost { get; init; } = 1;
		public int HitPoints { get; init; } = 1;

		/// <summary>Damage per second against each role, indexed by <see cref="CombatRole"/>.</summary>
		public float[] DamageVersusRole { get; init; } = new float[RoleStats.Roles];

		public float DamagePerSecondVersus(CombatRole role)
		{
			var i = (int)role;
			return i >= 0 && i < DamageVersusRole.Length ? DamageVersusRole[i] : 0f;
		}
	}
}
