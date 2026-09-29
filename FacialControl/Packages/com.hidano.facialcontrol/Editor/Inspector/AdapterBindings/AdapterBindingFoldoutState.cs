using UnityEditor;
using UnityEngine.Serialization;

namespace Hidano.FacialControl.Editor.Inspector.AdapterBindings
{
    /// <summary>
    /// <see cref="AdapterBindingsListView"/> の各 Adapter 行 Foldout の開閉状態を
    /// SessionState に保存・復元するユーティリティ。
    /// </summary>
    /// <remarks>
    /// キーは targetObject の InstanceID と要素の <c>[SerializeReference]</c> 参照 ID
    /// （<see cref="SerializedProperty.managedReferenceId"/>）で組み立てる。参照 ID は要素に
    /// 付いて回るため、Adapter の追加・削除・並べ替えで index がずれても開閉状態は別の要素へ
    /// 移らない。domain reload / Inspector 再構築をまたいで保持し、Editor 再起動でリセットする。
    /// null 要素（型が解決できない binding）は参照 ID を持たないため保存しない。
    /// </remarks>
    public static class AdapterBindingFoldoutState
    {
        private const string KeyPrefix = "Hidano.FacialControl.AdapterBindingFoldout";

        /// <summary>
        /// Adapter Binding 要素の SerializedProperty から SessionState 保存キーを組み立てる。
        /// 要素が null・参照 ID 未確定の場合は null を返す（= 保存しない）。
        /// </summary>
        public static string GetSessionStateKey(SerializedProperty elementProperty)
        {
            if (elementProperty == null)
            {
                return null;
            }

            var target = elementProperty.serializedObject.targetObject;
            if (target == null)
            {
                return null;
            }

            long referenceId = elementProperty.managedReferenceId;
            if (referenceId == ManagedReferenceUtility.RefIdNull
                || referenceId == ManagedReferenceUtility.RefIdUnknown)
            {
                return null;
            }

            return $"{KeyPrefix}.{target.GetInstanceID()}.{referenceId}";
        }

        /// <summary>
        /// 保存済みの開閉状態を返す。キーが null・未保存なら <paramref name="defaultOpen"/> を返す。
        /// </summary>
        public static bool Load(string sessionStateKey, bool defaultOpen = true)
        {
            if (string.IsNullOrEmpty(sessionStateKey))
            {
                return defaultOpen;
            }

            return SessionState.GetBool(sessionStateKey, defaultOpen);
        }

        /// <summary>
        /// 開閉状態を保存する。キーが null の場合は何もしない。
        /// </summary>
        public static void Save(string sessionStateKey, bool open)
        {
            if (string.IsNullOrEmpty(sessionStateKey))
            {
                return;
            }

            SessionState.SetBool(sessionStateKey, open);
        }

        /// <summary>
        /// 保存済みの開閉状態を消す。キーが null の場合は何もしない。
        /// </summary>
        public static void Erase(string sessionStateKey)
        {
            if (string.IsNullOrEmpty(sessionStateKey))
            {
                return;
            }

            SessionState.EraseBool(sessionStateKey);
        }
    }
}
