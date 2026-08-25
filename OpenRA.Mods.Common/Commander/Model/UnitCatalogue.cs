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
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>What kind of thing this is, taken from the queue the game itself builds it from.</summary>
	public enum BuildCategory
	{
		Unknown,
		Building,
		Defence,
		Infantry,
		Vehicle,
		Aircraft,
		Ship,
	}

	/// <summary>
	/// One buildable thing and everything knowable about it before the match starts.
	/// </summary>
	public sealed class CatalogueEntry
	{
		public string Type { get; init; } = "";
		public BuildCategory Category { get; init; }

		/// <summary>Sub-kind within the category where it matters: submarines fight nothing like destroyers.</summary>
		public bool IsSubmarine { get; init; }
		public bool IsStructure { get; init; }

		/// <summary>Whether it can shoot at all. An unarmed thing has no kill/death ratio worth the name.</summary>
		public bool IsArmed { get; init; }

		public int Cost { get; init; }
		public int HitPoints { get; init; }
		public string Armour { get; init; } = "";

		/// <summary>Which factions may build it, from the mod's own prerequisite tokens. Empty means anyone.</summary>
		public IReadOnlyList<string> Factions { get; init; } = [];

		/// <summary>Durability bought per credit, the one useful thing derivable without watching a match.</summary>
		public float HitPointsPerCredit => Cost <= 0 ? 0f : HitPoints / (float)Cost;

		public override string ToString() =>
			$"{Type} ({Category}{(IsSubmarine ? "/sub" : "")}) {Cost}cr {HitPoints}hp {Armour}" +
			(IsArmed ? " armed" : " unarmed");
	}

	/// <summary>
	/// <para>
	/// Every buildable thing in the mod, of every faction, with its static properties.
	/// </para>
	/// <para>
	/// The commander previously reasoned about units through hand-written lists - which unit is
	/// armour, which is anti-air, which is a scout - and this project has had to measure and reverse
	/// most of those lists at least once. A list is somebody's opinion frozen at the time they wrote
	/// it; the ruleset is what the game will actually do. Reading the catalogue from the rules means
	/// a unit the commander has never heard of is still classified correctly, and a unit whose cost
	/// or armour is rebalanced is re-read rather than re-guessed.
	/// </para>
	/// <para>
	/// This half is static: what things cost, how much they can take, what they are. The other half -
	/// what actually happens to them, how many they kill and how long they last - is watched during
	/// the match and lives in <see cref="WorldDatabase"/>. Neither is much use without the other.
	/// </para>
	/// </summary>
	public sealed class UnitCatalogue
	{
		readonly Dictionary<string, CatalogueEntry> entries = [];

		/// <summary>Every buildable thing, ordered by name so readers never depend on hash layout.</summary>
		public IReadOnlyList<CatalogueEntry> All { get; }

		public int Count => entries.Count;

		public CatalogueEntry Find(string type) =>
			string.IsNullOrEmpty(type) ? null : entries.GetValueOrDefault(type);

		public IEnumerable<CatalogueEntry> OfCategory(BuildCategory category) =>
			All.Where(e => e.Category == category);

		public UnitCatalogue(Ruleset rules)
		{
			ArgumentNullException.ThrowIfNull(rules);

			foreach (var (name, actor) in rules.Actors)
			{
				var buildable = actor.TraitInfoOrDefault<BuildableInfo>();
				if (buildable == null)
					continue;

				var building = actor.TraitInfoOrDefault<BuildingInfo>();
				var health = actor.TraitInfoOrDefault<HealthInfo>();
				var armour = actor.TraitInfoOrDefault<ArmorInfo>();

				var entry = new CatalogueEntry
				{
					Type = name,
					Category = Categorise(buildable, actor),
					IsStructure = building != null,

					// Submarines are separated because nothing else on the water behaves like them:
					// they cannot be engaged by most of what they can engage.
					IsSubmarine = actor.HasTraitInfo<CloakInfo>() && building == null
						&& buildable.Queue.Contains("Ship"),

					// Armed means it has a weapon, not that it is a combat unit. A harvester is
					// unarmed, a pillbox is armed and immobile, and both belong in the record.
					IsArmed = actor.TraitInfos<ArmamentInfo>().Any(),

					Cost = actor.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0,
					HitPoints = health?.HP ?? 0,
					Armour = armour?.Type ?? "",
					Factions = FactionsOf(buildable),
				};

				entries[name] = entry;
			}

			All = entries.Values.OrderBy(e => e.Type, StringComparer.Ordinal).ToArray();
		}

		/// <summary>
		/// Classified by the queue the game builds it from, refined by what it actually is.
		/// </summary>
		/// <remarks>
		/// The queue is the mod's own statement of what kind of thing this is, which makes it a
		/// better source than any classification written alongside the commander. Defence is split
		/// out from Building because the two are used completely differently - one is placed toward
		/// the enemy and the other away from them.
		/// </remarks>
		static BuildCategory Categorise(BuildableInfo buildable, ActorInfo actor)
		{
			var queue = buildable.Queue.FirstOrDefault() ?? "";

			return queue switch
			{
				"Building" => BuildCategory.Building,
				"Defense" => BuildCategory.Defence,
				"Infantry" => BuildCategory.Infantry,
				"Vehicle" => BuildCategory.Vehicle,
				"Aircraft" => BuildCategory.Aircraft,
				"Ship" => BuildCategory.Ship,
				_ => actor.HasTraitInfo<AircraftInfo>() ? BuildCategory.Aircraft
					: actor.HasTraitInfo<BuildingInfo>() ? BuildCategory.Building
					: BuildCategory.Unknown,
			};
		}

		/// <summary>
		/// Which factions the mod gates this behind, read from its prerequisite tokens.
		/// </summary>
		static IReadOnlyList<string> FactionsOf(BuildableInfo buildable)
		{
			var factions = new List<string>();
			foreach (var prerequisite in buildable.Prerequisites)
			{
				// Tokens look like "~structures.soviet" or "~!structures.ukraine"; the faction is
				// whatever follows the last dot, and a negated token excludes rather than includes.
				if (prerequisite.StartsWith("~!", StringComparison.Ordinal))
					continue;

				var dot = prerequisite.LastIndexOf('.');
				if (dot < 0 || !prerequisite.Contains("structures.", StringComparison.Ordinal))
					continue;

				factions.Add(prerequisite[(dot + 1)..]);
			}

			factions.Sort(StringComparer.Ordinal);
			return factions;
		}

		/// <summary>A one-line account of what the mod offers, for telemetry.</summary>
		public string Summary()
		{
			var byCategory = All
				.GroupBy(e => e.Category)
				.OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
				.Select(g => $"{g.Key.ToString().ToLowerInvariant()} {g.Count()}");

			return $"catalogue: {Count} buildable ({string.Join(", ", byCategory)}), " +
				$"{All.Count(e => e.IsArmed)} armed, {All.Count(e => e.IsSubmarine)} submarine";
		}
	}
}
