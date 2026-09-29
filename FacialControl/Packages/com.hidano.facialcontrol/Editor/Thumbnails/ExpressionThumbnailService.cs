using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
    /// キャッシュキー（<see cref="ExpressionThumbnailCacheKey"/>）は次から決まる:
    /// <list type="bullet">
    /// <item>参照モデル: GUID + ローカル ID + 依存アセット（FBX・マテリアル・テクスチャ等）すべての依存ハッシュ。
    /// 再インポートやマテリアルの保存で変わる</item>
    /// <item>AnimationClip: GUID + ローカル ID + 中身（全カーブの時刻 0 の値と参照オブジェクト）。
    /// 中身を変えずに保存しただけでは変わらない</item>
    /// </list>
    /// 変更の検知は 2 系統で行う。AnimationClip のメモリ上の編集は <see cref="CheckForClipChanges"/> が
    /// dirty count で拾い（AssetDatabase を引かない軽い確認。編集中の連続変更は落ち着くまで待つ）、
    /// ディスク上の変更（保存・再インポート・外部変更）は <see cref="NotifyAssetsImported"/> が拾う。
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

        /// <summary>
        /// ディスクキャッシュに残すファイル数の上限。ドメインロード後に最初の <see cref="CreateDefault"/> で
        /// 超過分を古い順に消す。
        /// </summary>
        public const int MaxDiskCacheFiles = 500;

        /// <summary>AnimationClip の変更を確認する間隔（秒）。</summary>
        private const double ChangePollIntervalSeconds = 0.5;

        /// <summary>どの行からも参照されなくなったテクスチャを、この件数を超えたら破棄する。</summary>
        private const int SpareCachedTextureCount = 32;

        private const string InstanceIdentityPrefix = "instance:";

        private static bool s_diskCachePruned;

        private readonly IExpressionThumbnailRenderer _renderer;
        private readonly ExpressionThumbnailDiskCache _diskCache;
        private readonly IExpressionAnimationClipSampler _sampler;
        private readonly bool _autoPump;

        private readonly Dictionary<string, Texture2D> _displayTextures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<AnimationClip, SampledClip> _sampledClips =
            new Dictionary<AnimationClip, SampledClip>();
        private readonly Dictionary<UnityEngine.Object, ObjectIdentity> _identities =
            new Dictionary<UnityEngine.Object, ObjectIdentity>();
        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly List<Binding> _queue = new List<Binding>();
        private readonly HashSet<string> _referencedKeysBuffer = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<UnityEngine.Object> _referencedObjectsBuffer = new HashSet<UnityEngine.Object>();
        private readonly List<string> _keysBuffer = new List<string>();
        private readonly List<UnityEngine.Object> _objectsBuffer = new List<UnityEngine.Object>();

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
            public int ClipDirtyCount;

            // 編集中の連続変更で毎回描画しないよう、変化後の dirty count が次の確認でも同じ（= 編集が
            // 落ち着いた）ときにだけ作り直す。
            public bool HasPendingChange;
            public int PendingDirtyCount;
        }

        private struct SampledClip
        {
            public int DirtyCount;
            public ExpressionSnapshot Snapshot;
            public string ContentSignature;
        }

        private sealed class ObjectIdentity
        {
            public string Value;
            public bool Persistent;

            /// <summary>この識別子が依存するアセットのパス。これらがインポートされたら識別子を作り直す。</summary>
            public HashSet<string> DependencyPaths;
        }

        /// <param name="renderer">描画の実装。所有権を受け取り、<see cref="Dispose"/> で破棄する</param>
        /// <param name="diskCache">ディスクキャッシュ。null ならディスクキャッシュを使わない</param>
        /// <param name="sampler">AnimationClip → snapshot のサンプラ</param>
        /// <param name="autoPump">
        /// true なら <see cref="EditorApplication.update"/> から <see cref="Pump"/> / <see cref="CheckForClipChanges"/>
        /// を、アセットのインポート時に <see cref="NotifyAssetsImported"/> を自動で呼び、ドメインリロード直前に
        /// <see cref="Dispose"/> する。テストでは false にして手動で進める
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
                ExpressionThumbnailAssetWatcher.AssetsImported += NotifyAssetsImported;
            }
        }

        /// <summary>
        /// 既定構成（<see cref="ExpressionThumbnailRenderer"/> + Library 配下のディスクキャッシュ + 自動 Pump）で生成する。
        /// </summary>
        public static ExpressionThumbnailService CreateDefault(IExpressionAnimationClipSampler sampler)
        {
            var diskCache = new ExpressionThumbnailDiskCache(ExpressionThumbnailDiskCache.DefaultDirectory);
            if (!s_diskCachePruned)
            {
                s_diskCachePruned = true;
                diskCache.Prune(MaxDiskCacheFiles);
            }

            return new ExpressionThumbnailService(
                new ExpressionThumbnailRenderer(),
                diskCache,
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
            _identities.Clear();
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
        /// 割り当て中の AnimationClip がメモリ上で編集された・参照が破棄されたかを確認し、作り直す。
        /// AssetDatabase は引かず dirty count だけを見る。dirty count が変わった直後は作り直さず、
        /// 次の確認でも同じ値（編集が落ち着いた）なら作り直す。
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
                if (dirtyCount == binding.ClipDirtyCount)
                {
                    binding.HasPendingChange = false;
                    continue;
                }

                if (!binding.HasPendingChange || binding.PendingDirtyCount != dirtyCount)
                {
                    binding.HasPendingChange = true;
                    binding.PendingDirtyCount = dirtyCount;
                    continue;
                }

                Resolve(binding);
            }
        }

        /// <summary>
        /// アセットがインポートされた（保存・再インポート・外部変更）ことを知らせる。
        /// 参照モデルの依存アセット、または AnimationClip のアセットが含まれていれば、識別子と
        /// サンプリング結果を作り直して該当行を解決し直す。
        /// </summary>
        public void NotifyAssetsImported(string[] assetPaths)
        {
            if (_disposed || assetPaths == null || assetPaths.Length == 0) return;

            _objectsBuffer.Clear();
            foreach (var pair in _identities)
            {
                var dependencies = pair.Value.DependencyPaths;
                if (dependencies == null) continue;

                for (int i = 0; i < assetPaths.Length; i++)
                {
                    if (dependencies.Contains(assetPaths[i]))
                    {
                        _objectsBuffer.Add(pair.Key);
                        break;
                    }
                }
            }

            if (_objectsBuffer.Count == 0) return;

            for (int i = 0; i < _objectsBuffer.Count; i++)
            {
                var obj = _objectsBuffer[i];
                _identities.Remove(obj);
                if (obj is AnimationClip clip)
                {
                    _sampledClips.Remove(clip);
                }
            }

            for (int i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                if (ContainsReference(_objectsBuffer, binding.Model) || ContainsReference(_objectsBuffer, binding.Clip))
                {
                    Resolve(binding);
                }
            }
            _objectsBuffer.Clear();
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
                PruneUnreferencedCaches();
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
                ExpressionThumbnailAssetWatcher.AssetsImported -= NotifyAssetsImported;
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
            _identities.Clear();

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
            binding.HasPendingChange = false;

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

            var modelIdentity = GetIdentity(binding.Model, includeDependencyHashes: true);
            var clipIdentity = GetIdentity(binding.Clip, includeDependencyHashes: false);

            binding.Snapshot = sampled.Snapshot;
            binding.ClipDirtyCount = sampled.DirtyCount;
            // 永続化されていない（シーン上・メモリ上の）オブジェクトの InstanceID はセッションをまたいで
            // 別オブジェクトに再利用されうるため、ディスクキャッシュのキーに使わない。
            binding.UseDiskCache = _diskCache != null && modelIdentity.Persistent && clipIdentity.Persistent;
            binding.Key = ExpressionThumbnailCacheKey.Compute(
                modelIdentity.Value,
                clipIdentity.Value + "|" + sampled.ContentSignature,
                sampled.Snapshot,
                CaptureResolution);

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
            int dirtyCount = EditorUtility.GetDirtyCount(clip);
            if (_sampledClips.TryGetValue(clip, out sampled) && sampled.DirtyCount == dirtyCount)
            {
                return true;
            }

            try
            {
                sampled = new SampledClip
                {
                    DirtyCount = dirtyCount,
                    Snapshot = _sampler.SampleSnapshot(clip.name, clip),
                    ContentSignature = ComputeClipContentSignature(clip),
                };
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

        /// <summary>
        /// どの行からも参照されなくなったテクスチャ（一定数を超えた分）と、サンプリング結果・識別子を捨てる。
        /// </summary>
        private void PruneUnreferencedCaches()
        {
            _referencedKeysBuffer.Clear();
            _referencedObjectsBuffer.Clear();
            for (int i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                if (binding.Key != null) _referencedKeysBuffer.Add(binding.Key);
                if (binding.Model != null) _referencedObjectsBuffer.Add(binding.Model);
                if (binding.Clip != null) _referencedObjectsBuffer.Add(binding.Clip);
            }

            if (_displayTextures.Count > _referencedKeysBuffer.Count + SpareCachedTextureCount)
            {
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

            _objectsBuffer.Clear();
            foreach (var clip in _sampledClips.Keys)
            {
                if (clip == null || !_referencedObjectsBuffer.Contains(clip))
                    _objectsBuffer.Add(clip);
            }
            foreach (var obj in _identities.Keys)
            {
                if (obj == null || !_referencedObjectsBuffer.Contains(obj))
                    _objectsBuffer.Add(obj);
            }
            for (int i = 0; i < _objectsBuffer.Count; i++)
            {
                var obj = _objectsBuffer[i];
                if (obj is AnimationClip clip) _sampledClips.Remove(clip);
                _identities.Remove(obj);
            }
            _objectsBuffer.Clear();
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

        private static bool ContainsReference(List<UnityEngine.Object> objects, UnityEngine.Object target)
        {
            if (ReferenceEquals(target, null)) return false;

            for (int i = 0; i < objects.Count; i++)
            {
                if (ReferenceEquals(objects[i], target)) return true;
            }
            return false;
        }

        /// <summary>
        /// キャッシュキー用のオブジェクト識別子を返す（インポートされるまでキャッシュする）。
        /// アセットなら GUID + ローカル ID（<paramref name="includeDependencyHashes"/> なら依存アセットすべての
        /// 依存ハッシュも）、アセットでなければ InstanceID。
        /// </summary>
        private ObjectIdentity GetIdentity(UnityEngine.Object obj, bool includeDependencyHashes)
        {
            if (_identities.TryGetValue(obj, out var cached)) return cached;

            var identity = new ObjectIdentity();
            if (EditorUtility.IsPersistent(obj)
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var builder = new StringBuilder(guid.Length + 24);
                builder.Append(guid).Append(':').Append(localId.ToString(CultureInfo.InvariantCulture));

                identity.Persistent = true;
                identity.DependencyPaths = new HashSet<string>(StringComparer.Ordinal) { path };
                if (includeDependencyHashes)
                {
                    var dependencies = AssetDatabase.GetDependencies(path, true);
                    Array.Sort(dependencies, StringComparer.Ordinal);
                    for (int i = 0; i < dependencies.Length; i++)
                    {
                        identity.DependencyPaths.Add(dependencies[i]);
                        builder.Append('|').Append(dependencies[i]).Append('=')
                            .Append(AssetDatabase.GetAssetDependencyHash(dependencies[i]).ToString());
                    }
                }
                identity.Value = builder.ToString();
            }
            else
            {
                identity.Persistent = false;
                identity.Value = InstanceIdentityPrefix + obj.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            }

            _identities[obj] = identity;
            return identity;
        }

        /// <summary>
        /// AnimationClip の中身の署名。全 float カーブの時刻 0 の値と、全参照カーブ（マテリアル差し替え等）の
        /// 時刻 0 の参照先から作る。snapshot に現れないプロパティ（マテリアル・UV 等）の変更も拾うため。
        /// </summary>
        private static string ComputeClipContentSignature(AnimationClip clip)
        {
            var builder = new StringBuilder(256);

            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < curveBindings.Length; i++)
            {
                var binding = curveBindings[i];
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;

                AppendBinding(builder, binding);
                builder.Append(curve.Evaluate(0f).ToString("R", CultureInfo.InvariantCulture)).Append('\u001e');
            }

            var referenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            for (int i = 0; i < referenceBindings.Length; i++)
            {
                var binding = referenceBindings[i];
                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);

                // 時刻 0 で有効な参照 = 時刻 0 以下で最後のキー（無ければ先頭のキー）。
                UnityEngine.Object value = null;
                if (keyframes != null && keyframes.Length > 0)
                {
                    value = keyframes[0].value;
                    for (int k = 1; k < keyframes.Length && keyframes[k].time <= 0f; k++)
                    {
                        value = keyframes[k].value;
                    }
                }

                AppendBinding(builder, binding);
                builder.Append(GetReferenceSignature(value)).Append('\u001e');
            }

            return builder.ToString();
        }

        private static void AppendBinding(StringBuilder builder, EditorCurveBinding binding)
        {
            builder.Append(binding.path).Append('\u001f')
                .Append(binding.type != null ? binding.type.FullName : string.Empty).Append('\u001f')
                .Append(binding.propertyName).Append('=');
        }

        private static string GetReferenceSignature(UnityEngine.Object value)
        {
            if (value == null) return "null";

            if (EditorUtility.IsPersistent(value)
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId))
            {
                return guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            }

            return InstanceIdentityPrefix + value.GetInstanceID().ToString(CultureInfo.InvariantCulture);
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
