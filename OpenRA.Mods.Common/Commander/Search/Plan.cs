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
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Search
{
	/// <summary>Why a committed plan stopped.</summary>
	public enum PlanStatus
	{
		/// <summary>Still running, and not to be reconsidered.</summary>
		Active,

		/// <summary>Ran to its committed end.</summary>
		Expired,

		/// <summary>The force sent to do it no longer exists in useful strength.</summary>
		ForceSpent,

		/// <summary>Home is in danger and matters more.</summary>
		HomeThreatened,

		/// <summary>What it was sent to take is already gone.</summary>
		ObjectiveGone,

		/// <summary>Something materially better appeared, by a margin, not a hair.</summary>
		Superseded,
	}

	/// <summary>
	/// <para>
	/// A decision the commander has made and will stick to.
	/// </para>
	/// <para>
	/// This type is the fix for the defect that produced thirty-eight draws. The previous commander
	/// re-derived its posture every review from the instantaneous army ratio, and an attack always
	/// makes that ratio worse before it makes it better - you lose units to the defences before you
	/// kill the production that wins the game. So it retreated at exactly the moment every
	/// successful assault in the history of the genre starts working, and no threshold change could
	/// have repaired that, because the flaw was in re-deciding at all.
	/// </para>
	/// <para>
	/// A plan therefore declares, <i>at launch</i>, the conditions under which it may be abandoned -
	/// while the commander is thinking clearly, rather than mid-assault when the numbers are
	/// transiently ugly. Between reviews it is checked against those conditions and nothing else.
	/// <b>A falling army ratio is not one of them.</b> It is the expected cost of the plan, it was
	/// priced in when the plan was chosen, and treating it as a reason to stop is precisely the bug.
	/// </para>
	/// </summary>
	public sealed class Plan
	{
		/// <summary>What this plan is trying to do, and where.</summary>
		public MacroAction Objective { get; init; }

		/// <summary>Tick the plan was committed at.</summary>
		public int StartTick { get; init; }

		/// <summary>Tick before which the plan is not reconsidered at all.</summary>
		public int CommittedUntilTick { get; init; }

		/// <summary>Credit value of the force this plan was launched with.</summary>
		public float LaunchStrength { get; init; }

		/// <summary>Base integrity when the plan was launched, for judging whether home has fallen apart.</summary>
		public float LaunchHomeIntegrity { get; init; }

		/// <summary>Win probability the search believed this plan would reach.</summary>
		public float ExpectedValue { get; init; }

		/// <summary>
		/// Fraction of the launch force below which the plan is abandoned. Deliberately low: an
		/// assault that has lost half its strength and is standing in the enemy base is usually
		/// closer to winning than one that turned around.
		/// </summary>
		public float MinimumForceFraction { get; init; } = 0.4f;

		/// <summary>Fraction of launch base integrity below which home takes priority.</summary>
		public float MinimumHomeFraction { get; init; } = 0.6f;

		/// <summary>
		/// How much better an alternative must look before it may interrupt. A margin rather than a
		/// tie-break, because two plans of nearly equal value will otherwise alternate forever and
		/// neither will be carried out.
		/// </summary>
		public float SupersedeMargin { get; init; } = 0.15f;

		public PlanStatus Status { get; private set; } = PlanStatus.Active;

		public bool IsActive => Status == PlanStatus.Active;

		/// <summary>
		/// Judges the plan against the conditions it declared at launch.
		/// <paramref name="bestAlternative"/> is the value of the best other plan available, or a
		/// negative number if none has been evaluated.
		/// </summary>
		public PlanStatus Review(int tick, float currentStrength, float currentHomeIntegrity,
			bool objectiveStillExists, float bestAlternative = -1f)
		{
			if (Status != PlanStatus.Active)
				return Status;

			// The force is gone. Not "the exchange looks bad" - gone.
			if (LaunchStrength > 0f && currentStrength < LaunchStrength * MinimumForceFraction)
				return Status = PlanStatus.ForceSpent;

			// Home is falling apart, which is worth more than any objective.
			if (LaunchHomeIntegrity > 0f && currentHomeIntegrity < LaunchHomeIntegrity * MinimumHomeFraction)
				return Status = PlanStatus.HomeThreatened;

			// What it was sent to take is already gone; carrying on would be marching on an empty
			// field.
			if (!objectiveStillExists)
				return Status = PlanStatus.ObjectiveGone;

			// Only now may the plan expire, and only now may something better replace it. Both are
			// checked last so that a plan in trouble is judged on its own terms first.
			if (tick >= CommittedUntilTick)
			{
				if (bestAlternative > ExpectedValue + SupersedeMargin)
					return Status = PlanStatus.Superseded;

				return Status = PlanStatus.Expired;
			}

			return PlanStatus.Active;
		}

		/// <summary>
		/// Whether an alternative is worth interrupting a still-committed plan for. Almost always
		/// no: this is the escape hatch for a genuinely different situation - the enemy base left
		/// undefended, our own base about to fall - and not for a marginally better score.
		/// </summary>
		public bool WouldSupersede(float alternativeValue) =>
			alternativeValue > ExpectedValue + SupersedeMargin;

		/// <summary>Seconds of commitment remaining.</summary>
		public float RemainingSeconds(int tick) =>
			Math.Max(0, CommittedUntilTick - tick) / (float)AbstractState.TicksPerSecond;

		public override string ToString() =>
			$"{Objective} committed to {CommittedUntilTick} (value {ExpectedValue:F3}, {Status})";
	}
}
