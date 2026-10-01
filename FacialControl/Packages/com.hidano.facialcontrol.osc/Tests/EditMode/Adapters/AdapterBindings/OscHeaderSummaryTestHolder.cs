using Hidano.FacialControl.Domain.Adapters;
using UnityEngine;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// drawer の <c>GetHeaderSummary</c> に実際の SerializedProperty を渡すための入れ物
    /// （<see cref="OscAdapterBindingDrawerHeaderSummaryTests"/> 用）。
    /// </summary>
    public sealed class OscHeaderSummaryTestHolder : ScriptableObject
    {
        [SerializeReference]
        public AdapterBindingBase Binding;
    }
}
