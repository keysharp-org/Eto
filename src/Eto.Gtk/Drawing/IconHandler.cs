namespace Eto.GtkSharp.Drawing
{

	public class IconHandler : ImageHandler<Gtk.IconSet, Icon>, Icon.IHandler, IGtkPixbuf
	{
		readonly Dictionary<Size, Gdk.Pixbuf> sizes = new Dictionary<Size, Gdk.Pixbuf>();
		Lazy<List<IconFrame>> _frames;

		public Gdk.Pixbuf Pixbuf { get; set; }

		#region IIcon Members

		public void Create(Stream stream)
		{
			using (var ms = new MemoryStream())
			{
				stream.CopyTo(ms);
				ms.Position = 0;
				Pixbuf = new Gdk.Pixbuf(ms);
				Control = new Gtk.IconSet(Pixbuf);
				var bytes = ms.ToArray();
				// Window and tray icons use only the primary image, so the frames are split on first request.
				// The directory is still checked here so a malformed file fails in the constructor.
				ValidateDirectory(bytes);
				_frames = new Lazy<List<IconFrame>>(() => GetFrames(bytes));
			}
		}

		public void Create(string fileName)
		{
			using (var fs = File.OpenRead(fileName))
				Create(fs);
		}

		const int sICONDIR = 6;            // sizeof(ICONDIR) 
		const int sICONDIRENTRY = 16;      // sizeof(ICONDIRENTRY)
		static readonly byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

		public Gdk.Pixbuf[] SplitIcon(MemoryStream input)
		{
			return SplitIcon(input.ToArray());
		}

		Gdk.Pixbuf[] SplitIcon(byte[] srcBuf)
		{
			// Get multiple .ico file image.
			int count = ValidateDirectory(srcBuf);
			var splitIcons = new List<Gdk.Pixbuf>(count);

			for (int i = 0; i < count; i++) {
				int entryOffset = sICONDIR + sICONDIRENTRY * i;
				int imgSize = BitConverter.ToInt32(srcBuf, entryOffset + 8); // ICONDIRENTRY.dwBytesInRes
				int imgOffset = BitConverter.ToInt32(srcBuf, entryOffset + 12); // ICONDIRENTRY.dwImageOffset

				// PNG-compressed ICO entries are already complete images. Wrapping one in another ICO
				// container makes GDK report "Compressed icons are not supported".
				if (HasPngSignature(srcBuf, imgOffset, imgSize))
				{
					using (var imageStream = new MemoryStream(srcBuf, imgOffset, imgSize, false))
						splitIcons.Add(new Gdk.Pixbuf(imageStream));
					continue;
				}

				using (var destStream = new MemoryStream ())
				using (var writer = new BinaryWriter (destStream)) {
					// Copy ICONDIR and ICONDIRENTRY.
					writer.Write (srcBuf, 0, sICONDIR - 2);
					writer.Write ((short)1);    // ICONDIR.idCount == 1;

					writer.Write (srcBuf, entryOffset, sICONDIRENTRY - 4); // write out icon info (minus old offset)
					writer.Write (sICONDIR + sICONDIRENTRY);    // write offset of icon data

					// Copy picture and mask data.
					writer.Write (srcBuf, imgOffset, imgSize);
					writer.Flush ();

					// Create new icon.
					destStream.Seek (0, SeekOrigin.Begin);
					splitIcons.Add (new Gdk.Pixbuf(destStream));
				}
			}

			return splitIcons.ToArray ();
		}

		static bool HasPngSignature(byte[] buffer, int offset, int length)
		{
			if (length < pngSignature.Length)
				return false;

			for (int i = 0; i < pngSignature.Length; i++)
				if (buffer[offset + i] != pngSignature[i])
					return false;

			return true;
		}

		// Checks the ICONDIR and every ICONDIRENTRY without decoding an image; returns the entry count.
		static int ValidateDirectory(byte[] srcBuf)
		{
			if (srcBuf.Length < sICONDIR)
				throw new InvalidDataException("Icon not a valid format");

			int count = BitConverter.ToInt16(srcBuf, 4); // ICONDIR.idCount
			if (count <= 0 || count > (srcBuf.Length - sICONDIR) / sICONDIRENTRY)
				throw new InvalidDataException("Icon not a valid format");

			for (int i = 0; i < count; i++)
			{
				int entryOffset = sICONDIR + sICONDIRENTRY * i;
				int imgSize = BitConverter.ToInt32(srcBuf, entryOffset + 8); // ICONDIRENTRY.dwBytesInRes
				int imgOffset = BitConverter.ToInt32(srcBuf, entryOffset + 12); // ICONDIRENTRY.dwImageOffset
				if (imgOffset < 0 || imgSize < 0 || imgOffset > srcBuf.Length - imgSize)
					throw new InvalidDataException("Icon not a valid format");
			}
			return count;
		}

		List<IconFrame> GetFrames(byte[] bytes)
		{
			var pixbufs = SplitIcon(bytes);
			var frameList = new List<IconFrame>(pixbufs.Length);
			foreach (var pb in pixbufs)
			{
				frameList.Add(IconFrame.FromControlObject(1, new Bitmap(new BitmapHandler(pb))));
			}
			return frameList;
		}

		#endregion

		public override Size Size
		{
			get
			{
				return new Size(Pixbuf.Width, Pixbuf.Height);
			}
		}

		public override void SetImage(Gtk.Image imageView, Gtk.IconSize? iconSize)
		{
			if (iconSize != null)
				imageView.SetFromIconSet(Control, iconSize.Value);
			else
				imageView.Pixbuf = Pixbuf;
		}

		public override void DrawImage(GraphicsHandler graphics, RectangleF source, RectangleF destination)
		{
			var context = graphics.Control;
			context.Save();
			context.Rectangle(destination.ToCairo());
			double scalex = 1;
			double scaley = 1;
			var pb = Widget.GetFrame(1, Size.Ceiling(destination.Size));
			if (Math.Abs(source.Width - destination.Width) > 0.5f || Math.Abs(source.Height - destination.Height) > 0.5f)
			{
				scalex = (double)destination.Width / (double)source.Width;
				scaley = (double)destination.Height / (double)source.Height;
				scalex *= (double)Size.Width / (double)pb.PixelSize.Width;
				scaley *= (double)Size.Height / (double)pb.PixelSize.Height;
				context.Scale(scalex, scaley);
			}
			Gdk.CairoHelper.SetSourcePixbuf(context, pb.Bitmap.ToGdk(), (destination.Left / scalex) - source.Left, (destination.Top / scaley) - source.Top);
			var pattern = (Cairo.SurfacePattern)context.GetSource();
			pattern.Filter = graphics.ImageInterpolation.ToCairo();
			context.Fill();
			context.Restore();
			pattern.Dispose();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_frames = null;
			base.Dispose(disposing);
			if (disposing)
			{
				foreach (var scaled in sizes.Values)
					scaled.Dispose();
				sizes.Clear();
				if (Pixbuf != null)
				{
					Pixbuf.Dispose();
					Pixbuf = null;
				}
			}
		}

		public Gdk.Pixbuf GetPixbuf(Size maxSize, Gdk.InterpType interpolation = Gdk.InterpType.Bilinear, bool shrink = false)
		{
			Gdk.Pixbuf pixbuf = Pixbuf;
			if (pixbuf.Width > maxSize.Width && pixbuf.Height > maxSize.Height
				|| (shrink && (pixbuf.Width < maxSize.Width || pixbuf.Height < maxSize.Height)))
			{
				if (!sizes.TryGetValue(maxSize, out pixbuf))
				{
					pixbuf = Pixbuf.ScaleSimple(maxSize.Width, maxSize.Height, interpolation);
					sizes[maxSize] = pixbuf;
				}
			}
			
			return pixbuf;
		}

		public void Create(IEnumerable<IconFrame> frames)
		{
			var frameList = frames.ToList();
			_frames = new Lazy<List<IconFrame>>(() => frameList);
			Control = new Gtk.IconSet();
			foreach (var frame in frameList)
			{
				Control.AddSource(new Gtk.IconSource { Pixbuf = frame.Bitmap.ToGdk() });
			}
			Pixbuf = frameList.FirstOrDefault(r => r.Scale == 1)?.Bitmap.ToGdk();
			if (Pixbuf == null)
			{
				var frame = frameList.OrderByDescending(r => r.Scale).FirstOrDefault();
				Pixbuf = frame?.Bitmap.ToGdk().ScaleSimple(frame.Size.Width, frame.Size.Height, Gdk.InterpType.Bilinear);
			}
		}

		public IEnumerable<IconFrame> Frames => _frames?.Value;
	}
}
