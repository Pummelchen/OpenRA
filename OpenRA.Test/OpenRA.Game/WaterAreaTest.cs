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
using OpenRA.Mods.Common;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class WaterAreaTest
	{
		[TestCase(TestName = "Empty map has no water.")]
		public void EmptyMap()
		{
			Assert.That(AIUtils.LargestConnectedRegion(10, 10, (x, y) => false), Is.EqualTo(0));
		}

		[TestCase(TestName = "A tiny 3x3 lake is a small region, not a navy water body.")]
		public void TinyLake()
		{
			static bool IsWater(int x, int y) => x >= 4 && x <= 6 && y >= 4 && y <= 6;
			Assert.That(AIUtils.LargestConnectedRegion(10, 10, IsWater), Is.EqualTo(9));
		}

		[TestCase(TestName = "A large sea (10x10) is a proper water body.")]
		public void LargeSea()
		{
			static bool IsWater(int x, int y) => x >= 0 && x <= 9 && y >= 0 && y <= 9;
			Assert.That(AIUtils.LargestConnectedRegion(10, 10, IsWater), Is.EqualTo(100));
		}

		[TestCase(TestName = "Two separate lakes do not merge into one region.")]
		public void SeparateLakes()
		{
			static bool IsWater(int x, int y) =>
				(x >= 1 && x <= 3 && y >= 1 && y <= 3) ||
				(x >= 6 && x <= 8 && y >= 6 && y <= 8);
			Assert.That(AIUtils.LargestConnectedRegion(10, 10, IsWater), Is.EqualTo(9));
		}

		[TestCase(TestName = "Diagonal adjacency connects a region (8-connectivity).")]
		public void DiagonalConnectivity()
		{
			static bool IsWater(int x, int y) =>
				(x == 2 && y == 2) || (x == 3 && y == 3) || (x == 4 && y == 4);
			Assert.That(AIUtils.LargestConnectedRegion(10, 10, IsWater), Is.EqualTo(3));
		}

		[TestCase(TestName = "A 9x9 body passes a 64-cell threshold; a 7x7 body does not.")]
		public void Threshold()
		{
			static bool Big(int x, int y) => x < 9 && y < 9;
			static bool Small(int x, int y) => x < 7 && y < 7;
			Assert.That(AIUtils.LargestConnectedRegion(10, 10, Big), Is.GreaterThanOrEqualTo(64));
			Assert.That(AIUtils.LargestConnectedRegion(10, 10, Small), Is.LessThan(64));
		}
	}
}
