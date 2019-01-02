using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using helpers;
using System.IO;
using System.Runtime.InteropServices;
using helpers.extensions;
using System.Xml;

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
        private PixelsMap.Triple _cPMDuo;
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
		private DateTime _dtLastChange;
		private bool _bChanged;
        private bool _bMergedChanges;



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
			Init(nDimensionTargetMax, enType);
		}
        public Composite(ushort nWidth, ushort nHeight) //это конструктор для пустышки или константного размера
            : this()
		{
			Init(nWidth, nHeight);
		}
		public Composite(XmlNode cXmlNode)
			: this()
		{
			try
			{
				base.LoadXML(cXmlNode);
				//bFullRenderOnPrepare = cXmlNode.AttributeOrDefaultGet<bool>("render_on_prepare", false);   // можно бы реализовать, как в роле
				Type cType = cXmlNode.AttributeOrDefaultGet<Type>("type", Type.Fixed);
				if (cType == Type.Fixed)
				{
					Init(cXmlNode.AttributeGet<ushort>("width"), cXmlNode.AttributeGet<ushort>("height"));
				}
				else
					Init(cXmlNode.AttributeGet<ushort>("max_dimension"), cXmlNode.AttributeOrDefaultGet<Type>("type", Type.Vertical));

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
		void Init(ushort nDimensionTargetMax, Type enType)
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
		void Init(ushort nWidth, ushort nHeight)
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
				Dispose();
                Baetylus.PixelsMapDispose(_cPMDuo, true);
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
				Logger.Timings cTimings = new helpers.Logger.Timings("composite:disposing");
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
					{
						cTimings.TotalRenew();
						for (int nI = 0; nI < _aEffects.Count; nI++)
						{
							_aEffects[nI].Dispose();
							_aEffects[nI] = null;
						}
						cTimings.Stop("composite dispose", "GC-mode: " + System.Runtime.GCSettings.LatencyMode + "; btl_queue:" + BTL.Baetylus.nCurrentDeviceBufferCount, 5);
						(new Logger()).WriteDebug3("composite disposed: " + nID);
                    }
                }
			}
			catch (Exception ex)
			{
				(new Logger()).WriteError(ex);
			}
			base.Dispose();
        }

		public void EffectsAdd(XmlNode cXmlNode)
		{
			foreach (XmlNode cXN in cXmlNode.NodesGet())
				EffectAdd((IVideo)Effect.EffectGet(cXN), cXN);
		}
		public void EffectAdd(IVideo iVideo, XmlNode cXmlNode)
		{
			EffectAdd((EffectVideo)iVideo, cXmlNode.AttributeOrDefaultGet<ushort>("indent", 0));
		}
		public void EffectAdd(EffectVideo cEffect)
        {
			EffectAdd(cEffect, 0);
        }
		public void EffectAdd(EffectVideo cEffect, ushort nIndent)
        {
            if (null == cEffect)
                throw new Exception("effect:add: общий массив объектов не инициализирован");
			lock (_aEffects)
			{
				if (this.stMergingMethod != cEffect.stMergingMethod)
					cEffect.stMergingMethod = this.stMergingMethod;

				cEffect.cDock.eCorner = Dock.Corner.unknown; // чтобы сами не позиционировались при препаре

                Effect cItemTMP;
                if (null != (cItemTMP = _aEffects.FirstOrDefault(o => o.nID == cEffect.nID)))   // один и тот же эффект можно сделать добавление, но нужно item-ы вводить, чтобы помнить разные Area, как в роле
                    throw new Exception("effect:add: this effect is already here [id=" + cEffect.nID + "][name=" + cEffect.sName + "]");
                if (cEffect.cMask!=null)    // т.к. композит разводит все эффекты, то смысла в масках нет. 
                    throw new Exception("effect:add: this effect is mask, but composite has not mask handler [id=" + cEffect.nID + "][name=" + cEffect.sName + "]");

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

                    if (stMergingMethod.eDeviceType == MergingDevice.DisCom)
                        PixelsMap.DisComInit();

                    foreach (Effect cEffect in _aEffects)
						if (EffectStatus.Idle == cEffect.eStatus)
						{
							((IVideo)cEffect).stMergingMethod = this.stMergingMethod;
							cEffect.Prepare();
						}

                    if (null != _cPMDuo && stArea != _cPMDuo.cFirst.stArea)
                    {
                        Baetylus.PixelsMapDispose(_cPMDuo, true);
                        _cPMDuo = null;
                    }
                    if (null == _cPMDuo)
                    {
                        _cPMDuo = new PixelsMap.Triple(this.stMergingMethod, this.stArea, PixelsMap.Format.ARGB32, true, Baetylus.PixelsMapDispose);
                        if (1 > _cPMDuo.cFirst.nLength)
                            (new Logger()).WriteNotice("1 > __cPixelsMap.nLength. composite.prepare");
                        _cPMDuo.Allocate();
                    }
                    _cPMDuo.RenewFirstTime();
                    nPixelsMapSyncIndex = byte.MaxValue;
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
            Baetylus.PixelsMapDispose(_cPMDuo, true);
            _cPMDuo = null;

            base.Stop();
        }
        
        override public PixelsMap FrameNext()   // если население композита тексты и они не менялись, то можно ничего не менять вообще! (оптимизация чата и подобных)  // сделал
        {
            _cPixelsMap = _cPMDuo.Switch(nPixelsMapSyncIndex);
            if (null == _cPixelsMap) return null;

            _bChanged = false;
			base.FrameNext();
			PixelsMap cPM = null;
            List<PixelsMap> aPMs = new List<PixelsMap>();
            Dictionary<Effect, PixelsMap> ahEffects_PMs = new Dictionary<Effect, PixelsMap>();
            IVideo iVideo = null;

            foreach (Effect cEffect in _aEffects)
            {
                if (null == cEffect || !(cEffect is IVideo) || (EffectStatus.Running != cEffect.eStatus))
                    continue;
                iVideo = (IVideo)cEffect;
                iVideo.nPixelsMapSyncIndex = nPixelsMapSyncIndex;
                cPM = iVideo.FrameNext();
                if (null == cPM)
                    continue;
                aPMs.Add(cPM);
                if (!_bChanged && (!(iVideo is Text) || ((Text)iVideo).dtChanged > _dtLastChange))
                    _bChanged = true;
            }

            if (_bChanged && 0 < aPMs.Count)   // когда == 0   - это пустышка и не надо для нее делать pixelsmap   //  || _bMergedChanges
            {
                //_bMergedChanges = _bChanged;
                _dtLastChange = DateTime.Now;
				_cPixelsMap.Merge(aPMs);
				Baetylus.PixelsMapDispose(aPMs.ToArray());
			}

            _cPixelsMap.nAlphaConstant = nCurrentOpacity;
            if (null != cMask)
                _cPixelsMap.eAlpha = cMask.eMaskType;

            _cPixelsMap.Move(stArea.nLeft, stArea.nTop);

            if (nFrameCurrent >= nDuration)
				base.Stop();
			return _cPixelsMap;
        }
    }
}
