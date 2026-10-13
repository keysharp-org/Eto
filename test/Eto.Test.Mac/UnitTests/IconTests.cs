using Eto.Test.UnitTests;
using NUnit.Framework;
using Eto.Mac;
using Eto.Mac.Drawing;

namespace Eto.Test.Mac64.UnitTests
{
	[TestFixture]
	public class IconTests : TestBase
	{
		[Test]
		public void RecreatingOrDisposingShouldReleaseDeferredFrames() => Invoke(() =>
		{
			using var handler = new IconHandler();
			using (var source = typeof(TestIcons).Assembly.GetManifestResourceStream("Eto.Test.Images.TestIcon.ico"))
				handler.Create(source);
			Assert.That(handler.Frames.Count(), Is.GreaterThan(1));

			using (var source = typeof(TestIcons).Assembly.GetManifestResourceStream("Eto.Test.Images.Logo.png"))
				handler.Create(source);
			using var bitmap = TestIcons.LogoBitmap;
			Assert.That(handler.Frames.Select(frame => frame.PixelSize),
				Is.EqualTo(new[] { bitmap.Size }));

			using (var source = typeof(TestIcons).Assembly.GetManifestResourceStream("Eto.Test.Images.TestIcon.ico"))
				handler.Create(source);
			handler.Dispose();
			Assert.That(handler.Frames, Is.Null);
		});

		[Test]
		public void BitmapToIconShouldNotChangeBitmapSize()
		{
			var bmp = TestIcons.LogoBitmap;
			var bitmapNSImage = bmp.ControlObject as NSImage;
			var bitmapRep = bitmapNSImage.Representations()[0] as NSBitmapImageRep;

			var oldSize = bmp.Size;

			var newSize = new Size(32, 32);
			// initial sanity check
			Assert.That(bitmapRep.Size.ToEtoSize(), Is.EqualTo(oldSize), "#1");

			var icon = bmp.WithSize(newSize);

			var iconNSImage = icon.ControlObject as NSImage;
			var iconRep = iconNSImage.Representations()[0] as NSBitmapImageRep;

			Assert.That(oldSize, Is.EqualTo(bmp.Size), "#2.1");
			Assert.That(icon.Size, Is.EqualTo(newSize), "#2.2");
			Assert.That(icon.Frames.First().PixelSize, Is.EqualTo(bmp.Size), "#2.3");

			// rep in icon needs the new size
			Assert.That(iconRep.Size.ToEtoSize(), Is.EqualTo(newSize), "#2.4");

			// rep in bitmap should have the old size still..
			Assert.That(bitmapRep.Size.ToEtoSize(), Is.EqualTo(oldSize), "#3");

			icon.Dispose();
			bmp.Dispose();
		}
	}
}
