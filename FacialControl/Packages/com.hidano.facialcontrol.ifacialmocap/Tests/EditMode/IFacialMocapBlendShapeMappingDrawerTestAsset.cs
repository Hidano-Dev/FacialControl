using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using UnityEngine;

namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    /// <summary><see cref="IFacialMocapBlendShapeMappingDrawerTests"/> 用の in-memory ホスト。</summary>
    public sealed class IFacialMocapBlendShapeMappingDrawerTestAsset : ScriptableObject
    {
        public List<IFacialMocapBlendShapeMapping> Mappings = new List<IFacialMocapBlendShapeMapping>();
    }
}
