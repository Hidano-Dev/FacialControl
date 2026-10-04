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
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
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
                Excluded("Hidano.FacialControl.Adapters.InputSources.InputActionAnalogSource", "Hidano.FacialControl.InputSystem", RecExclusionReason.WrappedByObservedSource, "wrapper経由でregistry登録されるIAnalogInputSource単独実装。", "Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding+AnalogInputSourceWrapper", "Hidano.FacialControl.InputSystem.Tests.PlayMode.Integration.InputSystemAdapterBindingIntegrationTests::OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes", new[] { new RecAllowedDirectReferrer("Hidano.FacialControl.InputSystem.Adapters.AdapterBindings.InputSystemAdapterBinding", "wrapper構築、値提供型への辞書引き渡し、overlay layer weight駆動。") }),
                Excluded("Hidano.FacialControl.Adapters.InputSources.ArKitOscAnalogSource", "Hidano.FacialControl.Osc", RecExclusionReason.NotRegisteredAtRuntime, "公開されるがregistryへ登録されないanalog source。", null, "Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings.ARKit.ArKitOscAdapterBindingTests::OnStart_FakeRegistry_RegistersNoInputSource", new[] { new RecAllowedDirectReferrer("Hidano.FacialControl.Osc.Adapters.AdapterBindings.ArKitOscAdapterBinding", "AnalogSourceの公開・Tick・診断公開のみ。") }),
                Excluded("Hidano.FacialControl.Adapters.InputSources.OscFloatAnalogSource", "Hidano.FacialControl.Osc", RecExclusionReason.NotRegisteredAtRuntime, "Runtime/Editorに構築・参照がなく合成パイプラインへ到達しない。", null, "Hidano.FacialControl.Tests.EditMode.Adapters.AdapterBindings.OscReceiverAdapterBindingTests::OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes"),
                Excluded("Hidano.FacialControl.Timeline.Editor.BakeSimulationHarness+OfflineExpressionSource", "Hidano.FacialControl.Timeline.Editor", RecExclusionReason.EditorOnly, "Editorのベイクシミュレーション専用ソース。", null, null)
            }.AsReadOnly();

        public static IReadOnlyList<RecProductAssemblyDeclaration> ProductAssemblies => ProductAssemblyList;
        public static IReadOnlyList<RecInputSourceCoverageEntry> Entries => EntryList;

        private static RecInputSourceCoverageEntry Observed(string type, string assembly, RecObservationCategory category, string reason)
        {
            return new RecInputSourceCoverageEntry(type, assembly, RecInputSourceClassification.Observed, category, RecExclusionReason.None, reason, null, Array.Empty<RecAllowedDirectReferrer>(), null);
        }

        private static RecInputSourceCoverageEntry Excluded(string type, string assembly, RecExclusionReason exclusion, string reason, string wrapper, string contract, IReadOnlyList<RecAllowedDirectReferrer> allowedDirectReferrers = null)
        {
            return new RecInputSourceCoverageEntry(type, assembly, RecInputSourceClassification.Excluded, RecObservationCategory.None, exclusion, reason, wrapper, allowedDirectReferrers ?? Array.Empty<RecAllowedDirectReferrer>(), contract);
        }
    }
}
