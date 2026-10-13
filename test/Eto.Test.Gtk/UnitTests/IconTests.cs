using Eto.GtkSharp.Drawing;
using Eto.Test.UnitTests;
using NUnit.Framework;

namespace Eto.Test.Gtk.UnitTests
{
	[TestFixture]
	public class IconTests : TestBase
	{
		[Test]
		public void DeferredFramesShouldSurviveSourceStreamDisposal() => Invoke(() =>
		{
			var handler = new IconHandler();
			using (var source = OpenIcon())
				handler.CreateDeferred(source);
			using var icon = new Icon(handler);
			using var expected = TestIcons.TestIcon;
			Assert.That(icon.Frames.Select(frame => frame.PixelSize),
				Is.EqualTo(expected.Frames.Select(frame => frame.PixelSize)));
			Assert.That(icon.GetFrame(1, new Size(32, 32)).PixelSize,
				Is.EqualTo(expected.GetFrame(1, new Size(32, 32)).PixelSize));
			Assert.That(icon.GetFrame(1), Is.SameAs(icon.GetFrame(1)));
		});

		[Test]
		public void RecreatingOrDisposingShouldReleaseDeferredFrames() => Invoke(() =>
		{
			using var bitmap = new Bitmap(20, 20, PixelFormat.Format32bppRgba);
			using var handler = new IconHandler();
			using (var source = OpenIcon())
				handler.CreateDeferred(source);
			var scaled = handler.GetPixbuf(new Size(5, 5));
			handler.Create(new[] { new IconFrame(1, bitmap) });
			Assert.That(handler.Frames.Select(frame => frame.PixelSize),
				Is.EqualTo(new[] { bitmap.Size }));
			Assert.That(handler.GetPixbuf(new Size(5, 5)), Is.Not.SameAs(scaled));

			using (var source = OpenIcon())
				handler.Create(source);
			using var expected = TestIcons.TestIcon;
			Assert.That(handler.Frames.Select(frame => frame.PixelSize),
				Is.EqualTo(expected.Frames.Select(frame => frame.PixelSize)));

			using (var source = OpenIcon())
				handler.CreateDeferred(source);
			handler.Dispose();
			Assert.That(handler.Frames, Is.Null);
		});

		static Stream OpenIcon() => typeof(TestIcons).Assembly.GetManifestResourceStream("Eto.Test.Images.TestIcon.ico");
	}
}
