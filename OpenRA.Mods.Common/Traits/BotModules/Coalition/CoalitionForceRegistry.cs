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

using System.Collections.Frozen;
using System.Collections.Generic;

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
		AntiStructure
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
	public static class CoalitionForceRegistry
	{
		/// <summary>Friendly capabilities a unit contributes, deduplicated and in declaration order.</summary>
		public static IReadOnlyList<FriendlyCapability> FriendlyCapabilitiesFor(UnitClass unitClass, string type,
			FrozenSet<string> artilleryTypes, FrozenSet<string> submarineTypes, FrozenSet<string> detectionTypes,
			FrozenSet<string> transportTypes, FrozenSet<string> scoutTypes, FrozenSet<string> antiAirTypes)
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
					break;
				case UnitClass.Armor:
					Emit(FriendlyCapability.AntiArmor);
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
				Emit(FriendlyCapability.Recon);
			if (antiAirTypes.Contains(type))
				Emit(FriendlyCapability.AntiAir);

			return emitted;
		}

		/// <summary>Records a capability into a group's profile (0..1 presence), idempotent.</summary>
		public static void Record(FriendlyCapability capability, float[] profile)
		{
			profile[(int)capability] = 1f;
		}
	}
}
