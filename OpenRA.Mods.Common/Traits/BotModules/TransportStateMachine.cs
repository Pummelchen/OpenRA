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

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// The explicit states of a transport mission, per the spec: assemble, load, wait for a timing
	/// window, transit, approach, unload, then either extract or hold. Every state has a completion
	/// condition evaluated by the transport controller; the state machine itself is a pure
	/// transition function so it can be unit-tested without a World.
	/// </summary>
	public enum TransportState
	{
		/// <summary>Payload units assemble near the transport.</summary>
		Assemble,

		/// <summary>Payload boards the transport.</summary>
		Load,

		/// <summary>Wait for the planned launch window (deception/synchronization).</summary>
		WaitForWindow,

		/// <summary>Move toward the insertion region.</summary>
		Transit,

		/// <summary>Final approach to the insertion point.</summary>
		Approach,

		/// <summary>Payload disembarks.</summary>
		Unload,

		/// <summary>Request extraction of the payload after the mission.</summary>
		ExtractionRequest,

		/// <summary>Transport returns to pick the payload up.</summary>
		ReturnForExtraction,

		/// <summary>Payload reboards for the return trip.</summary>
		Reload,

		/// <summary>Transport returns home with the payload.</summary>
		Extract,

		/// <summary>Hold position (fallback when transit becomes unsafe).</summary>
		Hold
	}

	/// <summary>
	/// Pure transport-mission state machine. Advancing states is a deterministic function of the
	/// current state and the mission's intent; the controller supplies the completion conditions.
	/// </summary>
	public sealed class TransportStateMachine
	{
		public TransportState State { get; private set; } = TransportState.Assemble;

		/// <summary>Set when the mission reached a terminal state this cycle.</summary>
		public bool Complete { get; private set; }

		/// <summary>Set when transit was aborted because it became unsafe.</summary>
		public bool Aborted { get; private set; }

		/// <summary>True when the mission expects extraction of the payload.</summary>
		public readonly bool ExtractOnCompletion;

		public TransportStateMachine(bool extractOnCompletion)
		{
			ExtractOnCompletion = extractOnCompletion;
		}

		/// <summary>
		/// Advances one state when the caller reports the current state's condition met.
		/// Returns the new state. Terminal transitions set <see cref="Complete"/>.
		/// </summary>
		public TransportState Advance()
		{
			switch (State)
			{
				case TransportState.Assemble:
					State = TransportState.Load;
					break;

				case TransportState.Load:
					State = TransportState.WaitForWindow;
					break;

				case TransportState.WaitForWindow:
					State = TransportState.Transit;
					break;

				case TransportState.Transit:
					State = TransportState.Approach;
					break;

				case TransportState.Approach:
					State = TransportState.Unload;
					break;

				case TransportState.Unload:
					if (ExtractOnCompletion)
						State = TransportState.ExtractionRequest;
					else
					{
						State = TransportState.Hold;
						Complete = true;
					}

					break;

				case TransportState.ExtractionRequest:
					State = TransportState.ReturnForExtraction;
					break;

				case TransportState.ReturnForExtraction:
					State = TransportState.Reload;
					break;

				case TransportState.Reload:
					State = TransportState.Extract;
					break;

				case TransportState.Extract:
					State = TransportState.Hold;
					Complete = true;
					break;

				case TransportState.Hold:
					Complete = true;
					break;
			}

			return State;
		}

		/// <summary>Aborts the mission and holds position; the payload survives at the cost of the objective.</summary>
		public void Abort()
		{
			Aborted = true;
			State = TransportState.Hold;
			Complete = true;
		}

		/// <summary>Resets the machine for a new mission cycle.</summary>
		public void Reset()
		{
			State = TransportState.Assemble;
			Complete = false;
			Aborted = false;
		}
	}
}
