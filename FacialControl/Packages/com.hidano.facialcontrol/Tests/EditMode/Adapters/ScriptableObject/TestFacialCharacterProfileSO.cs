using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings
{
    /// <summary>
    /// Test 専用の concrete <see cref="FacialCharacterProfileSO"/>。
    /// protected <c>_adapterBindings</c> field に test 側から直接書き込めるよう
    /// <see cref="WritableAdapterBindings"/> を公開し、protected <c>_baseExpression</c> field を
    /// 直接差し替える <see cref="OverrideBaseExpressionField"/> を提供する（reflection 不要化）。
    /// ScriptableObject の MonoScript 解決のため独立ファイルかつ public sealed として配置する。
    /// </summary>
    public sealed class TestFacialCharacterProfileSO : FacialCharacterProfileSO
    {
        public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

        /// <summary>
        /// シリアライズ済み <c>_baseExpression</c> field を直接差し替える（null 化を含む）。
        /// public getter の遅延生成契約を検証するためのテスト専用フック。
        /// </summary>
        public void OverrideBaseExpressionField(BaseExpressionSerializable value)
        {
            _baseExpression = value;
        }
    }
}
