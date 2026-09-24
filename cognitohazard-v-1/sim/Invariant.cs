using System.Globalization;

namespace Cognitohazard.Sim;

/// <summary>
/// Number text in the sim's file formats (levels, replays, loadouts) is
/// CULTURE-INVARIANT. The plain int.TryParse / StringBuilder.Append(int)
/// overloads use the machine's culture, and several write a negative number
/// with something other than '-': sv-SE writes U+2212, fa-IR a mark and U+2212,
/// ar-SA a mark and '-'. A replay recorded on such a machine wrote every
/// left/up move that way, and a machine in any other culture dropped those
/// frame lines as unparseable, so the replay diverged for reasons that had
/// nothing to do with the sim. Every number these formats read goes through
/// here, and every number they write goes through an invariant Append.
/// </summary>
internal static class Invariant
{
	public static bool TryInt(string s, out int v)
		=> int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

	public static bool TryLong(string s, out long v)
		=> long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

	public static bool TryULong(string s, out ulong v)
		=> ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

	public static bool TryHex(string s, out ulong v)
		=> ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
}
