using System;
using System.Collections.Generic;

using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

using helpers;
using helpers.extensions;

namespace BTL.Play
{
    public class Text : EffectVideo
    {
        public enum BrushStyle 
        {
            Solid,
            GradientV,
            GradientH
        }
		private string _sText;
		private Font _cFont;
		private float _nBorderWidth;
        private PixelsMap _cPixels;
        private object _cSyncRoot;
		private bool _bIsGoingStop;

        public string sText
        {
            get
            {
                return _sText;
            }
            set
            {
                ChangeText(value);
            }
        }
		public Color stColor { get; set; }
		public Color stColorBorder { get; set; }
		public Color stColorBorderBackground { get; set; }
		public Color stColorGradient { get; set; }
		public BrushStyle eBrushStyle { get; set; }
        public override ulong nDuration
        {
            get
            {
                return base.nDuration;
            }
            set
            {
                if (0 < value)
                    base.nDuration = value;
            }
        }

        private Text()
            :base(EffectType.Text)
        {
            _cFont = null;
        }
		public Text(string sText, Font cFont, float nBorderWidth)
            :this()
        {
			try
			{
                (new Logger()).WriteDebug3("[hc:" + GetHashCode() + "][type:" + eType.ToString() + "][text:" + sText+"]");
				_cSyncRoot = new object();
				_sText = sText;
                _sText = _sText.NormalizeNewLines();
				_cFont = cFont;
				_nBorderWidth = nBorderWidth;
				_bIsGoingStop = false;
				stArea = Measure(_sText, _cFont, nBorderWidth);

				//1b
				//if (0 != stArea.nWidth % 4)
				//    stArea = new Area(0, 0, (ushort)(stArea.nWidth + 4 - stArea.nWidth % 4), stArea.nHeight);
				
				eBrushStyle = BrushStyle.Solid;
				stColor = Color.White;
				stColorGradient = Color.Black;
				stColorBorder = Color.Black;
				stColorBorderBackground = Color.Transparent;
			}
			catch
			{
				Fail();
				throw;
			}
		}
        ~Text()
        {
			try
			{
				Dispose();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
        }

        static public Area Measure(string sText, Font cFont, double nBorderWidth)
		{
			if ((cFont.Style == FontStyle.Italic || cFont.Style == (FontStyle.Italic | FontStyle.Bold)) && sText.Length < 60)  // ВРЕМЕННО ////////////////  добавить tilt, shrink  (-0.1   0.8)
			{
				Area stArea = Measure(sText, cFont, nBorderWidth, int.MaxValue);
				return new Area(stArea.nLeft, stArea.nTop, (ushort)(stArea.nWidth * 0.9f + 1), (ushort)(stArea.nHeight - 1));
			}
			else
				return Measure(sText, cFont, nBorderWidth, int.MaxValue);
		}
        static private Area Measure(string sText, Font cFont, double nBorderWidth, int nWidthMaximum)
        {
            try    // ВНИМАНИЕ!  MeasureString  не учитывает последние пробелы!!   //  И не учитывает шрифт италик (наклонный)!!!! 
            {
                Graphics cGraphics = Graphics.FromImage(new Bitmap(1, 1));
                //cGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                //SizeF cTextSize = cGraphics.MeasureString(sText, cFont);
				SizeF cTextSize;

				if (sText.EndsWith(" "))
					cTextSize = cGraphics.MeasureString(sText + ".", cFont, nWidthMaximum, StringFormat.GenericTypographic);
				else
					cTextSize = cGraphics.MeasureString(sText, cFont, nWidthMaximum, StringFormat.GenericTypographic);

				if (cFont.Style == FontStyle.Italic || cFont.Style == (FontStyle.Italic | FontStyle.Bold))
					cTextSize.Width += cTextSize.Height / 6f; 

				ushort nHeight = (ushort)(cTextSize.Height * 0.91F + 2 * nBorderWidth);
				ushort nWidth = (ushort)(cTextSize.Width * 0.751F + 2 * nBorderWidth);

				if ("" != sText && 0 < cTextSize.Height && 0 == cTextSize.Width)
					return new Area(0, 0, 10, nHeight);
				else
					return new Area(0, 0, nWidth, nHeight);
            }
			catch (Exception ex) 
			{
				return new Area(0, 0, 0, 0);
				(new Logger()).WriteError(ex);
			}
        }     

        private void ChangeText(string sText)  
        {
            if (_sText != sText)
            {
                _sText = sText;
                Area stA = Measure(_sText, _cFont, _nBorderWidth);
                stArea = new Area(stArea.nLeft, stArea.nTop, stA.nWidth, stA.nHeight);
				stArea.DockAccept(cDock);
                if (EffectStatus.Preparing == eStatus || EffectStatus.Running == eStatus)
                    DrawText();
            }
        }
        override public void Prepare()
        {
            base.Prepare();
			if (null==_cPixels)
				DrawText();
        }
        
        private void DrawText()
        {
			if (ushort.MaxValue == stArea.nHeight && ushort.MaxValue > stArea.nWidth)
				stArea = Measure(_sText, _cFont, _nBorderWidth, stArea.nWidth);

            StringFormat cStringFormat = new StringFormat(StringFormat.GenericTypographic);

			Bitmap cBmp = new Bitmap(stArea.nWidth, stArea.nHeight);
            cBmp.MakeTransparent();
            Graphics cGraphics = Graphics.FromImage(cBmp);
			Pen cPen = null;
			if (255 > stColorBorderBackground.A)
			{
				cPen = new Pen(stColorBorderBackground, 1);
				cGraphics.DrawRectangle(cPen, new Rectangle(0, 0, stArea.nWidth, stArea.nHeight));
				cPen.Dispose();
			}
            GraphicsPath cPath = new GraphicsPath();


			Rectangle stRect;
			
			//////////////////////
			if ((_cFont.Style == FontStyle.Italic || _cFont.Style == (FontStyle.Italic | FontStyle.Bold)) && sText.Length < 60)
			{
				Area stAreaNoWrap = Measure(_sText, _cFont, _nBorderWidth, int.MaxValue);
				cPath.AddString(_sText, _cFont.FontFamily, (int)_cFont.Style, _cFont.SizeInPoints, stRect = new Rectangle(0, (int)_nBorderWidth, stAreaNoWrap.nWidth, stAreaNoWrap.nHeight), cStringFormat);
				cPath.Warp(new PointF[4] { new PointF(0 + stRect.Height * 0.0f, 0), new PointF(stRect.Width * 0.9f + stRect.Height * 0.0f, 0), new PointF(0, (ushort)(stRect.Height - 1)), new PointF(stRect.Width * 0.9f, (ushort)(stRect.Height - 1)) }, stRect);
			}
			else
				cPath.AddString(_sText, _cFont.FontFamily, (int)_cFont.Style, _cFont.SizeInPoints, stRect = new Rectangle(0, (int)_nBorderWidth, stArea.nWidth, stArea.nHeight), cStringFormat);

			Brush cBrush = null;
			if (eBrushStyle == BrushStyle.Solid)
			{
				cBrush = new SolidBrush(stColor);
			}
			else if (eBrushStyle == BrushStyle.GradientH)
			{
				cBrush = new LinearGradientBrush(new Rectangle(0, 0, cBmp.Width, cBmp.Height), stColor, stColorGradient, LinearGradientMode.Horizontal);
			}
			else if (eBrushStyle == BrushStyle.GradientV)
			{
				cBrush = new LinearGradientBrush(new Rectangle(0, 0, cBmp.Width, cBmp.Height), stColor, stColorGradient, LinearGradientMode.Vertical);
			}
			cGraphics.SmoothingMode = SmoothingMode.HighQuality;
			//cGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			//cGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			cGraphics.FillPath(cBrush, cPath);


            cPen = new Pen(stColorBorder, _nBorderWidth);
            if (0 != _nBorderWidth)
            {
                cPen.LineJoin = LineJoin.Round;
                cGraphics.DrawPath(cPen, cPath);
            }

            cPath.Dispose();
            cPen.Dispose();
			if(null != cBrush)
				cBrush.Dispose();
            cGraphics.Dispose();
			
			BitmapData cBitmapData = cBmp.LockBits(new Rectangle(0,0,cBmp.Width,cBmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            lock (_cSyncRoot)
			{
				#region grayscale
				//byte[] aBytes8bpp = new byte[cBmp.Width * cBmp.Height];//1b
				//byte[] aBytes32bpp = new byte[cBitmapData.Stride * cBitmapData.Height];
				//System.Runtime.InteropServices.Marshal.Copy(cBitmapData.Scan0, aBytes32bpp, 0, aBytes32bpp.Length);
				//for (int nIndx32 = 0, nIndx8 = 0; aBytes32bpp.Length > nIndx32; nIndx32 += 4, nIndx8++)
				//    aBytes8bpp[nIndx8] = (byte)((aBytes32bpp[nIndx32] * .3) + (aBytes32bpp[nIndx32 + 1] * .59) + (aBytes32bpp[nIndx32 + 2] * .11));
				#endregion
				if (null != _cPixels)
					Baetylus.PixelsMapDispose(_cPixels, true);
				_cPixels = new PixelsMap(bCUDA, stArea, PixelsMap.Format.ARGB32);
				_cPixels.nAlphaConstant = stColor.A;
				_cPixels.bKeepAlive = true;
				//1b _cPixels.CopyIn(aBytes8bpp); //1b 
				_cPixels.CopyIn(cBitmapData.Scan0, cBitmapData.Stride * cBitmapData.Height);  //1b 
			}

            
            cBmp.UnlockBits(cBitmapData);
            cBmp.Dispose();
        }

        override public PixelsMap FrameNext()
        {
            base.FrameNext();
            lock (_cSyncRoot)
            {
                if (nFrameCurrent > nDuration)
                {
                    base.Stop();
                    return null;
				}
				if (null != _cPixels)
					_cPixels.nAlphaConstant = nCurrentOpacity;

                return _cPixels;
            }
        }
		public override void Dispose()
		{
			if (null != _cPixels)
			{
				_cPixels.Dispose(true);
				_cPixels = null;
			}
			base.Dispose();
		}
		public override void Stop()
		{
			if (this.eStatus == EffectStatus.Preparing)
			{
				base.Stop();
				return;
			}
			if (!_bIsGoingStop && nFrameCurrent <= nDuration)
			{
				lock (_cSyncRoot)
					if (0 < nOutDissolve)
					{
						_bIsGoingStop = true;
						nDuration = nFrameCurrent + nOutDissolve - 1;
					}
					else
						base.Stop();
			}
		}
		public void Wrap(int nWidth)
		{
			Area stA = Measure(_sText, _cFont, _nBorderWidth, nWidth);
			stArea = new Area(stArea.nLeft, stArea.nTop, stA.nWidth, stA.nHeight);
			if (EffectStatus.Preparing == eStatus || EffectStatus.Running == eStatus)
				DrawText();
		}
    }
}


        
