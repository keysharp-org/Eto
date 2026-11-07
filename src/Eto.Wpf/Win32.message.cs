namespace Eto;

partial class Win32
{
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PeekMessage(out swin.MSG message, IntPtr hwnd, WM messageMin, WM messageMax, uint remove);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool TranslateMessage(ref swin.MSG message);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr DispatchMessage(ref swin.MSG message);

	[DllImport("user32.dll")]
	public static extern void PostQuitMessage(int exitCode);
}
