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

using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>Build a unit of this type, at this urgency.</summary>
	public sealed class ProduceUnitIntent : IManagerIntent
	{
		public string Unit { get; init; } = "";
		public int Count { get; init; } = 1;
		public string Reason { get; init; } = "";

		public string Describe() => $"produce {Count}x{Unit} ({Reason})";
	}

	/// <summary>Put up a structure of this type.</summary>
	public sealed class ConstructIntent : IManagerIntent
	{
		public string Structure { get; init; } = "";
		public string Reason { get; init; } = "";

		public string Describe() => $"construct {Structure} ({Reason})";
	}

	/// <summary>Send reconnaissance to a place.</summary>
	public sealed class ScoutIntent : IManagerIntent
	{
		public int Region { get; init; }
		public string Reason { get; init; } = "";

		public string Describe() => $"scout R{Region} ({Reason})";
	}

	/// <summary>Commit the field army to an objective.</summary>
	public sealed class AttackIntent : IManagerIntent
	{
		public int Region { get; init; }
		public MacroVerb Verb { get; init; }
		public float Confidence { get; init; }
		public string Reason { get; init; } = "";

		public string Describe() => $"{Verb} R{Region} at {Confidence:P0} ({Reason})";
	}

	/// <summary>Hold or reinforce a place.</summary>
	public sealed class DefendIntent : IManagerIntent
	{
		public int Region { get; init; }
		public float Urgency { get; init; }
		public string Reason { get; init; } = "";

		public string Describe() => $"defend R{Region} urgency {Urgency:F2} ({Reason})";
	}

	/// <summary>An assessment, carrying no action. Recorded so a decision can be explained later.</summary>
	/// <summary>
	/// Repair one of our own buildings.
	/// </summary>
	/// <remarks>
	/// Structures are the most expensive things the commander owns and the only ones it cannot
	/// replace under fire, so leaving them damaged is the cheapest loss it takes. The engine's own
	/// repair module only ever runs from a response-to-attack notification, so a building damaged in
	/// a raid that ends is never repaired at all - it simply stays broken for the rest of the match,
	/// supplying less power or fewer units the whole time, while the commander sits on six figures
	/// of unspent credits.
	/// </remarks>
	public sealed class RepairIntent : IManagerIntent
	{
		public uint ActorId { get; init; }
		public string Structure { get; init; } = "";
		public float HealthFraction { get; init; }
		public string Reason { get; init; } = "";

		public string Describe() => $"repair {Structure}#{ActorId} at {HealthFraction:P0}: {Reason}";
	}

	/// <summary>
	/// Move one of our own units out of the way.
	/// </summary>
	/// <remarks>
	/// Units with nothing to do drift to a stop wherever they happen to be, and where they happen to
	/// be is usually the base that produced them. Enough of them standing in the gaps between
	/// buildings turns a base into a maze: freshly built units crawl out through their own army
	/// instead of driving to the front, and reinforcements arrive late in ones and twos.
	/// </remarks>
	public sealed class RelocateIntent : IManagerIntent
	{
		public uint ActorId { get; init; }
		public CPos Destination { get; init; }
		public string Reason { get; init; } = "";

		public string Describe() => $"relocate #{ActorId} to {Destination}: {Reason}";
	}

	/// <summary>
	/// Put one of our units into attack mode.
	/// </summary>
	/// <remarks>
	/// A unit that holds fire until fired upon concedes the first shot in every engagement it is
	/// part of, which over a match is a large amount of free damage handed to the opponent. The one
	/// deliberate exception is a unit on its way to somewhere it is not supposed to be noticed.
	/// </remarks>
	public sealed class SetAttackModeIntent : IManagerIntent
	{
		public uint ActorId { get; init; }
		public string CurrentStance { get; init; } = "";

		public string Describe() => $"attack mode for #{ActorId} (was {CurrentStance})";
	}

	/// <summary>
	/// Put one of our units onto a harvester as a permanent escort.
	/// </summary>
	/// <remarks>
	/// Harvesters are the only units on the field whose loss compounds: every one killed slows
	/// everything the commander builds afterwards, and they spend the whole match alone, in the
	/// open, at the edge of the base, on a predictable route. They are the cheapest thing an
	/// opponent can profitably raid.
	/// </remarks>
	public sealed class EscortIntent : IManagerIntent
	{
		public uint EscortId { get; init; }
		public uint HarvesterId { get; init; }
		public string Reason { get; init; } = "";

		public string Describe() => $"escort #{EscortId} guards harvester #{HarvesterId}: {Reason}";
	}

	/// <summary>
	/// Put operatives into, or take them out of, covert transit.
	/// </summary>
	/// <remarks>
	/// A unit crossing hostile ground to reach somewhere unnoticed must not shoot at what it passes.
	/// Firing gives away both its position and the fact that something is coming, and an infiltrator
	/// that has been noticed is simply an expensive infantryman a long way from home. While in
	/// transit it holds fire; when the operation ends it goes back to engaging like everything else.
	/// </remarks>
	public sealed class CovertTransitIntent : IManagerIntent
	{
		public string OperativeType { get; init; } = "";
		public bool InTransit { get; init; }
		public string Reason { get; init; } = "";

		public string Describe() =>
			$"{OperativeType} {(InTransit ? "into" : "out of")} covert transit: {Reason}";
	}

	public sealed class AssessmentIntent : IManagerIntent
	{
		public string Topic { get; init; } = "";
		public string Finding { get; init; } = "";

		public string Describe() => $"{Topic}: {Finding}";
	}
}
