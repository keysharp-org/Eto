using Eto.Test.UnitTests;
using NUnit.Framework;
using SharedApplicationTests = Eto.Test.UnitTests.Forms.ApplicationTests;

namespace Eto.Test.Gtk.UnitTests;

[TestFixture]
public class ApplicationTests : TestBase
{
	sealed class AttachedApplicationHandler : Eto.GtkSharp.Forms.ApplicationHandler
	{
		// The test host has initialized GTK; these attached instances need no second D-Bus registration.
		protected override void Initialize() { }
	}

	static Platform CreatePlatform()
	{
		var platform = Eto.Platform.Copy(Platform.Instance);
		platform.Add<Application.IHandler>(() => new AttachedApplicationHandler());
		return platform;
	}

	[Test, InvokeOnUI]
	public void FailedRunShouldReleaseItsStartupForm() => SharedApplicationTests.VerifyFailedRunReleasesStartupForm(CreatePlatform());

	[Test, InvokeOnUI]
	public void QuitShouldIgnoreDisposedMainForm() => SharedApplicationTests.VerifyQuitIgnoresDisposedMainForm(CreatePlatform());
}
