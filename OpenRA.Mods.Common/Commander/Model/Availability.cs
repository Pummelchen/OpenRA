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
	/// <summary>Something the commander could start building right now, and what it would take.</summary>
	public sealed class BuildOption
	{
		public ActorCapability Capability { get; init; }
		public string Queue { get; init; } = "";

		/// <summary>Seconds until it would be in the field, including anything ahead of it.</summary>
		public float TimeToField { get; init; }

		/// <summary>Whether the credits are on hand right now.</summary>
		public bool Affordable { get; init; }

		/// <summary>Whether building it would take the base into a power deficit.</summary>
		public bool CausesBrownout { get; init; }

		public string Type => Capability?.Type ?? "";

		public override string ToString() =>
			$"{Type} on {Queue} in {TimeToField:F0}s"
			+ (Affordable ? "" : " (cannot afford)")
			+ (CausesBrownout ? " (brownout)" : "");
	}

	/// <summary>A support power the commander holds, ready or charging.</summary>
	public sealed record SupportPowerState(string Key, string Name, bool Ready, float SecondsRemaining);

	/// <summary>
	/// <para>
	/// What the commander can actually do this second, as opposed to what the rules permit in
	/// principle.
	/// </para>
	/// <para>
	/// The registry answers "what is a Tesla coil for". This answers "can I build one, right now,
	/// and would it leave me in a brownout" - and those are different questions with different
	/// answers, only one of which a decision can be made from. Without it a manager either assumes
	/// it can build everything, or asks the engine one item at a time and rebuilds the same picture
	/// on every cycle.
	/// </para>
	/// <para>
	/// <b>Time to field is the field that earns this class its place.</b> "Cheap now" and "strong in
	/// ninety seconds" are not comparable until the ninety seconds is on the table, and a commander
	/// that cannot compare them will always take the cheap thing.
	/// </para>
	/// </summary>
	public sealed class Availability
	{
		/// <summary>Everything buildable right now, soonest first.</summary>
		public IReadOnlyList<BuildOption> Options { get; init; } = [];

		public IReadOnlyList<SupportPowerState> SupportPowers { get; init; } = [];

		public int Cash { get; init; }
		public int PowerProvided { get; init; }
		public int PowerDrained { get; init; }
		public int ExcessPower => PowerProvided - PowerDrained;

		/// <summary>True while a power outage is actually in progress.</summary>
		public bool InOutage { get; init; }

		/// <summary>How many of ours hold each capability, counted once per cycle.</summary>
		public IReadOnlyDictionary<string, int> OwnedByVerb { get; init; } =
			new Dictionary<string, int>();

		public int Owned(string verb) => OwnedByVerb.GetValueOrDefault(verb);

		/// <summary>
		/// Options for one queue, soonest first.
		/// </summary>
		/// <remarks>
		/// The ordering is applied here rather than assumed of the caller. Documenting "soonest
		/// first" while relying on whoever built the list to have sorted it means an instance
		/// constructed any other way returns the wrong order and says nothing about it - which is
		/// precisely the kind of quiet contract violation this commander has been bitten by before.
		/// </remarks>
		public IEnumerable<BuildOption> On(string queue) =>
			Options.Where(o => string.Equals(o.Queue, queue, StringComparison.Ordinal))
				.OrderBy(o => o.TimeToField)
				.ThenBy(o => o.Type, StringComparer.Ordinal);

		/// <summary>Everything buildable, soonest first, whichever queue it comes from.</summary>
		public IEnumerable<BuildOption> Soonest() =>
			Options.OrderBy(o => o.TimeToField).ThenBy(o => o.Type, StringComparer.Ordinal);

		/// <summary>Whether a given type could be started now.</summary>
		public BuildOption Find(string type) =>
			Options.FirstOrDefault(o => string.Equals(o.Type, type, StringComparison.Ordinal));

		/// <summary>Support powers ready to fire.</summary>
		public IEnumerable<SupportPowerState> ReadyPowers() => SupportPowers.Where(p => p.Ready);

		public string Summary()
		{
			var ready = SupportPowers.Count(p => p.Ready);
			var affordable = Options.Count(o => o.Affordable);
			return $"available: {Options.Count} buildable ({affordable} affordable), " +
				$"power {ExcessPower:+#;-#;0} ({PowerProvided}/{PowerDrained})" +
				(InOutage ? " OUTAGE" : "") +
				$", {ready}/{SupportPowers.Count} powers ready, cash {Cash}";
		}

		/// <summary>The verbs counted in <see cref="OwnedByVerb"/>.</summary>
		public static readonly string[] Verbs =
			["armed", "antiair", "transport", "capturer", "detector", "hider",
			 "harvester", "production", "powerplant"];

		/// <summary>Which verbs a capability satisfies, for counting what we own.</summary>
		public static IEnumerable<string> VerbsOf(ActorCapability c)
		{
			ArgumentNullException.ThrowIfNull(c);

			if (c.IsArmed) yield return "armed";
			if (c.CanHitAir) yield return "antiair";
			if (c.Transports) yield return "transport";
			if (c.Captures) yield return "capturer";
			if (c.Detects) yield return "detector";
			if (c.CanHide) yield return "hider";
			if (c.Harvests) yield return "harvester";
			if (c.IsProduction) yield return "production";
			if (c.SuppliesPower) yield return "powerplant";
		}
	}
}
