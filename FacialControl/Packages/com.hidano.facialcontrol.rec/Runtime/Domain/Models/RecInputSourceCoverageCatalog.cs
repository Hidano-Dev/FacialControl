using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Rec.Domain
{
    public enum RecInputSourceClassification
    {
        Observed,
        Excluded
    }

    public enum RecObservationCategory
    {
        None,
        Trigger,
        Analog,
        ValueProvider,
        DirectActivation
    }

    public enum RecExclusionReason
    {
        None,
        InjectionSource,
        NotRegisteredAtRuntime,
        EditorOnly,
        WrappedByObservedSource
    }

    public enum RecWeightWritePathClassification
    {
        Gated,
        Excluded
    }

    public enum RecWeightWritePathExclusionReason
    {
        None,
        InjectionPath,
        StructuralWrite,
        Initialization
    }

    public sealed class RecWeightWritePathEntry
    {
        public RecWeightWritePathEntry(
            string typeFullName,
            string memberName,
            string assemblyName,
            RecWeightWritePathClassification classification,
            RecWeightWritePathExclusionReason exclusionReason,
            string reason)
        {
            TypeFullName = typeFullName ?? throw new ArgumentNullException(nameof(typeFullName));
            MemberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
            AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
            Classification = classification;
            ExclusionReason = exclusionReason;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public string TypeFullName { get; }
        public string MemberName { get; }
        public string AssemblyName { get; }
        public RecWeightWritePathClassification Classification { get; }
        public RecWeightWritePathExclusionReason ExclusionReason { get; }
        public string Reason { get; }
    }

    public sealed class RecProductAssemblyDeclaration
    {
        public RecProductAssemblyDeclaration(string name, bool isEditorOnly)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsEditorOnly = isEditorOnly;
        }

        public string Name { get; }
        public bool IsEditorOnly { get; }
    }

    public sealed class RecAllowedDirectReferrer
    {
        public RecAllowedDirectReferrer(string typeFullName, string reason)
        {
            TypeFullName = typeFullName ?? throw new ArgumentNullException(nameof(typeFullName));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public string TypeFullName { get; }
        public string Reason { get; }
    }

    public sealed class RecInputSourceCoverageEntry
    {
        public RecInputSourceCoverageEntry(
            string typeFullName,
            string assemblyName,
            RecInputSourceClassification classification,
            RecObservationCategory category,
            RecExclusionReason exclusionReason,
            string reason,
            string wrapperTypeFullName,
            IReadOnlyList<RecAllowedDirectReferrer> allowedDirectReferrers,
            string runtimeRegistrationContractTest)
        {
            TypeFullName = typeFullName ?? throw new ArgumentNullException(nameof(typeFullName));
            AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
            Classification = classification;
            Category = category;
            ExclusionReason = exclusionReason;
            Reason = RewriteWeightCoverageReason(typeFullName, reason);
            WrapperTypeFullName = wrapperTypeFullName;
            AllowedDirectReferrers = allowedDirectReferrers ?? Array.Empty<RecAllowedDirectReferrer>();
            RuntimeRegistrationContractTest = runtimeRegistrationContractTest;
        }

        public string TypeFullName { get; }
        public string AssemblyName { get; }
        public RecInputSourceClassification Classification { get; }
        public RecObservationCategory Category { get; }
        public RecExclusionReason ExclusionReason { get; }
        public string Reason { get; }
        public string WrapperTypeFullName { get; }
        public IReadOnlyList<RecAllowedDirectReferrer> AllowedDirectReferrers { get; }
        public string RuntimeRegistrationContractTest { get; }

        private static string RewriteWeightCoverageReason(string typeFullName, string reason)
        {
            if (reason == null) throw new ArgumentNullException(nameof(reason));
            if (string.Equals(typeFullName, "Hidano.FacialControl.Adapters.InputSources.OverlayInputSource", StringComparison.Ordinal)
                || string.Equals(typeFullName, "Hidano.FacialControl.Adapters.InputSources.InputActionAnalogSource", StringComparison.Ordinal))
            {
                return reason + " Weight paths are gated and injected through the core boundary.";
            }

            return reason;
        }
    }

    /// <summary>
    /// IInputSource と IAnalogInputSource 単独実装の REC 対象分類を保持する唯一の正本。
    /// 拡張パッケージを参照できないため、型は FullName、アセンブリは名前で記述する。
    /// </summary>
    public static class RecInputSourceCoverageCatalog
    {
        private static readonly IReadOnlyList<RecProductAssemblyDeclaration> ProductAssemblyList =
            new List<RecProductAssemblyDeclaration>
            {
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Domain", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Application", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Adapters", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Osc", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.InputSystem", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.LipSync", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.IFacialMocap", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Rec.Domain", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Rec.Application", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Rec.Adapters", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Timeline", false),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Editor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Osc.Editor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.InputSystem.Editor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.LipSync.Editor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.IFacialMocap.Editor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Rec.Editor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.Timeline.Editor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.RoutingEditor", true),
                new RecProductAssemblyDeclaration("Hidano.FacialControl.ExpressionCreator", true)
            }.AsReadOnly();

        private static readonly IReadOnlyList<RecInputSourceCoverageEntry> EntryList =
            new List<RecInputSourceCoverageEntry>
            {
                Observed("Hidano.FacialControl.Application.UseCases.LayerUseCase+LayerExpressionSource", "Hidano.FacialControl.Application", RecObservationCategory.DirectActivation, "系1の消費アダプタ。"),
                Observed("Hidano.FacialControl.Adapters.InputSources.AnalogBlendShapeInputSource", "Hidano.FacialControl.Adapters", RecObservationCategory.ValueProvider, "IAnalogInputSourceから導出する値提供型。"),
                Observed("Hidano.FacialControl.Adapters.InputSources.AnalogExpressionInputSource", "Hidano.FacialControl.Adapters", RecObservationCategory.ValueProvider, "入力側analogの直参照を含む値提供型。"),
                Observed("Hidano.FacialControl.Adapters.InputSources.OverlayInputSource", "Hidano.FacialControl.Adapters", RecObservationCategory.ValueProvider, "内部クロスフェード状態を持つ値提供型。"),
                Observed("Hidano.FacialControl.Adapters.InputSources.OscInputSource", "Hidano.FacialControl.Osc", RecObservationCategory.ValueProvider, "OSC/iFacialMocap受信BlendShape。"),
                Observed("Hidano.FacialControl.Adapters.InputSources.GazeVector2InputSource", "Hidano.FacialControl.Osc", RecObservationCategory.Analog, "gaze 2軸。"),
                Observed("Hidano.FacialControl.Adapters.InputSources.ExpressionTriggerInputSource", "Hidano.FacialControl.InputSystem", RecObservationCategory.Trigger, "Expression trigger。"),
                Observed("Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding+AnalogInputSourceWrapper", "Hidano.FacialControl.InputSystem", RecObservationCategory.Analog, "InputActionAnalogSourceのregistry wrapper。"),
                Observed("Hidano.FacialControl.LipSync.Adapters.LipSyncPhonemeOverlayInputSource", "Hidano.FacialControl.LipSync", RecObservationCategory.ValueProvider, "uLipSync音素オーバーレイ。"),
                Observed("Hidano.FacialControl.Adapters.InputSources.AnalogAxesInputSource", "Hidano.FacialControl.IFacialMocap", RecObservationCategory.Analog, "頭部N軸。"),
                Observed("Hidano.FacialControl.Timeline.Adapters.InputSources.TimelineBakedValueSink", "Hidano.FacialControl.Timeline", RecObservationCategory.ValueProvider, "Timelineベイク値。"),
                Observed("Hidano.FacialControl.Timeline.Adapters.InputSources.TimelineExpressionStateSink", "Hidano.FacialControl.Timeline", RecObservationCategory.Trigger, "Timeline expression state。"),
                Observed("Hidano.FacialControl.Timeline.Adapters.InputSources.TimelineAnalogInputSource", "Hidano.FacialControl.Timeline", RecObservationCategory.Analog, "Timeline analog。"),
                Excluded("Hidano.FacialControl.Timeline.Adapters.InputSources.TimelineGazeInputSource", "Hidano.FacialControl.Timeline", RecExclusionReason.InjectionSource, "Timeline注入ソース。", null, null),
                Excluded("Hidano.FacialControl.Rec.Adapters.Playback.RecPlaybackAnalogSource", "Hidano.FacialControl.Rec.Adapters", RecExclusionReason.InjectionSource, "REC再生注入用ソース。", null, null),
                Excluded("Hidano.FacialControl.Rec.Adapters.Playback.RecPlaybackValueProviderSource", "Hidano.FacialControl.Rec.Adapters", RecExclusionReason.InjectionSource, "REC再生注入用の値提供ソース。", null, null),
                Excluded("Hidano.FacialControl.Adapters.InputSources.InputActionAnalogSource", "Hidano.FacialControl.InputSystem", RecExclusionReason.WrappedByObservedSource, "wrapper経由でregistry登録されるIAnalogInputSource単独実装。", "Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding+AnalogInputSourceWrapper", "Hidano.FacialControl.InputSystem.Tests.PlayMode.Integration.InputSystemAdapterBindingIntegrationTests::OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes", new[] { new RecAllowedDirectReferrer("Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding", "wrapper構築、値提供型への辞書引き渡し、overlay layer weight駆動。") }),
                Excluded("Hidano.FacialControl.Adapters.InputSources.ArKitOscAnalogSource", "Hidano.FacialControl.Osc", RecExclusionReason.NotRegisteredAtRuntime, "公開されるがregistryへ登録されないanalog source。", null, "Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings.ArKitOscAdapterBindingTests::OnStart_FakeRegistry_RegistersNoInputSource", new[] { new RecAllowedDirectReferrer("Hidano.FacialControl.Adapters.AdapterBindings.ARKit.ArKitOscAdapterBinding", "AnalogSourceの公開・Tick・診断公開のみ。") }),
                Excluded("Hidano.FacialControl.Adapters.InputSources.OscFloatAnalogSource", "Hidano.FacialControl.Osc", RecExclusionReason.NotRegisteredAtRuntime, "Runtime/Editorに構築・参照がなく合成パイプラインへ到達しない。", null, "Hidano.FacialControl.Tests.EditMode.Adapters.AdapterBindings.OscReceiverAdapterBindingTests::OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes"),
                Excluded("Hidano.FacialControl.Timeline.Editor.BakeSimulationHarness+OfflineExpressionSource", "Hidano.FacialControl.Timeline.Editor", RecExclusionReason.EditorOnly, "Editorのベイクシミュレーション専用ソース。", null, null)
            }.AsReadOnly();

        public static IReadOnlyList<RecProductAssemblyDeclaration> ProductAssemblies => ProductAssemblyList;
        public static IReadOnlyList<RecInputSourceCoverageEntry> Entries => EntryList;
        public static IReadOnlyList<RecWeightWritePathEntry> WeightWritePaths { get; } =
            new List<RecWeightWritePathEntry>
            {
                Gated("Hidano.FacialControl.Adapters.Playable.FacialController", "SetLayerWeight", "Hidano.FacialControl.Adapters", "Facade for the gated LayerUseCase layer-weight write; script and overlay entry point."),
                Gated("Hidano.FacialControl.Application.UseCases.LayerUseCase", "SetLayerWeight", "Hidano.FacialControl.Application", "Live layer-weight write entry; no-op while live writes are suspended and observed after aggregation."),
                Gated("Hidano.FacialControl.Adapters.Playable.FacialController", "SetInputSourceWeight", "Hidano.FacialControl.Adapters", "Facade for the gated LayerUseCase input-source weight write."),
                Gated("Hidano.FacialControl.Application.UseCases.LayerUseCase", "SetInputSourceWeight", "Hidano.FacialControl.Application", "Facade for LayerInputSourceWeightBuffer.SetWeight, protected by the live-write gate."),
                Gated("Hidano.FacialControl.Domain.Services.LayerInputSourceWeightBuffer", "SetWeight", "Hidano.FacialControl.Domain", "Single live input-source weight entry point; suspend uses the in-flight fence."),
                Gated("Hidano.FacialControl.Adapters.Playable.FacialController", "BeginInputSourceWeightBatch", "Hidano.FacialControl.Adapters", "Facade for the gated input-source bulk write."),
                Gated("Hidano.FacialControl.Application.UseCases.LayerUseCase", "BeginInputSourceWeightBatch", "Hidano.FacialControl.Application", "Facade for LayerInputSourceWeightBuffer.BeginBulk."),
                Gated("Hidano.FacialControl.Domain.Services.LayerInputSourceWeightBuffer+BulkScope", "SetWeight", "Hidano.FacialControl.Domain", "Accumulates pending values; Dispose/CommitBulk applies the live gate."),
                ExcludedWeight("Hidano.FacialControl.Domain.Services.LayerInputSourceWeightBuffer", "SetWeightBypassingLiveGate", "Hidano.FacialControl.Domain", RecWeightWritePathExclusionReason.InjectionPath, "REC baseline, injection, and structural writes use this bypass; live callers do not."),
                ExcludedWeight("Hidano.FacialControl.Application.UseCases.LayerUseCase", "TryInjectLayerWeight / TryInjectInputSourceWeight / TrySetBaselineLayerWeight / TrySetBaselineInputSourceWeight / ResetWeightsToDeclared", "Hidano.FacialControl.Application", RecWeightWritePathExclusionReason.InjectionPath, "IWeightInjectionGate injection and baseline surface; injected values are observed at the next consumption point."),
                ExcludedWeight("Hidano.FacialControl.Application.UseCases.LayerUseCase", "BindLateInputSource", "Hidano.FacialControl.Application", RecWeightWritePathExclusionReason.StructuralWrite, "Declaration-derived structural values; an existing slot is not rewritten while live writes are suspended."),
                ExcludedWeight("Hidano.FacialControl.Application.UseCases.LayerUseCase", "UnbindLateInputSource", "Hidano.FacialControl.Application", RecWeightWritePathExclusionReason.StructuralWrite, "Compacts remaining slot weights without creating a new live value; the layer is observed afterward."),
                ExcludedWeight("Hidano.FacialControl.Application.UseCases.LayerUseCase", "BuildAggregatorPipeline", "Hidano.FacialControl.Application", RecWeightWritePathExclusionReason.Initialization, "Profile construction and SetProfile initialization values are outside a recording session."),
                Gated("Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding", "ApplyOverlayLayerWeights", "Hidano.FacialControl.InputSystem", "Extension-side caller; it invokes the core gated layer-weight entry from LateTick.")
            }.AsReadOnly();

        private static RecInputSourceCoverageEntry Observed(string type, string assembly, RecObservationCategory category, string reason)
        {
            return new RecInputSourceCoverageEntry(type, assembly, RecInputSourceClassification.Observed, category, RecExclusionReason.None, reason, null, Array.Empty<RecAllowedDirectReferrer>(), null);
        }

        private static RecInputSourceCoverageEntry Excluded(string type, string assembly, RecExclusionReason exclusion, string reason, string wrapper, string contract, IReadOnlyList<RecAllowedDirectReferrer> allowedDirectReferrers = null)
        {
            return new RecInputSourceCoverageEntry(type, assembly, RecInputSourceClassification.Excluded, RecObservationCategory.None, exclusion, reason, wrapper, allowedDirectReferrers ?? Array.Empty<RecAllowedDirectReferrer>(), contract);
        }

        private static RecWeightWritePathEntry Gated(string type, string member, string assembly, string reason)
        {
            return new RecWeightWritePathEntry(type, member, assembly, RecWeightWritePathClassification.Gated, RecWeightWritePathExclusionReason.None, reason);
        }

        private static RecWeightWritePathEntry ExcludedWeight(string type, string member, string assembly, RecWeightWritePathExclusionReason exclusion, string reason)
        {
            return new RecWeightWritePathEntry(type, member, assembly, RecWeightWritePathClassification.Excluded, exclusion, reason);
        }
    }
}
