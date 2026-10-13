namespace Eto.Mac.Drawing
{
	public class IconHandler : ImageHandler<NSImage, Icon>, Icon.IHandler
	{
		Lazy<List<IconFrame>> _frames;

		public IconHandler()
		{
		}

		public IconHandler(NSImage image)
		{
			SetImage(image);
		}

		void SetImage(NSImage image)
		{
			Control = image;
			_frames = new Lazy<List<IconFrame>>(() => GetFrames(image));
		}

		public void Create(Stream stream)
		{
			var data = NSData.FromStream(stream);
			SetImage(new NSImage(data));
		}

		public void Create(string fileName)
		{
			if (!File.Exists(fileName))
				throw new FileNotFoundException(string.Format(CultureInfo.CurrentCulture, "Icon not found"), fileName);
			SetImage(new NSImage(fileName));
		}

		List<IconFrame> GetFrames(NSImage image)
		{
			var representations = image.Representations();
			var frameList = new List<IconFrame>(representations.Length);
			foreach (var rep in representations)
			{
				var img = new NSImage();
				img.AddRepresentation(rep);
				frameList.Add(IconFrame.FromControlObject(1, new Bitmap(new BitmapHandler(img))));
			}
			return frameList;
		}

		public void Create(IEnumerable<IconFrame> frames)
		{
			var frameList = frames.ToList();
			_frames = new Lazy<List<IconFrame>>(() => frameList);
			var curScale = Screen.PrimaryScreen.LogicalPixelSize;
			var item = frameList.FirstOrDefault(r => Math.Abs(r.Scale - curScale) < 0.0001) ?? frameList.First();

			var size = Size.Ceiling((SizeF)item.PixelSize / (float)item.Scale).ToNS();

			Control = new NSImage { Size = size };
			foreach (var frame in frameList)
			{
				var rep = (NSImageRep)frame.Bitmap.ToNS().Representations().First().Copy();

				rep.Size = (new SizeF(rep.PixelsWide, rep.PixelsHigh) / (float)frame.Scale).ToNS();
				if (rep is IconFrameHandler.LazyImageRep mns)
				{
					mns.Size = size;//(size.ToEto() * (float)r.Scale).ToNS();
					var pixelSize = Size.Ceiling(size.ToEto() * (float)frame.Scale);
					mns.PixelsHigh = pixelSize.Height;
					mns.PixelsWide = pixelSize.Width;
				}

				Control.AddRepresentation(rep);
			}
		}

		public override Size Size
		{
			get { return Control.Size.ToEtoSize(); }
		}

		public override NSImage GetImage()
		{
			return Control;
		}

		public override void DrawImage(GraphicsHandler graphics, RectangleF source, RectangleF destination)
		{
			if (Control.Template)
			{
				DrawTemplateImage(graphics, source, destination);
				return;
			}

			var sourceRect = new CGRect(source.X, (float)Control.Size.Height - source.Y - source.Height, source.Width, source.Height);
			var destRect = destination.ToNS();
			Control.Draw(destRect, sourceRect, NSCompositingOperation.SourceOver, 1, true, null);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_frames = null;
			base.Dispose(disposing);
		}

		public IEnumerable<IconFrame> Frames => _frames?.Value;
	}
}
