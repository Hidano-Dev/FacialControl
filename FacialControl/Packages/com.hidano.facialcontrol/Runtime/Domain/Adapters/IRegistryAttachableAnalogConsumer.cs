using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// 構築時に直接参照で解決した analog source を、registry の Register / Replace / Unregister 通知に
    /// 追従して差し替える Analog 消費者の公開契約。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 消費者が「どの source id を読むか」は構築時の binding 定義のまま変わらない。本契約は
    /// 「その id の registry エントリが差し替わったら読む先を変える」だけで、binding の追加・削除・値変換は行わない。
    /// Timeline / REC の <see cref="IInputSourceRegistry.Replace(AdapterSlug, string, Hidano.FacialControl.Domain.Interfaces.IInputSource)"/>
    /// による乗っ取りが実際の BlendShape 出力に届くための口である。
    /// </para>
    /// <para>
    /// 差し替えは通知時（セッション開始 / 終了）のみ発生し、毎フレームの読み出し経路に分岐や確保を増やさない。
    /// <see cref="IInputSourceRegistry.Subscribe"/> に Unsubscribe は無いため、<see cref="DetachRegistry"/> は
    /// 登録済み handler を世代番号で no-op 化する。
    /// </para>
    /// </remarks>
    public interface IRegistryAttachableAnalogConsumer
    {
        /// <summary>
        /// 構築時に解決した binding ごとに <c>{slug}:{SourceId}</c> を <paramref name="registry"/> に購読する。
        /// 通知された source が <see cref="Hidano.FacialControl.Domain.Interfaces.IAnalogInputSource"/> なら読む先をそれに差し替え、
        /// <c>null</c>（Unregister）なら構築時の source に戻す。Analog でない非 null は無視する（警告なし）。
        /// 同じ registry への 2 回目以降は no-op。別の registry を渡された場合は <see cref="DetachRegistry"/> 相当を行ってから購読し直す。
        /// </summary>
        /// <param name="registry">購読先 registry。<c>null</c> は <see cref="System.ArgumentNullException"/>。</param>
        /// <param name="slug">消費者を構築した binding の slug。composite key の prefix に使う。</param>
        void AttachRegistry(IInputSourceRegistry registry, AdapterSlug slug);

        /// <summary>
        /// 全 binding の読む先を構築時の source に戻し、以後の通知を無視する。
        /// <see cref="IsRegistryAttached"/> は false になる。未接続時は no-op。
        /// </summary>
        void DetachRegistry();

        /// <summary>registry に接続中か。</summary>
        bool IsRegistryAttached { get; }
    }
}
