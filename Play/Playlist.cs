using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Drawing.Imaging;
using System.Drawing;
using helpers;

using System.Diagnostics;

namespace BTL.Play
{
	public class Playlist : ContainerVideoAudio
    {
		private class Item
        {
            public IEffect iEffect;
            public bool bIsActive;
            public Transition.TypeVideo cTransitionVideo;
            public Transition.TypeAudio cTransitionAudio;
            public ushort nTransitionDuration;
            public Item(IEffect iEffect, Transition.TypeVideo cTransitionVideo, Transition.TypeAudio cTransitionAudio, ushort nTransitionDuration)
            {
                this.iEffect = iEffect;
				this.bIsActive = true;
				this.cTransitionVideo = cTransitionVideo;
				this.cTransitionAudio = cTransitionAudio;
				this.nTransitionDuration = nTransitionDuration;
            }
            public Item(IEffect iEffect)
                : this(iEffect, Transition.TypeVideo.dissolve, Transition.TypeAudio.crossfade, ushort.MaxValue)  //_EdgesTransitions если ushort.MaxValue, то это значит переход по-умолчанию
            { }
            public Item(IEffect iEffect, ushort TransDuration)
                : this(iEffect, Transition.TypeVideo.dissolve, Transition.TypeAudio.crossfade, TransDuration)
            { }
        }

        public override ushort nEffectsQty
        {
            get
            {
                return (ushort)_aItemsQueue.Count;
            }
        }
		public override ulong nSumDuration
		{
			get
			{
				ulong nRetVal = 0;
				long nStart = DateTime.Now.Ticks;
				lock (_aItemsQueue)
				{
					(new Logger()).WriteDebug4("locking _aItemsQueue in nSumDuration [apl=" + _aItemsQueue.Count + "]");
					lock (_aItemsOnAir)
					{
						if (_aItemsOnAir.Count > 1)
						{
							if (_aItemsOnAir[0].iEffect is Transition)
								nRetVal += _aItemsOnAir[1].iEffect.nDuration + _aItemsOnAir[1].iEffect.nFrameStart - _aItemsOnAir[1].iEffect.nFrameCurrent;
							else
								nRetVal += _aItemsOnAir[0].iEffect.nDuration + _aItemsOnAir[0].iEffect.nFrameStart - _aItemsOnAir[0].iEffect.nFrameCurrent;
						}
						foreach (Item cItem in _aItemsQueue)
							nRetVal += cItem.iEffect.nDuration;
					}
				}
				(new Logger()).WriteDebug4("locking END _aItemsQueue in nSumDuration: " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + " ms");
				return nRetVal;
			}
		}
        private List<Item> _aItemsQueue;
        private List<Item> _aItemsOnAir;
        private Item _cTransition;
        private Queue<Dictionary<IEffect, ContainerAction>> _aqContainerActions;
        private List<int> _aItemsEffectHashCodesToDelete;
		private ThreadBufferQueue<IEffect> _aqPrepareQueue;
		private byte[] _aAudioSilence;
        private bool _bNeedEffectsReorder;
        private ushort _nDefaultTransitionDuration;
        private ushort _nNextTransDuration;
        public bool _bStopping = false;
        public ushort _nEndTransDuration;
        public delegate void PlaylistIsEmpty(Playlist cSender);
        public event PlaylistIsEmpty OnPlaylistIsEmpty;
		public object cPrepareLock = "";
        private bool _bStopOnEmpty;
        public bool bStopOnEmpty
        { 
            get { return _bStopOnEmpty; }
            set { _bStopOnEmpty = value; }
        }
        public Dock cDock
        {
            set
            {
				(new Logger()).WriteDebug3("playlist:dock:set:in");
				if (stArea == Area.stEmpty)
					stArea = stArea.Dock(Baetylus.Helper.cBaetylus.cBoard.stArea, value);
				(new Logger()).WriteDebug4("playlist:dock:set:out");
			}
		}
		public Playlist()
			: base(EffectType.Playlist)
        {
			try
			{
				_nDefaultTransitionDuration = 0;
                _nEndTransDuration = 0;
				_aqContainerActions = new Queue<Dictionary<IEffect, ContainerAction>>();
				_aqPrepareQueue = new ThreadBufferQueue<IEffect>(false, true);
                _aItemsEffectHashCodesToDelete = new List<int>();
				_aItemsQueue = new List<Item>();
				_aItemsOnAir = new List<Item>();
				_cTransition = new Item(new Transition());
				_cTransition.bIsActive = false;
				_nNextTransDuration = ushort.MaxValue;
				_aAudioSilence = new byte[2 * Preferences.nAudioBytesPerFramePerChannel];
				System.Threading.ThreadPool.QueueUserWorkItem(PrepareAsync);
				//_nPrepareFrameQty = (ushort)(_nTransitionFrameQty * 4);

				aChannelsAudio = new byte[0];  // UNDONE
			}
			catch
			{
				Fail();
				throw;
			}
		}
        ~Playlist()
        {
        }

		#region AnimationAdd function
		public Animation AnimationAdd(string sFileName, ushort nLoopsQty, bool bKeepAlive, ushort nTransDur)
        {
			Animation cRetVal = new Animation(sFileName, nLoopsQty, bKeepAlive);
			AnimationAdd(cRetVal, nTransDur);
			return cRetVal;
        }
		public void AnimationAdd(Animation cAnimation, ushort nTransDur)
		{
			EffectAdd(new Item(cAnimation, nTransDur));
		}
		#endregion
        #region VideoAdd function
        public Video VideoAdd(string sFileName)
        {
            return VideoAdd(sFileName, ushort.MaxValue);
        }
        public Video VideoAdd(string sFileName, ushort nTransDur)
        {
            Video cVideo = null;
            cVideo = new Video(sFileName);
            Item cItem = new Item(cVideo, nTransDur);
            EffectAdd(cItem);
            return cVideo;
        }
        public void VideoAdd(Video cVideo)
        {
            VideoAdd(cVideo, ushort.MaxValue);
        }
        public void VideoAdd(Video cVideo, ushort nTransDur)
        {
            EffectAdd(new Item(cVideo, nTransDur));
        }
        #endregion
        #region EffectAdd function
        public void EffectAdd(Effect cEffect)
        {
            EffectAdd(cEffect, ushort.MaxValue);
        }
        public void EffectAdd(Effect cEffect, ushort nTransDur)
        {
            Item cItem = new Item(cEffect, nTransDur);
            EffectAdd(cItem);
        }
        private void EffectAdd(Item cItem)
        {
            if (null == cItem || null==cItem.iEffect)
                throw new NullReferenceException("added Playlist item is null"); //TODO LANG
			if (!(cItem.iEffect is IVideo) && !(cItem.iEffect is IAudio))
                throw new Exception("non video/audio effect added"); //TODO LANG

            ///////////////ОТЛАДКА..............
            //cItem._cEff.DurationSet(100);
			Effect cEffect = (Effect)cItem.iEffect;
			cEffect.Prepared += OnEffectPrepared;
			cEffect.Started += OnEffectStarted;
			cEffect.Stopped += OnEffectStopped;
			cEffect.Failed += OnEffectFailed;

			bool bMustPrepare = false;
			lock (_aItemsQueue)
			{
				(new Logger()).WriteDebug2("locking _aItemsQueue in EffectAdd [apl=" + _aItemsQueue.Count + "]");
				if ((EffectStatus.Preparing == ((IEffect)this).eStatus || EffectStatus.Running == ((IEffect)this).eStatus) && 2 > _aItemsQueue.Count)
				{
					lock (_aqPrepareQueue.oSyncRoot)
					{
						(new Logger()).WriteDebug2("adding to async prepare on adding [hc:" + cItem.iEffect.GetHashCode() + "]");
						_aqPrepareQueue.Enqueue(cItem.iEffect);
					}
				}
				_aItemsQueue.Add(cItem);
			}
			OnEffectAdded(cEffect);
        }
		#endregion

        override public void Prepare()
        {
			(new Logger()).WriteDebug2("in [ph:" + GetHashCode() + "]");
            if (EffectStatus.Idle == ((IEffect)this).eStatus || EffectStatus.Stopped == ((IEffect)this).eStatus)
            {
				if (0 < _aItemsQueue.Count)
				{
					if (EffectStatus.Idle == _aItemsQueue[0].iEffect.eStatus)
					{
						_aItemsQueue[0].iEffect.Prepare(); //_aEffectsToPrepare.Add(_aItemsQueue[0].iEffect);
						(new Logger()).WriteDebug2("effect prepared in Prepare [ph:" + GetHashCode() + "][eh:" + _aItemsQueue[0].iEffect.GetHashCode() + "]");
					}
					Area stAreaFirst = ((IVideo)_aItemsQueue[0].iEffect).stArea;
					if (0 == this.stArea.nWidth || 0 == this.stArea.nHeight)
					{
						if (0 < stAreaFirst.nWidth && 0 < stAreaFirst.nHeight)
							this.stArea = new Area(this.stArea.nLeft, this.stArea.nTop, stAreaFirst.nWidth, stAreaFirst.nHeight);
						else
							this.stArea = Baetylus.Helper.cBaetylus.cBoard.stArea;
					}
				}
                base.Prepare();
            }
			(new Logger()).WriteDebug3("return");
        }
        override public void Start()
        {
            if (EffectStatus.Idle == ((IEffect)this).eStatus || EffectStatus.Stopped == ((IEffect)this).eStatus)
                this.Prepare();
			lock (_aItemsQueue)
			{
				(new Logger()).WriteDebug2("locking _aItemsQueue in Start [ph:" + GetHashCode() + "]");
				if (EffectStatus.Preparing == ((IEffect)this).eStatus)
				{
					base.Start(null);
					if (0 < _aItemsQueue.Count)
					{
						Item cItem = _aItemsQueue[0];
						_aItemsOnAir.Clear();
						_aItemsOnAir.Add(_cTransition);
						if (0 < cItem.nTransitionDuration)
						{
							_cTransition.iEffect.nDuration = cItem.nTransitionDuration;
							_cTransition.iEffect.iContainer = this;
							Transition cTransition = (Transition)_cTransition.iEffect;
							cTransition.EffectSourceSet(null);
							cTransition.EffectTargetSet(cItem.iEffect);
							cTransition.eTransitionTypeVideo = cItem.cTransitionVideo;
							cTransition.eTransitionTypeAudio = cItem.cTransitionAudio;
							//lock _aItemsQueue  был
							cTransition.Prepare();
							cTransition.Start(this);

						}
						else
						{
							cItem.iEffect.Start(null);
							(new Logger()).WriteDebug2("effect started in Start [ph:" + GetHashCode() + "][eh:" + cItem.iEffect.GetHashCode() + "]");
							cItem.iEffect.iContainer = this;
							_aItemsQueue.RemoveAt(0);
							_aItemsOnAir.Add(cItem);
							if (0 < _aItemsQueue.Count && EffectStatus.Idle == _aItemsQueue[0].iEffect.eStatus)
							{
								_aItemsQueue[0].iEffect.Prepare();
								(new Logger()).WriteDebug2("effect prepared in Start [ph:" + GetHashCode() + "][eh:" + _aItemsQueue[0].iEffect.GetHashCode() + "]");
							}
						}
						Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
						aCAs.Add((IEffect)this, ContainerAction.Add);
						if (null == iContainer)
							iContainer = BTL.Baetylus.Helper.cBaetylus;  //контейнер по-умолчанию
						iContainer.EffectsProcess(aCAs);
						(new Logger()).WriteDebug3("playlist started-1 [ph:" + GetHashCode() + "] [x,y: " + stArea.nLeft + ", " + stArea.nTop + "]");
					}
					else
					{
						if (bStopOnEmpty)
						{
							(new Logger()).WriteError("there aren't any effects in playlist [hc:" + GetHashCode() + "]");
							base.Stop();
						}
						else
						{
							_aItemsOnAir.Clear();
							_aItemsOnAir.Add(_cTransition);
							Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
							aCAs.Add((IEffect)this, ContainerAction.Add);
							if (null == iContainer)
								iContainer = BTL.Baetylus.Helper.cBaetylus;  //контейнер по-умолчанию
							iContainer.EffectsProcess(aCAs);
							(new Logger()).WriteDebug3("playlist started-2 [ph:" + GetHashCode() + "] [x,y: " + stArea.nLeft + ", " + stArea.nTop + "]");
						}
					}
				}
				else
					(new Logger()).WriteError(new Exception("playlist has error with prepare [hc:" + GetHashCode() + "] [this status = " + ((IEffect)this).eStatus + "]"));
			}
		}
		public Effect GetCurrentEffect()
		{
			lock (_aItemsOnAir)
			{
				if (_aItemsOnAir.Count > 1)
					if (_aItemsOnAir[0].iEffect is Transition)
						return (Effect)_aItemsOnAir[1].iEffect;
					else
						return (Effect)_aItemsOnAir[0].iEffect;
				else if (_aItemsOnAir.Count == 1)
					return (Effect)_aItemsOnAir[0].iEffect;
				else
					return null;
			}
		}
		public bool Skip(bool bLast, ushort nNewTransDur) 
		{ 
			return Skip(bLast, nNewTransDur, null);
		}
		public bool Skip(bool bLast, ushort nNewTransDur, ushort nDelay)
		{
			return Skip(bLast, nNewTransDur, null, nDelay);
		}
		public bool Skip(bool bLast, ushort nNewTransDur, Effect cEffect)
		{
			return Skip(bLast, nNewTransDur, cEffect, 0);
		}
		public bool Skip(bool bLast, ushort nNewTransDur, Effect cEffect, ushort nDelay)
        {
			/*вот такие у нас случаи тут могут быть: 
			 * 1. нам НЕ дали эффект
			 * 1.1 у нас есть текущий эффект (в _aItemsOnAir)
			 * 1.2 у нас нет текущего эффекта (в _aItemsOnAir), но есть эффект в очереди (в _aItemsQueue), т.е. пропускаем ближайший
			 * 1.3 у нас нет эффектов вообще (ни в _aItemsOnAir, ни в _aItemsQueue)
			 * 2. нам дали эффект
			 * 2.1 и он лежит в _aItemsOnAir, а значит текущий
			 * 2.2 и он лежит в _aItemsQueue
			 * 2.3 у нас нет такого эффекта вообще (ни в _aItemsOnAir, ни в _aItemsQueue)
			*/
			(new Logger()).WriteDebug("playlist:skipping in [l:" + bLast + "][t:" + nNewTransDur + "][a.c:" + _aItemsOnAir.Count + "][q.c:" + _aItemsQueue.Count + "][pl:" + GetHashCode() + "][ef:" + (null == cEffect ? "NULL" : cEffect.GetHashCode().ToString()) + "][ef_frames:" + (null == cEffect ? "" : cEffect.nFramesTotal.ToString()) + "]");
			Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
            bool bCurrent = false;
			Item cItem;
			lock (_aItemsQueue)
			{
				lock (_aItemsOnAir)
				{
					(new Logger()).WriteDebug2("locking _aItemsQueue in Skip");
					if (null == cEffect)
					{ //случаи 1.*
						if (null == (cItem = _aItemsOnAir.FirstOrDefault(o => o.bIsActive && o.iEffect.eType != EffectType.Transition)))
						{
							if(1 > _aItemsQueue.Count)
							{ //случай 1.3
								(new Logger()).WriteDebug2("playlist:skipping out because pl is empty [false][p.h:" + GetHashCode() + "][e.h:NULL]");
								return false;
							}
							//случай 1.2
							cItem = _aItemsQueue[0];
						}
						else //случай 1.1
							bCurrent = true;
						cEffect = (Effect)cItem.iEffect;
					}
					else
					{
						if (null == (cItem = _aItemsOnAir.FirstOrDefault(o => o.iEffect == cEffect)))
						{
							if (null == (cItem = _aItemsQueue.FirstOrDefault(o => o.iEffect == cEffect)))
							{ //случай 2.3
								(new Logger()).WriteDebug2("playlist:skipping out because no such effect in pl [false][p.h:" + GetHashCode() + "][e.h:" + cEffect.GetHashCode() + "]");
								return false;
							}
							//случай 2.2
						}
						else //случай 2.1
							bCurrent = true;
					}
					if (!bLast && (1 > _aItemsQueue.Count || (2 > _aItemsQueue.Count && 2 > _aItemsOnAir.Count))) //тут я подразумеваю, что в _aItemsOnAir ВСЕГДА есть транзишн... если это не так, нужно переписывать условие// v да! он всегда есть!
					{
						(new Logger()).WriteDebug2("playlist:skipping out [false][p.h:" + GetHashCode() + "][e.h:" + cEffect.GetHashCode() + "]");
						return false;
					}
					if (!bCurrent || !SkipCurrent(nNewTransDur, nDelay))
					{ //либо случаи 1.1 и 2.1 и мы опоздали, либо случаи 1.2 и 2.2
						if (1 < _aItemsQueue.Count && cItem == _aItemsQueue[0])
							_aqPrepareQueue.Enqueue(_aItemsQueue[1].iEffect);
						if (!bCurrent)
							_aItemsQueue.Remove(cItem);
					}
					(new Logger()).WriteDebug3("playlist:skipping out [true][p.h:" + GetHashCode() + "][e.h:" + cEffect.GetHashCode() + "]");
				}
			}
			(new Logger()).WriteDebug4("playlist:skipping out [true][hc:" + cEffect + "]");
            return true;
        }
		private bool SkipCurrent(ushort nNewTransDur)
		{
			return SkipCurrent(nNewTransDur, 0);
		}
        private bool SkipCurrent(ushort nNewTransDur, ushort nDelay)
        {
			Item cItem = _aItemsOnAir.FirstOrDefault(o => o.bIsActive && o.iEffect.eType != EffectType.Transition);
			if (null == cItem)
			{
				(new Logger()).WriteDebug2("playlist:skipping:current out [false][hc:" + cItem.iEffect + "]");
				return false;	
			}
			ushort nOffset = 4;  // хер его знает теперь зачем он! Кто знает, напишите!!
			if (nDelay > nOffset)
				nOffset = nDelay;
            if (nNewTransDur == ushort.MaxValue)
                nNewTransDur = 0;
			if (cItem.iEffect.nFrameCurrent + nNewTransDur + nOffset >= cItem.iEffect.nFramesTotal || cItem.iEffect.nFrameCurrent + nNewTransDur + nOffset >= cItem.iEffect.nDuration)
                return false;
			if (0 < _aItemsQueue.Count && _aItemsQueue[0].iEffect.nDuration < (ulong)(nNewTransDur*2))
				nNewTransDur = (ushort)(_aItemsQueue[0].iEffect.nDuration / 2);
			_nNextTransDuration = nNewTransDur;
			if (1 > _aItemsQueue.Count)
				_nEndTransDuration = nNewTransDur;
			cItem.iEffect.nDuration = cItem.iEffect.nFrameCurrent + nNewTransDur + nOffset;
			if (ulong.MaxValue > cItem.iEffect.nFramesTotal)
				(new Logger()).WriteNotice("playlist:skipping:current [nFramesTotal: " + cItem.iEffect.nFramesTotal + "] [new duration: " + cItem.iEffect.nDuration + "]");
            return true;
        }
		public void PLItemsDelete(int[] aEffectIDs)
		{
			bool bNeedToPrepareNextEffect = false;
			IEffect cDeletedPreparing = null;
			lock (_aItemsQueue)
			{
				(new Logger()).WriteDebug2("locking _aItemsQueue in PLItemsDelete");
				_aItemsEffectHashCodesToDelete.AddRange(aEffectIDs);

				List<Item> aItemsToDelete = new List<Item>();
				Item cPLI;
				foreach (int nHash in _aItemsEffectHashCodesToDelete)
				{
					cPLI = _aItemsQueue.FirstOrDefault(o => o.iEffect.GetHashCode() == nHash);
					if (null != cPLI)
						aItemsToDelete.Add(cPLI);
				}
				foreach (Item cPLIt in aItemsToDelete)
				{
					lock (_aqPrepareQueue.oSyncRoot)
					{
						if (0 < _aqPrepareQueue.nCount && _aItemsQueue[0].iEffect == _aqPrepareQueue.Peek())
							cDeletedPreparing = _aItemsQueue[0].iEffect;
						if (_aItemsQueue[0] == cPLIt && (_aItemsQueue[0].iEffect.eStatus == EffectStatus.Preparing || null != cDeletedPreparing)) // он может быть айдл, но на препаре заслан
							bNeedToPrepareNextEffect = true;
					}
					_aItemsQueue.Remove(cPLIt);
				}
				_aItemsEffectHashCodesToDelete.Clear();
				if (_aItemsQueue.Count == 0 && eStatus == EffectStatus.Preparing)
					Stop();
				else if (bNeedToPrepareNextEffect && _aItemsQueue.Count > 0)
				{
					lock (_aqPrepareQueue.oSyncRoot)
					{
						if (_aqPrepareQueue.Peek() == cDeletedPreparing)  // параллельно в препаре могли уже его удалить
							_aqPrepareQueue.Dequeue();
						_aqPrepareQueue.Enqueue(_aItemsQueue[0].iEffect);
					}
				}
			}
		}
		private void PrepareAsync(object cState) // срид сложный из-за ожидания пока видео уже после препаре втянется в память (bFileInMemory)
		{
			IEffect iEffect = null;
			Stopwatch cStopwatch = null;
			while (true)
			{
				try
				{
					if (0 < _aqPrepareQueue.nCount)
					{
						iEffect = _aqPrepareQueue.Peek();

						if (EffectStatus.Idle == iEffect.eStatus)
						{
							(new Logger()).WriteNotice("playlist:prepare:async: prepare started: [ph:" + GetHashCode() + "][type: " + iEffect.eType + "][duration=" + iEffect.nDuration + "] [frames= " + iEffect.nFramesTotal + "] [start=" + iEffect.nFrameStart + "] [hash=" + iEffect.GetHashCode() + "][queue_to_prepare=" + _aqPrepareQueue.nCount + "][apl=" + _aItemsQueue.Count + "]"); //logging

							cStopwatch = Stopwatch.StartNew();
							iEffect.Prepare();
							cStopwatch.Stop();
							if (20 < cStopwatch.Elapsed.TotalMilliseconds)
								(new Logger()).WriteNotice("playlist:prepare:async: [" + cStopwatch.Elapsed.TotalMilliseconds + "]"); //logging
							(new Logger()).WriteDebug2("playlist:prepare:async:end: " + "[type: " + iEffect.eType + "]" + (iEffect is Video ? "[queue length: " + ((Video)iEffect).nFileQueueLength + "]" : "")); //logging
						}
						_aqPrepareQueue.Dequeue();
					}
					if (eStatus == EffectStatus.Stopped)
						break;
					System.Threading.Thread.Sleep(10);
				}
				catch (Exception ex)
				{
					if (0 < _aqPrepareQueue.nCount && iEffect == _aqPrepareQueue.Peek())
						_aqPrepareQueue.Dequeue();
					(new Logger()).WriteError(ex);
					PLItemsDelete(new int[1] { iEffect.GetHashCode() });
					//cEffError.eStatus = EffectStatus.Error;  хорошо бы чо-то такое делать наверно....
					(new Logger()).WriteWarning("playlist:prepare:async: Effect was removed from playlist due to error with preparing!!!! [ph:" + GetHashCode() + "][eh:" + iEffect.GetHashCode() + "]"); //logging
				}
			}
		}

        override public PixelsMap FrameNextVideo()
        {
			(new Logger()).WriteDebug4("in");   //поиск бага
			long nStart0, nStart = nStart0 = DateTime.Now.Ticks;
			string sLog = "";
            base.FrameNextVideo();
            int nIndx;
			sLog += "________[base.FrameNext() = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;

            #region . выполнение заданий из _aqContainerActions .
            Dictionary<IEffect, ContainerAction> ahMoveInfo = null;
            Item cItem = null;
            lock (_aqContainerActions)
            {
                if (0 < _aqContainerActions.Count)
                {
					(new Logger()).WriteDebug4("playlist has container actions for process [hc:" + GetHashCode() + "]");
					while (0 < _aqContainerActions.Count)
                    {
                        ahMoveInfo = _aqContainerActions.Dequeue();
                        if (null == ahMoveInfo)
                            throw new NullReferenceException("move info can't be null");
                        foreach (IEffect cEffect in ahMoveInfo.Keys)
                        {
                            if (null == cEffect)
                                throw new NullReferenceException("effect can't be null");
							lock (_aItemsQueue)
							{
								(new Logger()).WriteDebug4("locking _aItemsQueue in FrameNextVideo in container_actions");
								lock (_aItemsOnAir)
								{
									switch (ahMoveInfo[cEffect])
									{
										case ContainerAction.Add:
											if (null != (cItem = _aItemsOnAir.Find(o => o.iEffect == cEffect)) && cItem != _cTransition)
												throw new Exception("this container already has specified effect");//??????????????????????? так ли это плохо??
											if (_cTransition.iEffect == cEffect)
												_cTransition.bIsActive = true;
											else if (0 < _aItemsQueue.Count && _aItemsQueue[0].iEffect == cEffect)
											{
												if (EffectStatus.Stopped == _aItemsQueue[0].iEffect.eStatus)
													_aItemsQueue[0].iEffect.Idle();
												bool bItemPreparing;
												lock (_aqPrepareQueue.oSyncRoot)
												{

													bItemPreparing = _aqPrepareQueue.Contains(_aItemsQueue[0].iEffect);
													if (!bItemPreparing && EffectStatus.Idle == _aItemsQueue[0].iEffect.eStatus)
													{
														_aqPrepareQueue.Enqueue(_aItemsQueue[0].iEffect);
														bItemPreparing = true;
													}
												}
												if (bItemPreparing)
												{
													(new Logger()).WriteDebug2("waiting for preparing in lock _aItemsQueue (!) in FrameNextVideo in container_actions. [hc=" + _aItemsQueue[0].iEffect.GetHashCode() + "]");
													while (_aqPrepareQueue.Contains(_aItemsQueue[0].iEffect))
														System.Threading.Thread.Sleep(1); //ждем когда он отпрепарится.... вариантов у нас нет
												}
												if (EffectStatus.Preparing == _aItemsQueue[0].iEffect.eStatus)
												{
													_aItemsQueue[0].iEffect.Start(null);
													(new Logger()).WriteDebug2("playlist item started in container_actions. [fhc:" + _aItemsQueue[0].iEffect.GetHashCode() + "][phc:" + GetHashCode() + "]" + (_aItemsQueue[0].iEffect is Video ? "[file_queue:" + ((Video)_aItemsQueue[0].iEffect).nFileQueueLength + "]" : ""));
													_aItemsQueue[0].iEffect.iContainer = this;
												}

												(new Logger()).WriteDebug3("playlist takes item [hc:" + GetHashCode() + "] with status:" + _aItemsQueue[0].iEffect.eStatus.ToString());

												_aItemsOnAir.Add(_aItemsQueue[0]);
												_aItemsQueue.RemoveAt(0);
												lock (_aqPrepareQueue.oSyncRoot)
												{
													if (0 < _aItemsQueue.Count && !_aqPrepareQueue.Contains(_aItemsQueue[0].iEffect) && EffectStatus.Idle == _aItemsQueue[0].iEffect.eStatus)    // что б заранее отпрепарить next elem
													{
														_aqPrepareQueue.Enqueue(_aItemsQueue[0].iEffect);
														(new Logger()).WriteDebug2("playlist item added for prepare [hc:" + _aItemsQueue[0].iEffect.GetHashCode() + "] [total_items=" + _aqPrepareQueue.nCount + "]");
													}
												}
											}
											else
												throw new Exception("trying to add non-playlist item");
											break;
										case ContainerAction.Remove:
											//_aItemsOnAir.Remove(cEffect);
											if (null != (cItem = _aItemsOnAir.Find(eff => eff.iEffect == cEffect)))
											{
												if (cItem != _cTransition)
												{
													//if (EffectStatus.Running == cItem.iEffect.eStatus)
													//    cItem.iEffect.Stop();

													//if (EffectStatus.Stopped == cIt.iEffect.eStatus)
													//    cIt.iEffect.Idle();
													_aItemsOnAir.Remove(cItem);
												}
												else
													cItem.bIsActive = false;
											}
											break;
										default:
											throw new NotImplementedException("unknown container action");
									}
								}
							}
                        }
                    }
                    _bNeedEffectsReorder = true;
                }
				else
					(new Logger()).WriteDebug4("playlist hasn't container actions for process [hc:" + GetHashCode() + "]");
				if (_bNeedEffectsReorder)
                {
                    cItem = null;
					for (ushort nOutterIndx = 0; _aItemsOnAir.Count > nOutterIndx; nOutterIndx++)
					{
						for (ushort nInnerIndx = (ushort)(nOutterIndx + 1); _aItemsOnAir.Count > nInnerIndx; nInnerIndx++)
						{
							ushort nZOuter = _aItemsOnAir[nOutterIndx].iEffect.nLayer;
							ushort nZInner = _aItemsOnAir[nInnerIndx].iEffect.nLayer;
							if (nZOuter > nZInner)
							{
								cItem = _aItemsOnAir[nOutterIndx];
								_aItemsOnAir[nOutterIndx] = _aItemsOnAir[nInnerIndx];
								_aItemsOnAir[nInnerIndx] = cItem;
							}
						}
					}
					_bNeedEffectsReorder = false;
                }
            }
            #endregion
			sLog += " [ContainerActions = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;

            #region . выполнение заданий по удалению PLItems .
			if (0 < _aItemsEffectHashCodesToDelete.Count)
			{
				lock (_aItemsQueue)
				{
					(new Logger()).WriteDebug4("locking _aItemsQueue in FrameNextVideo in items_deleting");
					List<Item> aItemsToDelete = new List<Item>();
					Item cPLI;
					foreach (int nHash in _aItemsEffectHashCodesToDelete)
					{
						cPLI = _aItemsQueue.FirstOrDefault(o => o.iEffect.GetHashCode() == nHash);
						if (null != cPLI)
							aItemsToDelete.Add(cPLI);
					}
					foreach (Item cPLIt in aItemsToDelete)
					{
						(new Logger()).WriteDebug3("effect removed from playlist [hc = " + cPLIt.GetHashCode() + "]");
						_aItemsQueue.Remove(cPLIt);
					}
					_aItemsEffectHashCodesToDelete.Clear();
				}
			}
            #endregion
			sLog += " [Deleting Items = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;
			
			IVideo cVideoEffect = null;
            PixelsMap cFrame = null;
            ulong nDur = 0;

            if (1 == _aItemsOnAir.Count && 0 == _aItemsQueue.Count && !_aItemsOnAir[0].bIsActive)
            {
				(new Logger()).WriteDebug3("playlist is empty [hc:" + GetHashCode() + "]");
				if (null != OnPlaylistIsEmpty)
					OnPlaylistIsEmpty(this);
                if (_bStopOnEmpty && EffectStatus.Stopped != this.eStatus)
                    Stop();
            }
			sLog += " [if_1 = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;

            if (1 == _aItemsOnAir.Count && 0 < _aItemsQueue.Count && !_aItemsOnAir[0].bIsActive)
            {
				(new Logger()).WriteDebug3("playlist adds next effect [hc:" + GetHashCode() + "]");
				Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
                    aCAs.Add(_aItemsQueue[0].iEffect, ContainerAction.Add);
                    ((IContainer)this).EffectsProcess(aCAs);
            }
			sLog += " [if_2 = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			long nStartFor = DateTime.Now.Ticks;

            for (nIndx = 0; nIndx < _aItemsOnAir.Count; nIndx++)
            {
				sLog += " --FOR: isTransition:" + (_aItemsOnAir[nIndx] == _cTransition).ToString() + "--";
				nStart = DateTime.Now.Ticks;

                if (EffectStatus.Running != _aItemsOnAir[nIndx].iEffect.eStatus && _aItemsOnAir[nIndx] != _cTransition)
                {
					(new Logger()).WriteDebug3("playlist removes effect [hc:" + GetHashCode() + "][hce:" + (_aItemsOnAir[nIndx].iEffect).GetHashCode() + "]");
					Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
                    aCAs.Add(_aItemsOnAir[nIndx].iEffect, ContainerAction.Remove);
                    ((IContainer)this).EffectsProcess(aCAs);
                    continue;
                }
				sLog += " [if_3 = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
				nStart = DateTime.Now.Ticks;

                if (_aItemsOnAir[nIndx].bIsActive)
                {
                    if (_aItemsOnAir[nIndx] != _cTransition)
                    {
                        nDur = _aItemsOnAir[nIndx].iEffect.nFramesTotal;
						nDur = _aItemsOnAir[nIndx].iEffect.nDuration < nDur ? _aItemsOnAir[nIndx].iEffect.nDuration : nDur;
                        if (ulong.MaxValue > nDur)
                        {
							sLog += " [if_5 before lock = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
							nStart = DateTime.Now.Ticks;

							ulong nFrameCurrent = _aItemsOnAir[nIndx].iEffect.nFrameCurrent;
							lock (_aItemsQueue)
							#region Расчет nNextTransDuration и если пора, то запуск _cTransitionEffect
							{
								if (new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds > 40)
									(new Logger()).WriteDebug2("locking _aItemsQueue in FrameNextVideo in FOR after more then 40ms waiting=" + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds);
								(new Logger()).WriteDebug4("locking _aItemsQueue in FrameNextVideo in FOR");
								long nStart2 = DateTime.Now.Ticks;
								sLog += " [if_5 lock begins = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
								nStart = DateTime.Now.Ticks;

								if (_bStopping)
									_aItemsQueue.Clear();
								if (0 < _aItemsQueue.Count)
								{
									if (ushort.MaxValue == _nNextTransDuration)
									{
										_nNextTransDuration = _aItemsQueue[0].nTransitionDuration == ushort.MaxValue ? _nDefaultTransitionDuration : _aItemsQueue[0].nTransitionDuration;
										if (nDur - nFrameCurrent < (ulong)(_nNextTransDuration + 2))
											_nNextTransDuration = 0;
										if (1 == _nNextTransDuration)
											_nNextTransDuration = 0;
									}
									if (nFrameCurrent == nDur - _nNextTransDuration - 1)
									{                               // т.е. если следующий элемент есть и его пора давать
										if (0 == _nNextTransDuration)
										{
											Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
											aCAs.Add(_aItemsOnAir[nIndx].iEffect, ContainerAction.Remove);
											aCAs.Add(_aItemsQueue[0].iEffect, ContainerAction.Add);
											((IContainer)this).EffectsProcess(aCAs);
										}
										else
										{
											if (EffectStatus.Running == ((IEffect)_cTransition.iEffect).eStatus)
												_cTransition.iEffect.Stop();
											if (EffectStatus.Stopped == ((IEffect)_cTransition.iEffect).eStatus)
												_cTransition.iEffect.Idle();
											_cTransition.iEffect.nDuration = _nNextTransDuration;
											_cTransition.iEffect.iContainer = this;
											Transition cTransition = (Transition)_cTransition.iEffect;
											cTransition.EffectSourceSet(_aItemsOnAir[nIndx].iEffect);
											cTransition.EffectTargetSet(_aItemsQueue[0].iEffect);
											cTransition.eTransitionTypeVideo = _aItemsQueue[0].cTransitionVideo;
											cTransition.eTransitionTypeAudio = _aItemsQueue[0].cTransitionAudio;
											(new Logger()).WriteDebug3("transition prepare and start [hc_of_target:" + _aItemsQueue[0].iEffect.GetHashCode() + "]");
											cTransition.Prepare();
											cTransition.Start(this);
										}
										_nNextTransDuration = ushort.MaxValue;
									}
								}
								else
								{
									if (nFrameCurrent == nDur - _nEndTransDuration - 1 && 1 < _nEndTransDuration
										|| nFrameCurrent < nDur - _nEndTransDuration - 1 && _bStopping)
									{
										if (EffectStatus.Running == ((IEffect)_cTransition.iEffect).eStatus)
											_cTransition.iEffect.Stop();
										if (EffectStatus.Stopped == ((IEffect)_cTransition.iEffect).eStatus)
											_cTransition.iEffect.Idle();
										_cTransition.iEffect.nDuration = _nEndTransDuration;
										_cTransition.iEffect.iContainer = this;
										Transition cTransition = (Transition)_cTransition.iEffect;
										cTransition.EffectSourceSet(_aItemsOnAir[nIndx].iEffect);
										cTransition.EffectTargetSet(null);
										cTransition.eTransitionTypeVideo = _aItemsOnAir[nIndx].cTransitionVideo;
										cTransition.eTransitionTypeAudio = _aItemsOnAir[nIndx].cTransitionAudio;
										cTransition.Prepare();
										cTransition.Start(this);
									}
								}
								sLog += " [if_5_inside_lock = " + new TimeSpan(DateTime.Now.Ticks - nStart2).TotalMilliseconds + "ms]";
							}
							sLog += " [if_5_outside_lock = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
                            #endregion
                        }
                    }
					nStart = DateTime.Now.Ticks;
                    cVideoEffect = (IVideo)_aItemsOnAir[nIndx].iEffect;
					(new Logger()).WriteDebug4("playlist calls for next frame from effect [hc:" + GetHashCode() + "][hce:" + cVideoEffect.GetHashCode() + "]");
					long nCBStart = DateTime.Now.Ticks;
					cFrame = cVideoEffect.FrameNext();
					sLog += " [video_frame_next = " + new TimeSpan(DateTime.Now.Ticks - nCBStart).TotalMilliseconds + "ms]" + (_aItemsOnAir[nIndx].iEffect is Video ? "[video_queue_length = " + ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]" : "");
					(new Logger()).WriteDebug4("playlist after videoframenext");
					if (_aItemsOnAir[nIndx].iEffect is Video && ((Video)_aItemsOnAir[nIndx].iEffect).bFramesStarvation)
						(new Logger()).WriteDebug2("there is a frame starvation! [queue="+ ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]");
//					Baetylus.Helper.cBaetylus.sPLLogs += "PL[" + this.GetHashCode() + "]:cVideoEffect.FrameNext(): [ " + new TimeSpan(DateTime.Now.Ticks - nCBStart).TotalMilliseconds.ToString() + "ms] ";
					sLog += " [if_1_outside_lock = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]"; // раньше стояла за фр.некстом
                }
            }
			sLog += " [FOR = " + new TimeSpan(DateTime.Now.Ticks - nStartFor).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;
			if (null != cFrame)
				cFrame.Move((short)(cFrame.stArea.nLeft + stArea.nLeft), (short)(cFrame.stArea.nTop + stArea.nTop));
			sLog += " [move = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= Preferences.nFrameDuration)    // logging
				(new Logger()).WriteNotice("playlist.cs: FrameNext(): [pl_id=" + GetHashCode() + "] execution time > " + Preferences.nFrameDuration + "ms: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]" + sLog);
			(new Logger()).WriteDebug4("return");
			if (this.nFrameCurrent >= this.nDuration) // что б при скипе лупа могли плагины назначать и дату смерти на всякий случай, а то хвосты иногда висят...
				this.Stop();
            return cFrame;
        }
        override public byte[] FrameNextAudio()
        {
            base.FrameNextAudio();
            byte[] aAudioBytes = null;
            int nIndx;
            for (nIndx = 0; nIndx < _aItemsOnAir.Count; nIndx++)
            {
                if (EffectStatus.Running != _aItemsOnAir[nIndx].iEffect.eStatus)
                    continue;
                if (_aItemsOnAir[nIndx].bIsActive && _aItemsOnAir[nIndx].iEffect is IAudio)
                {
                    //cAudioEffect = (IAudio)_aItemsOnAir[nIndx].iEffect;
					long nCBStart = DateTime.Now.Ticks;
					string sLog = "Audio frame next: [pl_hc = " + GetHashCode() + "][ef_hc = " + _aItemsOnAir[nIndx].iEffect.GetHashCode() + "]" + (_aItemsOnAir[nIndx].iEffect is Video ? "[video_queue_length before = " + ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]" : "");
					aAudioBytes = ((IAudio)_aItemsOnAir[nIndx].iEffect).FrameNext();
					double nTM = new TimeSpan(DateTime.Now.Ticks - nCBStart).TotalMilliseconds;
					if (nTM >= Preferences.nFrameDuration)
						(new Logger()).WriteNotice(sLog + "[exec time " + nTM + " ms]" + (_aItemsOnAir[nIndx].iEffect is Video ? "[video_queue_length after = " + ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]" : ""));

					(new Logger()).WriteDebug4("playlist after audioframenext");

//					Baetylus.Helper.cBaetylus.sPLLogs += "PL[" + this.GetHashCode() + "]:cAudioEffect.SampleNext(): [ " + nTM.ToString() + "ms] ";
                }
            }
			if (null == aAudioBytes)
				aAudioBytes = _aAudioSilence;
            return aAudioBytes; 
        }
		override internal void EffectsProcess(Dictionary<IEffect, ContainerAction> ahMoveInfos)
        {//заносит в _aqContainerActions все операции для Worker ....
            lock (_aqContainerActions)
            {
                _aqContainerActions.Enqueue(ahMoveInfos);
            }
        }
		override internal void EffectsReorder()
        {
            lock (_aqContainerActions)
                _bNeedEffectsReorder = true;
        }

        public override void Stop()
        {
			(new Logger()).WriteDebug3("playlist stopped [hc:" + GetHashCode() + "] [x,y: " + stArea.nLeft + ", " + stArea.nTop + "]");
            if (2 == _aItemsOnAir.Count && !_aItemsOnAir[0].bIsActive && 1 < _nEndTransDuration)
                _bStopping = true;
            else
                base.Stop();
        }
        public void EndTransDurationSet(ushort nEndTransDuration)
        {
            _nEndTransDuration = nEndTransDuration;
        }
    }
}
