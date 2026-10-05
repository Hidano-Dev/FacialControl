using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// binding が <c>{slug}:analog-expression</c> として構築する <see cref="AnalogExpressionInputSource"/> の
    /// binding 構成を、live 入力を起こさずに列挙できることを表す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Timeline の Edit プレビューは <c>OnStart</c>（InputAction の Enable やソケットの Open 等）を呼べないため、
    /// この宣言から Play と同じ <see cref="AnalogExpressionInputSource"/> をオフラインに組み、
    /// 各 <see cref="AnalogExpressionBinding.SourceId"/>（<c>{slug}:{SourceId}</c>）を Timeline の Analog トラックの値で駆動する。
    /// </para>
    /// <para>
    /// 返す binding は <c>OnStart</c> で消費者へ渡すものと同じ内容にする。<c>OnStart</c> が消費者を構築しない構成
    /// （必須の設定が欠けている等）では空を返す。呼び出しは Editor のメインスレッドからのみ。
    /// </para>
    /// </remarks>
    public interface IAnalogExpressionBindingDeclaration
    {
        /// <summary>
        /// この binding が <see cref="AnalogExpressionInputSource"/> へ渡す binding 列（構築しないなら空）。
        /// </summary>
        IReadOnlyList<AnalogExpressionBinding> GetAnalogExpressionBindings();
    }
}
