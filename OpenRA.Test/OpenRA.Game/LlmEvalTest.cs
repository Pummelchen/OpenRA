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
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Runs the Python scorer suite in <c>ai/test_llm_eval.py</c> against the shipped
	/// <c>ai/llm_eval.py</c> (reqs 729-738).
	/// </para>
	/// <para>
	/// This fixture previously re-implemented the legality, oscillation and idle scorers in C# and
	/// asserted against its own copy, which meant the shipped Python could regress without any test
	/// failing - and left the other eight scorers untested entirely. Driving the real module through
	/// a subprocess is the only way a C# suite can honestly cover Python code.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class LlmEvalTest
	{
		[Test(Description = "The Python scorer suite passes against the shipped ai/llm_eval.py.")]
		public void PythonScorerSuitePasses()
		{
			var repoRoot = FindRepositoryRoot();
			if (repoRoot == null)
				Assert.Ignore("Repository root not found from the test working directory.");

			var suite = Path.Combine(repoRoot, "ai", "test_llm_eval.py");
			if (!File.Exists(suite))
				Assert.Ignore($"Scorer suite not present at {suite}.");

			var python = FindPython(repoRoot);
			if (python == null)
				Assert.Ignore(
					"No Python 3.11+ interpreter found. The AI tooling requires 3.11 or newer; "
					+ "create the project-local runtime described in ai/README.md.");

			var (exitCode, output) = Run(python, suite, repoRoot);
			Assert.That(exitCode, Is.Zero, $"ai/test_llm_eval.py failed:\n{output}");
			Assert.That(output, Does.Contain("llm_eval tests OK"));
		}

		/// <summary>Walks up from the test assembly until the directory holding ai/llm_eval.py is found.</summary>
		static string FindRepositoryRoot()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 6 && dir != null; i++)
			{
				if (File.Exists(Path.Combine(dir.FullName, "ai", "llm_eval.py")))
					return dir.FullName;

				dir = dir.Parent;
			}

			return null;
		}

		/// <summary>
		/// Prefers the project-local runtime, then any system interpreter new enough to run the
		/// tooling. The system python3 is frequently older than 3.11, so it is verified, not assumed.
		/// </summary>
		static string FindPython(string repoRoot)
		{
			var candidates = new[]
			{
				Path.Combine(repoRoot, ".venv-ai", "bin", "python"),
				"/opt/homebrew/bin/python3.13",
				"/opt/homebrew/bin/python3",
				"/usr/local/bin/python3",
				"python3"
			};

			foreach (var candidate in candidates)
				if (IsSupportedPython(candidate))
					return candidate;

			return null;
		}

		static bool IsSupportedPython(string python)
		{
			try
			{
				var (exitCode, output) = Run(python,
					"-c \"import sys; print(1 if sys.version_info >= (3, 11) else 0)\"", null);
				return exitCode == 0 && output.Trim().EndsWith('1');
			}
			catch (Exception)
			{
				// An absent interpreter is a candidate that does not apply, not a test failure.
				return false;
			}
		}

		static (int ExitCode, string Output) Run(string fileName, string arguments, string workingDirectory)
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			if (workingDirectory != null)
				startInfo.WorkingDirectory = workingDirectory;

			using var process = Process.Start(startInfo);
			var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

			// A hung interpreter must fail the test rather than stall the whole suite.
			if (!process.WaitForExit(120000))
			{
				process.Kill(true);
				return (-1, output + "\n(timed out after 120s)");
			}

			return (process.ExitCode, output);
		}
	}
}
