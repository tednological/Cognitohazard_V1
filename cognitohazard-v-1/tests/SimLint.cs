using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Cognitohazard.Tests;

/// <summary>
/// Enforces spec §3.3 mechanically. Those constructs are described as
/// build-breaking rather than stylistic, so they get a test rather than a code
/// review: a single stray `randf` or `delta` in sim/ silently destroys replay
/// determinism and produces no other symptom.
///
/// Comments and string literals are stripped before matching, so prose that
/// merely mentions a banned name does not trip it.
/// </summary>
public static class SimLint
{
	private static readonly (string Name, string Pattern)[] Forbidden =
	{
		("Godot types",        @"\bGodot\b"),
		("randf/randi",        @"\brand[fi]\b"),
		("Time singleton",     @"\bTime\s*\."),
		("OS singleton",       @"\bOS\s*\."),
		("Engine singleton",   @"\bEngine\s*\."),
		("Input singleton",    @"\bInput\s*\."),
		("Node type",          @"\bNode\b"),
		("delta",              @"\bdelta\b"),
		("float",              @"\bfloat\b"),
		("double",             @"\bdouble\b"),
		("System.Math",        @"\bMath\s*\."),
		("System.Random",      @"\bRandom\b"),
		("DateTime",           @"\bDateTime\b"),
		("Stopwatch",          @"\bStopwatch\b"),
		// Hash-ordered collections whose iteration order would leak into state.
		// SortedDictionary and SortedSet are fine and are excluded by the \b
		// boundary plus the explicit negative lookbehind below.
		("unordered Dictionary", @"(?<!Sorted)\bDictionary\s*<"),
		("HashSet",            @"\bHashSet\s*<"),
	};

	private static string SimDir()
	{
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !Directory.Exists(Path.Combine(d.FullName, "sim"))) d = d.Parent;
		return d == null ? "sim" : Path.Combine(d.FullName, "sim");
	}

	/// <summary>Remove block comments, line comments, then string and char literals.</summary>
	private static string Strip(string src)
	{
		src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
		src = Regex.Replace(src, @"//[^\n]*", " ");
		src = Regex.Replace(src, @"""(\\.|[^""\\])*""", "\"\"");
		src = Regex.Replace(src, @"'(\\.|[^'\\])'", "''");
		return src;
	}

	public static void Run()
	{
		H.Group("sim lint (spec 3.3)");

		string dir = SimDir();
		if (!Directory.Exists(dir)) { H.Check("sim/ directory found", false, dir); return; }

		string[] files = Directory.GetFiles(dir, "*.cs");
		Array.Sort(files, StringComparer.Ordinal);
		H.Check("sim/ has sources", files.Length > 0, $"{files.Length} files");

		foreach (var (name, pattern) in Forbidden)
		{
			var hits = new List<string>();
			foreach (string f in files)
			{
				string[] lines = Strip(File.ReadAllText(f)).Split('\n');
				for (int i = 0; i < lines.Length; i++)
					if (Regex.IsMatch(lines[i], pattern))
						hits.Add($"{Path.GetFileName(f)}:{i + 1}");
			}
			H.Check($"no {name} in sim/", hits.Count == 0, string.Join(", ", hits));
		}

		// The lint is only worth anything if it can actually see a violation, so
		// prove the matcher fires on a known-bad sample.
		string bad = Strip("var x = randf(); // randf in a comment is fine\nfloat y;");
		H.Check("lint detects a planted violation",
			Regex.IsMatch(bad, @"\brand[fi]\b") && Regex.IsMatch(bad, @"\bfloat\b"));
		string good = Strip("// this comment says randf and float and delta\nint x = 1;");
		H.Check("lint ignores comments",
			!Regex.IsMatch(good, @"\brand[fi]\b") && !Regex.IsMatch(good, @"\bfloat\b"));
	}
}
