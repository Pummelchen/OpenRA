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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>Functional capabilities a friendly force group can expose, for assignment and production planning.</summary>
	public enum FriendlyCapability
	{
		AntiAir,
		AntiArmor,
		AntiInfantry,
		Artillery,
		Recon,
		Transport,
		Naval,
		Air,
		Detection,
		AntiStructure,
		Mobility,
		FastRaiding,
		AirSuperiority,
		SpecialOperations,
		BaseDefense
	}

	/// <summary>Coarse movement/activity state of a force group.</summary>
	public enum ForceStatus
	{
		Idle,
		Moving
	}

	/// <summary>A scarce asset tracked individually: a special unit (Tanya, spy, engineer) or a transport with its cargo.</summary>
	public sealed class SpecialAsset
	{
		public readonly string Owner;
		public readonly string Type;
		public readonly CPos Cell;

		/// <summary>Passenger count, meaningful for transports.</summary>
		public readonly int Cargo;

		public SpecialAsset(string owner, string type, CPos cell, int cargo = 0)
		{
			Owner = owner;
			Type = type;
			Cell = cell;
			Cargo = cargo;
		}
	}

	/// <summary>One friendly production facility with its live queue state.</summary>
	public sealed class ProductionFacility
	{
		public readonly string Owner;
		public readonly string QueueType;
		public readonly string Structure;
		public readonly CPos Cell;

		/// <summary>The item being produced, or null when the queue is idle.</summary>
		public string Current;

		/// <summary>Remaining queued items after the current one.</summary>
		public string[] Queued = [];

		/// <summary>What this queue can build right now (prerequisites satisfied).</summary>
		public string[] Buildable = [];

		/// <summary>0..100 progress of the current item.</summary>
		public int ProgressPercent;

		public ProductionFacility(string owner, string queueType, string structure, CPos cell)
		{
			Owner = owner;
			QueueType = queueType;
			Structure = structure;
			Cell = cell;
		}
	}

	/// <summary>
	/// Pure, engine-free helpers for the coalition force registry. Maps a friendly unit onto the
	/// functional capabilities it contributes (anti-air, artillery, transport, recon, ...), mirroring
	/// <see cref="CoalitionBlackboard.CapabilitiesFor"/> on the enemy side but for what our own force
	/// can do.
	/// </summary>
	/// <summary>
	/// A force package that spans allied players (req 26). OpenRA never permits one player to order
	/// another player's actors, so the atomic <see cref="ForceGroup"/> stays per-owner and each owner
	/// issues its own orders. A package is the level the commander actually reasons about and assigns:
	/// it aggregates several per-owner groups committed to one mission, so "the force attacking the
	/// north expansion" is one object with one combined strength, readiness and capability profile
	/// even when three different allied players contribute to it.
	/// </summary>
	public sealed class CoalitionForcePackage
	{
		/// <summary>The mission every member group is committed to.</summary>
		public readonly string MissionId;

		/// <summary>The per-owner groups that make up this package, in deterministic owner order.</summary>
		public readonly IReadOnlyList<ForceGroup> Members;

		/// <summary>Distinct allied players contributing to the package.</summary>
		public IEnumerable<string> Owners => Members.Select(m => m.Owner);

		/// <summary>True when more than one allied player contributes, i.e. a genuinely joint force.</summary>
		public bool IsJoint => Members.Select(m => m.Owner).Distinct().Count() > 1;

		public int TotalUnits => Members.Sum(m => m.TotalUnits);
		public float Strength => Members.Sum(m => m.Strength);

		/// <summary>Unit-weighted readiness: a large unready contingent must not be hidden by a small ready one.</summary>
		public float Readiness => WeightedAverage(m => m.Readiness);

		/// <summary>Unit-weighted cohesion across the whole package.</summary>
		public float Cohesion => WeightedAverage(m => m.Cohesion);

		/// <summary>Unit-weighted casualty fraction across the whole package.</summary>
		public float CasualtyFraction => WeightedAverage(m => m.CasualtyFraction);

		/// <summary>Combined capability profile: the package's anti-air is the coalition's, not one ally's.</summary>
		public readonly float[] Capabilities = new float[Enum.GetValues<FriendlyCapability>().Length];

		/// <summary>Combined per-class unit counts.</summary>
		public readonly int[] Counts = new int[Enum.GetValues<UnitClass>().Length];

		/// <summary>The package centre of mass, weighted by unit count.</summary>
		public CPos Center { get; }

		public CoalitionForcePackage(string missionId, IReadOnlyList<ForceGroup> members)
		{
			MissionId = missionId;
			Members = members ?? [];

			foreach (var member in Members)
			{
				for (var i = 0; i < Capabilities.Length; i++)
					Capabilities[i] += member.Capabilities[i];
				for (var i = 0; i < Counts.Length; i++)
					Counts[i] += member.Counts[i];
			}

			var units = TotalUnits;
			if (units > 0)
			{
				var x = Members.Sum(m => (long)m.Center.X * m.TotalUnits) / units;
				var y = Members.Sum(m => (long)m.Center.Y * m.TotalUnits) / units;
				Center = new CPos((int)x, (int)y);
			}
			else if (Members.Count > 0)
				Center = Members[0].Center;
		}

		float WeightedAverage(Func<ForceGroup, float> selector)
		{
			var units = TotalUnits;
			if (units == 0)
				return Members.Count == 0 ? 0f : Members.Average(selector);

			return Members.Sum(m => selector(m) * m.TotalUnits) / units;
		}

		/// <summary>True when the package fields the given capability at all.</summary>
		public bool Has(FriendlyCapability capability)
		{
			return Capabilities[(int)capability] > 0f;
		}

		/// <summary>
		/// Groups committed force groups into one package per mission. Groups with no mission are not
		/// packaged: an uncommitted force is not part of any operation. Ordering is deterministic
		/// (mission id, then owner) so every allied bot builds the identical packaging.
		/// </summary>
		public static IReadOnlyList<CoalitionForcePackage> Build(IEnumerable<ForceGroup> forces)
		{
			return (forces ?? [])
				.Where(f => !string.IsNullOrEmpty(f.MissionId))
				.GroupBy(f => f.MissionId, StringComparer.Ordinal)
				.OrderBy(g => g.Key, StringComparer.Ordinal)
				.Select(g => new CoalitionForcePackage(g.Key,
					g.OrderBy(f => f.Owner, StringComparer.Ordinal).ToArray()))
				.ToArray();
		}
	}

	public static class CoalitionForceRegistry
	{
		/// <summary>Friendly capabilities a unit contributes, deduplicated and in declaration order.</summary>
		public static IReadOnlyList<FriendlyCapability> FriendlyCapabilitiesFor(UnitClass unitClass, string type,
			FrozenSet<string> artilleryTypes, FrozenSet<string> submarineTypes, FrozenSet<string> detectionTypes,
			FrozenSet<string> transportTypes, FrozenSet<string> scoutTypes, FrozenSet<string> antiAirTypes,
			FrozenSet<string> specialTypes = null)
		{
			var emitted = new List<FriendlyCapability>();
			void Emit(FriendlyCapability capability)
			{
				if (!emitted.Contains(capability))
					emitted.Add(capability);
			}

			switch (unitClass)
			{
				case UnitClass.Air:
					Emit(FriendlyCapability.Air);
					Emit(FriendlyCapability.AntiAir);
					Emit(FriendlyCapability.AirSuperiority);
					Emit(FriendlyCapability.Mobility);
					break;
				case UnitClass.Armor:
					Emit(FriendlyCapability.AntiArmor);
					Emit(FriendlyCapability.Mobility);
					break;
				case UnitClass.Infantry:
					Emit(FriendlyCapability.AntiInfantry);
					break;
				case UnitClass.Naval:
					Emit(FriendlyCapability.Naval);
					break;
			}

			if (artilleryTypes.Contains(type))
			{
				Emit(FriendlyCapability.Artillery);
				Emit(FriendlyCapability.AntiStructure);
			}

			if (submarineTypes.Contains(type))
				Emit(FriendlyCapability.Naval);
			if (detectionTypes.Contains(type))
				Emit(FriendlyCapability.Detection);
			if (transportTypes.Contains(type))
				Emit(FriendlyCapability.Transport);
			if (scoutTypes.Contains(type))
			{
				Emit(FriendlyCapability.Recon);
				Emit(FriendlyCapability.FastRaiding);
			}

			if (antiAirTypes.Contains(type))
			{
				Emit(FriendlyCapability.AntiAir);
				Emit(FriendlyCapability.BaseDefense);
			}

			if (specialTypes?.Contains(type) == true)
				Emit(FriendlyCapability.SpecialOperations);

			return emitted;
		}

		/// <summary>Records a capability into a group's profile (0..1 presence), idempotent.</summary>
		public static void Record(FriendlyCapability capability, float[] profile)
		{
			profile[(int)capability] = 1f;
		}

		/// <summary>
		/// Assigns non-overlapping coalition production specializations. The strongest army is main,
		/// a deterministic naval owner is selected only when usable water exists, and the richest
		/// remaining ally specializes in expansion. All remaining allies escort.
		/// </summary>
		public static IReadOnlyDictionary<string, string> AssignRoles(IReadOnlyList<ForceGroup> forces,
			IReadOnlyDictionary<string, int> memberCash, bool hasBigWater)
		{
			var roles = forces.ToDictionary(f => f.Owner, _ => "escort");
			if (forces.Count == 0)
				return roles;

			var main = forces.OrderByDescending(f => f.TotalUnits).ThenBy(f => f.Owner).First();
			roles[main.Owner] = "main";

			ForceGroup naval = null;
			if (hasBigWater && forces.Count > 1)
			{
				naval = forces.Where(f => f.Owner != main.Owner)
					.OrderByDescending(f => f.Counts[(int)UnitClass.Naval])
					.ThenBy(f => f.Owner).First();
				roles[naval.Owner] = "naval";
			}

			var expansion = forces.Where(f => f.Owner != main.Owner && f.Owner != naval?.Owner)
				.OrderByDescending(f => memberCash.GetValueOrDefault(f.Owner))
				.ThenBy(f => f.Owner).FirstOrDefault();
			if (expansion != null)
				roles[expansion.Owner] = "expansion";

			return roles;
		}
	}
}
