using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Drawing.Imaging;
using System.Xml;
using System.Linq;
using helpers.extensions;
using helpers;

namespace BTL.Play
{
	public class Roll : EffectVideo, IContainer //UNDONE
    {
        public class Keyframes: System.Collections.ICollection
        {
            public Keyframe[] Position_X;
            public Keyframe[] Position_Y;
            private bool bAreEmpty
            {
                get
                {
                    return Position_X.IsNullOrEmpty() && Position_Y.IsNullOrEmpty();
                }
            }
            public Keyframes()
            {
                _oSyncRoot = new object();
            }
            public Keyframes(Keyframe[] Position_X, Keyframe[] Position_Y)
                :this()
            {
                this.Position_X = Position_X;
                this.Position_Y = Position_Y;
            }
            public Keyframes(XmlNode cXmlNode)
                 : this()
            {
                Keyframes cRetVal = new Keyframes();
                XmlNode cNode;
                if (null != (cNode = cXmlNode.NodeGet("x", false)))
                    this.Position_X = Keyframe.KeyframeArrayGet(cNode);
                if (null != (cNode = cXmlNode.NodeGet("y", false)))
                    this.Position_Y = Keyframe.KeyframeArrayGet(cNode);
            }
            public PointF PointCalculate(long nFrame)
            {
                PointF stRetVal=new PointF();
                if (Position_X.IsNullOrEmpty())
                    stRetVal.X = float.MaxValue;
                else
                    stRetVal.X = Keyframe.CalculateCurvesPoint(Position_X, nFrame);

                if (Position_Y.IsNullOrEmpty())
                    stRetVal.Y = float.MaxValue;
                else
                    stRetVal.Y = Keyframe.CalculateCurvesPoint(Position_Y, nFrame);
                return stRetVal;
            }
            public override string ToString()
            {
                return "[kf_x=" + (Position_X == null ? "NULL" : "" + Position_X.Length) + "][kf_y=" + (Position_Y == null ? "NULL" : "" + Position_Y.Length) + "]";
            }
            // System.Collections.ICollection
            private object _oSyncRoot;
            public object SyncRoot { get { return _oSyncRoot; } }
            public bool IsSynchronized { get; }
            public int Count  // чтобы работал IsNullOrEmpy
            {
                get
                {
                    if (bAreEmpty)
                        return 0;
                    return 1;
                }
            }
            public void CopyTo(Array array, int index)
            {

            }
            public System.Collections.IEnumerator GetEnumerator()
            {
                return null;
            }
        }
        public class Keyframe
        {
            public struct Point_FramesPixels
			{
				public double nFR;
				public double nPX;
				public Point_FramesPixels(double nXFrames, double nYPixels)
				{
					this.nFR = nXFrames;
					this.nPX = nYPixels;
				}
            }
            public struct Point_TimeFrames
			{
				public double nT;
				public double nFR;
				public Point_TimeFrames(double nXTime, double nYFrames)
				{
					this.nT = nXTime;
					this.nFR = nYFrames;
				}
			}
			public enum Type
			{ 
				calculated,
				hold,
				linear,
				bezier
			}
			private bool _bPreCalculated;
			public float nPosition;
			public long nFrame;
            public double nBesierControlPointCoeff;
            public Point_FramesPixels cBesierControlPoint;  // безьер кейфрейм действует только в паре со вторым кейфреймом.  более 2х кейфреймов не отлажено!!  это точка в сторону которой начинает идти крива¤ (не проход¤ через неЄ)
            public Type eType;
            public Keyframe()
			{
				nPosition = 0;
				nFrame = 0;
				eType = Type.hold;
                nBesierControlPointCoeff = double.MinValue;
            }
			public Keyframe(XmlNode cXmlNode)
				: this()
			{
				eType = cXmlNode.AttributeGet<Type>("type");
				nFrame = cXmlNode.AttributeGet<long>("frame");
				nPosition = cXmlNode.AttributeGet<float>("position");
				cBesierControlPoint = new Point_FramesPixels(cXmlNode.AttributeOrDefaultGet<double>("control_point_frame", 0), cXmlNode.AttributeOrDefaultGet<double>("control_point_position", 0));
                nBesierControlPointCoeff = cXmlNode.AttributeOrDefaultGet<double>("control_point_coeff", double.MinValue);
            }
            static public Keyframe[] KeyframeArrayGet(XmlNode cXmlNode)
			{
				List<Keyframe> aKeyframes = null;
                bool bPreviousWasOddBesier = false;
				if (null != cXmlNode.NodeGet("keyframe", false))
					foreach (XmlNode cNodeChild in cXmlNode.NodesGet("keyframe"))
					{
						if (null == aKeyframes)
							aKeyframes = new List<Keyframe>();
                        aKeyframes.Add(new Keyframe(cNodeChild));
                        if (aKeyframes[aKeyframes.Count() - 1].eType == Type.bezier)
                        {
                            if (bPreviousWasOddBesier)
                            {
                                ControlPointsCalc(aKeyframes[aKeyframes.Count() - 2], aKeyframes[aKeyframes.Count() - 1]);
                                ControlPointsCalc(aKeyframes[aKeyframes.Count() - 1], aKeyframes[aKeyframes.Count() - 2]);
                                bPreviousWasOddBesier = false;
                            }
                            else
                                bPreviousWasOddBesier = true;
                        }
                    }
                return aKeyframes.ToArray();
            }
            static private void ControlPointsCalc(Keyframe cKFTarget, Keyframe cKFPair)
            {
                if (cKFTarget.nBesierControlPointCoeff == double.MinValue)
                    return;
                if (cKFTarget.nBesierControlPointCoeff > 1)
                    cKFTarget.nBesierControlPointCoeff = 1;
                if (cKFTarget.nBesierControlPointCoeff < -1)
                    cKFTarget.nBesierControlPointCoeff = -1;

                if (cKFTarget.nBesierControlPointCoeff >= 0)  // крива¤ "выт¤гиваетс¤" в сторону абсцисс, т.е. кадров, т.е. положение объекта будет медленно мен¤тьс¤ вблизи этой точки
                {
                    cKFTarget.cBesierControlPoint.nFR = cKFTarget.nFrame + (cKFPair.nFrame - cKFTarget.nFrame) * cKFTarget.nBesierControlPointCoeff;
                    cKFTarget.cBesierControlPoint.nPX = cKFTarget.nPosition;
                }
                else // отрицательность условна - это положительный коэф, но крива¤ "выт¤гиваетс¤" в сторону ординат, т.е. пикселей, т.е. положение объекта будет быстро мен¤тьс¤ вблизи этой точки
                {
                    cKFTarget.cBesierControlPoint.nFR = cKFTarget.nFrame;
                    cKFTarget.cBesierControlPoint.nPX = cKFTarget.nPosition + (cKFPair.nPosition - cKFTarget.nPosition) * (-1) * cKFTarget.nBesierControlPointCoeff;
                }
                cKFTarget.nBesierControlPointCoeff = double.MinValue;  
            }
            public Dictionary<long, float> ahBezierRange;

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
						; // в идеале расчитать экстрапол¤цию за границы диапазона (касательна¤ в ту же сторону)
					if (aCurve[aCurve.Length - 1] == aInterval[0])
						; // в идеале расчитать экстрапол¤цию за границы диапазона (касательна¤ в ту же сторону)
					return aInterval[0].nPosition; // но пока просто остаЄмс¤ на этой точке
				}

				float nRetVal = 0;
				switch (GetIntervalType(aInterval[0], aInterval[1])) 
				{
					case Keyframe.Type.hold:
						// формула y=A при M<=x<N  
						nRetVal = aInterval[0].nPosition;
						break;
					case Keyframe.Type.linear:
						// формула y=kx+h, где y-пиксели, x-фреймы. M, N - точки на х; A, B - точки на y, то люба¤ точка (PX, FR) <=  PX=((B-A)/(N-M))*(FR-M)+A
						nRetVal = ((aInterval[1].nPosition - aInterval[0].nPosition) / (aInterval[1].nFrame - aInterval[0].nFrame)) * (nFrame - aInterval[0].nFrame) + aInterval[0].nPosition;
						break;
					case Keyframe.Type.bezier:
						// B(t) = (1-t)^3 P0 + 3(1-t)^2 t P1 + 3(1-t)t^2 P2 + t^3 P3     // P - points in 2d field - frames(x)/pixels(y)     t is abstract 'time'
						// where t is in [0,1], and
						//     1. P0 - first knot point
						//     2. P1 - first control point (close to P0)
						//     3. P2 - second control point (close to P3)
						//     4. P3 - second knot point
						if (null != aInterval[0].ahBezierRange && aInterval[0].ahBezierRange.ContainsKey(nFrame))
							nRetVal = aInterval[0].ahBezierRange[nFrame];
						else
						{
							(new Logger()).WriteError("bezier point was not precalculated! Initial point returned! [current_fr=" + nFrame + "][returned_position=" + aInterval[0].nPosition + "]");
							nRetVal = aInterval[0].nPosition;
						}
						break;
					default:
						break;
				}
				return nRetVal;
			}
			private static Type GetIntervalType(Keyframe cA, Keyframe cB)
			{
				switch (cA.eType)
				{
					case Keyframe.Type.hold:
						return Type.hold;
					case Keyframe.Type.linear:
						return Type.linear;
					case Keyframe.Type.bezier:
						if (cB.eType == Type.bezier)
							return Type.bezier;
						return Type.linear;
					default:
						throw new Exception("GetIntervalType. [point_type=" + cA.eType + "] not realized!");
				}
			}
			private static double MovePointIfItIsOutOfRect(double A, double B, double PToMove)
			{
				double nMin, nMax;
				if (A < B)
				{
					nMin = A;
					nMax = B;
				}
				else
				{
					nMin = B;
					nMax = A;
				}
				if (PToMove < nMin)
					PToMove = nMin;
				if (PToMove > nMax)
					PToMove = nMax;
				return PToMove;
			}
			private static Point_FramesPixels MovePointIfItIsOutOfRect(Point_FramesPixels A, Point_FramesPixels B, Point_FramesPixels PToMove)
			{
				PToMove.nFR = MovePointIfItIsOutOfRect(A.nFR, B.nFR, PToMove.nFR);
				PToMove.nPX = MovePointIfItIsOutOfRect(A.nPX, B.nPX, PToMove.nPX);
				return PToMove;
			}
			private static bool ValueIsInRange(double nA, double nB, double nValue)
			{
				if (nA <= nB && nA <= nValue && nValue <= nB)
					return true;
				if (nB <= nA && nB <= nValue && nValue <= nA)
					return true;
				return false;
			}
			private static double BezierFunction(double P0, double P1, double P2, double P3, double t)
			{
				//(new Logger()).WriteDebug2("BezierFunction [p0=" + P0 + "][p3=" + P3 + "][t=" + t + "]");
				return (1 - t) * (1 - t) * (1 - t) * P0 + 3 * (1 - t) * (1 - t) * t * P1 + 3 * (1 - t) * t * t * P2 + t * t * t * P3;
			}
			private static bool BezierFunctionSearchesT(double P0, double P1, double P2, double P3, Point_TimeFrames cStart, Point_TimeFrames cEnd, double nAsymptoticFrameValue, out Point_TimeFrames cResStart, out Point_TimeFrames cResEnd)
			{
				cResStart = new Point_TimeFrames();
				cResEnd = new Point_TimeFrames();
				if (!ValueIsInRange(cStart.nFR, cEnd.nFR, nAsymptoticFrameValue))
					return false;
				if (Math.Abs(cEnd.nFR - cStart.nFR) < 0.1)
				{
					cResStart = cStart;
					cResEnd = cEnd;
					return true;
				}
				if (Math.Abs(cEnd.nT - cStart.nT) < 0.00001)
				{
					(new Logger()).WriteError("bezier recursive function riched 0.001 range size and can't continue. [p0=" + P0 + "][p1=" + P1 + "][p2=" + P2 + "][p3=" + P3 + "]");
					return false;
				}

				double nBFromT;   // B(t)
				double nT;  // t

				if (Math.Abs(cEnd.nFR - cStart.nFR) <= 4)   // простой случай - половинное деление
				{
					nT = cStart.nT + (cEnd.nT - cStart.nT) / 2;
					nBFromT = BezierFunction(P0, P1, P2, P3, nT);
					if (!ValueIsInRange(cStart.nFR, nBFromT, nAsymptoticFrameValue))
					{
						cStart.nT = nT;
						cStart.nFR = nBFromT;
						nT = cEnd.nT;
						nBFromT = cEnd.nFR;
					}
				}
				else  // сложный случай - перебор отрезков  (на 100 и выше кей-фреймах особенно чувствуетс¤ разница - где-то на 300 вызовов BezierFunction меньше или на 15-20% быстрее - 6ms против 8ms)
				{
					double nDiff;
					nDiff = (cEnd.nT - cStart.nT) / (Math.Abs(cEnd.nFR - cStart.nFR) / 2);
					nT = cStart.nT + nDiff;
					nBFromT = BezierFunction(P0, P1, P2, P3, nT);
					while (!ValueIsInRange(cStart.nFR, nBFromT, nAsymptoticFrameValue))
					{
						cStart.nT = nT;
						cStart.nFR = nBFromT;
						nT = cStart.nT + nDiff;
						if (nT >= cEnd.nT - 0.002)
						{
							nT = cEnd.nT;
							nBFromT = cEnd.nFR;
							break;
						}
						nBFromT = BezierFunction(P0, P1, P2, P3, nT);
					} 
				}
				cEnd.nT = nT;
				cEnd.nFR = nBFromT;
				return BezierFunctionSearchesT(P0, P1, P2, P3, cStart, cEnd, nAsymptoticFrameValue, out cResStart, out cResEnd);
			}
			private static double LinearPositionGet(Point_FramesPixels A, Point_FramesPixels B, double nAsymptoticFrameValue)
			{
				// y=kx+a    k=(BY-AY)/(BX-AX)  a=AY-k*AX
				double k = (B.nPX - A.nPX) / (B.nFR - A.nFR);
				double a = A.nPX - k * A.nFR;
				return k * nAsymptoticFrameValue + a;
			}
			private static Dictionary<long, float> BezierIntervalPreCalculate(Keyframe cKeyP0, Keyframe cKeyP3)
			{
				Dictionary<long, float> aBezier = new Dictionary<long, float>();
				Point_FramesPixels P0, P1, P2, P3;
				P0 = new Point_FramesPixels((double)cKeyP0.nFrame, (double)cKeyP0.nPosition);
				P1 = cKeyP0.cBesierControlPoint;
				P3 = new Point_FramesPixels((double)cKeyP3.nFrame, (double)cKeyP3.nPosition);
				P2 = cKeyP3.cBesierControlPoint;
				P1 = MovePointIfItIsOutOfRect(P0, P3, P1);
				P2 = MovePointIfItIsOutOfRect(P0, P3, P2);
				Point_TimeFrames cResA = new Point_TimeFrames(0, cKeyP0.nFrame);
				Point_TimeFrames cResB = new Point_TimeFrames(1, cKeyP3.nFrame);
				Point_FramesPixels cA, cB;
				for (long nI = cKeyP0.nFrame + 1; nI < cKeyP3.nFrame; nI++)
				{
					//(new Logger()).WriteDebug2("BezierIntervalPreCalculate [frame=" + nI + "]");
					if (BezierFunctionSearchesT(P0.nFR, P1.nFR, P2.nFR, P3.nFR, cResA, new Point_TimeFrames(1, cKeyP3.nFrame), nI, out cResA, out cResB))
					{
						cA.nFR = cResA.nFR;
						cA.nPX = BezierFunction(P0.nPX, P1.nPX, P2.nPX, P3.nPX, cResA.nT);
						cB.nFR = cResB.nFR;
						cB.nPX = BezierFunction(P0.nPX, P1.nPX, P2.nPX, P3.nPX, cResB.nT);
						aBezier.Add(nI, (float)LinearPositionGet(cA, cB, nI));
					}
					else
					{
						(new Logger()).WriteError("bezier point not found [p0_frame=" + P0.nFR + "][p0_pos=" + P0.nPX + "][p3_frame=" + P3.nFR + "][p3_pos=" + P3.nPX + "]");
						aBezier.Add(nI, cKeyP0.nPosition);
					}
				}
				return aBezier;
			}
            public static void BezierPreCalculate(Keyframes cKeyframes)
            {
                if (cKeyframes.IsNullOrEmpty())
                    return;
                BezierPreCalculate(cKeyframes.Position_X);
                BezierPreCalculate(cKeyframes.Position_Y);
            }
            public static void BezierPreCalculate(Keyframe[] aKeyframes)
			{
				if (null == aKeyframes || aKeyframes.Length <= 0 || aKeyframes[0]._bPreCalculated)
					return;

				aKeyframes[0]._bPreCalculated = true;
				for (int nI = 0; nI <= aKeyframes.Length - 2; nI++)
				{
					if (GetIntervalType(aKeyframes[nI], aKeyframes[nI + 1]) == Type.bezier)
						aKeyframes[nI].ahBezierRange = BezierIntervalPreCalculate(aKeyframes[nI], aKeyframes[nI + 1]);
				}
			}
			public static Keyframe[] CopyCurve(Keyframe[] aCurve)
			{
				List<Keyframe> aRetVal = new List<Keyframe>();
				foreach (Keyframe cKF in aCurve)
				{
					aRetVal.Add(new Keyframe() { eType = cKF.eType, cBesierControlPoint = cKF.cBesierControlPoint, nFrame = cKF.nFrame, nPosition = cKF.nPosition });
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
		public class Item
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
			public IVideo iHead;
			public PointF stPosition;
			public PointF stPositionPrevious;
			public bool bMoved;
            public Play.Mask cMask;
			public float nSpeed;
			public bool bSticky;
			public Keyframes cKeyframes;
			public ulong nFrameStartInRoll;
			public bool bEffectWasOnScreen;
            public bool bNoOffScreen;  // не выкидываем по выходу за экран. нужно, если мы знаем, что он потом оп¤ть влетит и т.п.  уйдЄт по дюру в итоге
            public bool bWaitForEmptySpace;
            public bool bNotOccupyEmptySpace;
            public bool bRenderFields;
			public Item(IVideo iVideo, Point stPosition)
            {
                this.iVideo = iVideo;
                this.stPosition = this.stPositionPrevious = stPosition;
                this.nSpeed = float.MaxValue;
				nFrameStartInRoll = ulong.MaxValue;
				cKeyframes = null;
				bWaitForEmptySpace = true;
				bRenderFields = false;
                bNotOccupyEmptySpace = false;
            }
			public Item(IVideo iVideo, Point stPosition, float nSpeed, Keyframes cKeyframes, bool bSticky, bool bWaitForEmptySpace, bool bRenderFields)
				: this(iVideo, stPosition)
            {
				cMask = iVideo.cMask;
                if (null != cMask)
                    bNotOccupyEmptySpace = true;
                this.bWaitForEmptySpace = bWaitForEmptySpace;
                this.nSpeed = nSpeed;
				this.cKeyframes = cKeyframes;
				this.bSticky = bSticky;
				this.bRenderFields = bRenderFields;
			}
        }
        public enum Direction
        {
            Right,
            Left,
            Down,
			Up,
            Static,
			[ObsoleteAttribute("This property is deprecated. Use Down instead.", true)]
			UpToDown = Down,
			[ObsoleteAttribute("This property is deprecated. Use Right instead.", true)]
			LeftToRight = Right,
			[ObsoleteAttribute("This property is deprecated. Use Left instead.", true)]
			RightToLeft = Left,
			[ObsoleteAttribute("This property is deprecated. Use Up instead.", true)]
			DownToUp = Up
        }

		private List<Item> _aEffects;
        private Item _cLastAddedItem;
        private int _nEffectsCount;
        private List<Item> _aEffectsTMP;
        private List<float> _aSpeeds;
        private PixelsMap.Triple _cAdditionalPMDuo;
        private PixelsMap _cPixelsMap;
        private PixelsMap.Triple _cPMDuo;
        private PixelsMap.Triple _cPMDuoExternal;
        private float _nSpeed, _nSpeedNew;
		private ThreadBufferQueue<Bytes> _ahPreRender;
		private bool _bDoAbortWorker;
		private System.Threading.Thread _cThreadFramesGettingWorker;
		private object _oWorkerLock;
		private object _oStopLock;
		private bool _bStopping;
		private int _nQueueSizeMax;
		private ulong _nBytesPrerendered;
		private ulong _nMaxBytesPrerender;
		private ulong _nFrameCurrentPreRender;
		private bool _bPause;
		private bool _bPaused;
        private bool _bPrerender;
        private bool _bStoppedOnEmpty; // чтобы в рамках queue узнать об этом, что не надо больше в очередь класть
        private bool _bHaveSpaceForNextEffect;
        private int _nEffectIndex;
        private byte _nCurrentPMIndex;
        private byte _nPreviousPMIndex;
        private IVideo _iFrNextVideo;
        private Item _cFrNextItem;
        private PixelsMap _cFrNextRetVal;
        private PixelsMap _cFrNextPM;
        private PixelsMap _cFrNextPMMask;
        private List<PixelsMap> _aFrNextPMs;
        private Dictionary<IVideo, PixelsMap> _ahEffect_PM;
        private Dock.Offset _cOffsetAbs;
        private Bytes _aBytes;
        private bool _bPreRenderPartIsOver = false;
        private bool _bTurnOffQueue;
        private List<Bytes> _aGotFrames;
        Microsoft.VisualBasic.Devices.ComputerInfo _CompInfo;
        private MergingMethod? _stMergingMethodAsContainer;
        private bool _bIdleNotSyncEffectOrLate; // сделать stop-idle-prepare-start (true) или задержать эффект на 1-2 кадра пока не будет синхрона с ролом

        public bool bTurnOffDynamicQueue
        {            // way to not use queue in separate thread; recomended for pre-rendered effects
            get
            {
                return _bTurnOffQueue;
            }
            set
            {
                if (eStatus == EffectStatus.Idle)
                    _bTurnOffQueue = value;
                else
                    throw new Exception("changing bTurnOffQueue not on Idle status");
            }
        }
        public uint nPrerenderQueueCount
        {
            get
            {
                return _ahPreRender.nCount;
            }
        }
		public ushort nEffectsQty
        { 
            get { return (ushort)_nEffectsCount; } 
        }
        public Item cLastItem
        {
            get
            {
                lock (_aEffects)
                {
                    return _aEffects.IsNullOrEmpty() ? null : _aEffects[_aEffects.Count - 1];
                }
            }
        }
        public Item cLastItemWithKeyframes
        {
            get
            {
                lock (_aEffects)
                {
                    if (_aEffects.IsNullOrEmpty())
                        return null;
                    for (int nI = _aEffects.Count - 1; nI >= 0; nI--)
                        if (!_aEffects[nI].cKeyframes.IsNullOrEmpty())
                            return _aEffects[nI];
                }
                return null;
            }
        }
        public Direction eDirection { get; set; }
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
		}
		public bool bFullRenderOnPrepare { get; set; }
		public bool bStopOnEmpty { get; set; }  //todo: надобы в контейнер это подн¤ть...  
        public bool bStopOnEmptyQueue { get; set; } // если очередь байт 0 (не эффектов), то мы стопимс¤ (true), либо отдаЄм null (false)
        public int nQueueSizeMax
        {
            get
            {
                return _nQueueSizeMax;
            }
            set
            {
                if (eStatus == EffectStatus.Idle)
                    _nQueueSizeMax = value;
                else
                    throw new Exception("changing nQueueSizeMax not on Idle status");
            }
        }
        public bool bIdleNotSyncEffectOrLate
        {
            get
            {
                return _bIdleNotSyncEffectOrLate;
            }
            set
            {
                _bIdleNotSyncEffectOrLate = value;
            }
        }

        //public delegate void EffectEveryFrameDelayer(string sEffectName);
        //public event EffectEveryFrameDelayer cEffectEveryFrameDelayer;

        public Roll()
            : base(EffectType.Roll)
        {
			try
			{
				_aEffects = new List<Item>();
				_aEffectsTMP = new List<Item>();
				_aSpeeds = new List<float>();
				_aFrNextPMs = new List<PixelsMap>();
				_ahEffect_PM = new Dictionary<IVideo, PixelsMap>();
				eDirection = Direction.Down;
				nSpeed = _nSpeedNew = 1;
				bStopOnEmpty = false;
				bFullRenderOnPrepare = false;
                bStopOnEmptyQueue = true;
                _ahPreRender = new ThreadBufferQueue<Bytes>(uint.MaxValue);
				_nBytesPrerendered = 0;
				_nFrameCurrentPreRender = 0;
				_nMaxBytesPrerender = 2 * 1000 * 1000 * 1000;
				bTurnOffDynamicQueue = true;
				_nQueueSizeMax = Preferences.nQueueAnimationLength;
				_oWorkerLock = new object();
				_oStopLock = new object();
				_bStoppedOnEmpty = false;
                _aGotFrames = new List<Bytes>();
                _CompInfo = null;
                _bIdleNotSyncEffectOrLate = true;
            }
			catch
			{
				Fail();
				throw;
			}
		}
		public Roll(XmlNode cXmlNode)
			: this()
		{
			try
			{
				base.LoadXML(cXmlNode);
                _stMergingMethodAsContainer = new MergingMethod(cXmlNode);
                nSpeed = cXmlNode.AttributeOrDefaultGet<float>("speed", 0);
				bTurnOffDynamicQueue = cXmlNode.AttributeOrDefaultGet<bool>("turn_off_queue", true); 
				eDirection = cXmlNode.AttributeOrDefaultGet<Direction>("direction", Direction.Left);
				bStopOnEmpty = cXmlNode.AttributeOrDefaultGet<bool>("stop_on_empty", false);
				bFullRenderOnPrepare = cXmlNode.AttributeOrDefaultGet<bool>("render_on_prepare", false);
                bStopOnEmptyQueue = cXmlNode.AttributeOrDefaultGet<bool>("stop_on_empty_queue", true);
                _nQueueSizeMax = cXmlNode.AttributeOrDefaultGet<int>("queue_size", Preferences.nQueueAnimationLength);
                XmlNode cNodeChild;
				if (null != (cNodeChild = cXmlNode.NodeGet("effects", false)))
					EffectsAdd(cNodeChild);
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
                Stop();

                Baetylus.PixelsMapDispose(_cPMDuo, true);
                Baetylus.PixelsMapDispose(_cAdditionalPMDuo, true);
                lock (_ahPreRender.oSyncRoot)
                {
                    foreach (Bytes aFrames in _aGotFrames)
                    {
                        Baetylus._cBinM.BytesBack(aFrames, 34);
                    }
                    _aGotFrames.Clear();
                }
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
        }
        #region Effect Add
        public void EffectsAdd(XmlNode cXmlNode)
		{
			foreach (XmlNode cXN in cXmlNode.NodesGet())
				EffectAdd((IVideo)Effect.EffectGet(cXN), cXN);
		}
		public void EffectAdd(IVideo iVideo, XmlNode cXmlNode)
		{
			bool bWaitForEmptySpace = cXmlNode.AttributeOrDefaultGet<bool>("wait_empty_space", false);
			bool bRenderFields = cXmlNode.AttributeOrDefaultGet<bool>("render_fields", false);
            bool bNoOffScreen = cXmlNode.AttributeOrDefaultGet<bool>("no_off_screen", false);
            bool bContainerSize = cXmlNode.AttributeOrDefaultGet<bool>("container_size", false);
			if (bContainerSize)
			{
				if (stArea.nWidth != iVideo.stArea.nWidth || stArea.nHeight != iVideo.stArea.nHeight)
				{
					stArea = new Area(stArea.nLeft, stArea.nTop, iVideo.stArea.nWidth, iVideo.stArea.nHeight);
					stArea = stArea.Dock(stBase, cDock);
                    lock (_aEffects)
                    {
                        foreach (Item cI in _aEffects)
                            cI.iVideo.stBase = stArea;
                    }
				}
			}
            XmlNode cXmlKFs;
            if (null != (cXmlKFs = cXmlNode.NodeGet("keyframes", false)))
			{
                Keyframes cKFs = new Keyframes(cXmlKFs);
                if (cKFs.IsNullOrEmpty())
                {
                    Keyframe[] aKFs = Keyframe.KeyframeArrayGet(cXmlKFs);
                    EffectAdd(iVideo, aKFs, bWaitForEmptySpace, bRenderFields);
                }
                else
                {
                    EffectAdd(iVideo, cKFs, float.MinValue, false, 0, bWaitForEmptySpace, bRenderFields);
                }
			}
			else if (eDirection== Direction.Static)
            {
                EffectAdd(iVideo, null, bWaitForEmptySpace, bRenderFields);
            }
            else
			{
				bool bsteaky = cXmlNode.AttributeOrDefaultGet<bool>("steaky", false);
				if (bsteaky)
					EffectAdd(iVideo, bsteaky);
                else
                    EffectAdd(iVideo, null, cXmlNode.AttributeOrDefaultGet<float>("speed", float.MaxValue), false, 0, bWaitForEmptySpace, bRenderFields);
            }
            _cLastAddedItem.bNoOffScreen = bNoOffScreen;
        }
		public void EffectAdd(IVideo iVideo)
        {
			EffectAdd(iVideo, true);
        }
		public void EffectAdd(IVideo iVideo, bool bSteaky)
		{
			EffectAdd(iVideo, float.MinValue, null, bSteaky);
		}
		public void EffectAdd(IVideo iVideo, Keyframe[] aKeyframes, uint nStartDelay)
		{
			EffectAdd(iVideo, float.MinValue, aKeyframes, false, nStartDelay);
		}
		public void EffectAdd(IVideo iVideo, Keyframe[] aKeyframes, uint nStartDelay, bool bWaitForEmptySpace)
		{
			EffectAdd(iVideo, float.MinValue, aKeyframes, false, nStartDelay, bWaitForEmptySpace);
		}
		public void EffectAdd(IVideo iVideo, Keyframe[] aKeyframes, uint nStartDelay, bool bWaitForEmptySpace, bool bRenderFields)  // 
		{
			EffectAdd(iVideo, float.MinValue, aKeyframes, false, nStartDelay, bWaitForEmptySpace, bRenderFields);
		}
		public void EffectAdd(IVideo iVideo, Keyframe[] aKeyframes, bool bWaitForEmptySpace, bool bRenderFields)   //
		{
			EffectAdd(iVideo, float.MinValue, aKeyframes, false, 0, bWaitForEmptySpace, bRenderFields);
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
			EffectAdd(iVideo, nSpeed, aKeyframes, bSticky, nStartDelay, true, false);
		}
		public void EffectAdd(IVideo iVideo, float nSpeed, Keyframe[] aKeyframes, bool bSticky, uint nStartDelay, bool bWaitForEmptySpace)
		{
			EffectAdd(iVideo, nSpeed, aKeyframes, bSticky, nStartDelay, bWaitForEmptySpace, false);
		}
        public void EffectAdd(IVideo iVideo, float nSpeed, Keyframe[] aKeyframes, bool bSticky, uint nStartDelay, bool bWaitForEmptySpace, bool bRenderFields)
        {
            Keyframes cKFs = null;
            if (!aKeyframes.IsNullOrEmpty())
                switch (eDirection)
                {
                    case Direction.Static:
                        throw new Exception("unknown direction keyframes in static roll");
                    case Direction.Right:
                    case Direction.Left:
                        cKFs = new Keyframes(aKeyframes, null);
                        break;
                    case Direction.Down:
                    case Direction.Up:
                    default:
                        cKFs = new Keyframes(null, aKeyframes);
                        break;
                }
            EffectAdd(iVideo, cKFs, nSpeed, bSticky, nStartDelay, bWaitForEmptySpace, bRenderFields);
        }
        public void EffectAdd(IVideo iVideo, Keyframes cKeyframes, float nSpeed, bool bSticky, uint nStartDelay, bool bWaitForEmptySpace, bool bRenderFields)
        {
            Item cItemTMP;
            IEffect iEffect = (IEffect)iVideo;
            iEffect.Prepared += OnEffectPrepared;
            iEffect.Started += OnEffectStarted;
            iEffect.Stopped += OnEffectStopped;
			iEffect.Failed += OnEffectFailed;
			iEffect.nDelay += nStartDelay;
			Area stEffectArea = ((IVideo)iVideo).stArea;
            Point stEffectPosition = new Point();
            if (stArea.nWidth == 0 && stArea.nHeight == 0)
            {
				Area stAreaNew = stArea;
				stAreaNew.nWidth = stEffectArea.nWidth;
				stAreaNew.nHeight = stEffectArea.nHeight;
				stArea = stAreaNew;
            }
			iVideo.stBase = stArea;

			if (iEffect.eStatus > EffectStatus.Idle && this.stMergingMethod != iVideo.stMergingMethod)
				throw new Exception("некорректна¤ среда вычислений: [roll_cuda=" + stMergingMethod + "][effect_cuda=" + iVideo.stMergingMethod + "]"); //TODO LANG

			iEffect.iContainer = this;
            iVideo.stMergingMethod = this.stMergingMethod;
            lock (_aEffects)
                if (EffectStatus.Idle < eStatus)
                {
                    if (iEffect.eStatus == EffectStatus.Idle)
                        iEffect.Prepare();
                    if (null != iVideo.iMask && ((IEffect)iVideo.iMask).eStatus == EffectStatus.Idle)
                    {
                        ((IEffect)iVideo.iMask).iContainer = this;
                        ((IVideo)iVideo.iMask).stMergingMethod = this.stMergingMethod;
                        ((IEffect)iVideo.iMask).Prepare();
                    }
                }

            Keyframe.BezierPreCalculate(cKeyframes);

            switch (eDirection)   // где ждать очереди или эффект-деле¤
            {
                case Direction.Static:   // всЄ-равно где, лишь бы за кадром
                case Direction.Right:
                    stEffectPosition.X = -stEffectArea.nWidth;
                    stEffectPosition.Y = 0;
                    break;
                case Direction.Left:
                    stEffectPosition.X = stArea.nWidth;
                    stEffectPosition.Y = 0;
                    break;
                case Direction.Down:
                    stEffectPosition.X = 0;
                    stEffectPosition.Y = -stEffectArea.nHeight;
                    break;
                case Direction.Up:
                    stEffectPosition.X = 0;
                    stEffectPosition.Y = stArea.nHeight;
                    break;
            }

            lock (_aEffects) // reorder effects
			{
				//if (null != (cItemTMP = _aEffects.FirstOrDefault(o => o.iVideo == iVideo)))     // теперь можно добавл¤ть один и тот же эффект более 1 раза
				//{
				//	//_aEffects.Remove(cItemTMP);
				//}
                AddEffectInRightOrder(new Item(iVideo, stEffectPosition, nSpeed, cKeyframes, bSticky, bWaitForEmptySpace, bRenderFields));
            }
            (new Logger()).WriteDebug2("roll_effect_add: [effcount=" + _aEffects.Count + "][preren=" + _ahPreRender?.nCount + "][pos=" + stEffectPosition + "] [speed=" + nSpeed + "] [keyframes=" + (null == cKeyframes ? "NULL" : cKeyframes.ToString()) + "] [sticky=" + bSticky + "][file=" + (iVideo is Video ? ((Video)iVideo).sFile : (iVideo is Animation ? ((Animation)iVideo).sFolder : "")) + "]");
            OnEffectAdded((Effect)iVideo);
        }
        private void AddEffectInRightOrder(Item cItem)
        {
            bool bFound = false;
            for (int nI = 0; nI < _aEffects.Count; nI++)    // все маски с одним лаером - ниже не_масок с тем же лаером. пор¤док масок не важен.
            {
                if (!bFound && (_aEffects[nI].cEffect.nLayer > cItem.cEffect.nLayer ||
                                (cItem.cMask != null && _aEffects[nI].cEffect.nLayer == cItem.cEffect.nLayer)   // если нужно будет делать целевую маску, то она должа вставать над эффектом и т¤гатьс¤ в frNext, только если целевой эффект сработал, а потом PM вставить ниже эффекта... 
                                ))                                                                              // целевых может быть несколько дл¤ каждого эффекта и они имеют кейфреймы и т.п.  пока обходимс¤...
                    bFound = true;
                if (bFound)
                {
                    _aEffectsTMP.Add(_aEffects[nI]);
                    _aEffects.Remove(_aEffects[nI]);
                    nI--;
                }
            }
            _aEffects.Add(cItem);
            _cLastAddedItem = cItem;

            foreach (Item nIT in _aEffectsTMP)
                _aEffects.Add(nIT);
            _nEffectsCount = _aEffects.Count;
            _aEffectsTMP.Clear();
        }

        #endregion
        public void SetNotOccupyEmptySpace(IVideo iRecipient, bool bValue) // дл¤ фона в чате и т.п. чтобы не держали свободное место
        {                               // пока сделано так, что те, кто не ждЄт - те и не держат  (bWaitForEmptySpace)  т.е. эта херь пока не работает
            lock (_aEffects)
            {
                Item cRecipient = _aEffects.FirstOrDefault(o => o.cEffect == iRecipient);
                if (null == cRecipient)
                    throw (new Exception("Recipient item not found"));
                cRecipient.bNotOccupyEmptySpace = bValue;
            }
        }
        public void SetHeadEffect(IVideo iRecipient, IVideo iHead)
		{
			lock (_aEffects)
			{
				Item cRecipient = _aEffects.FirstOrDefault(o => o.cEffect == iRecipient);
				Item cFirst = _aEffects.FirstOrDefault(o => o.cEffect == iHead);
				if (null == cRecipient)
					throw (new Exception("Recipient item not found"));
				if (null == cRecipient)
					throw (new Exception("First item not found"));
				cRecipient.iHead = iHead;
			}
		}
        public void SetKeyframesToEffect(IVideo iRecipient, Keyframes cKeyframes)
        {
            lock (_aEffects)
            {
                Item cRecipient = _aEffects.FirstOrDefault(o => o.cEffect == iRecipient);
                if (null == cRecipient)
                    throw (new Exception("Recipient item not found"));
                cRecipient.cKeyframes = cKeyframes;
            }
        }
        public IEffect EffectGet(string sName)
        {
            lock (_aEffects)
            {
                Item cItem = _aEffects.FirstOrDefault(o => o.cEffect.sName == sName);
                return null == cItem ? null : cItem.iEffect;
            }
        }
        public IEffect[] EffectsGet()
        {
            lock (_aEffects)
            {
                return _aEffects.Select(o => o.iEffect).ToArray();
            }
        }
        public Keyframes EffectsKeyframesGet(string sName)
        {
            return EffectsKeyframesGet(EffectGet(sName));
        }
        public Keyframes EffectsKeyframesGet(IEffect iEffect)
        {
            lock (_aEffects)
            {
                Item cItem = _aEffects.FirstOrDefault(o => o.iEffect.nID == iEffect.nID);
                return cItem == null ? null : cItem.cKeyframes;
            }
        }
        public void RemoveEffect(IEffect iEffect)
        {
            lock (_aEffects)
            {
                if (null == iEffect)
                    return;
                Item cItem = _aEffects.FirstOrDefault(o => o.iEffect.nID == iEffect.nID);
                if (null == cItem)
                    return;
                _aEffects.Remove(cItem);
            }
        }
        public void RemoveAllEffects()
        {
            lock (_aEffects)
            {
                foreach (Item cItem in _aEffects)
                    if (cItem.iEffect.eStatus == EffectStatus.Preparing || cItem.iEffect.eStatus == EffectStatus.Running)
                        cItem.iEffect.Stop();
                _aEffects.Clear();
            }
        }
        public void QueuePause()
		{
			if (bTurnOffDynamicQueue || null == _cThreadFramesGettingWorker)
				return;
			_bPause = true;
			while (!_bPaused)
			{
				System.Threading.Thread.Sleep(1);
			}
		}
		public void QueueRelease()
		{
			_bPause = false;
		}

        private System.Threading.Thread _cThreadWritingFramesWorker;  //WF
        private bool _bDoWritingFrames;  //WF
        private int nWriteEveryNFrame, nWriteIndx;  //WF
        private Queue<byte[]> _aqWritingFrames;  //WF
        private Queue<byte[]> _aqWritingAudioFrames;  //WF
        private int _nFrameBufSize;  //WF
        private Area _stFullFrameArea;  //WF
        private void WritingFramesWorker(object cState)  //WF
        {
            (new Logger()).WriteNotice("BTL.WritingFramesWorker: started");

            string _sWritingFramesFile = System.IO.Path.Combine(Preferences.sDebugFolder, "WritingDebugFrames.txt");
            string _sWritingFramesDir = System.IO.Path.Combine(Preferences.sDebugFolder, "ROLL/");
            int _nFramesCount = 0;
            System.Drawing.Bitmap cBFrame;
            System.Drawing.Imaging.BitmapData cFrameBD;
            string[] aLines;
            bool bQueueIsNotEmpty = false;
            byte[] aBytes;
            string sParams;
            while (true)
            {
                try
                {
                    if (System.IO.File.Exists(_sWritingFramesFile))
                    {
                        aLines = System.IO.File.ReadAllLines(_sWritingFramesFile);

                        if (null != (sParams = aLines.FirstOrDefault(o => o.ToLower().StartsWith("roll"))))
                        {
                            _bDoWritingFrames = true;
                            if (!System.IO.Directory.Exists(_sWritingFramesDir))
                                System.IO.Directory.CreateDirectory(_sWritingFramesDir);
                            nWriteEveryNFrame = sParams.Length > 4 ? sParams.Substring(4).ToInt() : 1;
                        }
                        else
                        {
                            _aqWritingFrames.Clear();
                            _bDoWritingFrames = false;
                            if (_aqWritingAudioFrames.Count > 0)
                            {   // на 3 минутки хедер на 48  16 бит литл ендиан стерео - синтезировать его лень было
                                byte[] aHeaderWav = new byte[46] { 0x52, 0x49, 0x46, 0x46, 0x02, 0x92, 0x58, 0x02, 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74, 0x20, 0x12, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x80, 0xBB, 0x00, 0x00, 0x00, 0xEE, 0x02, 0x00, 0x04, 0x00, 0x10, 0x00, 0x00, 0x00, 0x64, 0x61, 0x74, 0x61, 0x00, 0x78, 0x58, 0x02 };
                                System.IO.FileStream stream = new System.IO.FileStream(_sWritingFramesDir + "samples.wav", System.IO.FileMode.Append);
                                stream.Write(aHeaderWav, 0, aHeaderWav.Length);
                                byte[] aBytesTMP;
                                while (0 < _aqWritingAudioFrames.Count)
                                {
                                    aBytesTMP = _aqWritingAudioFrames.Dequeue();
                                    stream.Write(aBytesTMP, 0, aBytesTMP.Length);
                                }
                                stream.Close();
                            }
                        }
                    }
                    else
                        _bDoWritingFrames = false;

                    if (_bDoWritingFrames || 0 < _aqWritingFrames.Count)
                    {
                        while (bQueueIsNotEmpty)
                        {
                            cBFrame = new System.Drawing.Bitmap(_stFullFrameArea.nWidth, _stFullFrameArea.nHeight);
                            cFrameBD = cBFrame.LockBits(new System.Drawing.Rectangle(0, 0, cBFrame.Width, cBFrame.Height), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            lock (_aqWritingFrames)
                            {
                                aBytes = _aqWritingFrames.Dequeue();
                                if (0 < _aqWritingFrames.Count)
                                    bQueueIsNotEmpty = true;
                                else
                                    bQueueIsNotEmpty = false;
                            }
                            System.Runtime.InteropServices.Marshal.Copy(aBytes, 0, cFrameBD.Scan0, (int)_nFrameBufSize);
                            cBFrame.UnlockBits(cFrameBD);
                            cBFrame.Save(_sWritingFramesDir + "frame_" + _nFramesCount.ToString("0000") + ".png");
                            _nFramesCount++;

                            aLines = System.IO.File.ReadAllLines(_sWritingFramesFile);
                            if (null == aLines.FirstOrDefault(o => o.ToLower() == "roll"))
                                _bDoWritingFrames = false;
                            if (3000 < _aqWritingFrames.Count)
                            {
                                _bDoWritingFrames = false;
                                System.IO.File.Delete(_sWritingFramesFile);
                            }
                        }
                        System.Threading.Thread.Sleep(40);
                        if (0 < _aqWritingFrames.Count)
                            bQueueIsNotEmpty = true;
                        else
                            bQueueIsNotEmpty = false;
                    }
                    else
                    {
                        lock (_aqWritingFrames)
                            if (0 == _aqWritingFrames.Count)
                                _nFramesCount = 0;
                        System.Threading.Thread.Sleep(5000);
                    }
                }
                catch (System.Threading.ThreadAbortException)
                { }
                catch (Exception ex)
                {
                    (new Logger()).WriteError(ex);
                }
            }
        }

        public List<Bytes> PreRenderedFramesGet()
        {
            List<Bytes> aRetVal = new List<Bytes>();
            lock (_ahPreRender.oSyncRoot)
            {
                foreach(Bytes aB in _ahPreRender)
                {
                    aRetVal.Add(aB);
                    if (aB == null)
                        continue;
                    if (!_aGotFrames.Contains(aB))
                        _aGotFrames.Add(aB);
                    else
                        (new Logger()).WriteError("this frame is already registered in _ahHashCodes_GotFrames. [frame_hc=" + aB.nID + "]. 1");
                }
            }
            return aRetVal;
        }
        public void ForgetGotFrames(List<Bytes> sFrames)
        {
            lock (_ahPreRender.oSyncRoot)
            {
                foreach (Bytes aB in sFrames)
                {
                    if (aB == null)
                        continue;
                    if (_ahPreRender.Contains(aB))
                    {
                        _ahPreRender.Remove(aB);
                        (new Logger()).WriteError("forgotten frame was removed from _ahPreRender. [fr_hc=" + aB.nID + "]");
                    }
                    if (_aGotFrames.Contains(aB))
                    {
                        _aGotFrames.Remove(aB);
                        Baetylus._cBinM.BytesBack(aB, 33);
                    }
                    else
                        (new Logger()).WriteError("this frame is not registered in _ahHashCodes_GotFrames. maybe you have already done 'ForgetGotFrames' for this [hc=" + aB.nID + "] frame. 4");
                }
            }
        }
        public void ClearPreRenderedQueue()
        {
            _ahPreRender.Clear();
        }
        public void PreRenderedFrameAdd(Bytes aFrame)
        {
            lock (_ahPreRender.oSyncRoot)
            {
                if (aFrame != null && !_aGotFrames.Contains(aFrame))
                    throw new Exception("this frame is not registered in _ahHashCodes_GotFrames. maybe you have already done 'ForgetGotFrames' for this [hc="+ aFrame.nID + "] frame. 1");
                _ahPreRender.Enqueue(aFrame);
            }
        }
        public void PreRenderedFramesAdd(List<Bytes> aFrames)
        {
            lock (_ahPreRender.oSyncRoot)
            {
                foreach (Bytes aB in aFrames)
                {
                    if (aB != null && !_aGotFrames.Contains(aB))
                        throw new Exception("this frame is not registered in _ahHashCodes_GotFrames. maybe you have already done 'ForgetGotFrames' for this [hc=" + aB.nID + "] frame. 2");
                    _ahPreRender.Enqueue(aB);
                }
            }
        }
        public void PreRenderedFramesRegistrationsMoveTo(Roll cTarget, List<Bytes> aFrames)
        {
            lock (_ahPreRender.oSyncRoot)
            {
                List<Bytes> aToMove = new List<Bytes>();
                foreach (Bytes aB in aFrames)
                {
                    if (aB == null)
                        continue;
                    if (aB != null && !_aGotFrames.Contains(aB))
                    {
                        (new Logger()).WriteError("this frame is not registered in _ahHashCodes_GotFrames. maybe you have already done 'ForgetGotFrames' for this [hc=" + aB.nID + "] frame. 3");
                        continue;
                    }
                    _aGotFrames.Remove(aB);
                    aToMove.Add(aB);
                }
                cTarget.PreRenderedFramesRegistrationsAdd(aToMove);
            }
        }
        private void PreRenderedFramesRegistrationsAdd(List<Bytes> aFrames) // must be privete! 
        {
            lock (_ahPreRender.oSyncRoot)
            {
                foreach (Bytes aB in aFrames)
                {
                    if (aB == null)
                        continue;
                    if (_aGotFrames.Contains(aB))
                    {
                        (new Logger()).WriteError("this frame is already registered in _ahHashCodes_GotFrames. [frame_hc=" + aB.nID + "]. 2");
                        continue;
                    }
                    _aGotFrames.Add(aB);
                }
            }
        }

        override public void Prepare()
        {
#if DEBUG
            _aqWritingFrames = new Queue<byte[]>();     //WF
            _aqWritingAudioFrames = new Queue<byte[]>();
            _cThreadWritingFramesWorker = new System.Threading.Thread(WritingFramesWorker);
            _cThreadWritingFramesWorker.IsBackground = true;
            _cThreadWritingFramesWorker.Priority = System.Threading.ThreadPriority.Normal;
            _cThreadWritingFramesWorker.Start();
#endif
            _nCurrentPMIndex = byte.MaxValue;


            try
            {
                if (stMergingMethod.eDeviceType == MergingDevice.DisCom)
                    PixelsMap.DisComInit();

                lock (_aEffects)
                {
                    foreach (Item cItem in _aEffects)
                    {
                        if (EffectStatus.Idle == cItem.iEffect.eStatus)
                        {
                            cItem.iVideo.stMergingMethod = this.stMergingMethod;
                            cItem.iEffect.Prepare();

                            if (null != cItem.iVideo.iMask)
                            {
                                cItem.iVideo.iMask.stMergingMethod = this.stMergingMethod;
                                ((IEffect)cItem.iVideo.iMask).Prepare();
                            }
                        }
                    }
                }

                if (null != _cPMDuo && stArea != _cPMDuo.cFirst.stArea)
                {
                    Baetylus.PixelsMapDispose(_cPMDuo, true);
                    _cPMDuo = null;
                }
                if (null == _cPMDuo)
                {
                    _cPMDuo = new PixelsMap.Triple(stMergingMethod, stArea, PixelsMap.Format.ARGB32, true, Baetylus.PixelsMapDispose);
                    if (1 > _cPMDuo.cFirst.nLength)
                        (new Logger()).WriteNotice("1 > __cPixelsMap.nLength. roll.prepare");
                    _cPMDuo.Allocate();
                }
                _cPMDuo.RenewFirstTime();
                nPixelsMapSyncIndex = byte.MaxValue;

                if (EffectStatus.Idle == eStatus)
				{
					if (!bTurnOffDynamicQueue)
					{
						DynamicQueueStart();
					}
					else if (bFullRenderOnPrepare)
                        PreRender(nDuration < int.MaxValue ? (int)nDuration : int.MaxValue);
                }
				base.Prepare();
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
        }
		public void Prepare(int nFrames)
		{
			if (!bTurnOffDynamicQueue || null != _cThreadFramesGettingWorker)
				throw new Exception("the queue will do this job. turn of the queue or use pause if needed");

			if (eStatus == EffectStatus.Idle)
			{
				bFullRenderOnPrepare = false;
				Prepare();
				bFullRenderOnPrepare = true;
			}
			PreRender(nFrames);
		}
		private void PreRender(int nFrames)
        {
            if (_bPreRenderPartIsOver && eStatus == EffectStatus.Running)
            {
                (new Logger()).WriteError("roll.prerender: attempt to pre-render after bPreRenderPartIsOver = true. stopping.");
                Stop();
            }
            if (eStatus == EffectStatus.Stopped)
                return;

            while (_bPause)
			{
				_bPaused = true;
				System.Threading.Thread.Sleep(1);
			}
			_bPaused = false;
            
            if (null==_CompInfo)
			    _CompInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
            if (_CompInfo.AvailablePhysicalMemory > _nMaxBytesPrerender)  // 2 гига - Ќ« 
            {
                PixelsMap cPM = null;
                Bytes aB = null;

                while (_nBytesPrerendered < _nMaxBytesPrerender)
                {
                    aB = null;
                    lock (_aEffects)
                    {
                        if (_bStopping)
                            return;

                        cPM = FrameNextPreRender(true);




#if DEBUG
                        #region write frames
                        // UNDONE  --  в отдельный класс бы
                        if (null != cPM)
                        {
                            if (_bDoWritingFrames)  //WF
                            {
                                _stFullFrameArea = cPM.stArea;  //WF
                                _nFrameBufSize = _stFullFrameArea.nWidth * _stFullFrameArea.nHeight * 4;  //WF
                                nWriteIndx++;
                                if (nWriteIndx >= nWriteEveryNFrame)
                                {
                                    nWriteIndx = 0;
                                    byte[] aBytes = new byte[_nFrameBufSize];
                                    Array.Copy(cPM.BytesReferenceGet(), aBytes, (int)_nFrameBufSize);
                                    lock (_aqWritingFrames)
                                        _aqWritingFrames.Enqueue(aBytes);
                                }
                            }
                        }
                        #endregion
#endif



                    }

                    if (null == cPM && null != _cThreadFramesGettingWorker) // есть очередь и она должна спать, если нечего делать
                    {
                        _bNotFirstTime = false;
                        _nAverSum = 0;
                        _nAverIndex = 0;
                        System.Threading.Thread.Sleep(20);
                        return;
                    }
                    nFrames -= 1;
                    lock (_ahPreRender.oSyncRoot)  // а то в стопе очистка очереди
                    {
                        if (_bStopping)
                            return;
                        if (null != cPM)
                        {
                            aB = Baetylus._cBinM.BytesGet(cPM.nLength, 30);
                            if (cPM.stMergingMethod.eDeviceType > 0)
                                cPM.CopyOut(aB.aBytes);
                            else
                                Array.Copy(cPM.BytesReferenceGet(), aB.aBytes, aB.Length);
                        }
                        if (eStatus != EffectStatus.Stopped && !_bStoppedOnEmpty)
                        {
                            if (!_bPrerender)
                                _bPrerender = true;

                            if (aB != null && null != _cThreadFramesGettingWorker)
                            {
                                _nAverSum += (int)_ahPreRender.nCount;
                                if (_nAverIndex >= 100)
                                {
                                    (new Logger()).WriteDebug2("roll.prerender. info [average per last " + _nAverIndex + " =" + ((float)_nAverSum) / _nAverIndex + "]");
                                    _nAverIndex = 0;
                                    _nAverSum = 0;
                                }
                                _nAverIndex++;
                                if (_bNotFirstTime && _ahPreRender.nCount < 1)
                                    (new Logger()).WriteError("roll.prerender. nothing in the queue! [frames=" + _nFrameCurrentPreRender + "]");
                                if (!_bNotFirstTime)
                                {
                                    if (nFrames == 0)
                                        for (byte nI = 0; nI < nQueueSizeMax / 3; nI++)
                                            _ahPreRender.Enqueue(null);
                                    _bNotFirstTime = true;
                                }
                            }

                            _ahPreRender.Enqueue(aB);  // null можно добавл¤ть   «десь спим, но oSyncRoot свободен будет
                        }
                    }
                    if (null == _cThreadFramesGettingWorker && aB != null)  // иначе за размером следит очередь - nQueueSize
                        _nBytesPrerendered = (ulong)(aB.Length * _ahPreRender.nCount);
                    if (nFrames <= 0 || _bStoppedOnEmpty)
                        break;
                }
                if (_nBytesPrerendered >= _nMaxBytesPrerender)
                    (new Logger()).WriteError("roll.prerender. end of prerender - max_bytes_prerendered limit reached!! [size=" + _nBytesPrerendered + "][frames=" + _nFrameCurrentPreRender + "][available_memory=" + _CompInfo.AvailablePhysicalMemory + "]");
            }
            else
            {
                (new Logger()).WriteError("there is not enough memory for prerendering roll effect! [roll x=" + stArea.nLeft + "; y=" + stArea.nTop + "; w=" + stArea.nWidth + "]");
                if (eStatus == EffectStatus.Running || eStatus == EffectStatus.Preparing)
                    Stop();
            }
		}
        private bool _bNotFirstTime;
        private int _nAverSum;
        private int _nAverIndex;

        public override void Start()
		{
			base.Start();
		}
		override public void Stop()
        {
			lock (_oStopLock)
			{
				if (_bStopping)
					return;
				_bStopping = true;
			}
			try
			{
				_bDoAbortWorker = true;
				lock (_ahPreRender.oSyncRoot)
				{
					Bytes aB;
					while (0 < _ahPreRender.nCount)
					{
						if (null != (aB = _ahPreRender.Dequeue()) && !_aGotFrames.Contains(aB))
							Baetylus._cBinM.BytesBack(aB, 31);
					}
                    _ahPreRender.Clear();
                }
                lock (_aEffects)
                {
                    Baetylus.PixelsMapDispose(_cPMDuo, true);
                    //_cPMDuo = null;
                    Baetylus.PixelsMapDispose(_cAdditionalPMDuo, true);
                    //_cAdditionalPMDuo = null;
                }
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
            }
            //if (eStatus != EffectStatus.Idle)
            base.Stop();
        }
        public override void Idle()
        {
            base.Idle();
            _bPreRenderPartIsOver = false;
            _bStopping = false;
        }

        //System.Diagnostics.Stopwatch cStopwatch = null;
        //System.Diagnostics.Stopwatch cSW_while = null;
        //System.Diagnostics.Stopwatch cSW_lock = null;

        public void DynamicQueueStart()
		{
			(new Logger()).WriteNotice("dynamic queue try to start: [area=" + stArea + "]");
			lock (_oWorkerLock)
			{
				if (0 < _ahPreRender.nCount)
				{
					(new Logger()).WriteWarning("_ahPreRender is not empty!!! DynamicQueueStart aborted");
					return;
				}
				if (null != _cThreadFramesGettingWorker)
				{
					(new Logger()).WriteWarning("worker has already started!!! DynamicQueueStart aborted");
					return;
				}
				_bDoAbortWorker = false;
				_ahPreRender.BufferLengthSet((uint)_nQueueSizeMax);
				_cThreadFramesGettingWorker = new System.Threading.Thread(FramesGettingWorker);
				_cThreadFramesGettingWorker.IsBackground = true;
				_cThreadFramesGettingWorker.Priority = System.Threading.ThreadPriority.Normal;
				_cThreadFramesGettingWorker.Start();

				while (_ahPreRender.nCount < _nQueueSizeMax && _nEffectsCount > 0)
				{
					if (_ahPreRender.nCount > 5)  // быстрее освобождать prepare, как в ffmpeg
						break;
					System.Threading.Thread.Sleep(1);
				}
			}
		}
		private void FramesGettingWorker(object cState)
		{
			try
			{
				//Logger.Timings cTimings = new helpers.Logger.Timings("animation:FramesGettingWorker");
				(new Logger()).WriteNotice("dynamic queue started: [area=" + stArea + "]");
				while (!_bDoAbortWorker)
				{ 
					PreRender(1); // здесь уснЄт если очередь >= nQueueSize
					if (_nFrameCurrentPreRender >= nDuration || _bStoppedOnEmpty)
						break;
					System.Threading.Thread.Sleep(2);
				}
				(new Logger()).WriteNotice("dynamic queue stopped: [area=" + stArea + "][pre_render_count=" + _ahPreRender.nCount + "]");
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
			finally
			{
				_bDoAbortWorker = true;
				(new Logger()).WriteNotice("dynamic queue stopped finally: [area=" + stArea + "]");
			}
		}

		override public PixelsMap FrameNext()
		{
			base.FrameNext();
			PixelsMap cRetVal = null;

			if (!_bPreRenderPartIsOver) // 
				lock (_ahPreRender.oSyncRoot)
				{
                    if (_bStopping)
                        return null;
                    if (_ahPreRender.nCount > 0) // есть пререндер или включена очередь
					{
						_aBytes = _ahPreRender.Dequeue();
						if (null != _aBytes)  // а если нулл -  если хотели, чтобы ничего не было некоторое врем¤... (если это фича)
                        {
                            _cAdditionalPMDuo = ForContainerPMGet();
                            cRetVal = _cAdditionalPMDuo.Switch(nPixelsMapSyncIndex);
                            if (null == cRetVal) return null;

                            cRetVal.CopyIn(_aBytes.aBytes); // этот копиин просто тер¤ет наши байты
                            if (!_aGotFrames.Contains(_aBytes))
                                Baetylus._cBinM.BytesBack(_aBytes, 32);
                            cRetVal.nAlphaConstant = nCurrentOpacity;
                            if (null != cMask)
                                cRetVal.eAlpha = cMask.eMaskType;
                        }
                    }
					else     // кончилась пререндерна¤ часть  или очередь черпанула, т.е. чату, например, просто нечего показывать
					{
                        if (bStopOnEmptyQueue)  // что-то пошло не так
                        {
                            (new Logger()).WriteWarning("roll's queue is empty - stopping [area=" + stArea.nWidth + ", " + stArea.nHeight + "][bStopOnEmptyQueue=" + bStopOnEmptyQueue + "][_bStoppedOnEmpty=" + _bStoppedOnEmpty + "][frcurrent=" + nFrameCurrent + "][dur=" + nDuration + "][layer=" + nLayer + "]");
                            _bPreRenderPartIsOver = true;
                            _bStoppedOnEmpty = true;
                            //return null; // внизу
                        }
                        else // чат, например
                        {
                            //(new Logger()).WriteDebug2("roll's queue is empty - returning null [area=" + stArea.nWidth + ", " + stArea.nHeight + "][bStopOnEmptyQueue=" + bStopOnEmptyQueue + "][_bStoppedOnEmpty=" + _bStoppedOnEmpty + "][frcurrent=" + nFrameCurrent + "][dur=" + nDuration + "][layer=" + nLayer + "]"); // на чате лупит всЄ врем¤
                            //return null;  но надо внизу проверить услови¤ стопа
                        }

                        //                  if (bTurnOffDynamicQueue)    // был обычный пре-рендер    //  отключена пока, хот¤ и отлажена - не надЄжно. 
                        //{
                        //	if (!_bPreRenderPartIsOver)
                        //	{
                        //                          _bPreRenderPartIsOver = true;
                        //                          //if (_bPrerender)  // теперь только пререндер должен быть. 
                        //                          (new Logger()).WriteError("prerender part of the roll is over - starting live FrameNext. [area=" + stArea.nWidth + ", " + stArea.nHeight + "][_bStoppedOnEmpty=" + _bStoppedOnEmpty + "][frcurrent=" + nFrameCurrent + "][dur=" + nDuration + "][layer=" + nLayer + "]");
                        //                      }
                        //                  }
                        //else // нон-стоп ролл, типа чата и там нечего показывать пока
                        //{
                        //	//return null;
                        //}
                    }
                }

			// не надо элза     //  отключена пока, хот¤ и отлажена - не надЄжно. 
			if (false        && _bPreRenderPartIsOver && !_bStoppedOnEmpty) // нету ни пререндера, ни очереди, но эффекты еще есть - можно продолжать брать кадры
			{
                lock (_aEffects)
                {
                    if (_bStopping)
                        return null;
                    cRetVal = _cPixelsMap = FrameNextPreRender(false);  // отдаст всегда __cPixelsMap (т.е. один из _cPMDuo), т.к. тут нет очереди (false)

                    if (null != _cPixelsMap && stMergingMethod != iContainer?.stMergingMethod.Value && null != (_cAdditionalPMDuo = ForContainerPMGet()))  // разные куды у нас и родител¤
                    {
                        cRetVal = _cAdditionalPMDuo.Switch(nPixelsMapSyncIndex);  // фактически здесь _cPMDExternal === _cAdditionalPMDuo
                        if (null == cRetVal) return null;

                        if (cRetVal.stMergingMethod.eDeviceType > 0) // если внешний PM не диском
                        {
                            if (_cPixelsMap.stMergingMethod.eDeviceType > 0) // если PM рола тоже не диском
                            {
                                Bytes aB = Baetylus._cBinM.BytesGet(_cPixelsMap.nLength, 31);
                                _cPixelsMap.CopyOut(aB.aBytes);
                                cRetVal.CopyIn(aB.aBytes);
                                Baetylus._cBinM.BytesBack(aB, 35);
                                // TODO передать напр¤мую из устройства в устройство
                            }
                            else
                                cRetVal.CopyIn(_cPixelsMap.BytesReferenceGet());
                        }
                        else // если внешний диском, а у рола - нет
                            _cPixelsMap.CopyOut(cRetVal.BytesReferenceGet());
                    }

                    if (null != cRetVal)
                    {
                        cRetVal.nAlphaConstant = nCurrentOpacity;
                        if (null != cMask)
                            cRetVal.eAlpha = cMask.eMaskType;
                    }
                }
			}
			if (_bPreRenderPartIsOver && _bStoppedOnEmpty || nFrameCurrent >= nDuration)  // отдаст этот кадр последний (или null) и всЄ ... (и получаем регул¤рно pixelmap.id==0 error в baetylus :=) )
				Stop();

			return cRetVal;
		}
		private PixelsMap FrameNextPreRender(bool bRenderForQueue)
		{
			_cFrNextRetVal = null;
			_iFrNextVideo = null;
            _cFrNextItem = null;
            _nFrameCurrentPreRender++;

			_nSpeed = _nSpeedNew;
			_bHaveSpaceForNextEffect = true;
			_nEffectIndex = 0;
            _cFrNextPM = null;
            _aFrNextPMs.Clear();
            _ahEffect_PM.Clear();

            _nPreviousPMIndex = _nCurrentPMIndex;

            if (bRenderForQueue) // не важна стыковка с кудой Ѕ“Ћ, но важно, чтобы был наш пор¤док (а не 255, что приведЄт-таки к стыковке и к дЄрганию 0,0,1,1,1,2,2,0 и т.п.)
            {
                if (stMergingMethod.eDeviceType == MergingDevice.CUDA)
                {
                    if (_nCurrentPMIndex == byte.MaxValue)
                        _nCurrentPMIndex = 0;
                    else
                        _nCurrentPMIndex = PixelsMap.Triple.GetNextIndex(_nCurrentPMIndex);
                }
                else
                    _nCurrentPMIndex = byte.MaxValue;
            }
            else // даЄм кадр в синхроне с внешним контейнером
            {
                if (stMergingMethod.eDeviceType == MergingDevice.CUDA)  // и у нас куда 
                {
                    if (nPixelsMapSyncIndex < byte.MaxValue)   // и контейнер нам нав¤зал номер триплы (а значит он тоже CUDA)
                        _nCurrentPMIndex = nPixelsMapSyncIndex;
                    else
                    {
                        if (_nCurrentPMIndex == byte.MaxValue)
                            _nCurrentPMIndex = 0;
                        else
                            _nCurrentPMIndex = PixelsMap.Triple.GetNextIndex(_nCurrentPMIndex);
                    }
                }
                else
                    _nCurrentPMIndex = byte.MaxValue;
            }

            _cFrNextRetVal = _cPMDuo.Switch(_nCurrentPMIndex);


            //cSW_while = System.Diagnostics.Stopwatch.StartNew();
            while (_nEffectIndex < _aEffects.Count)
			{
                _cFrNextItem = _aEffects[_nEffectIndex];
                if (!_bHaveSpaceForNextEffect && _cFrNextItem.bWaitForEmptySpace)
				{
					_nEffectIndex++;
					continue;
				}
				//cStopwatch = System.Diagnostics.Stopwatch.StartNew();
				if (_cFrNextItem.iHead != null)
				{
					if (((Effect)_cFrNextItem.iHead).nFrameCurrent == 0)
					{
						_nEffectIndex++;
						continue;
					}
					_cFrNextItem.iHead = null;
				}
				if (_cFrNextItem.iEffect.nDelay > 0)
				{
					_cFrNextItem.iEffect.nDelay--;
					if (_cFrNextItem.bWaitForEmptySpace)
                        _bHaveSpaceForNextEffect = false;  //_cFrNextItem.bNotOccupyEmptySpace ? true :
                   _nEffectIndex++;
					continue;
				}

				if (!_cFrNextItem.bMoved)
				{
					if (float.MaxValue > _cFrNextItem.nSpeed && float.MinValue < _cFrNextItem.nSpeed)
						_aSpeeds.Add(_cFrNextItem.nSpeed);
					_cFrNextItem.bMoved = true;
				}

				if (_cFrNextItem.bWaitForEmptySpace)
					_bHaveSpaceForNextEffect = MoveEffect(_nEffectIndex);  //_cFrNextItem.bNotOccupyEmptySpace ? true :
                else
					MoveEffect(_nEffectIndex);

				if (IsEffectInRollArea(_nEffectIndex) && (EffectStatus.Stopped != _cFrNextItem.cEffect.eStatus || _ahEffect_PM.ContainsKey(_cFrNextItem.iVideo)))
				{
					if (!_cFrNextItem.bEffectWasOnScreen)
					{
						_cFrNextItem.bEffectWasOnScreen = true;
                        OnEffectIsOnScreen((Effect)_cFrNextItem.iVideo);
					}
					if (EffectStatus.Preparing == _cFrNextItem.iEffect.eStatus)
						_cFrNextItem.iEffect.Start(null);
                    if (EffectStatus.Running != _cFrNextItem.iEffect.eStatus)
                        throw new Exception("Ёффект не может быть не Prepare и не Running в ROLL.FrameNext() [layer=" + _cFrNextItem.cEffect.nLayer + "]");

                    _iFrNextVideo = _cFrNextItem.iVideo;
                    if (!_ahEffect_PM.ContainsKey(_iFrNextVideo))
                    {
                        _iFrNextVideo.nPixelsMapSyncIndex = _nCurrentPMIndex;
                        _ahEffect_PM.Add(_iFrNextVideo, _iFrNextVideo.FrameNext());
                    }
                    _cFrNextPM = _ahEffect_PM[_iFrNextVideo];  // дл¤ оптимизации можно юзать один эффект и дл¤ плашки и дл¤ маски и т.п. а FrNext дЄргаем один раз на кадр
                    if (null == _cFrNextPM)
					{
						(new Logger()).WriteNotice("Got a null pixelsmap from Effect.FrameNext() in roll [layer=" + _cFrNextItem.cEffect.nLayer + "]");
						_nEffectIndex++;
						continue;
					}
                    _cFrNextPM.eAlpha = _iFrNextVideo.cMask == null ? DisCom.Alpha.normal : _iFrNextVideo.cMask.eMaskType;

                    short nLeftNewPos = (short)Math.Floor(_cFrNextItem.stPosition.X + _cFrNextPM.stArea.nLeft);     //_iFrNextVideo.stArea.nLeft  в эффектах fr next надо каждый раз starea возвращать в PM. см Anim
                    short nTopNewPos = (short)Math.Floor(_cFrNextItem.stPosition.Y + _cFrNextPM.stArea.nTop);      //_iFrNextVideo.stArea.nTop

                    _cFrNextPM.Move((short)(nLeftNewPos), (short)(nTopNewPos));

					_cOffsetAbs = !_cFrNextItem.bRenderFields || _cFrNextItem.stPosition.X == _cFrNextItem.stPositionPrevious.X ? null : ((IVideo)_cFrNextItem.cEffect).OffsetAbsoluteGet();
					_cFrNextPM.Shift(_cFrNextItem.stPosition, _cOffsetAbs, _cFrNextItem.stPositionPrevious);

                    if (null != _iFrNextVideo.iMask && ((IEffect)_iFrNextVideo.iMask).eStatus != EffectStatus.Stopped) // если маска одна и та же на разных сло¤х, то дЄргать надо только один раз на кадр!
					{
                        if (!_ahEffect_PM.ContainsKey(_iFrNextVideo.iMask))
                        {
                            if (((IEffect)_iFrNextVideo.iMask).eStatus == EffectStatus.Preparing || ((IEffect)_iFrNextVideo.iMask).eStatus == EffectStatus.Idle)
                                ((IEffect)_iFrNextVideo.iMask).Start(null);
                            _iFrNextVideo.iMask.nPixelsMapSyncIndex = _nCurrentPMIndex;
                            _ahEffect_PM.Add(_iFrNextVideo.iMask, _iFrNextVideo.iMask.FrameNext());
                        }
                        _cFrNextPMMask = _ahEffect_PM[_iFrNextVideo.iMask];
                        if (null != _cFrNextPMMask)
                        {
                            _aFrNextPMs.Add(_cFrNextPMMask);
                            _aFrNextPMs[_aFrNextPMs.Count - 1].eAlpha = _iFrNextVideo.iMask.cMask == null ? DisCom.Alpha.mask : _iFrNextVideo.iMask.cMask.eMaskType;
                        }
                    }
					_aFrNextPMs.Add(_cFrNextPM);
				}
				else if (_cFrNextItem.bEffectWasOnScreen && !_cFrNextItem.bNoOffScreen || EffectStatus.Stopped == _cFrNextItem.cEffect.eStatus)
				{
					if (float.MaxValue > _cFrNextItem.nSpeed && float.MinValue < _cFrNextItem.nSpeed)
						_aSpeeds.RemoveAt(0);

					OnEffectIsOffScreen(_cFrNextItem.cEffect);
					_aEffects.RemoveAt(_nEffectIndex--);
                    _nEffectsCount = _aEffects.Count;
                }
				//cStopwatch.Stop();
				//(new Logger()).WriteNotice("         ======TIMING: roll: while: nEffectIndex=" + nEffectIndex + " type=" + _aEffects[nEffectIndex].iEffect.eType + " ms=" + cStopwatch.Elapsed.TotalMilliseconds);

				_nEffectIndex++;
			}
            //cSW_while.Stop();
            //(new Logger()).WriteNotice("         ======TIMING: roll: complited while: ms=" + cSW_while.Elapsed.TotalMilliseconds);

            if (_aEffects.Count <= 0 && bStopOnEmpty)
                _bStoppedOnEmpty = true;  // дл¤ очереди инфа на стоп

            if (0 < _aFrNextPMs.Count)
            {
                if (null == _cFrNextRetVal)
                {
                    _cFrNextRetVal = new PixelsMap(stMergingMethod, stArea, PixelsMap.Format.ARGB32);
                    _cFrNextRetVal.bKeepAlive = false; // на одни раз
                    _cFrNextRetVal.nIndexTriple = _nCurrentPMIndex;
                    _cFrNextRetVal.Allocate();
                }
                _cFrNextRetVal.eAlpha = (bOpacity ? DisCom.Alpha.none : DisCom.Alpha.normal);
                _cFrNextRetVal.Merge(_aFrNextPMs);
                Baetylus.PixelsMapDispose(_aFrNextPMs.ToArray());
                return _cFrNextRetVal;
            }
            else
                return null;
        }
        private PixelsMap.Triple ForContainerPMGet()  // returns pm for parent usage; __cPixelsMap - our internal roll pm
        {
            if (true || _cPMDuo.cFirst.stMergingMethod != iContainer.stMergingMethod.Value)  //!__cPixelsMap.bCUDA && iContainer.bCUDA.Value     // куда родител¤ != куде рола  // куда теперь - это ћћ
            {// всегда берЄм еще PM, если сюда обратились, значит надо
                if (null != _cAdditionalPMDuo && stArea != _cAdditionalPMDuo.cFirst.stArea)
                {
                    Baetylus.PixelsMapDispose(_cAdditionalPMDuo, true);
                    _cAdditionalPMDuo = null;
                }
                if (null == _cAdditionalPMDuo)
                {
                    _cAdditionalPMDuo = new PixelsMap.Triple(iContainer.stMergingMethod.Value, stArea, PixelsMap.Format.ARGB32, true, Baetylus.PixelsMapDispose);
                    _cAdditionalPMDuo.Allocate();
                    _cAdditionalPMDuo.RenewFirstTime();
                }
                return _cAdditionalPMDuo;
            }
            return null;
            //return _cPMDuo;
        }
        private bool IsEffectOnStartPosition(int nEffectIndex)
        {
            // function is not used. if need - think about locking _aEffects !!!
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
			bool bFirstTime = false;
			bool bRetVal = false;
			bool bKeyframes = false;
            PointF stNewPosition = new PointF();
            float nSpeed;
            if (0 == _aSpeeds.Count)
                nSpeed = _nSpeed;
            else
                nSpeed = _aSpeeds[_aSpeeds.Count - 1];
			bool bSticky = (0 < nEffectIndex && _aEffects[nEffectIndex].bSticky && _aEffects[nEffectIndex].cKeyframes.IsNullOrEmpty());

            nSpeed = nSpeed / 25; //преобразуем "кол-во пикселей в секунду" в "кол-во пикселей в кадр" 
			(new Logger()).WriteDebug4("[speed_ppf=" + nSpeed + "] [sticky=" + bSticky + "]");

            Area stEffectArea = ((IVideo)_aEffects[nEffectIndex].iVideo).stArea;
			//Point stPositionOriginal = (Point)_aEffects[nEffectIndex].stPosition;



			if (!_aEffects[nEffectIndex].cKeyframes.IsNullOrEmpty())
			{
				(new Logger()).WriteDebug4("keyframes_count=" + _aEffects[nEffectIndex].cKeyframes.ToString());
				bKeyframes = true;
				if (ulong.MaxValue == _aEffects[nEffectIndex].nFrameStartInRoll)
				{
					bFirstTime = true;
					_aEffects[nEffectIndex].nFrameStartInRoll = _nFrameCurrentPreRender;
                }
                stNewPosition = _aEffects[nEffectIndex].cKeyframes.PointCalculate((long)_nFrameCurrentPreRender - (long)_aEffects[nEffectIndex].nFrameStartInRoll + 1);
                if (bFirstTime)
				{
                    _aEffects[nEffectIndex].stPosition.X = 0;
                    _aEffects[nEffectIndex].stPosition.Y = 0;
                    if (stNewPosition.X < float.MaxValue)
                        _aEffects[nEffectIndex].stPosition.X = stNewPosition.X;
                    if (stNewPosition.Y < float.MaxValue)
                        _aEffects[nEffectIndex].stPosition.Y = stNewPosition.Y;
                }
            }

			_aEffects[nEffectIndex].stPositionPrevious = _aEffects[nEffectIndex].stPosition;

            if (bKeyframes)
            {
                if (stNewPosition.X < float.MaxValue)
                    _aEffects[nEffectIndex].stPosition.X = stNewPosition.X;
                if (stNewPosition.Y < float.MaxValue)
                    _aEffects[nEffectIndex].stPosition.Y = stNewPosition.Y;
            }
            else
            {
                switch (eDirection)
                {
                    case Direction.Static:
                        _aEffects[nEffectIndex].stPosition.X = 0;
                        _aEffects[nEffectIndex].stPosition.Y = 0;
                        break;
                    case Direction.Right:
                        if (bSticky)
                            _aEffects[nEffectIndex].stPosition.X = _aEffects[nEffectIndex - 1].stPosition.X - _aEffects[nEffectIndex - 1].iVideo.stArea.nWidth;
                        else
                            _aEffects[nEffectIndex].stPosition.X += nSpeed;
                        break;
                    case Direction.Left:
                        if (bSticky)
                            _aEffects[nEffectIndex].stPosition.X = _aEffects[nEffectIndex - 1].stPosition.X + _aEffects[nEffectIndex - 1].iVideo.stArea.nWidth;
                        else
                            _aEffects[nEffectIndex].stPosition.X -= nSpeed;
                        break;
                    case Direction.Down:
                        if (bSticky)
                            _aEffects[nEffectIndex].stPosition.Y = _aEffects[nEffectIndex - 1].stPosition.Y - _aEffects[nEffectIndex - 1].iVideo.stArea.nHeight;
                        else
                            _aEffects[nEffectIndex].stPosition.Y += nSpeed;
                        break;
                    case Direction.Up:
                        if (bSticky)
                            _aEffects[nEffectIndex].stPosition.Y = _aEffects[nEffectIndex - 1].stPosition.Y + _aEffects[nEffectIndex - 1].iVideo.stArea.nHeight;
                        else
                            _aEffects[nEffectIndex].stPosition.Y -= nSpeed;
                        break;
                    default:
                        throw new Exception("unknown Direction [direction=" + eDirection + "]");//TODO LANG
                }
            }

            switch (eDirection)
            {
                case Direction.Static:
                    bRetVal = true;
                    break;
                case Direction.Right:
                    if (_aEffects[nEffectIndex].stPosition.X > 1)
                        bRetVal = true;
                    break;
                case Direction.Left:
                    if ((int)(_aEffects[nEffectIndex].stPosition.X + stEffectArea.nWidth + 1.5) < stArea.nWidth)
                        bRetVal = true;
                    break;
                case Direction.Down:
                    if (_aEffects[nEffectIndex].stPosition.Y > 1)
                        bRetVal = true;
                    break;
                case Direction.Up:
                    if ((int)(_aEffects[nEffectIndex].stPosition.Y + stEffectArea.nHeight + 1.5) < stArea.nHeight)
                        bRetVal = true;
                    break;
            }

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
        MergingMethod? IContainer.stMergingMethod  // not currently use
        {
            get
            {
                return _stMergingMethodAsContainer; // Preferences.stMerging;
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
