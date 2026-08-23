#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Composes the real coalition subsystems - mission manager, order arbiter, force packages,
	/// wave composition, operation schedule - into a driveable scenario without needing a live
	/// <c>World</c>.
	/// </para>
	/// <para>
	/// The scenario cases these support (reqs 663-684) previously had only contract coverage of the
	/// pieces plus a headless match in which the scenario might or might not have occurred. This lets
	/// a named scenario be set up deliberately, run through the shipped components, and asserted on
	/// its outcome - which is what "tested" was supposed to mean for those rows.
	/// </para>
	/// </summary>
	sealed class ScenarioHarness
	{
		public readonly MissionManager Missions = new();
		public readonly CoalitionOrderArbiter Arbiter = new();
		public readonly ReserveManager Reserve = new();
		public readonly List<ForceGroup> Forces = [];

		/// <summary>Scenario time; missions and schedules are keyed off this.</summary>
		public int Tick { get; private set; }

		public int Advance(int ticks = 100)
		{
			Tick += Math.Max(1, ticks);
			return Tick;
		}

		/// <summary>Registers a force belonging to one allied player.</summary>
		public ForceGroup AddForce(string owner, int units, float strength = 0f,
			params (UnitClass Class, int Count)[] composition)
		{
			var group = new ForceGroup(owner)
			{
				TotalUnits = units,
				Strength = strength <= 0f ? units * 10f : strength,
				Readiness = 1f,
				Center = new CPos(20, 20)
			};

			foreach (var (unitClass, count) in composition)
				group.Counts[(int)unitClass] = count;

			Forces.Add(group);
			return group;
		}

		/// <summary>Creates a mission and commits the named forces to it through the arbiter.</summary>
		public CoalitionMission Launch(MissionType type, int priority, CPos? target, string objective,
			ArbiterPriority arbiterPriority = ArbiterPriority.ActiveCombat, params string[] owners)
		{
			var mission = Missions.CreateMission(type, priority, target, objective, createdTick: Tick);
			foreach (var owner in owners)
			{
				Arbiter.Assign(mission.Id, type.ToString(), arbiterPriority, owner);
				var force = Forces.FirstOrDefault(f => f.Owner == owner);
				if (force != null)
				{
					force.MissionId = mission.Id;
					force.Role = type.ToString();
				}
			}

			mission.Status = MissionStatus.Executing;
			return mission;
		}

		/// <summary>Concludes a mission and releases the forces committed to it.</summary>
		public void Conclude(CoalitionMission mission, MissionStatus outcome, string reason = null)
		{
			mission.Status = outcome;
			mission.OutcomeReason = reason;
			Missions.RecordOutcome(mission);
			Arbiter.ReleaseMission(mission.Id);
			foreach (var force in Forces.Where(f => f.MissionId == mission.Id))
			{
				force.MissionId = null;
				force.Role = null;
			}
		}

		/// <summary>Forces still uncommitted to any mission — the strategic reserve.</summary>
		public IReadOnlyList<ForceGroup> Uncommitted =>
			Forces.Where(f => string.IsNullOrEmpty(f.MissionId)).ToArray();

		public int UncommittedUnits => Uncommitted.Sum(f => f.TotalUnits);

		/// <summary>The joint packages currently committed, spanning allied players.</summary>
		public IReadOnlyList<CoalitionForcePackage> Packages => CoalitionForcePackage.Build(Forces);

		/// <summary>The threat picture the coalition is presenting to a defender.</summary>
		public IReadOnlyList<PresentedThreat> PresentedThreats(Func<CoalitionMission, int> regionOf,
			Func<CoalitionMission, string> domainOf)
		{
			return Missions.Missions
				.Where(m => m.Status is MissionStatus.Ready or MissionStatus.Executing)
				.Select(m => new PresentedThreat(m.Type, regionOf(m), domainOf(m), m.Priority))
				.ToArray();
		}

		/// <summary>Active missions of a given type.</summary>
		public IReadOnlyList<CoalitionMission> Active(MissionType type)
		{
			return Missions.Missions
				.Where(m => m.Type == type && m.Status is MissionStatus.Ready or MissionStatus.Executing)
				.ToArray();
		}
	}
}
