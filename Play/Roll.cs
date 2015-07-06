using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;

using helpers;

namespace BTL.Play
{
	public class Roll : EffectVideo, IContainer //UNDONE
    {
		
		public class Keyframe
		{
			public enum Type
			{ 
				calculated,
				hold,
				linear,
				besier
			}
			public float nPosition;
			public long nFrame;
			public float nBesierP1;
			public Type eType;
			public Keyframe()
			{
				nPosition = 0;
				nFrame = 0;
				nBesierP1 = 0;
				eType = Type.linear;
			}

			public static Keyframe[] FindInterval(Keyframe[] aCurve, long nFrame)
			{
				if (aCurve[0].nFrame >= nFrame)
					return new Keyframe[1] { aCurve[0]};
				if (aCurve[aCurve.Length - 1].nFrame <= nFrame)
					return new Keyframe[1] { aCurve[aCurve.Length - 1] };
				for (int ni = 0; ni < aCurve.Length - 1; ni++)
				{
					if (aCurve[ni].nFrame == nFrame)
						return new Keyframe[1] { aCurve[ni] };
					if (aCurve[ni].nFrame > nFrame || aCurve[ni].nFrame < nFrame && aCurve[ni + 1].nFrame > nFrame)
						return new Keyframe[2] { aCurve[ni], aCurve[ni + 1] };
				}
				throw new Exception("impossible to find interval");
			}
			public static float CalculateCurvesPoint(Keyframe[] aCurve, long nFrame)
			{
				if (null == aCurve || 1> aCurve.Length)
					throw new Exception("curve is empty");
				Keyframe[] aInterval=FindInterval(aCurve, nFrame);
				if (1 == aInterval.Length)
				{
					if (aCurve[0] == aInterval[0])
						; // в идеале расчитать экстрапол€цию за границы диапазона (касательна€ в ту же сторону)
					if (aCurve[aCurve.Length - 1] == aInterval[0])
						; // в идеале расчитать экстрапол€цию за границы диапазона (касательна€ в ту же сторону)
					return aInterval[0].nPosition; // но пока просто остаЄмс€ на этой точке
				}

				float nRetVal = 0;
				switch (aInterval[0].eType)
				{
					case Keyframe.Type.hold:
						// формула y=A при M<=x<N  
						nRetVal = aInterval[0].nPosition;
						break;
					case Keyframe.Type.linear:
						// формула y=kx+h, где y-пиксели, x-фреймы. M, N - точки на х; A, B - точки на y, то люба€ точка (PX, FR) <=  PX=((B-A)/(N-M))*(FR-M)+A
						nRetVal = ((aInterval[1].nPosition - aInterval[0].nPosition) / (aInterval[1].nFrame - aInterval[0].nFrame)) * (nFrame - aInterval[0].nFrame) + aInterval[0].nPosition;
						break;
					case Keyframe.Type.besier:
						break;
					default:
						break;
				}
				return nRetVal;
			}
			public static bool CurveIsWrong(Keyframe[] aCurve)
			{
				if (null == aCurve || 1 > aCurve.Length)
					return true;
				else
					return false;
			}
			public static Keyframe[] CopyCurve(Keyframe[] aCurve)
			{
				List<Keyframe> aRetVal = new List<Keyframe>();
				foreach (Keyframe cKF in aCurve)
				{
					aRetVal.Add(new Keyframe() { eType = cKF.eType, nBesierP1 = cKF.nBesierP1, nFrame = cKF.nFrame, nPosition = cKF.nPosition });
				}
				return aRetVal.ToArray();
			}
			public static void ChangeFramesInCurve(Keyframe[] aCurve, long nDeltaFrames)
			{
				foreach (Keyframe cKF in aCurve)
				{
					cKF.nFrame += nDeltaFrames;
				}
			}
			public static void ChangePointsInCurve(Keyframe[] aCurve, float nDeltaPosition)
			{
				foreach (Keyframe cKF in aCurve)
				{
					cKF.nPosition += nDeltaPosition;
				}
			}
		}
		private class Item
        {
			public IVideo iVideo;
			public IEffect iEffect
			{
				get
				{
					return (IEffect)iVideo;
				}
			}
			public Effect cEffect
			{
				get
				{
					return (Effect)iVideo;
				}
			}
			public PointF stPosition;
			public PointF stPositionPrevious;
			public bool bMoved;
			public float nSpeed;
			public bool bSticky;
			public Keyframe[] aKeyframes;
			public ulong nFrameStartInRoll;
			public ulong nStartDelay;
			public Item(IVideo iVideo, Point stPosition)
            {
				this.iVideo = iVideo;
                this.stPosition = this.stPositionPrevious = stPosition;
                this.nSpeed = float.MaxValue;
				nFrameStartInRoll = ulong.MaxValue;
				nStartDelay = 0;
				aKeyframes = null;
            }
			public Item(IVideo iVideo, Point stPosition, float nSpeed, Keyframe[] aKeyframes, bool bSticky, uint nStartDelay)
				: this(iVideo, stPosition)
            {
                this.nSpeed = nSpeed;
				this.aKeyframes = aKeyframes;
				this.bSticky = bSticky;
				this.nStartDelay = nStartDelay;
            }
        }
        public enum Direction
        {
            Right,
            Left,
            Down,
			Up,
			[ObsoleteAttribute("This property is obsolete. Use Down instead.", true)]
			UpToDown = Down,
			[ObsoleteAttribute("This property is obsolete. Use Right instead.", true)]
			LeftToRight = Right,
			[ObsoleteAttribute("This property is obsolete. Use Left instead.", true)]
			RightToLeft = Left,
			[ObsoleteAttribute("This property is obsolete. Use Up instead.", true)]
			DownToUp = Up
        }

        private List<Item> _aEffects;
        private List<float> _aSpeeds;
        private PixelsMap _cCUDAPixelsMap;
        private PixelsMap _cPixelsMap;
		private float _nSpeed, _nSpeedNew;
        public ushort nEffectsQty
        { 
            get { return (ushort)_aEffects.Count; } 
        }
		public Direction eDirection;
		public float nSpeed
		{
			get
			{
				return _nSpeedNew;
			}
			set
			{
				(new Logger()).WriteDebug2("roll_speed = " + value);
				_nSpeedNew = value;
			}
		} //кол-во пикселей в секунду.  "byte" стало мало помен€л на short. //EMERGENCY а нам нужен именно short? или мы все же не юзаем минусовые знаечни€?
		public bool bStopOnEmpty;  //todo: надобы в контейнер это подн€ть...

        public Roll()
            : base(EffectType.Roll)
        {
			try
			{
				_aEffects = new List<Item>();
				_aSpeeds = new List<float>();
				eDirection = Direction.Down;
				nSpeed = _nSpeedNew = 1;
				bStopOnEmpty = false;
			}
			catch
			{
				Fail();
				throw;
			}
		}
        ~Roll()
        {
            try
            {
                if (null != _cPixelsMap)
                    _cPixelsMap.Dispose(true);
            }
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
            try
			{
				if (null != _cCUDAPixelsMap)
					_cCUDAPixelsMap.Dispose(true);
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
        }

		public void EffectAdd(IVideo iVideo)
        {
			EffectAdd(iVideo, true);
        }
		public void EffectAdd(IVideo iVideo, bool bSteaky)
		{
			EffectAdd(iVideo, float.MaxValue, null, bSteaky);
		}

		public void EffectAdd(IVideo iVideo, Keyframe[] aKeyframes)
		{
			EffectAdd(iVideo, float.MinValue, aKeyframes, false, 0); 
		}

		public void EffectAdd(IVideo iVideo, Keyframe[] aKeyframes, uint nStartDelay)
		{
			EffectAdd(iVideo, float.MinValue, aKeyframes, false, nStartDelay);
		}

		public void EffectAdd(IVideo iVideo, float nSpeed)
		{
			EffectAdd(iVideo, nSpeed, null, false);
		}

		public void EffectAdd(IVideo iVideo, float nSpeed, Keyframe[] aKeyframes, bool bSticky)
		{
			EffectAdd(iVideo, nSpeed, aKeyframes, bSticky, 0); 
		}

		public void EffectAdd(IVideo iVideo, float nSpeed, Keyframe[] aKeyframes, bool bSticky, uint nStartDelay)
        {
			IEffect iEffect = (IEffect)iVideo;
			iEffect.Prepared += OnEffectPrepared;
			iEffect.Started += OnEffectStarted;
			iEffect.Stopped += OnEffectStopped;
			iEffect.Failed += OnEffectFailed;
			Area stEffectArea = ((IVideo)iVideo).stArea;
            Point stEffectPosition = new Point();
            if (stArea.nWidth == 0 && stArea.nHeight == 0)
            {
				Area stAreaNew = stArea;
				stAreaNew.nWidth = stEffectArea.nWidth;
				stAreaNew.nHeight = stEffectArea.nHeight;
				stArea = stAreaNew;
            }
            if (EffectStatus.Idle < ((IEffect)this).eStatus)
				iEffect.Prepare();
            lock (_aEffects)
            {
                if (0 < _aEffects.Count && _aEffects[0].iVideo.bCUDA != iVideo.bCUDA)
                    throw new Exception("некорректна€ среда вычислений"); //TODO LANG
                if (1 > _aEffects.Count && null != _cPixelsMap && iVideo.bCUDA != _cPixelsMap.bCUDA)
                {
                    _cPixelsMap.Dispose(true);
					_cPixelsMap = new PixelsMap(iVideo.bCUDA, stArea, PixelsMap.Format.ARGB32);
                    _cPixelsMap.bKeepAlive = true;
					if (1 > _cPixelsMap.nLength)
						(new Logger()).WriteNotice("1 > _cPixelsMap.nLength. roll.effectadd");
					_cPixelsMap.Allocate();
                }
                if (eDirection == Direction.Up)
                {
                    stEffectPosition.X = 0;
                    stEffectPosition.Y = stArea.nHeight;
                }
                else if (eDirection == Direction.Down)
                {
                    stEffectPosition.X = 0;
                    stEffectPosition.Y = - stEffectArea.nHeight;
                }
                else if (eDirection == Direction.Right)
                {
                    stEffectPosition.X = - stEffectArea.nWidth;
                    stEffectPosition.Y = 0;
                }
                else if (eDirection == Direction.Left)
                {
                    stEffectPosition.X = stArea.nWidth;
                    stEffectPosition.Y = 0;
                }
				_aEffects.Add(new Item(iVideo, stEffectPosition, nSpeed, aKeyframes, bSticky, nStartDelay));
			}
			(new Logger()).WriteDebug2("roll_effect_add: [pos=" + stEffectPosition + "] [speed=" + nSpeed + "] [keyframes=" + aKeyframes + "] [sticky=" + bSticky + "]");
				OnEffectAdded((Effect)iVideo);
        }
        override public void Prepare()
        {
            try
            {
				bool bCUDAEffects = bCUDA;
				foreach (Item cItem in _aEffects)
				{
					if (EffectStatus.Idle == cItem.iEffect.eStatus)
						cItem.iEffect.Prepare();
					if (!cItem.iVideo.bCUDA)
						bCUDAEffects = false;
				}
                if (null != _cPixelsMap && stArea != _cPixelsMap.stArea)
                {
                    _cPixelsMap.Dispose(true);
                    _cPixelsMap = null;
                }
                if (null == _cPixelsMap)
                {
					_cPixelsMap = new PixelsMap(bCUDAEffects, stArea, PixelsMap.Format.ARGB32);
                    _cPixelsMap.bKeepAlive = true;
					if (1 > _cPixelsMap.nLength)
						(new Logger()).WriteNotice("1 > _cPixelsMap.nLength. roll.prepare");
					_cPixelsMap.Allocate();
                }
                if (bCUDA && !bCUDAEffects)
				{
					if (null != _cCUDAPixelsMap && stArea != _cCUDAPixelsMap.stArea)
					{
						_cCUDAPixelsMap.Dispose(true);
						_cCUDAPixelsMap = null;
					}
					if (null == _cCUDAPixelsMap)
					{
						_cCUDAPixelsMap = new PixelsMap(true, stArea, PixelsMap.Format.ARGB32);
						_cCUDAPixelsMap.bKeepAlive = true;
					}
				}
                base.Prepare();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
        }
        override public void Stop()
        {
			try
			{
				if (null != _cCUDAPixelsMap)
				{
					_cCUDAPixelsMap.Dispose(true);
					_cCUDAPixelsMap = null;
				}
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
            base.Stop();
        }
		//System.Diagnostics.Stopwatch cStopwatch = null;
		//System.Diagnostics.Stopwatch cSW_while = null;
		//System.Diagnostics.Stopwatch cSW_lock = null;
        override public PixelsMap FrameNext()
        {
			_nSpeed = _nSpeedNew;
            PixelsMap cRetVal = null;
            base.FrameNext();
            bool bHaveSpaceForNextEffect = true;
            int nEffectIndex = 0;
            PixelsMap cPM = null;
            IVideo iVideo;
            List<PixelsMap> aPMs = new List<PixelsMap>();

//			cSW_lock = System.Diagnostics.Stopwatch.StartNew();
            lock (_aEffects)
            {
				//cSW_while = System.Diagnostics.Stopwatch.StartNew();
                while (bHaveSpaceForNextEffect && nEffectIndex < _aEffects.Count)
                {
					//cStopwatch = System.Diagnostics.Stopwatch.StartNew();

					if (_aEffects[nEffectIndex].nStartDelay > 0)
					{
						_aEffects[nEffectIndex].nStartDelay--;
						bHaveSpaceForNextEffect = false;
						nEffectIndex++;
						continue;
					}
					if (!_aEffects[nEffectIndex].bMoved)
                    {
						if (float.MaxValue > _aEffects[nEffectIndex].nSpeed && float.MinValue < _aEffects[nEffectIndex].nSpeed)
                            _aSpeeds.Add(_aEffects[nEffectIndex].nSpeed);
                        if (null != EffectIsOnScreen)
                            EffectIsOnScreen(this, (Effect)_aEffects[nEffectIndex].iVideo);
						_aEffects[nEffectIndex].bMoved = true;
                    }

                    bHaveSpaceForNextEffect = MoveEffect(nEffectIndex);

					if (IsEffectInRollArea(nEffectIndex) && EffectStatus.Stopped != _aEffects[nEffectIndex].cEffect.eStatus)
                    {
                        if (EffectStatus.Preparing == _aEffects[nEffectIndex].iEffect.eStatus)
							_aEffects[nEffectIndex].iEffect.Start(null);
						if (EffectStatus.Running != _aEffects[nEffectIndex].iEffect.eStatus)
                            throw new Exception("Ёффект не может быть не Prepare в ROLL.FrameNext()");
                        cPM = (iVideo = (IVideo)_aEffects[nEffectIndex].iVideo).FrameNext();
                        if (null == cPM) continue;

						short nLeftNewPos = (short)Math.Floor(_aEffects[nEffectIndex].stPosition.X);
						short nTopNewPos = (short)Math.Floor(_aEffects[nEffectIndex].stPosition.Y);

						cPM.Move((short)(nLeftNewPos), (short)(nTopNewPos));

						if (Direction.Up == eDirection)
							cPM.Shift(true,  - (_aEffects[nEffectIndex].stPosition.Y - nTopNewPos), new Dock.Offset(0, 0), 0);  //true      >0  <=1  
						else if (Direction.Left == eDirection)
						{
							Dock.Offset cOffset = ((IVideo)_aEffects[nEffectIndex].cEffect).OffsetAbsoluteGet();
							cPM.Shift(false, -(_aEffects[nEffectIndex].stPosition.X - nLeftNewPos), cOffset, _aEffects[nEffectIndex].stPosition.X - _aEffects[nEffectIndex].stPositionPrevious.X);  //    если <0 ,то значит влево шло - это дл€ куды инфа - если идеально совпадЄт, то =0 и отрабатывать не нужно будет
						}
                        if (null != iVideo.iMask)
                        {
                            aPMs.Add(iVideo.iMask.FrameNext());
                            aPMs[aPMs.Count - 1].eAlpha = DisCom.Alpha.mask;
                        }
                        aPMs.Add(cPM);
                    }
                    else
                    {
						if (float.MaxValue > _aEffects[nEffectIndex].nSpeed && float.MinValue < _aEffects[nEffectIndex].nSpeed)
                            _aSpeeds.RemoveAt(0);
                        if (null != EffectIsOffScreen)
							EffectIsOffScreen(this, _aEffects[nEffectIndex].cEffect);
                        _aEffects.RemoveAt(nEffectIndex--);
						GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
					}
					//cStopwatch.Stop();
					//(new Logger()).WriteNotice("         ======TIMING: roll: while: nEffectIndex=" + nEffectIndex + " type=" + _aEffects[nEffectIndex].iEffect.eType + " ms=" + cStopwatch.Elapsed.TotalMilliseconds);

                    nEffectIndex++;
                }
				//cSW_while.Stop();
				//(new Logger()).WriteNotice("         ======TIMING: roll: complited while: ms=" + cSW_while.Elapsed.TotalMilliseconds);
            }
			//cSW_lock.Stop();
			//(new Logger()).WriteNotice("         ======TIMING: roll: lock: ms=" + cSW_lock.Elapsed.TotalMilliseconds);
			if (0 < aPMs.Count)
			{
                _cPixelsMap.eAlpha = (bOpacity ? DisCom.Alpha.none : DisCom.Alpha.normal);
				_cPixelsMap.Merge(aPMs);
                cRetVal = _cPixelsMap;
				Baetylus.PixelsMapDispose(aPMs.ToArray());
                //if (true)
                //{
                //    Bitmap bmp;
                //    BitmapData bd;
                //    bmp = new Bitmap((int)stArea.nWidth, stArea.nHeight);
                //    bd = bmp.LockBits(new Rectangle(0, 0, stArea.nWidth, stArea.nHeight), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                //    cRetVal.CopyOut(bd.Scan0);
                //    bmp.UnlockBits(bd);
                //    bmp.Save(@"c:\___TMP\" + (nDebug++).ToString("0000") + ".png");
                //    bmp.Dispose();
                //}
                if (bCUDA && !cRetVal.bCUDA)
				{
					if (null != _cCUDAPixelsMap && stArea != _cCUDAPixelsMap.stArea)
					{
						_cCUDAPixelsMap.Dispose(true);
						_cCUDAPixelsMap = null;
					}
					if (null == _cCUDAPixelsMap)
					{
						_cCUDAPixelsMap = new PixelsMap(true, stArea, PixelsMap.Format.ARGB32);
						_cCUDAPixelsMap.bKeepAlive = true;
					}
					_cCUDAPixelsMap.CopyIn(cRetVal.CopyOut());
					cRetVal = _cCUDAPixelsMap;
				}
			}
			if (0 == _aEffects.Count && bStopOnEmpty && EffectStatus.Stopped != this.eStatus)
				Stop();

			if (null != cRetVal)
				cRetVal.nAlphaConstant = nCurrentOpacity;

            return cRetVal;
        }
        private bool IsEffectOnStartPosition(int nEffectIndex)
        {
            ushort nX = (ushort)_aEffects[nEffectIndex].stPosition.X;
            ushort nY = (ushort)_aEffects[nEffectIndex].stPosition.Y;
            ushort nBW = stArea.nWidth;
            ushort nBH = stArea.nHeight;
            ushort nEW = _aEffects[nEffectIndex].iVideo.stArea.nWidth;
            ushort nEH = _aEffects[nEffectIndex].iVideo.stArea.nHeight;
			if (nY == nBH || nY == -nEH || nX == nBW || nX == -nEW)
                return true;
            else
                return false;
        }
        private bool IsEffectInRollArea(int nEffectIndex)
        {
            Area stEffectArea = ((IVideo)(_aEffects[nEffectIndex].iVideo)).stArea;
			return (_aEffects[nEffectIndex].stPosition.X + stEffectArea.nWidth > 0 && _aEffects[nEffectIndex].stPosition.X < stArea.nWidth &&
				_aEffects[nEffectIndex].stPosition.Y + stEffectArea.nHeight > 0 && _aEffects[nEffectIndex].stPosition.Y < stArea.nHeight);
        }

        //moves effect with defined index on the base area and retuns true if there is a space for a next effect and false if there isn't
        private bool MoveEffect(int nEffectIndex)
        {
			bool bRetVal = false;
			bool bKeyframes = false;
			float nNewPosition = 0;
            float nSpeed;
            if (0 == _aSpeeds.Count)
                nSpeed = _nSpeed;
            else
                nSpeed = _aSpeeds[_aSpeeds.Count - 1];
			bool bSticky = (0 < nEffectIndex && _aEffects[nEffectIndex].bSticky && Keyframe.CurveIsWrong(_aEffects[nEffectIndex].aKeyframes));

			nSpeed = nSpeed / 25; //преобразуем "кол-во пикселей в секунду" в "кол-во пикселей в кадр" 
			(new Logger()).WriteDebug4("[speed_ppf=" + nSpeed + "] [sticky=" + bSticky + "]");

            Area stEffectArea = ((IVideo)_aEffects[nEffectIndex].iVideo).stArea;
			//Point stPositionOriginal = (Point)_aEffects[nEffectIndex].stPosition;



			if (!Keyframe.CurveIsWrong(_aEffects[nEffectIndex].aKeyframes))
			{
				(new Logger()).WriteDebug4("keyframes_count=" + _aEffects[nEffectIndex].aKeyframes.Length);
				bKeyframes = true;
				if (ulong.MaxValue == _aEffects[nEffectIndex].nFrameStartInRoll)
					_aEffects[nEffectIndex].nFrameStartInRoll = nFrameCurrent;
				nNewPosition = Keyframe.CalculateCurvesPoint(_aEffects[nEffectIndex].aKeyframes, (long)nFrameCurrent - (long)_aEffects[nEffectIndex].nFrameStartInRoll + 1);
			}

			_aEffects[nEffectIndex].stPositionPrevious = _aEffects[nEffectIndex].stPosition;

            if (eDirection == Direction.Up)
            {

				if (bSticky)
					_aEffects[nEffectIndex].stPosition.Y = _aEffects[nEffectIndex - 1].stPosition.Y + _aEffects[nEffectIndex - 1].iVideo.stArea.nHeight;
				else if (bKeyframes)
					_aEffects[nEffectIndex].stPosition.Y = nNewPosition;
				else
					_aEffects[nEffectIndex].stPosition.Y -= nSpeed;
				if ((int)(_aEffects[nEffectIndex].stPosition.Y + stEffectArea.nHeight + 1.5) < stArea.nHeight)
					bRetVal = true;
			}
            else if (eDirection == Direction.Down)
            {
				if (bSticky)
					_aEffects[nEffectIndex].stPosition.Y = _aEffects[nEffectIndex - 1].stPosition.Y - _aEffects[nEffectIndex - 1].iVideo.stArea.nHeight;
				else if (bKeyframes)
					_aEffects[nEffectIndex].stPosition.Y = nNewPosition;
				else
					_aEffects[nEffectIndex].stPosition.Y += nSpeed;
				if (_aEffects[nEffectIndex].stPosition.Y > 1)
                    bRetVal = true;
            }
            else if (eDirection == Direction.Right)
            {
				if (bSticky)
					_aEffects[nEffectIndex].stPosition.X = _aEffects[nEffectIndex - 1].stPosition.X - _aEffects[nEffectIndex - 1].iVideo.stArea.nWidth;
				else if (bKeyframes)
					_aEffects[nEffectIndex].stPosition.X = nNewPosition;
				else
					_aEffects[nEffectIndex].stPosition.X += nSpeed;
				if (_aEffects[nEffectIndex].stPosition.X > 1)
					bRetVal = true;
			}
			else if (eDirection == Direction.Left)
			{
				if (bSticky)
					_aEffects[nEffectIndex].stPosition.X = _aEffects[nEffectIndex - 1].stPosition.X + _aEffects[nEffectIndex - 1].iVideo.stArea.nWidth;
				else if (bKeyframes)
					_aEffects[nEffectIndex].stPosition.X = nNewPosition;
				else
					_aEffects[nEffectIndex].stPosition.X -= nSpeed;
				if ((int)(_aEffects[nEffectIndex].stPosition.X + stEffectArea.nWidth + 1.5) < stArea.nWidth)
					bRetVal = true;
			}
			else
				throw new Exception("unexpected error while effect position was changing");//TODO LANG

			return bRetVal;
		}

		#region IContainer implementation
		public event ContainerVideoAudio.EventDelegate EffectAdded;
		public event ContainerVideoAudio.EventDelegate EffectPrepared;
		public event ContainerVideoAudio.EventDelegate EffectStarted;
		public event ContainerVideoAudio.EventDelegate EffectStopped;
		public event ContainerVideoAudio.EventDelegate EffectIsOnScreen;
		public event ContainerVideoAudio.EventDelegate EffectIsOffScreen;
		public event ContainerVideoAudio.EventDelegate EffectFailed;

		#region events processing
		virtual protected void OnEffectAdded(Effect cSender)
		{
			if (null != EffectAdded)
				EffectAdded(this, cSender);
		}
		virtual protected void OnEffectPrepared(Effect cSender)
		{
			if (null != EffectPrepared)
				EffectPrepared(this, cSender);
		}
		virtual protected void OnEffectStarted(Effect cSender)
		{
			if (null != EffectStarted)
				EffectStarted(this, cSender);
		}
		virtual protected void OnEffectStopped(Effect cSender)
		{
			if (null != EffectStopped)
				EffectStopped(this, cSender);
		}
		virtual protected void OnEffectIsOnScreen(Effect cSender)
		{
			if (null != EffectIsOnScreen)
				EffectIsOnScreen(this, cSender);
		}
		virtual protected void OnEffectIsOffScreen(Effect cSender)
		{
			if (null != EffectIsOffScreen)
				EffectIsOffScreen(this, cSender);
		}
		virtual protected void OnEffectFailed(Effect cSender)
		{
			if (null != EffectFailed)
				EffectFailed(this, cSender);
		}
		#endregion

		private void EffectsProcess(Dictionary<IEffect, ContainerAction> ahMoveInfos) //UNDONE
		{
		}
		private void EffectsReorder() //UNDONE
		{
		}

		event ContainerVideoAudio.EventDelegate IContainer.EffectAdded
		{
			add
			{
				this.EffectAdded += value;
			}
			remove
			{
				this.EffectAdded -= value;
			}
		}
		event ContainerVideoAudio.EventDelegate IContainer.EffectPrepared
		{
			add
			{
				this.EffectPrepared += value;
			}
			remove
			{
				this.EffectPrepared -= value;
			}
		}
		event ContainerVideoAudio.EventDelegate IContainer.EffectStarted
		{
			add
			{
				this.EffectStarted += value;
			}
			remove
			{
				this.EffectStarted -= value;
			}
		}
		event ContainerVideoAudio.EventDelegate IContainer.EffectStopped
		{
			add
			{
				this.EffectStopped += value;
			}
			remove
			{
				this.EffectStopped -= value;
			}
		}
		event ContainerVideoAudio.EventDelegate IContainer.EffectIsOnScreen
		{
			add
			{
				this.EffectIsOnScreen += value;
			}
			remove
			{
				this.EffectIsOnScreen -= value;
			}
		}
		event ContainerVideoAudio.EventDelegate IContainer.EffectIsOffScreen
		{
			add
			{
				this.EffectIsOffScreen += value;
			}
			remove
			{
				this.EffectIsOffScreen -= value;
			}
		}
		event ContainerVideoAudio.EventDelegate IContainer.EffectFailed
		{
			add
			{
				this.EffectFailed += value;
			}
			remove
			{
				this.EffectFailed -= value;
			}
		}

        ushort IContainer.nEffectsQty
        {
            get
            {
                return this.nEffectsQty;
            }
        }
		ulong IContainer.nSumDuration
		{
			get
			{
				throw new Exception("the term 'sumduration' is not applicable to roll");
			}
		}
		void IContainer.EffectsProcess(Dictionary<IEffect, ContainerAction> ahMoveInfos)
		{
			this.EffectsProcess(ahMoveInfos);
		}
		void IContainer.EffectsReorder()
		{
			this.EffectsReorder();
		}

		#endregion
	}
}
