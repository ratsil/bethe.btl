using System;
using System.Collections.Generic;
using System.Text;

using helpers;

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
        static Effect()
        {
            _aqEvents = new ThreadBufferQueue<Tuple<EventDelegate, Effect>>(false, true);
            _cThreadEvents = new System.Threading.Thread(WorkerEvents);
            _cThreadEvents.IsBackground = true;
            _cThreadEvents.Start();
        }
        static private void WorkerEvents()
		{
			Tuple<EventDelegate, Effect> cEvent;
			System.Diagnostics.Stopwatch cWatch = new System.Diagnostics.Stopwatch();
			while (true)
			{
				try
				{
					cEvent = _aqEvents.Dequeue();

					cWatch.Reset();
					cWatch.Restart();
					cEvent.Item1(cEvent.Item2);
					(new Logger()).WriteDebug3("event sended [hc = " + cEvent.Item2.GetHashCode() + "][" + cEvent.Item1.Method.Name + "]");
					cWatch.Stop();
					if (40 < cWatch.ElapsedMilliseconds)
						(new Logger()).WriteDebug3("duration: " + cWatch.ElapsedMilliseconds + " queue: " + _aqEvents.nCount);
					if (0 < _aqEvents.nCount)
						(new Logger()).WriteDebug3(" queue: " + _aqEvents.nCount);
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
			(new Logger()).WriteDebug3("in [hc = " + cSender.GetHashCode() + "][" + dEvent.Method.Name + "]");
			_aqEvents.Enqueue(new Tuple<EventDelegate, Effect>(dEvent, cSender));
        }
        #endregion

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
				_nDuration = value;
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

		internal Effect(EffectType eType)
		{
            (new Logger()).WriteDebug3("in [hc:" + GetHashCode() + "][type:" + eType.ToString() + "]");
            _eStatus = EffectStatus.Idle;
			_dtStatusChanged = DateTime.Now;
			_eType = eType;
			_iContainer = null;
			_nFramesTotal = 0;
			_nFrameStart = 0;
 			_nFrameCurrent = 0;
           _nDuration = ulong.MaxValue;
            _nDelay = 0;
            (new Logger()).WriteDebug4("return [hc:" + GetHashCode() + "]");
		}
		~Effect()
        {
			try
			{
				(new Logger()).WriteDebug3("in [hc:" + GetHashCode() + "]");
				Dispose();
				(new Logger()).WriteDebug4("return [hc:" + GetHashCode() + "]");
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
		}

        virtual public void Dispose()
        {
			(new Logger()).WriteDebug4("in [hc:" + GetHashCode() + "]");
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
					throw new Exception("uknown effect status");
			}
			(new Logger()).WriteDebug4("return [hc:" + GetHashCode() + "]");
		}
		virtual public void Prepare()
        {
            //_iContainer = null; //EMERGENCY почему мы контейнер здесь обнуляем??  попробуем не обнулять )))  х.з. почему... //Может, из-за повторного использования эффекта?
            switch (eStatus)
            {
                case EffectStatus.Idle:
                    break;
                case EffectStatus.Preparing:
					throw new Exception("effect already preparing " + this.GetHashCode());
                case EffectStatus.Running:
					throw new Exception("effect already running" + this.GetHashCode());
                case EffectStatus.Stopped:
					throw new Exception("effect stopped and must be prepared again" + this.GetHashCode());
				case EffectStatus.Error:
					throw new Exception("effect has error state" + this.GetHashCode());
				default:
					throw new Exception("uknown effect status" + this.GetHashCode());
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
			(new Logger()).WriteDebug3("in [hc = " + GetHashCode() + "]");
            while (true)
            {
                switch (eStatus)
                {
                    case EffectStatus.Idle:
                        Prepare();
                        continue;
                    case EffectStatus.Preparing:
                        if (null != iContainer)
                        {
                            Dictionary<IEffect, ContainerAction> aCAs = new Dictionary<IEffect, ContainerAction>();
                            aCAs.Add(this, ContainerAction.Add);
                            iContainer.EffectsProcess(aCAs);
                            _iContainer = iContainer;
                        }
                        break;
                    case EffectStatus.Running:
						throw new Exception("effect already running" + this.GetHashCode());
                    case EffectStatus.Stopped:
						throw new Exception("effect stopped and must be prepared again" + this.GetHashCode());
					case EffectStatus.Error:
						throw new Exception("effect has error state" + this.GetHashCode());
					default:
						throw new Exception("uknown effect status" + this.GetHashCode());
				}
				_eStatus = EffectStatus.Running;
				_dtStatusChanged = DateTime.Now;
				break;
            }
			try
			{
				(new Logger()).WriteDebug3("before event started [hc = " + GetHashCode() + "][event = " + (null == Started ? "null" : "ok") + "]");
				if (null != Started)
                    EventSend(Started, this);
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
			(new Logger()).WriteDebug4("return [hc = " + GetHashCode() + "]"); 
		}
        protected void Action()
        {
            switch (eStatus)
            {
                case EffectStatus.Idle:
                    throw new Exception("effect must be started before action");
                case EffectStatus.Preparing:
                    throw new Exception("effect preparing");
                case EffectStatus.Stopped:
                    throw new Exception("effect stopped and must be prepared again");
            }
        }
        virtual public void Stop()
        {
			(new Logger()).WriteDebug3("in [hc = " + GetHashCode() + "]");
            _iContainer = null;
            switch (eStatus)
            {
                case EffectStatus.Idle:
					throw new Exception("effect is idle and wasn't started nor prepared" + this.GetHashCode());
                case EffectStatus.Preparing:
                    break;
                case EffectStatus.Running:
                    break;
				case EffectStatus.Stopped:
					throw new Exception("effect already stopped" + this.GetHashCode());
				case EffectStatus.Error:
					throw new Exception("effect has error state" + this.GetHashCode());
				default:
					throw new Exception("uknown effect status" + this.GetHashCode());
			}
			_eStatus = EffectStatus.Stopped;
			_dtStatusChanged = DateTime.Now;
			try
			{
				(new Logger()).WriteDebug3("before Stopped [hc = " + GetHashCode() + "]["+(null == Stopped ? "null":"ok")+"]");

				if (null != Stopped)
                    EventSend(Stopped, this);
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
			(new Logger()).WriteDebug4("return [hc = " + GetHashCode() + "]");
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
					throw new Exception("effect must be stopped first" + this.GetHashCode());
                case EffectStatus.Stopped:
                    nDelay = 0;
                    break;
				case EffectStatus.Error:
					throw new Exception("effect has error state" + this.GetHashCode());
				default:
					throw new Exception("uknown effect status" + this.GetHashCode());
			}
            _eStatus = EffectStatus.Idle;
			_dtStatusChanged = DateTime.Now;
            nDuration = ulong.MaxValue;
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
    }
}
