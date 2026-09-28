namespace Hidano.FacialControl.Testing
{
    /// <summary>
    /// インメモリの Fake であることを示すマーカー。
    /// Small テストで <see cref="TestDependencyRegistry"/> に登録できる依存はこのインターフェースを実装したものに限る
    /// （時刻・保存・通信など、Small で禁止された API に触れる本番実装の注入を SetUp 時点で検出するため）。
    /// </summary>
    public interface IFakeDependency
    {
    }
}
