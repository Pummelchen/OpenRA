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

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>What the whole staff is working toward for the next few minutes.</summary>
	public enum Stance
	{
		/// <summary>Grow. Nothing is threatening and nothing is ready.</summary>
		Build,

		/// <summary>Find out. We do not know enough to commit to anything.</summary>
		Probe,

		/// <summary>Squeeze. Raid the economy and deny ground without committing the field army.</summary>
		Pressure,

		/// <summary>Commit. The army is ready, the objective is known, and the moment is now.</summary>
		Assault,

		/// <summary>Hold. Something of ours is worth more than anything of theirs right now.</summary>
		Defend,

		/// <summary>Rebuild. We lost the last exchange and must not lose the next one too.</summary>
		Recover,
	}

	/// <summary>
	/// <para>
	/// The tactical chief's orders to the rest of the staff, valid for a stated period.
	/// </para>
	/// <para>
	/// A directive is what makes a staff a command structure rather than a committee. Each
	/// specialist optimises its own domain, and left alone they will each do so at the worst
	/// possible moment - economy expanding while the base is under attack, production building
	/// harvesters while an assault waits for tanks. The chief sees all their reports at once and
	/// decides which domain gets its way.
	/// </para>
	/// <para>
	/// It carries a validity window for the same reason a plan carries one: re-deciding every cycle
	/// is how the previous commander drew thirty-eight matches. Every attack makes the army ratio
	/// worse before it makes it better, so a chief that reconsiders continuously will always cancel
	/// at the worst moment.
	/// </para>
	/// </summary>
	public sealed class Directive
	{
		public Stance Stance { get; init; } = Stance.Build;

		/// <summary>Where the main effort goes, when there is one.</summary>
		public int? MainEffortRegion { get; init; }

		/// <summary>Where to make a show of force, to move the defence off the main effort.</summary>
		public int? FeintRegion { get; init; }

		/// <summary>Whether infiltration and stealth operations are authorised this period.</summary>
		public bool AuthoriseSpecialOperations { get; init; }

		/// <summary>Fraction of the army held back rather than committed.</summary>
		public float ReserveFraction { get; init; } = 0.25f;

		/// <summary>Tick this directive was issued, and the tick it stops being binding.</summary>
		public int IssuedTick { get; init; }
		public int ValidUntilTick { get; init; }

		/// <summary>Why, in one sentence, for the log and for the next person reading it.</summary>
		public string Rationale { get; init; } = "";

		public bool IsValid(int tick) => tick < ValidUntilTick;

		public static readonly Directive Initial = new()
		{
			Stance = Stance.Build,
			Rationale = "match start: nothing known, nothing ready",
			ValidUntilTick = 0,
		};

		public override string ToString() =>
			$"{Stance}" +
			(MainEffortRegion.HasValue ? $" main effort R{MainEffortRegion}" : "") +
			(FeintRegion.HasValue ? $", feint R{FeintRegion}" : "") +
			(AuthoriseSpecialOperations ? ", special ops authorised" : "") +
			$" - {Rationale}";
	}
}
