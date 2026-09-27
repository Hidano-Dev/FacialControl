using System;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Windows.Routing
{
    /// <summary>
    /// ルーティングエディタ（別パッケージ <c>com.hidano.facialcontrol.routing-editor</c>）を
    /// core の Profile Inspector から開くための登録点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ルーティングエディタは core Editor の Logic 層（<see cref="Logic.RoutingGraphModelBuilder"/> 等）に
    /// 依存するため、core Editor 側から拡張の asmdef を参照すると循環参照になる。
    /// そこで拡張側がドメインリロード時に <see cref="OpenHandler"/> を登録し、
    /// Inspector は登録の有無で「ルーティングを編集」ボタンの表示を切り替える。
    /// </para>
    /// <para>
    /// テストでハンドラを差し替える場合は元の値を退避し、TearDown で必ず復元すること。
    /// </para>
    /// </remarks>
    public static class RoutingEditorLauncher
    {
        private const string HandlerMissingWarning =
            "[RoutingEditorLauncher] ルーティングエディタが登録されていません。" +
            "com.hidano.facialcontrol.routing-editor パッケージを追加してください。";

        /// <summary>
        /// プロファイル SO を受け取りルーティングエディタのウィンドウを開くハンドラ。
        /// 拡張パッケージ未導入時は null。
        /// </summary>
        public static Func<ScriptableObject, EditorWindow> OpenHandler { get; set; }

        /// <summary>ルーティングエディタが利用可能（ハンドラ登録済み）か。</summary>
        public static bool IsAvailable => OpenHandler != null;

        /// <summary>
        /// 登録済みハンドラでルーティングエディタを開く。
        /// 未登録なら警告を出して null を返す。
        /// </summary>
        public static EditorWindow Open(ScriptableObject profile)
        {
            Func<ScriptableObject, EditorWindow> handler = OpenHandler;
            if (handler == null)
            {
                Debug.LogWarning(HandlerMissingWarning);
                return null;
            }

            return handler(profile);
        }
    }
}
