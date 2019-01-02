using System;
using System.Collections.Generic;

using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

using helpers;
using helpers.extensions;
using System.Xml;


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
        private float _nShadowOffsetX;
        private float _nShadowOffsetY;
        private Bitmap _cImage;
		private PixelsMap _cPixelsMap;
        private PixelsMap.Triple _cPMDuo;
        private object _cSyncRoot;
		private bool _bIsGoingStop;
		private object _oLock;
		private bool _bDisposed;
		private bool _bWaitForOutDissolve;
		private ushort _nWidthMax;
		private ushort _nHeightMax;
		private float _nShiftTop;
		private float _nPressBottom;
		private Area _stAreaMeasured;

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
        public float nPressBottom
        {
            get
            {
                return _nPressBottom;
            }
            set
            {
                ChangePressBottom(value);
            }
        }
        public bool bWaitForOutDissolve
		{
			set
			{
				_bWaitForOutDissolve = value;
			}
		}
		private Color _stColor;
		private Color _stColorBorder;
        private Color _stColorShadow;
        private Color _stColorBorderBackground; // not realised yet
		private Color _stColorGradient; // not realised yet
        private BrushStyle _eBrushStyle; // not realised yet
		private DateTime _dtChanged;
        private Bytes _aDrawCurrent;
        private List<long> _aPMGotDrawCurrent;

        private Text()
			: base(EffectType.Text)
		{
			_cFont = null;
			_oLock = new object();
			_cSyncRoot = new object();
			_bDisposed = false;
			_nWidthMax = ushort.MaxValue;
			_nHeightMax = ushort.MaxValue;
			_nShiftTop = 0;
			_nPressBottom = 0;
			_stAreaMeasured = new Area(0, 0, 0, 0);
			_bIsGoingStop = false;
			_bWaitForOutDissolve = true;

			_eBrushStyle = BrushStyle.Solid;
			_stColor = Color.White;
			_stColorGradient = Color.Black;
			_stColorBorder = Color.Black;
			_stColorBorderBackground = Color.Transparent;
			_dtChanged = DateTime.Now;
			_nBorderWidth = 0;
            cDock = new Dock();
            _aDrawCurrent = null;
            _aPMGotDrawCurrent = new List<long>();
        }
    private Text(string sText, Font cFont, float nBorderWidth)
			: this(sText, cFont, nBorderWidth, Color.White, Color.Black)
		{
		}
		public Text(string sText, Font cFont, float nBorderWidth, Color stColor, Color stColorBorder)
			: this(sText, cFont, nBorderWidth, stColor, stColorBorder, ushort.MaxValue)
		{
		}
		public Text(string sText, Font cFont, float nBorderWidth, Color stColor, Color stColorBorder, ushort nWidthMax)
			: this(sText, cFont, nBorderWidth, stColor, stColorBorder, nWidthMax, 0, 0)
		{
		}
		public Text(string sText, Font cFont, float nBorderWidth, Color stColor, Color stColorBorder, ushort nWidthMax, short nShiftTop, short nPressBottom)
			: this()
		{
			try
			{
				_sText = sText;
				_cFont = cFont;
				_nBorderWidth = nBorderWidth;
				_nWidthMax = nWidthMax;
				_nShiftTop = nShiftTop;
				_nPressBottom = nPressBottom;
				_stColor = stColor;
				_stColorBorder = stColorBorder;

				Init();
			}
			catch
			{
				Fail();
				throw;
			}
		}
		public Text(XmlNode cXmlNode)
			: this()
		{
			try
			{
				LoadXML(cXmlNode);

				_nWidthMax = cXmlNode.AttributeOrDefaultGet<ushort>("width_max", ushort.MaxValue);
				_nHeightMax = cXmlNode.AttributeOrDefaultGet<ushort>("height_max", ushort.MaxValue);
				_nShiftTop = cXmlNode.AttributeOrDefaultGet<float>("shift_top", 0);
				_nPressBottom = cXmlNode.AttributeOrDefaultGet<float>("press_bot", 0);

				XmlNode cNodeChild = cXmlNode.NodeGet("value", false);
                if (null == cNodeChild || null == cNodeChild.FirstChild || null == cNodeChild.FirstChild.Value || 1 > cNodeChild.FirstChild.Value.Length)
					_sText = " ";
				else
					_sText = cNodeChild.FirstChild.Value.FromXML();

				XmlNode cNodeFont = cXmlNode.NodeGet("font");
				_cFont = new System.Drawing.Font(cNodeFont.AttributeValueGet("name"), cNodeFont.AttributeGet<int>("size"), cNodeFont.AttributeGet<System.Drawing.FontStyle>("style"));  // bold | italic = 3

				if (null != (cNodeChild = cNodeFont.NodeGet("color", false)))
					_stColor = System.Drawing.Color.FromArgb(cNodeChild.AttributeOrDefaultGet<byte>("alpha", 255), cNodeChild.AttributeGet<byte>("red"), cNodeChild.AttributeGet<byte>("green"), cNodeChild.AttributeGet<byte>("blue"));

				if (null != (cNodeChild = cNodeFont.NodeGet("border", false)))
				{
					_nBorderWidth = cNodeChild.AttributeGet<float>("width");
					cNodeChild = cNodeChild.NodeGet("color");
					_stColorBorder = System.Drawing.Color.FromArgb(cNodeChild.AttributeOrDefaultGet<byte>("alpha", 255), cNodeChild.AttributeGet<byte>("red"), cNodeChild.AttributeGet<byte>("green"), cNodeChild.AttributeGet<byte>("blue"));
				}
                if (null != (cNodeChild = cNodeFont.NodeGet("shadow", false)))
                {
                    _nShadowOffsetX = cNodeChild.AttributeGet<float>("offset_x");
                    _nShadowOffsetY = cNodeChild.AttributeGet<float>("offset_y");
                    cNodeChild = cNodeChild.NodeGet("color");
                    _stColorShadow = System.Drawing.Color.FromArgb(cNodeChild.AttributeOrDefaultGet<byte>("alpha", 255), cNodeChild.AttributeGet<byte>("red"), cNodeChild.AttributeGet<byte>("green"), cNodeChild.AttributeGet<byte>("blue"));
                }
                Init();
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

		private void Init()
		{
			(new Logger()).WriteDebug3("[hc:" + nID + "][type:" + eType.ToString() + "][text:" + sText + "]");
			_sText = Macroses(_sText.NormalizeNewLines());
			_stAreaMeasured = MeasureDrawAndSave();  // только хранение w h
			stArea = new Area(stArea.nLeft, stArea.nTop, _stAreaMeasured.nWidth > _nWidthMax ? _nWidthMax : _stAreaMeasured.nWidth, _stAreaMeasured.nHeight > _nHeightMax ? _nHeightMax : _stAreaMeasured.nHeight);
		}



		static public Area SizeOfSpaceGet(Font cFont, float nBorderWidth)
		{
			Text cT1 = new Text("SSS SSS", cFont, nBorderWidth);
			Text cT2 = new Text("SSSSSS", cFont, nBorderWidth);
			Area cRetVal = new Area(0, 0, (ushort)(cT1.stArea.nWidth - cT2.stArea.nWidth), cT1.stArea.nHeight);
			return cRetVal;
		}
		public DateTime dtChanged
		{
			get
			{
				return _dtChanged;
			}
		}
        private void ChangeText(string sText)  
        {
            if (_sText != sText)
            {
                _sText = Macroses(sText.NormalizeNewLines());
                _stAreaMeasured = MeasureDrawAndSave();
				stArea = new Area(stArea.nLeft, stArea.nTop, _stAreaMeasured.nWidth > _nWidthMax ? _nWidthMax : _stAreaMeasured.nWidth, _stAreaMeasured.nHeight > _nHeightMax ? _nHeightMax : _stAreaMeasured.nHeight);
				stArea = stArea.Dock(stBase, cDock);
				if (EffectStatus.Preparing == eStatus || EffectStatus.Running == eStatus || (EffectStatus.Idle == eStatus && null != _cPMDuo))
                    DrawText();
            }
        }
        private string Macroses(string sText)
        {
            return sText.Replace("%br%", "\n");
        }
        private void ChangePressBottom(float nNewPressBottom)
        {
            if (_nPressBottom != nNewPressBottom)
            {
                _nPressBottom = nNewPressBottom;
                _stAreaMeasured = MeasureDrawAndSave();
                stArea = new Area(stArea.nLeft, stArea.nTop, _stAreaMeasured.nWidth > _nWidthMax ? _nWidthMax : _stAreaMeasured.nWidth, _stAreaMeasured.nHeight > _nHeightMax ? _nHeightMax : _stAreaMeasured.nHeight);
                stArea = stArea.Dock(stBase, cDock);
                if (EffectStatus.Preparing == eStatus || EffectStatus.Running == eStatus || (EffectStatus.Idle == eStatus && null != _cPMDuo))
                    DrawText();
            }
        }
        override public void Prepare()
        {
			base.Prepare();
            stArea = stArea.Dock(stBase, cDock);
            if (null == _cPMDuo)
                DrawText();

            _cPMDuo.RenewFirstTime();
            nPixelsMapSyncIndex = byte.MaxValue;
        }
        private unsafe Size GetRealWidth(Bitmap cBM, PixelFormat ePixelFormat)
		{
			BitmapData bData = cBM.LockBits(new Rectangle(0, 0, cBM.Width, cBM.Height), ImageLockMode.ReadOnly, ePixelFormat);
			byte bitsPerPixel = (byte)Image.GetPixelFormatSize(bData.PixelFormat);
			byte* scan0 = (byte*)bData.Scan0.ToPointer();

			// перебор средней одной строки
			int nRealEnd = 0;
			int nRealStart = bData.Width - 1;
			int i = cBM.Height / 3;  // в этой строке 100% есть пиксели букв - экономим врем¤
			int j = 0;
			for (byte* data = scan0 + i * bData.Stride + 3; j < nRealStart; data += 4)
			{
				//byte* data = scan0 + i * bData.Stride + j * bitsPerPixel / 8;
				if (*(data) != 0)
				{
					nRealStart = j;
					break;
				}
				j++;
			}
			j = bData.Width - 1;
			for (byte* data = scan0 + i * bData.Stride + (bData.Width - 1) * bitsPerPixel / 8 + 3; j > nRealEnd; data -= 4)
			{
				//byte* data = scan0 + i * bData.Stride + j * bitsPerPixel / 8;
				if (*(data) != 0)
				{
					nRealEnd = j;
					break;
				}
				j--;
			}
			// перебор всех строк
			for (i = 0; i < bData.Height; ++i)
			{
				j = 0;
				for (byte* data = scan0 + i * bData.Stride + 3; j < nRealStart; data += 4)
				{
					//byte* data = scan0 + i * bData.Stride + j * bitsPerPixel / 8;
					if (*(data) != 0)
					{
						nRealStart = j;
						break;
					}
					j++;
				}
			}
			for (i = 0; i < bData.Height; ++i)
			{
				j = bData.Width - 1;
				for (byte* data = scan0 + i * bData.Stride + (bData.Width - 1) * bitsPerPixel / 8 + 3; j > nRealEnd; data -= 4)
				{
					//byte* data = scan0 + i * bData.Stride + j * bitsPerPixel / 8;
					if (*(data) != 0)
					{
						nRealEnd = j;
						break;
					}
					j--;
				}
			}
			cBM.UnlockBits(bData);
			return new Size(nRealStart, nRealEnd);  // индексы начала и конца
		}
		private Area MeasureDrawAndSave() // измерение теперь реальное, а значит по факту просто рисует результат.
		{
			StringFormat cStringFormat = new StringFormat(StringFormat.GenericTypographic);

			Font cFont2 = new Font(_cFont.Name, _cFont.Size * 2, _cFont.Style);  // рисуем в 2 раза бќльшим шрифтом и уменьшаем - результат ахерительный! как в фотошопе!
			Graphics cGraphics2 = Graphics.FromImage(new Bitmap(1, 1));
			cGraphics2.PageUnit = GraphicsUnit.Pixel;
			SizeF stSize2 = cGraphics2.MeasureString(_sText, cFont2, int.MaxValue, StringFormat.GenericTypographic);
			float nBorderWidth2 = _nBorderWidth * 2;
            float nShadowSizeX2 = _nShadowOffsetX * 2;
            float nShadowSizeY2 = _nShadowOffsetY * 2;
            float nShift2 = cFont2.Size * _nShiftTop / 100;
			float nMoveX2 = nShift2 <= 0 ? 0 : nShift2;
			float nPress2 = cFont2.Size * _nPressBottom / 100;
			if (nMoveX2 > 0)
				stSize2.Width += nMoveX2;
			if (nPress2 < 0)
				stSize2.Height += -nPress2;
			if (stSize2.Width < 2)  // а то на микро-текстах сбоит
				stSize2.Width = 2;
			if (stSize2.Height < 2)
				stSize2.Height = 2;
			Bitmap cBmp2 = new Bitmap((int)stSize2.Width, (int)stSize2.Height, PixelFormat.Format32bppArgb);
			GraphicsPath cPath2 = new GraphicsPath();
			Rectangle stRect2;
			cPath2.AddString(_sText, _cFont.FontFamily, (int)_cFont.Style, _cFont.SizeInPoints * 2, stRect2 = new Rectangle(0, (int)(_nBorderWidth + 0.5), (int)stSize2.Width, (int)stSize2.Height), cStringFormat);
			if (nShift2 != 0 || nPress2 != 0)
				cPath2.Warp(new PointF[4] { new PointF((int)_nBorderWidth + nMoveX2, 0), new PointF(stSize2.Width + nMoveX2, 0), new PointF((int)_nBorderWidth - nShift2 + nMoveX2, stSize2.Height - nPress2), new PointF(stSize2.Width - nShift2 + nMoveX2, stSize2.Height - nPress2) }, stRect2);

			cGraphics2 = Graphics.FromImage(cBmp2);
			cGraphics2.Clear(Color.Transparent);
			Brush cBrush2 = new SolidBrush(_stColor);

			cGraphics2.PageUnit = GraphicsUnit.Pixel;
			cGraphics2.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			cGraphics2.SmoothingMode = SmoothingMode.HighQuality;
			cGraphics2.CompositingQuality = CompositingQuality.HighQuality;
			cGraphics2.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (nShadowSizeX2 > 0 || nShadowSizeY2 > 0)
            {
                cGraphics2.TranslateTransform(nShadowSizeX2, nShadowSizeY2);
                Pen pen = new Pen(_stColorShadow, Math.Max(nShadowSizeX2, nShadowSizeY2));
                cGraphics2.DrawPath(pen, cPath2);
                cGraphics2.ResetTransform();
            }
            cGraphics2.FillPath(cBrush2, cPath2);
			if (0 < nBorderWidth2)
			{
				Pen cPen2 = new Pen(_stColorBorder, nBorderWidth2);
				cPen2.Alignment = PenAlignment.Center;
				cPen2.LineJoin = LineJoin.Round;
				cGraphics2.DrawPath(cPen2, cPath2);
			}
			// checking real size
			Size cStartEndReal2 = GetRealWidth(cBmp2, PixelFormat.Format32bppArgb);
            if (cStartEndReal2.Width > cStartEndReal2.Height)
            {
                (new Logger()).WriteWarning("was not found any pixels during drawing text. Try to change 'press' or 'shift' parameters.");
                _cImage = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                return new Area(0, 0, (ushort)_cImage.Width, (ushort)_cImage.Height);
            }
			int nWReal2 = cStartEndReal2.Height - cStartEndReal2.Width + 1;
			int nWReal = (int)(nWReal2 / 2f + 0.5) + 1;  // +1 об¤зательно 
			int nLeftNew = -(int)(cStartEndReal2.Width / 2.0 - 0.5);
			if (nWReal > _nWidthMax)
				_cImage = new Bitmap((int)(_nWidthMax), (int)(stSize2.Height / 2 + 0.5), PixelFormat.Format32bppArgb);
			else
				_cImage = new Bitmap((int)(nWReal), (int)(stSize2.Height / 2 + 0.5), PixelFormat.Format32bppArgb);
			Graphics cGraphics = Graphics.FromImage(_cImage);
			cGraphics.Clear(Color.Transparent);
			cGraphics.PageUnit = GraphicsUnit.Pixel;
			cGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			cGraphics.SmoothingMode = SmoothingMode.AntiAlias;
			cGraphics.CompositingQuality = CompositingQuality.HighQuality;
			cGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			if (nWReal > _nWidthMax)
			{
				float nK = ((float)_nWidthMax - 1f) / (cStartEndReal2.Height - cStartEndReal2.Width + 1);
				nLeftNew = -(int)(nK * cStartEndReal2.Width + 0.5);
				cGraphics.DrawImage(cBmp2, new Rectangle(nLeftNew, 0, (int)(nK * stSize2.Width + 0.5), (int)(stSize2.Height / 2 + 0.5)), new Rectangle(0, 0, (int)stSize2.Width, (int)stSize2.Height), GraphicsUnit.Pixel);
			}
			else
				cGraphics.DrawImage(cBmp2, new Rectangle(nLeftNew, 0, (int)(stSize2.Width / 2 + 0.5), (int)(stSize2.Height / 2 + 0.5)), new Rectangle(0, 0, (int)stSize2.Width, (int)stSize2.Height), GraphicsUnit.Pixel);

			Area stRetVal = new Area(0, 0, (ushort)_cImage.Width, (ushort)_cImage.Height);
			return stRetVal;
		}
		private Brush GetBrush(Bitmap cBmp)
		{
			Brush cBrush = null;
			if (_eBrushStyle == BrushStyle.Solid)
			{
				cBrush = new SolidBrush(_stColor);
			}
			else if (_eBrushStyle == BrushStyle.GradientH)
			{
				cBrush = new LinearGradientBrush(new Rectangle(0, 0, cBmp.Width, cBmp.Height), _stColor, _stColorGradient, LinearGradientMode.Horizontal);
			}
			else if (_eBrushStyle == BrushStyle.GradientV)
			{
				cBrush = new LinearGradientBrush(new Rectangle(0, 0, cBmp.Width, cBmp.Height), _stColor, _stColorGradient, LinearGradientMode.Vertical);
			}
			return cBrush;
		}
		private void DrawText()
        {
			BitmapData cBitmapData = _cImage.LockBits(new Rectangle(0,0, _cImage.Width, _cImage.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            lock (_cSyncRoot)
			{
                if (_aDrawCurrent != null)
                    Baetylus._cBinM.BytesBack(_aDrawCurrent, 51);
                _aDrawCurrent = Baetylus._cBinM.BytesGet(cBitmapData.Stride * cBitmapData.Height, 52);
                System.Runtime.InteropServices.Marshal.Copy(cBitmapData.Scan0, _aDrawCurrent.aBytes, 0, cBitmapData.Stride * cBitmapData.Height);
                _aPMGotDrawCurrent.Clear();

                if (null != _cPMDuo && stArea != _cPMDuo.cFirst.stArea)
				{
                    Baetylus.PixelsMapDispose(_cPMDuo, true);
                    _cPMDuo = null;
				}
				if (null == _cPMDuo)
                {
                    _cPMDuo = new PixelsMap.Triple(stMergingMethod, stArea, PixelsMap.Format.ARGB32, true, Baetylus.PixelsMapDispose);
                    _cPMDuo.SetAlphaConstant(_stColor.A);
                    _cPMDuo.Allocate();
                }
                _dtChanged = DateTime.Now;
            }
            _cImage.UnlockBits(cBitmapData);
            _cImage = null;
        }

        override public PixelsMap FrameNext()
        {
            lock (_cSyncRoot)
            {
				base.FrameNext();
				if (nFrameCurrent >= nDuration)
                {
                    base.Stop();
                }

                _cPixelsMap = _cPMDuo.Switch(nPixelsMapSyncIndex);

                if (null == _cPixelsMap)
                {
                    _cPixelsMap = new PixelsMap(stMergingMethod, stArea, PixelsMap.Format.ARGB32);
                    _cPixelsMap.bKeepAlive = false; // на одни раз
                    _cPixelsMap.nIndexTriple = nPixelsMapSyncIndex;
                    _cPixelsMap.Allocate();
                }

                if (null != _cPixelsMap)
                {
                    if (!_aPMGotDrawCurrent.Contains(_cPixelsMap.nID))
                    {
                        _dtChanged = DateTime.Now;
                        _aPMGotDrawCurrent.Add(_cPixelsMap.nID);
                        _cPixelsMap.CopyIn(_aDrawCurrent.aBytes);
                    }
                    if (_cPixelsMap.nAlphaConstant != nCurrentOpacity)
                    {
                        _dtChanged = DateTime.Now;
                        _cPixelsMap.nAlphaConstant = nCurrentOpacity;
                    }
                    if (null != cMask && _cPixelsMap.eAlpha != cMask.eMaskType)
                    {
                        _dtChanged = DateTime.Now;
                        _cPixelsMap.eAlpha = cMask.eMaskType;
                    }
                    if (_cPixelsMap.stArea.nLeft != stArea.nLeft || _cPixelsMap.stArea.nTop != stArea.nTop)
                    {
                        _dtChanged = DateTime.Now;
                        _cPixelsMap.Move(stArea.nLeft, stArea.nTop);
                    }
                }
                return _cPixelsMap;
            }
        }
		public override void Dispose()
		{
			lock (_oLock)
			{
				if (_bDisposed)
					return;
				_bDisposed = true;
			}
			base.Dispose();
			lock (_cSyncRoot)
			{
                Baetylus.PixelsMapDispose(_cPMDuo, true);
                //_cPMDuo.Dispose(true);
                _cPMDuo = null;
            }
        }
        public override void Stop()
		{
			if (this.eStatus == EffectStatus.Preparing)
			{
                base.Stop();
                return;
            }

            lock (_cSyncRoot)
            {
                if (!_bIsGoingStop)
                {
                    if (_bWaitForOutDissolve && 0 < nOutDissolve && nFrameCurrent < nDuration - nOutDissolve + 1)
                    {
                        _bIsGoingStop = true;
                        nDuration = nFrameCurrent + nOutDissolve - 1;
                    }
                    else if (!_bWaitForOutDissolve || nOutDissolve == 0 || nFrameCurrent >= nDuration)
                        base.Stop();
                }
            }
        }
        public void Wrap(int nWidth)
		{
			// не реализовано, из-за удачного по¤влени¤ cWidthMax ;
			//stArea = new Area(stArea.nLeft, stArea.nTop, stA.nWidth, stA.nHeight);
			//Area stA = MeasureAndDraw();
			//if (EffectStatus.Preparing == eStatus || EffectStatus.Running == eStatus)
			//	DrawText();
		}
		static private Area Measure_old(string sText, Font cFont, double nBorderWidth)
		{
			if ((cFont.Style == FontStyle.Italic || cFont.Style == (FontStyle.Italic | FontStyle.Bold)) && sText.Length < 60)  // ¬–≈ћ≈ЌЌќ ////////////////  добавить tilt, shrink  (-0.1   0.8)
			{
				Area stArea = Measure_old(sText, cFont, nBorderWidth, int.MaxValue);
				return new Area(0, 0, (ushort)(stArea.nWidth * 0.9f + 1), (ushort)(stArea.nHeight - 1));
			}
			else
				return Measure_old(sText, cFont, nBorderWidth, int.MaxValue);
		}
		static private Area Measure_old(string sText, Font cFont, double nBorderWidth, int nWidthMaximum)
		{
			try    // ¬Ќ»ћјЌ»≈!  MeasureString  не учитывает последние пробелы!!   //  » не учитывает шрифт италик (наклонный)!!!! 
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
				int nAdd = cFont.Size > 9 ? 2 : 0;
				ushort nWidth = (ushort)(cTextSize.Width * 0.751F + nAdd + 2 * nBorderWidth);
				if (0 == nHeight)
					nHeight = 1;
				if (0 == nWidth)
					nWidth = 1;
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

	}
}


        
