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
using System.Linq;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>One queue's instruction for this cycle.</summary>
	public sealed record CompositionChoice(string Queue, string Unit, string Reason);

	/// <summary>How much of the army each arm should be, and how much of it currently is.</summary>
	public sealed record ArmShare(string Queue, string Best, float Score, float Target, float Current)
	{
		public bool Underweight => Current < Target;

		public override string ToString() =>
			$"{Queue}: {Best ?? "-"} score {Score:F2}, {Current:P0} of {Target:P0}";
	}

	/// <summary>
	/// <para>
	/// Decides what the army should be made of, from what the opponent is actually made of.
	/// </para>
	/// <para>
	/// This replaces a hack that was load-bearing and known to be. Production used to ask every idle
	/// queue for the single highest-ranked unit in one global list, so a queue that could not build
	/// that unit produced nothing at all. Letting each queue build its own favourite instead was
	/// measured and cost two thirds of the commander's exchange ratio - 0.88 to 0.31 - because the
	/// barracks are cheap and fast, so a free barracks makes infantry without pause, and an
	/// infantry-heavy army loses to a tank army. The accident was acting as a composition filter.
	/// </para>
	/// <para>
	/// The filter is now explicit, which is what makes the navy buildable: a shipyard was never
	/// offered anything under the old scheme, because the best unit in the game is never a ship.
	/// </para>
	/// <para>
	/// <b>Nothing here is a fixed percentage.</b> The share each arm gets is derived from how well
	/// its best available unit performs against the armour the enemy has actually been seen fielding,
	/// per credit. Against heavy armour the infantry share collapses on its own, because the mod's
	/// own damage tables say rifles do little to a tank - and against an opponent who fields
	/// infantry it recovers, for the same reason. A hand-written percentage table cannot do that,
	/// which is why every bot that ships with one plays the same match every time.
	/// </para>
	/// </summary>
	public static class CompositionPlan
	{
		/// <summary>
		/// How sharply the better arm is preferred. Shares go as score^Sharpness, so at 2 an arm
		/// twice as efficient gets four times the army value rather than twice.
		/// </summary>
		/// <remarks>
		/// Above 1 on purpose. Proportional allocation keeps building a little of everything, and
		/// "a little of everything" is the composition that loses: it is the average of several
		/// answers rather than the right one. The exponent is what turns a ranking into a decision.
		/// </remarks>
		public const float Sharpness = 2f;

		/// <summary>What to assume the enemy fields before anything has been seen of them.</summary>
		/// <remarks>
		/// Not a uniform guess. An opponent's opening is overwhelmingly infantry and light vehicles
		/// because that is what an opening can afford, so assuming heavy armour on tick one builds
		/// the wrong counter to a threat that does not exist yet. This is a prior, and it is
		/// replaced by observation the moment there is any.
		/// </remarks>
		public static readonly IReadOnlyDictionary<string, float> OpeningPrior =
			new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
			{
				["None"] = 0.4f,
				["Light"] = 0.35f,
				["Heavy"] = 0.25f,
			};

		/// <summary>
		/// What the enemy is made of, by armour class, weighted by the cost of what was seen.
		/// </summary>
		/// <remarks>
		/// Weighted by value rather than by headcount, because a dozen riflemen and a dozen heavy
		/// tanks are not a dozen of anything comparable. Counting bodies would have the commander
		/// build anti-infantry against an armoured column that happened to bring a screen.
		/// </remarks>
		public static IReadOnlyDictionary<string, float> EnemyArmourMix(
			CapabilityRegistry registry, IEnumerable<string> enemyTypes)
		{
			ArgumentNullException.ThrowIfNull(registry);
			ArgumentNullException.ThrowIfNull(enemyTypes);

			var mix = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			var total = 0f;

			foreach (var type in enemyTypes)
			{
				var capability = registry.Find(type);

				// Structures are excluded. They are a target, not a threat to be out-composed, and
				// including them would tip every mix towards Concrete and have the commander build
				// siege units to fight an army.
				if (capability == null || capability.IsStructure || string.IsNullOrEmpty(capability.Armour))
					continue;

				var weight = Math.Max(1, capability.Cost);
				mix[capability.Armour] = mix.GetValueOrDefault(capability.Armour) + weight;
				total += weight;
			}

			if (total <= 0f)
				return OpeningPrior;

			foreach (var key in mix.Keys.ToArray())
				mix[key] /= total;

			return mix;
		}

		/// <summary>
		/// How good a unit is against the enemy we believe we are facing, per credit spent.
		/// </summary>
		public static float ScoreAgainst(
			ActorCapability capability, IReadOnlyDictionary<string, float> armourMix, bool enemyHasAir)
		{
			ArgumentNullException.ThrowIfNull(capability);
			ArgumentNullException.ThrowIfNull(armourMix);

			if (capability.IsStructure || !capability.IsArmed || capability.Cost <= 0)
				return 0f;

			var score = armourMix.Sum(a => a.Value * capability.DamagePerCreditVersus(a.Key));

			// Reach is worth paying for: a unit that outranges what it fights takes less damage
			// doing the same damage, and damage-per-credit alone cannot express that. Modest and
			// bounded - this is a tiebreaker between comparable units, not a reason to buy artillery
			// exclusively.
			score *= 1f + Math.Min(0.5f, capability.Reach / 20f);

			// Against an opponent who flies, a unit that cannot shoot upwards is worth less than its
			// ground numbers say, because some fraction of the fights it is bought for it cannot
			// join at all.
			if (enemyHasAir && !capability.CanHitAir)
				score *= 0.7f;

			return score;
		}

		/// <summary>
		/// The share of army value each arm should hold, and what it holds now.
		/// </summary>
		/// <param name="registry">The capability registry.</param>
		/// <param name="available">What can be started right now, per queue.</param>
		/// <param name="ownedTypes">Our standing units, by type, one entry per unit.</param>
		/// <param name="enemyTypes">Enemy units seen standing, by type, one entry per unit.</param>
		public static IReadOnlyList<ArmShare> Shares(
			CapabilityRegistry registry,
			Availability available,
			IEnumerable<string> ownedTypes,
			IEnumerable<string> enemyTypes)
		{
			ArgumentNullException.ThrowIfNull(registry);
			ArgumentNullException.ThrowIfNull(available);
			ArgumentNullException.ThrowIfNull(ownedTypes);
			ArgumentNullException.ThrowIfNull(enemyTypes);

			var enemy = enemyTypes.ToArray();
			var mix = EnemyArmourMix(registry, enemy);
			var enemyHasAir = enemy.Any(t => registry.Find(t)?.IsAircraft == true);

			// What each arm could field at its best, and what each arm is worth to us today.
			var owned = ownedTypes.ToArray();
			var valueByQueue = new Dictionary<string, float>(StringComparer.Ordinal);
			var armyValue = 0f;

			foreach (var type in owned)
			{
				var capability = registry.Find(type);
				if (capability == null || capability.IsStructure || !capability.IsArmed)
					continue;

				// A unit is counted against the arm that makes it. Units buildable from more than
				// one queue are counted once, against the first, so the shares still sum to one.
				var queue = capability.Queues.FirstOrDefault();
				if (queue == null)
					continue;

				valueByQueue[queue] = valueByQueue.GetValueOrDefault(queue) + capability.Cost;
				armyValue += capability.Cost;
			}

			var scored = new List<(string Queue, string Best, float Score)>();
			foreach (var queue in available.Options.Select(o => o.Queue).Distinct(StringComparer.Ordinal))
			{
				var best = available.On(queue)
					.Select(o => (o.Type, Score: ScoreAgainst(o.Capability, mix, enemyHasAir)))
					.Where(x => x.Score > 0f)
					.OrderByDescending(x => x.Score)
					.ThenBy(x => x.Type, StringComparer.Ordinal)
					.FirstOrDefault();

				if (best.Type != null)
					scored.Add((queue, best.Type, best.Score));
			}

			var weightTotal = scored.Sum(s => (float)Math.Pow(s.Score, Sharpness));

			return scored
				.Select(s => new ArmShare(
					s.Queue,
					s.Best,
					s.Score,
					weightTotal <= 0f ? 0f : (float)Math.Pow(s.Score, Sharpness) / weightTotal,
					armyValue <= 0f ? 0f : valueByQueue.GetValueOrDefault(s.Queue) / armyValue))
				.OrderByDescending(s => s.Target)
				.ThenBy(s => s.Queue, StringComparer.Ordinal)
				.ToList();
		}

		/// <summary>
		/// The one unit the shares will not buy on their own: something that shoots upwards.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Efficiency scoring cannot produce anti-air, and this is not a flaw in the scoring - it is
		/// what efficiency means. A ground unit fights in most engagements and an anti-air unit
		/// fights in the few that involve aircraft, so per credit the ground unit wins every
		/// comparison right up to the moment the aircraft arrive, at which point the army has no
		/// answer at all and the comparison stops mattering.
		/// </para>
		/// <para>
		/// The live availability survey found the commander holding <b>one</b> anti-air unit against
		/// sixty-five armed ones, fifteen thousand ticks into a match. That is not a bad trade, it is
		/// an opening an opponent with aircraft walks straight through, and no amount of better
		/// ranking closes it. It needs a floor.
		/// </para>
		/// </remarks>
		/// <param name="minimumShare">
		/// Fraction of army value that should be able to shoot upwards once the enemy flies.
		/// </param>
		public static CompositionChoice AirDefence(
			CapabilityRegistry registry,
			Availability available,
			IEnumerable<string> ownedTypes,
			IEnumerable<string> enemyTypes,
			IEnumerable<string> idleQueues,
			float minimumShare = 0.15f)
		{
			ArgumentNullException.ThrowIfNull(registry);
			ArgumentNullException.ThrowIfNull(available);
			ArgumentNullException.ThrowIfNull(ownedTypes);
			ArgumentNullException.ThrowIfNull(enemyTypes);
			ArgumentNullException.ThrowIfNull(idleQueues);

			// No aircraft seen, no floor. Anti-air bought against an opponent who never flies is
			// exactly the waste the efficiency scoring is right to avoid.
			if (!enemyTypes.Any(t => registry.Find(t)?.IsAircraft == true))
				return null;

			var armed = 0f;
			var covered = 0f;
			foreach (var type in ownedTypes)
			{
				var capability = registry.Find(type);
				if (capability == null || capability.IsStructure || !capability.IsArmed)
					continue;

				armed += capability.Cost;
				if (capability.CanHitAir)
					covered += capability.Cost;
			}

			if (armed > 0f && covered / armed >= minimumShare)
				return null;

			var idle = new HashSet<string>(idleQueues, StringComparer.Ordinal);

			// The cheapest thing that can shoot upwards, not the best. The floor is about having an
			// answer at all; buying the finest anti-air unit in the game to reach fifteen percent
			// spends the army's whole budget on the fights it is least likely to have.
			var pick = available.Options
				.Where(o => idle.Contains(o.Queue)
					&& o.Capability != null
					&& !o.Capability.IsStructure
					&& o.Capability.CanHitAir)
				.OrderBy(o => o.Capability.Cost)
				.ThenBy(o => o.Type, StringComparer.Ordinal)
				.FirstOrDefault();

			if (pick == null)
				return null;

			var share = armed > 0f ? covered / armed : 0f;
			return new CompositionChoice(pick.Queue, pick.Type,
				$"enemy flies and only {share:P0} of the army can shoot upwards "
				+ $"(floor {minimumShare:P0})");
		}

		/// <summary>
		/// What each idle queue should build, or nothing if that arm is already at its share.
		/// </summary>
		/// <param name="shares">The result of <see cref="Shares"/>.</param>
		/// <param name="idleQueues">Queues with nothing in production.</param>
		/// <param name="armySize">How many armed units we hold; shares are meaningless below a handful.</param>
		public static IReadOnlyList<CompositionChoice> Decide(
			IReadOnlyList<ArmShare> shares, IEnumerable<string> idleQueues, int armySize)
		{
			ArgumentNullException.ThrowIfNull(shares);
			ArgumentNullException.ThrowIfNull(idleQueues);

			var idle = new HashSet<string>(idleQueues, StringComparer.Ordinal);
			var choices = new List<CompositionChoice>();

			// With almost no army there is no composition to balance, and refusing to build while
			// the shares settle is how a commander loses in the first three minutes. Below this,
			// every idle queue builds its best and the shares take over once there is an army for
			// them to describe.
			var opening = armySize < 6;

			foreach (var share in shares)
			{
				if (!idle.Contains(share.Queue) || share.Best == null)
					continue;

				if (!opening && !share.Underweight)
					continue;

				choices.Add(new CompositionChoice(share.Queue, share.Best,
					opening
						? $"opening: {share.Queue} idle, best available is {share.Best}"
						: $"{share.Queue} at {share.Current:P0} of a {share.Target:P0} share "
							+ $"(score {share.Score:F2})"));
			}

			return choices;
		}
	}
}
