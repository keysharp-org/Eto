using Eto.Test.UnitTests;
using NUnit.Framework;
using System.Windows.Interop;

namespace Eto.Test.Wpf.UnitTests;

[TestFixture]
public class ApplicationTests : TestBase
{
	[Test, InvokeOnUI]
	public void RunIterationShouldRaiseIdleWhenTheQueueIsEmpty()
	{
		Application.Instance.RunIteration();
		var idle = false;
		void OnIdle(object sender, EventArgs e) => idle = true;
		ComponentDispatcher.ThreadIdle += OnIdle;
		try
		{
			Application.Instance.RunIteration();
			Assert.That(idle, Is.True);
		}
		finally
		{
			ComponentDispatcher.ThreadIdle -= OnIdle;
		}
	}

	[Test, InvokeOnUI]
	public void RunIterationShouldPreprocessNativeMessages()
	{
		const int messageId = 0x8123;
		using var source = new HwndSource(new HwndSourceParameters("Eto pump test") { ParentWindow = new IntPtr(-3) });
		var filtered = false;
		var dispatched = false;
		source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
		{
			if (message == messageId)
				dispatched = true;
			return IntPtr.Zero;
		});
		void Filter(ref MSG message, ref bool handled)
		{
			if (message.hwnd == source.Handle && message.message == messageId)
				filtered = true;
		}
		ComponentDispatcher.ThreadFilterMessage += Filter;
		try
		{
			Assert.That(Win32.PostMessage(source.Handle, (Win32.WM)messageId, IntPtr.Zero, IntPtr.Zero), Is.True);
			Application.Instance.RunIteration();
			Assert.That(filtered, Is.True, "WPF must preprocess messages before native dispatch.");
			Assert.That(dispatched, Is.True);
		}
		finally
		{
			ComponentDispatcher.ThreadFilterMessage -= Filter;
		}
	}
}
