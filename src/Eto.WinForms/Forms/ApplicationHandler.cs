using Microsoft.WindowsAPICodePack.Taskbar;
namespace Eto.WinForms.Forms
{
	public class ApplicationHandler : WidgetHandler<object, Application, Application.ICallback>, Application.IHandler
	{
		string badgeLabel;
		bool attached;
		bool _optionsSet;
		bool _stopRequested;
		readonly Thread mainThread;
		SynchronizationContext context;
		swf.ApplicationContext applicationContext = new swf.ApplicationContext();
		public static bool EnableScrollingUnderMouse = true;
		public static bool BubbleMouseEvents = true;
		public static bool BubbleKeyEvents = true;

		public static ApplicationHandler Instance => Eto.Forms.Application.Instance?.Handler as ApplicationHandler;

		bool _isActive;
		public bool IsActive
		{
			get => _isActive;
			set
			{
				if (_isActive != value)
				{
					_isActive = value;
					Callback.OnIsActiveChanged(Widget, EventArgs.Empty);
				}
			}
		}

		public ApplicationHandler()
		{
			mainThread = Thread.CurrentThread;
			swf.Application.EnableVisualStyles();
			try
			{
				swf.Application.SetCompatibleTextRenderingDefault(false);
			}
			catch
			{
				// ignoring error, as it requires to be called before any IWin32Window is created
				// When integrating with other native apps, this may not be possible.
			}
		}

		void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
		{
			var unhandledExceptionArgs = new UnhandledExceptionEventArgs(e.ExceptionObject, e.IsTerminating);
			Callback.OnUnhandledException(Widget, unhandledExceptionArgs);
		}

		void OnUnhandledThreadException(object sender, ThreadExceptionEventArgs e)
		{
			var unhandledExceptionArgs = new UnhandledExceptionEventArgs(e.Exception, true);
			Callback.OnUnhandledException(Widget, unhandledExceptionArgs);
		}

		public void RunIteration()
		{
			swf.Application.DoEvents();
		}

		public void Restart()
		{
			swf.Application.Restart();
		}

		public string BadgeLabel
		{
			get { return badgeLabel; }
			set
			{
				badgeLabel = value;
				if (TaskbarManager.IsPlatformSupported)
				{
					if (!string.IsNullOrEmpty(badgeLabel))
					{
						var bmp = new sd.Bitmap(14, 14, sd.Imaging.PixelFormat.Format32bppArgb);
						using (var graphics = sd.Graphics.FromImage(bmp))
						{
							DrawBadgeLabel(bmp, graphics);
						}
						var icon = sd.Icon.FromHandle(bmp.GetHicon());

						TaskbarManager.Instance.SetOverlayIcon(icon, badgeLabel);
					}
					else
						TaskbarManager.Instance.SetOverlayIcon(null, null);
				}
			}
		}
		
		protected virtual void DrawBadgeLabel(sd.Bitmap bmp, sd.Graphics graphics)
		{
			var font = new sd.Font(sd.FontFamily.GenericSansSerif, 9, sd.FontStyle.Bold, sd.GraphicsUnit.Pixel);

			var size = graphics.MeasureString(badgeLabel, font, bmp.Size, sd.StringFormat.GenericTypographic);
			graphics.SmoothingMode = sd.Drawing2D.SmoothingMode.AntiAlias;
			graphics.FillEllipse(sd.Brushes.Red, new sd.Rectangle(0, 0, bmp.Width - 1, bmp.Height - 1));
			// graphics.DrawEllipse(new sd.Pen(sd.Brushes.White, 2), new sd.Rectangle(0, 0, 15, 15));
			var pt = new sd.PointF((bmp.Width - size.Width - 0.5F) / 2, (bmp.Height - size.Height - 1) / 2);
			graphics.DrawString(badgeLabel, font, sd.Brushes.White, pt, sd.StringFormat.GenericTypographic);
		}

		public void Run()
		{
			if (!attached)
			{
				if (!_optionsSet)
				{
					SetOptions();
					_optionsSet = true;
				}

				_stopRequested = false;
				Callback.OnInitialized(Widget, EventArgs.Empty);
				// ExitThread only reaches a loop that is already running, and a closed main form can't start one.
				if (!_stopRequested && applicationContext.MainForm?.IsDisposed != true)
				{
					swf.Application.Run(applicationContext);
					// The message filters belong to the thread context that ended with the loop.
					_optionsSet = false;
					// WM_QUIT can be posted more than once, and a leftover would end the next Run at once.
					while (Win32.PeekMessage(out _, IntPtr.Zero, Win32.WM.QUIT, Win32.WM.QUIT, Win32.PM_REMOVE))
					{
					}
				}
				// A finished loop leaves its ThreadExit handler on this one, and a skipped loop its closed main form.
				applicationContext = new swf.ApplicationContext();
			}
			else
			{
				Callback.OnInitialized(Widget, EventArgs.Empty);
			}
		}

		static readonly object SuppressKeyPressKey = new object();

		void CreateContext()
		{
			// creates sync context
			using (var ctl = new swf.Control())
			{
			}
			context = SynchronizationContext.Current;
		}

		void SetOptions()
		{
			CreateContext();

			_isActive = Win32.ApplicationIsActivated();

			if (EtoEnvironment.Platform.IsWindows && EnableScrollingUnderMouse)
				swf.Application.AddMessageFilter(new ScrollMessageFilter());

			if (BubbleMouseEvents)
			{
				var bubble = new BubbleEventFilter();
				bubble.AddBubbleMouseEvents((c, cb, e) => cb.OnMouseWheel(c, e), null, Win32.WM.MOUSEWHEEL, Win32.WM.MOUSEHWHEEL);
				bubble.AddBubbleMouseEvent((c, cb, e) => cb.OnMouseMove(c, e), null, Win32.WM.MOUSEMOVE);
				bubble.AddBubbleMouseEvents((c, cb, e) => 
				{
					WindowsControl.SkipMouseCapture = false;
					cb.OnMouseDown(c, e);
					if (e.Handled && c.Handler is IWindowsControl handler && handler.ShouldCaptureMouse && !WindowsControl.SkipMouseCapture)
					{
						handler.ContainerControl.Capture = true;
						handler.MouseCaptured = true;
					}
				}, true, Win32.WM.LBUTTONDOWN, Win32.WM.RBUTTONDOWN, Win32.WM.MBUTTONDOWN);
				bubble.AddBubbleMouseEvents((c, cb, e) =>
				{
					cb.OnMouseDoubleClick(c, e);
					if (!e.Handled)
						cb.OnMouseDown(c, e);
				}, null, Win32.WM.LBUTTONDBLCLK, Win32.WM.RBUTTONDBLCLK, Win32.WM.MBUTTONDBLCLK);
				void OnMouseUpHandler(Control c, Control.ICallback cb, MouseEventArgs e)
				{
					var handler = c.Handler as IWindowsControl;
					if (handler != null && handler.MouseCaptured)
					{
						handler.MouseCaptured = false;
						handler.ContainerControl.Capture = false;
					}
					cb.OnMouseUp(c, e);

					if (handler != null && e.Handled && handler.ContainerControl.Capture)
						handler.ContainerControl.Capture = false;
				}
				
				bubble.AddBubbleMouseEvent(OnMouseUpHandler, false, Win32.WM.LBUTTONUP, b => MouseButtons.Primary);
				bubble.AddBubbleMouseEvent(OnMouseUpHandler, false, Win32.WM.RBUTTONUP, b => MouseButtons.Alternate);
				bubble.AddBubbleMouseEvent(OnMouseUpHandler, false, Win32.WM.MBUTTONUP, b => MouseButtons.Middle);
				swf.Application.AddMessageFilter(bubble);
			}
			if (BubbleKeyEvents)
			{
				var bubble = new BubbleEventFilter();

				Action<Control, Control.ICallback, KeyEventArgs> keyDown = (c, cb, e) =>
				{
					cb.OnKeyDown(c, e);
					c.Properties[SuppressKeyPressKey] = e.Handled;
				};

				Action<Control, Control.ICallback, KeyEventArgs> keyPress = (c, cb, e) =>
				{
					if (!c.Properties.Get<bool>(SuppressKeyPressKey))
					{
						if (!char.IsControl(e.KeyChar))
						{
							var tia = new TextInputEventArgs(e.KeyChar.ToString());
							cb.OnTextInput(c, tia);
							e.Handled = tia.Cancel;
						}

						if (!e.Handled)
							cb.OnKeyDown(c, e);
					}
					else
					{
						e.Handled = true;
						c.Properties.Remove(SuppressKeyPressKey);
					}
				};

				bubble.AddBubbleKeyEvent(keyDown, Win32.WM.KEYDOWN, KeyEventType.KeyDown);
				bubble.AddBubbleKeyEvent(keyDown, Win32.WM.SYSKEYDOWN, KeyEventType.KeyDown);
				bubble.AddBubbleKeyCharEvent(keyPress, Win32.WM.CHAR, KeyEventType.KeyDown);
				bubble.AddBubbleKeyCharEvent(keyPress, Win32.WM.SYSCHAR, KeyEventType.KeyDown);
				bubble.AddBubbleKeyEvent((c, cb, e) => cb.OnKeyUp(c, e), Win32.WM.KEYUP, KeyEventType.KeyUp);
				bubble.AddBubbleKeyEvent((c, cb, e) => cb.OnKeyUp(c, e), Win32.WM.SYSKEYUP, KeyEventType.KeyUp);
				swf.Application.AddMessageFilter(bubble);
			}
		}

		public void Attach(object context)
		{
			attached = true;
			SetOptions();
		}

		public void OnMainFormChanged()
		{
			applicationContext.MainForm = (swf.Form)Widget.MainForm.ControlObject;
		}

		public void Quit()
		{
			if (Thread.CurrentThread != mainThread)
			{
				Invoke(Quit);
				return;
			}
			// The main form goes last, as closing it ends the loop whatever the other forms decide.
			foreach (var form in swf.Application.OpenForms.Cast<swf.Form>().OrderBy(form => form == applicationContext.MainForm).ToList())
			{
				form.Close();
				// A modal form that accepted the close stays open until its own loop ends.
				if (swf.Application.OpenForms.Cast<swf.Form>().Contains(form) && !(form.Modal && form.DialogResult != swf.DialogResult.None))
					return;
			}
			_stopRequested = true;
			applicationContext?.ExitThread();
		}

		public bool QuitIsSupported { get { return true; } }

		public void Open(string url)
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case Application.TerminatingEvent:
					// handled by WindowHandler
					break;
				case Application.UnhandledExceptionEvent:
					swf.Application.ThreadException += OnUnhandledThreadException;
					AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
					break;
				case Application.NotificationActivatedEvent:
					// handled by NotificationHandler
					break;
				case Application.ThemeChangedEvent:
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		public void Invoke(Action action)
		{
			if (Thread.CurrentThread == mainThread)
				action();
			else if (context != null)
				context.Send(state => action(), null);
		}

		public void AsyncInvoke(Action action)
		{
			// Not created in the constructor, as its window would block SetCompatibleTextRenderingDefault.
			if (context == null && Thread.CurrentThread == mainThread)
				CreateContext();
			if (context != null)
				context.Post(state => action(), null);
		}

		public Keys CommonModifier
		{
			get
			{
				return Keys.Control;
			}
		}

		public Keys AlternateModifier
		{
			get
			{
				return Keys.Alt;
			}
		}

#if NET9_0_OR_GREATER
		Theme _theme;
		public Theme Theme
		{
			get => _theme ??= new Theme(new ThemeHandler(swf.Application.ColorMode));
			set
			{
				if (value != null)
				{
					var handler = value.Handler as ThemeHandler;
					if (handler != null)
						handler.SetTheme();
				}
				_theme = value;
				
				// invalidate all windows to update theme
				// doesn't seem to work for all controls, especially scroll bars.
				foreach (var window in swf.Application.OpenForms)
				{
					if (window is swf.Form form)
						form.Invalidate(true);
				}
				Callback.OnThemeChanged(Widget, EventArgs.Empty);
			}
		}
#else
		public Theme Theme { get; set; }
#endif

	}
}
