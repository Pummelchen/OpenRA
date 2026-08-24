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
	/// <summary>How well a manager's domain is doing, in terms its chief can act on.</summary>
	public enum Readiness
	{
		/// <summary>Failing. Something in this domain will lose the match if left alone.</summary>
		Critical,

		/// <summary>Coping, but not able to support anything ambitious.</summary>
		Strained,

		/// <summary>Doing its job.</summary>
		Healthy,

		/// <summary>Has more capacity than it is being asked for - spare that could be committed.</summary>
		Surplus,
	}

	/// <summary>
	/// <para>
	/// A manager's report to the tactical chief: <b>conclusions, not measurements</b>.
	/// </para>
	/// <para>
	/// The distinction is the point of having a staff. A chief handed every actor position, queue
	/// entry and credit balance is a chief doing everyone's job badly - which is precisely what the
	/// two-thousand-line command module was. A chief handed "armour is ready in ninety seconds",
	/// "the economy has more income than it can spend" and "I do not know where their army is" can
	/// decide something.
	/// </para>
	/// <para>
	/// Every field here answers a question the chief actually asks when deciding whether to attack.
	/// A manager with nothing useful to say leaves them null rather than filling them in.
	/// </para>
	/// </summary>
	public sealed class ManagerReport
	{
		/// <summary>Which specialist is speaking.</summary>
		public string Manager { get; init; } = "";

		/// <summary>State of this domain.</summary>
		public Readiness Readiness { get; init; } = Readiness.Healthy;

		/// <summary>One sentence a person could read in a log and understand.</summary>
		public string Headline { get; init; } = "";

		/// <summary>
		/// How much this manager trusts its own report, 0 to 1. Intelligence reporting "they are
		/// teching" at 20% confidence is a different input from the same words at 90%, and a chief
		/// that cannot tell them apart will commit to a counter that was never justified.
		/// </summary>
		public float Confidence { get; init; } = 1f;

		/// <summary>
		/// Seconds until this domain can support a major commitment, where that is meaningful.
		/// Production answers "when is the army ready"; economy answers "when can I afford it".
		/// This is what turns "attack eventually" into a timing.
		/// </summary>
		public int? ReadyInSeconds { get; init; }

		/// <summary>Fighting strength this manager controls, in credits, where it controls any.</summary>
		public float? ForceValue { get; init; }

		/// <summary>Net credits per second this domain adds or consumes.</summary>
		public float? NetCreditsPerSecond { get; init; }

		/// <summary>Somewhere this manager believes the chief should be looking.</summary>
		public int? RegionOfInterest { get; init; }

		public override string ToString() =>
			$"[{Manager}] {Readiness}: {Headline}" +
			(ReadyInSeconds.HasValue ? $" (ready in {ReadyInSeconds}s)" : "");
	}
}
