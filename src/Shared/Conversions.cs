namespace Eto
{

	/// <summary>
	/// Conversions code which are platform independent
	/// </summary>
	public static partial class Conversions
	{
		static Regex _platformMnemonic;

		/// <summary>
		/// The regex for mnemonic strings to be converted to a platform
		/// </summary>
		internal static Regex PlatformMnemonic => _platformMnemonic ?? (_platformMnemonic = new Regex(@"(?<=([^&](?:[&]{2})*)|^)[&](?![&])", RegexOptions.Compiled));

		/// <summary>
		/// Translates degrees to radians.
		/// </summary>
		/// <param name="angle">The angle for conversion.</param>
		public static float DegreesToRadians(float angle)
		{
			return (float)Math.PI * angle / 180.0f;
		}

		/// <summary>
		/// Translates mnemonics in a string to a platform.
		/// </summary>
		/// <param name="value">The value to check for mnemonics.</param>
		public static string ToPlatformMnemonic(this string value)
		{
			if (value == null)
				return string.Empty;

			value = value.Replace("_", "__");

			var index = FindMnemonic(value, '&');
			if (index >= 0)
			{
				var sb = new StringBuilder(value);
				sb[index] = '_';
				sb.Replace("&&", "&");
				return sb.ToString();
			}

			return value.Replace("&&", "&");
		}

		/// <summary>
		/// Translates mnemonics in a string to eto.
		/// </summary>
		/// <param name="value">The value to check for mnemonics.</param>
		public static string ToEtoMnemonic(this string value)
		{
			if (value == null)
				return null;

			var index = FindMnemonic(value, '_');
			if (index >= 0)
			{
				var sb = new StringBuilder(value);
				sb[index] = '&';
				sb.Replace("__", "_");
				return sb.ToString();
			}

			return value.Replace("__", "_");
		}

		/// <summary>
		/// Finds the mnemonic marker the way <see cref="PlatformMnemonic"/> matches one: the last marker of the first
		/// odd-length run of markers, except a run of three or more at the start, whose pairs have no other character
		/// before them. A compiled Regex costs about 10 ms the first time a menu or button text is set.
		/// </summary>
		static int FindMnemonic(string value, char marker)
		{
			for (int i = 0; i < value.Length; i++)
			{
				if (value[i] != marker)
					continue;
				int start = i;
				while (i + 1 < value.Length && value[i + 1] == marker)
					i++;
				if (((i - start) & 1) == 0 && (start > 0 || i == 0))
					return i;
			}
			return -1;
		}

	}

}
