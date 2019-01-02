using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using helpers;

namespace BTL.Play
{
    abstract public class ContainerVideoAudio : EffectVideoAudio, IContainer
    {
        #region events processing
		new public delegate void EventDelegate(Effect cSender, Effect cEffect);
		public event EventDelegate EffectAdded;
		public event EventDelegate EffectPrepared;
		public event EventDelegate EffectStarted;
		public event EventDelegate EffectStopped;
		public event EventDelegate EffectIsOnScreen;
		public event EventDelegate EffectIsOffScreen;
		public event EventDelegate EffectFailed;
        static private ThreadBufferQueue<Tuple<EventDelegate, Effect, Effect>> _aqEvents;
        static private System.Threading.Thread _cThreadEvents;
        static ContainerVideoAudio()
        {
            _aqEvents = new ThreadBufferQueue<Tuple<EventDelegate, Effect, Effect>>(false, true);
            _cThreadEvents = new System.Threading.Thread(WorkerEvents);
            _cThreadEvents.IsBackground = true;
            _cThreadEvents.Start();
        }
        static private void WorkerEvents()
        {
            Tuple<EventDelegate, Effect, Effect> cEvent;
			Logger.Timings cTimings = new Logger.Timings("conteiner_WorkerEvents");
            while (true)
			{
				try
				{
					cEvent = _aqEvents.Dequeue();

					cTimings.TotalRenew();
					cEvent.Item1(cEvent.Item2, cEvent.Item3);
					(new Logger()).WriteDebug3("event sent cva [hc = " + cEvent.Item3.nID + "][" + cEvent.Item1.Method.Name + "][events_queue=" + _aqEvents.nCount + "]");
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
        static public void EventSend(EventDelegate dEvent, Effect cSender, Effect cEffect)
        {
			(new Logger()).WriteDebug3("in [hc = " + cEffect.nID + "]");
			_aqEvents.Enqueue(new Tuple<EventDelegate, Effect, Effect>(dEvent, cSender, cEffect));
        }
        virtual protected void OnEffectAdded(Effect cSender)
        {
            if (null != EffectAdded)
                EventSend(EffectAdded, this, cSender);
        }
        virtual protected void OnEffectPrepared(Effect cSender)
        {
            if (null != EffectPrepared)
                EventSend(EffectPrepared, this, cSender);
        }
        virtual protected void OnEffectStarted(Effect cSender)
        {
			(new Logger()).WriteDebug3("in [hc = " + cSender.nID + "][" + (null == EffectStarted ? "null" : "ok") + "]");
			if (null != EffectStarted)
                EventSend(EffectStarted, this, cSender);
        }
        virtual protected void OnEffectStopped(Effect cSender)
        {
			(new Logger()).WriteDebug3("in [hc = " + cSender.nID + "][" + (null == EffectStopped ? "null" : "ok") + "]");
			if (null != EffectStopped)
                EventSend(EffectStopped, this, cSender);
        }
        virtual protected void OnEffectIsOnScreen(Effect cSender)
        {
            if (null != EffectIsOnScreen)
                EventSend(EffectIsOnScreen, this, cSender);
        }
        virtual protected void OnEffectIsOffScreen(Effect cSender)
        {
            if (null != EffectIsOffScreen)
                EventSend(EffectIsOffScreen, this, cSender);
        }
        virtual protected void OnEffectFailed(Effect cSender)
        {
            if (null != EffectFailed)
                EventSend(EffectFailed, this, cSender);
        }
        #endregion

        virtual public ushort nEffectsQty { get; protected set; }
		virtual public ulong nSumDuration { get; private set; }

		internal ContainerVideoAudio(EffectType eType)
            : base(eType)
        {
        }

		virtual internal void EffectsProcess(Dictionary<IEffect, ContainerAction> ahMoveInfos)
        {
        }
		virtual internal void EffectsReorder()
        {
        }

		#region IContainer implementation

		event EventDelegate IContainer.EffectAdded
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
		event EventDelegate IContainer.EffectPrepared
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
		event EventDelegate IContainer.EffectStarted
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
		event EventDelegate IContainer.EffectStopped
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
		event EventDelegate IContainer.EffectIsOnScreen
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
		event EventDelegate IContainer.EffectIsOffScreen
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
		event EventDelegate IContainer.EffectFailed
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
