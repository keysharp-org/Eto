using Eto.Mac.Forms.Actions;
using Eto.Mac.Drawing;
using System.Runtime.CompilerServices;
namespace Eto.Mac.Forms
{
	public class ApplicationHandler : MacObject<NSApplication, Application, Application.ICallback>, Application.IHandler
	{
		bool _attached;
		bool? _activateOnStartup;
		bool _hasLaunched;
		bool _stopRequested;

		internal static bool QueueResizing { get; set; }

		public NSApplicationDelegate AppDelegate { get; set; }

		public bool AddFullScreenMenuItem { get; set; }

		public bool AllowClosingMainForm { get; set; }

		public bool ActivateOnStartup
		{
			get { return _activateOnStartup ?? !_attached && Debugger.IsAttached; }
			set { _activateOnStartup = value; }
		}

		/// <summary>
		/// Gets or sets a value indicating the app should take activation from whatever app currently holds it,
		/// instead of asking for it cooperatively.
		/// </summary>
		/// <remarks>
		/// macOS only hands activation to an app that asks for it when no other app is holding it, so an app
		/// started while something else is frontmost can show a window that never becomes key - and a control
		/// in it can never get focus. Turning this on takes activation regardless, which is only appropriate
		/// when stealing focus from the user is expected - an automated test run being the usual case.
		/// </remarks>
		/// <value><c>true</c> to take activation from the frontmost app; otherwise, <c>false</c>. Defaults to <c>false</c>.</value>
		public bool ActivateIgnoringOtherApps { get; set; }

		// pointer to the initial menu so we know whether we want to keep it or not for the main form
		internal IntPtr InitialMenu { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether native macOS crash reports are generated for uncaught .NET exceptions.
		/// </summary>
		/// <remarks>
		/// By default, this will be true when NOT debugging in Xamarin Studio.
		/// 
		/// The crash report will also include the .NET exception details, which can be extremely useful to determine 
		/// where crashes occurred.
		/// 
		/// When attaching to existing applications, this is assumed to be dealt with by native code and will not be 
		/// enabled regardless.
		/// </remarks>
		/// <value><c>true</c> to enable native crash reports; otherwise, <c>false</c>.</value>
		public bool EnableNativeCrashReport { get; set; } = !System.Diagnostics.Debugger.IsAttached;

#if Mac64
		/// <summary>
		/// Gets or sets a value indicating whether we should translate native exceptions into .NET so it shows the .net stack trace
		/// </summary>
		/// <remarks>
		/// This allows us to get the stack trace of the .NET Code if it calls something that causes a native crash.
		/// 
		/// There is currently no way to catch the translated exceptions, however the crash report will be much more useful.
		/// </remarks>
		/// <value><c>true</c> to enable native crash translation; otherwise, <c>false</c></value>
		public bool EnableNativeExceptionTranslation { get; set; } = true;
#endif

		public ApplicationHandler()
		{
			Control = NSApplication.SharedApplication;
		}

		public static ApplicationHandler Instance
		{
			get { return Application.Instance == null ? null : Application.Instance.Handler as ApplicationHandler; }
		}

		/// <summary>
		/// Event to inject custom functionality during the event loop of a modal session for an eto dialog.
		/// </summary>
		public event EventHandler<ModalEventArgs> ProcessModalSession;

		/// <summary>
		/// Raises the <see cref="ProcessModalSession"/> event.
		/// </summary>
		/// <param name="e">Event arguments</param>
		protected virtual void OnProcessModalSession(ModalEventArgs e)
		{
			if (ProcessModalSession != null)
				ProcessModalSession(this, e);
		}

		internal void TriggerProcessModalSession(ModalEventArgs e)
		{
			OnProcessModalSession(e);
		}

		public string BadgeLabel
		{
			get
			{
				var badgeLabel = Control.DockTile.BadgeLabel;
				return string.IsNullOrEmpty(badgeLabel) ? null : badgeLabel;
			}
			set
			{
				Control.DockTile.BadgeLabel = value ?? string.Empty;
				Control.DockTile.Display();
			}
		}

		public bool ShouldCloseForm(Window window, bool wasClosed)
		{
			if (ReferenceEquals(window, Widget.MainForm))
			{
				if (AllowClosingMainForm && wasClosed)
					Widget.MainForm = null;
				return AllowClosingMainForm;
			}

			return true;
		}

		protected override NSApplication CreateControl()
		{
			return NSApplication.SharedApplication;
		}

		protected override bool DisposeControl { get { return false; } }

		static void restart_WillTerminate(object sender, EventArgs e)
		{
			// re-open after we terminate
			var args = new string[]
			{
				"-c",
				"open \"$1\"",
				string.Empty,
				NSBundle.MainBundle.BundlePath
			};
#if ( MACOS10_15_OR_GREATER && NET8_0_OR_GREATER ) || MONOMAC
			NSTask.LaunchFromUrl(NSUrl.FromFilename("/bin/sh"), args, out var error, null);
#else
			NSTask.LaunchFromPath("/bin/sh", args);
#endif
		}

		public void Invoke(Action action)
		{
			if (NSThread.IsMain)
				action();
			else
				Control.InvokeOnMainThread(action);
		}

		public void AsyncInvoke(Action action)
		{
			Control.BeginInvokeOnMainThread(action);
		}

		public void Restart()
		{
			// prevent System.InvalidOperationException:
			// Event registration is overwriting existing delegate.
			// Either just use events or your own delegate: Eto.Mac.AppDelegate
			var oldDelegate = Control.Delegate;
			Control.Delegate = null;
			Control.WillTerminate += restart_WillTerminate;
			Control.Terminate(AppDelegate);

			// only get here if cancelled, remove event to restart
			Control.WillTerminate -= restart_WillTerminate;
			Control.Delegate = oldDelegate;
		}

		static readonly IntPtr selNextEventMatchingMaskUntilDateInModeDequeue_Handle = Selector.GetHandle("nextEventMatchingMask:untilDate:inMode:dequeue:");
		static readonly IntPtr selSendEvent_Handle = Selector.GetHandle("sendEvent:");

		// How long one RunIteration may keep dispatching before handing control back, the same bound the Gtk
		// backend uses. It only matters when something keeps the main loop from ever going idle (a source that
		// re-arms itself, a flood of events): the pump never waits for work, so an idle loop returns at once.
		static readonly long RunIterationTimeout = Stopwatch.Frequency / 10;

#if MACOS_NET
		static NSString DefaultRunLoopMode => CFRunLoop.ModeDefault;
#else
		static NSString DefaultRunLoopMode => CFRunLoop.CFDefaultRunLoopMode;
#endif

		public void RunIteration()
		{
			MacView.InMouseTrackingLoop = false;
			// Dispatch what the main loop already has ready, as the other platforms do, and never wait for more.
			var deadline = Stopwatch.GetTimestamp() + RunIterationTimeout;
			while (Stopwatch.GetTimestamp() < deadline)
			{
				// dequeue the next event, without waiting for one
				var evt = Control.NextEvent(NSEventMask.AnyEvent, NSDate.DistantPast, NSRunLoopMode.Default, true);
				if (evt != null)
				{
					Control.SendEvent(evt);
					continue;
				}

				// Events are only part of the main loop: timers (UITimer), performSelectorOnMainThread (Invoke,
				// AsyncInvoke), main queue blocks and async continuations are run loop sources, which the other
				// backends' pumps dispatch too. Run the ones that are ready without waiting, and stop once a pass
				// finds nothing to do. Handling a source can queue events, hence going round again.
				if (CFRunLoop.Current.RunInMode(DefaultRunLoopMode, 0, true) != CFRunLoopExitReason.HandledSource)
					break;
			}

			// This pump may have dispatched the event that a stopped Run() is waiting for.
			if (_stopRequested)
				PostWakeEvent();
		}

		public void Attach(object context)
		{
			_attached = true;
		}

		public void OnMainFormChanged()
		{
		}

		public void Run()
		{
			if (!_attached)
			{
				_stopRequested = false;
				if (!_hasLaunched)
				{
					if (EnableNativeCrashReport)
						CrashReporter.Attach();

#if Mac64
					// convert objective-c exceptions into .NET exceptions
					if (EnableNativeExceptionTranslation)
						NSSetUncaughtExceptionHandler(UncaughtExceptionHandler);
#endif

					EtoBundle.Init();

					EtoFontManager.Install();

					if (Control.Delegate == null)
						Control.Delegate = AppDelegate ?? new AppDelegate();

					// FinishLaunching fires applicationDidFinishLaunching: on the delegate,
					// which calls Initialize() and raises Application.Initialized.
					Control.FinishLaunching();
					_hasLaunched = true;
				}
				else
					Initialize(AppDelegate);

				// Run the AppKit event loop. Unlike NSApplicationMain this can be called
				// multiple times after Stop() stops the current loop. Callbacks during
				// FinishLaunching can stop the app before that loop starts.
				if (!_stopRequested)
					Control.Run();
				_stopRequested = false;
			}
			else
				Initialize(Control.Delegate as NSApplicationDelegate);
		}

		public void Initialize(NSApplicationDelegate appdelegate)
		{
			AppDelegate = appdelegate;
			if (ActivateOnStartup)
			{
				// if we're debugging, make the app active when it starts
				EnsureActivated(true, true, null);
			}
			Callback.OnInitialized(Widget, EventArgs.Empty);
		}

		public void Quit()
		{
			if (!NSThread.IsMain)
			{
				AsyncInvoke(Quit);
				return;
			}

			var args = new CancelEventArgs();
			// An earlier Run can leave its closed main form assigned, or the form may never have been shown.
			var mainForm = Widget.MainForm is { Loaded: true } form ? form.Handler as IMacWindow : null;
			if (mainForm != null)
				args.Cancel = !mainForm.CloseWindow(ce => Callback.OnTerminating(Widget, ce));
			else
				Callback.OnTerminating(Widget, args);

			if (!args.Cancel)
			{
				if (mainForm != null)
				{
					// CloseWindow only raises the events, and the window's own close then finds it unloaded.
					(Widget.MainForm.ControlObject as NSWindow)?.Close();
					// AppDelegate would show the closed form again when a later Run activates.
					Widget.MainForm = null;
				}
				foreach (var window in Widget.Windows.ToList())
					window.Close();
				if (!Widget.Windows.Any(window => window.Loaded))
					Stop();
			}
		}

		void Stop()
		{
			_stopRequested = true;

			if (Control.ModalWindow != null)
			{
				Control.AbortModal();
				// Run clears the request if its loop finishes first, and the next loop must not stop for this.
				Control.BeginInvokeOnMainThread(() =>
				{
					if (_stopRequested)
						Stop();
				});
				return;
			}

			// NSApp.Stop sets a flag; PostEvent ensures the loop processes it immediately.
			if (Control.Running)
			{
				Control.Stop(Control);
				PostWakeEvent();
			}
		}

		void PostWakeEvent()
		{
			Control.PostEvent(
				NSEvent.OtherEvent(NSEventType.ApplicationDefined, new CGPoint(), (NSEventModifierMask)0, 0.0, 0, null, 0, 0, 0),
				atStart: true);
		}

		public bool QuitIsSupported { get { return true; } }

		public void Open(string url)
		{
			NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
		}

#if Mac64
		delegate void UncaughtExceptionHandlerDelegate(IntPtr nsexceptionPtr);

		[DllImport(Constants.FoundationLibrary)]
		static extern void NSSetUncaughtExceptionHandler(UncaughtExceptionHandlerDelegate handler);

		static void UncaughtExceptionHandler(IntPtr nsexceptionPtr)
		{
			var nsexception = Runtime.GetNSObject<NSException>(nsexceptionPtr);
			if (nsexception != null)
			{
				if (EtoEnvironment.Platform.IsMono)
				{
					// mono includes full stack already
					throw new ObjCException(nsexception);
				}
				else
				{
					// .NET 5 does not include the full stack as it goes through native code.
					// Fortunately, .NET 5 does actually use the StackTrace property for its Exception.ToString() implementation,
					// so we can feed the stack to the exception object.
					var st = new System.Diagnostics.StackTrace(1); // skip UncaughtException method
					var ststr = st.ToString();
					throw new ObjCException(nsexception, st.ToString());
				}
			}
		}
#endif

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case Application.UnhandledExceptionEvent:
					AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
					break;
				case Application.TerminatingEvent:
					// handled by app delegate
					break;
				case Application.NotificationActivatedEvent:
					if (MacVersion.IsAtLeast(10, 8))
					{
						NSUserNotificationCenter.DefaultUserNotificationCenter.DidActivateNotification += (sender, e) => DidActivateNotification(e.Notification);
						NSApplication.Notifications.ObserveDidFinishLaunching(DidFinishLaunching);
					}
					break;
				case Application.IsActiveChangedEvent:
					NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidBecomeActiveNotification, SharedApplication_ActiveChanged);
					NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidResignActiveNotification, SharedApplication_ActiveChanged);
					break;
				case Application.ThemeChangedEvent:
					AddControlObserver(new NSString("effectiveAppearance"), e =>
					{
						Callback.OnThemeChanged(Widget, EventArgs.Empty);
					});
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		void SharedApplication_ActiveChanged(NSNotification obj) => Callback.OnIsActiveChanged(Widget, EventArgs.Empty);

		void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
		{
			var unhandledExceptionArgs = new UnhandledExceptionEventArgs(e.ExceptionObject, e.IsTerminating);
			Callback.OnUnhandledException(Widget, unhandledExceptionArgs);
		}

		static readonly NSString s_NSApplicationLaunchUserNotificationKey = Dlfcn.GetStringConstant(Messaging.AppKitHandle, "NSApplicationLaunchUserNotificationKey");

		static void DidFinishLaunching(object sender, NSApplicationDidFinishLaunchingEventArgs e)
		{
			NSObject userNotificationObject;
			if (e.Notification.UserInfo.TryGetValue(s_NSApplicationLaunchUserNotificationKey, out userNotificationObject))
			{
				DidActivateNotification(userNotificationObject as NSUserNotification);
			}
		}

		internal static void DidActivateNotification(NSUserNotification notification)
		{
			NSObject idString, dataString;
			if (notification.UserInfo.TryGetValue(NotificationHandler.Info_Id, out idString))
			{
				notification.UserInfo.TryGetValue(NotificationHandler.Info_Data, out dataString);
				var app = ApplicationHandler.Instance;
				app.Callback.OnNotificationActivated(app.Widget, new NotificationEventArgs((NSString)idString, dataString as NSString));
				NSUserNotificationCenter.DefaultUserNotificationCenter.RemoveDeliveredNotification(notification);
			}
		}

		public void EnableFullScreen()
		{
			if (Control.RespondsToSelector(new Selector("setPresentationOptions:")))
			{
				AddFullScreenMenuItem = true;
			}
		}

		internal void EnsureActivated(bool showInTaskbar, bool showActivated, Icon icon)
		{
			var wasProhibited = Control.ActivationPolicy == NSApplicationActivationPolicy.Prohibited;
			if (showInTaskbar && wasProhibited)
			{
				// use the window icon as the app icon if the app is not in the dock yet
				if (icon != null)
					Control.ApplicationIconImage = icon.ToNS();
				
				// ensure the dock icon is visible (so we can get a menu, etc)
				Control.ActivationPolicy = NSApplicationActivationPolicy.Regular;
			}

			if (!showActivated)
				return;

			// taking activation from whatever app has it is only for callers that opted in, and has to be done
			// for every window since another app can take it back at any point during a run
			if (ActivateIgnoringOtherApps)
				Control.ActivateIgnoringOtherApps(true);
			// otherwise only ask for it as the app gets its first window, which is all macOS will grant anyway
			else if (showInTaskbar && wasProhibited)
				Control.Activate();
		}

		public Keys CommonModifier => Keys.Application;

		public Keys AlternateModifier => Keys.Alt;

		public bool IsActive => Control.Active;

		Theme _currentTheme = Themes.System;
		public Theme Theme
		{
			get => _currentTheme;
			set
			{
				_currentTheme = value;
				Control.Appearance = ThemeHandler.GetControl(value);
			}
		}
	}
}
