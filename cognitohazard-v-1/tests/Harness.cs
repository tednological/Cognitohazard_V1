using System;
using System.Collections.Generic;

namespace Cognitohazard.Tests;

/// <summary>Minimal assertion harness. Non-zero exit on failure (spec §4.3).</summary>
public static class H
{
	private sealed class Result
	{
		public string Name = "";
		public bool Passed;
		public string Detail = "";
	}

	private static readonly List<Result> Results = new();
	private static string _group = "";

	public static void Group(string name) => _group = name;

	public static void Check(string name, bool ok, string detail = "")
	{
		Results.Add(new Result { Name = _group + " / " + name, Passed = ok, Detail = detail });
	}

	public static void Eq(string name, long actual, long expected)
		=> Check(name, actual == expected, $"expected {expected}, got {actual}");

	public static void Near(string name, double actual, double expected, double tol)
		=> Check(name, Math.Abs(actual - expected) <= tol,
			$"expected {expected:F6} +/-{tol:G3}, got {actual:F6}");

	public static void NoThrow(string name, Action a)
	{
		try { a(); Check(name, true); }
		catch (Exception e) { Check(name, false, e.GetType().Name + ": " + e.Message); }
	}

	public static int Report()
	{
		int pass = 0, fail = 0;
		foreach (var r in Results)
		{
			if (r.Passed) { pass++; Console.WriteLine($"  PASS  {r.Name}"); }
			else { fail++; Console.WriteLine($"  FAIL  {r.Name}\n          {r.Detail}"); }
		}
		Console.WriteLine();
		Console.WriteLine($"{pass} passed, {fail} failed, {Results.Count} total");
		return fail == 0 ? 0 : 1;
	}
}
