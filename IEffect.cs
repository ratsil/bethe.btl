using System;
using System.Collections.Generic;
using System.Text;
using BTL.Play;

namespace BTL
{
    public enum EffectStatus
    {
		Error = -1,
        Unknown = 0,
        Idle = 1,
        Preparing = 2,
        Running = 3,
        Stopped = 4
    }
    public enum EffectType
    {
        Animation,
        Video,
        Playlist,
        Text,
        Clock,
        Roll,
        Transition,
        Composite,
		Audio
    }
    public interface IEffect : IEquatable<IEffect>
    {
        event Effect.EventDelegate Prepared;
		event Effect.EventDelegate Started;
		event Effect.EventDelegate Stopped;
        event Effect.EventDelegate Failed;

        int nID { get; }
        IContainer iContainer { get; set; }
		EffectType eType { get; }
		EffectStatus eStatus { get; }
		DateTime dtStatusChanged { get; }
		ushort nLayer { get; set; }
        ulong nDelay { get; set; }
		ulong nFramesTotal { get; }
		ulong nFrameStart { get; set; }
		ulong nFrameCurrent { get; }
		ulong nDuration { get; set; }
        object cTag { get; set; }
        string sName { get; set; }

        void Prepare();
        void Start();
        void Start(IContainer iContainer);
        void Stop();
        void Idle();
        void Fail();
        void Dispose();
		void SimultaneousSet(ulong nSimultaneousID, ushort nSimultaneousTotalQty);
        void SimultaneousReset();
    }
}
