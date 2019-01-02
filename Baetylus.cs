#define CUDA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime;
using helpers;
using BTL.Play;

using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Xml.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices;

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
            static public Area stCurrentBTLArea
            {
                get
                {
                    (new Logger()).WriteDebug3("in");

                    return cBaetylus.stFullFrameArea;
                }
            }
        }
        public class MergingWork
        {
            static private object _oLock = new object();
            static private MergingWork _cCurrent;
            static private Queue<MergingWork> _aqMergings = new Queue<MergingWork>();
            static private Queue<List<PixelsMap>> _aqPMsArrays = new Queue<List<PixelsMap>>();
            static private Queue<PixelsMap> _aqPMBackgrounds = new Queue<PixelsMap>();
            static private List<long> _aPMIDsBackgrounds = new List<long>();
            static private bool _bInitiated;
            static private int _nMaxID;
            static private int _nPMsArraysTotal;
            static private int _nPMBackgroundsTotal;
            static private MergingMethod _stMergingMethod;
            static private Area _stArea;
            static private PixelsMap.Format _ePixelFormat;
            static private DisCom.Alpha _eAlpha;

            private Queue<PixelsMap> _aqToDispose;
            private Dictionary<PixelsMap, bool> _ahDisposingPM_boolForce;
            private PixelsMap _cDisposingPM;
            private bool _bDisposingForce;
            public int nID;
            public List<PixelsMap> aPMs;
            private PixelsMap _cPMBackground;
            public PixelsMap cPMForCopyOut;
            public Frame cResultFrame;
            public bool bFrameIsEmpty;

            static public void Init(MergingMethod stMergingMethod, Area stArea, PixelsMap.Format ePixelFormat, DisCom.Alpha eAlpha)
            {
                if (_bInitiated)
                    throw new Exception("MergingWork already initiated");
                _stMergingMethod = stMergingMethod;
                _stArea = stArea;
                _ePixelFormat = ePixelFormat;
                _eAlpha = eAlpha;
                _nMaxID = 0;
                _nPMsArraysTotal = 0;
                _nPMBackgroundsTotal = 0;
                _bInitiated = true;
            }
            private MergingWork()
            {
                if (!_bInitiated)
                    throw new Exception("MergingWork must be inited before ctor");
                _aqToDispose = new Queue<PixelsMap>();
                _ahDisposingPM_boolForce = new Dictionary<PixelsMap, bool>();
                nID = System.Threading.Interlocked.Increment(ref _nMaxID);
                _cPMBackground = GetPMBackground();
                (new Logger()).WriteDebug("new MergingWork created [id=" + nID + "]");
            }
            static public MergingWork Get()
            {
                lock (_oLock)
                {
                    if (_aqMergings.Count == 0)
                        _aqMergings.Enqueue(new MergingWork());
                    _cCurrent = _aqMergings.Peek();
                    _cCurrent.cResultFrame = null;
                    _cCurrent.DisposePMs();     //_cCurrent._aqToDispose.Clear();
                    _cCurrent._ahDisposingPM_boolForce.Clear();
                    _cCurrent.aPMs = GetPMsArray();
                    _cCurrent.bFrameIsEmpty = false;
                    _cCurrent.cPMForCopyOut = null;
                    _cCurrent._cPMBackground.MakeCurrent();
                    return _aqMergings.Dequeue();
                }
            }
            static public void Back(MergingWork cMergingWork)
            {
                if (null == cMergingWork)
                    return;
                lock (_oLock)
                {
                    _aqMergings.Enqueue(cMergingWork);
                }
            }
            static public void EnqueueToDisposeList(PixelsMap cPMToDispose, bool bForce)
            {
                lock (_oLock)
                {
                    _cCurrent.AddToDisposeList(cPMToDispose, bForce);
                }
            }
            static private List<PixelsMap> GetPMsArray()
            {
                lock (_aqPMsArrays)
                {
                    if (_aqPMsArrays.Count == 0)
                    {
                        _aqPMsArrays.Enqueue(new List<PixelsMap>());
                        _nPMsArraysTotal++;
                    }
                    return _aqPMsArrays.Dequeue();
                }
            }
            static private void BackPMsArray(List<PixelsMap> aPMs)
            {
                if (null == aPMs)
                    return;
                lock (_aqPMsArrays)
                {
                    aPMs.Clear();
                    _aqPMsArrays.Enqueue(aPMs);
                }
            }
            static private PixelsMap GetPMBackground()
            {
                lock (_aqPMBackgrounds)
                {
                    if (_aqPMBackgrounds.Count == 0)
                    {
                        PixelsMap cBackground = new PixelsMap(_stMergingMethod, _stArea, _ePixelFormat, true);
                        cBackground.eAlpha = _eAlpha;
                        cBackground.Allocate();
                        cBackground.bKeepAlive = true;

                        _aqPMBackgrounds.Enqueue(cBackground);
                        _aPMIDsBackgrounds.Add(cBackground.nID);
                        _nPMBackgroundsTotal++;
                    }
                    return _aqPMBackgrounds.Dequeue();
                }
            }
            static private void BackPMBackground(PixelsMap cPM)
            {
                if (null == cPM || !_aPMIDsBackgrounds.Contains(cPM.nID))
                    return;
                lock (_aqPMBackgrounds)
                {
                    _aqPMBackgrounds.Enqueue(cPM);
                }
            }
            public void AddToDisposeList(PixelsMap cPMToDispose, bool bForce)
            {
                lock (_oLock)
                {
                    if (cPMToDispose.bDisposed)
                        return;

                    if (_ahDisposingPM_boolForce.ContainsKey(cPMToDispose))
                    {
                        if (!_ahDisposingPM_boolForce[cPMToDispose] && bForce)
                        {
                            _ahDisposingPM_boolForce[cPMToDispose] = bForce;
                        }
                    }
                    else
                    {
                        _ahDisposingPM_boolForce.Add(cPMToDispose, bForce);
                        _aqToDispose.Enqueue(cPMToDispose);
                    }
                }
            }
            public void DisposePMs()
            {
                while (_aqToDispose.Count > 0)
                {
                    lock (_oLock)
                    {
                        _cDisposingPM = _aqToDispose.Dequeue();
                        _bDisposingForce = _ahDisposingPM_boolForce[_cDisposingPM];
                        _ahDisposingPM_boolForce.Remove(_cDisposingPM); //if (cDispose.bForce) cDispose.cPixelsMap.bKeepAlive = false;
                    }
                    _cDisposingPM.Dispose(_bDisposingForce);
                }
            }
            public void Merge()
            {
                _cPMBackground.Merge(aPMs, true);
                cPMForCopyOut = _cPMBackground;
                BackPMsArray();
            }
            public void BackPMsArray()
            {
                if (null == aPMs)
                    return;

                foreach (PixelsMap cPM in aPMs)
                    AddToDisposeList(cPM, false);
                BackPMsArray(aPMs);
                aPMs = null;
            }
            public override string ToString()
            {
                return "[id=" + nID + "][total=" + _nMaxID + "]";
            }
        }
        public class Frame : helpers.Frame
        {
            new public class Audio : helpers.Frame.Audio
            {
                override public void Dispose()
                {
                    lock (_oDisposeLock)
                    {
                        if (bDisposed)
                            return;
                        bDisposed = true;
                    }
                    if (null != aFrameBytes)
                        _cBinM.BytesBack(aFrameBytes, 0);
                }
            }
            new public class Video : helpers.Frame.Video
            {
            }
            new public Audio cAudio;
            new public Video cVideo;
        }
        private class LoggerMessage
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
        private class AudioChannelMapping
        {
            public Bytes aBuffer;
            public byte nBufferChannel;
            public byte nBufferChannelsQty;
        }

        static public BytesInMemory _cBinM;
        static private Bytes _cAudioSilence2Channels;
        static public Bytes cAudioSilence2Channels
        {
            get
            {
                if (null == _cAudioSilence2Channels)
                {
                    _cAudioSilence2Channels = new Bytes() { aBytes = new byte[2 * Preferences.nAudioBytesPerFramePerChannel], nID = -1 };
                    _cBinM.AddToIgnor(_cAudioSilence2Channels);
                }
                return _cAudioSilence2Channels;
            }
        }
        static private Bytes _cEmptyBytes;
        static public uint nQueueDeviceLength;
        static public uint nCurrentDeviceBufferCount;
        static public int nBufferOneThird;
        static public int nBufferTwoThird;
        static private Queue<Dictionary<IEffect, ContainerAction>> _aqContainerActions;
        static private bool _bNeedEffectsReorder;
        static private ushort _nReferencesQty;
        static private object _cSyncRoot = new object();
        static private List<IEffect> _aEffects;
        static private ThreadBufferQueue<MergingWork> _aqWorkForMerge;
        static private ThreadBufferQueue<MergingWork> _aqWorkMerged;
        //static public Mutex cMutArea;

        private Thread _cThreadWorkerFrameNext;
        private Thread _cThreadWorkerMerge;
        private Thread _cThreadWorkerCopyOut;
        private Thread _cThreadWritingFramesWorker;
        private bool _bDoWritingFrames;
        private int nWriteEveryNFrame, nWriteIndx;
        private Queue<byte[]> _aqWritingFrames;
        private Queue<byte[]> _aqWritingAudioFrames;
        private int _nFrameBufSize;
        private int _nFullFrameBytes;
        private int _nLoggingPerFrames;
        private NamedPipeServerStream _cServerPipeStream;
        private BinaryFormatter _cBinFormatter;
        private Area _stFullFrameArea;
        public Area stFullFrameArea
        {
            get
            {
                return _stFullFrameArea;
            }
        }

        static Baetylus()
        {
            _cBinM = new BytesInMemory("baetylus bytes");
            _cEmptyBytes = new Bytes() { aBytes = new byte[0], nID = -1 };
            _cBinM.AddToIgnor(_cEmptyBytes);
        }
        public Baetylus()
        {
            (new Logger()).WriteDebug3("in");
            StreamReader cPipeSReader;
            StreamWriter cPipeSWriter;
            Thread _PipeServerThread;

            lock (_cSyncRoot)
            {
                if (1 > _nReferencesQty)
                {
                    (new Logger()).WriteDebug4("baetylus:constructor:init");
                    _aqContainerActions = new Queue<Dictionary<IEffect, ContainerAction>>();

                    #region pipe
                    _cServerPipeStream = new NamedPipeServerStream("FramesGettingPipe-" + Preferences.sDeviceMake + "-" + Preferences.nDeviceTarget + "-" + Preferences.nTargetChannel);
                    (new Logger()).WriteNotice("PIPE Server started and waiting for the device..." + Preferences.nDeviceTarget);

                    _cServerPipeStream.WaitForConnection();
                    (new Logger()).WriteNotice("PIPE BTL accepted connection " + Preferences.nDeviceTarget);

                    _cBinFormatter = new BinaryFormatter();
                    cPipeSReader = new StreamReader(_cServerPipeStream);
                    cPipeSWriter = new StreamWriter(_cServerPipeStream);
                    cPipeSWriter.AutoFlush = true;
                    string sQuest;

                    cPipeSWriter.WriteLine("get_area");
                    while (null == (sQuest = cPipeSReader.ReadLine())) { System.Threading.Thread.Sleep(10); }
                    _stFullFrameArea = (Area)(new XmlSerializer(typeof(Area), new XmlRootAttribute() { ElementName = "stArea", IsNullable = false })).Deserialize(new System.IO.StringReader(sQuest));
                    (new Logger()).WriteNotice("PIPE BTL got area [w=" + _stFullFrameArea.nWidth + "; h=" + _stFullFrameArea.nHeight + "]");

                    cPipeSWriter.WriteLine("get_fps");
                    while (null == (sQuest = cPipeSReader.ReadLine())) { System.Threading.Thread.Sleep(10); }
                    Preferences.nFPS = ushort.Parse(sQuest);
                    (new Logger()).WriteNotice("PIPE BTL got fps [" + Preferences.nFPS + "]");

                    cPipeSWriter.WriteLine("get_buffer_size");
                    nQueueDeviceLength = (uint)_cBinFormatter.Deserialize(_cServerPipeStream);
                    (new Logger()).WriteNotice("PIPE BTL got buffer_size [" + nQueueDeviceLength + "]");
                    nBufferOneThird = (int)nQueueDeviceLength / 3;
                    nBufferTwoThird = 2 * nBufferOneThird;

                    //_PipeServerThread = new Thread(ThreadStartServer);
                    //_PipeServerThread.IsBackground = true;
                    //_PipeServerThread.Priority = System.Threading.ThreadPriority.Normal;
                    //_PipeServerThread.Start();
                    #endregion






                    _aEffects = new List<IEffect>();
                    //_stFullFrameArea = cBoard.stArea;
                    _nLoggingPerFrames = 1000;
                    _aqWorkForMerge = new ThreadBufferQueue<MergingWork>(2, true, true);
                    _aqWorkMerged = new ThreadBufferQueue<MergingWork>(2, true, true);
                    _bNeedEffectsReorder = false;
                    _cThreadWorkerFrameNext = new System.Threading.Thread(WorkerFrameNext);
                    _cThreadWorkerFrameNext.IsBackground = true;
                    _cThreadWorkerFrameNext.Priority = System.Threading.ThreadPriority.Highest;
                    _cThreadWorkerFrameNext.Start();
                    _cThreadWorkerMerge = new System.Threading.Thread(WorkerMerge);
                    _cThreadWorkerMerge.IsBackground = true;
                    _cThreadWorkerMerge.Priority = System.Threading.ThreadPriority.Highest;
                    _cThreadWorkerMerge.Start();
                    _cThreadWorkerCopyOut = new System.Threading.Thread(WorkerCopyOut);
                    _cThreadWorkerCopyOut.IsBackground = true;
                    _cThreadWorkerCopyOut.Priority = System.Threading.ThreadPriority.Highest;
                    _cThreadWorkerCopyOut.Start();
                    _bDoWritingFrames = false;
#if DEBUG
                    _aqWritingFrames = new Queue<byte[]>();
                    _aqWritingAudioFrames = new Queue<byte[]>();
                    _cThreadWritingFramesWorker = new System.Threading.Thread(WritingFramesWorker);
                    _cThreadWritingFramesWorker.IsBackground = true;
                    _cThreadWritingFramesWorker.Priority = System.Threading.ThreadPriority.Normal;
                    _cThreadWritingFramesWorker.Start();
#endif

                    cPipeSWriter.WriteLine("turn_on");
                }
                _nReferencesQty++;
            }
            (new Logger()).WriteDebug4("return [refqty:" + _nReferencesQty + "]");
        }
        ~Baetylus()
        {
            try
            {
                _nReferencesQty--;
                if (1 > _nReferencesQty)
                {
                }
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
        }

        static internal void PixelsMapDispose(PixelsMap[] aPixelsMaps)
        {
            foreach (PixelsMap cPixelsMap in aPixelsMaps)
                PixelsMapDispose(cPixelsMap, false);
        }
        static internal void PixelsMapDispose(PixelsMap.Triple cPixelsMapDuo, bool bForce)
        {
            if (null == cPixelsMapDuo)
                return;
            foreach (PixelsMap cPM in cPixelsMapDuo.aAllUsedPMs)
                PixelsMapDispose(cPM, bForce);
        }
        static internal void PixelsMapDispose(PixelsMap cPixelsMap)
        {
            PixelsMapDispose(cPixelsMap, false);
        }
        static internal void PixelsMapDispose(PixelsMap cPixelsMap, bool bForce)
        {
            if (cPixelsMap == null)
                return;

            MergingWork.EnqueueToDisposeList(cPixelsMap, bForce);
        }

        private void WorkerFrameNext(object cState)
        {
            (new Logger()).WriteNotice("WorkerFrameNext started");
            (new Logger()).WriteDebug("GC_INFO: [btl_generation=" + GC.GetGeneration(this) + "][LatencyMode=" + GCSettings.LatencyMode + "][is_server=" + GCSettings.IsServerGC + "]");
            _nFullFrameBytes = stFullFrameArea.nHeight * stFullFrameArea.nWidth * 4;
            LinkedList<LoggerMessage> aLoggerMessages = new LinkedList<LoggerMessage>();
            Logger cLogger;
            IVideo iVideoEffect = null;
            MergingWork.Init(Preferences.stMerging, _stFullFrameArea, PixelsMap.Format.ARGB32, Preferences.bBackgroundAlpha ? DisCom.Alpha.normal : DisCom.Alpha.none);
            PixelsMap cFrame = null;
            Bytes aFrameBufferAudio = null, aBytesAudio = null;
            List<PixelsMap> aFrames;
            Dictionary<IEffect, ContainerAction> ahMoveInfo = null;
            int nIndx, nLogIndx = 0;
            bool bIsStarted = false;
            double nTotalFrameDuration, nTotalFrameDurationMax = 0, nTotalFrameDurationMin = double.MaxValue, nTotalDurationSum = 0;

            List<IVideo> aOpacityEffects = new List<IVideo>();
            Dictionary<IAudio, Bytes> ahAudioEffects = new Dictionary<IAudio, Bytes>(); ;

            AudioChannelMapping[] aAudioChannelMappings = null;
            uint nSampleBytesQty = Preferences.nAudioByteDepth;
            uint nChannelSamplesQty = Preferences.nAudioSamplesPerFrame;
            uint nChannelBytesQty = Preferences.nAudioSamplesPerFrame * Preferences.nAudioByteDepth;
            uint nChannelBytesQtyToSend = Preferences.nAudioSamplesPerFrame * Preferences.nAudioByteDepthToSend;
            byte nChannelsQty = 0;
            byte[] aChannels;
            int nSource = 0, nDestination = 0;
            bool bVideoFrame = false;
            bool bWroteNotice = false;
            bool bWroteWarning = false;
            IAudio iAudioEffect;
            _nFrameBufSize = _stFullFrameArea.nWidth * _stFullFrameArea.nHeight * 4;
            Logger.Timings cTimings = new helpers.Logger.Timings("BTL: WorkerFrameNext");
            List<long> aHashesToBack = new List<long>();
            Dictionary<ulong, List<IEffect>> ahSimultanious = new Dictionary<ulong, List<IEffect>>();
            MergingWork cMergingWork = null;
            bool bWasEnqueue = false;

            while (null != _aEffects)
            {
                try
                {
                    bWasEnqueue = false;
                    cTimings.TotalRenew();
                    cMergingWork = MergingWork.Get();
                    aFrames = cMergingWork.aPMs;
                    aOpacityEffects.Clear();
                    aFrameBufferAudio = null;
                    iVideoEffect = null;
                    ahAudioEffects.Clear();
                    aAudioChannelMappings = null;

                    if (1 > _aEffects.Count && (bVideoFrame || Preferences.bClearScreenOnEmpty)) //добавляем в очередь признак необходимости очистить экран, иначе девайс будет повторять предыдущий кадр до потери сознания
                    {
                        if (!bWroteNotice)
                        {
                            (new Logger()).WriteNotice("вставляем черное поле...");
                            bWroteNotice = true;
                        }
                        cMergingWork.bFrameIsEmpty = true;
                        _aqWorkForMerge.Enqueue(cMergingWork);
                        bWasEnqueue = true;
                        bVideoFrame = false;
                    }
                    else
                    {
                        bWroteNotice = false;
                    }
                    cTimings.Restart("before FOR");

                    long nStart0 = DateTime.Now.Ticks;     // ------------------------------- 
                    #region for
                    for (nIndx = _aEffects.Count - 1; 0 <= nIndx; nIndx--)
                    {
                        cTimings.Restart("<br>\t\t\tFOR i=" + nIndx + "[type:" + _aEffects[nIndx].eType + "][layer:" + _aEffects[nIndx].nLayer + "]");
                        try
                        {
                            if (!bIsStarted)
                                bIsStarted = true;
                            if (null == _aEffects[nIndx])
                            {
                                (new Logger()).WriteError("effect can't be null");
                                continue;
                            }
                            if (((Effect)_aEffects[nIndx]).bSimultaneousWait)
                                continue;
                            if (!(_aEffects[nIndx] is IVideo) && !(_aEffects[nIndx] is IAudio))
                                continue;
                            if (EffectStatus.Running != _aEffects[nIndx].eStatus)
                                continue;
                            if (0 < _aEffects[nIndx].nDelay)
                            {
                                _aEffects[nIndx].nDelay--;
                                continue;
                            }
                            cTimings.Restart("F1");
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
                                    iVideoEffect.nPixelsMapSyncIndex = PixelsMap.nIndexTripleCurrent;
                                    cFrame = iVideoEffect.FrameNext();

                                    if (null != cFrame && cFrame.nID == 0)
                                    {
                                        (new Logger()).WriteError("got pixelmap with id==0 [type=" + (_aEffects[nIndx].eType) + "][hc=" + _aEffects[nIndx].nID + "]");
                                        PixelsMapDispose(cFrame);
                                        cFrame = null;
                                    }
                                    if (null != cFrame)
                                    {
                                        aFrames.Insert(0, cFrame);
                                        if (null != iVideoEffect.iMask)
                                        {
                                            iVideoEffect.iMask.nPixelsMapSyncIndex = PixelsMap.nIndexTripleCurrent;
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
                            cTimings.Restart("FrNext");
                            if (_aEffects[nIndx] is IAudio)
                            {
                                iAudioEffect = (IAudio)_aEffects[nIndx];
                                if (null == (aChannels = iAudioEffect.aChannels) || 0 < aChannels.Length) //см. комменты в IAudio
                                    ahAudioEffects.Add(iAudioEffect, iAudioEffect.FrameNext());
                                else
                                    iAudioEffect.Skip();
                            }
                            cTimings.Restart("AudNext");
                            if (null != iAudioEffect && ahAudioEffects.ContainsKey(iAudioEffect))
                            {
                                if (null == ahAudioEffects[iAudioEffect] && aFrames.Contains(cFrame))
                                {
                                    (new Logger()).WriteDebug2("removing audio frame because of NULL bytes [fr_total:" + ((IEffect)iAudioEffect).nFramesTotal + "][dur:" + ((IEffect)iAudioEffect).nDuration + "][layer:" + ((IEffect)iAudioEffect).nLayer + "]");
                                    aFrames.Remove(cFrame);
                                    PixelsMapDispose(cFrame);
                                    if (null != iVideoEffect.iMask)
                                    {
                                        PixelsMapDispose(aFrames[0]);
                                        aFrames.RemoveAt(0);
                                    }
                                }
                                if (!bVideoFrame)
                                {
                                    (new Logger()).WriteDebug2("removing audio frame because of null video [fr_total:" + ((IEffect)iAudioEffect).nFramesTotal + "][dur:" + ((IEffect)iAudioEffect).nDuration + "][layer:" + ((IEffect)iAudioEffect).nLayer + "]");
                                    _cBinM.BytesBack(ahAudioEffects[iAudioEffect], 1);
                                    ahAudioEffects.Remove(iAudioEffect);
                                }
                            }
                            cTimings.Restart("F4");
                        }
                        catch (Exception ex)
                        {
                            if (!ex.Message.StartsWith("effect stopped and must be prepared again")) // пока тут дёргаем некст фрейм уже иногда стопят
                                (new Logger()).WriteError("catch-1 [message=]", ex);
                        }
                    }
                    #endregion
                    cTimings.Restart("<br>\tEnd FOR subtotal:" + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms ");
                    #region audio mappings
                    if (0 < ahAudioEffects.Count)
                    {
                        foreach (IAudio iAudio in _aEffects.Where(o => o is IAudio && ahAudioEffects.Keys.Contains((IAudio)o)).Reverse().Select(o => (IAudio)o))  // верхние закрывают нижние 
                        {
                            if (null == (aBytesAudio = ahAudioEffects[iAudio]))  // если звука нет - не берем
                            {
                                continue;
                            }
                            nChannelsQty = (byte)(aBytesAudio.Length / nChannelBytesQty);
                            aChannels = iAudio.aChannels;
                            if (null == aChannels)                              // по-умолчанию расставляем каналы на 0, 1, ...
                            {
                                aChannels = new byte[nChannelsQty];
                                for (byte nChannel = 0; nChannelsQty > nChannel; nChannel++)
                                    aChannels[nChannel] = nChannel;
                            }
                            if (null == aAudioChannelMappings)
                            {
                                if (Preferences.nAudioChannelsQty == aChannels.Length)   //если эффект сразу покрыл все каналы, то смысла дальше искать нет
                                {
                                    aFrameBufferAudio = aBytesAudio;
                                    break;
                                }
                                else
                                    aAudioChannelMappings = new AudioChannelMapping[Preferences.nAudioChannelsQty];     // в противном случае начинаем распределять по каналам - массив на все каналы
                            }
                            for (byte nChannel = 0; aChannels.Length > nChannel; nChannel++)
                            {
                                if (aChannels[nChannel] < aAudioChannelMappings.Length && null == aAudioChannelMappings[aChannels[nChannel]])   // ткнём куда не занято
                                {
                                    aAudioChannelMappings[aChannels[nChannel]] = new AudioChannelMapping();
                                    aAudioChannelMappings[aChannels[nChannel]].aBuffer = aBytesAudio;
                                    aAudioChannelMappings[aChannels[nChannel]].nBufferChannel = nChannel;
                                    aAudioChannelMappings[aChannels[nChannel]].nBufferChannelsQty = (byte)aChannels.Length;
                                }
                            }
                        }
                        if (null != aAudioChannelMappings)
                        {
                            aHashesToBack.Clear();
                            int nByte = 0;
                            aFrameBufferAudio = _cBinM.BytesGet((int)(Preferences.nAudioChannelsQty * nChannelBytesQtyToSend), 1);        //new byte[Preferences.nAudioChannelsQty * nChannelBytesQty];
                            for (int nChannel = 0; aAudioChannelMappings.Length > nChannel; nChannel++)
                            {
                                if (null == aAudioChannelMappings[nChannel])
                                    continue;
                                for (int nSampleIndx = 0; nChannelSamplesQty > nSampleIndx; nSampleIndx++)
                                {
                                    try
                                    {
                                        nSource = (int)(((nSampleIndx * aAudioChannelMappings[nChannel].nBufferChannelsQty) + aAudioChannelMappings[nChannel].nBufferChannel) * nSampleBytesQty);
                                        nDestination = (int)(((nSampleIndx * aAudioChannelMappings.Length) + nChannel) * Preferences.nAudioByteDepthToSend);
                                        for (nByte = 0; nSampleBytesQty > nByte; nByte++)
                                        {
                                            if (aAudioChannelMappings[nChannel].aBuffer.Length <= nSource + nByte)
                                            {
                                                (new Logger()).WriteError("worker.audio: bytes audio are less then expected [buffer=" + aAudioChannelMappings[nChannel].aBuffer.Length + "][expected=" + (nSource + nByte) + "]");
                                                break;
                                            }
                                            if (Preferences.nAudioByteDepthToSend == nSampleBytesQty) // for decklink and display
                                                aFrameBufferAudio.aBytes[nDestination + nByte] = aAudioChannelMappings[nChannel].aBuffer.aBytes[nSource + nByte];
                                            else // for aja
                                                aFrameBufferAudio.aBytes[nDestination + (Preferences.nAudioByteDepthToSend - nSampleBytesQty) + nByte] = aAudioChannelMappings[nChannel].aBuffer.aBytes[nSource + nByte];
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        (new Logger()).WriteError("catch-2", ex);
                                        (new Logger()).WriteDebug("CATCH: [nChannelSamplesQty=" + nChannelSamplesQty + "][nSampleBytesQty=" + nSampleBytesQty + "][nSampleIndx=" + nSampleIndx + "][nChannel=" + nChannel + "][aFrameBufferAudio.len=" + aFrameBufferAudio.Length + "][nDestination=" + nDestination + "][nByte=" + nByte + "][nSource=" + nSource + "]");
                                    }
                                }
                                if (!aHashesToBack.Contains(aAudioChannelMappings[nChannel].aBuffer.nID))
                                {
                                    _cBinM.BytesBack(aAudioChannelMappings[nChannel].aBuffer, 2);
                                    aHashesToBack.Add(aAudioChannelMappings[nChannel].aBuffer.nID);
                                }
                            }
                        }
                    }
                    #endregion audio mappings
                    cTimings.Restart("Audio");
                    #region  container actions
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
                                                if (((Effect)cEffect).bSimultaneousWait)
                                                {
                                                    if (!ahSimultanious.ContainsKey(((Effect)cEffect).nSimultaneousID))
                                                        ahSimultanious.Add(((Effect)cEffect).nSimultaneousID, new List<IEffect>());
                                                    ahSimultanious[((Effect)cEffect).nSimultaneousID].Add(cEffect);
                                                    if (ahSimultanious[((Effect)cEffect).nSimultaneousID].Count == ((Effect)cEffect).nSimultaneousTotalQty)
                                                    {
                                                        foreach (IEffect cE in ahSimultanious[((Effect)cEffect).nSimultaneousID])
                                                            ((Effect)cE).bSimultaneousWait = false;
                                                        ahSimultanious.Remove(((Effect)cEffect).nSimultaneousID);
                                                    }
                                                }
                                            }
                                            else
                                                throw new Exception("this container already has specified effect");
                                            break;
                                        case ContainerAction.Remove:
                                            if (_aEffects.Contains(cEffect))
                                                _aEffects.Remove(cEffect);
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
                                        aLoggerMessages.AddLast(new LoggerMessage(Logger.Level.debug3, "effects reordered: " + _aEffects[nOutterIndx].eType.ToString() + "[" + _aEffects[nOutterIndx].nID + "] has layer " + _aEffects[nOutterIndx].nLayer + " and " + _aEffects[nInnerIndx].eType.ToString() + "[" + _aEffects[nInnerIndx].nID + "] has layer " + _aEffects[nInnerIndx].nLayer));
                                        cEffect = _aEffects[nOutterIndx];
                                        _aEffects[nOutterIndx] = _aEffects[nInnerIndx];
                                        _aEffects[nInnerIndx] = cEffect;
                                    }
                                }
                            }
                            _bNeedEffectsReorder = false;
                        }
                    }
                    #endregion
                    cTimings.Restart("Container");

                    if ((null == iVideoEffect && 1 > ahAudioEffects.Count) || (null != iVideoEffect && 1 > aFrames.Count) || (0 < ahAudioEffects.Count && null == aFrameBufferAudio)) //(1 > aFrames.Count && null == aFrameBufferAudio) || 
                    {
                        if (bIsStarted && !bWroteWarning)
                        {
                            (new Logger()).WriteWarning("we're sleeping  [aFrames.Count = " + aFrames.Count + "] [aFrameBufferAudio is null = " + aFrameBufferAudio.IsNullOrEmpty() + "] [aAudioEffects.Count = " + ahAudioEffects.Count + "]");
                            bWroteWarning = true;
                        }
                        System.Threading.Thread.Sleep(10);
                        if (null != aFrameBufferAudio)
                           _cBinM.BytesBack(aFrameBufferAudio, 3);
                        cMergingWork.BackPMsArray();
                        if (!bWasEnqueue)
                            MergingWork.Back(cMergingWork);
                        continue;  //sMessage = ":got null video:";
                    }
                    bWroteWarning = false;

                    cMergingWork.cResultFrame = new Frame();
                    cMergingWork.cResultFrame.cVideo = new Frame.Video();
                    cMergingWork.cResultFrame.cAudio = new Frame.Audio();
                    cMergingWork.cResultFrame.cAudio.aFrameBytes = aFrameBufferAudio;

                    if (null != iVideoEffect)
                    {
                        cMergingWork.cResultFrame.cVideo.oFrameBytes = _cBinM.BytesGet(_nFullFrameBytes, 5);
                        cTimings.Restart("bytes_get");
                    }

                    #region logging
                    if (nLogIndx >= _nLoggingPerFrames)
                    {
                        aLoggerMessages.AddLast(new LoggerMessage(Logger.Level.notice, "WorkerFrameNext За последние " + nLogIndx + " кадров [min=" + nTotalFrameDurationMin + "][max=" + nTotalFrameDurationMax + "][average=" + (nTotalDurationSum / _nLoggingPerFrames) + "] ms"));
                        nLogIndx = 0;
                        nTotalFrameDurationMax = 0;
                        nTotalDurationSum = 0;
                        nTotalFrameDurationMin = double.MaxValue;
                    }
                    nLogIndx++;

                    if (0 < aLoggerMessages.Count)
                    {
                        cLogger = new Logger();
                        foreach (LoggerMessage cLM in aLoggerMessages)
                            cLogger.Write(cLM.dt, cLM.eLevel, cLM.sMessage);
                        aLoggerMessages.Clear();
                    }
                    cTimings.Restart("logging");

                    nTotalFrameDuration = cTimings.Stop("BTL.WorkerFrameNext.Before_enqueue", "[mode=" + GCSettings.LatencyMode + "][queue=" + nCurrentDeviceBufferCount + "]", (ulong)Preferences.nFrameDuration);
                    nTotalDurationSum += nTotalFrameDuration;
                    if (nTotalFrameDuration > nTotalFrameDurationMax)
                        nTotalFrameDurationMax = nTotalFrameDuration;
                    if (nTotalFrameDuration < nTotalFrameDurationMin)
                        nTotalFrameDurationMin = nTotalFrameDuration;
                    #endregion logging

                    if (1 == cMergingWork.aPMs.Count && _stFullFrameArea == cMergingWork.aPMs[0].stArea)
                    {
                        cMergingWork.cPMForCopyOut = cMergingWork.aPMs[0];
                        lock (_aqWorkForMerge.oSyncRoot)
                        {
                            while (_aqWorkForMerge.nCount > 0)
                                System.Threading.Monitor.Wait(_aqWorkForMerge.oSyncRoot);
                        }
                        _aqWorkMerged.Enqueue(cMergingWork);
                        bWasEnqueue = true;
                    }
                    else
                    {
                        _aqWorkForMerge.Enqueue(cMergingWork);
                        bWasEnqueue = true;
                    }
                }
                catch (Exception ex)
                {
                    if (!bWasEnqueue)
                        MergingWork.Back(cMergingWork);
                    (new Logger()).WriteError("BTL.FrNEXT.CATCH!!", ex);
                    cTimings.Stop("BTL.FrNEXT.CATCH!!", "[queue:" + nCurrentDeviceBufferCount + "]", 0);
                }
            }
        }
        private void WorkerMerge(object cState)
        {
            (new Logger()).WriteNotice("WorkerMerge started");
            MergingWork cMergingWork = null;
            Logger.Timings cTimings = new helpers.Logger.Timings("BTL: WorkerMerge");
            int nLogIndx = 0;
            double nTotalFrameDuration, nTotalFrameDurationMax = 0, nTotalFrameDurationMin = double.MaxValue, nTotalDurationSum = 0;

            while (null != _aEffects)
            {
                try
                {
                    cMergingWork = _aqWorkForMerge.Peek();
                    cTimings.TotalRenew();

                    if (cMergingWork.bFrameIsEmpty)
                    {
                        cMergingWork.cResultFrame = new Frame() { cAudio = null, cVideo = new Frame.Video() { oFrameBytes = _cEmptyBytes } };
                    }
                    else
                    {
                        (new Logger()).WriteDebug4("[" + _stFullFrameArea.nWidth + "x" + _stFullFrameArea.nHeight + "--" + _stFullFrameArea.nLeft + ":" + _stFullFrameArea.nTop + "]");  //DNF

                        cMergingWork.Merge();
                        cMergingWork.BackPMsArray();

                        #region logging
                        if (nLogIndx >= _nLoggingPerFrames)
                        {
                            (new Logger()).WriteNotice("WorkerMerge За последние " + nLogIndx + " кадров [min=" + nTotalFrameDurationMin + "][max=" + nTotalFrameDurationMax + "][average=" + (nTotalDurationSum / _nLoggingPerFrames) + "] ms");
                            nLogIndx = 0;
                            nTotalFrameDurationMax = 0;
                            nTotalDurationSum = 0;
                            nTotalFrameDurationMin = double.MaxValue;
                        }
                        nLogIndx++;

                        nTotalFrameDuration = cTimings.Stop("BTL.WorkerMerge.Before_enqueue", "[mode=" + GCSettings.LatencyMode + "][queue=" + nCurrentDeviceBufferCount + "]", (ulong)Preferences.nFrameDuration);
                        nTotalDurationSum += nTotalFrameDuration;
                        if (nTotalFrameDuration > nTotalFrameDurationMax)
                            nTotalFrameDurationMax = nTotalFrameDuration;
                        if (nTotalFrameDuration < nTotalFrameDurationMin)
                            nTotalFrameDurationMin = nTotalFrameDuration;
                        #endregion logging
                    }
                    _aqWorkMerged.Enqueue(cMergingWork);
                    _aqWorkForMerge.Dequeue();
                }
                catch (Exception ex)
                {
                    _aqWorkForMerge.Dequeue();
                    (new Logger()).WriteError("BTL.MERGE.CATCH!!", ex);
                    cTimings.Stop("BTL.MERGE.CATCH!!", "[queue:" + nCurrentDeviceBufferCount + "][cMergingWork=" + (null == cMergingWork ? "NULL" : cMergingWork.ToString()) + "]", 0);
                }
            }

        }
        private void WorkerCopyOut(object cState)
        {
            (new Logger()).WriteNotice("WorkerCopyOut started");
            MergingWork cMergingWork = null;
            int nIndxGCForced = 0;
            uint nIndxQueue = 0;
            long nMem;
            ulong nTimingDur = 0;
            int nTimingIndex = 0;
            int nTimingMaxIndex = 500 / Preferences.nGCFramesInterval;
            if (nTimingMaxIndex == 0)
                nTimingMaxIndex = 1;
            Logger.Timings cTimings = new helpers.Logger.Timings("BTL: WorkerCopyOut");
            int nLogIndx = 0;
            double nTotalFrameDuration, nTotalFrameDurationMax = 0, nTotalFrameDurationMin = double.MaxValue, nTotalDurationSum = 0;

            while (null != _aEffects)
            {
                try
                {
                    cMergingWork = _aqWorkMerged.Peek();
                    cTimings.TotalRenew();

                    //if (400 == nTMPCount)      //    =================    SLEEP    TEST  ===========================
                    //    System.Threading.Thread.Sleep(10000);
                    //nTMPCount++; 

                    if (null != cMergingWork.cPMForCopyOut)
                    {
                        if (cMergingWork.cResultFrame.cVideo.oFrameBytes is IntPtr)
                            cMergingWork.cPMForCopyOut.CopyOut(cMergingWork.cResultFrame.cVideo.pFrameBytes);
                        else if (cMergingWork.cResultFrame.cVideo.oFrameBytes is Bytes)
                            cMergingWork.cPMForCopyOut.CopyOut(cMergingWork.cResultFrame.cVideo.aFrameBytes.aBytes);
                        cTimings.Restart("copyout");
                    }

                    cMergingWork.BackPMsArray();
                    cMergingWork.DisposePMs();
                    cTimings.Restart("disposing");

                    #region DoWritingFrames
#if DEBUG
                    if (_bDoWritingFrames)
                    {
                        if (null != cMergingWork?.cResultFrame?.cVideo?.oFrameBytes)
                        {
                            nWriteIndx++;
                            if (nWriteIndx >= nWriteEveryNFrame)
                            {
                                nWriteIndx = 0;
                                byte[] aBytes = new byte[_nFrameBufSize];
                                if (cMergingWork.cResultFrame.cVideo.oFrameBytes is IntPtr)
                                    System.Runtime.InteropServices.Marshal.Copy(cMergingWork.cResultFrame.cVideo.pFrameBytes, aBytes, 0, (int)_nFrameBufSize);
                                else
                                    Array.Copy(cMergingWork.cResultFrame.cVideo.aFrameBytes.aBytes, aBytes, (int)_nFrameBufSize);
                                lock (_aqWritingFrames)
                                    _aqWritingFrames.Enqueue(aBytes);
                            }
                        }
                        if (null != cMergingWork?.cResultFrame?.cAudio?.aFrameBytes)
                            lock (_aqWritingAudioFrames)
                                _aqWritingAudioFrames.Enqueue(cMergingWork.cResultFrame.cAudio.aFrameBytes.aBytes);
                        cTimings.Restart("writing frames");
                    }
#endif
                    #endregion DoWritingFrames

                    #region logging + GC
                    if (null != cMergingWork.cPMForCopyOut)
                    {
                        if (nLogIndx >= _nLoggingPerFrames)
                        {
                            (new Logger()).WriteNotice("WorkerCopyOut За последние " + nLogIndx + " кадров [min=" + nTotalFrameDurationMin + "][max=" + nTotalFrameDurationMax + "][average=" + (nTotalDurationSum / _nLoggingPerFrames) + "] ms");  //[nGCCollectCallsQty=" + nGCCollectCallsQty + "]");
                            nLogIndx = 0;
                            nTotalFrameDurationMax = 0;
                            nTotalDurationSum = 0;
                            nTotalFrameDurationMin = double.MaxValue;
                            //nGCCollectCallsQty = 0;
                        }
                        nLogIndx++;

                        if (nCurrentDeviceBufferCount > nBufferTwoThird)
                            GCSettings.LatencyMode = GCLatencyMode.Interactive;
                        else if (nCurrentDeviceBufferCount > nBufferOneThird)
                            GCSettings.LatencyMode = GCLatencyMode.LowLatency;
                        else
                            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

                        if (Preferences.nGCFramesInterval > 0 && Preferences.nGCFramesInterval < nIndxGCForced++ && nCurrentDeviceBufferCount > nBufferTwoThird)
                        {
                            nMem = GC.GetTotalMemory(false);
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false);
                            GC.WaitForFullGCComplete(1000);
                            if (nTimingIndex++ > nTimingMaxIndex)
                            {
                                nTimingIndex = 0;
                                nTimingDur = 0;
                            }
                            else
                                nTimingDur = (ulong)Preferences.nFrameDuration + 10;
                            nTotalFrameDuration = cTimings.Stop("BTL.Before_enqueue", "GC-Optimized " + "[frames_ago=" + nIndxGCForced + "][mode=" + GCSettings.LatencyMode + "][queue:" + nCurrentDeviceBufferCount + "][gc_count:[0=" + GC.CollectionCount(0) + "][1=" + GC.CollectionCount(1) + "][2=" + GC.CollectionCount(2) + "]][gc_mem_diff=" + (nMem - GC.GetTotalMemory(false)) + "]", nTimingDur);  //  , (ulong)Preferences.nFrameDuration + 10
                            nIndxGCForced = 0;
                        }
                        else
                            nTotalFrameDuration = cTimings.Stop("BTL.Before_enqueue", "[mode=" + GCSettings.LatencyMode + "][queue=" + nCurrentDeviceBufferCount + "]", (ulong)Preferences.nFrameDuration);

                        nTotalDurationSum += nTotalFrameDuration;
                        if (nTotalFrameDuration > nTotalFrameDurationMax)
                            nTotalFrameDurationMax = nTotalFrameDuration;
                        if (nTotalFrameDuration < nTotalFrameDurationMin)
                            nTotalFrameDurationMin = nTotalFrameDuration;

                        if (nCurrentDeviceBufferCount < nBufferTwoThird || nIndxQueue++ > 200)
                        {
                            nIndxQueue = 0;
                            (new Logger()).WriteNotice("[buffer.count= " + nCurrentDeviceBufferCount + "][max=" + nQueueDeviceLength + "]");
                        }
                    }
                    #endregion logging + GC

                    SendFrameToDevice(cMergingWork.cResultFrame);
                    MergingWork.Back(cMergingWork);
                    _aqWorkMerged.Dequeue();
                }
                catch (Exception ex)
                {
                    MergingWork.Back(cMergingWork);
                    _aqWorkMerged.Dequeue();
                    (new Logger()).WriteError("BTL.COPY_OUT.CATCH!!", ex);
                    cTimings.Stop("BTL.COPY_OUT.CATCH!!", "[queue:" + nCurrentDeviceBufferCount + "][cMergingWork=" + (null == cMergingWork ? "NULL" : cMergingWork.ToString()) + "]", 0);
                }
            }
        }
        private void SendFrameToDevice(Frame cFrame)
        {
            if (!(cFrame.cVideo.oFrameBytes is Bytes))
                throw new Exception("not realized or deprecated");

            Bytes aTMP = cFrame.cVideo.aFrameBytes;

            if (cFrame.cVideo == null)
                _cServerPipeStream.WriteByte(1);
            else if (aTMP.Length == 0)
                _cServerPipeStream.WriteByte(3);
            else
                _cServerPipeStream.WriteByte(2);

            if (cFrame.cVideo != null && aTMP.Length > 0)
            {
                _cServerPipeStream.Write(aTMP.aBytes, 0, aTMP.Length);
                _cBinM.BytesBack(aTMP, 4);
            }

            if (cFrame.cAudio == null)
                _cServerPipeStream.WriteByte(10);
            else if (cFrame.cAudio.aFrameBytes == null)
                _cServerPipeStream.WriteByte(12);
            else
                _cServerPipeStream.WriteByte(11);

            if (cFrame.cAudio != null && cFrame.cAudio.aFrameBytes != null)
            {
                _cServerPipeStream.Write(cFrame.cAudio.aFrameBytes.aBytes, 0, cFrame.cAudio.aFrameBytes.Length);
                _cBinM.BytesBack(cFrame.cAudio.aFrameBytes, 5);
                cFrame.cAudio.aFrameBytes = null;
            }
            nCurrentDeviceBufferCount = (uint)_cBinFormatter.Deserialize(_cServerPipeStream);
        }
        private void WritingFramesWorker(object cState)
        {
            (new Logger()).WriteNotice("BTL.WritingFramesWorker: started");

            string _sWritingFramesFile = Path.Combine(Preferences.sDebugFolder, "WritingDebugFrames.txt");
            string _sWritingFramesDir = Path.Combine(Preferences.sDebugFolder, "BTL/");
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

                        if (null != (sParams = aLines.FirstOrDefault(o => o.ToLower().StartsWith("btl"))))
                        {
                            _bDoWritingFrames = true;
                            if (!System.IO.Directory.Exists(_sWritingFramesDir))
                                System.IO.Directory.CreateDirectory(_sWritingFramesDir);
                            nWriteEveryNFrame = sParams.Length > 3 ? sParams.Substring(4).ToInt() : 1;
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

        public List<Play.Effect> BaetylusEffectsInfoGet()
        {
            List<Play.Effect> aEff = new List<Play.Effect>();
            lock (_aEffects)
                foreach (IEffect cEff in _aEffects)
                    if (cEff is Play.Effect)
                        aEff.Add((Play.Effect)cEff);
            return aEff;
        }

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
        MergingMethod? IContainer.stMergingMethod
        {
            get
            {
                return Preferences.stMerging;
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
