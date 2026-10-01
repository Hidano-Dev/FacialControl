using UnityEngine;

namespace Hidano.FacialControl.Adapters.AdapterBindings
{
    /// <summary>
    /// OSC binding が内部で生成する設定 SO（既定値用・旧設定の写し）の生成と破棄をまとめる。
    /// </summary>
    internal static class OscRuntimeSettingsInstances
    {
        /// <summary>保存されない一時インスタンスとして印を付ける。</summary>
        public static T MarkTransient<T>(T instance) where T : Object
        {
            if (instance != null)
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
            }
            return instance;
        }

        /// <summary>
        /// 生成済みの一時インスタンスを破棄する。HideAndDontSave は自動で回収されないため、
        /// binding の Dispose や作り直しの際に明示的に破棄する。
        /// </summary>
        public static void Destroy<T>(ref T instance) where T : Object
        {
            if (instance == null)
            {
                instance = null;
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                Object.Destroy(instance);
            }
            else
            {
                Object.DestroyImmediate(instance);
            }
            instance = null;
        }
    }
}
