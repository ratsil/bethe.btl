#define CUDA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime;
using DeckLinkAPI;
using helpers;
using BTL.Play;

//отладка
//using System.Drawing;
//using System.Drawing.Imaging;
using System.Diagnostics;
using helpers.extensions;

namespace BTL
{
    public class Baetylus : IContainer
    {
        static public class Helper
        {
            static private Baetylus _cBTL;
            static private object cSyncRoot = new object();
            static public Baetylus cBaetylus
            {
                get
                {
                    (new Logger()).WriteDebug3("in");
                    lock (cSyncRoot)
                    {
                        (new Logger()).WriteDebug4("helper:baetylus:lock:inside");
                        if (null == _cBTL)
                        {
                            (new Logger()).WriteDebug4("helper:baetylus:init");
                            _cBTL = new Baetylus();
                            (new Logger()).WriteDebug4("helper:baetylus:init:after");
                        }
                        (new Logger()).WriteDebug4("return");
                        return _cBTL;
                    }
                }
				set
				{
					(new Logger()).WriteDebug3("in");
					_cBTL = value;
				}
            }
			static public Device cBoard
			{
				get
				{
					(new Logger()).WriteDebug3("in");
					return cBaetylus.cBoard;
				}
			}
        }
		class LoggerMessage
		{
			public DateTime dt;
			public Logger.Level eLevel;
			public string sMessage;

			public LoggerMessage(Logger.Level eLevel, string sMessage)
			{
				dt = DateTime.Now;
				this.eLevel = eLevel;
				this.sMessage = sMessage;
			}
		}
		class AudioChannelMapping
		{
			public byte[] aBuffer;
			public byte nBufferChannel;
			public byte nBufferChannelsQty;
		}

#region отладка
		static public uint nVideoBufferCount;
		static public uint nAudioBufferCount;
		//static public uint nAudioSamplesBuffered;
		//static public uint nVideoFramesBuffered;
		//static public ulong nAudioStreamTime;
		//static public ulong nVideoStreamTime;
		//static public bool bMustGetImage;
		//static public bool bImageIsReady;
		//static public bool bMustGetImageInDevice;
		//static public bool bImageIsReadyInDevice;
		//static public Bitmap cBitmap;
		//static public Bitmap cBitmapInDevice;
#endregion

		#region timings
		class TimingInfo
		{
			public enum Type
			{
				VideoEffectFrameNext,
				AudioEffectSampleNext,
				AudioChannelMappings,
				ContainerActions,
				FrameBufferGet,
				Merge,
				CopyOut,
				PixelsMapsDispose
			}

			static public int nTypesQty = Enum.GetValues(typeof(Type)).Length;

			public Type eType;
			public double nValue;
			public object o;

			public TimingInfo(Type eType, double nValue)
				: this(eType, nValue, null)
			{
			}
			public TimingInfo(Type eType, double nValue, object o)
			{
				this.eType = eType;
				this.nValue = nValue;
				this.o = o;
			}
		}
		public string sPLLogs = "";
		//private long _nDTStart;
		//private double _nTotalMilliseconds;
		#endregion

        static private Queue<Dictionary<IEffect, ContainerAction>> _aqContainerActions;
        static private bool _bNeedEffectsReorder;
        static private ushort _nReferencesQty;
        static private object _cSyncRoot = new object();
        static private Device[] _aBoards;
        static public Device[] aBoards
        {
            get
            {
                (new Logger()).WriteDebug3("in");
                (new Logger()).WriteDebug4("baetylus:boards:get:before lock");
                lock (_cSyncRoot)
                {
                    (new Logger()).WriteDebug4("baetylus:boards:get:inside lock");
                    if (null == _aBoards)
						_aBoards = Device.BoardsGet();
                    (new Logger()).WriteDebug4("return");
                    return _aBoards;
                }
            }
        }

        static private List<IEffect> _aEffects;
        static private Area _stFullFrameArea;
		static private ThreadBufferQueue<Device.Frame> _aqBufferFrame;
		static private Queue<PixelsMap> _aqPixelsMapsDisposed;

		private IDevice _cBoardCurrent;
		private System.Threading.Thread _cThreadWorker;
		private System.Threading.Thread _cThreadWritingFramesWorker;
		private bool _bDoWritingFrames;
		private Queue<byte[]> _aqWritingFrames;
		private int _nFrameBufSize;

        public Device cBoard
        {
            get
            {
                (new Logger()).WriteDebug3("in");
                if (null == _cBoardCurrent)
                {
                    (new Logger()).WriteDebug4("baetylus:board:get:init");
                    if (null == aBoards || 1 > aBoards.Length)
                    {
                        (new Logger()).WriteDebug2("baetylus:board:get:return null");
                        return null;
                    }
                    _cBoardCurrent = _aBoards[Preferences.nDeviceTarget];
                }
                (new Logger()).WriteDebug4("return");
                return (Device)_cBoardCurrent;
            }

			set
			{
				_cBoardCurrent = value;
			}
		}

        public Baetylus()
        {
            (new Logger()).WriteDebug3("in");
            lock (_cSyncRoot)
            {
                if (1 > _nReferencesQty)
                {
                    (new Logger()).WriteDebug4("baetylus:constructor:init");
                    _aqContainerActions = new Queue<Dictionary<IEffect, ContainerAction>>();

                    _aEffects = new List<IEffect>();
                    _stFullFrameArea = cBoard.stArea;
					_aqBufferFrame = new ThreadBufferQueue<Device.Frame>(Preferences.nQueueBaetylusLength, true, false);
					_aqPixelsMapsDisposed = new Queue<PixelsMap>();
                    _bNeedEffectsReorder = false;
					_cThreadWorker = new System.Threading.Thread(Worker);
					_cThreadWorker.IsBackground = true;
					_cThreadWorker.Priority = System.Threading.ThreadPriority.AboveNormal;
					_cThreadWorker.Start();
					_bDoWritingFrames = false;
					_aqWritingFrames = new Queue<byte[]>();
					_cThreadWritingFramesWorker = new System.Threading.Thread(WritingFramesWorker);
					_cThreadWritingFramesWorker.IsBackground = true;
					_cThreadWritingFramesWorker.Priority = System.Threading.ThreadPriority.Normal;
					_cThreadWritingFramesWorker.Start();

					cBoard.NextFrame += new NextFrameCallback(OnNextFrame);
                    cBoard.TurnOn();
                }
                _nReferencesQty++;
            }
            (new Logger()).WriteDebug4("return [refqty:" + _nReferencesQty + "]");
        }
        ~Baetylus()
        {
            _nReferencesQty--;
            if (1 > _nReferencesQty)
            {
            }
        }

		static internal void PixelsMapDispose(PixelsMap[] aPixelsMaps)
		{
			foreach (PixelsMap cPixelsMap in aPixelsMaps)
				PixelsMapDispose(cPixelsMap, false);
		}
		static internal void PixelsMapDispose(PixelsMap cPixelsMap)
		{
			PixelsMapDispose(cPixelsMap, false);
		}
		static internal void PixelsMapDispose(PixelsMap cPixelsMap, bool bForce)
		{
			if (bForce)
				cPixelsMap.bKeepAlive = false;
			lock (_aqPixelsMapsDisposed)
				if (1 > _aqPixelsMapsDisposed.Count(row => row == cPixelsMap))
					_aqPixelsMapsDisposed.Enqueue(cPixelsMap);
		}
        public List<Play.Effect> BaetylusEffectsInfoGet()
        {
            List<Play.Effect> aEff = new List<Play.Effect>();
            lock (_aEffects)
                foreach (IEffect cEff in _aEffects)
                    if (cEff is Play.Effect)
                        aEff.Add((Play.Effect)cEff);
            return aEff;
        }
        private void Worker(object cState)
        {
			LinkedList<LoggerMessage> aLoggerMessages = new LinkedList<LoggerMessage>();
			Logger cLogger;
			Device.Frame.Audio cFrameAudio = null;
			Device.Frame.Video cFrameVideo = null;
            IVideo iVideoEffect = null;
			PixelsMap cBackground = new PixelsMap(Preferences.bCUDA, _stFullFrameArea, PixelsMap.Format.ARGB32);
			cBackground.eAlpha = (null == Preferences.cDownStreamKeyer ? DisCom.Alpha.none : DisCom.Alpha.normal);
            cBackground.Allocate();
            cBackground.bKeepAlive = true;
            PixelsMap cFrame = null;
			byte[] aFrameBufferAudio = null, aBytesAudio = null;
			List<PixelsMap> aFrames = new List<PixelsMap>();
            Dictionary<IEffect, ContainerAction> ahMoveInfo = null;
			int nIndx, nLogIndx = 0;
            bool bIsStarted = false;

			#region timings
			Stopwatch cStopwatchEffect = null, cStopwatch = null;
			double nTotalFrameDuration, nTotalFrameDurationMax = 0;
			string sMessage;
			LinkedList<TimingInfo> aTimings = new LinkedList<TimingInfo>();
			#endregion

			List<IVideo> aOpacityEffects = new List<IVideo>();
			Dictionary<IAudio,byte[]> ahAudioEffects;
			Dictionary<IEffect, ulong> ahEffectsDelays = new Dictionary<IEffect, ulong>();

			AudioChannelMapping[] aAudioChannelMappings = null;
			uint nSampleBytesQty = Preferences.nAudioByteDepth;
			uint nChannelSamplesQty = Preferences.nAudioSamplesPerFrame;
			uint nChannelBytesQty = Preferences.nAudioSamplesPerFrame * Preferences.nAudioByteDepth;
			byte nChannelsQty = 0;
			byte[] aChannels;
			int nSource, nDestination;
			bool bVideoFrame = false;
			bool bWroteNotice = false;
			bool bWroteWarning = false;
			IAudio iAudioEffect;
			_nFrameBufSize = _stFullFrameArea.nWidth * _stFullFrameArea.nHeight * 4;
			
            while (null != _aEffects)
            {
                try
                {
					long nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
					cStopwatchEffect = Stopwatch.StartNew();
                    aFrames.Clear();
                    aOpacityEffects.Clear();
                    aFrameBufferAudio = null;
                    iVideoEffect = null;
					ahAudioEffects = new Dictionary<IAudio, byte[]>();
					aAudioChannelMappings = null;

					sPLLogs = "";
					sMessage = "";
					aTimings.Clear();
					//if (DateTime.Now > dtLastTime.AddMinutes(1)) //EMERGENCY если воскрешать то выкинуть DateTime... отталкиваться можно от кол-ва прошедших итераций
					//{
					//    dtLastTime = DateTime.Now;
					//    string sDebug = "worker: _aEffects.Count = " + _aEffects.Count + ", <br>\t_aEffects =";
					//    foreach (IEffect cEff in _aEffects)
					//    {
					//        sDebug += "<br>\t\t[" + _aEffects.IndexOf(cEff) + "]\ttype = " + cEff.eType + "\tstatus = " + cEff.eStatus;
					//    }
					//    sDebug += "<br>\t\tqueues: [v:" + _aqBufferVideo.nCount + "][a:" + _aqBufferAudio.nCount + "]";
					//    aqLoggerMessages.Enqueue(new LoggerMessage(Logger.Level.notice, sDebug);
					//}
					if (1 > _aEffects.Count && (bVideoFrame || Preferences.bClearScreenOnEmpty)) //добавляем в очередь признак необходимости очистить экран, иначе девайс будет повторять предыдущий кадр до потери сознания
					{
						if (!bWroteNotice)
						{
							(new Logger()).WriteNotice("вставляем черное поле...");
							bWroteNotice = true;
						}
						_aqBufferFrame.Enqueue(new Device.Frame() { cAudio = null, cVideo = new Device.Frame.Video() { oFrameBytes = new byte[0] } });
						bVideoFrame = false;
					}
					else
					{
						bWroteNotice = false;
					}
					if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)    
						(new Logger()).WriteDebug2("PART-01: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
                    #region audio & video processing
					for (nIndx = _aEffects.Count - 1; 0 <= nIndx; nIndx--)
					{
						nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
						try
						{
							if (!bIsStarted)
								bIsStarted = true;
							if (null == _aEffects[nIndx])
							{
								(new Logger()).WriteError("effect can't be null");
								continue;
							}
							if (!(_aEffects[nIndx] is IVideo) && !(_aEffects[nIndx] is IAudio))
								continue;
							if (EffectStatus.Running != _aEffects[nIndx].eStatus)
								continue;
							if (0 < _aEffects[nIndx].nDelay)
							{
								if (!ahEffectsDelays.ContainsKey(_aEffects[nIndx]))
									ahEffectsDelays.Add(_aEffects[nIndx], _aEffects[nIndx].nDelay);
								if (0 < ahEffectsDelays[_aEffects[nIndx]])
								{
									ahEffectsDelays[_aEffects[nIndx]]--;
									continue;
								}
							}
							if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
								(new Logger()).WriteDebug2("PART-02-1: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
							nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
							bVideoFrame = true;
							iAudioEffect = null;
							cFrame = null;
							if (_aEffects[nIndx] is IVideo)
							{
								iVideoEffect = (IVideo)_aEffects[nIndx];
								if (1 > aOpacityEffects.Count(row => row.stArea >= iVideoEffect.stArea))
								{
									if (iVideoEffect.bOpacity)
										aOpacityEffects.Add(iVideoEffect);
									cStopwatch = Stopwatch.StartNew();
									cFrame = iVideoEffect.FrameNext();
									cStopwatch.Stop();
									aTimings.AddLast(new TimingInfo(TimingInfo.Type.VideoEffectFrameNext, cStopwatch.Elapsed.TotalMilliseconds, iVideoEffect));
                                    if (null != cFrame)
                                    {
                                        aFrames.Insert(0, cFrame);
                                        if (null != iVideoEffect.iMask)
                                        {
                                            aFrames.Insert(0, iVideoEffect.iMask.FrameNext());
                                            aFrames[0].eAlpha = DisCom.Alpha.mask;
                                        }

                                    }
                                    else
                                        bVideoFrame = false;
								}
								else
									iVideoEffect.Skip();
							}
							if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
								(new Logger()).WriteDebug2("PART-02-2: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
							nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
							if (_aEffects[nIndx] is IAudio)
							{
								iAudioEffect = (IAudio)_aEffects[nIndx];
								if (null == (aChannels = iAudioEffect.aChannels) || 0 < aChannels.Length) //см. комменты в IAudio
									ahAudioEffects.Add(iAudioEffect, iAudioEffect.FrameNext());
								else
									iAudioEffect.Skip();
							}
							if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
								(new Logger()).WriteDebug2("PART-02-3: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
							nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
							if (null != iAudioEffect && ahAudioEffects.ContainsKey(iAudioEffect))
							{
								if (null == ahAudioEffects[iAudioEffect] && aFrames.Contains(cFrame))
								{
									aFrames.Remove(cFrame);
									PixelsMapDispose(cFrame);
                                    if (null != iVideoEffect.iMask)
                                    {
                                        PixelsMapDispose(aFrames[0]);
                                        aFrames.RemoveAt(0);
                                    }
								}
								if (!bVideoFrame)
									ahAudioEffects.Remove(iAudioEffect);
							}
							if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
								(new Logger()).WriteDebug2("PART-02-4: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
							nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
						}
						catch (Exception ex)
						{
							(new Logger()).WriteError(ex);
						}
						if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
							(new Logger()).WriteDebug2("PART-02-5: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
					}
					#region audio mappings
					nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
					if (0 < ahAudioEffects.Count)
					{
						foreach (IAudio iAudio in ahAudioEffects.Keys)
						{
							cStopwatch = Stopwatch.StartNew();
							if (null == (aBytesAudio = ahAudioEffects[iAudio]))
							{
								cStopwatch.Stop();
								aTimings.AddLast(new TimingInfo(TimingInfo.Type.AudioEffectSampleNext, cStopwatch.Elapsed.TotalMilliseconds, iAudio));
								continue;
							}
							nChannelsQty = (byte)(aBytesAudio.Length / nChannelBytesQty);
							aChannels = iAudio.aChannels;
							if (null == aChannels)
							{
								aChannels = new byte[nChannelsQty];
								for (byte nChannel = 0; nChannelsQty > nChannel; nChannel++)
									aChannels[nChannel] = nChannel;
							}
							if (null == aAudioChannelMappings)
							{
								if (Preferences.nAudioChannelsQty == aChannels.Length)
								{
									aFrameBufferAudio = aBytesAudio;
									cStopwatch.Stop();
									aTimings.AddLast(new TimingInfo(TimingInfo.Type.AudioEffectSampleNext, cStopwatch.Elapsed.TotalMilliseconds, iAudio));
									break;
								}
								else
									aAudioChannelMappings = new AudioChannelMapping[Preferences.nAudioChannelsQty];
							}
							for (byte nChannel = 0; aChannels.Length > nChannel; nChannel++)
							{
								if (aChannels[nChannel] < aAudioChannelMappings.Length && null == aAudioChannelMappings[aChannels[nChannel]])
								{
									aAudioChannelMappings[aChannels[nChannel]] = new AudioChannelMapping();
									aAudioChannelMappings[aChannels[nChannel]].aBuffer = aBytesAudio;
									aAudioChannelMappings[aChannels[nChannel]].nBufferChannel = nChannel;
									aAudioChannelMappings[aChannels[nChannel]].nBufferChannelsQty = (byte)aChannels.Length;
								}
							}
							cStopwatch.Stop();
							aTimings.AddLast(new TimingInfo(TimingInfo.Type.AudioEffectSampleNext, cStopwatch.Elapsed.TotalMilliseconds, iAudio));
						}
						if (null != aAudioChannelMappings)
						{
							cStopwatch = Stopwatch.StartNew();
							aFrameBufferAudio = new byte[Preferences.nAudioChannelsQty * nChannelBytesQty];
							for (int nChannel = 0; aAudioChannelMappings.Length > nChannel; nChannel++)
							{
								if (null == aAudioChannelMappings[nChannel])
									continue;
								for (int nSampleIndx = 0; nChannelSamplesQty > nSampleIndx; nSampleIndx++)
								{
									nSource = (int)(((nSampleIndx * aAudioChannelMappings[nChannel].nBufferChannelsQty) + aAudioChannelMappings[nChannel].nBufferChannel) * nSampleBytesQty);
									nDestination = (int)(((nSampleIndx * aAudioChannelMappings.Length) + nChannel) * nSampleBytesQty);
									for (int nByte = 0; nSampleBytesQty > nByte; nByte++)
										aFrameBufferAudio[nDestination + nByte] = aAudioChannelMappings[nChannel].aBuffer[nSource + nByte];
								}
							}
							cStopwatch.Stop();
							aTimings.AddLast(new TimingInfo(TimingInfo.Type.AudioChannelMappings, cStopwatch.Elapsed.TotalMilliseconds));
						}
					}
					if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
						(new Logger()).WriteDebug2("PART-03: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
					#endregion audio mappings
					#endregion

					#region  container actions
					cStopwatch = Stopwatch.StartNew();
					lock (_aqContainerActions)
					{
						// очистка от стопов. пока тут. еще можно ее сделать у тех кто стопит, но это не всегда возможно ((
						Dictionary<IEffect, ContainerAction> ahToRemove = new Dictionary<IEffect, ContainerAction>();
						foreach (IEffect cEffect in _aEffects.Where(row => EffectStatus.Stopped == row.eStatus || EffectStatus.Error == row.eStatus).ToArray())
							ahToRemove.Add(cEffect, ContainerAction.Remove);
						if (0 < ahToRemove.Count)
							_aqContainerActions.Enqueue(ahToRemove);

						if (0 < _aqContainerActions.Count)
						{
							if (null == _aEffects)
								throw new NullReferenceException("internal error: array of effects is null");

							while (0 < _aqContainerActions.Count)
							{
								ahMoveInfo = _aqContainerActions.Dequeue();
								//nActionID = _aqActionsIDs.Dequeue();
								//OnActionIsFinished(nActionID);
								if (null == ahMoveInfo)
									throw new NullReferenceException("move info can't be null");
								foreach (IEffect cEffect in ahMoveInfo.Keys)
								{
									if (null == cEffect)
										throw new NullReferenceException("effect can't be null");

									switch (ahMoveInfo[cEffect])
									{
										case ContainerAction.Add:
											if (!_aEffects.Contains(cEffect))
											{
												(new Logger()).WriteDebug3("ContainerAction.Add");
												_aEffects.Add(cEffect);
												cEffect.iContainer = (IContainer)Helper.cBaetylus;
											}
											else
												throw new Exception("this container already has specified effect");
											break;
										case ContainerAction.Remove:
											if (_aEffects.Contains(cEffect))
												_aEffects.Remove(cEffect);
											if (ahEffectsDelays.ContainsKey(cEffect))
												ahEffectsDelays.Remove(cEffect);
											break;
										default:
											throw new NotImplementedException("unknown container action");
									}
								}
							}
							_bNeedEffectsReorder = true;
						}
						if (_bNeedEffectsReorder)
						{
							IEffect cEffect = null;
							for (ushort nOutterIndx = 0; _aEffects.Count > nOutterIndx; nOutterIndx++)
							{
								for (ushort nInnerIndx = (ushort)(nOutterIndx + 1); _aEffects.Count > nInnerIndx; nInnerIndx++)
								{
									if (_aEffects[nOutterIndx].nLayer > _aEffects[nInnerIndx].nLayer)
									{
										aLoggerMessages.AddLast(new LoggerMessage(Logger.Level.debug3, "effects reordered: " + _aEffects[nOutterIndx].eType.ToString() + "[" + _aEffects[nOutterIndx].GetHashCode() + "] has layer " + _aEffects[nOutterIndx].nLayer + " and " + _aEffects[nInnerIndx].eType.ToString() + "[" + _aEffects[nInnerIndx].GetHashCode() + "] has layer " + _aEffects[nInnerIndx].nLayer));
										cEffect = _aEffects[nOutterIndx];
										_aEffects[nOutterIndx] = _aEffects[nInnerIndx];
										_aEffects[nInnerIndx] = cEffect;
									}
								}
							}
							_bNeedEffectsReorder = false;
						}
					}
					cStopwatch.Stop();
					aTimings.AddLast(new TimingInfo(TimingInfo.Type.ContainerActions, cStopwatch.Elapsed.TotalMilliseconds));
					#endregion

					#region merging
					nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
					if ((null == iVideoEffect && 1 > ahAudioEffects.Count) || (null != iVideoEffect && 1 > aFrames.Count) || (0 < ahAudioEffects.Count && null == aFrameBufferAudio)) //(1 > aFrames.Count && null == aFrameBufferAudio) || 
					{
						if (bIsStarted && !bWroteWarning)
						{
							(new Logger()).WriteWarning("we're sleeping  [aFrames.Count = " + aFrames.Count + "] [aFrameBufferAudio is null = " + aFrameBufferAudio.IsNullOrEmpty() + "] [aAudioEffects.Count = " + ahAudioEffects.Count + "]");
							bWroteWarning = true;
						}
						System.Threading.Thread.Sleep(10);
						continue;  //sMessage = ":got null video:";
					}
					bWroteWarning = false;

					if (null != iVideoEffect)
					{
						cStopwatch = Stopwatch.StartNew();
						if ((null == cFrameVideo || null == cFrameVideo.oFrameBytes) && null == (cFrameVideo = _cBoardCurrent.FrameBufferGet()))
						{
							(new Logger()).WriteError(new Exception("FATAL ERROR"));
							break;
						}
						cStopwatch.Stop();
						aTimings.AddLast(new TimingInfo(TimingInfo.Type.FrameBufferGet, cStopwatch.Elapsed.TotalMilliseconds));

						if (1 < aFrames.Count || _stFullFrameArea != aFrames[0].stArea)
						{
							cStopwatch = Stopwatch.StartNew();
							cBackground.Merge(aFrames);
							cFrame = cBackground;
							cStopwatch.Stop();
							aTimings.AddLast(new TimingInfo(TimingInfo.Type.Merge, cStopwatch.Elapsed.TotalMilliseconds));
							(new Logger()).WriteDebug4("merge: " + cStopwatch.Elapsed.TotalMilliseconds);
						}
						else
						{
							cFrame = aFrames[0];
							PixelsMapDispose(cFrame);
						}

						//if (400 == nTMPCount)      //    =================    SLEEP    TEST  ===========================
						//    System.Threading.Thread.Sleep(10000);
						//nTMPCount++; 

						cStopwatch = Stopwatch.StartNew();
						if (cFrameVideo.oFrameBytes is IntPtr)
							cFrame.CopyOut((IntPtr)cFrameVideo.oFrameBytes);
						else if (cFrameVideo.oFrameBytes is byte[])
							cFrame.CopyOut((byte[])cFrameVideo.oFrameBytes);
						cStopwatch.Stop();
						aTimings.AddLast(new TimingInfo(TimingInfo.Type.CopyOut, cStopwatch.Elapsed.TotalMilliseconds));
					}
					else
						cFrameVideo = new Device.Frame.Video();

					cFrameAudio = new Device.Frame.Audio();
					cFrameAudio.aFrameBytes = aFrameBufferAudio;
					if (0 < sMessage.Length)
						aLoggerMessages.AddLast(new LoggerMessage(Logger.Level.notice, sMessage));

					//закомментил, т.к. мне кажется dispose этих pixelsmap'ов совершенно лишний - они и так диспозятся в merge.
					//cStopwatch = Stopwatch.StartNew();
					foreach (PixelsMap cPM in aFrames)
						PixelsMapDispose(cPM);
					//cStopwatch.Stop();
					//aTimings.AddLast(new TimingInfo(TimingInfo.Type.PixelsMapsQueuedForDispose, cStopwatch.Elapsed.TotalMilliseconds));
					if (PixelsMap.bMemoryStarvation || Preferences.nQueueBaetylusLength == (_aqBufferFrame.nCount + 1))
					{
						//    nDoingStart = DateTime.Now.Ticks;
						//    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
						//    nDoingDur = DateTime.Now.Ticks - nDoingStart;
						//    nGCCollectCallsQty++;
						//    if (20 < (new TimeSpan(nDoingDur)).TotalMilliseconds)
						//        aqLoggerMessages.Enqueue(new LoggerMessage(Logger.Level.notice, "Baetylus:Worker:GC.Collect():[dur=" + (new TimeSpan(nDoingDur)).TotalMilliseconds.ToString() + "ms]"));
						//}
						cStopwatch = Stopwatch.StartNew();
						lock (_aqPixelsMapsDisposed)
							while (0 < _aqPixelsMapsDisposed.Count && (PixelsMap.bMemoryStarvation || Preferences.nQueueBaetylusLength == (_aqBufferFrame.nCount + 1)))
								_aqPixelsMapsDisposed.Dequeue().Dispose();
						cStopwatch.Stop();
						aTimings.AddLast(new TimingInfo(TimingInfo.Type.PixelsMapsDispose, cStopwatch.Elapsed.TotalMilliseconds));
					}
					if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
						(new Logger()).WriteDebug2("PART-04: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
					#endregion

					#region logging
					nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
					cStopwatchEffect.Stop();
					nTotalFrameDuration = cStopwatchEffect.Elapsed.TotalMilliseconds;
					if (nTotalFrameDuration > nTotalFrameDurationMax)
						nTotalFrameDurationMax = nTotalFrameDuration;
					if (nLogIndx == 5000)
					{
						aLoggerMessages.AddLast(new LoggerMessage(Logger.Level.notice, "За последние 5000 кадров максимальное время выполнения эффектов в одном кадре составило " + nTotalFrameDurationMax.ToString() + " ms")); //[nGCCollectCallsQty=" + nGCCollectCallsQty + "]");
						nLogIndx = 0;
						nTotalFrameDurationMax = 0;
						//nGCCollectCallsQty = 0;
					}
					if (Logger.bDebug && nTotalFrameDuration > Preferences.nFrameDuration)
					{
						sMessage = "ВНИМАНИЕ! Длительность выполнения эффектов для этого кадра превысила порог: [" + Preferences.nFrameDuration + " ms][dur:" + nTotalFrameDuration.ToString() + " ms]<br>\t";
						foreach (TimingInfo cTI in aTimings)
						{
							if (null == cTI.o || !(cTI.o is IEffect))
								sMessage += "[" + cTI.eType.ToString() + ":" + cTI.nValue + "ms]";
						}
						sMessage += "<br>СПИСОК ЭФФЕКТОВ: [count=" + _aEffects.Count + "]<br>";
						foreach (IEffect iEffect in _aEffects)
						{
							sMessage += "\t[type:" + iEffect.eType + "][status:" + iEffect.eStatus + "][dur:" + iEffect.nDuration + "][layer:" + iEffect.nLayer + "][hash:" + iEffect.GetHashCode() + "]";
							foreach (TimingInfo cTI in aTimings)
							{
								if (null != cTI.o && iEffect == cTI.o)
									sMessage += "[" + cTI.eType.ToString() + ":" + cTI.nValue + "ms]";
							}
							sMessage += "<br>";
						}
						if(0 < sPLLogs.Length)
							sMessage += "\t\t" + sPLLogs;
						aLoggerMessages.AddLast(new LoggerMessage(Logger.Level.notice, sMessage));
					}
					nLogIndx++;

					if (0 < aLoggerMessages.Count)
					{
						cLogger = new Logger();
						foreach (LoggerMessage cLM in aLoggerMessages)
							cLogger.Write(cLM.dt, cLM.eLevel, cLM.sMessage);
						aLoggerMessages.Clear();
					}
					if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)
						(new Logger()).WriteDebug2("PART-05: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]");  // ---------------------------------
					#endregion

					//if (200 == nCount++)
					//    System.Threading.Thread.Sleep(8000);

					Device.Frame cFrameResult = new Device.Frame() { cAudio = cFrameAudio, cVideo = cFrameVideo };  // bug
					if (null == _cBugCatcher)
						_cBugCatcher = new Device.BugCatcher(((Device)_cBoardCurrent)._cVideoFrameEmpty);
					_cBugCatcher.Enqueue(cFrameResult, "baetylus:_aqBufferFrame:" + _aqBufferFrame.nCount);
					_aqBufferFrame.Enqueue(cFrameResult);
					if (cFrameResult.cAudio == null || cFrameResult.cAudio.aFrameBytes == null)
						(new Logger()).WriteNotice("Sent null audio frame! [audio_frame_is_null = " + (cFrameResult.cAudio == null ? "true]" : "false][bytes_is_null = " + (cFrameResult.cAudio.aFrameBytes == null ? "true" : "false") + "]"));

					if (_bDoWritingFrames)
					{
						if (null != cFrameVideo)
						{
							byte[] aBytes = new byte[_nFrameBufSize];
							System.Runtime.InteropServices.Marshal.Copy(cFrameVideo.pFrameBytes, aBytes, 0, (int)_nFrameBufSize);
							lock (_aqWritingFrames)
								_aqWritingFrames.Enqueue(aBytes);
						}
					}

					cFrameAudio = null;
					cFrameVideo = null;
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
			}
		}
		private void WritingFramesWorker(object cState)
		{
			string _sWritingFramesFile = "d:/FramesDebugWriting/WritingDebugFrames.txt";
			string _sWritingFramesDir = "d:/FramesDebugWriting/BTL/";
			int _nFramesCount = 0;
			System.Drawing.Bitmap cBFrame;
			System.Drawing.Imaging.BitmapData cFrameBD;
			string[] aLines;
			bool bQueueIsNotEmpty = false;
			byte[] aBytes; 

			while (true)
			{
				try
				{
					if (System.IO.File.Exists(_sWritingFramesFile))
					{
						aLines = System.IO.File.ReadAllLines(_sWritingFramesFile);
						if ("btl" == aLines.FirstOrDefault(o => o.ToLower() == "btl"))
						{
							_bDoWritingFrames = true;
							if (!System.IO.Directory.Exists(_sWritingFramesDir))
								System.IO.Directory.CreateDirectory(_sWritingFramesDir);
						}
						else
							_bDoWritingFrames = false;
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
							if (null == aLines.FirstOrDefault(o => o.ToLower() == "btl"))
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
						System.Threading.Thread.Sleep(2000);
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
		Device.BugCatcher _cBugCatcher; //bug
		#region callbacks

        Device.Frame OnNextFrame()
        {
			Device.Frame cRetVal = null;
			nVideoBufferCount = _aqBufferFrame.nCount;
            if (0 < nVideoBufferCount)
                cRetVal = _aqBufferFrame.Dequeue();
			return cRetVal;
        }

        #endregion

		#region IContainer implementation
		public event ContainerVideoAudio.EventDelegate EffectAdded;
		public event ContainerVideoAudio.EventDelegate EffectPrepared;
		public event ContainerVideoAudio.EventDelegate EffectStarted;
		public event ContainerVideoAudio.EventDelegate EffectStopped;
		public event ContainerVideoAudio.EventDelegate EffectIsOnScreen;
		public event ContainerVideoAudio.EventDelegate EffectIsOffScreen;
		public event ContainerVideoAudio.EventDelegate EffectFailed;

		internal void EffectsProcess(Dictionary<IEffect, ContainerAction> ahMoveInfos)
		{//заносит в _aqContainerActions все операции для Worker ....
			lock (_aqContainerActions)
			{
				_aqContainerActions.Enqueue(ahMoveInfos);
			}
		}
		internal void EffectsReorder()
		{
			if (null == _aEffects)
				throw new NullReferenceException("internal error: array of effects is null");
			lock (_aqContainerActions)
				_bNeedEffectsReorder = true;
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
                return (ushort)_aEffects.Count;
            }
        }
		ulong IContainer.nSumDuration
		{
			get
			{
				return _aEffects.Max(o => o.nDuration);
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
