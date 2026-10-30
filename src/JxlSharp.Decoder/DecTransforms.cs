// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JxlSharp.Base;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// DCT resampling scale tables and <c>WcMultipliers</c> constants.
    /// Ported from <c>lib/jxl/dct_scales.h</c> and <c>lib/jxl/dct_scales.cc</c>.
    /// </summary>
    public static class DctScales
    {
        /// <summary><c>sqrt(2)</c> constant (1.41421356237f).</summary>
        public const float kSqrt2 = 1.41421356237f;

        /// <summary><c>sqrt(0.5)</c> constant (0.70710678118f).</summary>
        public const float kSqrt0_5 = 0.70710678118f;

        private static readonly float[] Scales8To1 = { 1.0f };

        private static readonly float[] Scales16To2 =
        {
            1.0f,
            0.901764195028874394f,
        };

        private static readonly float[] Scales32To4 =
        {
            1.0f,
            0.974886821136879522f,
            0.901764195028874394f,
            0.787054918159101335f,
        };

        private static readonly float[] Scales64To8 =
        {
            1.0f,                0.9936866130906366f, 0.9748868211368796f,
            0.9440180941651672f, 0.9017641950288744f, 0.8490574973847023f,
            0.7870549181591013f, 0.7171081282466044f,
        };

        private static readonly float[] Scales128To16 =
        {
            1.0f,                0.9984194528776054f, 0.9936866130906366f, 0.9858278282666936f,
            0.9748868211368796f, 0.9609244059440204f, 0.9440180941651672f, 0.9242615922757944f,
            0.9017641950288744f, 0.8766500784429904f, 0.8490574973847023f, 0.8191378932865928f,
            0.7870549181591013f, 0.7529833816270532f, 0.7171081282466044f, 0.6796228528314651f,
        };

        private static readonly float[] Scales256To32 =
        {
            1.0f,                0.9996047255830407f, 0.9984194528776054f, 0.9964458326264695f,
            0.9936866130906366f, 0.9901456355893141f, 0.9858278282666936f, 0.9807391980963174f,
            0.9748868211368796f, 0.9682788310563117f, 0.9609244059440204f, 0.9528337534340876f,
            0.9440180941651672f, 0.9344896436056892f, 0.9242615922757944f, 0.913348084400198f,
            0.9017641950288744f, 0.8895259056651056f, 0.8766500784429904f, 0.8631544288990163f,
            0.8490574973847023f, 0.8343786191696513f, 0.8191378932865928f, 0.8033561501721485f,
            0.7870549181591013f, 0.7702563888779096f, 0.7529833816270532f, 0.7352593067735488f,
            0.7171081282466044f, 0.6985543251889097f, 0.6796228528314651f, 0.6603391026591464f,
        };

        private static readonly float[] Scales1To8 = { 1.0f };

        private static readonly float[] Scales2To16 =
        {
            1.0f,
            1.108937353592731823f,
        };

        private static readonly float[] Scales4To32 =
        {
            1.0f,
            1.025760096781116015f,
            1.108937353592731823f,
            1.270559368765487251f,
        };

        private static readonly float[] Scales8To64 =
        {
            1.0f,                1.0063534990068217f, 1.0257600967811158f,
            1.0593017296817173f, 1.1089373535927318f, 1.1777765381970435f,
            1.2705593687654873f, 1.3944898413647777f,
        };

        private static readonly float[] Scales16To128 =
        {
            1.0f,                1.0015830492062623f, 1.0063534990068217f, 1.0143759095928793f,
            1.0257600967811158f, 1.0406645869480142f, 1.0593017296817173f, 1.0819447744633812f,
            1.1089373535927318f, 1.1407059950032632f, 1.1777765381970435f, 1.2207956782315876f,
            1.2705593687654873f, 1.3280505578213306f, 1.3944898413647777f, 1.4714043176061107f,
        };

        private static readonly float[] Scales32To256 =
        {
            1.0f,                1.0003954307206069f, 1.0015830492062623f, 1.0035668445360069f,
            1.0063534990068217f, 1.009952439375063f,  1.0143759095928793f, 1.0196390660647288f,
            1.0257600967811158f, 1.0327603660498115f, 1.0406645869480142f, 1.049501024072585f,
            1.0593017296817173f, 1.0701028169146336f, 1.0819447744633812f, 1.0948728278734026f,
            1.1089373535927318f, 1.124194353004584f,  1.1407059950032632f, 1.158541237256391f,
            1.1777765381970435f, 1.1984966740820495f, 1.2207956782315876f, 1.244777922949508f,
            1.2705593687654873f, 1.2982690107339132f, 1.3280505578213306f, 1.3600643892400104f,
            1.3944898413647777f, 1.4315278911623237f, 1.4714043176061107f, 1.5143734423314616f,
        };

        private static readonly float[] WcMult4 =
        {
            0.541196100146197f,
            1.3065629648763764f,
        };

        private static readonly float[] WcMult8 =
        {
            0.5097955791041592f,
            0.6013448869350453f,
            0.8999762231364156f,
            2.5629154477415055f,
        };

        private static readonly float[] WcMult16 =
        {
            0.5024192861881557f, 0.5224986149396889f, 0.5669440348163577f,
            0.6468217833599901f, 0.7881546234512502f, 1.060677685990347f,
            1.7224470982383342f, 5.101148618689155f,
        };

        private static readonly float[] WcMult32 =
        {
            0.5006029982351963f, 0.5054709598975436f, 0.5154473099226246f,
            0.5310425910897841f, 0.5531038960344445f, 0.5829349682061339f,
            0.6225041230356648f, 0.6748083414550057f, 0.7445362710022986f,
            0.8393496454155268f, 0.9725682378619608f, 1.1694399334328847f,
            1.4841646163141662f, 2.057781009953411f,  3.407608418468719f,
            10.190008123548033f,
        };

        private static readonly float[] WcMult64 =
        {
            0.500150636020651f,  0.5013584524464084f, 0.5037887256810443f,
            0.5074711720725553f, 0.5124514794082247f, 0.5187927131053328f,
            0.52657731515427f,   0.535909816907992f,  0.5469204379855088f,
            0.5597698129470802f, 0.57465518403266f,   0.5918185358574165f,
            0.6115573478825099f, 0.6342389366884031f, 0.6603198078137061f,
            0.6903721282002123f, 0.7251205223771985f, 0.7654941649730891f,
            0.8127020908144905f, 0.8683447152233481f, 0.9345835970364075f,
            1.0144082649970547f, 1.1120716205797176f, 1.233832737976571f,
            1.3892939586328277f, 1.5939722833856311f, 1.8746759800084078f,
            2.282050068005162f,  2.924628428158216f,  4.084611078129248f,
            6.796750711673633f,  20.373878167231453f,
        };

        private static readonly float[] WcMult128 =
        {
            0.5000376519155477f, 0.5003390374428216f, 0.5009427176380873f,
            0.5018505174842379f, 0.5030651913013697f, 0.5045904432216454f,
            0.5064309549285542f, 0.5085924210498143f, 0.5110815927066812f,
            0.5139063298475396f, 0.5170756631334912f, 0.5205998663018917f,
            0.524490540114724f,  0.5287607092074876f, 0.5334249333971333f,
            0.538499435291984f,  0.5440022463817783f, 0.549953374183236f,
            0.5563749934898856f, 0.5632916653417023f, 0.5707305880121454f,
            0.5787218851348208f, 0.5872989370937893f, 0.5964987630244563f,
            0.606362462272146f,  0.6169357260050706f, 0.6282694319707711f,
            0.6404203382416639f, 0.6534518953751283f, 0.6674352009263413f,
            0.6824501259764195f, 0.6985866506472291f, 0.7159464549705746f,
            0.7346448236478627f, 0.7548129391165311f, 0.776600658233963f,
            0.8001798956216941f, 0.8257487738627852f, 0.8535367510066064f,
            0.8838110045596234f, 0.9168844461846523f, 0.9531258743921193f,
            0.9929729612675466f, 1.036949040910389f,  1.0856850642580145f,
            1.1399486751015042f, 1.2006832557294167f, 1.2690611716991191f,
            1.346557628206286f,  1.4350550884414341f, 1.5369941008524954f,
            1.6555965242641195f, 1.7952052190778898f, 1.961817848571166f,
            2.163957818751979f,  2.4141600002500763f, 2.7316450287739396f,
            3.147462191781909f,  3.7152427383269746f, 4.5362909369693565f,
            5.827688377844654f,  8.153848602466814f,  13.58429025728446f,
            40.744688103351834f,
        };

        private static readonly float[] WcMult256 =
        {
            0.5000094125358878f, 0.500084723455784f,  0.5002354020255269f,
            0.5004615618093246f, 0.5007633734146156f, 0.5011410648064231f,
            0.5015949217281668f, 0.502125288230386f,  0.5027325673091954f,
            0.5034172216566842f, 0.5041797745258774f, 0.5050208107132756f,
            0.5059409776624396f, 0.5069409866925212f, 0.5080216143561264f,
            0.509183703931388f,  0.5104281670536573f, 0.5117559854927805f,
            0.5131682130825206f, 0.5146659778093218f, 0.516250484068288f,
            0.5179230150949777f, 0.5196849355823947f, 0.5215376944933958f,
            0.5234828280796439f, 0.52552196311921f,   0.5276568203859896f,
            0.5298892183652453f, 0.5322210772308335f, 0.5346544231010253f,
            0.537191392591309f,  0.5398342376841637f, 0.5425853309375497f,
            0.545447171055775f,  0.5484223888484947f, 0.551513753605893f,
            0.554724179920619f,  0.5580567349898085f, 0.5615146464335654f,
            0.5651013106696203f, 0.5688203018875696f, 0.5726753816701664f,
            0.5766705093136241f, 0.5808098529038624f, 0.5850978012111273f,
            0.58953897647151f,   0.5941382481306648f, 0.5989007476325463f,
            0.6038318843443582f, 0.6089373627182432f, 0.614223200800649f,
            0.6196957502119484f, 0.6253617177319102f, 0.6312281886412079f,
            0.6373026519855411f, 0.6435930279473415f, 0.6501076975307724f,
            0.6568555347890955f, 0.6638459418498757f, 0.6710888870233562f,
            0.6785949463131795f, 0.6863753486870501f, 0.6944420255086364f,
            0.7028076645818034f, 0.7114857693151208f, 0.7204907235796304f,
            0.7298378629074134f, 0.7395435527641373f, 0.749625274727372f,
            0.7601017215162176f, 0.7709929019493761f, 0.7823202570613161f,
            0.7941067887834509f, 0.8063772028037925f, 0.8191580674598145f,
            0.83247799080191f,   0.8463678182968619f, 0.860860854031955f,
            0.8759931087426972f, 0.8918035785352535f, 0.9083345588266809f,
            0.9256319988042384f, 0.9437459026371479f, 0.962730784794803f,
            0.9826461881778968f, 1.0035572754078206f, 1.0255355056139732f,
            1.048659411496106f,  1.0730154944316674f, 1.0986992590905857f,
            1.1258164135986009f, 1.1544842669978943f, 1.184833362908442f,
            1.217009397314603f,  1.2511754798461228f, 1.287514812536712f,
            1.326233878832723f,  1.3675662599582539f, 1.411777227500661f,
            1.459169302866857f,  1.5100890297227016f, 1.5649352798258847f,
            1.6241695131835794f, 1.6883285509131505f, 1.7580406092704062f,
            1.8340456094306077f, 1.9172211551275689f, 2.0086161135167564f,
            2.1094945286246385f, 2.22139377701127f,   2.346202662531156f,
            2.486267909203593f,  2.644541877144861f,  2.824791402350551f,
            3.0318994541759925f, 3.2723115884254845f, 3.5547153325075804f,
            3.891107790700307f,  4.298537526449054f,  4.802076008665048f,
            5.440166215091329f,  6.274908408039339f,  7.413566756422303f,
            9.058751453879703f,  11.644627325175037f, 16.300023088031555f,
            27.163977662448232f, 81.48784219222516f,
        };

        /// <summary>
        /// Gets the DCT resample scale table for <c>DCTResampleScales&lt;from, to&gt;::kScales</c>.
        /// </summary>
        /// <param name="from">Source DCT size.</param>
        /// <param name="to">Target DCT size.</param>
        /// <returns>Read-only span of resample scales.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<float> GetResampleScales(int from, int to)
        {
            return (from, to) switch
            {
                (8, 1)    => Scales8To1,
                (16, 2)   => Scales16To2,
                (32, 4)   => Scales32To4,
                (64, 8)   => Scales64To8,
                (128, 16) => Scales128To16,
                (256, 32) => Scales256To32,
                (1, 8)    => Scales1To8,
                (2, 16)   => Scales2To16,
                (4, 32)   => Scales4To32,
                (8, 64)   => Scales8To64,
                (16, 128) => Scales16To128,
                (32, 256) => Scales32To256,
                _         => throw new ArgumentOutOfRangeException(nameof(from), $"Unsupported DCTResampleScales<{from}, {to}>."),
            };
        }

        /// <summary>
        /// Returns <c>DCTResampleScales&lt;from, to&gt;::kScales[x]</c>.
        /// </summary>
        /// <param name="from">Source DCT size.</param>
        /// <param name="to">Target DCT size.</param>
        /// <param name="x">Coefficient index.</param>
        /// <returns>Resample scale factor.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DCTTotalResampleScale(int from, int to, int x)
        {
            return GetResampleScales(from, to)[x];
        }

        /// <summary>
        /// Gets the <c>WcMultipliers&lt;n&gt;::kMultipliers</c> table of length <c>n / 2</c>.
        /// </summary>
        /// <param name="n">DCT size in <c>{4, 8, 16, 32, 64, 128, 256}</c>.</param>
        /// <returns>Read-only span of multipliers.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<float> GetWcMultipliers(int n)
        {
            return n switch
            {
                4   => WcMult4,
                8   => WcMult8,
                16  => WcMult16,
                32  => WcMult32,
                64  => WcMult64,
                128 => WcMult128,
                256 => WcMult256,
                _   => throw new ArgumentOutOfRangeException(nameof(n), $"Unsupported WcMultipliers<{n}>."),
            };
        }
    }

    /// <summary>
    /// Strided 2D float reader for DCT/IDCT and transpose operations.
    /// Ported from <c>jxl::DCTFrom</c> in <c>lib/jxl/dct_block-inl.h</c>.
    /// </summary>
    public readonly ref struct DctFrom
    {
        private readonly ReadOnlySpan<float> _data;
        private readonly nuint _stride;

        /// <summary>
        /// Initializes a new instance of the <see cref="DctFrom"/> struct.
        /// </summary>
        /// <param name="data">Span starting at the top-left of the 2D block.</param>
        /// <param name="stride">Row stride in floats.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DctFrom(ReadOnlySpan<float> data, nuint stride)
        {
            _data   = data;
            _stride = stride;
        }

        /// <summary>Gets the row stride in floats.</summary>
        public nuint Stride { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _stride; }

        /// <summary>Gets the underlying read-only span.</summary>
        public ReadOnlySpan<float> Data { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _data; }

        /// <summary>
        /// Reads the float at (<paramref name="row"/>, <paramref name="col"/>).
        /// </summary>
        /// <param name="row">Row index.</param>
        /// <param name="col">Column index.</param>
        /// <returns>Value at (<paramref name="row"/>, <paramref name="col"/>).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Read(nuint row, nuint col) => _data[(int)(row * _stride + col)];

        /// <summary>
        /// Returns a read-only span starting at (<paramref name="row"/>, <paramref name="col"/>).
        /// </summary>
        /// <param name="row">Row index.</param>
        /// <param name="col">Column index.</param>
        /// <returns>Span starting at (<paramref name="row"/>, <paramref name="col"/>).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<float> Slice(nuint row, nuint col) => _data.Slice((int)(row * _stride + col));
    }

    /// <summary>
    /// Strided 2D float writer for DCT/IDCT and transpose operations.
    /// Ported from <c>jxl::DCTTo</c> in <c>lib/jxl/dct_block-inl.h</c>.
    /// </summary>
    public readonly ref struct DctTo
    {
        private readonly Span<float> _data;
        private readonly nuint _stride;

        /// <summary>
        /// Initializes a new instance of the <see cref="DctTo"/> struct.
        /// </summary>
        /// <param name="data">Span starting at the top-left of the 2D block.</param>
        /// <param name="stride">Row stride in floats.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DctTo(Span<float> data, nuint stride)
        {
            _data   = data;
            _stride = stride;
        }

        /// <summary>Gets the row stride in floats.</summary>
        public nuint Stride { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _stride; }

        /// <summary>Gets the underlying writable span.</summary>
        public Span<float> Data { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _data; }

        /// <summary>
        /// Writes <paramref name="val"/> to (<paramref name="row"/>, <paramref name="col"/>).
        /// </summary>
        /// <param name="val">Value to write.</param>
        /// <param name="row">Row index.</param>
        /// <param name="col">Column index.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(float val, nuint row, nuint col) => _data[(int)(row * _stride + col)] = val;

        /// <summary>
        /// Returns a writable span starting at (<paramref name="row"/>, <paramref name="col"/>).
        /// </summary>
        /// <param name="row">Row index.</param>
        /// <param name="col">Column index.</param>
        /// <returns>Span starting at (<paramref name="row"/>, <paramref name="col"/>).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<float> Slice(nuint row, nuint col) => _data.Slice((int)(row * _stride + col));
    }

    /// <summary>
    /// 2D block transpose operations for DCT and IDCT.
    /// Ported from <c>lib/jxl/transpose-inl.h</c>.
    /// </summary>
    public static class DctTranspose
    {
        /// <summary>
        /// Transposes a <paramref name="rows"/> x <paramref name="cols"/> block from <paramref name="from"/> into a <paramref name="cols"/> x <paramref name="rows"/> block in <paramref name="to"/>.
        /// </summary>
        /// <param name="rows">Number of rows in <paramref name="from"/>.</param>
        /// <param name="cols">Number of columns in <paramref name="from"/>.</param>
        /// <param name="from">Source block.</param>
        /// <param name="to">Destination block.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Run(nuint rows, nuint cols, DctFrom from, DctTo to)
        {
            Transpose.GenericTransposeBlock<float>(from.Data, (int)from.Stride, to.Data, (int)to.Stride, (int)rows, (int)cols);
        }
    }

    /// <summary>
    /// Forward and inverse DCT, AFV corner-DCT, and VarDCT transform operations.
    /// Ported from <c>lib/jxl/dct-inl.h</c> and <c>lib/jxl/dec_transforms-inl.h</c>.
    /// </summary>
    public static partial class DecTransforms
    {
        private static readonly float[][] Afv4x4Basis =
        {
            new float[]
            {
                0.25f, 0.25f, 0.25f, 0.25f,
                0.25f, 0.25f, 0.25f, 0.25f,
                0.25f, 0.25f, 0.25f, 0.25f,
                0.25f, 0.25f, 0.25f, 0.25f,
            },
            new float[]
            {
                0.876902929799142f,   0.2206518106944235f,  -0.10140050393753763f, -0.1014005039375375f,
                0.2206518106944236f,  -0.10140050393753777f, -0.10140050393753772f, -0.10140050393753763f,
                -0.10140050393753758f, -0.10140050393753769f, -0.1014005039375375f,  -0.10140050393753768f,
                -0.10140050393753768f, -0.10140050393753759f, -0.10140050393753763f, -0.10140050393753741f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  0.40670075830260755f, 0.44444816619734445f,
                0.0f,                  0.0f,                  0.19574399372042936f, 0.2929100136981264f,
                -0.40670075830260716f, -0.19574399372042872f, 0.0f,                 0.11379074460448091f,
                -0.44444816619734384f, -0.29291001369812636f, -0.1137907446044814f, 0.0f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  -0.21255748058288748f, 0.3085497062849767f,
                0.0f,                  0.4706702258572536f,   -0.1621205195722993f,  0.0f,
                -0.21255748058287047f, -0.16212051957228327f, -0.47067022585725277f, -0.1464291867126764f,
                0.3085497062849487f,   0.0f,                  -0.14642918671266536f, 0.4251149611657548f,
            },
            new float[]
            {
                0.0f, -0.7071067811865474f, 0.0f, 0.0f,
                0.7071067811865476f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 0.0f,
            },
            new float[]
            {
                -0.4105377591765233f,  0.6235485373547691f,   -0.06435071657946274f, -0.06435071657946266f,
                0.6235485373547694f,   -0.06435071657946284f, -0.0643507165794628f,  -0.06435071657946274f,
                -0.06435071657946272f, -0.06435071657946279f, -0.06435071657946266f, -0.06435071657946277f,
                -0.06435071657946277f, -0.06435071657946273f, -0.06435071657946274f, -0.0643507165794626f,
            },
            new float[]
            {
                0.0f,                  0.0f,                   -0.4517556589999482f, 0.15854503551840063f,
                0.0f,                  -0.04038515160822202f,  0.0074182263792423875f, 0.39351034269210167f,
                -0.45175565899994635f, 0.007418226379244351f,  0.1107416575309343f,  0.08298163094882051f,
                0.15854503551839705f,  0.3935103426921022f,    0.0829816309488214f,  -0.45175565899994796f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  -0.304684750724869f,  0.5112616136591823f,
                0.0f,                  0.0f,                  -0.290480129728998f,  -0.06578701549142804f,
                0.304684750724884f,    0.2904801297290076f,   0.0f,                 -0.23889773523344604f,
                -0.5112616136592012f,  0.06578701549142545f,  0.23889773523345467f, 0.0f,
            },
            new float[]
            {
                0.0f,                 0.0f,                  0.3017929516615495f,   0.25792362796341184f,
                0.0f,                 0.16272340142866204f,  0.09520022653475037f,  0.0f,
                0.3017929516615503f,  0.09520022653475055f,  -0.16272340142866173f, -0.35312385449816297f,
                0.25792362796341295f, 0.0f,                  -0.3531238544981624f,  -0.6035859033230976f,
            },
            new float[]
            {
                0.0f,                  0.0f, 0.40824829046386274f, 0.0f,
                0.0f,                  0.0f, 0.0f,                 -0.4082482904638628f,
                -0.4082482904638635f,  0.0f, 0.0f,                 -0.40824829046386296f,
                0.0f,                  0.4082482904638634f, 0.408248290463863f, 0.0f,
            },
            new float[]
            {
                0.0f,                  0.0f,                 0.1747866975480809f,   0.0812611176717539f,
                0.0f,                  0.0f,                 -0.3675398009862027f,  -0.307882213957909f,
                -0.17478669754808135f, 0.3675398009862011f,  0.0f,                  0.4826689115059883f,
                -0.08126111767175039f, 0.30788221395790305f, -0.48266891150598584f, 0.0f,
            },
            new float[]
            {
                0.0f,                 0.0f,                  -0.21105601049335784f, 0.18567180916109802f,
                0.0f,                 0.0f,                  0.49215859013738733f,  -0.38525013709251915f,
                0.21105601049335806f, -0.49215859013738905f, 0.0f,                  0.17419412659916217f,
                -0.18567180916109904f, 0.3852501370925211f,  -0.1741941265991621f,  0.0f,
            },
            new float[]
            {
                0.0f,                  0.0f,                 -0.14266084808807264f, -0.3416446842253372f,
                0.0f,                  0.7367497537172237f,  0.24627107722075148f,  -0.08574019035519306f,
                -0.14266084808807344f, 0.24627107722075137f, 0.14883399227113567f,  -0.04768680350229251f,
                -0.3416446842253373f,  -0.08574019035519267f, -0.047686803502292804f, -0.14266084808807242f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  -0.13813540350758585f, 0.3302282550303788f,
                0.0f,                  0.08755115000587084f,  -0.07946706605909573f, -0.4613374887461511f,
                -0.13813540350758294f, -0.07946706605910261f, 0.49724647109535086f,  0.12538059448563663f,
                0.3302282550303805f,   -0.4613374887461554f,  0.12538059448564315f,  -0.13813540350758452f,
            },
            new float[]
            {
                0.0f,                  0.0f,                 -0.17437602599651067f, 0.0702790691196284f,
                0.0f,                  -0.2921026642334881f, 0.3623817333531167f,   0.0f,
                -0.1743760259965108f,  0.36238173335311646f, 0.29210266423348785f,  -0.4326608024727445f,
                0.07027906911962818f,  0.0f,                 -0.4326608024727457f,  0.34875205199302267f,
            },
            new float[]
            {
                0.0f,                  0.0f,                  0.11354987314994337f,  -0.07417504595810355f,
                0.0f,                  0.19402893032594343f,  -0.435190496523228f,   0.21918684838857466f,
                0.11354987314994257f,  -0.4351904965232251f,  0.5550443808910661f,   -0.25468277124066463f,
                -0.07417504595810233f, 0.2191868483885728f,   -0.25468277124066413f, 0.1135498731499429f,
            },
        };

        /// <summary>
        /// Performs a 1D forward DCT along the <paramref name="n"/> rows of width <paramref name="m"/>.
        /// Ported from <c>jxl::DCT1D&lt;N, M&gt;</c> in <c>lib/jxl/dct-inl.h</c>.
        /// </summary>
        /// <param name="n">Transform length along rows (power of two).</param>
        /// <param name="m">Number of columns.</param>
        /// <param name="from">Source block.</param>
        /// <param name="to">Destination block.</param>
        /// <param name="tmp">Scratch buffer of length at least <c>3 * n * m</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Dct1D(int n, int m, DctFrom from, DctTo to, Span<float> tmp)
        {
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && System.Runtime.Intrinsics.X86.Fma.IsSupported &&
                m >= 8 && (n == 4 || n == 8 || n == 16 || n == 32) && !from.Data.Overlaps(to.Data))
            {
                if (n == 8 && m == 8) Forward8BlockSimd(from, to);
                else TransformColumnsSimd(n, m, from, to, false, tmp);
                return;
            }

            LoadFromBlock(n, m, from, tmp);
            Dct1DImpl(n, m, tmp, tmp.Slice(n * m));
            StoreToBlockAndScale(n, m, tmp, to);
        }

        /// <summary>
        /// Performs a 1D inverse DCT along the <paramref name="n"/> rows of width <paramref name="m"/>.
        /// Ported from <c>jxl::IDCT1D&lt;N, M&gt;</c> in <c>lib/jxl/dct-inl.h</c>.
        /// </summary>
        /// <param name="n">Transform length along rows (power of two).</param>
        /// <param name="m">Number of columns.</param>
        /// <param name="from">Source block.</param>
        /// <param name="to">Destination block.</param>
        /// <param name="tmp">Scratch buffer of length at least <c>3 * n * m</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Idct1D(int n, int m, DctFrom from, DctTo to, Span<float> tmp)
        {
            if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && System.Runtime.Intrinsics.X86.Fma.IsSupported &&
                m >= 8 && (n == 4 || n == 8 || n == 16 || n == 32) && !from.Data.Overlaps(to.Data))
            {
                if (n == 8 && m == 8) Inverse8BlockSimd(from, to);
                else TransformColumnsSimd(n, m, from, to, true, tmp);
                return;
            }

            Idct1DImpl(n, m, from.Data, (int)from.Stride, to.Data, (int)to.Stride, tmp);
        }

        /// <summary>
        /// Computes the maybe-transposed, scaled 2D DCT of a <paramref name="rows"/> x <paramref name="cols"/> block.
        /// Ported from <c>jxl::ComputeScaledDCT&lt;ROWS, COLS&gt;</c> in <c>lib/jxl/dct-inl.h</c>.
        /// </summary>
        /// <param name="rows">Number of rows (power of two).</param>
        /// <param name="cols">Number of columns (power of two).</param>
        /// <param name="from">Source block.</param>
        /// <param name="to">Destination contiguous coefficient buffer of length <c>rows * cols</c>.</param>
        /// <param name="scratchSpace">Scratch buffer of length at least <c>4 * rows * cols</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ComputeScaledDCT(int rows, int cols, DctFrom from, Span<float> to, Span<float> scratchSpace)
        {
            Span<float> block = scratchSpace;
            Span<float> tmp   = scratchSpace.Slice(rows * cols);
            nuint uRows       = (nuint)rows;
            nuint uCols       = (nuint)cols;

            if (rows < cols)
            {
                Dct1D(rows, cols, from, new DctTo(block, uCols), tmp);
                DctTranspose.Run(uRows, uCols, new DctFrom(block, uCols), new DctTo(to, uRows));
                Dct1D(cols, rows, new DctFrom(to, uRows), new DctTo(block, uRows), tmp);
                DctTranspose.Run(uCols, uRows, new DctFrom(block, uRows), new DctTo(to, uCols));
            }
            else
            {
                Dct1D(rows, cols, from, new DctTo(to, uCols), tmp);
                DctTranspose.Run(uRows, uCols, new DctFrom(to, uCols), new DctTo(block, uRows));
                Dct1D(cols, rows, new DctFrom(block, uRows), new DctTo(to, uRows), tmp);
            }
        }

        /// <summary>
        /// Computes the maybe-transposed, scaled 2D IDCT of a <paramref name="rows"/> x <paramref name="cols"/> block.
        /// Ported from <c>jxl::ComputeScaledIDCT&lt;ROWS, COLS&gt;</c> in <c>lib/jxl/dct-inl.h</c>.
        /// </summary>
        /// <param name="rows">Number of output pixel rows (power of two).</param>
        /// <param name="cols">Number of output pixel columns (power of two).</param>
        /// <param name="from">Mutable coefficient buffer of length <c>rows * cols</c>.</param>
        /// <param name="to">Destination pixel block.</param>
        /// <param name="scratchSpace">Scratch buffer of length at least <c>4 * rows * cols</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void ComputeScaledIDCT(int rows, int cols, Span<float> from, DctTo to, Span<float> scratchSpace)
        {
            Span<float> block = scratchSpace;
            Span<float> tmp   = scratchSpace.Slice(rows * cols);
            nuint uRows       = (nuint)rows;
            nuint uCols       = (nuint)cols;

            // Reverse the steps done in ComputeScaledDCT.
            if (rows < cols)
            {
                DctTranspose.Run(uRows, uCols, new DctFrom(from, uCols), new DctTo(block, uRows));
                Idct1D(cols, rows, new DctFrom(block, uRows), new DctTo(from, uRows), tmp);
                DctTranspose.Run(uCols, uRows, new DctFrom(from, uRows), new DctTo(block, uCols));
                Idct1D(rows, cols, new DctFrom(block, uCols), to, tmp);
            }
            else
            {
                Idct1D(cols, rows, new DctFrom(from, uRows), new DctTo(block, uRows), tmp);
                DctTranspose.Run(uCols, uRows, new DctFrom(block, uRows), new DctTo(from, uCols));
                Idct1D(rows, cols, new DctFrom(from, uCols), to, tmp);
            }
        }

        /// <summary>
        /// Computes the in-place <paramref name="n"/> x <paramref name="n"/> un-transposed scaled DCT of <paramref name="block"/>.
        /// Ported from <c>ComputeDCT&lt;N&gt;</c> in <c>lib/jxl/dct_test.cc</c>.
        /// </summary>
        /// <param name="n">Square block dimension (power of two).</param>
        /// <param name="block">In-place block of <c>n * n</c> floats.</param>
        public static void ComputeDCT(int n, Span<float> block)
        {
            int blockSize        = n * n;
            float[] tmpBlock     = new float[blockSize];
            float[] scratchSpace = new float[4 * blockSize];
            nuint un             = (nuint)n;

            ComputeScaledDCT(n, n, new DctFrom(block, un), tmpBlock, scratchSpace);
            DctTranspose.Run(un, un, new DctFrom(tmpBlock, un), new DctTo(block, un));
        }

        /// <summary>
        /// Computes the in-place <paramref name="n"/> x <paramref name="n"/> un-transposed scaled IDCT of <paramref name="block"/>.
        /// Ported from <c>ComputeIDCT&lt;N&gt;</c> in <c>lib/jxl/dct_test.cc</c>.
        /// </summary>
        /// <param name="n">Square block dimension (power of two).</param>
        /// <param name="block">In-place block of <c>n * n</c> floats.</param>
        public static void ComputeIDCT(int n, Span<float> block)
        {
            int blockSize        = n * n;
            float[] tmpBlock     = new float[blockSize];
            float[] scratchSpace = new float[4 * blockSize];
            nuint un             = (nuint)n;

            DctTranspose.Run(un, un, new DctFrom(block, un), new DctTo(tmpBlock, un));
            ComputeScaledIDCT(n, n, tmpBlock, new DctTo(block, un), scratchSpace);
        }

        /// <summary>
        /// Computes the 4x4 AFV inverse transform from 16 <paramref name="coeffs"/> into 16 <paramref name="pixels"/>.
        /// Ported from <c>jxl::AFVIDCT4x4</c> in <c>lib/jxl/dec_transforms-inl.h</c>.
        /// </summary>
        /// <param name="coeffs">Span of 16 AFV coefficients.</param>
        /// <param name="pixels">Span of 16 output pixels.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void AFVIDCT4x4(ReadOnlySpan<float> coeffs, Span<float> pixels)
        {
            for (int i = 0; i < 16; i++)
            {
                pixels[i] = 0.0f;
            }

            for (int j = 0; j < 16; j++)
            {
                float cf         = coeffs[j];
                float[] basisRow = Afv4x4Basis[j];

                for (int i = 0; i < 16; i++)
                {
                    pixels[i] = MathF.FusedMultiplyAdd(cf, basisRow[i], pixels[i]);
                }
            }
        }

        /// <summary>
        /// Transforms dequantized frequency <paramref name="coefficients"/> into spatial <paramref name="pixels"/> according to <paramref name="strategy"/>.
        /// Ported from <c>jxl::TransformToPixels</c> in <c>lib/jxl/dec_transforms-inl.h</c>.
        /// </summary>
        /// <param name="strategy">AC strategy type for the block.</param>
        /// <param name="coefficients">Mutable dequantized coefficients buffer.</param>
        /// <param name="pixels">Destination pixel buffer.</param>
        /// <param name="pixelsStride">Row stride of <paramref name="pixels"/> in floats.</param>
        /// <param name="scratchSpace">Scratch buffer of length at least <c>4 * max_block_area</c>.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void TransformToPixels(
            AcStrategyType strategy,
            Span<float> coefficients,
            Span<float> pixels,
            nuint pixelsStride,
            Span<float> scratchSpace)
        {
            switch (strategy)
            {
                case AcStrategyType.IDENTITY:
                    IdentityToPixels(coefficients, pixels, pixelsStride);
                    break;

                case AcStrategyType.DCT8X4:
                    Dct8x4ToPixels(coefficients, pixels, pixelsStride, scratchSpace);
                    break;

                case AcStrategyType.DCT4X8:
                    Dct4x8ToPixels(coefficients, pixels, pixelsStride, scratchSpace);
                    break;

                case AcStrategyType.DCT4X4:
                    Dct4x4ToPixels(coefficients, pixels, pixelsStride, scratchSpace);
                    break;

                case AcStrategyType.DCT2X2:
                    Dct2x2ToPixels(coefficients, pixels, pixelsStride);
                    break;

                case AcStrategyType.AFV0:
                case AcStrategyType.AFV1:
                case AcStrategyType.AFV2:
                case AcStrategyType.AFV3:
                    AFVTransformToPixels((int)strategy - (int)AcStrategyType.AFV0, coefficients, pixels, pixelsStride);
                    break;

                default:
                    AcStrategy acs = AcStrategy.FromRawStrategy(strategy);
                    int rows       = (int)(acs.CoveredBlocksY() * FrameDimensions.kBlockDim);
                    int cols       = (int)(acs.CoveredBlocksX() * FrameDimensions.kBlockDim);

                    ComputeScaledIDCT(rows, cols, coefficients, new DctTo(pixels, pixelsStride), scratchSpace);
                    break;
            }
        }

        /// <summary>
        /// Computes the lowest-frequency (LLF) coefficients in <paramref name="llf"/> from the 8x8 block DC values in <paramref name="dc"/>.
        /// Ported from <c>jxl::LowestFrequenciesFromDC</c> in <c>lib/jxl/dec_transforms-inl.h</c>.
        /// </summary>
        /// <param name="strategy">AC strategy type.</param>
        /// <param name="dc">Span starting at the top-left DC value in the DC image.</param>
        /// <param name="dcStride">Row stride of the DC image in floats.</param>
        /// <param name="llf">Destination LLF coefficient buffer.</param>
        /// <param name="scratch">Scratch buffer for large multiblock strategies.</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void LowestFrequenciesFromDC(
            AcStrategyType strategy,
            ReadOnlySpan<float> dc,
            nuint dcStride,
            Span<float> llf,
            Span<float> scratch)
        {
            AcStrategy acs = AcStrategy.FromRawStrategy(strategy);

            if (!acs.IsMultiblock())
            {
                llf[0] = dc[0];

                return;
            }

            int rows       = (int)acs.CoveredBlocksY();
            int cols       = (int)acs.CoveredBlocksX();
            nuint outCols  = CoeffOrder.CoefficientColumns((nuint)rows, (nuint)cols) * FrameDimensions.kBlockDim;
            DctFrom inFrom = new DctFrom(dc, dcStride);
            DctTo outTo    = new DctTo(llf, outCols);

            if (rows * cols <= 16)
            {
                Span<float> warmBlock   = stackalloc float[16];
                Span<float> warmScratch = stackalloc float[64];

                ReinterpretingDCT(rows, cols, inFrom, outTo, warmBlock, warmScratch);
            }
            else
            {
                ReinterpretingDCT(rows, cols, inFrom, outTo, scratch, scratch.Slice(rows * cols));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void ReinterpretingDCT(
            int rows,
            int cols,
            DctFrom input,
            DctTo output,
            Span<float> block,
            Span<float> scratchSpace)
        {
            int dctRows = rows * (int)FrameDimensions.kBlockDim;
            int dctCols = cols * (int)FrameDimensions.kBlockDim;

            ComputeScaledDCT(rows, cols, input, block, scratchSpace);

            if (rows < cols)
            {
                ReadOnlySpan<float> scaleY = DctScales.GetResampleScales(rows, dctRows);
                ReadOnlySpan<float> scaleX = DctScales.GetResampleScales(cols, dctCols);

                for (int y = 0; y < rows; y++)
                {
                    float sy = scaleY[y];

                    for (int x = 0; x < cols; x++)
                    {
                        output.Write(block[y * cols + x] * sy * scaleX[x], (nuint)y, (nuint)x);
                    }
                }
            }
            else
            {
                ReadOnlySpan<float> scaleY = DctScales.GetResampleScales(cols, dctCols);
                ReadOnlySpan<float> scaleX = DctScales.GetResampleScales(rows, dctRows);

                for (int y = 0; y < cols; y++)
                {
                    float sy = scaleY[y];

                    for (int x = 0; x < rows; x++)
                    {
                        output.Write(block[y * rows + x] * sy * scaleX[x], (nuint)y, (nuint)x);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void IdentityToPixels(ReadOnlySpan<float> coefficients, Span<float> pixels, nuint pixelsStride)
        {
            float block00   = coefficients[0];
            float block01   = coefficients[1];
            float block10   = coefficients[8];
            float block11   = coefficients[9];
            Span<float> dcs = stackalloc float[4];

            dcs[0] = block00 + block01 + block10 + block11;
            dcs[1] = block00 + block01 - block10 - block11;
            dcs[2] = block00 - block01 + block10 - block11;
            dcs[3] = block00 - block01 - block10 + block11;

            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    IdentitySubblockToPixels(y, x, dcs[y * 2 + x], coefficients, pixels, pixelsStride);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void IdentitySubblockToPixels(
            int y,
            int x,
            float blockDc,
            ReadOnlySpan<float> coefficients,
            Span<float> pixels,
            nuint pixelsStride)
        {
            float residualSum = 0.0f;

            for (int iy = 0; iy < 4; iy++)
            {
                for (int ix = 0; ix < 4; ix++)
                {
                    if (ix != 0 || iy != 0)
                    {
                        residualSum += coefficients[(y + iy * 2) * 8 + x + ix * 2];
                    }
                }
            }

            float centerVal = blockDc - residualSum * (1.0f / 16.0f);

            pixels[(int)((nuint)(4 * y + 1) * pixelsStride + (nuint)(4 * x + 1))] = centerVal;

            for (int iy = 0; iy < 4; iy++)
            {
                for (int ix = 0; ix < 4; ix++)
                {
                    if (ix != 1 || iy != 1)
                    {
                        pixels[(int)((nuint)(y * 4 + iy) * pixelsStride + (nuint)(x * 4 + ix))] =
                            coefficients[(y + iy * 2) * 8 + x + ix * 2] + centerVal;
                    }
                }
            }

            pixels[(int)((nuint)(y * 4) * pixelsStride + (nuint)(x * 4))] =
                coefficients[(y + 2) * 8 + x + 2] + centerVal;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Dct8x4ToPixels(
            ReadOnlySpan<float> coefficients,
            Span<float> pixels,
            nuint pixelsStride,
            Span<float> scratchSpace)
        {
            float block0      = coefficients[0];
            float block1      = coefficients[8];
            Span<float> dcs   = stackalloc float[2] { block0 + block1, block0 - block1 };
            Span<float> block = stackalloc float[32];

            for (int x = 0; x < 2; x++)
            {
                ExtractHalfSubblock4x8(x, dcs[x], coefficients, block);
                ComputeScaledIDCT(8, 4, block, new DctTo(pixels.Slice(x * 4), pixelsStride), scratchSpace);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Dct4x8ToPixels(
            ReadOnlySpan<float> coefficients,
            Span<float> pixels,
            nuint pixelsStride,
            Span<float> scratchSpace)
        {
            float block0      = coefficients[0];
            float block1      = coefficients[8];
            Span<float> dcs   = stackalloc float[2] { block0 + block1, block0 - block1 };
            Span<float> block = stackalloc float[32];

            for (int y = 0; y < 2; y++)
            {
                ExtractHalfSubblock4x8(y, dcs[y], coefficients, block);
                ComputeScaledIDCT(4, 8, block, new DctTo(pixels.Slice((int)((nuint)(y * 4) * pixelsStride)), pixelsStride), scratchSpace);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExtractHalfSubblock4x8(int offset, float dcVal, ReadOnlySpan<float> coefficients, Span<float> block)
        {
            block[0] = dcVal;

            for (int iy = 0; iy < 4; iy++)
            {
                for (int ix = 0; ix < 8; ix++)
                {
                    if (ix != 0 || iy != 0)
                    {
                        block[iy * 8 + ix] = coefficients[(offset + iy * 2) * 8 + ix];
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Dct4x4ToPixels(
            ReadOnlySpan<float> coefficients,
            Span<float> pixels,
            nuint pixelsStride,
            Span<float> scratchSpace)
        {
            float block00   = coefficients[0];
            float block01   = coefficients[1];
            float block10   = coefficients[8];
            float block11   = coefficients[9];
            Span<float> dcs = stackalloc float[4]
            {
                block00 + block01 + block10 + block11,
                block00 + block01 - block10 - block11,
                block00 - block01 + block10 - block11,
                block00 - block01 - block10 + block11,
            };
            Span<float> block = stackalloc float[16];

            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    ExtractQuarterSubblock4x4(y, x, dcs[y * 2 + x], coefficients, block);
                    ComputeScaledIDCT(
                        4,
                        4,
                        block,
                        new DctTo(pixels.Slice((int)((nuint)(y * 4) * pixelsStride + (nuint)(x * 4))), pixelsStride),
                        scratchSpace);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExtractQuarterSubblock4x4(
            int y,
            int x,
            float dcVal,
            ReadOnlySpan<float> coefficients,
            Span<float> block)
        {
            block[0] = dcVal;

            for (int iy = 0; iy < 4; iy++)
            {
                for (int ix = 0; ix < 4; ix++)
                {
                    if (ix != 0 || iy != 0)
                    {
                        block[iy * 4 + ix] = coefficients[(y + iy * 2) * 8 + x + ix * 2];
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Dct2x2ToPixels(ReadOnlySpan<float> coefficients, Span<float> pixels, nuint pixelsStride)
        {
            const int kDim     = (int)FrameDimensions.kBlockDim;
            Span<float> coeffs = stackalloc float[(int)FrameDimensions.kDCTBlockSize];

            for (int i = 0; i < (int)FrameDimensions.kDCTBlockSize; i++)
            {
                coeffs[i] = coefficients[i];
            }

            IDCT2TopBlock(2, coeffs, kDim, coeffs);
            IDCT2TopBlock(4, coeffs, kDim, coeffs);
            IDCT2TopBlock(8, coeffs, kDim, coeffs);

            for (int y = 0; y < kDim; y++)
            {
                for (int x = 0; x < kDim; x++)
                {
                    pixels[(int)((nuint)y * pixelsStride + (nuint)x)] = coeffs[y * kDim + x];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void IDCT2TopBlock(int s, ReadOnlySpan<float> block, int strideOut, Span<float> output)
        {
            const int kDim   = (int)FrameDimensions.kBlockDim;
            Span<float> temp = stackalloc float[(int)FrameDimensions.kDCTBlockSize];
            int num2x2       = s / 2;

            for (int y = 0; y < num2x2; y++)
            {
                for (int x = 0; x < num2x2; x++)
                {
                    float c00 = block[y * kDim + x];
                    float c01 = block[y * kDim + num2x2 + x];
                    float c10 = block[(y + num2x2) * kDim + x];
                    float c11 = block[(y + num2x2) * kDim + num2x2 + x];

                    temp[y * 2 * kDim + x * 2]           = c00 + c01 + c10 + c11;
                    temp[y * 2 * kDim + x * 2 + 1]       = c00 + c01 - c10 - c11;
                    temp[(y * 2 + 1) * kDim + x * 2]     = c00 - c01 + c10 - c11;
                    temp[(y * 2 + 1) * kDim + x * 2 + 1] = c00 - c01 - c10 + c11;
                }
            }

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    output[y * strideOut + x] = temp[y * kDim + x];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void AFVTransformToPixels(
            int afvKind,
            ReadOnlySpan<float> coefficients,
            Span<float> pixels,
            nuint pixelsStride)
        {
            Span<float> scratchSpace = stackalloc float[4 * 8 * 4];
            int afvX                 = afvKind & 1;
            int afvY                 = afvKind / 2;
            float block00            = coefficients[0];
            float block01            = coefficients[1];
            float block10            = coefficients[8];
            float dc0                = (block00 + block10 + block01) * 4.0f;
            float dc1                = block00 + block10 - block01;
            float dc2                = block00 - block10;
            Span<float> coeff        = stackalloc float[16];
            Span<float> block        = stackalloc float[32];

            // IAFV: (even, even) positions.
            ExtractQuarterSubblock4x4(0, 0, dc0, coefficients, coeff);
            AFVIDCT4x4(coeff, block);
            WriteAfvCornerSubblock(afvX, afvY, block, pixels, pixelsStride);

            // IDCT4x4 in (odd, even) positions.
            ExtractQuarterSubblock4x4(0, 1, dc1, coefficients, block);
            ComputeScaledIDCT(
                4,
                4,
                block,
                new DctTo(pixels.Slice((int)((nuint)(afvY * 4) * pixelsStride + (nuint)(afvX == 1 ? 0 : 4))), pixelsStride),
                scratchSpace);

            // IDCT4x8.
            ExtractHalfSubblock4x8(1, dc2, coefficients, block);
            ComputeScaledIDCT(
                4,
                8,
                block,
                new DctTo(pixels.Slice((int)((nuint)(afvY == 1 ? 0 : 4) * pixelsStride)), pixelsStride),
                scratchSpace);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteAfvCornerSubblock(
            int afvX,
            int afvY,
            ReadOnlySpan<float> block,
            Span<float> pixels,
            nuint pixelsStride)
        {
            for (int iy = 0; iy < 4; iy++)
            {
                int srcY = afvY == 1 ? 3 - iy : iy;

                for (int ix = 0; ix < 4; ix++)
                {
                    int srcX = afvX == 1 ? 3 - ix : ix;

                    pixels[(int)((nuint)(iy + afvY * 4) * pixelsStride + (nuint)(afvX * 4 + ix))] =
                        block[srcY * 4 + srcX];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Dct1DImpl(int n, int sz, Span<float> mem, Span<float> tmp)
        {
            if (n <= 1)
            {
                return;
            }

            if (n == 2)
            {
                for (int k = 0; k < sz; k++)
                {
                    float in1   = mem[k];
                    float in2   = mem[sz + k];
                    mem[k]      = in1 + in2;
                    mem[sz + k] = in1 - in2;
                }

                return;
            }

            if (n == 4)
            {
                Dct1DFour(sz, mem);

                return;
            }

            int half = n / 2;

            AddReverse(half, sz, mem, mem.Slice(half * sz), tmp);
            Dct1DImpl(half, sz, tmp, tmp.Slice(n * sz));
            SubReverse(half, sz, mem, mem.Slice(half * sz), tmp.Slice(half * sz));
            Multiply(n, sz, tmp);
            Dct1DImpl(half, sz, tmp.Slice(half * sz), tmp.Slice(n * sz));
            B(half, sz, tmp.Slice(half * sz));
            InverseEvenOdd(n, sz, tmp, mem);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Idct1DImpl(
            int n,
            int sz,
            ReadOnlySpan<float> from,
            int fromStride,
            Span<float> to,
            int toStride,
            Span<float> tmp)
        {
            if (n <= 1)
            {
                for (int k = 0; k < sz; k++)
                {
                    to[k] = from[k];
                }

                return;
            }

            if (n == 2)
            {
                for (int k = 0; k < sz; k++)
                {
                    float in1        = from[k];
                    float in2        = from[fromStride + k];
                    to[k]            = in1 + in2;
                    to[toStride + k] = in1 - in2;
                }

                return;
            }

            if (n == 4)
            {
                Idct1DFour(sz, from, fromStride, to, toStride);

                return;
            }

            int half = n / 2;

            ForwardEvenOdd(n, sz, from, fromStride, tmp);
            Idct1DImpl(half, sz, tmp, sz, tmp, sz, tmp.Slice(n * sz));
            BTranspose(half, sz, tmp.Slice(half * sz));
            Idct1DImpl(half, sz, tmp.Slice(half * sz), sz, tmp.Slice(half * sz), sz, tmp.Slice(n * sz));
            MultiplyAndAdd(n, sz, tmp, to, toStride);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddReverse(int n, int sz, ReadOnlySpan<float> aIn1, ReadOnlySpan<float> aIn2, Span<float> aOut)
        {
            for (int i = 0; i < n; i++)
            {
                ReadOnlySpan<float> row1 = aIn1.Slice(i * sz);
                ReadOnlySpan<float> row2 = aIn2.Slice((n - i - 1) * sz);
                Span<float> dst          = aOut.Slice(i * sz);

                for (int k = 0; k < sz; k++)
                {
                    dst[k] = row1[k] + row2[k];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubReverse(int n, int sz, ReadOnlySpan<float> aIn1, ReadOnlySpan<float> aIn2, Span<float> aOut)
        {
            for (int i = 0; i < n; i++)
            {
                ReadOnlySpan<float> row1 = aIn1.Slice(i * sz);
                ReadOnlySpan<float> row2 = aIn2.Slice((n - i - 1) * sz);
                Span<float> dst          = aOut.Slice(i * sz);

                for (int k = 0; k < sz; k++)
                {
                    dst[k] = row1[k] - row2[k];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void B(int n, int sz, Span<float> coeff)
        {
            for (int k = 0; k < sz; k++)
            {
                coeff[k] = MathF.FusedMultiplyAdd(coeff[k], DctScales.kSqrt2, coeff[sz + k]);
            }

            for (int i = 1; i + 1 < n; i++)
            {
                Span<float> row1 = coeff.Slice(i * sz);
                Span<float> row2 = coeff.Slice((i + 1) * sz);

                for (int k = 0; k < sz; k++)
                {
                    row1[k] += row2[k];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BTranspose(int n, int sz, Span<float> coeff)
        {
            for (int i = n - 1; i > 0; i--)
            {
                Span<float> row1 = coeff.Slice(i * sz);
                Span<float> row2 = coeff.Slice((i - 1) * sz);

                for (int k = 0; k < sz; k++)
                {
                    row1[k] += row2[k];
                }
            }

            for (int k = 0; k < sz; k++)
            {
                coeff[k] *= DctScales.kSqrt2;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Dct1DFour(int sz, Span<float> mem)
        {
            const float m0 = 0.541196100146197f;
            const float m1 = 1.3065629648763764f;
            int sz2        = 2 * sz;
            int sz3        = 3 * sz;

            for (int k = 0; k < sz; k++)
            {
                float v0 = mem[k];
                float v1 = mem[sz + k];
                float v2 = mem[sz2 + k];
                float v3 = mem[sz3 + k];

                float a0 = v0 + v3;
                float a1 = v1 + v2;
                float s0 = (v0 - v3) * m0;
                float s1 = (v1 - v2) * m1;
                float d0 = s0 + s1;
                float d1 = s0 - s1;

                mem[k]       = a0 + a1;
                mem[sz + k]  = MathF.FusedMultiplyAdd(d0, DctScales.kSqrt2, d1);
                mem[sz2 + k] = a0 - a1;
                mem[sz3 + k] = d1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Idct1DFour(
            int sz,
            ReadOnlySpan<float> from,
            int fromStride,
            Span<float> to,
            int toStride)
        {
            const float m0 = 0.541196100146197f;
            const float m1 = 1.3065629648763764f;
            int fs2        = 2 * fromStride;
            int fs3        = 3 * fromStride;
            int ts2        = 2 * toStride;
            int ts3        = 3 * toStride;

            for (int k = 0; k < sz; k++)
            {
                float f0 = from[k];
                float f1 = from[fromStride + k];
                float f2 = from[fs2 + k];
                float f3 = from[fs3 + k];

                float e0 = f0 + f2;
                float e1 = f0 - f2;
                float b1 = f3 + f1;
                float b0 = f1 * DctScales.kSqrt2;
                float o0 = b0 + b1;
                float o1 = b0 - b1;

                to[k]            = MathF.FusedMultiplyAdd(m0, o0, e0);
                to[toStride + k] = MathF.FusedMultiplyAdd(m1, o1, e1);
                to[ts2 + k]      = MathF.FusedMultiplyAdd(-m1, o1, e1);
                to[ts3 + k]      = MathF.FusedMultiplyAdd(-m0, o0, e0);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InverseEvenOdd(int n, int sz, ReadOnlySpan<float> aIn, Span<float> aOut)
        {
            int half = n / 2;

            for (int i = 0; i < half; i++)
            {
                aIn.Slice(i * sz, sz).CopyTo(aOut.Slice((2 * i) * sz, sz));
            }

            for (int i = half; i < n; i++)
            {
                aIn.Slice(i * sz, sz).CopyTo(aOut.Slice((2 * (i - half) + 1) * sz, sz));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ForwardEvenOdd(int n, int sz, ReadOnlySpan<float> aIn, int aInStride, Span<float> aOut)
        {
            int half = n / 2;

            for (int i = 0; i < half; i++)
            {
                aIn.Slice((2 * i) * aInStride, sz).CopyTo(aOut.Slice(i * sz, sz));
            }

            for (int i = half; i < n; i++)
            {
                aIn.Slice((2 * (i - half) + 1) * aInStride, sz).CopyTo(aOut.Slice(i * sz, sz));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Multiply(int n, int sz, Span<float> coeff)
        {
            int half                        = n / 2;
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(n);

            for (int i = 0; i < half; i++)
            {
                float mul       = multipliers[i];
                Span<float> row = coeff.Slice((half + i) * sz);

                for (int k = 0; k < sz; k++)
                {
                    row[k] *= mul;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MultiplyAndAdd(int n, int sz, ReadOnlySpan<float> coeff, Span<float> output, int outStride)
        {
            int half                        = n / 2;
            ReadOnlySpan<float> multipliers = DctScales.GetWcMultipliers(n);

            for (int i = 0; i < half; i++)
            {
                float mul               = multipliers[i];
                ReadOnlySpan<float> in1 = coeff.Slice(i * sz);
                ReadOnlySpan<float> in2 = coeff.Slice((half + i) * sz);
                Span<float> out1        = output.Slice(i * outStride);
                Span<float> out2        = output.Slice((n - i - 1) * outStride);

                for (int k = 0; k < sz; k++)
                {
                    float v1 = in1[k];
                    float v2 = in2[k];
                    out1[k]  = MathF.FusedMultiplyAdd(mul, v2, v1);
                    out2[k]  = MathF.FusedMultiplyAdd(-mul, v2, v1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LoadFromBlock(int n, int sz, DctFrom from, Span<float> coeff)
        {
            for (int i = 0; i < n; i++)
            {
                from.Slice((nuint)i, 0).Slice(0, sz).CopyTo(coeff.Slice(i * sz, sz));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreToBlockAndScale(int n, int sz, ReadOnlySpan<float> coeff, DctTo to)
        {
            float mul = 1.0f / n;

            for (int i = 0; i < n; i++)
            {
                ReadOnlySpan<float> src = coeff.Slice(i * sz);
                Span<float> dst         = to.Slice((nuint)i, 0);

                for (int k = 0; k < sz; k++)
                {
                    dst[k] = src[k] * mul;
                }
            }
        }
    }
}
