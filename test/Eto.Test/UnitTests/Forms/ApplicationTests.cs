using NUnit.Framework;

namespace Eto.Test.UnitTests.Forms;

[TestFixture]
public class ApplicationTests : TestBase
{
	const string LibGLib = "libglib-2.0.so.0";
	sealed class DelayedSynchronizationContext : System.Threading.SynchronizationContext
	{
		readonly Queue<Action> _callbacks = new();

		public override void Post(System.Threading.SendOrPostCallback callback, object state)
		{
			lock (_callbacks)
				_callbacks.Enqueue(() => callback(state));
		}

		internal void Drain()
		{
			while (true)
			{
				Action callback;
				lock (_callbacks)
				{
					if (_callbacks.Count == 0)
						return;
					callback = _callbacks.Dequeue();
				}
				callback();
			}
		}
	}

	[System.Runtime.InteropServices.DllImport(LibGLib,
		CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
	static extern IntPtr g_variant_parse(IntPtr type, string text, IntPtr limit, IntPtr endptr, IntPtr error);

	[System.Runtime.InteropServices.DllImport(LibGLib,
		CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
	static extern void g_variant_unref(IntPtr value);

	[Test, InvokeOnUI]
	public void ReinitializingWithNewPlatformShouldThrowException()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			_ = new Application(Platform.Instance.GetType().AssemblyQualifiedName);
		});
	}

	[Test, InvokeOnUI]
	public void ReinitializingWithCurrentPlatformShouldThrowException()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			_ = new Application(Platform.Instance);
		});
	}

	[TestCase(-1, false, null, ThemeStyle.Light)]
	[TestCase(-1, false, "", ThemeStyle.Light)]
	[TestCase(-1, true, null, ThemeStyle.Dark)]
	[TestCase(-1, false, "Adwaita-dark", ThemeStyle.Dark)]
	[TestCase(-1, false, "Yaru:Dark", ThemeStyle.Dark)]
	[TestCase(0, false, "Adwaita", ThemeStyle.Light)]
	[TestCase(1, false, "Adwaita", ThemeStyle.Dark)]
	[TestCase(2, true, "Adwaita-dark", ThemeStyle.Light)]
	[TestCase(3, false, "Adwaita-dark", ThemeStyle.Dark)]
	[InvokeOnUI]
	public void GtkSystemThemeStyleShouldResolvePortalThenSettings(int portalColorScheme,
		bool applicationPreferDarkTheme, string themeName, ThemeStyle expected)
	{
		if (!Platform.Instance.IsGtk)
			Assert.Ignore("This test is specific to the GTK platform.");

		var type = Platform.Instance.GetType().Assembly.GetType("Eto.GtkSharp.Forms.ThemeHandler");
		var method = type?.GetMethod("ResolveSystemThemeStyle",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		var portalValue = portalColorScheme < 0 ? (uint?)null : (uint)portalColorScheme;

		Assert.That(method, Is.Not.Null);
		Assert.That(method.Invoke(null, new object[] { portalValue, applicationPreferDarkTheme, themeName }),
			Is.EqualTo(expected));
	}

	[Test, InvokeOnUI]
	public void GtkSystemThemeNotificationShouldQueueManagedCallback()
	{
		if (!Platform.Instance.IsGtk)
			Assert.Ignore("This test is specific to the GTK platform.");

		var type = Platform.Instance.GetType().Assembly.GetType("Eto.GtkSharp.Forms.LinuxSystemTheme");
		var queueChanged = type?.GetMethod("QueueReadResult",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		Action queued = null;
		var expected = new InvalidOperationException();
		Action<uint?> changed = _ => throw expected;
		Action<Action> asyncInvoke = action => queued = action;
		var listener = Activator.CreateInstance(type,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
			null, new object[] { changed, asyncInvoke }, null);

		try
		{
			Assert.That(queueChanged, Is.Not.Null);
			Assert.DoesNotThrow(() => queueChanged.Invoke(listener, new object[] { 0L, (uint?)1 }));
			Assert.That(queued, Is.Not.Null);
			Assert.That(Assert.Throws<InvalidOperationException>(() => queued()), Is.SameAs(expected));
		}
		finally
		{
			(listener as IDisposable)?.Dispose();
		}
	}

	[Test, InvokeOnUI]
	public void GtkSystemThemeNotificationShouldParsePortalValues()
	{
		if (!Platform.Instance.IsGtk)
			Assert.Ignore("This test is specific to the GTK platform.");

		var type = Platform.Instance.GetType().Assembly.GetType("Eto.GtkSharp.Forms.LinuxSystemTheme");
		var settingChanged = type?.GetMethod("HandleSettingChanged",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var queued = new Queue<Action>();
		var changes = new List<uint?>();
		var listener = Activator.CreateInstance(type,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
			null, new object[] { (Action<uint?>)changes.Add, (Action<Action>)queued.Enqueue }, null);

		try
		{
			Assert.That(settingChanged, Is.Not.Null);
			InvokeWithVariant(settingChanged, listener,
				"('org.freedesktop.appearance', 'color-scheme', <uint32 0>)");
			queued.Dequeue()();
			InvokeWithVariant(settingChanged, listener,
				"('org.freedesktop.appearance', 'color-scheme', <uint32 1>)");
			queued.Dequeue()();
			InvokeWithVariant(settingChanged, listener,
				"('org.freedesktop.appearance', 'color-scheme', <uint32 3>)");
			queued.Dequeue()();
			InvokeWithVariant(settingChanged, listener,
				"('org.freedesktop.appearance', 'accent-color', <uint32 1>)");
			InvokeWithVariant(settingChanged, listener,
				"('org.freedesktop.appearance', 'color-scheme', <'dark'>)");

			Assert.That(queued, Is.Empty);
			Assert.That(changes, Is.EqualTo(new uint?[] { 0, 1, 3 }));
		}
		finally
		{
			(listener as IDisposable)?.Dispose();
		}
	}

	[TestCase("(<uint32 1>,)", 1u)]
	[TestCase("(<<uint32 2>> ,)", 2u)]
	[InvokeOnUI]
	public void GtkSystemThemeReadShouldUnwrapPortalVariants(string variantText, uint expected)
	{
		if (!Platform.Instance.IsGtk)
			Assert.Ignore("This test is specific to the GTK platform.");

		var type = Platform.Instance.GetType().Assembly.GetType("Eto.GtkSharp.Forms.LinuxSystemTheme");
		var getValue = type?.GetMethod("GetUInt32Child",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
		var variant = g_variant_parse(IntPtr.Zero, variantText, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		try
		{
			Assert.That(getValue, Is.Not.Null);
			Assert.That(variant, Is.Not.EqualTo(IntPtr.Zero));
			Assert.That(getValue.Invoke(null, new object[] { variant, 0 }), Is.EqualTo((uint?)expected));
		}
		finally
		{
			if (variant != IntPtr.Zero)
				g_variant_unref(variant);
		}
	}

	[Test, InvokeOnUI]
	public void GtkSystemThemeShouldSuppressStaleAndDisposedCallbacks()
	{
		if (!Platform.Instance.IsGtk)
			Assert.Ignore("This test is specific to the GTK platform.");

		var type = Platform.Instance.GetType().Assembly.GetType("Eto.GtkSharp.Forms.LinuxSystemTheme");
		var settingChanged = type?.GetMethod("HandleSettingChanged",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var ownerChanged = type?.GetMethod("HandleNameOwnerChanged",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var queueReadResult = type?.GetMethod("QueueReadResult",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var queued = new Queue<Action>();
		var changes = new List<uint?>();
		var listener = Activator.CreateInstance(type,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
			null, new object[] { (Action<uint?>)changes.Add, (Action<Action>)queued.Enqueue }, null);

		Assert.Multiple(() =>
		{
			Assert.That(settingChanged, Is.Not.Null);
			Assert.That(ownerChanged, Is.Not.Null);
			Assert.That(queueReadResult, Is.Not.Null);
		});
		InvokeWithVariant(settingChanged, listener,
			"('org.freedesktop.appearance', 'color-scheme', <uint32 1>)");
		InvokeWithVariant(ownerChanged, listener,
			"('org.freedesktop.portal.Desktop', ':1.42', '')");
		Assert.That(queued, Has.Count.EqualTo(2));
		while (queued.Count > 0)
			queued.Dequeue()();
		Assert.That(changes, Is.EqualTo(new uint?[] { null }));

		(listener as IDisposable)?.Dispose();
		queueReadResult.Invoke(listener, new object[] { 2L, (uint?)1 });
		queued.Dequeue()();
		Assert.That(changes, Is.EqualTo(new uint?[] { null }));
	}

	[Test, InvokeOnUI]
	public void GtkSystemThemeStartAndDisposeShouldBeIdempotent()
	{
		if (!Platform.Instance.IsGtk)
			Assert.Ignore("This test is specific to the GTK platform.");

		var type = Platform.Instance.GetType().Assembly.GetType("Eto.GtkSharp.Forms.LinuxSystemTheme");
		var start = type?.GetMethod("Start",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var disposed = type?.GetField("_disposed",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var connection = type?.GetField("_connection",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var settingCallbackHandle = type?.GetField("_settingCallbackHandle",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var ownerCallbackHandle = type?.GetField("_ownerCallbackHandle",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var listener = Activator.CreateInstance(type,
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
			null, new object[] { (Action<uint?>)(_ => { }), (Action<Action>)(_ => { }) }, null);
		var context = new DelayedSynchronizationContext();
		var previousContext = System.Threading.SynchronizationContext.Current;

		Assert.That(start, Is.Not.Null);
		try
		{
			System.Threading.SynchronizationContext.SetSynchronizationContext(context);
			start.Invoke(listener, null);
			start.Invoke(listener, null);
		}
		finally
		{
			System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
		}
		var timeout = System.Diagnostics.Stopwatch.StartNew();
		while ((IntPtr)connection.GetValue(listener) == IntPtr.Zero && timeout.ElapsedMilliseconds < 2000)
		{
			Application.Instance.RunIteration();
			System.Threading.Thread.Yield();
		}
		Assert.That(connection.GetValue(listener), Is.Not.EqualTo(IntPtr.Zero));
		Assert.Multiple(() =>
		{
			Assert.That(((System.Runtime.InteropServices.GCHandle)settingCallbackHandle.GetValue(listener)).IsAllocated,
				Is.True);
			Assert.That(((System.Runtime.InteropServices.GCHandle)ownerCallbackHandle.GetValue(listener)).IsAllocated,
				Is.True);
		});

		var disposeTask = System.Threading.Tasks.Task.Run(() => (listener as IDisposable)?.Dispose());
		timeout.Restart();
		while (!disposeTask.IsCompleted && timeout.ElapsedMilliseconds < 2000)
		{
			Application.Instance.RunIteration();
			System.Threading.Thread.Yield();
		}
		Assert.Multiple(() =>
		{
			Assert.That(disposeTask.IsCompleted, Is.True);
			Assert.That(disposed?.GetValue(listener), Is.EqualTo(1));
			Assert.That(connection.GetValue(listener), Is.Not.EqualTo(IntPtr.Zero));
		});
		context.Drain();
		Assert.Multiple(() =>
		{
			Assert.That(connection.GetValue(listener), Is.EqualTo(IntPtr.Zero));
			Assert.That(((System.Runtime.InteropServices.GCHandle)settingCallbackHandle.GetValue(listener)).IsAllocated,
				Is.False);
			Assert.That(((System.Runtime.InteropServices.GCHandle)ownerCallbackHandle.GetValue(listener)).IsAllocated,
				Is.False);
		});
		Assert.DoesNotThrow(() => (listener as IDisposable)?.Dispose());
	}

	[Test, InvokeOnUI]
	public void GtkPortalThemeChangeShouldUpdateSystemButNotFixedTheme()
	{
		if (!Platform.Instance.IsGtk)
			Assert.Ignore("This test is specific to the GTK platform.");

		var app = Application.Instance;
		var originalTheme = app.Theme;
		var handler = typeof(Application).GetProperty("Handler",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(app);
		var portalChanged = handler?.GetType().GetMethod("OnPortalColorSchemeChanged",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var eventCount = 0;
		EventHandler<EventArgs> themeChanged = (_, _) => eventCount++;
		app.ThemeChanged += themeChanged;

		try
		{
			Assert.That(portalChanged, Is.Not.Null);
			app.Theme = Themes.System;
			var expected = app.Theme.ThemeStyle == ThemeStyle.Light ? ThemeStyle.Dark : ThemeStyle.Light;
			var colorScheme = expected == ThemeStyle.Dark ? 1u : 2u;
			eventCount = 0;
			portalChanged.Invoke(handler, new object[] { (uint?)colorScheme });
			Assert.Multiple(() =>
			{
				Assert.That(app.Theme.ThemeStyle, Is.EqualTo(expected));
				Assert.That(eventCount, Is.EqualTo(1));
			});
			portalChanged.Invoke(handler, new object[] { (uint?)colorScheme });
			Assert.That(eventCount, Is.EqualTo(1));

			app.Theme = Themes.Light;
			eventCount = 0;
			portalChanged.Invoke(handler, new object[] { (uint?)(colorScheme == 1 ? 2u : 1u) });
			Assert.Multiple(() =>
			{
				Assert.That(app.Theme, Is.EqualTo(Themes.Light));
				Assert.That(eventCount, Is.Zero);
			});
		}
		finally
		{
			portalChanged?.Invoke(handler, new object[] { (uint?)null });
			app.Theme = originalTheme;
			app.ThemeChanged -= themeChanged;
		}
	}

	static void InvokeWithVariant(System.Reflection.MethodInfo method, object target, string text)
	{
		var variant = g_variant_parse(IntPtr.Zero, text, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		try
		{
			Assert.That(variant, Is.Not.EqualTo(IntPtr.Zero));
			method.Invoke(target, new object[] { variant });
		}
		finally
		{
			if (variant != IntPtr.Zero)
				g_variant_unref(variant);
		}
	}

	[TestCase(-1), ManualTest]
	[TestCase(10), ManualTest]
	[TestCase(1000), ManualTest]
	public void RunIterationShouldAllowBlocking(int delay)
	{
		int count = 0;
		Label countLabel = null;
		Form form = null;
		bool running = true;
		bool stopClicked = false;
		Application.Instance.Invoke(() =>
		{
			form = new Form();
			form.Closed += (sender, e) => running = false;
			form.Title = "RunIterationShouldAllowBlocking (" + nameof(delay) + ": " + delay + ")";
			var stopButton = new Button { Text = "Stop" };
			stopButton.Click += (sender, e) =>
			{
				running = false;
				stopClicked = true;
			};

			countLabel = new Label();

			var spinner = new Spinner { Enabled = false };

			var enableSpinnerCheck = new CheckBox { Text = "Enable spinner" };
			enableSpinnerCheck.CheckedChanged += (sender, e) =>
			{
				spinner.Enabled = enableSpinnerCheck.Checked == true;
			};

			var layout = new DynamicLayout();

			layout.Padding = 10;
			layout.DefaultSpacing = new Size(4, 4);
			layout.Add(new Label { Text = "The controls in this form should\nbe functional while test is running,\nand count should increase without moving the mouse.\nControls should be non-interactable during the delay.", TextAlignment = TextAlignment.Center });
			layout.Add(new DropDown { DataStore = new[] { "Item 1", "Item 2", "Item 3" } });
			layout.Add(new TextBox());
			layout.Add(new DateTimePicker());
			layout.AddCentered(enableSpinnerCheck);
			layout.AddCentered(spinner);
			layout.AddCentered(countLabel);
			layout.AddCentered(stopButton);

			form.Content = layout;
		});

		Application.Instance.Invoke(() =>
		{
			form.Owner = Application.Instance.MainForm;
			form.Show();
			do
			{
				Application.Instance.RunIteration();
				if (delay > 0)
					System.Threading.Thread.Sleep(delay);
				countLabel.Text = $"Iteration Count: {count++}";
			} while (running);
			form.Close();
		});

		Assert.That(stopClicked, Is.True, "#1 - Must press the stop button to close the form");
	}

	[Test]
	public void EnsureUIThreadShouldThrow()
	{
		Form form = null;
		TextBox textBox = null;
		var oldMode = Application.Instance.UIThreadCheckMode;
		Application.Instance.UIThreadCheckMode = UIThreadCheckMode.Error;
		Invoke(() =>
		{
			textBox = new TextBox();
			form = new Form();
		});

		Assert.Throws<UIThreadAccessException>(() => textBox.Text = "hello", "#1");
		Assert.Throws<UIThreadAccessException>(() => form.Bounds = new Rectangle(0, 0, 100, 100), "#2");

		Application.Instance.UIThreadCheckMode = oldMode;
	}
	
	public static Task<T> StartSTATask<T>(Func<T> func)
	{
		var tcs = new TaskCompletionSource<T>();
		var thread = new Thread(() =>
		{
			try
			{
				tcs.SetResult(func());
			}
			catch (Exception e)
			{
				tcs.SetException(e);
			}
		});
#pragma warning disable CA1416
		if (EtoEnvironment.Platform.IsWindows)
			thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
		thread.Start();
		return tcs.Task;
	}	

	[Test]
	public void ShowingUIInSeparateThreadShouldWork() => Async(async () =>
	{
		if (!Platform.Instance.SupportedFeatures.HasFlag(PlatformFeatures.MultiThreadedUI))
		{
			Assert.Inconclusive("Platform does not support multi-threaded UI");
			return;
		}
		int mainThreadId = Thread.CurrentThread.ManagedThreadId;
		int? threadId = null;
		bool? isSameThreadForInvoke = null;
		await StartSTATask(() =>
		{
			threadId = Thread.CurrentThread.ManagedThreadId;
			var app = new Application(Eto.Platform.Copy());
			app.Attach();

			var dialog = new Dialog { Title = "From Separate Thread", Size = new(300, 300) };

			app.InvokeAsync(async () =>
			{
				isSameThreadForInvoke = threadId == Thread.CurrentThread.ManagedThreadId;
				// let the modal dialog actually come up on its thread before closing it again
				await Task.Delay(250);
				dialog.Close();
			});

			dialog.ShowModal();
			return true;
		});

		Assert.That(isSameThreadForInvoke, Is.Not.Null);
		Assert.That(isSameThreadForInvoke, Is.EqualTo(true));
		Assert.That(threadId, Is.Not.EqualTo(mainThreadId));
	});
}
