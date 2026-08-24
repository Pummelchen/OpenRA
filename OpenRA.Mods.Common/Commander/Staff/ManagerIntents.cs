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
	public sealed class AssessmentIntent : IManagerIntent
	{
		public string Topic { get; init; } = "";
		public string Finding { get; init; } = "";

		public string Describe() => $"{Topic}: {Finding}";
	}
}
