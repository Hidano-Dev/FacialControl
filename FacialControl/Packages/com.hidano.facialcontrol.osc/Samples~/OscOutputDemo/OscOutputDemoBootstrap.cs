using System;
using System.Collections;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Samples.OscOutputDemo
{
    // Sender 側 demo の最小 helper。procedural mesh の生成は廃止し、
    // ユーザーが Scene に持ち込んだ SkinnedMeshRenderer に対して FacialController が
    // 自動的に動作する設計に合わせる。本 MonoBehaviour の唯一の責務は OSC 通信を
    // ウィンドウ非フォーカス時にも処理させるため `Application.runInBackground` を有効化すること。
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("FacialControl/Samples/OSC Output Demo Bootstrap")]
    public sealed class OscOutputDemoBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            UnityEngine.Application.runInBackground = true;
        }
    }

    [Serializable]
    [FacialAdapterBinding("OSC Output Demo Signal")]
    public sealed class OscOutputDemoSignalBinding : AdapterBindingBase
    {
        [SerializeField]
        [Tooltip("デモ用 sin 波の 1 周期の秒数。")]
        private float _cycleSeconds = 4f;

        private DemoSignalState _state;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            if (!AdapterSlug.TryParse(Slug, out AdapterSlug slug))
            {
                Debug.LogWarning("[OscOutputDemo] Demo signal slug is invalid.");
                return;
            }

            int blendShapeCount = ctx.BlendShapeNames != null ? ctx.BlendShapeNames.Count : 0;
            if (blendShapeCount == 0)
            {
                Debug.LogWarning(
                    "[OscOutputDemo] Mesh has no BlendShapes; demo signal will only emit Gaze values.");
            }

            _state = new DemoSignalState(Mathf.Max(0.25f, _cycleSeconds));
            ctx.InputSourceRegistry.Register(
                slug,
                "blendshape",
                new DemoBlendShapeSource(slug.Value + ":blendshape", blendShapeCount, _state));
            // Profile の既定 Gaze チャネル id ("gaze") と一致させ、FacialController の自動解決で結線されるようにする。
            ctx.InputSourceRegistry.Register(
                slug,
                "gaze",
                new DemoGazeSource(slug.Value + ":gaze", _state));
        }
    }

    internal sealed class DemoSignalState
    {
        private const float Tau = 6.28318530718f;
        private const float PerChannelPhaseOffset = 0.7f;

        private readonly float _cycleSeconds;
        private float _time;

        public DemoSignalState(float cycleSeconds)
        {
            _cycleSeconds = cycleSeconds;
        }

        public float GazeX { get; private set; }
        public float GazeY { get; private set; }

        public void Advance(float deltaTime)
        {
            _time += Mathf.Max(0f, deltaTime);
            float t = (_time / _cycleSeconds) * Tau;

            GazeX = Mathf.Sin(t * 0.75f) * 0.8f;
            GazeY = Mathf.Cos(t * 0.5f) * 0.45f;
        }

        // index ごとに位相をずらした sin 波を 0..1 に正規化して返す。
        // すべての BlendShape index に対して波形を提供できるため、mesh の BlendShape 数が
        // いくつでも demo signal が動作する。
        public float GetChannel(int index)
        {
            float t = (_time / _cycleSeconds) * Tau + (index * PerChannelPhaseOffset);
            return 0.5f + (0.5f * Mathf.Sin(t));
        }
    }

    internal sealed class DemoBlendShapeSource : IInputSource
    {
        private readonly DemoSignalState _state;
        private readonly BitArray _contributeMask;

        public DemoBlendShapeSource(string id, int blendShapeCount, DemoSignalState state)
        {
            Id = id;
            BlendShapeCount = Mathf.Max(0, blendShapeCount);
            _state = state;
            _contributeMask = new BitArray(BlendShapeCount, true);
        }

        public string Id { get; }
        public InputSourceType Type => InputSourceType.ValueProvider;
        public int BlendShapeCount { get; }
        public BitArray ContributeMask => _contributeMask;

        public void Tick(float deltaTime)
        {
            _state.Advance(deltaTime);
        }

        public bool TryWriteValues(Span<float> output)
        {
            int len = output.Length;
            for (int i = 0; i < len; i++)
            {
                output[i] = _state.GetChannel(i);
            }
            return true;
        }
    }

    internal sealed class DemoGazeSource : IInputSource, IAnalogInputSource
    {
        private readonly DemoSignalState _state;
        private readonly BitArray _contributeMask = new BitArray(0);

        public DemoGazeSource(string id, DemoSignalState state)
        {
            Id = id;
            _state = state;
        }

        public string Id { get; }
        public InputSourceType Type => InputSourceType.ValueProvider;
        public int BlendShapeCount => 0;
        public BitArray ContributeMask => _contributeMask;
        public bool IsValid => true;
        public int AxisCount => 2;

        public void Tick(float deltaTime)
        {
        }

        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        public bool TryReadScalar(out float value)
        {
            value = _state.GazeX;
            return true;
        }

        public bool TryReadVector2(out float x, out float y)
        {
            x = _state.GazeX;
            y = _state.GazeY;
            return true;
        }

        public bool TryReadAxes(Span<float> output)
        {
            if (output.Length == 0)
            {
                return false;
            }

            output[0] = _state.GazeX;
            if (output.Length > 1)
            {
                output[1] = _state.GazeY;
            }

            return true;
        }
    }
}
