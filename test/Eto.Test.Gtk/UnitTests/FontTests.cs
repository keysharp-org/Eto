using Eto.GtkSharp.Drawing;
using Eto.Test.UnitTests;
using NUnit.Framework;

namespace Eto.Test.Gtk.UnitTests
{
	[TestFixture, NonParallelizable]
	public class FontTests : TestBase
	{
		private const string MissingFamily = "Eto Missing Theme Font 8C797EBB";

		[Test]
		public void NativeDescriptionShouldResolveMissingFamily() => Invoke(() =>
		{
			using (var font = new Font(new FontHandler(MissingFamily + " Bold Italic 13")))
			{
				var familyName = font.FamilyName;
				Assert.That(Fonts.AvailableFontFamilies.Select(family => family.Name), Does.Contain(familyName));
				Assert.That(font.Family.Name, Is.EqualTo(familyName));
				Assert.That(font.Size, Is.EqualTo(13));
				Assert.That(font.Bold, Is.True);
				Assert.That(font.Italic, Is.True);
				using (var recreated = new Font(font.FamilyName, font.Size))
					Assert.That(recreated.MeasureString("Keysharp").Width, Is.GreaterThan(0));
			}
			Assert.Throws<ArgumentOutOfRangeException>(() => new FontFamily(MissingFamily));
		});

		[Test]
		public void SystemFontShouldResolveMissingThemeFamily() => Invoke(() =>
		{
			var settings = global::Gtk.Settings.Default;
			var previous = settings.FontName;
			try
			{
				settings.FontName = MissingFamily + " 11";
				SystemFonts.ClearCache();
				var font = SystemFonts.Default();
				Assert.That(((FontHandler)font.Handler).Control.Family, Is.EqualTo(MissingFamily));
				Assert.That(Fonts.AvailableFontFamilies.Select(family => family.Name), Does.Contain(font.FamilyName));
				using (var recreated = new Font(font.FamilyName, font.Size))
					Assert.That(recreated.MeasureString("Keysharp").Width, Is.GreaterThan(0));
			}
			finally
			{
				settings.FontName = previous;
				SystemFonts.ClearCache();
			}
		});
	}
}
