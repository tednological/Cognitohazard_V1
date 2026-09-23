namespace Cognitohazard.Sim;

/// <summary>
/// FNV-1a 64. Accumulates the whole simulation state into the value compared by
/// the replay test (spec §4.2). Order of Add calls is significant and must stay
/// stable — changing it invalidates every golden hash.
/// </summary>
public struct Hash64
{
	private const ulong Offset = 14695981039346656037UL;
	private const ulong Prime = 1099511628211UL;

	private ulong _v;

	public static Hash64 New() => new Hash64 { _v = Offset };

	public ulong Value => _v;

	public void Add(ulong word)
	{
		for (int i = 0; i < 8; i++)
		{
			_v ^= (word >> (i * 8)) & 0xFF;
			_v *= Prime;
		}
	}

	public void Add(long word) => Add(unchecked((ulong)word));
	public void Add(int word) => Add(unchecked((ulong)(long)word));
	public void Add(bool b) => Add(b ? 1UL : 0UL);
}
