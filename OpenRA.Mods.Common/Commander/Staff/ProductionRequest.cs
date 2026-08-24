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
	/// <summary>How badly a specialist wants something built.</summary>
	public enum RequestPriority
	{
		/// <summary>Would be useful.</summary>
		Wanted,

		/// <summary>This domain cannot do its job without it.</summary>
		Needed,

		/// <summary>Something is failing now and this is the remedy.</summary>
		Urgent,
	}

	/// <summary>
	/// <para>
	/// One specialist asking the production managers for something.
	/// </para>
	/// <para>
	/// A request is not an order. Scouting needs dogs and does not get to decide that dogs matter
	/// more than tanks this minute; that judgement belongs to whoever owns production, weighing
	/// every request against the chief's directive. Before this existed six managers queued items
	/// directly and could contradict one another with nobody arbitrating.
	/// </para>
	/// </summary>
	public sealed class ProductionRequest
	{
		/// <summary>Who is asking.</summary>
		public string Requester { get; init; } = "";

		/// <summary>Actor name wanted.</summary>
		public string Item { get; init; } = "";

		public int Count { get; init; } = 1;

		public RequestPriority Priority { get; init; } = RequestPriority.Wanted;

		/// <summary>Why, in a few words, so a decision against it can be explained.</summary>
		public string Reason { get; init; } = "";

		public override string ToString() => $"{Requester} wants {Count}x{Item} ({Priority}: {Reason})";
	}
}
