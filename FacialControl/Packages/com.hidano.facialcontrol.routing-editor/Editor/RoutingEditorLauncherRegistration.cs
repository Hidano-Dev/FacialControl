using Hidano.FacialControl.Editor.Windows.Routing;
using UnityEditor;

namespace Hidano.FacialControl.RoutingEditor
{
    /// <summary>
    /// ドメインリロード時に core の <see cref="RoutingEditorLauncher"/> へ
    /// <see cref="RoutingEditorWindow.Open"/> を登録し、Profile Inspector の
    /// 「ルーティングを編集」ボタンから本パッケージのウィンドウが開くようにする。
    /// </summary>
    /// <remarks>
    /// core Editor は本パッケージを参照できない（本パッケージが core Editor の Logic 層に依存するため
    /// asmdef 参照が循環する）。そのため配線はこちら側からの登録で行う。
    /// </remarks>
    [InitializeOnLoad]
    internal static class RoutingEditorLauncherRegistration
    {
        static RoutingEditorLauncherRegistration()
        {
            RoutingEditorLauncher.OpenHandler = RoutingEditorWindow.Open;
        }
    }
}
