using Eto.Test.UnitTests;
using Eto.WinForms.Forms;
using NUnit.Framework;
using System.Diagnostics;

namespace Eto.Test.WinForms.UnitTests
{
	[TestFixture]
	public class ApplicationTests : TestBase
	{
		[Test, InvokeOnUI]
		public void RunIterationBeforeRunShouldEnableWorkerAsyncInvoke()
		{
			// A fresh handler has no context from Run or Attach, even though the test application's loop is running.
			using var handler = new ApplicationHandler();
			handler.RunIteration(1);

			var called = false;
			var post = Task.Run(() => handler.AsyncInvoke(() => called = true));
			Assert.That(post.Wait(2000), Is.True, "The worker must be able to post without waiting for dispatch.");

			var watch = Stopwatch.StartNew();
			while (!called && watch.ElapsedMilliseconds < 2000)
				handler.RunIteration((int)Math.Max(0, 2000 - watch.ElapsedMilliseconds));

			Assert.That(called, Is.True, "A standalone iteration must initialize the transport used by worker callbacks.");
		}
	}
}
