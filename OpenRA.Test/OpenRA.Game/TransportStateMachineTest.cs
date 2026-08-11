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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class TransportStateMachineTest
	{
		[TestCase(TestName = "Without extraction the machine runs to Hold and completes.")]
		public void NoExtractionCompletesAtHold()
		{
			var machine = new TransportStateMachine(extractOnCompletion: false);

			Assert.That(machine.State, Is.EqualTo(TransportState.Assemble));
			while (!machine.Complete)
				machine.Advance();

			Assert.That(machine.State, Is.EqualTo(TransportState.Hold));
			Assert.That(machine.Complete, Is.True);
			Assert.That(machine.Aborted, Is.False);
		}

		[TestCase(TestName = "With extraction the machine visits the extraction cycle before completing.")]
		public void ExtractionCycles()
		{
			var machine = new TransportStateMachine(extractOnCompletion: true);

			var visited = new System.Collections.Generic.HashSet<TransportState>();
			while (!machine.Complete)
				visited.Add(machine.Advance());

			Assert.That(visited, Does.Contain(TransportState.ExtractionRequest));
			Assert.That(visited, Does.Contain(TransportState.ReturnForExtraction));
			Assert.That(visited, Does.Contain(TransportState.Reload));
			Assert.That(visited, Does.Contain(TransportState.Extract));
			Assert.That(machine.State, Is.EqualTo(TransportState.Hold), "Hold is the terminal state.");
			Assert.That(machine.Complete, Is.True);
		}

		[TestCase(TestName = "The state order matches the spec.")]
		public void StateOrder()
		{
			var machine = new TransportStateMachine(extractOnCompletion: true);
			var order = new System.Collections.Generic.List<TransportState>();
			while (!machine.Complete)
				order.Add(machine.Advance());

			Assert.That(order[0], Is.EqualTo(TransportState.Load));
			Assert.That(order[1], Is.EqualTo(TransportState.WaitForWindow));
			Assert.That(order[2], Is.EqualTo(TransportState.Transit));
			Assert.That(order[3], Is.EqualTo(TransportState.Approach));
			Assert.That(order[4], Is.EqualTo(TransportState.Unload));
			Assert.That(order[5], Is.EqualTo(TransportState.ExtractionRequest));
			Assert.That(order[6], Is.EqualTo(TransportState.ReturnForExtraction));
			Assert.That(order[7], Is.EqualTo(TransportState.Reload));
			Assert.That(order[8], Is.EqualTo(TransportState.Extract));
		}

		[TestCase(TestName = "Abort holds position and reports the abort.")]
		public void AbortHolds()
		{
			var machine = new TransportStateMachine(extractOnCompletion: false);
			machine.Advance(); // Assemble -> Load
			machine.Advance(); // Load -> WaitForWindow

			machine.Abort();

			Assert.That(machine.Aborted, Is.True);
			Assert.That(machine.State, Is.EqualTo(TransportState.Hold));
			Assert.That(machine.Complete, Is.True);
		}

		[TestCase(TestName = "Reset restarts the cycle from Assemble.")]
		public void ResetRestarts()
		{
			var machine = new TransportStateMachine(extractOnCompletion: true);
			while (!machine.Complete)
				machine.Advance();

			machine.Reset();

			Assert.That(machine.State, Is.EqualTo(TransportState.Assemble));
			Assert.That(machine.Complete, Is.False);
			Assert.That(machine.Aborted, Is.False);
		}

		[TestCase(TestName = "All eleven states are defined.")]
		public void AllStatesDefined()
		{
			var states = System.Enum.GetValues<TransportState>();
			Assert.That(states, Is.EqualTo(new[]
			{
				TransportState.Assemble,
				TransportState.Load,
				TransportState.WaitForWindow,
				TransportState.Transit,
				TransportState.Approach,
				TransportState.Unload,
				TransportState.ExtractionRequest,
				TransportState.ReturnForExtraction,
				TransportState.Reload,
				TransportState.Extract,
				TransportState.Hold
			}));
		}
	}
}
