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
	/// <summary>
	/// <para>
	/// What a manager has worked out, in the order it has to be worked out: what happened, what is
	/// true now, what it wants to be true shortly, and what it is doing about it.
	/// </para>
	/// <para>
	/// The four are separated because collapsing them is how this staff has repeatedly gone wrong.
	/// A manager that reports only its current state gives the chief a snapshot with no direction -
	/// "forty-three structures" says nothing about whether that is thirty more than ten minutes ago
	/// or twenty fewer. A manager that reports only its intention gives the chief a plan with no
	/// evidence behind it. Both failures are in this commander's measured history: an assault was
	/// launched on a force ratio that was pinned at exactly 1.00 all match, and a siege was
	/// prosecuted twenty-six times against a construction yard that was being rebuilt behind it,
	/// because nothing distinguished progress from repetition.
	/// </para>
	/// <para>
	/// <b>Past</b> is what the record shows, and only what it shows. <b>Present</b> is the current
	/// reading. <b>Target</b> is what this manager wants to be true in the next few minutes, stated
	/// so the chief can weigh it against everybody else's. <b>Action</b> is what has actually been
	/// ordered - not what would be nice, what was issued.
	/// </para>
	/// </summary>
	public sealed class Assessment
	{
		/// <summary>What the record shows has happened. Empty when there is not yet enough of it.</summary>
		public string Past { get; init; } = "";

		/// <summary>The current reading.</summary>
		public string Present { get; init; } = "";

		/// <summary>What this manager wants true shortly, and why it is worth the chief's credits or army.</summary>
		public string Target { get; init; } = "";

		/// <summary>What was actually ordered this cycle.</summary>
		public string Action { get; init; } = "";

		/// <summary>How far along the target is, 0 to 1, where it can be measured at all.</summary>
		public float? Progress { get; init; }

		public bool IsEmpty =>
			string.IsNullOrEmpty(Past) && string.IsNullOrEmpty(Present)
			&& string.IsNullOrEmpty(Target) && string.IsNullOrEmpty(Action);

		public override string ToString()
		{
			var parts = new System.Collections.Generic.List<string>();
			if (!string.IsNullOrEmpty(Past))
				parts.Add($"was: {Past}");

			if (!string.IsNullOrEmpty(Present))
				parts.Add($"is: {Present}");

			if (!string.IsNullOrEmpty(Target))
				parts.Add($"wants: {Target}");

			if (!string.IsNullOrEmpty(Action))
				parts.Add($"doing: {Action}");

			if (Progress.HasValue)
				parts.Add($"{Progress.Value:P0} there");

			return string.Join("; ", parts);
		}
	}
}
