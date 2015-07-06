using System;
using System.Collections.Generic;
using System.Text;

using helpers;
using BTL.Play;

namespace BTL
{
    public enum ContainerAction
	{
		Add,
		Remove
	}
    public interface IContainer
    {
		event ContainerVideoAudio.EventDelegate EffectAdded;
		event ContainerVideoAudio.EventDelegate EffectPrepared;
		event ContainerVideoAudio.EventDelegate EffectStarted;
		event ContainerVideoAudio.EventDelegate EffectStopped;
		event ContainerVideoAudio.EventDelegate EffectIsOnScreen;
		event ContainerVideoAudio.EventDelegate EffectIsOffScreen;
		event ContainerVideoAudio.EventDelegate EffectFailed;

        ushort nEffectsQty { get; }
		ulong nSumDuration { get; }
		
		void EffectsProcess(Dictionary<IEffect, ContainerAction> ahMoveInfos); //возвращает порядковый номер этой работы чтобы узнать потом, что она сделана //EMERGENCY что-то я не вкурил в этот комментарий
		void EffectsReorder();
    }
}
