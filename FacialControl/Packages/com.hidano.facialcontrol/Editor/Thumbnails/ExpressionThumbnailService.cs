using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// Expression 行のサムネイル（<see cref="ExpressionThumbnailView"/>）に画像を供給する。
    /// <para>
    /// Inspector を開くたびに全 Expression を同期描画しないよう、次の順で解決する:
    /// 1. メモリキャッシュ（同じキーの画像があれば即表示）
    /// 2. ディスクキャッシュ（<see cref="ExpressionThumbnailDiskCache"/>。1 回の <see cref="Pump"/> で最大
    ///    <see cref="MaxDiskLoadsPerPump"/> 件）
    /// 3. 描画（<see cref="IExpressionThumbnailRenderer"/>。1 回の <see cref="Pump"/> で 1 件）
    /// 2 と 3 は <see cref="EditorApplication.update"/> から少しずつ進める（遅延生成）。
    /// </para>
    /// <para>
    /// キャッシュキーは参照モデルと Expression の中身から決まる（<see cref="ExpressionThumbnailCacheKey"/>）。
    /// AnimationClip の中身の変更は <see cref="CheckForClipChanges"/> が dirty count と依存ハッシュで検知し、
    /// キーが変わったサムネイルを作り直す。
    /// </para>
    /// <para>
    /// メモリ上のテクスチャと描画用の一時オブジェクトは <see cref="Dispose"/>（Inspector の OnDisable、
    /// ドメインリロード直前）ですべて破棄する。
    /// </para>
    /// </summary>
    public sealed class ExpressionThumbnailService : IDisposable
    {
        /// <summary>キャプチャ解像度（px）。ディスクキャッシュと拡大表示はこの解像度。</summary>
        public const int CaptureResolution = 512;

        /// <summary>
        /// Inspector 行に表示するテクスチャの解像度（px）。表示サイズ 128px の HiDPI（2x）に合わせる。
        /// 512px のまま保持すると 1 枚 1MB になり、Expression 数に比例してメモリを使うため縮小して持つ。
        /// </summary>
        public const int DisplayResolution = 256;

        /// <summary>1 回の <see cref="Pump"/> で読み込むディスクキャッシュの上限件数。</summary>
        public const int MaxDiskLoadsPerPump = 8;

        /// <summary>AnimationClip の変更を確認する間隔（秒）。</summary>
        private const double ChangePollIntervalSeconds = 0.5;

        /// <summary>どの行からも参照されなくなったテクスチャを、この件数を超えたら破棄する。</summary>
        private const int SpareCachedTextureCount = 32;

        private readonly IExpressionThumbnailRenderer _renderer;
        private readonly ExpressionThumbnailDiskCache _diskCache;
        private readonly IExpressionAnimationClipSampler _sampler;
        private readonly bool _autoPump;

        private readonly Dictionary<string, Texture2D> _displayTextures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<AnimationClip, SampledClip> _sampledClips =
            new Dictionary<AnimationClip, SampledClip>();
        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly List<Binding> _queue = new List<Binding>();
        private readonly HashSet<string> _referencedKeysBuffer = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _keysBuffer = new List<string>();

        private double _lastPollTime;
        private bool _disposed;

        private sealed class Binding
        {
            public ExpressionThumbnailView View;
            public GameObject Model;
            public AnimationClip Clip;
            public string Key;
            public bool UseDiskCache;
            public ExpressionSnapshot Snapshot;
            public string ClipIdentity;
            public int ClipDirtyCount;
        }

        private struct SampledClip
        {
            public string Identity;
            public int DirtyCount;
            public ExpressionSnapshot Snapshot;
        }

        /// <param name="renderer">描画の実装。所有権を受け取り、<see cref="Dispose"/> で破棄する</param>
        /// <param name="diskCache">ディスクキャッシュ。null ならディスクキャッシュを使わない</param>
        /// <param name="sampler">AnimationClip → snapshot のサンプラ</param>
        /// <param name="autoPump">
        /// true なら <see cref="EditorApplication.update"/> から <see cref="Pump"/> / <see cref="CheckForClipChanges"/>
        /// を自動で呼び、ドメインリロード直前に <see cref="Dispose"/> する。テストでは false にして手動で進める
        /// </param>
        public ExpressionThumbnailService(
            IExpressionThumbnailRenderer renderer,
            ExpressionThumbnailDiskCache diskCache,
            IExpressionAnimationClipSampler sampler,
            bool autoPump)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            _diskCache = diskCache;
            _autoPump = autoPump;

            if (_autoPump)
            {
                EditorApplication.update += OnEditorUpdate;
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            }
        }

        /// <summary>
        /// 既定構成（<see cref="ExpressionThumbnailRenderer"/> + Library 配下のディスクキャッシュ + 自動 Pump）で生成する。
        /// </summary>
        public static ExpressionThumbnailService CreateDefault(IExpressionAnimationClipSampler sampler)
        {
            return new ExpressionThumbnailService(
                new ExpressionThumbnailRenderer(),
                new ExpressionThumbnailDiskCache(ExpressionThumbnailDiskCache.DefaultDirectory),
                sampler,
                autoPump: true);
        }

        /// <summary>生成待ち（ディスク読み込み・描画待ち）の件数。</summary>
        public int PendingCount => _queue.Count;

        /// <summary>メモリに保持しているサムネイルテクスチャの件数。</summary>
        public int CachedTextureCount => _displayTextures.Count;

        public bool IsDisposed => _disposed;

        /// <summary>
        /// <paramref name="view"/> に <paramref name="referenceModel"/> + <paramref name="clip"/> のサムネイルを表示する。
        /// 同じ view を再度渡すと割り当てを置き換える。メモリキャッシュにあれば即表示し、
        /// 無ければ「生成中」を表示して生成待ちに積む。
        /// </summary>
        public void Bind(ExpressionThumbnailView view, GameObject referenceModel, AnimationClip clip)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (_disposed) return;

            var binding = FindBinding(view);
            if (binding == null)
            {
                binding = new Binding { View = view };
                _bindings.Add(binding);
                view.EnlargeRequested += OnEnlargeRequested;
            }

            binding.Model = referenceModel;
            binding.Clip = clip;
            Resolve(binding);
        }

        /// <summary>
        /// すべての割り当ての参照モデルを差し替える（参照モデルが変わったとき）。
        /// </summary>
        public void RebindReferenceModel(GameObject referenceModel)
        {
            if (_disposed) return;

            for (int i = 0; i < _bindings.Count; i++)
            {
                _bindings[i].Model = referenceModel;
                Resolve(_bindings[i]);
            }
        }

        /// <summary>
        /// すべての割り当てを解除する（Expression 一覧を作り直す前に呼ぶ）。テクスチャは再利用のため残す。
        /// </summary>
        public void ClearBindings()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                _bindings[i].View.EnlargeRequested -= OnEnlargeRequested;
            }
            _bindings.Clear();
            _queue.Clear();
        }

        /// <summary>
        /// 表示中のサムネイルをキャッシュ（メモリ・ディスク）から消して作り直す。
        /// </summary>
        public void RegenerateAll()
        {
            if (_disposed) return;

            _sampledClips.Clear();
            for (int i = 0; i < _bindings.Count; i++)
            {
                var key = _bindings[i].Key;
                if (key == null) continue;

                _diskCache?.Delete(key);
                DestroyDisplayTexture(key);
            }

            for (int i = 0; i < _bindings.Count; i++)
            {
                Resolve(_bindings[i]);
            }
        }

        /// <summary>
        /// 割り当て中の AnimationClip / 参照モデルが変更・破棄されていないかを確認し、
        /// キーが変わったサムネイルを作り直す。
        /// </summary>
        public void CheckForClipChanges()
        {
            if (_disposed) return;

            for (int i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];

                // 破棄された参照（Unity の null）は割り当て時と状態が変わっているので解決し直す。
                bool modelDestroyed = !ReferenceEquals(binding.Model, null) && binding.Model == null;
                bool clipDestroyed = !ReferenceEquals(binding.Clip, null) && binding.Clip == null;
                if (modelDestroyed || clipDestroyed)
                {
                    if (modelDestroyed) binding.Model = null;
                    if (clipDestroyed) binding.Clip = null;
                    Resolve(binding);
                    continue;
                }

                if (binding.Clip == null || binding.Model == null || binding.Key == null) continue;

                int dirtyCount = EditorUtility.GetDirtyCount(binding.Clip);
                if (dirtyCount == binding.ClipDirtyCount
                    && string.Equals(GetObjectIdentity(binding.Clip, out _), binding.ClipIdentity, StringComparison.Ordinal))
                {
                    continue;
                }

                Resolve(binding);
            }
        }

        /// <summary>
        /// 生成待ちを進める。ディスクキャッシュを最大 <see cref="MaxDiskLoadsPerPump"/> 件読むか、
        /// 描画を 1 件行ったところで戻る。
        /// </summary>
        public void Pump()
        {
            if (_disposed) return;

            int diskLoads = 0;
            while (_queue.Count > 0)
            {
                var binding = _queue[0];

                // 別の行が同じキーの画像を先に用意していればそれを使う。
                if (TryShowFromMemory(binding))
                {
                    _queue.RemoveAt(0);
                    continue;
                }

                if (binding.UseDiskCache)
                {
                    if (diskLoads >= MaxDiskLoadsPerPump) break;
                    diskLoads++;

                    if (_diskCache.TryLoad(binding.Key, out var pngBytes))
                    {
                        var display = CreateDisplayTextureFromPng(pngBytes);
                        if (display != null)
                        {
                            _queue.RemoveAt(0);
                            StoreAndShow(binding.Key, display);
                            continue;
                        }
                    }
                }

                _queue.RemoveAt(0);
                RenderAndShow(binding);
                break;
            }

            if (_queue.Count == 0)
            {
                PruneUnreferencedTextures();
            }
        }

        /// <summary>
        /// 拡大表示用の画像を返す。ディスクキャッシュがあれば <see cref="CaptureResolution"/> の画像を読み込んで返し
        /// （<paramref name="owned"/> = true。呼び出し側で破棄する）、無ければ表示用テクスチャを返す（owned = false）。
        /// </summary>
        public Texture2D LoadFullSize(string key, out bool owned)
        {
            owned = false;
            if (_disposed || key == null) return null;

            if (_diskCache != null && _diskCache.TryLoad(key, out var pngBytes))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (texture.LoadImage(pngBytes))
                {
                    owned = true;
                    return texture;
                }
                UnityEngine.Object.DestroyImmediate(texture);
            }

            return _displayTextures.TryGetValue(key, out var display) ? display : null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_autoPump)
            {
                EditorApplication.update -= OnEditorUpdate;
                AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            }

            for (int i = 0; i < _bindings.Count; i++)
            {
                _bindings[i].View.EnlargeRequested -= OnEnlargeRequested;
                if (_bindings[i].Key != null)
                {
                    _bindings[i].View.ShowStatus(ExpressionThumbnailView.PendingMessage);
                }
            }
            _bindings.Clear();
            _queue.Clear();
            _sampledClips.Clear();

            foreach (var texture in _displayTextures.Values)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
            _displayTextures.Clear();

            _renderer.Dispose();
        }

        // ====================================================================
        // 内部処理
        // ====================================================================

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastPollTime >= ChangePollIntervalSeconds)
            {
                _lastPollTime = now;
                CheckForClipChanges();
            }

            if (_queue.Count > 0)
            {
                Pump();
            }
        }

        private Binding FindBinding(ExpressionThumbnailView view)
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (ReferenceEquals(_bindings[i].View, view))
                    return _bindings[i];
            }
            return null;
        }

        private void Resolve(Binding binding)
        {
            _queue.Remove(binding);
            binding.Key = null;
            binding.ClipIdentity = null;

            if (binding.Model == null)
            {
                binding.View.ShowStatus(ExpressionThumbnailView.NoReferenceModelMessage);
                return;
            }

            if (binding.Clip == null)
            {
                binding.View.ShowStatus(ExpressionThumbnailView.NoClipMessage);
                return;
            }

            if (!TrySample(binding.Clip, out var sampled))
            {
                binding.View.ShowStatus(ExpressionThumbnailView.FailedMessage);
                return;
            }

            var modelIdentity = GetObjectIdentity(binding.Model, out bool modelPersistent);
            bool clipPersistent = !sampled.Identity.StartsWith(InstanceIdentityPrefix, StringComparison.Ordinal);

            binding.Snapshot = sampled.Snapshot;
            binding.ClipIdentity = sampled.Identity;
            binding.ClipDirtyCount = sampled.DirtyCount;
            // 永続化されていない（シーン上・メモリ上の）オブジェクトの InstanceID はセッションをまたいで
            // 別オブジェクトに再利用されうるため、ディスクキャッシュのキーに使わない。
            binding.UseDiskCache = _diskCache != null && modelPersistent && clipPersistent;
            binding.Key = ExpressionThumbnailCacheKey.Compute(
                modelIdentity, sampled.Identity, sampled.Snapshot, CaptureResolution);

            if (TryShowFromMemory(binding)) return;

            binding.View.ShowStatus(ExpressionThumbnailView.PendingMessage);
            _queue.Add(binding);
        }

        private bool TryShowFromMemory(Binding binding)
        {
            if (binding.Key != null
                && _displayTextures.TryGetValue(binding.Key, out var texture)
                && texture != null)
            {
                binding.View.ShowTexture(texture, binding.Key);
                return true;
            }
            return false;
        }

        private bool TrySample(AnimationClip clip, out SampledClip sampled)
        {
            var identity = GetObjectIdentity(clip, out _);
            int dirtyCount = EditorUtility.GetDirtyCount(clip);

            if (_sampledClips.TryGetValue(clip, out sampled)
                && sampled.DirtyCount == dirtyCount
                && string.Equals(sampled.Identity, identity, StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                var snapshot = _sampler.SampleSnapshot(clip.name, clip);
                sampled = new SampledClip { Identity = identity, DirtyCount = dirtyCount, Snapshot = snapshot };
                _sampledClips[clip] = sampled;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExpressionThumbnail] AnimationClip '{clip.name}' をサンプリングできませんでした: {e.Message}");
                _sampledClips.Remove(clip);
                sampled = default;
                return false;
            }
        }

        private void RenderAndShow(Binding binding)
        {
            Texture2D full = null;
            try
            {
                full = _renderer.Render(binding.Model, binding.Clip, binding.Snapshot, CaptureResolution);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExpressionThumbnail] サムネイルを描画できませんでした: {e.Message}");
            }

            if (full == null)
            {
                binding.View.ShowStatus(ExpressionThumbnailView.FailedMessage);
                return;
            }

            try
            {
                if (binding.UseDiskCache)
                {
                    _diskCache.Save(binding.Key, full.EncodeToPNG());
                }

                StoreAndShow(binding.Key, Downscale(full, DisplayResolution));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(full);
            }
        }

        /// <summary>
        /// テクスチャをメモリキャッシュに入れ、同じキーの行すべてに表示する（生成待ちからも外す）。
        /// </summary>
        private void StoreAndShow(string key, Texture2D display)
        {
            if (_displayTextures.TryGetValue(key, out var previous) && previous != null && previous != display)
            {
                UnityEngine.Object.DestroyImmediate(previous);
            }
            _displayTextures[key] = display;

            for (int i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                if (!string.Equals(binding.Key, key, StringComparison.Ordinal)) continue;

                _queue.Remove(binding);
                binding.View.ShowTexture(display, key);
            }
        }

        private void DestroyDisplayTexture(string key)
        {
            if (_displayTextures.TryGetValue(key, out var texture))
            {
                _displayTextures.Remove(key);
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private void PruneUnreferencedTextures()
        {
            _referencedKeysBuffer.Clear();
            for (int i = 0; i < _bindings.Count; i++)
            {
                if (_bindings[i].Key != null)
                    _referencedKeysBuffer.Add(_bindings[i].Key);
            }

            if (_displayTextures.Count <= _referencedKeysBuffer.Count + SpareCachedTextureCount) return;

            _keysBuffer.Clear();
            foreach (var key in _displayTextures.Keys)
            {
                if (!_referencedKeysBuffer.Contains(key))
                    _keysBuffer.Add(key);
            }

            for (int i = 0; i < _keysBuffer.Count; i++)
            {
                DestroyDisplayTexture(_keysBuffer[i]);
            }
            _keysBuffer.Clear();
        }

        private void OnEnlargeRequested(ExpressionThumbnailView view, string key)
        {
            if (_disposed) return;

            var texture = LoadFullSize(key, out bool owned);
            if (texture == null) return;

            var binding = FindBinding(view);
            var title = binding != null && binding.Clip != null ? binding.Clip.name : null;
            UnityEditor.PopupWindow.Show(view.worldBound, new ExpressionThumbnailPopup(texture, owned, title));
        }

        private const string InstanceIdentityPrefix = "instance:";

        /// <summary>
        /// キャッシュキー用のオブジェクト識別子を返す。アセットなら GUID + ローカル ID + 依存ハッシュ
        /// （再インポート・保存で変わる）、アセットでなければ InstanceID。
        /// </summary>
        private static string GetObjectIdentity(UnityEngine.Object obj, out bool persistent)
        {
            if (obj != null
                && EditorUtility.IsPersistent(obj)
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
            {
                persistent = true;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return guid + ":" + localId + ":" + AssetDatabase.GetAssetDependencyHash(path);
            }

            persistent = false;
            return InstanceIdentityPrefix + (obj != null ? obj.GetInstanceID() : 0);
        }

        private static Texture2D CreateDisplayTextureFromPng(byte[] pngBytes)
        {
            var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!full.LoadImage(pngBytes)) return null;
                return Downscale(full, DisplayResolution);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(full);
            }
        }

        /// <summary>
        /// 長辺が <paramref name="maxSize"/> 以下になるよう整数倍で縮小する（ボックスフィルタ）。
        /// GPU を使わないので、グラフィックスデバイスの無い batchmode でも動く。
        /// 返すテクスチャは CPU 側のコピーを持たない（メモリ節約）。
        /// </summary>
        public static Texture2D Downscale(Texture2D source, int maxSize)
        {
            int factor = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(source.width, source.height) / (float)maxSize));
            int width = Mathf.Max(1, source.width / factor);
            int height = Mathf.Max(1, source.height / factor);

            var sourcePixels = source.GetPixels32();
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    int sy0 = y * factor;
                    int sx0 = x * factor;
                    for (int sy = sy0; sy < sy0 + factor && sy < source.height; sy++)
                    {
                        int row = sy * source.width;
                        for (int sx = sx0; sx < sx0 + factor && sx < source.width; sx++)
                        {
                            var c = sourcePixels[row + sx];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            a += c.a;
                            n++;
                        }
                    }
                    pixels[y * width + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            result.SetPixels32(pixels);
            result.Apply(false, true);
            return result;
        }
    }
}
