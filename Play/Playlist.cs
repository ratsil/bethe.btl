using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Drawing.Imaging;
using System.Drawing;
using helpers;
using System.Diagnostics;
using System.Xml;
using helpers.extensions;

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

        public override ushort nEffectsQty   // in catch block  
        {
            get
            {
                return (ushort)_aItemsQueue.Count;
            }
        }
		public override ulong nSumDuration   // in catch block  
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
        public override MergingMethod stMergingMethod    // to search try-catch blocks in hierarchy use Roslyn  
        {
            get
            {
                return base.stMergingMethod;
            }
            set
            {
                if (base.stMergingMethod == value)
                    return;
                if (eStatus == EffectStatus.Idle)
                {
                    lock (_aItemsQueue)
                    {
                        base.stMergingMethod = value;
                        foreach (Item cI in _aItemsQueue)
                        {
                            if (cI.iEffect.eStatus == EffectStatus.Idle)
                            {
                                if (cI.iEffect is EffectVideo)
                                    ((EffectVideo)cI.iEffect).stMergingMethod = value;
                            }
                            else
                                (new Logger()).WriteError("playlist: impossible to change bCUDA in not idle effect [pl_hc=" + nID + "][ef_hc=" + cI.iEffect.nID + "][status=" + eStatus + "][merging=" + base.stMergingMethod + "]");
                        }
                    }
                }
                else
                    (new Logger()).WriteError("playlist: impossible to change bCUDA in not idle playlist [pl_hc=" + nID + "][status=" + eStatus + "][merging=" + base.stMergingMethod + "]");
            }
        }
        public delegate void PlaylistIsEmpty(Playlist cSender);
        public event PlaylistIsEmpty OnPlaylistIsEmpty;
        public bool bStopOnEmpty
        {
            get { return _bStopOnEmpty; }
            set { _bStopOnEmpty = value; }
        }

        private List<Item> _aItemsQueue;
        private List<Item> _aItemsOnAir;
        private Item _cTransition;
        private Queue<Dictionary<IEffect, ContainerAction>> _aqContainerActions;
        private List<int> _aItemsEffectHashCodesToDelete;
		private ThreadBufferQueue<IEffect> _aqPrepareQueue;
        private bool _bNeedEffectsReorder;
        private ushort _nDefaultTransitionDuration;
        private ushort _nNextTransDuration;
        private bool _bStopping = false;
        private object _oLockPrepare;
        private bool _bPrepared;
        private object _oLockStart;
        private bool _bStarted;
        private object _oLockStop;
		private bool _bStopped;
		private bool _bStopOnEmpty;
		private Item _cItemOnAir
		{
			get
			{
				if (_aItemsOnAir[0] != _cTransition)
					return _aItemsOnAir[0];
				else if (_aItemsOnAir.Count > 1)
					return _aItemsOnAir[1];
				return null;
			}
		}

		public Playlist()
			: base(EffectType.Playlist)
		{
			try
			{
				_nDefaultTransitionDuration = 0;
				_aqContainerActions = new Queue<Dictionary<IEffect, ContainerAction>>();
				_aqPrepareQueue = new ThreadBufferQueue<IEffect>(false, true);
                _aItemsEffectHashCodesToDelete = new List<int>();
				_aItemsQueue = new List<Item>();
				_aItemsOnAir = new List<Item>();
				_cTransition = new Item(new Transition());
				_cTransition.bIsActive = false;
				_nNextTransDuration = ushort.MaxValue;
				System.Threading.ThreadPool.QueueUserWorkItem(PrepareAsync);
				//_nPrepareFrameQty = (ushort)(_nTransitionFrameQty * 4);
				aChannelsAudio = new byte[0];  // UNDONE
				_oLockStop = new object();
				_oLockPrepare = new object();
                _oLockStart = new object();
            }
			catch
			{
				Fail();
				throw;
			}
		}
		public Playlist(XmlNode cXmlNode)
			: this()
		{
			try
			{
				base.LoadXML(cXmlNode);
				bStopOnEmpty = cXmlNode.AttributeOrDefaultGet<bool>("stop_on_empty", false);
				XmlNode cNodeChild;
				if (null != (cNodeChild = cXmlNode.NodeGet("effects", false)))
					EffectsAdd(cNodeChild);
				else
					throw new Exception("effects section is missing");
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

		#region AnimationAdd functions
		public Animation AnimationAdd(string sFileName, ushort nLoopsQty, bool bKeepAlive, ushort nTransDur)
        {
			Animation cRetVal = new Animation(sFileName, nLoopsQty, bKeepAlive);
			AnimationAdd(cRetVal, nTransDur);
			return cRetVal;
        }
		public void AnimationAdd(Animation cAnimation, ushort nTransDur)
		{
			ItemAdd(new Item(cAnimation, nTransDur));
		}
		#endregion
        #region VideoAdd functions
        public Video VideoAdd(string sFileName)
        {
            return VideoAdd(sFileName, _nDefaultTransitionDuration);
        }
        public Video VideoAdd(string sFileName, ushort nTransDur)
        {
            Video cVideo = null;
            cVideo = new Video(sFileName);
            Item cItem = new Item(cVideo, nTransDur);
            ItemAdd(cItem);
            return cVideo;
        }
        public void VideoAdd(Video cVideo)
        {
            VideoAdd(cVideo, _nDefaultTransitionDuration);
        }
        public void VideoAdd(Video cVideo, ushort nTransDur)
        {
            ItemAdd(new Item(cVideo, nTransDur));
        }
		#endregion
		#region EffectAdd functions
		private void EffectsAdd(XmlNode cXmlNode)
		{
			foreach (XmlNode cXN in cXmlNode.NodesGet())
				EffectAdd((IVideo)Effect.EffectGet(cXN), cXN);
		}
		private void EffectAdd(IVideo iVideo, XmlNode cXmlNode)
		{
			EffectAdd((Effect)iVideo, cXmlNode.AttributeOrDefaultGet<ushort>("transition", 0));
		}
		public void EffectAdd(Effect cEffect)
        {
            EffectAdd(cEffect, _nDefaultTransitionDuration);
        }
        public void EffectAdd(Effect cEffect, ushort nTransDur)
        {
            Item cItem = new Item(cEffect, nTransDur);
            ItemAdd(cItem);
        }
        private void ItemAdd(Item cItem)
        {
            if (null == cItem || null==cItem.iEffect)
                throw new NullReferenceException("added Playlist item is null"); //TODO LANG
			if (!(cItem.iEffect is IVideo) && !(cItem.iEffect is IAudio))
                throw new Exception("non video/audio effect added"); //TODO LANG

            if (cItem.iEffect is IVideo)
            {
                if (cItem.iEffect.eStatus > EffectStatus.Idle && this.stMergingMethod != ((IVideo)cItem.iEffect).stMergingMethod)
                    throw new Exception("некорректна¤ среда вычислений: [playlist_merging=" + stMergingMethod + "][effect_merging=" + ((IVideo)cItem.iEffect).stMergingMethod + "]"); //TODO LANG
                cItem.iEffect.iContainer = this;
                ((IVideo)cItem.iEffect).stMergingMethod = this.stMergingMethod;
            }





            if (0 == this.stArea.nWidth || 0 == this.stArea.nHeight)
			{
				Area stAreaFirst = ((IVideo)cItem.iEffect).stArea;
				if (0 < stAreaFirst.nWidth && 0 < stAreaFirst.nHeight)
					this.stArea = new Area(this.stArea.nLeft, this.stArea.nTop, stAreaFirst.nWidth, stAreaFirst.nHeight);
				else
					this.stArea = Baetylus.Helper.stCurrentBTLArea;
			}

			if (cItem.iEffect is IVideo)
				((IVideo)cItem.iEffect).stBase = stArea;
            ///////////////ќ“Ћјƒ ј..............
            //cItem._cEff.DurationSet(100);
			Effect cEffect = (Effect)cItem.iEffect;
			cEffect.Prepared += OnEffectPrepared;
			cEffect.Started += OnEffectStarted;
			cEffect.Stopped += OnEffectStopped;
			cEffect.Failed += OnEffectFailed;

			bool bMustPrepare = false;
			lock (_aItemsQueue)
			{
				(new Logger()).WriteDebug4("locking _aItemsQueue in EffectAdd [apl=" + _aItemsQueue.Count + "]");
				if ((EffectStatus.Preparing == eStatus || EffectStatus.Running == eStatus) && 2 > _aItemsQueue.Count)
				{
					lock (_aqPrepareQueue.oSyncRoot)
					{
                        (new Logger()).WriteDebug2("adding to async prepare on item_add [plhc:" + nID + "][hc:" + cItem.iEffect.nID + "][total_items=" + _aqPrepareQueue.nCount + "]");
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
            lock (_oLockPrepare)
            {
                if (_bPrepared)
                    return;
                _bPrepared = true;
            }

            (new Logger()).WriteDebug2("in [ph:" + nID + "]");
            lock (_aItemsQueue)
            {

                if (EffectStatus.Idle == ((IEffect)this).eStatus || EffectStatus.Stopped == ((IEffect)this).eStatus)
                {
                    for (int nI = 0; nI < (_aItemsQueue.Count > 3 ? 3 : _aItemsQueue.Count); nI++)  // после старта первые 2 будут уже подготовлены
                    {
                        if (EffectStatus.Idle == _aItemsQueue[nI].iEffect.eStatus)
                        {
                            _aItemsQueue[nI].iEffect.Prepare(); //_aEffectsToPrepare.Add(_aItemsQueue[0].iEffect);
                            (new Logger()).WriteDebug2("effect [#" + nI + "] prepared in Prepare [plhc:" + nID + "][hc:" + _aItemsQueue[nI].iEffect.nID + "]");
                        }
                    }
                    base.Prepare();
                }
            }
            (new Logger()).WriteDebug3("return");
        }
        override public void Start(IContainer iContainer)
		{
			lock (_aItemsQueue)
			{
				try
				{
					if (null != iContainer)
						base.iContainer = iContainer;
					//base.Start(iContainer);
					Start();
				}
				catch (Exception ex)
				{
					(new Logger()).WriteError(ex);
				}
			}
		}
		override public void Start()
        {
            lock (_oLockStart)
            {
                if (_bStarted)
                    return;
                _bStarted = true;
            }

            if (EffectStatus.Idle == ((IEffect)this).eStatus || EffectStatus.Stopped == ((IEffect)this).eStatus)
                this.Prepare();
			lock (_aItemsQueue)
			{
				(new Logger()).WriteDebug2("locking _aItemsQueue in Start [ph:" + nID + "]");
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
							if (nInDissolve < 2)
								nInDissolve = cItem.nTransitionDuration.ToByte();

							//_cTransition.iEffect.nDuration = cItem.nTransitionDuration;
							//_cTransition.iEffect.iContainer = this;
							//Transition cTransition = (Transition)_cTransition.iEffect;
							//cTransition.EffectSourceSet(null);
							//cTransition.EffectTargetSet(cItem.iEffect);
							//cTransition.eTransitionTypeVideo = cItem.cTransitionVideo;
							//cTransition.eTransitionTypeAudio = cItem.cTransitionAudio;
							////lock _aItemsQueue  был
							//cTransition.Prepare();
							//cTransition.Start(this);

						}

						cItem.iEffect.Start(null);
						(new Logger()).WriteDebug2("effect started in Start [plhc:" + nID + "][hc:" + cItem.iEffect.nID + "]");
						cItem.iEffect.iContainer = this;
						_aItemsQueue.RemoveAt(0);
						_aItemsOnAir.Add(cItem);
                        for (int nI = 0; nI < (_aItemsQueue.Count > 2 ? 2 : _aItemsQueue.Count); nI++)  
                        {
                            if (EffectStatus.Idle == _aItemsQueue[nI].iEffect.eStatus) // не должно срабатывать никогда, т.к. первые два, что добавл¤ютс¤ после this.prepare подготавливаютс¤ в add
                            {
                                _aqPrepareQueue.Enqueue(_aItemsQueue[nI].iEffect);
                                (new Logger()).WriteDebug2("effect [#" + nI + "] added to async prepare in Start [plhc:" + nID + "][hc:" + _aItemsQueue[nI].iEffect.nID + "]");
                            }
                        }

						Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
						aCAs.Add((IEffect)this, ContainerAction.Add);
						if (null == iContainer)
							iContainer = BTL.Baetylus.Helper.cBaetylus;  //контейнер по-умолчанию
						iContainer.EffectsProcess(aCAs);
						(new Logger()).WriteDebug3("playlist started-1 [ph:" + nID + "] [x,y: " + stArea.nLeft + ", " + stArea.nTop + "]");
					}
					else
					{
						if (bStopOnEmpty)
						{
							(new Logger()).WriteError("there aren't any effects in playlist [hc:" + nID + "]");
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
							(new Logger()).WriteDebug3("playlist started-2 [ph:" + nID + "] [x,y: " + stArea.nLeft + ", " + stArea.nTop + "]");
						}
					}
				}
				else
					(new Logger()).WriteError(new Exception("playlist has error with prepare [hc:" + nID + "] [this status = " + ((IEffect)this).eStatus + "]"));
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
		public bool Skip(bool bSkipIfLast, ushort nNewTransDur)   // in catch block  
		{ 
			return Skip(bSkipIfLast, nNewTransDur, null);
		}
		public bool Skip(bool bSkipIfLast, ushort nNewTransDur, ushort nDelay)   // in catch block  
        {
			return Skip(bSkipIfLast, nNewTransDur, null, nDelay);
		}
		public bool Skip(bool bSkipIfLast, ushort nNewTransDur, Effect cEffect)   // in catch block  
        {
			return Skip(bSkipIfLast, nNewTransDur, cEffect, 0);
		}
		public bool Skip(bool bSkipIfLast, ushort nNewTransDur, Effect cEffect, ushort nDelay)   // in catch block  
        {
			/*вот такие у нас случаи тут могут быть: 
			 * 1. нам Ќ≈ дали эффект
			 * 1.1 у нас есть текущий эффект (в _aItemsOnAir)
			 * 1.2 у нас нет текущего эффекта (в _aItemsOnAir), но есть эффект в очереди (в _aItemsQueue), т.е. пропускаем ближайший
			 * 1.3 у нас нет эффектов вообще (ни в _aItemsOnAir, ни в _aItemsQueue)
			 * 2. нам дали эффект
			 * 2.1 и он лежит в _aItemsOnAir, а значит текущий
			 * 2.2 и он лежит в _aItemsQueue
			 * 2.3 у нас нет такого эффекта вообще (ни в _aItemsOnAir, ни в _aItemsQueue)
			*/
			(new Logger()).WriteDebug("playlist:skipping in [l:" + bSkipIfLast + "][t:" + nNewTransDur + "][a.c:" + _aItemsOnAir.Count + "][q.c:" + _aItemsQueue.Count + "][pl:" + nID + "][ef:" + (null == cEffect ? "NULL" : cEffect.nID.ToString()) + "][ef_frames:" + (null == cEffect ? "" : cEffect.nFramesTotal.ToString()) + "]");
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
								(new Logger()).WriteDebug2("playlist:skipping out because pl is empty [false][p.h:" + nID + "][e.h:NULL]");
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
								(new Logger()).WriteDebug2("playlist:skipping out because no such effect in pl [false][p.h:" + nID + "][e.h:" + cEffect.nID + "]");
								return false;
							}
							//случай 2.2
						}
						else //случай 2.1
							bCurrent = true;
					}
					if (!bSkipIfLast && (1 > _aItemsQueue.Count || (2 > _aItemsQueue.Count && 2 > _aItemsOnAir.Count))) //тут ¤ подразумеваю, что в _aItemsOnAir ¬—≈√ƒј есть транзишн... если это не так, нужно переписывать условие// v да! он всегда есть!
					{
						(new Logger()).WriteDebug2("playlist:skipping out [false][p.h:" + nID + "][e.h:" + cEffect.nID + "]");
						return false;
					}
					if (!bCurrent || !SkipCurrent(nNewTransDur, nDelay))
					{ //либо случаи 1.1 и 2.1 и мы опоздали, либо случаи 1.2 и 2.2
                        if (1 < _aItemsQueue.Count && cItem == _aItemsQueue[0])
                        {
                            (new Logger()).WriteDebug2("adding to async prepare on skip [plhc:" + nID + "][hc:" + _aItemsQueue[1].iEffect.nID + "][total_items=" + _aqPrepareQueue.nCount + "]");
                            _aqPrepareQueue.Enqueue(_aItemsQueue[1].iEffect);
                        }
                        if (!bCurrent)
                        {
                            (new Logger()).WriteDebug2("removing item from queue on skip [hc:" + cItem.iEffect.nID + "]");
                            _aItemsQueue.Remove(cItem);
                        }
					}
					(new Logger()).WriteDebug3("playlist:skipping out [true][p.h:" + nID + "][e.h:" + cEffect.nID + "]");
				}
			}
			(new Logger()).WriteDebug4("playlist:skipping out [true][hc:" + cEffect + "]");
            return true;
        }
		private bool SkipCurrent(ushort nNewTransDur)   // in catch block  
        {
			return SkipCurrent(nNewTransDur, 0);
		}
        private bool SkipCurrent(ushort nNewTransDur, ushort nDelay)   // in catch block  
        {
			Item cItem = _aItemsOnAir.FirstOrDefault(o => o.bIsActive && o.iEffect.eType != EffectType.Transition);
			if (null == cItem)
			{
				(new Logger()).WriteDebug2("playlist:skipping:current out [false][hc:" + cItem.iEffect + "]");
				return false;	
			}
			ushort nOffset = 2;  // хер его знает теперь зачем он!  то знает, напишите!!     // я думаю это прапорщеский зазорчег. сделал его 2 вместо 4х ))  // 
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
				this.nDuration = this.nFrameCurrent + (nOutDissolve > 1 ? nOutDissolve : nNewTransDur) + nOffset;
			else
				cItem.iEffect.nDuration = cItem.iEffect.nFrameCurrent + nNewTransDur + nOffset;

			if (ulong.MaxValue > cItem.iEffect.nFramesTotal)
				(new Logger()).WriteNotice("playlist:skipping:current [nFramesTotal: " + cItem.iEffect.nFramesTotal + "] [new duration: " + cItem.iEffect.nDuration + "]");
            return true;
        }
		public void PLItemsDelete(int[] aEffectIDs)   // in catch block  
        {
			IEffect cDeletedPreparing = null;
			lock (_aItemsQueue)
			{
				(new Logger()).WriteDebug2("locking _aItemsQueue in PLItemsDelete");
				_aItemsEffectHashCodesToDelete.AddRange(aEffectIDs);

				List<Item> aItemsToDelete = new List<Item>();
				Item cPLI;
				foreach (int nHash in _aItemsEffectHashCodesToDelete)
				{
					cPLI = _aItemsQueue.FirstOrDefault(o => o.iEffect.nID == nHash);
					if (null != cPLI)
						aItemsToDelete.Add(cPLI);
				}
				foreach (Item cPLIt in aItemsToDelete)
				{
					lock (_aqPrepareQueue.oSyncRoot)
					{
                        if (_aqPrepareQueue.Contains(cPLIt.iEffect))
                        {
                            _aqPrepareQueue.Remove(cPLIt.iEffect);
                            (new Logger()).WriteDebug2("effect removed from async prepare in items_delete [plhc:" + nID + "][hc:" + cPLIt.iEffect.nID + "][total_items=" + _aqPrepareQueue.nCount + "]");
                        }
					}
					_aItemsQueue.Remove(cPLIt);
				}
				_aItemsEffectHashCodesToDelete.Clear();
				if (_aItemsQueue.Count == 0 && eStatus == EffectStatus.Preparing)
					Stop();
				else
				{
					lock (_aqPrepareQueue.oSyncRoot)
					{
                        for (int nI = 0; nI < (_aItemsQueue.Count > 2 ? 2 : _aItemsQueue.Count); nI++)
                        {
                            if (!_aqPrepareQueue.Contains(_aItemsQueue[nI].iEffect) && EffectStatus.Idle == _aItemsQueue[nI].iEffect.eStatus) // что б заранее отпрепарить 2 next effects
                            {
                                _aqPrepareQueue.Enqueue(_aItemsQueue[nI].iEffect);
                                (new Logger()).WriteDebug2("effect [#" + nI + "] added to async prepare in items_delete [plhc:" + nID + "][hc:" + _aItemsQueue[nI].iEffect.nID + "][total_items=" + _aqPrepareQueue.nCount + "]");
                            }
                        }
					}
				}
			}
		}
		private void PrepareAsync(object cState)   // worker       // срид сложный из-за ожидани¤ пока видео уже после препаре вт¤нетс¤ в пам¤ть (bFileInMemory)
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
							(new Logger()).WriteNotice("playlist:prepare:async: prepare started: [plhc:" + nID + "][type: " + iEffect.eType + "][duration=" + iEffect.nDuration + "] [frames= " + iEffect.nFramesTotal + "] [start=" + iEffect.nFrameStart + "] [ehc=" + iEffect.nID + "][queue_to_prepare=" + _aqPrepareQueue.nCount + "][apl=" + _aItemsQueue.Count + "]"); //logging

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
                    try
                    {
                        if (0 < _aqPrepareQueue.nCount && iEffect == _aqPrepareQueue.Peek())
                            _aqPrepareQueue.Dequeue();
                        PLItemsDelete(new int[1] { iEffect.nID });
                        //cEffError.eStatus = EffectStatus.Error;  хорошо бы чо-то такое делать наверно....
                    }
                    catch (Exception ex2)
                    {
                        (new Logger()).WriteError("playlist:prepare:async:catch: error in catch block! [ph:" + nID + "][eh:" + (null == iEffect ? "NULL" : "" + iEffect.nID) + "]", ex2); //logging
                    }
                    finally
                    {
                        (new Logger()).WriteError("playlist:prepare:async: Effect was removed from playlist due to error with preparing!!!! [ph:" + nID + "][eh:" + (null == iEffect ? "NULL" : "" + iEffect.nID) + "]", ex); //logging
                    }
                }
			}
		}

        override public PixelsMap FrameNextVideo()
        {
			base.FrameNextVideo();
			(new Logger()).WriteDebug4("in");   //поиск бага
			long nStart0, nStart = nStart0 = DateTime.Now.Ticks;
			string sLog = "";
            int nIndx;
			sLog += "________[base.FrameNext() = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;

            #region . выполнение заданий из _aqContainerActions .
            Dictionary<IEffect, ContainerAction> ahMoveInfo = null;
            Item cItem = null;

			if (null!= _cItemOnAir &&  EffectStatus.Running != _cItemOnAir.iEffect.eStatus)
			{
				(new Logger()).WriteDebug3("playlist removes effect [hcpl:" + nID + "][hce:" + (_cItemOnAir.iEffect).nID + "][status="+ _cItemOnAir.iEffect.eStatus + "][air_count="+ _aItemsOnAir.Count + "]");
				Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
				aCAs.Add(_cItemOnAir.iEffect, ContainerAction.Remove);
				((IContainer)this).EffectsProcess(aCAs);
			}
			lock (_aqContainerActions)
            {
                if (0 < _aqContainerActions.Count)
                {
					(new Logger()).WriteDebug4("playlist has container actions for process [hcpl:" + nID + "]");
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
                                                        (new Logger()).WriteWarning("adding this item to async prepare on container_add [plhc:" + nID + "][hc:" + _aItemsQueue[0].iEffect.nID + "][total_items=" + _aqPrepareQueue.nCount + "]");
                                                        _aqPrepareQueue.Enqueue(_aItemsQueue[0].iEffect);
														bItemPreparing = true;
													}
												}
												if (bItemPreparing)
												{
													(new Logger()).WriteDebug2("waiting for preparing in lock _aItemsQueue (!) in FrameNextVideo in container_actions. [hc=" + _aItemsQueue[0].iEffect.nID + "]");
													while (_aqPrepareQueue.Contains(_aItemsQueue[0].iEffect))
														System.Threading.Thread.Sleep(1); //ждем когда он отпрепаритс¤.... вариантов у нас нет
												}
												if (EffectStatus.Preparing == _aItemsQueue[0].iEffect.eStatus)
												{
													_aItemsQueue[0].iEffect.Start(null);
													(new Logger()).WriteDebug2("playlist item started in container_actions. [fhc:" + _aItemsQueue[0].iEffect.nID + "][plhc:" + nID + "]" + (_aItemsQueue[0].iEffect is Video ? "[file_queue:" + ((Video)_aItemsQueue[0].iEffect).nFileQueueLength + "]" : ""));
													_aItemsQueue[0].iEffect.iContainer = this;
												}

												(new Logger()).WriteDebug3("playlist takes item [plhc:" + nID + "] with status:" + _aItemsQueue[0].iEffect.eStatus.ToString());

												_aItemsOnAir.Add(_aItemsQueue[0]);
												_aItemsQueue.RemoveAt(0);
												lock (_aqPrepareQueue.oSyncRoot)
												{
                                                    for (int nI = 0; nI < (_aItemsQueue.Count > 2 ? 2 : _aItemsQueue.Count); nI++)
                                                    {
                                                        if (!_aqPrepareQueue.Contains(_aItemsQueue[nI].iEffect) && EffectStatus.Idle == _aItemsQueue[nI].iEffect.eStatus) // что б заранее отпрепарить 2 next effects
                                                        {
                                                            _aqPrepareQueue.Enqueue(_aItemsQueue[nI].iEffect);
                                                            (new Logger()).WriteDebug2("effect [#" + nI + "] added to async prepare in container_add [plhc:" + nID + "][hc:" + _aItemsQueue[nI].iEffect.nID + "][total_items=" + _aqPrepareQueue.nCount + "]");
                                                        }
                                                    }
												}
											}
                                            else
                                                throw new Exception(_aItemsQueue.Count > 0 ? "trying to add non-playlist item" : "there are no items in queue to add");
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
					(new Logger()).WriteDebug4("playlist hasn't container actions for process [hc:" + nID + "]");
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
						cPLI = _aItemsQueue.FirstOrDefault(o => o.iEffect.nID == nHash);
						if (null != cPLI)
							aItemsToDelete.Add(cPLI);
					}
					foreach (Item cPLIt in aItemsToDelete)
					{
						(new Logger()).WriteDebug3("effect removed from playlist [hc = " + cPLIt.iEffect.nID + "]");
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
				(new Logger()).WriteDebug3("playlist is empty [hc:" + nID + "]");
				if (null != OnPlaylistIsEmpty)
					OnPlaylistIsEmpty(this);
                if (_bStopOnEmpty && EffectStatus.Stopped != this.eStatus)
                    Stop();
            }
			sLog += " [if_1 = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;

            if (1 == _aItemsOnAir.Count && 0 < _aItemsQueue.Count && !_aItemsOnAir[0].bIsActive)
            {
				(new Logger()).WriteDebug3("playlist adds next effect [hc:" + nID + "]");
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
					(new Logger()).WriteDebug3("playlist removes effect [hcpl:" + nID + "][hce:" + (_aItemsOnAir[nIndx].iEffect).nID + "][status=" + (_aItemsOnAir[nIndx].iEffect).eStatus + "][air_count=" + _aItemsOnAir.Count + "]");
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
						nDur = GetDur(_aItemsOnAir[nIndx].iEffect);
                        if (ulong.MaxValue > nDur)
                        {
							sLog += " [if_5 before lock = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
							nStart = DateTime.Now.Ticks;

							ulong nFrameCurrent = _aItemsOnAir[nIndx].iEffect.nFrameCurrent;
							lock (_aItemsQueue)
							#region –асчет nNextTransDuration и если пора, то запуск _cTransitionEffect
							{
								if (new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds > 40)
									(new Logger()).WriteDebug2("locking _aItemsQueue in FrameNextVideo in FOR after more then 40ms waiting=" + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds);
								(new Logger()).WriteDebug4("locking _aItemsQueue in FrameNextVideo in FOR");
								long nStart2 = DateTime.Now.Ticks;
								sLog += " [if_5 lock begins = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
								nStart = DateTime.Now.Ticks;

								//if (_bStopping)
								//{
								//	foreach (Item cItt in _aItemsQueue)
								//		if (cItt.iEffect.eStatus == EffectStatus.Preparing || cItt.iEffect.eStatus == EffectStatus.Running)
								//			cItt.iEffect.Stop();
								//	_aItemsQueue.Clear();
								//	nDur = _aItemsOnAir[nIndx].iEffect.nDuration = (ulong)(nFrameCurrent + _nEndTransDuration + 1);
								//}
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
									if (nFrameCurrent % 100 == 0)  // раз в 4 секунды дЄргать, что б не слишком часто
										CheckQueueForPrepare(nDur - nFrameCurrent - _nNextTransDuration);
									if (nFrameCurrent == nDur - _nNextTransDuration - 1)
									{                               // т.е. если следующий элемент есть и его пора давать
										if (0 == _nNextTransDuration)
										{
											(new Logger()).WriteDebug3("playlist removes effect (for transition) [hcpl:" + nID + "][hce:" + (_aItemsOnAir[nIndx].iEffect).nID + "][status=" + (_aItemsOnAir[nIndx].iEffect).eStatus + "][air_count=" + _aItemsOnAir.Count + "]");
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
											(new Logger()).WriteDebug3("transition prepare and start [hc_of_target:" + _aItemsQueue[0].iEffect.nID + "]");
											cTransition.Prepare();
											cTransition.Start(this);
										}
										_nNextTransDuration = ushort.MaxValue;
									}
								}
								//else   // это просто стандартный nOutDissolve
								//{
								//	if (nFrameCurrent == nDur - _nEndTransDuration - 1 && 1 < _nEndTransDuration
								//		) //  || nFrameCurrent < nDur - _nEndTransDuration - 1 && _bStopping
								//	{
								//		if (EffectStatus.Running == ((IEffect)_cTransition.iEffect).eStatus)
								//			_cTransition.iEffect.Stop();
								//		if (EffectStatus.Stopped == ((IEffect)_cTransition.iEffect).eStatus)
								//			_cTransition.iEffect.Idle();
								//		_cTransition.iEffect.nDuration = _nEndTransDuration;
								//		_cTransition.iEffect.iContainer = this;
								//		Transition cTransition = (Transition)_cTransition.iEffect;
								//		cTransition.EffectSourceSet(_aItemsOnAir[nIndx].iEffect);
								//		cTransition.EffectTargetSet(null);
								//		cTransition.eTransitionTypeVideo = _aItemsOnAir[nIndx].cTransitionVideo;
								//		cTransition.eTransitionTypeAudio = _aItemsOnAir[nIndx].cTransitionAudio;
								//		cTransition.Prepare();
								//		cTransition.Start(this);
								//	}
								//}
								sLog += " [if_5_inside_lock = " + new TimeSpan(DateTime.Now.Ticks - nStart2).TotalMilliseconds + "ms]";
							}
							sLog += " [if_5_outside_lock = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
                            #endregion
                        }
                    }
					nStart = DateTime.Now.Ticks;
                    cVideoEffect = (IVideo)_aItemsOnAir[nIndx].iEffect;
					(new Logger()).WriteDebug4("playlist calls for next frame from effect [hc:" + nID + "][hce:" + _aItemsOnAir[nIndx].iEffect.nID + "]");
					long nCBStart = DateTime.Now.Ticks;

                    cVideoEffect.nPixelsMapSyncIndex = nPixelsMapSyncIndex;
                    cFrame = cVideoEffect.FrameNext();

                    sLog += " [video_frame_next = " + new TimeSpan(DateTime.Now.Ticks - nCBStart).TotalMilliseconds + "ms]" + (_aItemsOnAir[nIndx].iEffect is Video ? "[video_queue_length = " + ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]" : "") + (_aItemsOnAir[nIndx].iEffect is Animation ? "[anim_cache = " + ((Animation)_aItemsOnAir[nIndx].iEffect).nCacheCurrent + "]" : "");
					(new Logger()).WriteDebug4("playlist after videoframenext");
					if (_aItemsOnAir[nIndx].iEffect is Video && ((Video)_aItemsOnAir[nIndx].iEffect).bFramesStarvation)
						(new Logger()).WriteDebug2("there is a frame starvation! [queue="+ ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]");
                    //					Baetylus.Helper.cBaetylus.sPLLogs += "PL[" + this.nID + "]:cVideoEffect.FrameNext(): [ " + new TimeSpan(DateTime.Now.Ticks - nCBStart).TotalMilliseconds.ToString() + "ms] ";
                    sLog += " [if_1_outside_lock = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]"; // раньше сто¤ла за фр.некстом
                }
            }
			sLog += " [FOR = " + new TimeSpan(DateTime.Now.Ticks - nStartFor).TotalMilliseconds + "ms]";
			nStart = DateTime.Now.Ticks;
			if (null != cFrame)
				cFrame.Move((short)(cFrame.stArea.nLeft + stArea.nLeft), (short)(cFrame.stArea.nTop + stArea.nTop));
			sLog += " [move = " + new TimeSpan(DateTime.Now.Ticks - nStart).TotalMilliseconds + "ms]";
			if (new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds >= 20)    // logging  >= Preferences.nFrameDuration
				(new Logger()).WriteNotice("playlist.cs: FrameNext(): [pl_id=" + nID + "] execution time > " + Preferences.nFrameDuration + "ms: [dur = " + new TimeSpan(DateTime.Now.Ticks - nStart0).TotalMilliseconds.ToString() + "ms]" + sLog);
			(new Logger()).WriteDebug4("return");
            if (null != cFrame)
            {
                cFrame.nAlphaConstant = nCurrentOpacity;
                if (null != cMask)
                    cFrame.eAlpha = cMask.eMaskType;
            }

            if (this.nFrameCurrent >= this.nDuration && EffectStatus.Stopped != this.eStatus)
            {
                Stop();
            }
            return cFrame;
        }
        override public Bytes FrameNextAudio()
        {
            base.FrameNextAudio();
            Bytes aAudioBytes = null;
            int nIndx;
			string sLog = "";
			for (nIndx = 0; nIndx < _aItemsOnAir.Count; nIndx++)
            {
                if (EffectStatus.Running != _aItemsOnAir[nIndx].iEffect.eStatus)
                    continue;
                if (_aItemsOnAir[nIndx].bIsActive && _aItemsOnAir[nIndx].iEffect is IAudio)
                {
                    //cAudioEffect = (IAudio)_aItemsOnAir[nIndx].iEffect;
					long nCBStart = DateTime.Now.Ticks;
					sLog = "Audio frame next: [pl_hc = " + nID + "][ef_hc = " + _aItemsOnAir[nIndx].iEffect.nID + "]" + (_aItemsOnAir[nIndx].iEffect is Video ? "[video_queue_length before = " + ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]" : "");
					aAudioBytes = ((IAudio)_aItemsOnAir[nIndx].iEffect).FrameNext();
					double nTM = new TimeSpan(DateTime.Now.Ticks - nCBStart).TotalMilliseconds;
					if (nTM >= Preferences.nFrameDuration)
						(new Logger()).WriteNotice(sLog + "[exec time " + nTM + " ms]" + (_aItemsOnAir[nIndx].iEffect is Video ? "[video_queue_length after = " + ((Video)_aItemsOnAir[nIndx].iEffect).nFileQueueLength + "]" : ""));

					(new Logger()).WriteDebug4("playlist after audioframenext");

                    //					Baetylus.Helper.cBaetylus.sPLLogs += "PL[" + this.nID + "]:cAudioEffect.SampleNext(): [ " + nTM.ToString() + "ms] ";
                }
            }
			if (null == aAudioBytes)
			{
				(new Logger()).WriteDebug2("playlist retuns audio silence. [pl_hc = " + nID + "] log=" + sLog);
				aAudioBytes = Baetylus.cAudioSilence2Channels;
			}
			if (nCurrentLevel < 1)
				aAudioBytes = Transition.TransitionAudioFrame(aAudioBytes, Transition.TypeAudio.crossfade, 1 - nCurrentLevel);
			return aAudioBytes;
		}
		private ulong GetDur(IEffect cE)
		{
			return cE.nDuration < cE.nFramesTotal ? cE.nDuration : cE.nFramesTotal;
		}
		private void CheckQueueForPrepare(ulong nCurrentRemain)
		{
			int nIndx = 0;
			while (nCurrentRemain < 1500 && nIndx < _aItemsQueue.Count)
			{
				lock (_aqPrepareQueue.oSyncRoot)
				{
					if (EffectStatus.Idle == _aItemsQueue[nIndx].iEffect.eStatus && !_aqPrepareQueue.Contains(_aItemsQueue[nIndx].iEffect))    
					{
                        (new Logger()).WriteDebug2("adding to async prepare on check_queue [hc:" + _aItemsQueue[nIndx].iEffect.nID + "] [total_items=" + _aqPrepareQueue.nCount + "]");
                        _aqPrepareQueue.Enqueue(_aItemsQueue[nIndx].iEffect);
					}
				}
				if (ulong.MaxValue > GetDur(_aItemsQueue[nIndx].iEffect))
					nCurrentRemain += GetDur(_aItemsQueue[nIndx].iEffect);
				else
					break;
				nIndx++;
			}
		}
		override internal void EffectsProcess(Dictionary<IEffect, ContainerAction> ahMoveInfos)
        {//заносит в _aqContainerActions все операции дл¤ Worker ....
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
            lock (_oLockStop)
            {
                if (_bStopped && _bStopping)
                    return;
                if (_bStopping)
                _bStopped = true;
            }
            (new Logger()).WriteDebug3("playlist stopped [hc:" + nID + "] [x,y: " + stArea.nLeft + ", " + stArea.nTop + "]");
			if (
					!_bStopping && 
					2 == _aItemsOnAir.Count && 
					!_aItemsOnAir[0].bIsActive && 
					_aItemsOnAir[1].iEffect.nDuration - _aItemsOnAir[1].iEffect.nFrameCurrent > nOutDissolve &&  // есть место у эффекта
					this.nDuration > this.nFrameCurrent + nOutDissolve + 1)
			{
				_bStopping = true;
				this.nDuration = this.nFrameCurrent + nOutDissolve + 1;
				return;
            }
			base.Stop();
        }
        public void EndTransDurationSet(ushort nEndTransDuration)
        {
			nOutDissolve = (byte)nEndTransDuration;
        }
    }
}
