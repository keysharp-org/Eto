using Eto.GtkSharp;
using Eto.GtkSharp.Drawing;
using Eto.GtkSharp.Forms.Menu;

namespace Eto.GtkSharp.Forms
{
	public class LinuxTrayIndicatorHandler : WidgetHandler<GLib.Object, TrayIndicator, TrayIndicator.ICallback>, TrayIndicator.IHandler
	{
		const string libappindicator = "libappindicator3.so.1";
		const string libgio = "libgio-2.0.so.0";
		const string libgobject = "libgobject-2.0.so.0";
		Image image;
		string imagePath;
		ContextMenu menu;

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static IntPtr app_indicator_new(string id, string icon_name, int category);

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static void app_indicator_set_icon(IntPtr self, string icon_name);

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static void app_indicator_set_menu(IntPtr self, IntPtr menu);

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static string app_indicator_get_title(IntPtr self);

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static void app_indicator_set_title(IntPtr self, string title);

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static int app_indicator_get_status(IntPtr self);

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static void app_indicator_set_status(IntPtr self, int status);

		[DllImport(libappindicator, CallingConvention = CallingConvention.Cdecl)]
		public extern static void app_indicator_dispose(IntPtr gobject);

		// GError is deliberately not used below: this is best-effort, a NULL GError** is legal and means "don't
		// report", and it keeps this off the error-freeing path entirely. A NULL return is all it needs to know.
		[DllImport(libgio, CallingConvention = CallingConvention.Cdecl)]
		static extern IntPtr g_bus_get_sync(int bus_type, IntPtr cancellable, IntPtr error);

		// A NULL callback makes this fire-and-forget: glib sends the call with NO_REPLY_EXPECTED and returns
		// without waiting for anything. Auto-start is a separate message flag, so the bus still starts the
		// service.
		[DllImport(libgio, CallingConvention = CallingConvention.Cdecl)]
		static extern void g_dbus_connection_call(IntPtr connection, string bus_name, string object_path,
			string interface_name, string method_name, IntPtr parameters, IntPtr reply_type, int flags,
			int timeout_msec, IntPtr cancellable, IntPtr callback, IntPtr user_data);

		[DllImport(libgobject, CallingConvention = CallingConvention.Cdecl)]
		static extern void g_object_unref(IntPtr obj);

		static uint Id = 0;

		public string Title
		{
			get { return app_indicator_get_title(Control.Handle); }
			set { app_indicator_set_title(Control.Handle, value); }
		}

		public bool Visible
		{
			get { return app_indicator_get_status(Control.Handle) == 1; }
			set { app_indicator_set_status(Control.Handle, value ? 1 : 0); }
		}

		public LinuxTrayIndicatorHandler()
		{
#if NET
			DllImportResolverManager.Add((name, assembly, path) =>
			{
				if (name == libappindicator) 
				{
					IntPtr result = IntPtr.Zero;
					if (!NativeLibrary.TryLoad("libayatana-appindicator3.so.1", assembly, path, out result))
					{
						return IntPtr.Zero;
					}
					return result;
				}
				return IntPtr.Zero;
			});
#endif
			
			EnsureStatusNotifierWatcher();

			Control = GLib.Object.GetObject(app_indicator_new(Assembly.GetExecutingAssembly().FullName + Id, "", 0), true);
			app_indicator_set_menu(Control.Handle, (new Gtk.Menu()).Handle);

			Id++;
		}

		const int GBusTypeSession = 2;
		// Nothing waits on the reply, so this only bounds how long the bus holds the message before dropping it.
		const int ActivateTimeoutMs = 5000;

		// The name libappindicator looks for, and the one to ask for where a desktop makes it activatable.
		const string KdeWatcher = "org.kde.StatusNotifierWatcher";
		// Cinnamon's xapp-sn-watcher is activatable under this name instead, and merely ACQUIRES org.kde. once
		// running, so asking for org.kde. alone fails with "not provided by any .service files" and starts
		// nothing at all.
		const string XAppWatcher = "org.x.StatusNotifierWatcher";

		static bool watcherActivationTried;

		/// <summary>
		/// Asks the bus to start the desktop's StatusNotifier host, which is often not running at all: Cinnamon's
		/// xapp-sn-watcher ships D-Bus ACTIVATABLE and exits again once nothing needs it, so whether a tray icon
		/// appears comes down to whether anything has asked for one recently.
		/// <para>
		/// It matters because of what libappindicator does when no watcher answers: it falls back to a legacy
		/// XEmbed tray icon, and a Wayland session has nowhere to put one, so the icon never appears at all with
		/// nothing logged to say why. Starting the watcher is the difference between an icon and no icon, not
		/// merely between one transport and another.
		/// </para>
		/// <para>
		/// Any method call on an activatable name asks the bus to start it, so Peer.Ping is used: it takes no
		/// arguments, so there is no GVariant to build. Nothing waits, for the reply or for the watcher, because
		/// nothing needs to: libappindicator watches NameOwnerChanged for the watcher's name and registers
		/// itself whenever it turns up. A session whose watcher had gone idle therefore gets its icon a couple
		/// of seconds late, once, rather than never. Entirely best-effort: no watcher, no bus, or any failure at
		/// all changes nothing.
		/// </para>
		/// </summary>
		static void EnsureStatusNotifierWatcher()
		{
			if (watcherActivationTried)
				return;

			watcherActivationTried = true;

			var connection = IntPtr.Zero;

			try
			{
				connection = g_bus_get_sync(GBusTypeSession, IntPtr.Zero, IntPtr.Zero);

				if (connection == IntPtr.Zero)
					return;

				// Either name may be the activatable one; asking for a name nobody provides simply goes nowhere,
				// and asking for one already running is answered and discarded.
				Activate(connection, KdeWatcher);
				Activate(connection, XAppWatcher);
			}
			catch
			{
				// No libgio, no session bus, no watcher: fall through to whatever the indicator would have done.
			}
			finally
			{
				if (connection != IntPtr.Zero)
					g_object_unref(connection);
			}
		}

		static void Activate(IntPtr connection, string busName) =>
			g_dbus_connection_call(connection, busName, "/StatusNotifierWatcher",
				"org.freedesktop.DBus.Peer", "Ping", IntPtr.Zero, IntPtr.Zero, 0,
				ActivateTimeoutMs, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

		void RemoveTempImage()
		{
			if (!string.IsNullOrEmpty(imagePath))
			{
				ApplicationHandler.TempFiles.Remove(imagePath);
				File.Delete(imagePath);
				imagePath = null;
			}
		}

		public Image Image
		{
			get { return image; }
			set
			{
				RemoveTempImage();
				image = value;
				if (image == null)
				{
					app_indicator_set_icon(Control.Handle, "");
					return;
				}

				imagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
				image.ToGdk()?.Save(imagePath, "png");
				app_indicator_set_icon(Control.Handle, imagePath);
				ApplicationHandler.TempFiles.Add(imagePath);
			}
		}

		public ContextMenu Menu
		{
			get { return menu; }
			set
			{
				if (menu != null)
				{
					var handler = menu.Handler as ContextMenuHandler;
					if (handler != null)
					{
						handler.Changed -= ContextMenu_Changed;
					}
				}
				menu = value;
				if (menu == null)
					app_indicator_set_menu(Control.Handle, (new Gtk.Menu()).Handle);
				else
				{
					app_indicator_set_menu(Control.Handle, menu.ToGtk().Handle);
					var handler = menu.Handler as ContextMenuHandler;
					if (handler != null)
					{
						// need to re-set the when it has changed.. I guess.
						handler.Changed += ContextMenu_Changed;
					}
				}
			}
		}

		void ContextMenu_Changed(object sender, EventArgs e)
		{
			app_indicator_set_menu(Control.Handle, menu.ToGtk().Handle);
		}

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case TrayIndicator.ActivatedEvent:
					// Appindicator only has a context menu.
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (menu?.Handler is ContextMenuHandler handler)
				handler.Changed -= ContextMenu_Changed;
			menu = null;
			image = null;
			if (Control != null)
			{
				Visible = false;
				Control.Dispose();
				Control = null;
			}
			RemoveTempImage();
			base.Dispose(disposing);
		}
	}
}
