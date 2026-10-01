using System;

namespace Hidano.FacialControl.Adapters.RuntimeSettings
{
    /// <summary>
    /// <see cref="AdapterRuntimeSettingsBase"/> 派生型を Collection Inspector の Add 一覧から外す。
    /// </summary>
    /// <remarks>
    /// 既存アセットの読み込みと移行のためだけに残している旧型に付ける。
    /// 型そのものは残るため、既存の sub-asset は引き続き読み込める。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class HideInAdapterRuntimeSettingsMenuAttribute : Attribute
    {
    }
}
