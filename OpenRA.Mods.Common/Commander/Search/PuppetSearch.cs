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
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Search
{
	/// <summary>
	/// <para>
	/// Monte Carlo tree search over macro-actions: what should the commander do for the next two
	/// minutes.
	/// </para>
	/// <para>
	/// Three choices make this fit inside a fifteen-second review. It searches <b>puppet actions</b>
	/// rather than unit orders, so branching is a dozen instead of astronomical. It evaluates leaves
	/// with the fitted win-probability model rather than by playing a game out, so a node costs one
	/// sigmoid instead of a rollout. And it steps a forward model that costs under two microseconds,
	/// so thousands of nodes fit in a few milliseconds.
	/// </para>
	/// <para>
	/// It is deterministic. UCT ties break by index, the opponent's replies are cycled rather than
	/// sampled, and no random source is consulted anywhere - so the same position always produces
	/// the same plan. That is not fastidiousness: a commander whose decisions cannot be reproduced
	/// cannot be debugged, benchmarked, or replayed, and every measurement in this project depends
	/// on it.
	/// </para>
	/// </summary>
	public sealed class PuppetSearch
	{
		public sealed class Settings
		{
			/// <summary>Seconds of game time each step of the plan covers.</summary>
			public float StepSeconds { get; init; } = 15f;

			/// <summary>How many steps deep to look. Eight steps of fifteen seconds is two minutes.</summary>
			public int Depth { get; init; } = 8;

			/// <summary>Node budget. The search stops here whether or not it has converged.</summary>
			public int Iterations { get; init; } = 2000;

			/// <summary>
			/// UCT exploration weight. sqrt(2) is the textbook value for rewards in 0..1, which win
			/// probability already is - so it needs no rescaling and is not a tuning knob.
			/// </summary>
			public float Exploration { get; init; } = 1.414f;

			public static readonly Settings Default = new();
		}

		sealed class Node
		{
			public MacroAction Action;
			public AbstractState State;
			public Node Parent;
			public List<Node> Children;
			public List<MacroAction> Untried;
			public int Visits;
			public double TotalValue;
			public int Depth;

			public double MeanValue => Visits == 0 ? 0.0 : TotalValue / Visits;
		}

		readonly ForwardModel model;
		readonly WinProbabilityModel evaluator;
		readonly Settings settings;

		public PuppetSearch(ForwardModel model, WinProbabilityModel evaluator, Settings settings = null)
		{
			ArgumentNullException.ThrowIfNull(model);
			ArgumentNullException.ThrowIfNull(evaluator);

			this.model = model;
			this.evaluator = evaluator;
			this.settings = settings ?? Settings.Default;
		}

		/// <summary>What the search decided, and what it thought of the alternatives.</summary>
		public sealed class Result
		{
			public MacroAction Best { get; init; }
			public float BestValue { get; init; }

			/// <summary>Value of the second-best action: the margin a plan must beat to be superseded.</summary>
			public float RunnerUpValue { get; init; }

			public int NodesExpanded { get; init; }

			/// <summary>Every root action with its value and visit count, best first.</summary>
			public IReadOnlyList<(MacroAction Action, float Value, int Visits)> Ranked { get; init; } = [];
		}

		/// <summary>Searches from <paramref name="root"/> and returns the action to commit to.</summary>
		public Result Search(AbstractState root)
		{
			ArgumentNullException.ThrowIfNull(root);

			var rootActions = MacroActionGenerator.Generate(root, model);
			if (rootActions.Count == 0)
				return new Result { Best = new MacroAction(MacroVerb.Defend, 0), BestValue = 0.5f, RunnerUpValue = 0f };

			var rootNode = new Node
			{
				State = root,
				Untried = new List<MacroAction>(rootActions),
				Children = [],
				Depth = 0,
			};

			var expanded = 0;

			for (var iteration = 0; iteration < settings.Iterations; iteration++)
			{
				var node = rootNode;

				// Selection: descend by UCT while the node is fully expanded and not at the horizon.
				while (node.Untried.Count == 0 && node.Children.Count > 0 && node.Depth < settings.Depth)
					node = SelectChild(node);

				// Expansion: try one action that has not been tried here yet.
				if (node.Untried.Count > 0 && node.Depth < settings.Depth)
				{
					var action = node.Untried[0];
					node.Untried.RemoveAt(0);

					var enemyAction = EnemyReply(node);
					var next = model.Step(node.State, action, enemyAction, settings.StepSeconds);

					var child = new Node
					{
						Action = action,
						State = next,
						Parent = node,
						Children = [],
						Untried = MacroActionGenerator.Generate(next, model),
						Depth = node.Depth + 1,
					};

					node.Children.Add(child);
					node = child;
					expanded++;
				}

				// Evaluation: the fitted model, not a rollout. A rollout would cost hundreds of
				// steps to produce a noisier estimate than a calibrated probability already gives.
				var value = evaluator.Evaluate(node.State, model);

				// Backpropagation.
				for (var current = node; current != null; current = current.Parent)
				{
					current.Visits++;
					current.TotalValue += value;
				}
			}

			return Rank(rootNode, expanded);
		}

		/// <summary>
		/// UCT: mean value plus an exploration bonus that decays as a child is visited. Ties break by
		/// the order actions were generated in, so the search is reproducible.
		/// </summary>
		Node SelectChild(Node node)
		{
			Node best = null;
			var bestScore = double.NegativeInfinity;
			var logVisits = Math.Log(Math.Max(1, node.Visits));

			foreach (var child in node.Children)
			{
				// An unvisited child is always taken first: its value is unknown, and unknown is
				// exactly what a search exists to resolve.
				var score = child.Visits == 0
					? double.PositiveInfinity
					: child.MeanValue + (settings.Exploration * Math.Sqrt(logVisits / child.Visits));

				if (score > bestScore)
				{
					bestScore = score;
					best = child;
				}
			}

			return best ?? node;
		}

		/// <summary>
		/// <para>
		/// What the opponent does in reply. Cycled through their plausible options by visit count
		/// rather than sampled, which covers the same distribution evenly and keeps the search
		/// deterministic.
		/// </para>
		/// <para>
		/// The opponent is modelled as competent but not clairvoyant: they defend where they are
		/// strong and attack where we are weak. Planning against a passive opponent is the classic
		/// way to build a commander that wins in simulation and loses in the game.
		/// </para>
		/// </summary>
		MacroAction EnemyReply(Node node)
		{
			var state = node.State;
			var enemyHome = MacroActionGenerator.HomeRegion(state.Enemy);
			var ourHome = MacroActionGenerator.HomeRegion(state.Self);

			// Three replies spanning the plausible range: build up, hold what they have, come at us.
			var replies = new[]
			{
				new MacroAction(MacroVerb.Produce, enemyHome),
				new MacroAction(MacroVerb.Defend, enemyHome),
				new MacroAction(MacroVerb.Attack, ourHome),
			};

			return replies[node.Visits % replies.Length];
		}

		static Result Rank(Node root, int expanded)
		{
			var ranked = new List<(MacroAction Action, float Value, int Visits)>();
			foreach (var child in root.Children)
				ranked.Add((child.Action, (float)child.MeanValue, child.Visits));

			// Ordered by visits, not by mean value. In UCT the most-visited child is the robust
			// choice: a child visited twice with a lucky evaluation can have a higher mean than one
			// visited four hundred times, and acting on that would be acting on noise.
			ranked.Sort((a, b) =>
			{
				if (a.Visits != b.Visits)
					return b.Visits.CompareTo(a.Visits);

				if (a.Value != b.Value)
					return b.Value.CompareTo(a.Value);

				return a.Action.Verb.CompareTo(b.Action.Verb);
			});

			if (ranked.Count == 0)
				return new Result { Best = new MacroAction(MacroVerb.Defend, 0), BestValue = 0.5f, RunnerUpValue = 0f };

			return new Result
			{
				Best = ranked[0].Action,
				BestValue = ranked[0].Value,
				RunnerUpValue = ranked.Count > 1 ? ranked[1].Value : 0f,
				NodesExpanded = expanded,
				Ranked = ranked,
			};
		}
	}
}
