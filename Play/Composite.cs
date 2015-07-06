using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using helpers;
using System.IO;
using System.Runtime.InteropServices;

namespace BTL.Play
{
    //пока условия такие. 
    //Элементы добавляются только до препаре. 
    //Area не меняется и равна объедиению эрий всех элементов.
    public class Composite : EffectVideo     
    {
        class Line
        {
            public ushort nWidth = 0;
            public ushort nHeight = 0;
            public bool bFull = false;
			public List<EffectVideo> aLine;
            Composite _cParent;
			public Line(EffectVideo cEffect, Composite cParent)
            {
				aLine = new List<EffectVideo>();
                aLine.Add(cEffect);
                nWidth = cEffect.stArea.nWidth;
                nHeight = cEffect.stArea.nHeight;
                _cParent = cParent;
				Area stArea = _cParent.stArea;
                stArea.nHeight += nHeight;
				_cParent.stArea = stArea;
				stArea = cEffect.stArea;
				stArea.nTop = (short)(_cParent._nHeightTotal + cEffect.stArea.nTop);
				cEffect.stArea = stArea;
            }
			internal void Add(EffectVideo cEffect)
            {
				Area stArea = cEffect.stArea;
				stArea.nTop = (short)(_cParent._nHeightTotal + cEffect.stArea.nTop);
                stArea.nLeft = (short)nWidth;
				cEffect.stArea = stArea;
                aLine.Add(cEffect);
                nWidth += cEffect.stArea.nWidth;
                if (nHeight < cEffect.stArea.nHeight)
                {
					stArea = _cParent.stArea;
                    stArea.nHeight += (ushort)(cEffect.stArea.nHeight - nHeight);
					_cParent.stArea = stArea;
                    short nDelta = (short)((float)(cEffect.stArea.nHeight - nHeight) / 2F + 0.5F);
                    nHeight = cEffect.stArea.nHeight;
					for (int ni = 0; aLine.Count - 1 > ni; ni++)
					{
						stArea = aLine[ni].stArea;
						stArea.nTop += nDelta;
						aLine[ni].stArea = stArea;
					}
                }
                else if (nHeight > cEffect.stArea.nHeight)
                {
                    short nDelta = (short)((float)(nHeight - cEffect.stArea.nHeight) / 2F + 0.5F);
					stArea = cEffect.stArea;
					stArea.nTop += nDelta;
					cEffect.stArea = stArea;
                }
                if (0 < _cParent._nCurentIndent)
                {
					stArea = aLine[aLine.Count - 1].stArea;
					stArea.nLeft += (short)_cParent._nCurentIndent;
					aLine[aLine.Count - 1].stArea = stArea;
                    nWidth += _cParent._nCurentIndent;
                }
            }
        }
        class Column
        { }
        public enum Type
        { 
            Vertical,
            Horizontal,
            Fixed
        }
        private Type _eType;
        private ushort _nWidth;
        private ushort _nHeight;
        private ushort _nCurentIndent;
		private bool _bDisposed;
		private object _oLockDispose;

        private PixelsMap _cPixelsMap;
        private List<Line> _aLines;
        private List<Column> _aColumns;
        private List<Effect> _aEffects;   //эффекты все лежат тут и они же еще лежат в _aLines или _aColumns
        private ushort _nHeightTotal
        {
            get 
            {
                ushort nRetVal = 0;
                foreach(Line cLine in _aLines)
                {
                    if (cLine.bFull)
                        nRetVal += cLine.nHeight;
                }
                return nRetVal;
            }
        }

		//override public ulong nFramesTotal     //  отрубил проверить чо будет    тест проба внимание  //////////////////
		//{
		//    get
		//    {
		//        return ulong.MaxValue;
		//    }
		//}
		//override public ulong nFrameCurrent
		//{
		//    get
		//    {
		//        return 0;
		//    }
		//}
		private Composite()
            : base(EffectType.Composite)
        {
            _nWidth = 0;
            _nHeight = 0;
            _aEffects = new List<Effect>();
			_bDisposed = false;
			_oLockDispose = new object();
        }
        public Composite(ushort nDimensionTargetMax, Type enType) 
            :this()
        {
            switch (enType)
            {
                case Type.Vertical:
					_nWidth = nDimensionTargetMax;
                    _aLines = new List<Line>();
                    enType = Type.Vertical;
                    break;
                case Type.Horizontal:                 //это конструктор горизонтальный. TODO. ДОДЕЛАТЬ ДОБАВЛЕНИЕ !!!!!!!!!  
					_nHeight = nDimensionTargetMax;
                    _aColumns = new List<Column>();
                    enType = Type.Horizontal;
                    break;
                case Type.Fixed:
					_nHeight = _nWidth = nDimensionTargetMax;
                    stArea = new Area(0, 0, _nWidth, _nHeight);
                    enType = Type.Fixed;
                    break;
                default:
                    break;
            }
            
        }
        public Composite(ushort nWidth, ushort nHeight) //это конструктор для пустышки или константного размера
            : this()
        {
            stArea = new Area(0, 0, nWidth, nHeight);
            _nWidth = nWidth;
            _nHeight = nHeight;
            _eType = Type.Fixed;
        }
        ~Composite()
        {
            try
            {
                _cPixelsMap.Dispose();
            }
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
        }
        public override void Dispose()
        {
			lock(_oLockDispose)
			{
				if (_bDisposed)
					return;
				_bDisposed = true;
			}
			try
			{
				if (null != _aEffects)
				{
					lock (_aEffects)
					{
						foreach (Effect cEff in _aEffects)
						{
							cEff.Dispose();
						}
						if (null != _aColumns)
						{
							_aColumns.Clear();
							_aColumns = null;
						}
						if (null != _aLines)
						{
							_aLines.Clear();
							_aLines = null;
						}
					}
					if (0 < _aEffects.Count)
						new System.Threading.Thread(() =>
						{
							System.Threading.Thread.CurrentThread.IsBackground = true;
							try
							{
								for (int nI = 0; nI < _aEffects.Count; nI++)
								{
									_aEffects[nI] = null;
									System.Threading.Thread.Sleep(10);
									GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
								}
								_aEffects = null;
							}
							catch { }
							(new Logger()).WriteDebug3("composite disposed: " + GetHashCode());
						}).Start();
					}
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
			base.Dispose();
        }

        public void EffectAdd(EffectVideo cEffect)
        {
			EffectAdd(cEffect, 0);
        }
		public void EffectAdd(EffectVideo cEffect, ushort nIndent)
        {
            if (null == cEffect)
                throw new Exception("effect:add: общий массив объектов не инициализирован");
            _aEffects.Add(cEffect);
            _nCurentIndent = nIndent;

            switch (_eType)
            {
                case Type.Vertical:
                    if (null == _aLines)
                        throw new Exception("effect:add: горизонтальный массив объектов не инициализирован");
                    if (1 > _aLines.Count || cEffect.stArea.nWidth > _nWidth - _aLines[_aLines.Count - 1].nWidth)
                    {
                        if (0 < _aLines.Count)
                            _aLines.Last().bFull = true;
                        _aLines.Add(new Line(cEffect, this));
                    }
                    else
                        _aLines[_aLines.Count - 1].Add(cEffect);
					Area stArea = this.stArea;
                    stArea.nWidth = stArea.nWidth < _aLines[_aLines.Count - 1].nWidth ? stArea.nWidth = _aLines[_aLines.Count - 1].nWidth : stArea.nWidth;
					this.stArea = stArea;
                    break;
                case Type.Horizontal:
                    //TODO   реализовать бы...
                    if (null == _aColumns)
                        throw new Exception("effect:add: вертикальный массив объектов не инициализирован");
                    break;
                case Type.Fixed:
                    //здесь ничего не надо - в _aEffects уже добавили ))
                    break;
                default:
                    break;
            }
            
        }
        public void InvertOrder()
        {
            throw new NotImplementedException("НЕ РЕАЛИЗОВАНО !!!"); //TODO   должна менять строки или столбцы местами
        }
        override public void Prepare()
        {
            lock (_aEffects)
                try
                {
                    if (EffectStatus.Idle != ((IEffect)this).eStatus)
                        return;
                    foreach (Effect cEffect in _aEffects)
						if (EffectStatus.Idle == cEffect.eStatus)
							cEffect.Prepare();
                    _cPixelsMap = new PixelsMap(this.bCUDA, this.stArea, PixelsMap.Format.ARGB32);
                    _cPixelsMap.bKeepAlive = true;
					if (1 > _cPixelsMap.nLength)
						(new Logger()).WriteNotice("1 > _cPixelMap.nLength. composite.prepare");
					_cPixelsMap.Allocate();
                    base.Prepare();
                }
                catch (Exception ex)
                {
                    (new Logger()).WriteError(ex);
                }
        }
        override public void Start(IContainer iContainer)
        {
			lock (_aEffects)
			{
				try
				{
					if (EffectStatus.Idle == ((IEffect)this).eStatus)
						Prepare();
					if (EffectStatus.Preparing != ((IEffect)this).eStatus)
						return;
					foreach (Effect cEffect in _aEffects)
						if (EffectStatus.Preparing == cEffect.eStatus || EffectStatus.Idle == cEffect.eStatus)
							cEffect.Start(null);
                    base.Start(iContainer);
				}
                catch (Exception ex)
                {
                    (new Logger()).WriteError(ex);
                }
			}
        }
        override public void Stop()
        {
            base.Stop();
        }
        
        override public PixelsMap FrameNext()
        {
			base.FrameNext();
            PixelsMap cPM = null;
            List<PixelsMap> aPMs = new List<PixelsMap>();
            List<Effect> aEffectsUsed = new List<Effect>();
            IVideo iVideo;
            foreach (Effect cEffect in _aEffects)
			{
				if (null == cEffect || !(cEffect is IVideo) || (EffectStatus.Running != cEffect.eStatus) || null == (cPM = (iVideo = (IVideo)cEffect).FrameNext()))
					continue;
                if (null != iVideo.iMask)
                {
                    aPMs.Add(iVideo.iMask.FrameNext());
                    aPMs[aPMs.Count - 1].eAlpha = DisCom.Alpha.mask;
                }
				aPMs.Add(cPM);
			}
			if (0 < aPMs.Count)   // когда ==0   - это пустышка и не надо для нее делать pixelsmap
			{
                _cPixelsMap.Merge(aPMs);
				Baetylus.PixelsMapDispose(aPMs.ToArray());
				//if (bCUDA && !cRetVal.bCUDA)
                //{
                //    cPM = new PixelsMap(true, stArea, PixelsMap.Format.ARGB32);
                //    cPM.CopyIn(cRetVal.CopyOut());
                //    cPM.bKeepAlive = cRetVal.bKeepAlive;
                //    PixelsMap cTemp = cRetVal;
                //    cRetVal = cPM;
                //    cTemp.Dispose(true);
                //}
			}
			if (null != _cPixelsMap)
				_cPixelsMap.nAlphaConstant = nCurrentOpacity;
            return _cPixelsMap;
        }
    }
}
