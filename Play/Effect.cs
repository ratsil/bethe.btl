using System;
using System.Collections.Generic;
using System.Text;
using helpers;
using System.Xml;
using helpers.extensions;

namespace BTL.Play
{
    abstract public class Effect : IEffect
    {
        #region events processing
        public delegate void EventDelegate(Effect cSender);
        public event EventDelegate Prepared;
        public event EventDelegate Started;
        public event EventDelegate Stopped;
        public event EventDelegate Failed;
        static private ThreadBufferQueue<Tuple<EventDelegate, Effect>> _aqEvents;
        static private System.Threading.Thread _cThreadEvents;
        static private int _nIDMax;
        
        static Effect()
        {
            _aqEvents = new ThreadBufferQueue<Tuple<EventDelegate, Effect>>(false, true);
            _cThreadEvents = new System.Threading.Thread(WorkerEvents);
            _cThreadEvents.IsBackground = true;
            _cThreadEvents.Start();
            _nIDMax = 2000000;
        }
        static private void WorkerEvents()
        {
            Tuple<EventDelegate, Effect> cEvent;
            Logger.Timings cTimings = new Logger.Timings("effect_WorkerEvents");
            while (true)
            {
                try
                {
                    cEvent = _aqEvents.Dequeue();
                    cTimings.TotalRenew();
                    cEvent.Item1(cEvent.Item2);
                    (new Logger()).WriteDebug3("event sent e [hc = " + cEvent.Item2.nID + "][" + cEvent.Item1.Method.Name + "][events_queue=" + _aqEvents.nCount + "]");
                    //GC.Collect
                    cTimings.Stop("work too long", "", 5);
                }
                catch (System.Threading.ThreadAbortException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    (new Logger()).WriteError(ex);
                }
            }
        }
        static public void EventSend(EventDelegate dEvent, Effect cSender)
        {
            (new Logger()).WriteDebug3("in [hc = " + cSender.nID + "][" + dEvent.Method.Name + "]");
            _aqEvents.Enqueue(new Tuple<EventDelegate, Effect>(dEvent, cSender));
        }
        #endregion
        static private int NextIDGet()
        {
            return System.Threading.Interlocked.Increment(ref _nIDMax);
        }

        private readonly int _nID;
        private IContainer _iContainer;
        private EffectType _eType;
        private EffectStatus _eStatus;
        private DateTime _dtStatusChanged;
        private ushort _nLayer;
        private ulong _nDelay;
        private ulong _nFramesTotal;
        private ulong _nFrameStart;
        private ulong _nFrameCurrent;
        private ulong _nDuration;
        private object _oLock;
        private bool _bDisposed;
        private ulong _nSimultaneousID;
        private ushort _nSimultaneousTotalQty;
        private bool _bSimultaneousWait;

        virtual internal IContainer iContainer
        {
            get
            {
                return _iContainer;
            }
            set
            {
                _iContainer = value;
            }
        }
        virtual internal EffectType eType
        {
            get
            {
                return _eType;
            }
            set
            {
                _eType = value;
            }
        }
        virtual public EffectStatus eStatus
        {
            get
            {
                return _eStatus;
            }
        }
        virtual public DateTime dtStatusChanged
        {
            get
            {
                return _dtStatusChanged;
            }
        }
        virtual public ushort nLayer
        {
            get
            {
                return _nLayer;
            }
            set
            {
                _nLayer = value;
            }
        }
        virtual public ulong nDelay
        {
            get
            {
                return _nDelay;
            }
            set
            {
                _nDelay = value;
            }
        }
        virtual public ulong nFramesTotal
        {
            get
            {
                return _nFramesTotal;
            }
        }
        virtual public ulong nFrameStart
        {
            get
            {
                return _nFrameStart;
            }
            set
            {
                _nFrameStart = (ulong.MaxValue > value ? value : 0);
            }
        }
        virtual public ulong nFrameCurrent
        {
            get
            {
                return _nFrameCurrent;
            }
            protected set
            {
                _nFrameCurrent = value;
            }
        }
        virtual public ulong nDuration
        {
            get
            {
                return _nDuration;
            }
            set
            {
                ulong nFT = nFramesTotal;
                if (value > nFT)
                    value = nFT;
                _nDuration = value;
            }
        }
        public int nID
        {
            get
            {
                return _nID;
            }
        }
        public Effect cContainer
        {
            get
            {
                if (null == iContainer || iContainer is Baetylus)
                    return null;
                return (Effect)iContainer;
            }
        }
        public bool bContainer
        {
            get
            {
                return (this is IContainer);
            }
        }
        public object oTag { get; set; } //хранилка для пользовательской ботвы. внутри BTL не set'ить!!!!
        public string sName { get; set; }
        internal ulong nSimultaneousID
        {
            get
            {
                return _nSimultaneousID;
            }
            set
            {
                _nSimultaneousID = value;
            }
        }
        internal ushort nSimultaneousTotalQty
        {
            get
            {
                return _nSimultaneousTotalQty;
            }
            set
            {
                _nSimultaneousTotalQty = value;
            }
        }
        internal bool bSimultaneousWait
        {
            get
            {
                return _bSimultaneousWait;
            }
            set
            {
                _bSimultaneousWait = value;
            }
        }

        internal Effect(EffectType eType)
        {
            _nID = Effect.NextIDGet();
            (new Logger()).WriteDebug3("in [hc:" + nID + "][type:" + eType.ToString() + "]");
            _eStatus = EffectStatus.Idle;
            _dtStatusChanged = DateTime.Now;
            _eType = eType;
            _iContainer = null;
            _nFramesTotal = ulong.MaxValue;
            _nFrameStart = 0;
            _nFrameCurrent = 0;
            _nDuration = ulong.MaxValue;
            _nDelay = 0;
            _oLock = new object();
            _bDisposed = false;
            _nSimultaneousID = 0;
            _nSimultaneousTotalQty = 0;
            _bSimultaneousWait = false;
            (new Logger()).WriteDebug4("return [hc:" + nID + "]");
        }
        ~Effect()
        {
            try
            {
                (new Logger()).WriteDebug3("in [hc:" + nID + "]");
                Dispose();
                (new Logger()).WriteDebug4("return [hc:" + nID + "]");
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
        }

        virtual public void Dispose()
        {
            lock (_oLock)
            {
                if (_bDisposed)
                    return;
                _bDisposed = true;
            }

            (new Logger()).WriteDebug4("in [hc:" + nID + "]");
            switch (eStatus)
            {
                case EffectStatus.Idle:
                    break;
                case EffectStatus.Preparing:
                    Stop();
                    break;
                case EffectStatus.Running:
                    Stop();
                    break;
                case EffectStatus.Stopped:
                    break;
                case EffectStatus.Error:
                    break;
                default:
                    throw new Exception("dispose:uknown effect status");
            }
            (new Logger()).WriteDebug4("return [hc:" + nID + "]");
        }
        virtual public void Prepare()
        {
            //_iContainer = null; //EMERGENCY почему мы контейнер здесь обнуляем??  попробуем не обнулять )))  х.з. почему... //Может, из-за повторного использования эффекта?
            switch (eStatus)
            {
                case EffectStatus.Idle:
                    break;
                case EffectStatus.Preparing:
                    throw new Exception("prepare:effect already preparing " + this.nID);
                case EffectStatus.Running:
                    throw new Exception("prepare:effect already running " + this.nID);
                case EffectStatus.Stopped:
                    throw new Exception("prepare:effect stopped and must be prepared again " + this.nID);
                case EffectStatus.Error:
                    throw new Exception("prepare:effect has error state " + this.nID);
                default:
                    throw new Exception("prepare:uknown effect status " + this.nID);
            }
            _eStatus = EffectStatus.Preparing;
            _dtStatusChanged = DateTime.Now;
            try
            {
                if (null != Prepared)
                    EventSend(Prepared, this);
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
        }
        virtual public void Start()
        {
            (new Logger()).WriteDebug3("in");
            Start(Baetylus.Helper.cBaetylus);
        }
        virtual public void Start(IContainer iContainer)
        {
            (new Logger()).WriteDebug3("in [hc = " + nID + "]");
            while (true)
            {
                switch (eStatus)
                {
                    case EffectStatus.Idle:
                        Prepare();
                        continue;
                    case EffectStatus.Preparing:
                        if (null == _iContainer && null != iContainer)
                        {
                            Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
                            aCAs.Add(this, ContainerAction.Add);
                            iContainer.EffectsProcess(aCAs);
                            _iContainer = iContainer;
                        }
                        break;
                    case EffectStatus.Running:
                        throw new Exception("start:effect already running " + this.nID);
                    case EffectStatus.Stopped:
                        throw new Exception("start:effect stopped and must be prepared again " + this.nID);
                    case EffectStatus.Error:
                        throw new Exception("start:effect has error state " + this.nID);
                    default:
                        throw new Exception("start:uknown effect status " + this.nID);
                }
                _eStatus = EffectStatus.Running;
                _dtStatusChanged = DateTime.Now;
                break;
            }
            try
            {
                (new Logger()).WriteDebug3("before event started [hc = " + nID + "][event = " + (null == Started ? "null" : "ok") + "]");
                if (null != Started)
                    EventSend(Started, this);
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
            (new Logger()).WriteDebug4("return [hc = " + nID + "]");
        }
        protected void Action()
        {
            switch (eStatus)
            {
                case EffectStatus.Idle:
                    throw new Exception("action:effect must be started before action");
                case EffectStatus.Preparing:
                    throw new Exception("action:effect preparing");
                case EffectStatus.Stopped:
                    throw new Exception("action:effect stopped and must be prepared again " + this.nID);
            }
        }
        virtual public void Stop()
        {
            (new Logger()).WriteDebug3("in [hc = " + nID + "]");
            _iContainer = null;
            switch (eStatus)
            {
                case EffectStatus.Idle:
                    throw new Exception("stop:effect is idle and wasn't started nor prepared" + this.nID);
                case EffectStatus.Preparing:
                    break;
                case EffectStatus.Running:
                    break;
                case EffectStatus.Stopped:
                    throw new Exception("stop:effect already stopped " + this.nID);
                case EffectStatus.Error:
                    throw new Exception("stop:effect has error state " + this.nID);
                default:
                    throw new Exception("stop:uknown effect status " + this.nID);
            }
            _eStatus = EffectStatus.Stopped;
            _dtStatusChanged = DateTime.Now;
            try
            {
                (new Logger()).WriteDebug3("before Stopped [hc = " + nID + "][" + (null == Stopped ? "null" : "ok") + "]");

                if (null != Stopped)
                    EventSend(Stopped, this);
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
            (new Logger()).WriteDebug4("return [hc = " + nID + "]");
        }
        virtual public void Idle()
        {
            switch (eStatus)
            {
                case EffectStatus.Idle:
                    break;
                case EffectStatus.Preparing:
                    break;
                case EffectStatus.Running:
                    throw new Exception("effect must be stopped first " + this.nID);
                case EffectStatus.Stopped:
                    nDelay = 0;
                    break;
                case EffectStatus.Error:
                    throw new Exception("effect has error state " + this.nID);
                default:
                    throw new Exception("uknown effect status " + this.nID);
            }
            _eStatus = EffectStatus.Idle;
            _dtStatusChanged = DateTime.Now;
            //nDuration = ulong.MaxValue; 
            _nFrameCurrent = 0;
        }
        virtual protected void Fail()
        {
            _eStatus = EffectStatus.Error;
            _dtStatusChanged = DateTime.Now;
            try
            {
                if (null != Failed)
                    EventSend(Failed, this);
            }
            catch (Exception ex)
            {
                (new Logger()).WriteError(ex);
            }
        }
        internal void LoadXML(XmlNode cXmlNode)
        {
            nLayer = cXmlNode.AttributeOrDefaultGet<ushort>(new string[] { "layer", "z-buffer" }, 10);
            nFrameStart = cXmlNode.AttributeOrDefaultGet<ulong>("start", 0);
            nDuration = cXmlNode.AttributeOrDefaultGet<ulong>("duration", ulong.MaxValue);
            sName = cXmlNode.AttributeOrDefaultGet<string>("name", null);
            XmlNode cNodeChild;
            nDelay = null == (cNodeChild = cXmlNode.NodeGet("show", false)) ? 0 : cNodeChild.AttributeGet<uint>("delay");
        }
        static public Effect EffectGet(XmlNode cXmlNode)
        {
            switch (cXmlNode.Name)
            {
                // effects
                case "text":
                    return new Text(cXmlNode);
                case "clock":
                    return new Clock(cXmlNode);
                case "animation":
                    return new Animation(cXmlNode);
                case "video":
                    return new Video(cXmlNode);
                case "audio":
                    return new Audio(cXmlNode);
                // containers:
                case "composite":
                    return new Composite(cXmlNode);
                case "playlist":
                    return new Playlist(cXmlNode);
                case "roll":
                    return new Roll(cXmlNode);
            }
            throw new Exception("EffectGet: unknown effect [name=" + cXmlNode.Name + "]");
        }
        public void SimultaneousSet(ulong nSimultaneousID, ushort nSimultaneousTotalQty)
        {
            if (nSimultaneousTotalQty > 1)
            {
                _bSimultaneousWait = true;
                _nSimultaneousID = nSimultaneousID;
                _nSimultaneousTotalQty = nSimultaneousTotalQty;
            }
        }
        public void SimultaneousReset()
        {
            _bSimultaneousWait = false;
            _nSimultaneousID = 0;
            _nSimultaneousTotalQty = 0;
        }
        public bool Equals(IEffect iEffect)
        {
            return null != iEffect && _nID == iEffect.nID;
        }
        public override bool Equals(object obj)
        {
            return Equals((IEffect)obj);
        }
        public override int GetHashCode()
        {
            return nID;
        }



        int IEffect.nID
        {
            get
            {
                return this.nID;
            }
        }
        bool IEquatable<IEffect>.Equals(IEffect iEffect)
        {
            return this.Equals(iEffect);
        }

        event EventDelegate IEffect.Prepared
		{
			add
			{
				this.Prepared += value;
			}
			remove
			{
				this.Prepared -= value;
			}
		}
		event EventDelegate IEffect.Started
		{
			add
			{
				this.Started += value;
			}
			remove
			{
				this.Started -= value;
			}
		}
		event EventDelegate IEffect.Stopped
		{
			add
			{
				this.Stopped += value;
			}
			remove
			{
				this.Stopped -= value;
			}
		}
        event EventDelegate IEffect.Failed
        {
            add
            {
                this.Failed += value;
            }
            remove
            {
                this.Failed -= value;
            }
        }

		void IEffect.Dispose()
        {
			this.Dispose();
        }
        void IEffect.Prepare()
        {
            this.Prepare();
        }
        void IEffect.Start()
        {
            this.Start();
        }
        void IEffect.Start(IContainer iContainer)
        {
            this.Start(iContainer);
        }
        void IEffect.Stop()
        {
            this.Stop();
        }
        void IEffect.Idle()
        {
            this.Idle();
        }
		void IEffect.Fail()
		{
			this.Fail();
		}
		void IEffect.SimultaneousSet(ulong nSimultaneousID, ushort nSimultaneousTotalQty)
		{
			this.SimultaneousSet(nSimultaneousID, nSimultaneousTotalQty);
		}
        void IEffect.SimultaneousReset()
        {
            this.SimultaneousReset();
        }

        IContainer IEffect.iContainer
		{
			get
			{
				return this.iContainer;
			}
			set
			{
				this.iContainer = value;
			}
		}
		EffectType IEffect.eType
        {
			get
			{
				return this.eType;
			}
		}
		EffectStatus IEffect.eStatus
        {
			get
			{
				return this.eStatus;
			}
        }
		DateTime IEffect.dtStatusChanged
		{
			get
			{
				return this.dtStatusChanged;
			}
		}
		ushort IEffect.nLayer
		{
			get
			{
				return this.nLayer;
			}
			set
			{
				this.nLayer = value;
			}
		}

        ulong IEffect.nDelay
        {
            get
            {
                return this.nDelay;
            }
            set
            {
                this.nDelay = value;
            }
        }
		ulong IEffect.nFramesTotal
		{
			get
			{
				return this.nFramesTotal;
			}
		}
		ulong IEffect.nFrameStart
		{
			get
			{
				return this.nFrameStart;
			}
			set
			{
				this.nFrameStart = value;
			}
		}
		ulong IEffect.nFrameCurrent
		{
			get
			{
				return this.nFrameCurrent;
			}
		}
		ulong IEffect.nDuration
		{
			get
			{
				return this.nDuration;
			}
			set
			{
				this.nDuration = value;
			}
		}
        object IEffect.cTag
        {
            get
            {
                return this.oTag;
            }
            set
            {
                this.oTag = value;
            }
        }
        string IEffect.sName
        {
            get
            {
                return this.sName;
            }
            set
            {
                this.sName = value;
            }
        }

    }
}
