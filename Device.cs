using System;
using System.Collections.Generic;
using System.Text;

using System.Runtime.InteropServices;
using DeckLinkAPI;
using helpers;
using System.Linq;

// отладка
using System.Drawing;
using System.Drawing.Imaging;           // отладка
// отладка

namespace BTL
{
	abstract public class Device : IDevice
	{
		class Logger : helpers.Logger
		{
			public Logger()
				: base("device", "device[" + System.Diagnostics.Process.GetCurrentProcess().Id + "]")
			{ }
		}
		public class Frame
		{
			public class Audio
			{
				public Audio()
				{
					nID = nIDs++;
				}
				static private ulong nIDs = 0;
				public ulong nID;
				public byte[] aFrameBytes;
			}
			public class Video
			{
				public Video()
				{
					nID = nIDs++;
				}
				static private ulong nIDs = 0;
				public ulong nID;
				public object oFrameBytes;
				public IntPtr pFrameBytes
				{
					get
					{
						if (null == oFrameBytes)
							return IntPtr.Zero;
						if (oFrameBytes is IntPtr)
							return (IntPtr)oFrameBytes;
						throw new Exception("unexpected video frame buffer type");
					}
				}
				public byte[] aFrameBytes
				{
					get
					{
						if (null == oFrameBytes)
							return null;
						if (oFrameBytes is byte[])
							return (byte[])oFrameBytes;
						throw new Exception("unexpected video frame buffer type");
					}
				}
				private object oSyncRoot = new object();
				private int _nReferences = 0;
				public int nReferences
				{
					get
					{
						lock (oSyncRoot)
						{
							if (bFreeze && 1 > nRefTotal && DateTime.Now > dtRefEnd)
							{
								bFreeze = false;
								dtRefEnd = DateTime.MaxValue;
								(new Logger()).WriteNotice("there is freeze [" + nFreezeIndx + "] in the air!!!   __END__ (3 seconds ago)");
								nFreezeIndx++;
							}
							return _nReferences;
						}
					}
					set
					{
						lock (oSyncRoot)
						{
							nRefTotal += value - _nReferences;
							if (0 < nRefTotal)
							{
								if (0 < Baetylus.nVideoBufferCount && DateTime.MaxValue > dtRefEnd)
									(new Logger()).WriteNotice("there is freeze [" + nFreezeIndx + "] in the air!!!   __CONTINUE__  [nRefTotal=" + nRefTotal + "][_nReferences=" + _nReferences + "][new_value=" + value + "]"); // временно
								if (DateTime.MaxValue == dtRefEnd)
								{
									(new Logger()).WriteNotice("there is freeze [" + nFreezeIndx + "] in the air!!!   __BEGIN__  [nRefTotal=" + nRefTotal + "][_nReferences=" + _nReferences + "][new_value=" + value + "]");
								}
								dtRefEnd = DateTime.Now.AddSeconds(3);
								bFreeze = true;
							}
							_nReferences = value;
						}
					}
				}
			}
			public Audio cAudio;
			public Video cVideo;
			public ulong nID;
			static private ulong nIDs = 0;

			public Frame()
			{
				nID = nIDs++;
			}
		}
		private static int nRefTotal = 0;
		private static int nFreezeIndx = 0;
		private static bool bFreeze = false;
		private static DateTime dtRefEnd = DateTime.MaxValue;
		public event AVFrameArrivedCallback AVFrameArrived;
		public event NextFrameCallback NextFrame;
		protected bool bAVFrameArrivedAttached
		{
			get
			{
				return (null != AVFrameArrived);
			}
		}
		protected bool NextFrameAttached
		{
			get
			{
				return (null != NextFrame);
			}
		}

		public Frame.Video _cVideoFrameEmpty; //bug
		public List<Frame.Video> _aConveyorTotal; //bug
		protected IntPtr _pVideoFrameBuffer;
		protected uint _nAudioBufferCapacity_InSamples;
		protected ushort _nTimescale;
		protected ushort _nFrameDuration;
		protected ushort _nFPS;
		protected DateTime _dtLastTimeFrameScheduleCalled = DateTime.MinValue;
		protected int _nFramesLated;
		protected int _nFramesDropped;
		protected int _nFramesFlushed;
		protected bool _bNeedToAddFrame;
		protected int n__PROBA__VideoFramesBuffered, n__PROBA__AudioFramesBuffered;
		protected int n__PROBA__AddToVideoStreamTime;
		protected bool _b__PROBA__OutOfRangeFlag;
		protected Queue<Frame.Audio> _aq__PROBA__AudioFrames = new Queue<Frame.Audio>();
		protected Queue<Frame.Video> _aq__PROBA__VideoFrames = new Queue<Frame.Video>();
		protected Frame.Video _cFrameVideoLast;
		protected Frame.Audio _cFrameAudioEmpty;

		public int nFramesLated
		{
			get
			{
				return _nFramesLated;
			}
		}
		public int nFramesDropped
		{
			get
			{
				return _nFramesDropped;
			}
		}
		public int nFramesFlushed
		{
			get
			{
				return _nFramesFlushed;
			}
		}

		protected Area _stArea;
		public Area stArea
		{
			get
			{
				return _stArea;
			}
			protected set
			{
				(new Logger()).WriteDebug3("device:area:set:in");
				_stArea = value;
				(new Logger()).WriteDebug4("device:area:set:return");
			}
		}
		public byte[] aFrameLastBytes { get; private set; }

		static public Device[] BoardsGet()
		{
			(new Logger()).WriteDebug3("in");
			List<Device> aRetVal = new List<Device>();

			aRetVal.AddRange(Decklink.BoardsGet());
#if XNA
			aRetVal.AddRange(Display.BoardsGet());
#endif
			if (1 > aRetVal.Count)
                throw new Exception("can't find any board");
            (new Logger()).WriteDebug4("return");
            return aRetVal.ToArray();
		}

		protected Device()
		{
			(new Logger()).WriteDebug3("in");
			_bNeedToAddFrame = false;
			_pVideoFrameBuffer = IntPtr.Zero;
			_nFramesLated = 0;
			_nFramesDropped = 0;
			_nFramesFlushed = 0;
			_b__PROBA__OutOfRangeFlag = true;
			_aConveyorTotal = new List<Frame.Video>();
			(new Logger()).WriteDebug4("return");
		}
		~Device()
		{
		}

		System.Threading.Thread _cThread;
		virtual public void TurnOn()
		{
			if (Preferences.bAudio)
			{
				_cFrameAudioEmpty = new Frame.Audio();
				_cFrameAudioEmpty.aFrameBytes = new byte[Preferences.nAudioBytesPerFrame];
			}
            if (!Preferences.bDeviceInput)
            {
                _cVideoFrameEmpty = FrameBufferPrepare();
                if (_cVideoFrameEmpty.oFrameBytes is IntPtr)
                {
                    uint nBlack = 0x0;
                    for (int nIndx = 0; nIndx < _stArea.nWidth * _stArea.nHeight * 4; nIndx += 4)
                        Marshal.WriteInt32(_cVideoFrameEmpty.pFrameBytes, nIndx, (Int32)nBlack); //pixel format
                }
                _cFrameVideoLast = _cVideoFrameEmpty;
                while (Preferences.nQueueBaetylusLength + 2 > _aFrames.Count)
                    AddNewFrameToConveyor("! from TurnOn !");

                _cThread = new System.Threading.Thread(FrameScheduleWorker);
                _cThread.Priority = System.Threading.ThreadPriority.Highest;
                _cThread.Start();
                _cBugCatcherOnFrameGet = new BugCatcher(_cVideoFrameEmpty);  // bug
                _cBugCatcherOnVideoFrameReturn = new BugCatcher(_cVideoFrameEmpty);  // bug
                _cBugCatcherOnVideoFramePrepare = new BugCatcher(_cVideoFrameEmpty);  // bug
                _cBugCatcherScheduleFrame = new BugCatcher(_cVideoFrameEmpty);  // bug
                //System.Threading.ThreadPool.QueueUserWorkItem(FrameScheduleWorker);
            }




		}
		virtual public void DownStreamKeyer()
		{ }
		abstract protected Frame.Video FrameBufferPrepare();
		private Frame.Video AddNewFrameToConveyor(string sInfo)
		{
			Frame.Video oRetVal;
			oRetVal = FrameBufferPrepare();
			if (_aConveyorTotal.Contains(oRetVal))    // проверить было ли вообще такое	
			{
				do
				{
					oRetVal = FrameBufferPrepare();
					(new Logger()).WriteWarning("Trying to ADD NEW videoframe that already exists in conveyor: [_aFrames.Count = " + _aFrames.Count() + "][_aq__PROBA__AVFrames = (" + _aq__PROBA__VideoFrames.Count + ", " + _aq__PROBA__AudioFrames.Count + ")]<br>[info = " + sInfo);
				} while (_aConveyorTotal.Contains(oRetVal));
			}
			_aFrames.AddLast(oRetVal);
			_aConveyorTotal.Add(oRetVal);
			nConvLength++;
			return oRetVal;
		}
		private void ReturnFrameToConveyor(Frame.Video oVF, string sInfo) //bug
		{
			lock (_aFrames)
			{
				if (_aFrames.Contains(oVF))
				{
					(new Logger()).WriteWarning("Trying to RETURN videoframe that already returned to conveyor some items ago: [_aFrames.Count = " + _aFrames.Count() + "][_aq__PROBA__AVFrames = (" + _aq__PROBA__VideoFrames.Count + ", " + _aq__PROBA__AudioFrames.Count + ")]<br>[info = " + sInfo);
					return;
				}
				_aFrames.AddLast(oVF);
			}
		}
		protected void FrameBufferReleased(Frame.Video o)  //bug
		{
			lock (_aFrames)
			{
				if (0 < o.nReferences)
				{
					o.nReferences--;
					return;
				}
				if (o == _cVideoFrameEmpty || o == _cFrameVideoLast)
					return;
				ReturnFrameToConveyor(o, "! from FrameBufferReleased !");
			}
		}
		private LinkedList<Frame.Video> _aFrames = new LinkedList<Frame.Video>();
		private int nConvLength = 0;
		long nQ = 0;
		public Frame.Video FrameBufferGet()
		{
			Frame.Video cRetVal = null;
			string sInfo = "video prepare:";//bug
			lock (_aFrames)
			{
				if (3 > _aFrames.Count) // чтобы сохранять зазор в 2 кадра  //bug
				{
					AddNewFrameToConveyor("! from FrameBufferGet !"); 
					(new Logger()).WriteNotice("размер конвейера был увеличен до " + nConvLength);
					sInfo += "conveier new:";
				}
				else
					sInfo += "conveier old:";
				cRetVal = _aFrames.First.Value;
				_aFrames.RemoveFirst();
			}
			_cBugCatcherOnVideoFramePrepare.Enqueue(cRetVal, sInfo + "_aq__PROBA__VideoFrames:" + _aq__PROBA__VideoFrames.Count + ":_aq__PROBA__AudioFrames:" + _aq__PROBA__AudioFrames.Count);
			return cRetVal;
		}
		private void FrameScheduleWorker(object cState)
		{
			bool bAdded;
			double nSecondsElapsed;
			int nSleepDuration = Preferences.nFrameDuration / 2;
			while (true)
			{
				try
				{
					if (_dtLastTimeFrameScheduleCalled > DateTime.MinValue && (nSecondsElapsed = DateTime.Now.Subtract(_dtLastTimeFrameScheduleCalled).TotalSeconds) > 3)
					{
						(new Logger()).WriteError(new Exception("frame scheduled was more than 3 seconds ago. device may be dead. [delta t = " + nSecondsElapsed + " seconds]"));
						_dtLastTimeFrameScheduleCalled = DateTime.MinValue;
					}
					if (_bNeedToAddFrame)
					{
						bAdded = FrameSchedule();
						//if (bAdded && Preferences.nQueueDeviceLength <= n__PROBA__AudioFramesBuffered && Preferences.nQueueDeviceLength <= n__PROBA__VideoFramesBuffered)
						if (bAdded && Preferences.nQueueDeviceLength <= n__PROBA__AudioFramesBuffered && Preferences.nQueueDeviceLength <= n__PROBA__VideoFramesBuffered)
							_bNeedToAddFrame = false;
					}
					else
					{
						(new Logger()).WriteDebug4("FrameScheduleWorker:Sleep");
						System.Threading.Thread.Sleep(nSleepDuration);
					}
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
			}
		}
		int nQty = 5;

		// bug
		BugCatcher _cBugCatcherOnFrameGet, _cBugCatcherOnVideoFrameReturn, _cBugCatcherOnVideoFramePrepare;
		public BugCatcher _cBugCatcherScheduleFrame;
		public class BugCatcher
		{
			class Info
			{
				public Frame cFrame;
				public string sInfo;
			}
			List<Info> aLastNFrames = new List<Info>();
			Frame.Video _cEmptyVideoFrame;
			public BugCatcher(Frame.Video cEmpty)
			{
				Info cInfo = new Info { cFrame = new Frame() { cVideo = cEmpty }, sInfo = "empty frame" };
				aLastNFrames.Add(cInfo);
				aLastNFrames.Add(cInfo);
				aLastNFrames.Add(cInfo);
				aLastNFrames.Add(cInfo);
				_cEmptyVideoFrame = cEmpty;
			}
			public void Enqueue(Frame cFrame, string sInfo)
			{
				return;
				aLastNFrames.Add(new Info() { cFrame = cFrame, sInfo = sInfo });
				aLastNFrames.RemoveAt(0);
				TestForBug();
			}
			public void Enqueue(Frame.Video cFrame, string sInfo)
			{
				return;
				Enqueue(new Frame() { cVideo = cFrame }, sInfo);
			}
			void TestForBug()
			{
				try
				{
					if (
							null != aLastNFrames[0].cFrame &&
							null != aLastNFrames[1].cFrame &&
							null != aLastNFrames[2].cFrame &&
							null != aLastNFrames[3].cFrame &&
							aLastNFrames[1].cFrame.cVideo != _cEmptyVideoFrame
							&& aLastNFrames[1].cFrame.cVideo == aLastNFrames[2].cFrame.cVideo
							&& aLastNFrames[0].cFrame.cVideo != aLastNFrames[1].cFrame.cVideo
							&& aLastNFrames[2].cFrame.cVideo != aLastNFrames[3].cFrame.cVideo
						)
						(new Logger()).WriteWarning("BUG DETECTED: <br>" + aLastNFrames[0].sInfo + "<br>" + aLastNFrames[1].sInfo + "<br>" + aLastNFrames[2].sInfo + "<br>" + aLastNFrames[3].sInfo);
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
			}
		}
		public static Dictionary<ulong, long> _aCurrentFramesIDs; //DNF
		public static System.Diagnostics.Stopwatch _cStopWatch;
		public static long _nLastScTimeComplited = 0;
		public static List<ulong> _aBackUPFramesIDs; //DNF 

		private long BalanceBeemTimeCounter;


		protected Frame.Audio AudioFrameGet()
		{
			if (0 < _aq__PROBA__AudioFrames.Count)
				return _aq__PROBA__AudioFrames.Dequeue();
			Frame.Audio cRetVal = _cFrameAudioEmpty;
			Frame cFrame = NextFrame();
			_cBugCatcherOnFrameGet.Enqueue(cFrame, "audio recieve:_aq__PROBA__VideoFrames:" + _aq__PROBA__VideoFrames.Count + ":_aq__PROBA__AudioFrames:" + _aq__PROBA__AudioFrames.Count);
			if (null != cFrame)
			{
				if (null == cFrame.cVideo.oFrameBytes)
				{
					_cFrameVideoLast.nReferences++;
					(new Logger()).WriteDebug2("BYTES FROM BTL IS NULL 1 - repeat the last [id=" + _cFrameVideoLast.nID + "][ref=" + _cFrameVideoLast.nReferences + "]");
				}
				else if (cFrame.cVideo.oFrameBytes is byte[] && 1 > cFrame.cVideo.aFrameBytes.Length)
					_cFrameVideoLast = _cVideoFrameEmpty; //получили признак необходимости очистить экран
				else
					_cFrameVideoLast = cFrame.cVideo;



				_aq__PROBA__VideoFrames.Enqueue(_cFrameVideoLast);



				if (null != cFrame.cAudio && null != cFrame.cAudio.aFrameBytes)
					cRetVal = cFrame.cAudio;
				else
					(new Logger()).WriteNotice("Got null audio frame from BTL! [audio_frame_is_null = " + (cFrame.cAudio == null ? "true]" : "false][bytes_is_null = " + (cFrame.cAudio.aFrameBytes == null ? "true" : "false") + "]"));
			}
			else
			{
				_aq__PROBA__VideoFrames.Enqueue(_cFrameVideoLast);
				_cFrameVideoLast.nReferences++;
				// пока не разобрались с набегающим рассинхроном - теряем кадрик!
				(new Logger()).WriteDebug2("FRAME FROM BTL IS NULL 1 - repeat the last [id=" + _cFrameVideoLast.nID + "][ref=" + _cFrameVideoLast.nReferences + "]");
			}


			// пока не разобрались с набегающим рассинхроном - теряем кадрик!  
			if (2 < _aq__PROBA__VideoFrames.Count)
			{
				if (250 < BalanceBeemTimeCounter)
				{
					Frame.Video cVF = _aq__PROBA__VideoFrames.Dequeue();
					FrameBufferReleased(cVF); //  возврат в конвейер
					(new Logger()).WriteWarning("BALANCE-BEEM. SYNC CORRECTED BY DROPPING __VIDEO__ FRAME! [video_frame_id=" + cVF.nID + "][ticks=" + DateTime.Now.Ticks + "]");
				}
				else if (0 == BalanceBeemTimeCounter)
					(new Logger()).WriteNotice("balance-beem started [ticks=" + DateTime.Now.Ticks + "]");
				BalanceBeemTimeCounter++;
			}
			else if (0 < BalanceBeemTimeCounter)
			{
				BalanceBeemTimeCounter = 0;
				(new Logger()).WriteNotice("balance-beem ended [ticks=" + DateTime.Now.Ticks + "]");
			}

			return cRetVal;
		}
		protected Frame.Video VideoFrameGet()
		{
			Frame.Video cRetVal;
			if (1 < _aq__PROBA__AudioFrames.Count - _aq__PROBA__VideoFrames.Count)
			{
				//_cFrameVideoLast.nReferences++;
				//_aq__PROBA__VideoFrames.Enqueue(_cFrameVideoLast); //КАК Я И ГОВОРИЛ, БАГ ИМЕННО ИЗ-ЗА ЭТОЙ КОНСТРУКЦИИ =)) МЫ ОДИН И ТОТ ЖЕ НОРМАЛЬНЫЙ КАДР ХУЯЧИМ ДВА РАЗА ИЗ-ЗА ЭТОГО IF'А
				if (250 < BalanceBeemTimeCounter)
				{
					ulong nID = _aq__PROBA__AudioFrames.Dequeue().nID;
					(new Logger()).WriteWarning("BALANCE-BEEM. SYNC CORRECTED BY DROPPING __AUDIO__ FRAME! [audio_frame_id=" + nID + "][ticks=" + DateTime.Now.Ticks + "]");
				}
				else if (0 == BalanceBeemTimeCounter)
					(new Logger()).WriteNotice("balance-beem started [ticks=" + DateTime.Now.Ticks + "]");
				BalanceBeemTimeCounter++;
			}
			else if (0 < BalanceBeemTimeCounter)
			{
				BalanceBeemTimeCounter = 0;
				(new Logger()).WriteNotice("balance-beem ended [ticks=" + DateTime.Now.Ticks + "]");
			}

			if (0 < _aq__PROBA__VideoFrames.Count)
			{
				cRetVal = _aq__PROBA__VideoFrames.Dequeue();
				_cBugCatcherOnVideoFrameReturn.Enqueue(cRetVal, "first return:_aq__PROBA__VideoFrames:" + _aq__PROBA__VideoFrames.Count + ":_aq__PROBA__AudioFrames:" + _aq__PROBA__AudioFrames.Count);  // bug
				return cRetVal;
			}
			cRetVal = _cFrameVideoLast;
			Frame cFrame = NextFrame();
			_cBugCatcherOnFrameGet.Enqueue(cFrame, "video recieve:_aq__PROBA__VideoFrames:" + _aq__PROBA__VideoFrames.Count + ":_aq__PROBA__AudioFrames:" + _aq__PROBA__AudioFrames.Count);
			if (null != cFrame)
			{
				if (null == cFrame.cVideo.oFrameBytes)
				{
					_cFrameVideoLast.nReferences++;
					(new Logger()).WriteDebug2("BYTES FROM BTL IS NULL 2 - repeat the last [id=" + _cFrameVideoLast.nID + "][ref=" + _cFrameVideoLast.nReferences + "]");
				}
				else if (cFrame.cVideo.oFrameBytes is byte[] && 1 > cFrame.cVideo.aFrameBytes.Length)
					cRetVal = _cFrameVideoLast = _cVideoFrameEmpty; //получили признак необходимости очистить экран
				else
					cRetVal = _cFrameVideoLast = cFrame.cVideo;

				if (null != cFrame.cAudio && null != cFrame.cAudio.aFrameBytes)
					_aq__PROBA__AudioFrames.Enqueue(cFrame.cAudio);
				else
					_aq__PROBA__AudioFrames.Enqueue(_cFrameAudioEmpty);
			}
			else
			{
				_cFrameVideoLast.nReferences++;
				(new Logger()).WriteDebug2("FRAME FROM BTL IS NULL 2 - repeat the last [id=" + _cFrameVideoLast.nID + "][ref=" + _cFrameVideoLast.nReferences + "]");
				_aq__PROBA__AudioFrames.Enqueue(_cFrameAudioEmpty);
			}
			_cBugCatcherOnVideoFrameReturn.Enqueue(cRetVal, "last return:_aq__PROBA__VideoFrames:" + _aq__PROBA__VideoFrames.Count + ":_aq__PROBA__AudioFrames:" + _aq__PROBA__AudioFrames.Count);  // bug
			return cRetVal;
		}
		abstract protected bool FrameSchedule();

		protected void OnAVFrameArrived(int nBytesVideoQty, IntPtr pBytesVideo, int nBytesAudioQty, IntPtr pBytesAudio)
		{
			if(null != AVFrameArrived)
				AVFrameArrived(nBytesVideoQty, pBytesVideo, nBytesAudioQty, pBytesAudio);
		}

		event AVFrameArrivedCallback IDevice.AVFrameArrived
		{
			add
			{
				lock (AVFrameArrived)
					AVFrameArrived += value;
			}
			remove
			{
				lock (AVFrameArrived)
					AVFrameArrived -= value;
			}
		}
		event NextFrameCallback IDevice.NextVideoFrame
		{
			add
			{
				lock (NextFrame)
					NextFrame += value;
			}
			remove
			{
				lock (NextFrame)
					NextFrame -= value;
			}
		}
		void IDevice.TurnOn()
		{
			this.TurnOn();
		}
		Frame.Video IDevice.FrameBufferGet()
		{
			return this.FrameBufferGet();
		}
	}
}
