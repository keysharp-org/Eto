namespace Eto.GtkSharp.Forms;

sealed class LinuxSystemTheme : IDisposable
{
	const string LibGio = "libgio-2.0.so.0";
	const string LibGLib = "libglib-2.0.so.0";
	const string LibGObject = "libgobject-2.0.so.0";
	const string PortalService = "org.freedesktop.portal.Desktop";
	const string PortalPath = "/org/freedesktop/portal/desktop";
	const string SettingsInterface = "org.freedesktop.portal.Settings";
	const string AppearanceNamespace = "org.freedesktop.appearance";
	const string ColorSchemeKey = "color-scheme";
	const string DBusService = "org.freedesktop.DBus";
	const string DBusPath = "/org/freedesktop/DBus";
	const string DBusInterface = "org.freedesktop.DBus";
	const int InitialReadTimeoutMs = 200;
	const int ReadTimeoutMs = 5000;
	const int SessionBus = 2;
	const int NoAutoStart = 1;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate void DBusSignalCallback(IntPtr connection, IntPtr senderName, IntPtr objectPath,
		IntPtr interfaceName, IntPtr signalName, IntPtr parameters, IntPtr userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	delegate void AsyncReadyCallback(IntPtr sourceObject, IntPtr result, IntPtr userData);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern void g_bus_get(int busType, IntPtr cancellable, AsyncReadyCallback callback, IntPtr userData);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr g_bus_get_finish(IntPtr result, IntPtr error);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern void g_dbus_connection_call(IntPtr connection, string busName, string objectPath,
		string interfaceName, string methodName, IntPtr parameters, IntPtr replyType, int flags,
		int timeoutMs, IntPtr cancellable, AsyncReadyCallback callback, IntPtr userData);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr g_dbus_connection_call_finish(IntPtr connection, IntPtr result, IntPtr error);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr g_bus_get_sync(int busType, IntPtr cancellable, IntPtr error);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern IntPtr g_dbus_connection_call_sync(IntPtr connection, string busName, string objectPath,
		string interfaceName, string methodName, IntPtr parameters, IntPtr replyType, int flags,
		int timeoutMs, IntPtr cancellable, IntPtr error);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern uint g_dbus_connection_signal_subscribe(IntPtr connection, string sender,
		string interfaceName, string member, string objectPath, string arg0, int flags,
		DBusSignalCallback callback, IntPtr userData, IntPtr destroyNotify);

	[DllImport(LibGio, CallingConvention = CallingConvention.Cdecl)]
	static extern void g_dbus_connection_signal_unsubscribe(IntPtr connection, uint subscriptionId);

	[DllImport(LibGLib, CallingConvention = CallingConvention.Cdecl)]
	static extern void g_variant_unref(IntPtr value);

	[DllImport(LibGObject, CallingConvention = CallingConvention.Cdecl)]
	static extern void g_object_unref(IntPtr obj);

	sealed class ReadRequest
	{
		internal ReadRequest(LinuxSystemTheme listener, long generation, bool fallback)
		{
			Listener = new WeakReference<LinuxSystemTheme>(listener);
			Generation = generation;
			Fallback = fallback;
		}

		internal WeakReference<LinuxSystemTheme> Listener { get; }
		internal long Generation { get; }
		internal bool Fallback { get; }
	}

	static readonly DBusSignalCallback s_signalCallback = OnSignal;
	static readonly AsyncReadyCallback s_busReadyCallback = OnBusReady;
	static readonly AsyncReadyCallback s_readReadyCallback = OnReadReady;
	readonly Action<uint?> _changed;
	readonly Action<Action> _asyncInvoke;
	readonly object _sync = new();
	SynchronizationContext _context;
	int _contextThreadId;
	GLib.Cancellable _cancellable;
	IntPtr _connection;
	uint _settingSubscriptionId;
	uint _ownerSubscriptionId;
	GCHandle _settingCallbackHandle;
	GCHandle _ownerCallbackHandle;
	long _changeGeneration;
	int _started;
	int _disposed;

	internal LinuxSystemTheme(Action<uint?> changed, Action<Action> asyncInvoke)
	{
		_changed = changed;
		_asyncInvoke = asyncInvoke;
	}

	// Windows created before the asynchronous read reports would otherwise open in the GTK default theme.
	// This blocks startup, so it never waits for the portal to be activated, and it uses Read because every
	// portal version implements it, which saves the second timeout a ReadOne fallback would cost.
	internal static uint? ReadColorScheme()
	{
		var connection = IntPtr.Zero;
		var reply = IntPtr.Zero;
		try
		{
			connection = g_bus_get_sync(SessionBus, IntPtr.Zero, IntPtr.Zero);
			if (connection == IntPtr.Zero)
				return null;
			using var namespaceValue = new GLib.Variant(AppearanceNamespace);
			using var keyValue = new GLib.Variant(ColorSchemeKey);
			using var parameters = GLib.Variant.NewTuple(new[] { namespaceValue, keyValue });
			reply = g_dbus_connection_call_sync(connection, PortalService, PortalPath, SettingsInterface,
				"Read", parameters.Handle, IntPtr.Zero, NoAutoStart, InitialReadTimeoutMs, IntPtr.Zero, IntPtr.Zero);
			return GetVariantType(reply) == "(v)" ? GetUInt32Child(reply, 0) : null;
		}
		catch (Exception ex)
		{
			Trace(ex);
			return null;
		}
		finally
		{
			if (reply != IntPtr.Zero)
				g_variant_unref(reply);
			if (connection != IntPtr.Zero)
				g_object_unref(connection);
		}
	}

	internal void Start()
	{
		lock (_sync)
		{
			if (_started != 0 || IsDisposed)
				return;

			_started = 1;
			_context = SynchronizationContext.Current;
			_contextThreadId = Thread.CurrentThread.ManagedThreadId;
			_cancellable = new GLib.Cancellable();
			var callbackHandle = CreateWeakHandle(this);
			try
			{
				g_bus_get(SessionBus, _cancellable.Handle, s_busReadyCallback,
					GCHandle.ToIntPtr(callbackHandle));
			}
			catch (Exception ex)
			{
				callbackHandle.Free();
				_cancellable.Dispose();
				_cancellable = null;
				_started = 0;
				Trace(ex);
			}
		}
	}

	static void OnBusReady(IntPtr sourceObject, IntPtr result, IntPtr userData)
	{
		var connection = IntPtr.Zero;
		try
		{
			connection = g_bus_get_finish(result, IntPtr.Zero);
			if (connection != IntPtr.Zero && GetListener(userData)?.CompleteStart(connection) == true)
				connection = IntPtr.Zero;
		}
		catch (Exception ex)
		{
			Trace(ex);
		}
		finally
		{
			if (connection != IntPtr.Zero)
				g_object_unref(connection);
			FreeHandle(userData);
		}
	}

	bool CompleteStart(IntPtr connection)
	{
		lock (_sync)
		{
			if (IsDisposed)
				return false;

			_connection = connection;
			try
			{
				_settingSubscriptionId = Subscribe(PortalService, SettingsInterface, "SettingChanged",
					PortalPath, AppearanceNamespace, out _settingCallbackHandle);
				_ownerSubscriptionId = Subscribe(DBusService, DBusInterface, "NameOwnerChanged",
					DBusPath, PortalService, out _ownerCallbackHandle);
			}
			catch (Exception ex)
			{
				Trace(ex);
				ReleaseConnection(freeCallbackHandles: true);
				return true;
			}
		}

		ReadCurrentValue();
		return true;
	}

	uint Subscribe(string sender, string interfaceName, string member, string objectPath, string arg0,
		out GCHandle callbackHandle)
	{
		callbackHandle = CreateWeakHandle(this);
		try
		{
			return g_dbus_connection_signal_subscribe(_connection, sender, interfaceName, member,
				objectPath, arg0, 0, s_signalCallback, GCHandle.ToIntPtr(callbackHandle), IntPtr.Zero);
		}
		catch
		{
			callbackHandle.Free();
			throw;
		}
	}

	void ReadCurrentValue()
	{
		var generation = Interlocked.Increment(ref _changeGeneration);
		ReadColorScheme("ReadOne", generation, fallback: false);
	}

	void ReadColorScheme(string method, long generation, bool fallback)
	{
		var failed = false;
		lock (_sync)
		{
			if (IsDisposed || _connection == IntPtr.Zero
				|| generation != Interlocked.Read(ref _changeGeneration))
				return;

			var callbackHandle = GCHandle.Alloc(new ReadRequest(this, generation, fallback));
			try
			{
				using var namespaceValue = new GLib.Variant(AppearanceNamespace);
				using var keyValue = new GLib.Variant(ColorSchemeKey);
				using var parameters = GLib.Variant.NewTuple(new[] { namespaceValue, keyValue });
				g_dbus_connection_call(_connection, PortalService, PortalPath, SettingsInterface,
					method, parameters.Handle, IntPtr.Zero, 0, ReadTimeoutMs, _cancellable.Handle,
					s_readReadyCallback, GCHandle.ToIntPtr(callbackHandle));
			}
			catch (Exception ex)
			{
				callbackHandle.Free();
				failed = true;
				Trace(ex);
			}
		}

		if (failed)
		{
			if (fallback)
				QueueReadResult(generation, null);
			else
				ReadColorScheme("Read", generation, fallback: true);
		}
	}

	static void OnReadReady(IntPtr sourceObject, IntPtr result, IntPtr userData)
	{
		var request = GetTarget<ReadRequest>(userData);
		var reply = IntPtr.Zero;
		uint? colorScheme = null;
		try
		{
			reply = g_dbus_connection_call_finish(sourceObject, result, IntPtr.Zero);
			if (GetVariantType(reply) == "(v)")
				colorScheme = GetUInt32Child(reply, 0);
		}
		catch (Exception ex)
		{
			Trace(ex);
		}
		finally
		{
			if (reply != IntPtr.Zero)
				g_variant_unref(reply);
			FreeHandle(userData);
		}

		try
		{
			if (request?.Listener.TryGetTarget(out var listener) != true)
				return;
			if (reply == IntPtr.Zero && !request.Fallback)
				listener.ReadColorScheme("Read", request.Generation, fallback: true);
			else
				listener.QueueReadResult(request.Generation, colorScheme);
		}
		catch (Exception ex)
		{
			Trace(ex);
		}
	}

	static void OnSignal(IntPtr connection, IntPtr senderName, IntPtr objectPath,
		IntPtr interfaceName, IntPtr signalName, IntPtr parameters, IntPtr userData)
	{
		try
		{
			var listener = GetListener(userData);
			switch (NativeMethods.GetString(signalName))
			{
				case "SettingChanged":
					listener?.HandleSettingChanged(parameters);
					break;
				case "NameOwnerChanged":
					listener?.HandleNameOwnerChanged(parameters);
					break;
			}
		}
		catch (Exception ex)
		{
			Trace(ex);
		}
	}

	void HandleSettingChanged(IntPtr parameters)
	{
		if (IsDisposed || GetVariantType(parameters) != "(ssv)"
			|| GetStringChild(parameters, 1) != ColorSchemeKey
			|| !TryGetUInt32Child(parameters, 2, out var colorScheme))
			return;

		var generation = Interlocked.Increment(ref _changeGeneration);
		QueueReadResult(generation, colorScheme);
	}

	void HandleNameOwnerChanged(IntPtr parameters)
	{
		if (IsDisposed || GetVariantType(parameters) != "(sss)")
			return;

		if (string.IsNullOrEmpty(GetStringChild(parameters, 2)))
		{
			var generation = Interlocked.Increment(ref _changeGeneration);
			QueueReadResult(generation, null);
		}
		else
			ReadCurrentValue();
	}

	void QueueReadResult(long generation, uint? colorScheme)
	{
		_asyncInvoke(() =>
		{
			if (!IsDisposed && Interlocked.Read(ref _changeGeneration) == generation)
				_changed(colorScheme);
		});
	}

	static GCHandle CreateWeakHandle(LinuxSystemTheme listener) =>
		GCHandle.Alloc(new WeakReference<LinuxSystemTheme>(listener));

	static LinuxSystemTheme GetListener(IntPtr userData) =>
		GetTarget<WeakReference<LinuxSystemTheme>>(userData)?.TryGetTarget(out var listener) == true
			? listener : null;

	static T GetTarget<T>(IntPtr userData) where T : class
	{
		try
		{
			return GCHandle.FromIntPtr(userData).Target as T;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	static void FreeHandle(IntPtr userData)
	{
		try
		{
			GCHandle.FromIntPtr(userData).Free();
		}
		catch (InvalidOperationException)
		{
		}
	}

	static string GetVariantType(IntPtr value)
	{
		if (value == IntPtr.Zero)
			return null;
		using var variant = new GLib.Variant(value);
		return variant.Type.ToString();
	}

	static string GetStringChild(IntPtr value, int index)
	{
		if (value == IntPtr.Zero)
			return null;
		using var variant = new GLib.Variant(value);
		var children = variant.ToArray();
		try
		{
			return index < children.Length && children[index].Type.ToString() == "s"
				? (string)children[index] : null;
		}
		finally
		{
			Dispose(children);
		}
	}

	static uint? GetUInt32Child(IntPtr value, int index) =>
		TryGetUInt32Child(value, index, out var result) ? result : null;

	static bool TryGetUInt32Child(IntPtr value, int index, out uint result)
	{
		using var variant = new GLib.Variant(value);
		var children = variant.ToArray();
		try
		{
			var parsed = index < children.Length ? GetUInt32(children[index]) : null;
			result = parsed.GetValueOrDefault();
			return parsed != null;
		}
		finally
		{
			Dispose(children);
		}
	}

	static uint? GetUInt32(GLib.Variant value)
	{
		if (!value.Type.IsVariant)
			return value.Type.ToString() == "u" ? (uint)value : null;

		var children = value.ToArray();
		try
		{
			return children.Length == 1 ? GetUInt32(children[0]) : null;
		}
		finally
		{
			Dispose(children);
		}
	}

	static void Dispose(IEnumerable<GLib.Variant> variants)
	{
		foreach (var variant in variants)
			variant.Dispose();
	}

	bool IsDisposed => Volatile.Read(ref _disposed) != 0;

	void ReleaseConnection(bool freeCallbackHandles)
	{
		if (_connection == IntPtr.Zero)
			return;
		if (_settingSubscriptionId != 0)
			g_dbus_connection_signal_unsubscribe(_connection, _settingSubscriptionId);
		if (_ownerSubscriptionId != 0)
			g_dbus_connection_signal_unsubscribe(_connection, _ownerSubscriptionId);
		if (freeCallbackHandles)
		{
			FreeHandle(ref _settingCallbackHandle);
			FreeHandle(ref _ownerCallbackHandle);
		}
		else
		{
			// Cross-context callbacks may still be queued, so their weak handles must remain valid.
			_settingCallbackHandle = default;
			_ownerCallbackHandle = default;
		}
		g_object_unref(_connection);
		_connection = IntPtr.Zero;
		_settingSubscriptionId = 0;
		_ownerSubscriptionId = 0;
	}

	static void Trace(Exception exception) =>
		Debug.WriteLine($"Could not monitor the Linux system theme: {exception}");

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		if (_context != null && Thread.CurrentThread.ManagedThreadId != _contextThreadId)
		{
			var completed = new TaskCompletionSource<bool>();
			try
			{
				_context.Post(state =>
				{
					var request = (Tuple<LinuxSystemTheme, TaskCompletionSource<bool>>)state;
					try
					{
						request.Item1.DisposeNative(freeCallbackHandles: true);
					}
					catch (Exception ex)
					{
						Trace(ex);
					}
					finally
					{
						request.Item2.TrySetResult(true);
					}
				}, Tuple.Create(this, completed));
			}
			catch (Exception ex)
			{
				Trace(ex);
				DisposeNative(freeCallbackHandles: false);
				return;
			}
			if (completed.Task.Wait(1000))
				return;
			lock (_sync)
				_cancellable?.Cancel();
			return;
		}

		DisposeNative(freeCallbackHandles: true);
	}

	void DisposeNative(bool freeCallbackHandles)
	{
		lock (_sync)
		{
			_cancellable?.Cancel();
			ReleaseConnection(freeCallbackHandles);
			_cancellable?.Dispose();
			_cancellable = null;
		}
	}

	static void FreeHandle(ref GCHandle handle)
	{
		if (handle.IsAllocated)
			FreeHandle(GCHandle.ToIntPtr(handle));
		handle = default;
	}
}
