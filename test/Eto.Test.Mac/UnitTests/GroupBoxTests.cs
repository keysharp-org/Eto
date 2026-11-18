using Eto.Test.UnitTests;
using NUnit.Framework;

namespace Eto.Test.Mac.UnitTests
{
	[TestFixture]
	public class GroupBoxTests : TestBase
	{
		[Test]
		public void TitlesShouldInterpretMnemonics()
		{
			Invoke(() =>
			{
				using var groupBox = new GroupBox();
				var native = (NSBox)groupBox.ControlObject;
				foreach (var (source, displayed) in new[]
				{
					("First && second", "First & second"),
					("&Criteria", "Criteria"),
					("Save && &Close", "Save & Close"),
					("_First && second_part", "_First & second_part"),
					("&&&&", "&&"),
					("Plain", "Plain"),
					("&", ""),
					("", ""),
					(null, ""),
					("&Restored && title", "Restored & title")
				})
				{
					groupBox.Text = source;
					Assert.Multiple(() =>
					{
						Assert.That(groupBox.Text, Is.EqualTo(source ?? string.Empty));
						Assert.That(native.Title, Is.EqualTo(displayed));
						Assert.That(native.TitlePosition, Is.EqualTo(displayed.Length == 0 ? NSTitlePosition.NoTitle : NSTitlePosition.AtTop));
					});
				}
			});
		}
	}
}
