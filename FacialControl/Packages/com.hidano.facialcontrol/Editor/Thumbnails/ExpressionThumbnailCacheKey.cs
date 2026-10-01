using System;
using System.Globalization;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// Expression サムネイルのキャッシュキーを組み立てる。
    /// <para>
    /// キーは「参照モデルの識別子（アセット GUID + 依存ハッシュ等）」「Expression を時刻 0 で
    /// サンプリングした snapshot の中身」「解像度」「生成方式のバージョン」から決まる。
    /// Expression の AnimationClip を差し替えた・中身を編集した・参照モデルを差し替えた／再インポート
    /// したときにキーが変わり、サムネイルが再生成される。
    /// </para>
    /// <para>
    /// プロセスをまたいで同じ値になる必要があるため <see cref="string.GetHashCode"/> は使わず、
    /// FNV-1a 64bit で計算する。
    /// </para>
    /// </summary>
    public static class ExpressionThumbnailCacheKey
    {
        /// <summary>
        /// 生成方式（構図・ライティング・適用方法）を変えたときに上げる。古いキャッシュを無効化する。
        /// </summary>
        public const int FormatVersion = 1;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// キャッシュキー（16 桁の 16 進小文字）を返す。ファイル名にそのまま使える。
        /// </summary>
        /// <param name="modelIdentity">参照モデルの識別子。null は空文字扱い</param>
        /// <param name="clipIdentity">
        /// AnimationClip の識別子。snapshot に現れない要素（マテリアル差し替え等）の変更を拾うため、
        /// clip アセットの GUID / 依存ハッシュを渡す。null は空文字扱い
        /// </param>
        /// <param name="snapshot">Expression の AnimationClip を時刻 0 でサンプリングした snapshot</param>
        /// <param name="resolution">キャプチャ解像度（px）</param>
        public static string Compute(string modelIdentity, string clipIdentity, in ExpressionSnapshot snapshot, int resolution)
        {
            ulong hash = FnvOffsetBasis;
            hash = AppendInt(hash, FormatVersion);
            hash = AppendInt(hash, resolution);
            hash = AppendString(hash, modelIdentity);
            hash = AppendString(hash, clipIdentity);

            var blendShapes = snapshot.BlendShapes.Span;
            hash = AppendInt(hash, blendShapes.Length);
            for (int i = 0; i < blendShapes.Length; i++)
            {
                hash = AppendString(hash, blendShapes[i].RendererPath);
                hash = AppendString(hash, blendShapes[i].Name);
                hash = AppendFloat(hash, blendShapes[i].Value);
            }

            var bones = snapshot.Bones.Span;
            hash = AppendInt(hash, bones.Length);
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                hash = AppendString(hash, bone.BonePath);
                hash = AppendFloat(hash, bone.PositionX);
                hash = AppendFloat(hash, bone.PositionY);
                hash = AppendFloat(hash, bone.PositionZ);
                hash = AppendFloat(hash, bone.EulerX);
                hash = AppendFloat(hash, bone.EulerY);
                hash = AppendFloat(hash, bone.EulerZ);
                hash = AppendFloat(hash, bone.ScaleX);
                hash = AppendFloat(hash, bone.ScaleY);
                hash = AppendFloat(hash, bone.ScaleZ);
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static ulong AppendByte(ulong hash, byte value)
        {
            unchecked
            {
                hash ^= value;
                hash *= FnvPrime;
            }
            return hash;
        }

        private static ulong AppendInt(ulong hash, int value)
        {
            unchecked
            {
                hash = AppendByte(hash, (byte)value);
                hash = AppendByte(hash, (byte)(value >> 8));
                hash = AppendByte(hash, (byte)(value >> 16));
                hash = AppendByte(hash, (byte)(value >> 24));
            }
            return hash;
        }

        private static ulong AppendFloat(ulong hash, float value)
        {
            // -0 と +0 を同一視する（見た目が変わらないため）。
            if (value == 0f)
                value = 0f;
            return AppendInt(hash, BitConverter.SingleToInt32Bits(value));
        }

        private static ulong AppendString(ulong hash, string value)
        {
            if (value == null)
                value = string.Empty;

            // 長さを先に入れて ("ab","c") と ("a","bc") を区別する。
            hash = AppendInt(hash, value.Length);
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    hash = AppendByte(hash, (byte)c);
                    hash = AppendByte(hash, (byte)(c >> 8));
                }
            }
            return hash;
        }
    }
}
