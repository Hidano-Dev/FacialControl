using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Unity.Profiling;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Hidano.FacialControl.Adapters.DependencyInjection
{
    /// <summary>
    /// per-<c>FacialController</c> の child <see cref="LifetimeScope"/> を build / dispose する plain C# wrapper。
    /// <see cref="FacialControlAppLifetimeScope"/> を親として <c>CreateChild</c> で動的生成し、
    /// <see cref="InputSourceRegistry"/> と各 <see cref="AdapterBindingHost"/> を <c>Lifetime.Scoped</c>
    /// で登録する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本クラスは MonoBehaviour ではなく plain C# class として動作し、<c>FacialController.Initialize</c> /
    /// <c>Cleanup</c> から build / dispose を呼び出される。<see cref="Dispose"/> で配下 host
    /// （<see cref="AdapterBindingHost"/> 群）の <see cref="IDisposable.Dispose"/> が VContainer 経由で
    /// 自動呼出される。
    /// </para>
    /// </remarks>
    public sealed class FacialControllerLifetimeScope : IDisposable
    {
        public const string BuildProfilerMarkerName =
            "FacialControl.FacialControllerLifetimeScope.Build";
        public const string DisposeProfilerMarkerName =
            "FacialControl.FacialControllerLifetimeScope.Dispose";

        private static readonly ProfilerMarker BuildProfilerMarker =
            new ProfilerMarker(BuildProfilerMarkerName);
        private static readonly ProfilerMarker DisposeProfilerMarker =
            new ProfilerMarker(DisposeProfilerMarkerName);

        private LifetimeScope _childScope;

        private FacialControllerLifetimeScope(LifetimeScope childScope)
        {
            _childScope = childScope;
        }

        /// <summary>
        /// VContainer の child <see cref="LifetimeScope"/>。<see cref="Dispose"/> 後は <c>null</c>。
        /// </summary>
        public LifetimeScope ChildScope => _childScope;

        /// <summary>
        /// child scope の <see cref="IObjectResolver"/>。<see cref="Dispose"/> 後は <c>null</c>。
        /// </summary>
        public IObjectResolver Container => _childScope != null ? _childScope.Container : null;

        /// <summary>
        /// per-FC child scope を build する factory。
        /// </summary>
        /// <param name="appScope">親 <see cref="FacialControlAppLifetimeScope"/>。null 不可。</param>
        /// <param name="profile">FacialProfile（値型）。</param>
        /// <param name="blendShapeNames">BlendShape 名配列。null 不可（空 list 可）。</param>
        /// <param name="bindings">SO の <c>_adapterBindings</c> 一覧。null 不可（空 list 可）。null 要素と無効（<see cref="AdapterBindingBase.Disabled"/>）の要素は skip。</param>
        /// <param name="hostGameObject">binding helper を <c>AddComponent</c> する宿主 GameObject。null 不可。</param>
        /// <param name="childScopeName">child scope の名前（任意）。</param>
        /// <exception cref="ArgumentNullException">必須引数のいずれかが null の場合。</exception>
        public static FacialControllerLifetimeScope Build(
            LifetimeScope appScope,
            FacialProfile profile,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<AdapterBindingBase> bindings,
            GameObject hostGameObject,
            string childScopeName = null,
            IActiveExpressionProvider activeExpressionProvider = null)
        {
            if (appScope == null)
            {
                throw new ArgumentNullException(nameof(appScope));
            }
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }
            if (hostGameObject == null)
            {
                throw new ArgumentNullException(nameof(hostGameObject));
            }

            using (BuildProfilerMarker.Auto())
            {
            FacialProfile capturedProfile = profile;
            IReadOnlyList<string> capturedBlendShapeNames = blendShapeNames;
            // 無効の binding は host を作らず（OnStart / Tick / Dispose を呼ばない）、
            // AdapterBuildContext.AdapterBindings にも載せない（起動していない binding を他の binding が前提にしないため）。
            IReadOnlyList<AdapterBindingBase> capturedBindings = AdapterBindingBase.SelectEnabled(bindings);
            GameObject capturedHostGameObject = hostGameObject;
            IActiveExpressionProvider capturedActiveProvider = activeExpressionProvider;

            LifetimeScope childScope = appScope.CreateChild(
                (Action<IContainerBuilder>)(builder =>
                {
                    builder.Register<IInputSourceRegistry, InputSourceRegistry>(Lifetime.Scoped);
                    builder.Register<IFacialOutputBus, FacialOutputBus>(Lifetime.Scoped);
                    builder.Register<IFacialInputObservationBus, FacialInputObservationBus>(Lifetime.Scoped);

                    builder.RegisterEntryPoint<PhonemeOverlayInputSourceRegistrationHost>(
                        resolver => new PhonemeOverlayInputSourceRegistrationHost(
                            CreateAdapterBuildContext(
                                resolver,
                                capturedProfile,
                                capturedBlendShapeNames,
                                capturedBindings,
                                capturedHostGameObject,
                                capturedActiveProvider)),
                        Lifetime.Scoped);

                    for (int i = 0; i < capturedBindings.Count; i++)
                    {
                        AdapterBindingBase binding = capturedBindings[i];
                        if (binding == null)
                        {
                            // 型欠落（[SerializeReference] の null 要素）は warn + skip し、
                            // 残りの binding は引き続き build する。
                            Debug.LogWarning(
                                "[FacialControl] FacialControllerLifetimeScope.Build: skipping null AdapterBinding (missing type).");
                            continue;
                        }

                        AdapterBindingBase capturedBinding = binding;
                        builder.RegisterEntryPoint<AdapterBindingHost>(
                            resolver => new AdapterBindingHost(
                                capturedBinding,
                                CreateAdapterBuildContext(
                                    resolver,
                                    capturedProfile,
                                    capturedBlendShapeNames,
                                    capturedBindings,
                                    capturedHostGameObject,
                                    capturedActiveProvider)),
                            Lifetime.Scoped);
                    }
                }),
                childScopeName);

            return new FacialControllerLifetimeScope(childScope);
            }
        }

        private static AdapterBuildContext CreateAdapterBuildContext(
            IObjectResolver resolver,
            FacialProfile profile,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<AdapterBindingBase> bindings,
            GameObject hostGameObject,
            IActiveExpressionProvider activeExpressionProvider)
        {
            IInputSourceRegistry registry = resolver.Resolve<IInputSourceRegistry>();
            IFacialOutputBus facialOutputBus = resolver.Resolve<IFacialOutputBus>();
            ITimeProvider timeProvider = resolver.Resolve<ITimeProvider>();
            resolver.TryResolve<ILipSyncProvider>(out ILipSyncProvider lipSyncProvider);

            return new AdapterBuildContext(
                profile,
                blendShapeNames,
                registry,
                facialOutputBus,
                timeProvider,
                hostGameObject,
                lipSyncProvider,
                activeExpressionProvider,
                bindings);
        }

        /// <summary>
        /// child scope を dispose する。配下の <see cref="Lifetime.Scoped"/> 登録物（<see cref="AdapterBindingHost"/> 等）
        /// の <see cref="IDisposable.Dispose"/> も VContainer 経由で連鎖呼出される。
        /// </summary>
        public void Dispose()
        {
            if (_childScope != null)
            {
                using (DisposeProfilerMarker.Auto())
                {
                    _childScope.Dispose();
                    _childScope = null;
                }
            }
        }
    }
}
