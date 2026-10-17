namespace SDeleteGUI.Services;

internal static class SizeFormatter
{
	public static string Format(long bytes)
	{
		const double kb = 1024d;
		const double mb = kb * 1024;
		const double gb = mb * 1024;
		const double tb = gb * 1024;

		if (bytes >= tb) return $"{bytes / tb:0.#} {Loc.Get("UnitTB")}";
		if (bytes >= gb) return $"{bytes / gb:0.#} {Loc.Get("UnitGB")}";
		if (bytes >= mb) return $"{bytes / mb:0.#} {Loc.Get("UnitMB")}";
		if (bytes >= kb) return $"{bytes / kb:0.#} {Loc.Get("UnitKB")}";
		return $"{bytes} {Loc.Get("UnitB")}";
	}
}
