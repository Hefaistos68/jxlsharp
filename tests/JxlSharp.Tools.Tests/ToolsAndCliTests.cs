// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using JxlSharp.Base;
using JxlSharp.Decoder;
using JxlSharp.Extras;
using JxlSharp.TestLib;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Tools.Tests
{
    /// <summary>
    /// Unit tests for <see cref="GaussBlur"/> and <see cref="RecursiveGaussian"/>.
    /// Ported from <c>tools/gauss_blur_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class GaussBlurTests
    {
        private static readonly float[] SignTestCenter = new float[33 * 33]
        {
            -0.128445f, -0.098473f, -0.121883f, -0.093601f, 0.095665f,  -0.271332f,
            -0.705475f, -1.324005f, -2.020741f, -1.329464f, 1.834064f,  4.787300f,
            5.834560f,  5.272720f,  3.967960f,  3.547935f,  3.432732f,  3.383015f,
            3.239326f,  3.290806f,  3.298954f,  3.397808f,  3.359730f,  3.533844f,
            3.511856f,  3.436787f,  3.428310f,  3.460209f,  3.550011f,  3.590942f,
            3.593109f,  3.560005f,  3.443165f,  0.089741f,  0.179230f,  -0.032997f,
            -0.182610f, 0.005669f,  -0.244759f, -0.395123f, -0.514961f, -1.003529f,
            -1.798656f, -2.377975f, 0.222191f,  3.957664f,  5.946804f,  5.543129f,
            4.290096f,  3.621010f,  3.407257f,  3.392494f,  3.345367f,  3.391903f,
            3.441605f,  3.429260f,  3.444969f,  3.507130f,  3.518612f,  3.443111f,
            3.475948f,  3.536148f,  3.470333f,  3.628311f,  3.600243f,  3.292892f,
            -0.226730f, -0.573616f, -0.762165f, -0.398739f, -0.189842f, -0.275921f,
            -0.446739f, -0.550037f, -0.461033f, -0.724792f, -1.448349f, -1.814064f,
            -0.491032f, 2.817703f,  5.213242f,  5.675629f,  4.864548f,  3.876324f,
            3.535587f,  3.530312f,  3.413765f,  3.386261f,  3.404854f,  3.383472f,
            3.420830f,  3.326496f,  3.257877f,  3.362152f,  3.489609f,  3.619587f,
            3.555805f,  3.423164f,  3.309708f,  -0.483940f, -0.502926f, -0.592983f,
            -0.492527f, -0.413616f, -0.482555f, -0.475506f, -0.447990f, -0.338120f,
            -0.189072f, -0.376427f, -0.910828f, -1.878044f, -1.937927f, 1.423218f,
            4.871609f,  5.767548f,  5.103741f,  3.983868f,  3.633003f,  3.458263f,
            3.507309f,  3.247021f,  3.220612f,  3.326061f,  3.352814f,  3.291061f,
            3.322739f,  3.444302f,  3.506207f,  3.556839f,  3.529575f,  3.457024f,
            -0.408161f, -0.431343f, -0.454369f, -0.356419f, -0.380924f, -0.399452f,
            -0.439476f, -0.412189f, -0.306816f, -0.008213f, -0.325813f, -0.537842f,
            -0.984100f, -1.805332f, -2.028198f, 0.773205f,  4.423046f,  5.604839f,
            5.231617f,  4.080299f,  3.603008f,  3.498741f,  3.517010f,  3.333897f,
            3.381336f,  3.342617f,  3.369686f,  3.434155f,  3.490452f,  3.607029f,
            3.555298f,  3.702297f,  3.618679f,  -0.503609f, -0.578564f, -0.419014f,
            -0.239883f, 0.269836f,  0.022984f,  -0.455067f, -0.621777f, -0.304176f,
            -0.163792f, -0.490250f, -0.466637f, -0.391792f, -0.657940f, -1.498035f,
            -1.895836f, 0.036537f,  3.462456f,  5.586445f,  5.658791f,  4.434784f,
            3.423435f,  3.318848f,  3.202328f,  3.532764f,  3.436687f,  3.354881f,
            3.356941f,  3.382645f,  3.503902f,  3.512867f,  3.632366f,  3.537312f,
            -0.274734f, -0.658829f, -0.726532f, -0.281254f, 0.053196f,  -0.064991f,
            -0.608517f, -0.720966f, -0.070602f, -0.111320f, -0.440956f, -0.492180f,
            -0.488762f, -0.569283f, -1.012741f, -1.582779f, -2.101479f, -1.392380f,
            2.451153f,  5.555855f,  6.096313f,  5.230045f,  4.068172f,  3.404274f,
            3.392586f,  3.326065f,  3.156670f,  3.284828f,  3.347012f,  3.319252f,
            3.352310f,  3.610790f,  3.499847f,  -0.150600f, -0.314445f, -0.093575f,
            -0.057384f, 0.053688f,  -0.189255f, -0.263515f, -0.318653f, 0.053246f,
            0.080627f,  -0.119553f, -0.152454f, -0.305420f, -0.404869f, -0.385944f,
            -0.689949f, -1.204914f, -1.985748f, -1.711361f, 1.260658f,  4.626896f,
            5.888351f,  5.450989f,  4.070587f,  3.539200f,  3.383492f,  3.296318f,
            3.267334f,  3.436028f,  3.463005f,  3.502625f,  3.522282f,  3.403763f,
            -0.348049f, -0.302303f, -0.137016f, -0.041737f, -0.164001f, -0.358849f,
            -0.469627f, -0.428291f, -0.375797f, -0.246346f, -0.118950f, -0.084229f,
            -0.205681f, -0.241199f, -0.391796f, -0.323151f, -0.241211f, -0.834137f,
            -1.684219f, -1.972137f, 0.448399f,  4.019985f,  5.648144f,  5.647846f,
            4.295094f,  3.641884f,  3.374790f,  3.197342f,  3.425545f,  3.507481f,
            3.478065f,  3.430889f,  3.341900f,  -1.016304f, -0.959221f, -0.909466f,
            -0.810715f, -0.590729f, -0.594467f, -0.646721f, -0.629364f, -0.528561f,
            -0.551819f, -0.301086f, -0.149101f, -0.060146f, -0.162220f, -0.326210f,
            -0.156548f, -0.036293f, -0.426098f, -1.145470f, -1.628998f, -2.003052f,
            -1.142891f, 2.885162f,  5.652863f,  5.718426f,  4.911140f,  3.234222f,
            3.473373f,  3.577183f,  3.271603f,  3.410435f,  3.505489f,  3.434032f,
            -0.508911f, -0.438797f, -0.437450f, -0.627426f, -0.511745f, -0.304874f,
            -0.274246f, -0.261841f, -0.228466f, -0.342491f, -0.528206f, -0.490082f,
            -0.516350f, -0.361694f, -0.398514f, -0.276020f, -0.210369f, -0.355938f,
            -0.402622f, -0.538864f, -1.249573f, -2.100105f, -0.996178f, 1.886410f,
            4.929745f,  5.630871f,  5.444199f,  4.042740f,  3.739189f,  3.691399f,
            3.391956f,  3.469696f,  3.431232f,  0.204849f,  0.205433f,  -0.131927f,
            -0.367908f, -0.374378f, -0.126820f, -0.186951f, -0.228565f, -0.081776f,
            -0.143143f, -0.379230f, -0.598701f, -0.458019f, -0.295586f, -0.407730f,
            -0.245853f, -0.043140f, 0.024242f,  -0.038998f, -0.044151f, -0.425991f,
            -1.240753f, -1.943146f, -2.174755f, 0.523415f,  4.376751f,  5.956558f,
            5.850082f,  4.403152f,  3.517399f,  3.560753f,  3.554836f,  3.471985f,
            -0.508503f, -0.109783f, 0.057747f,  0.190079f,  -0.257153f, -0.591980f,
            -0.666771f, -0.525391f, -0.293060f, -0.489731f, -0.304855f, -0.259644f,
            -0.367825f, -0.346977f, -0.292889f, -0.215652f, -0.120705f, -0.176010f,
            -0.422905f, -0.114647f, -0.289749f, -0.374203f, -0.606754f, -1.127949f,
            -1.994583f, -0.588058f, 3.415840f,  5.603470f,  5.811581f,  4.959423f,
            3.721760f,  3.710499f,  3.785461f,  -0.554588f, -0.565517f, -0.434578f,
            -0.012482f, -0.284660f, -0.699795f, -0.957535f, -0.755135f, -0.382034f,
            -0.321552f, -0.287571f, -0.279537f, -0.314972f, -0.256287f, -0.372818f,
            -0.316017f, -0.287975f, -0.365639f, -0.512589f, -0.420692f, -0.436485f,
            -0.295353f, -0.451958f, -0.755459f, -1.272358f, -2.301353f, -1.776161f,
            1.572483f,  4.826286f,  5.741898f,  5.162853f,  4.028049f,  3.686325f,
            -0.495590f, -0.664413f, -0.760044f, -0.152634f, -0.286480f, -0.340462f,
            0.076477f,  0.187706f,  -0.068787f, -0.293491f, -0.361145f, -0.292515f,
            -0.140671f, -0.190723f, -0.333302f, -0.368168f, -0.192581f, -0.154499f,
            -0.236544f, -0.124405f, -0.208321f, -0.465607f, -0.883080f, -1.104813f,
            -1.210567f, -1.415665f, -1.924683f, -1.634758f, 0.601017f,  4.276672f,
            5.501350f,  5.331257f,  3.809288f,  -0.727722f, -0.533619f, -0.511524f,
            -0.470688f, -0.610710f, -0.575130f, -0.311115f, -0.090420f, -0.297676f,
            -0.646118f, -0.742805f, -0.485050f, -0.330910f, -0.275417f, -0.357037f,
            -0.425598f, -0.481876f, -0.488941f, -0.393551f, -0.051105f, -0.090755f,
            -0.328674f, -0.536369f, -0.533684f, -0.336960f, -0.689194f, -1.187195f,
            -1.860954f, -2.290253f, -0.424774f, 3.050060f,  5.083332f,  5.291920f,
            -0.343605f, -0.190975f, -0.303692f, -0.456512f, -0.681820f, -0.690693f,
            -0.416729f, -0.286446f, -0.442055f, -0.709148f, -0.569160f, -0.382423f,
            -0.402321f, -0.383362f, -0.366413f, -0.290718f, -0.110069f, -0.220280f,
            -0.279018f, -0.255424f, -0.262081f, -0.487556f, -0.444492f, -0.250500f,
            -0.119583f, -0.291557f, -0.537781f, -1.104073f, -1.737091f, -1.697441f,
            -0.323456f, 2.042049f,  4.605103f,  -0.310631f, -0.279568f, -0.012695f,
            -0.160130f, -0.358746f, -0.421101f, -0.559677f, -0.474136f, -0.416565f,
            -0.561817f, -0.534672f, -0.519157f, -0.767197f, -0.605831f, -0.186523f,
            0.219872f,  0.264984f,  -0.193432f, -0.363182f, -0.467472f, -0.462009f,
            -0.571053f, -0.522476f, -0.315903f, -0.237427f, -0.147320f, -0.100201f,
            -0.237568f, -0.763435f, -1.242043f, -2.135159f, -1.409485f, 1.236370f,
            -0.474247f, -0.517906f, -0.410217f, -0.542244f, -0.795986f, -0.590004f,
            -0.388863f, -0.462921f, -0.810627f, -0.778637f, -0.512486f, -0.718025f,
            -0.710854f, -0.482513f, -0.318233f, -0.194962f, -0.220116f, -0.421673f,
            -0.534233f, -0.403339f, -0.389332f, -0.407303f, -0.437355f, -0.469730f,
            -0.359600f, -0.352745f, -0.466755f, -0.414585f, -0.430756f, -0.656822f,
            -1.237038f, -2.046097f, -1.574898f, -0.593815f, -0.582165f, -0.336098f,
            -0.372612f, -0.554386f, -0.410603f, -0.428276f, -0.647644f, -0.640720f,
            -0.582207f, -0.414112f, -0.435547f, -0.435505f, -0.332561f, -0.248116f,
            -0.340221f, -0.277855f, -0.352699f, -0.377319f, -0.230850f, -0.313267f,
            -0.446270f, -0.346237f, -0.420422f, -0.530781f, -0.400341f, -0.463661f,
            -0.209091f, -0.056705f, -0.011772f, -0.169388f, -0.736275f, -1.463017f,
            -0.752701f, -0.668865f, -0.329765f, -0.299347f, -0.245667f, -0.286999f,
            -0.520420f, -0.675438f, -0.255753f, 0.141357f,  -0.079639f, -0.419476f,
            -0.374069f, -0.046253f, 0.116116f,  -0.145847f, -0.380371f, -0.563412f,
            -0.638634f, -0.310116f, -0.260914f, -0.508404f, -0.465508f, -0.527824f,
            -0.370979f, -0.305595f, -0.244694f, -0.254490f, 0.009968f,  -0.050201f,
            -0.331219f, -0.614960f, -0.788208f, -0.483242f, -0.367516f, -0.186951f,
            -0.180031f, 0.129711f,  -0.127811f, -0.384750f, -0.499542f, -0.418613f,
            -0.121635f, 0.203197f,  -0.167290f, -0.397270f, -0.355461f, -0.218746f,
            -0.376785f, -0.521698f, -0.721581f, -0.845741f, -0.535439f, -0.220882f,
            -0.309067f, -0.555248f, -0.690342f, -0.664948f, -0.390102f, 0.020355f,
            -0.130447f, -0.173252f, -0.170059f, -0.633663f, -0.956001f, -0.621696f,
            -0.388302f, -0.342262f, -0.244370f, -0.386948f, -0.401421f, -0.172979f,
            -0.206163f, -0.450058f, -0.525789f, -0.549274f, -0.349251f, -0.474613f,
            -0.667976f, -0.435600f, -0.175369f, -0.196877f, -0.202976f, -0.242481f,
            -0.258369f, -0.189133f, -0.395397f, -0.765499f, -0.944016f, -0.850967f,
            -0.631561f, -0.152493f, -0.046432f, -0.262066f, -0.195919f, 0.048218f,
            0.084972f,  0.039902f,  0.000618f,  -0.404430f, -0.447456f, -0.418076f,
            -0.631935f, -0.717415f, -0.502888f, -0.530514f, -0.747826f, -0.704041f,
            -0.674969f, -0.516853f, -0.418446f, -0.327740f, -0.308815f, -0.481636f,
            -0.440083f, -0.481720f, -0.341053f, -0.283897f, -0.324368f, -0.352829f,
            -0.434349f, -0.545589f, -0.533104f, -0.472755f, -0.570496f, -0.557735f,
            -0.708176f, -0.493332f, -0.194416f, -0.186249f, -0.256710f, -0.271835f,
            -0.304752f, -0.431267f, -0.422398f, -0.646725f, -0.680801f, -0.249031f,
            -0.058567f, -0.213890f, -0.383949f, -0.540291f, -0.549877f, -0.225567f,
            -0.037174f, -0.499874f, -0.641010f, -0.628044f, -0.390549f, -0.311497f,
            -0.542313f, -0.569565f, -0.473408f, -0.331245f, -0.357197f, -0.285599f,
            -0.200157f, -0.201866f, -0.124428f, -0.346016f, -0.392311f, -0.264496f,
            -0.285370f, -0.436974f, -0.523483f, -0.410461f, -0.267925f, -0.055016f,
            -0.382458f, -0.319771f, -0.049927f, 0.124329f,  0.266102f,  -0.106606f,
            -0.773647f, -0.973053f, -0.708206f, -0.486137f, -0.319923f, -0.493900f,
            -0.490860f, -0.324986f, -0.147346f, -0.146088f, -0.161758f, -0.084396f,
            -0.379494f, 0.041626f,  -0.113361f, -0.277767f, 0.083366f,  0.126476f,
            0.139057f,  0.038040f,  0.038162f,  -0.242126f, -0.411736f, -0.370049f,
            -0.455357f, -0.039257f, 0.264442f,  -0.271492f, -0.425346f, -0.514847f,
            -0.448650f, -0.580399f, -0.652603f, -0.774803f, -0.692524f, -0.579578f,
            -0.465206f, -0.386265f, -0.458012f, -0.446594f, -0.284893f, -0.345448f,
            -0.350876f, -0.440350f, -0.360378f, -0.270428f, 0.237213f,  -0.063602f,
            -0.364529f, -0.179867f, 0.078197f,  0.117947f,  -0.093410f, -0.359119f,
            -0.480961f, -0.540638f, -0.436287f, -0.598576f, -0.253735f, -0.060093f,
            -0.549145f, -0.808327f, -0.698593f, -0.595764f, -0.582508f, -0.497353f,
            -0.480892f, -0.584240f, -0.665791f, -0.690903f, -0.743446f, -0.796677f,
            -0.782391f, -0.649010f, -0.628139f, -0.880848f, -0.829361f, -0.373272f,
            -0.223667f, 0.174572f,  -0.348743f, -0.798901f, -0.692307f, -0.607609f,
            -0.401455f, -0.480919f, -0.450798f, -0.435413f, -0.322338f, -0.228382f,
            -0.450466f, -0.504440f, -0.477402f, -0.662224f, -0.583397f, -0.217445f,
            -0.157459f, -0.079584f, -0.226168f, -0.488720f, -0.669624f, -0.666878f,
            -0.565311f, -0.549625f, -0.364601f, -0.497627f, -0.736897f, -0.763023f,
            -0.741020f, -0.404503f, 0.184814f,  -0.075315f, -0.281513f, -0.532906f,
            -0.405800f, -0.313438f, -0.536652f, -0.403381f, 0.011967f,  0.103310f,
            -0.269848f, -0.508656f, -0.445923f, -0.644859f, -0.617870f, -0.500927f,
            -0.371559f, -0.125580f, 0.028625f,  -0.154713f, -0.442024f, -0.492764f,
            -0.199371f, 0.236305f,  0.225925f,  0.075577f,  -0.285812f, -0.437145f,
            -0.374260f, -0.156693f, -0.129635f, -0.243206f, -0.123058f, 0.162148f,
            -0.313152f, -0.337982f, -0.358421f, 0.040070f,  0.038925f,  -0.333313f,
            -0.351662f, 0.023014f,  0.091362f,  -0.282890f, -0.373253f, -0.389050f,
            -0.532707f, -0.423347f, -0.349968f, -0.287045f, -0.202442f, -0.308430f,
            -0.222801f, -0.106323f, -0.056358f, 0.027222f,  0.390732f,  0.033558f,
            -0.160088f, -0.382217f, -0.535282f, -0.515900f, -0.022736f, 0.165665f,
            -0.111408f, -0.233784f, -0.312357f, -0.541885f, -0.480022f, -0.482513f,
            -0.246254f, 0.132244f,  0.090134f,  0.234634f,  -0.089249f, -0.460854f,
            -0.515457f, -0.450874f, -0.311031f, -0.387680f, -0.360554f, -0.179241f,
            -0.283817f, -0.475815f, -0.246399f, -0.388958f, -0.551140f, -0.496239f,
            -0.559879f, -0.379761f, -0.254288f, -0.395111f, -0.613018f, -0.459427f,
            -0.263580f, -0.268929f, 0.080826f,  0.115616f,  -0.097324f, -0.325310f,
            -0.480450f, -0.313286f, -0.310371f, -0.517361f, -0.288288f, -0.112679f,
            -0.173241f, -0.221664f, -0.039452f, -0.107578f, -0.089630f, -0.483768f,
            -0.571087f, -0.497108f, -0.321533f, -0.375492f, -0.540363f, -0.406815f,
            -0.388512f, -0.514561f, -0.540192f, -0.402412f, -0.232246f, -0.304749f,
            -0.383724f, -0.679596f, -0.685463f, -0.694538f, -0.642937f, -0.425789f,
            0.103271f,  -0.194862f, -0.487999f, -0.717281f, -0.681850f, -0.709286f,
            -0.615398f, -0.554245f, -0.254681f, -0.049950f, -0.002914f, -0.095383f,
            -0.370911f, -0.564224f, -0.242714f,
        };

        /// <summary>
        /// Ported from <c>GaussBlurTest.ImpulseResponse</c>.
        /// </summary>
        [Test]
        public void ImpulseResponse()
        {
            TestImpulseResponseForWidth(10);
            TestImpulseResponseForWidth(15);
            TestImpulseResponseForWidth(32);
            TestImpulseResponseForWidth(31);
            TestImpulseResponseForWidth(33);
        }

        /// <summary>
        /// Ported from <c>GaussBlurTest.Test2D</c>.
        /// </summary>
        [Test]
        public void Test2D()
        {
            int[] dimensions = new int[] { 6, 15, 17, 64, 50, 49 };
            double[] sigmas  = new double[] { 1.0, 2.5, 3.6, 7.0 };

            foreach (int xsize in dimensions)
            {
                foreach (int ysize in dimensions)
                {
                    foreach (double sigma in sigmas)
                    {
                        TestDirac2D((nuint)xsize, (nuint)ysize, sigma);
                    }
                }
            }
        }

        /// <summary>
        /// Ported from <c>GaussBlurTest.TestRandom</c>.
        /// </summary>
        [Test]
        public void TestRandom()
        {
            TestRandomForSizes(0.0f, 10.0f, 3.0);
            TestRandomForSizes(0.0f, 10.0f, 7.0);
            TestRandomForSizes(-4.0f, -1.0f, 3.0);
            TestRandomForSizes(-4.0f, -1.0f, 7.0);
            TestRandomForSizes(-6.0f, 6.0f, 3.0);
            TestRandomForSizes(-6.0f, 6.0f, 7.0);
        }

        /// <summary>
        /// Ported from <c>GaussBlurTest.TestSign</c>.
        /// </summary>
        [Test]
        public void TestSign()
        {
            JxlMemoryManager memoryManager = TestMemoryManager.MemoryManager();
            const nuint xsize              = 500;
            const nuint ysize              = 606;
            using ImagePlane<float> input  = ImagePlane<float>.Create(memoryManager, xsize, ysize).Value!;
            using ImagePlane<float> temp   = ImagePlane<float>.Create(memoryManager, xsize, ysize).Value!;
            using ImagePlane<float> outRg  = ImagePlane<float>.Create(memoryManager, xsize, ysize).Value!;

            ImageOps.ZeroFillImage(input);

            const nint xtest = (nint)(xsize / 2);
            const nint ytest = (nint)(ysize / 2);

            for (nint dy = -16; dy <= 16; ++dy)
            {
                Span<float> row = input.Row((nuint)(ytest + dy));

                for (nint dx = -16; dx <= 16; ++dx)
                {
                    row[(int)(xtest + dx)] = SignTestCenter[(dy + 16) * 33 + (dx + 16)];
                }
            }

            const double sigma   = 7.155933;
            RecursiveGaussian rg = GaussBlur.CreateRecursiveGaussian(sigma);
            Status status        = GaussBlur.FastGaussian(memoryManager, rg, input, temp, outRg);

            status.IsOk.ShouldBeTrue();

            float[] kernel                 = GaussianKernel((int)(4 * sigma), (float)sigma);
            using ImagePlane<float> outOld = Convolve(input, kernel);

            double rgCenter  = outRg.Row((nuint)ytest)[(int)xtest];
            double oldCenter = outOld.Row((nuint)ytest)[(int)xtest];

            rgCenter.ShouldBe(oldCenter, 1e-2);
        }

        private static bool NearEdge(long width, long peak)
        {
            return peak < 10 || peak > width - 10;
        }

        private static void VerifySymmetric(long width, long peak, ReadOnlySpan<float> output)
        {
            double tolerance = NearEdge(width, peak) ? 0.015 : 6E-7;

            for (long i = 1; ; ++i)
            {
                if (peak - i < 0 || peak + i >= width)
                {
                    break;
                }

                ((double)output[(int)(peak + i - 1)] + tolerance).ShouldBeGreaterThan((double)output[(int)(peak + i)]);
                ((double)output[(int)(peak - i)]).ShouldBe((double)output[(int)(peak + i)], tolerance);
            }
        }

        private static void TestImpulseResponse(nuint width, nuint peak)
        {
            RecursiveGaussian rg3 = GaussBlur.CreateRecursiveGaussian(3.0);
            RecursiveGaussian rg4 = GaussBlur.CreateRecursiveGaussian(4.0);
            RecursiveGaussian rg5 = GaussBlur.CreateRecursiveGaussian(5.0);

            float[] input = new float[36];
            float[] out3  = new float[36];
            float[] out4  = new float[36];
            float[] out5  = new float[36];

            input[peak] = 1.0f;

            GaussBlur.FastGaussian1D(rg3, width, input, out3);
            GaussBlur.FastGaussian1D(rg4, width, out3, out4);
            GaussBlur.FastGaussian1D(rg5, width, input, out5);

            VerifySymmetric((long)width, (long)peak, out3);
            VerifySymmetric((long)width, (long)peak, out4);
            VerifySymmetric((long)width, (long)peak, out5);

            ((double)out5[peak] + 0.05).ShouldBeLessThan((double)out3[peak]);

            double tolerance = NearEdge((long)width, (long)peak) ? 0.04 : 0.01;

            for (nuint i = 0; i < width; ++i)
            {
                ((double)out4[i]).ShouldBe((double)out5[i], tolerance);
            }
        }

        private static void TestImpulseResponseForWidth(nuint width)
        {
            for (nuint i = 0; i < width; ++i)
            {
                TestImpulseResponse(width, i);
            }
        }

        private static float[] GaussianKernel(int radius, float sigma)
        {
            float[] kernel = new float[2 * radius + 1];
            float scaler   = -1.0f / (2.0f * sigma * sigma);
            double sum     = 0.0;

            for (int i = -radius; i <= radius; ++i)
            {
                float val           = MathF.Exp(scaler * i * i);
                kernel[i + radius]  = val;
                sum                += val;
            }

            for (int i = 0; i < kernel.Length; ++i)
            {
                kernel[i] = (float)(kernel[i] / sum);
            }

            return kernel;
        }

        private static void ExtrapolateBorders(ReadOnlySpan<float> rowIn, Span<float> rowTmp, int xsize, int radius)
        {
            int lastCol = xsize - 1;

            for (int x = 1; x <= radius; ++x)
            {
                rowTmp[radius - x] = rowIn[Math.Min(x, xsize - 1)];
            }

            rowIn.Slice(0, xsize).CopyTo(rowTmp.Slice(radius, xsize));

            for (int x = 1; x <= radius; ++x)
            {
                rowTmp[radius + lastCol + x] = rowIn[Math.Max(0, lastCol - x)];
            }
        }

        private static ImagePlane<float> ConvolveXAndTranspose(ImagePlane<float> input, float[] kernel)
        {
            JxlMemoryManager memoryManager = TestMemoryManager.MemoryManager();
            ImagePlane<float> output       = ImagePlane<float>.Create(memoryManager, input.YSize, input.XSize).Value!;
            int r                          = kernel.Length / 2;
            int xsize                      = (int)input.XSize;
            float[] rowTmp                 = new float[xsize + 2 * r];

            for (nuint y = 0; y < input.YSize; ++y)
            {
                ExtrapolateBorders(input.ConstRow(y), rowTmp, xsize, r);

                for (int x = 0; x < xsize; ++x)
                {
                    output.Row((nuint)x)[(int)y] = ComputeConvolvedSample(rowTmp, kernel, x, r, xsize);
                }
            }

            return output;
        }

        private static float ComputeConvolvedSample(ReadOnlySpan<float> rowTmp, ReadOnlySpan<float> kernel, int x, int r, int xsize)
        {
            float sum     = 0.0f;
            bool nearEdge = x < r || x + r >= xsize;

            for (int i = -r; i <= r; ++i)
            {
                int idx = nearEdge ? Math.Max(0, Math.Min(x + i, xsize)) : (x + i);
                sum    += rowTmp[r + idx] * kernel[r + i];
            }

            return sum;
        }

        private static ImagePlane<float> Convolve(ImagePlane<float> input, float[] kernel)
        {
            using ImagePlane<float> tmp = ConvolveXAndTranspose(input, kernel);

            return ConvolveXAndTranspose(tmp, kernel);
        }

        private static void TestDirac2D(nuint xsize, nuint ysize, double sigma)
        {
            JxlMemoryManager memoryManager = TestMemoryManager.MemoryManager();
            using ImagePlane<float> input  = ImagePlane<float>.Create(memoryManager, xsize, ysize).Value!;
            using ImagePlane<float> temp   = ImagePlane<float>.Create(memoryManager, xsize, ysize).Value!;
            using ImagePlane<float> output = ImagePlane<float>.Create(memoryManager, xsize, ysize).Value!;

            ImageOps.ZeroFillImage(input);
            input.Row(ysize / 2)[(int)(xsize / 2)] = 1.0f;

            RecursiveGaussian rg = GaussBlur.CreateRecursiveGaussian(sigma);
            Status status        = GaussBlur.FastGaussian(
                memoryManager,
                rg,
                xsize,
                ysize,
                y => input.ConstRow(y),
                y => temp.Row(y),
                y => output.Row(y));

            status.IsOk.ShouldBeTrue();

            float[] kernel                   = GaussianKernel((int)(4 * sigma), (float)sigma);
            using ImagePlane<float> expected = Convolve(input, kernel);
            double maxL1                     = sigma < 1.5 ? 5E-3 : 6E-4;
            nint border                      = (nint)(2 * sigma);

            ImageTestUtils.VerifyRelativeError(expected, output, maxL1, 1E-8, border);
        }

        private static void SetBorder(nuint thickness, float value, ImagePlane<float> image)
        {
            nuint xsize = image.XSize;
            nuint ysize = image.YSize;
            nuint top   = Math.Min(thickness, ysize);

            for (nuint y = 0; y < top; ++y)
            {
                image.Row(y).Slice(0, (int)xsize).Fill(value);
            }

            nuint bottomStart = ysize > thickness ? ysize - thickness : 0;

            for (nuint y = bottomStart; y < ysize; ++y)
            {
                image.Row(y).Slice(0, (int)xsize).Fill(value);
            }

            if (ysize >= 2 * thickness && xsize >= thickness)
            {
                for (nuint y = thickness; y < ysize - thickness; ++y)
                {
                    Span<float> row = image.Row(y);

                    row.Slice(0, (int)thickness).Fill(value);
                    row.Slice((int)(xsize - thickness), (int)thickness).Fill(value);
                }
            }
        }

        private static void TestRandomCase(
            (nuint XSize, nuint YSize) dims,
            (float Min, float Max) range,
            double sigma,
            double maxL1,
            double maxRel)
        {
            JxlMemoryManager memoryManager = TestMemoryManager.MemoryManager();
            using ImagePlane<float> input  = ImagePlane<float>.Create(memoryManager, dims.XSize, dims.YSize).Value!;
            using ImagePlane<float> temp   = ImagePlane<float>.Create(memoryManager, dims.XSize, dims.YSize).Value!;
            using ImagePlane<float> output = ImagePlane<float>.Create(memoryManager, dims.XSize, dims.YSize).Value!;

            ulong seed = 65537UL + dims.XSize * 129UL + dims.YSize;
            ImageTestUtils.RandomFillImage(input, range.Min, range.Max, seed);

            nuint border = (nuint)(4 * sigma);
            SetBorder(border, 0.0f, input);

            RecursiveGaussian rg = GaussBlur.CreateRecursiveGaussian(sigma);
            Status status        = GaussBlur.FastGaussian(
                memoryManager,
                rg,
                input.XSize,
                input.YSize,
                y => input.ConstRow(y),
                y => temp.Row(y),
                y => output.Row(y));

            status.IsOk.ShouldBeTrue();

            float[] kernel                   = GaussianKernel((int)(4 * sigma), (float)sigma);
            using ImagePlane<float> expected = Convolve(input, kernel);

            ImageTestUtils.VerifyRelativeError(expected, output, maxL1, maxRel, (nint)border);
        }

        private static void TestRandomForSizes(float min, float max, double sigma)
        {
            const double maxL1  = 6E-3;
            const double maxRel = 3E-3;

            TestRandomCase((128, 1), (min, max), sigma, maxL1, maxRel);
            TestRandomCase((1, 128), (min, max), sigma, maxL1, maxRel);
            TestRandomCase((30, 201), (min, max), sigma, maxL1 * 1.6, maxRel * 1.2);
            TestRandomCase((201, 30), (min, max), sigma, maxL1 * 1.6, maxRel * 1.2);
            TestRandomCase((201, 201), (min, max), sigma, maxL1 * 2.0, maxRel * 1.2);
        }
    }

    /// <summary>
    /// Regression tests for <see cref="DjxlFuzzer"/> across all 44 OSS-Fuzz minimized inputs.
    /// Ported from <c>tools/djxl_fuzzer_test.cc</c>.
    /// </summary>
    [TestFixture]
    public class DjxlFuzzerTests
    {
        /// <summary>
        /// Returns all 44 OSS-Fuzz test case IDs from <c>tools/djxl_fuzzer_test.cc</c>.
        /// </summary>
        /// <returns>Sequence of OSS-Fuzz testcase identifiers.</returns>
        public static IEnumerable<ulong> AllTestIds()
        {
            return new ulong[]
            {
                4546077333782528UL, 4716049045520384UL, 4718378999218176UL, 4729306868219904UL,
                4787817341911040UL, 4816304719134720UL, 4848606801166336UL, 4859247059402752UL,
                4887504894951424UL, 4984529666834432UL, 5014934495297536UL, 5112097090961408UL,
                5189497920290816UL, 5381727462227968UL, 5382562858532864UL, 5392074930782208UL,
                5467620336336896UL, 5473482434019328UL, 5489367788945408UL, 5556400888086528UL,
                5582808628723712UL, 5631220790198272UL, 5685623166468096UL, 5737500246671360UL,
                5785438255710208UL, 5800733037953024UL, 5849986531721216UL, 5858549672050688UL,
                5899664422993920UL, 5900921718046720UL, 5906295376445440UL, 5914266367557632UL,
                6013780411154432UL, 6165169006313472UL, 6277573962760192UL, 6329817929220096UL,
                6355777170833408UL, 6375307931680768UL, 6448658097242112UL, 6515680276512768UL,
                6569981946494976UL, 6735607318052864UL, 6737321070821376UL, 6748486320652288UL,
            };
        }

        /// <summary>
        /// Ported from <c>DjxlFuzzerTest.TestOne</c>.
        /// </summary>
        /// <param name="id">OSS-Fuzz testcase identifier.</param>
        [TestCaseSource(nameof(AllTestIds))]
        public void TestOne(ulong id)
        {
            string relativePath = $"oss-fuzz/clusterfuzz-testcase-minimized-djxl_fuzzer-{id}";
            byte[] input        = TestUtils.ReadTestData(relativePath);

            Should.NotThrow(() => DjxlFuzzer.TestOneInput(input));
        }

        /// <summary>
        /// Verifies that inputs shorter than 4 bytes return 0 immediately without error.
        /// </summary>
        [Test]
        public void ShortInputReturnsZero()
        {
            int rc = DjxlFuzzer.DoTestOneInput(new byte[] { 0xFF, 0x0A });

            rc.ShouldBe(0);
        }

        /// <summary>
        /// Verifies that <see cref="DjxlFuzzer.DecodeJpegXl(ReadOnlySpan{byte}, JxlMemoryManager?, nuint, nuint, in FuzzSpec, FuzzDecodeResult)"/>
        /// decodes a valid JXL bitstream with a custom <see cref="FuzzSpec"/>.
        /// </summary>
        [Test]
        public void DecodeValidJxlWithFuzzSpec()
        {
            byte[] jxl       = TestUtils.ReadTestData("jxl/blending/cropped_traffic_light.jxl");
            FuzzSpec spec    = new FuzzSpec { GetAlpha = true, UseStreaming = true, Coalescing = true };
            var decodeResult = new FuzzDecodeResult();
            var trackingMgr  = new TrackingMemoryManager(TrackingMemoryManager.KGiB, 5 * TrackingMemoryManager.KGiB);

            bool ok = DjxlFuzzer.DecodeJpegXl(jxl, trackingMgr.Get(), 1u << 20, 5u << 20, in spec, decodeResult);

            ok.ShouldBeTrue();
            decodeResult.XSize.ShouldBeGreaterThan(0u);
            decodeResult.YSize.ShouldBeGreaterThan(0u);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            trackingMgr.Reset().IsOk.ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Ssimulacra"/> and <see cref="Ssimulacra2"/>.
    /// </summary>
    [TestFixture]
    public class SsimulacraTests
    {
        /// <summary>
        /// Verifies that identical images yield a SSIMULACRA v1 score of 0.
        /// </summary>
        [Test]
        public void SsimulacraIdenticalImagesScoreZero()
        {
            JxlMemoryManager mm     = NoMemoryManager.Get();
            using Image3<float> img = Image3<float>.Create(mm, 32, 32).Value!;
            FillGradient(img, 0.0f);

            StatusOr<Ssimulacra> res = Ssimulacra.ComputeDiff(img, img, simple: false);

            res.IsOk.ShouldBeTrue();
            res.Value!.Score().ShouldBe(0.0, 1e-6);
        }

        /// <summary>
        /// Verifies that distorted images yield a positive SSIMULACRA v1 score in both full and simple modes.
        /// </summary>
        [Test]
        public void SsimulacraDistortedImagesPositiveScore()
        {
            JxlMemoryManager mm      = NoMemoryManager.Get();
            using Image3<float> orig = Image3<float>.Create(mm, 32, 32).Value!;
            using Image3<float> dist = Image3<float>.Create(mm, 32, 32).Value!;

            FillGradient(orig, 0.0f);
            FillGradient(dist, 0.15f);

            StatusOr<Ssimulacra> fullRes   = Ssimulacra.ComputeDiff(orig, dist, simple: false);
            StatusOr<Ssimulacra> simpleRes = Ssimulacra.ComputeDiff(orig, dist, simple: true);

            fullRes.IsOk.ShouldBeTrue();
            simpleRes.IsOk.ShouldBeTrue();
            fullRes.Value!.Score().ShouldBeGreaterThan(0.0);
            simpleRes.Value!.Score().ShouldBeGreaterThan(0.0);

            using StringWriter sw = new StringWriter();
            TextWriter origOut    = Console.Out;

            try
            {
                Console.SetOut(sw);
                fullRes.Value.PrintDetails();
            }
            finally
            {
                Console.SetOut(origOut);
            }

            sw.ToString().ShouldContain("avg ssim");
        }

        /// <summary>
        /// Verifies that identical <see cref="ImageBundle"/> instances yield a SSIMULACRA 2 score of 100.
        /// </summary>
        [Test]
        public void Ssimulacra2IdenticalImagesScore100()
        {
            JxlMemoryManager mm    = NoMemoryManager.Get();
            ImageMetadata metadata = new ImageMetadata();
            metadata.SetFloat32Samples();
            metadata.ColorEncoding = ColorEncoding.SRGB();
            using ImageBundle orig = new ImageBundle(mm, metadata);
            Image3<float> planes   = Image3<float>.Create(mm, 32, 32).Value!;

            FillGradient(planes, 0.0f);
            orig.SetFromImage(planes, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            StatusOr<Msssim> res = Ssimulacra2.ComputeSSIMULACRA2(orig, orig);

            res.IsOk.ShouldBeTrue();
            res.Value!.Score().ShouldBe(100.0, 1e-4);
        }

        /// <summary>
        /// Verifies that distorted <see cref="ImageBundle"/> instances yield a SSIMULACRA 2 score below 100.
        /// </summary>
        [Test]
        public void Ssimulacra2DistortedImagesScoreBelow100()
        {
            JxlMemoryManager mm    = NoMemoryManager.Get();
            ImageMetadata metadata = new ImageMetadata();
            metadata.SetFloat32Samples();
            metadata.ColorEncoding = ColorEncoding.SRGB();
            using ImageBundle orig = new ImageBundle(mm, metadata);
            using ImageBundle dist = new ImageBundle(mm, metadata);
            Image3<float> planes1  = Image3<float>.Create(mm, 32, 32).Value!;
            Image3<float> planes2  = Image3<float>.Create(mm, 32, 32).Value!;

            FillGradient(planes1, 0.0f);
            FillGradient(planes2, 0.2f);
            orig.SetFromImage(planes1, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();
            dist.SetFromImage(planes2, ColorEncoding.SRGB()).IsOk.ShouldBeTrue();

            StatusOr<Msssim> res = Ssimulacra2.ComputeSSIMULACRA2(orig, dist);

            res.IsOk.ShouldBeTrue();
            res.Value!.Score().ShouldBeLessThan(99.5);
        }

        private static void FillGradient(Image3<float> img, float offset)
        {
            for (nuint c = 0; c < 3; ++c)
            {
                for (nuint y = 0; y < img.YSize; ++y)
                {
                    Span<float> row = img.PlaneRow(c, y);

                    for (nuint x = 0; x < img.XSize; ++x)
                    {
                        float v = ((x + y * 2u + c * 3u) % 32u) / 31.0f + offset;

                        row[(int)x] = Math.Clamp(v, 0.0f, 1.0f);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="CommandLineParser"/>, <see cref="CmdLineParsers"/>, <see cref="TrackingMemoryManager"/>,
    /// <see cref="FileIo"/>, <see cref="SpeedStats"/>, and <see cref="CodecConfig"/>.
    /// </summary>
    [TestFixture]
    public class ToolsCommonTests
    {
        /// <summary>
        /// Verifies <see cref="CommandLineParser"/> option, flag, positional, verbosity, and help handling.
        /// </summary>
        [Test]
        public void CommandLineParserParsesOptionsAndPositionals()
        {
            CommandLineParser parser     = new CommandLineParser();
            StrongBox<string?> pos1      = new StrongBox<string?>();
            string? pos2                 = null;
            int quality                  = 0;
            bool flagSet                 = false;

            parser.AddHelpText("General options:", 0);
            int idPos1 = parser.AddPositionalOption("INPUT", true, "Input file", pos1);
            int idPos2 = parser.AddPositionalOption("OUTPUT", false, "Output file", v => pos2 = v);
            int idQual = parser.AddOptionValue<int>('q', "quality", "N", "Quality setting", v => quality = v, CmdLineParsers.ParseSigned);
            int idFlag = parser.AddOptionFlag<bool>('f', "fast", "Fast mode", v => flagSet = v, CmdLineParsers.SetBooleanTrue);
            _          = parser.AddOptionFlag<bool>('v', "verbose", "Verbose output", _ => { }, CmdLineParsers.SetBooleanTrue);

            bool ok = parser.Parse(new string[] { "cjxl", "-v", "--quality=85", "-f", "in.png", "--", "-out.jxl" });

            ok.ShouldBeTrue();
            parser.Verbosity.ShouldBe(1);
            parser.HelpFlagPassed().ShouldBeFalse();
            parser.GetOption(idPos1).Matched.ShouldBeTrue();
            parser.GetOption(idPos2).Matched.ShouldBeTrue();
            parser.GetOption(idQual).Matched.ShouldBeTrue();
            parser.GetOption(idFlag).Matched.ShouldBeTrue();
            pos1.Value.ShouldBe("in.png");
            pos2.ShouldBe("-out.jxl");
            quality.ShouldBe(85);
            flagSet.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="CommandLineParser.PrintHelp"/> and <c>--help</c> flag recognition.
        /// </summary>
        [Test]
        public void CommandLineParserHelpOutput()
        {
            CommandLineParser parser = new CommandLineParser();
            parser.AddHelpText("Header section", 0);
            parser.AddPositionalOption("INPUT", true, "Input file", _ => { });
            parser.AddOptionValue<uint>('e', "effort", "N", "Encoder effort", _ => { }, CmdLineParsers.ParseUint32, 0);
            parser.AddOptionValue<uint>('\0', "hidden", "N", "Hidden option", _ => { }, CmdLineParsers.ParseUint32, 2);

            bool ok = parser.Parse(new string[] { "cjxl", "--help" });

            ok.ShouldBeTrue();
            parser.HelpFlagPassed().ShouldBeTrue();

            using StringWriter sw = new StringWriter();
            TextWriter origOut    = Console.Out;

            try
            {
                Console.SetOut(sw);
                parser.PrintHelp();
            }
            finally
            {
                Console.SetOut(origOut);
            }

            string help = sw.ToString();
            help.ShouldContain("Usage: cjxl INPUT [OPTIONS...]");
            help.ShouldContain("--effort=N");
        }

        /// <summary>
        /// Verifies all value parsers in <see cref="CmdLineParsers"/>.
        /// </summary>
        [Test]
        public void CmdLineParsersAllTypes()
        {
            CmdLineParsers.ParseSigned("-42", out int s32).ShouldBeTrue();
            s32.ShouldBe(-42);
            CmdLineParsers.ParseSigned("0x10", out s32).ShouldBeTrue();
            s32.ShouldBe(16);
            CmdLineParsers.ParseSigned("not_an_int", out _).ShouldBeFalse();

            CmdLineParsers.ParseUnsigned("1024", out nuint u).ShouldBeTrue();
            u.ShouldBe((nuint)1024);
            CmdLineParsers.ParseUnsigned("-1", out _).ShouldBeFalse();

            CmdLineParsers.ParseInt64("-9000000000", out long s64).ShouldBeTrue();
            s64.ShouldBe(-9000000000L);

            CmdLineParsers.ParseUint32("0xFF", out uint u32).ShouldBeTrue();
            u32.ShouldBe(255u);

            CmdLineParsers.ParseFloat("1.5", out float f32).ShouldBeTrue();
            f32.ShouldBe(1.5f);

            CmdLineParsers.ParseDouble("2.75", out double f64).ShouldBeTrue();
            f64.ShouldBe(2.75);

            CmdLineParsers.ParseString("hello", out string str).ShouldBeTrue();
            str.ShouldBe("hello");

            CmdLineParsers.SetBooleanTrue(out bool bTrue).ShouldBeTrue();
            bTrue.ShouldBeTrue();
            CmdLineParsers.SetBooleanFalse(out bool bFalse).ShouldBeTrue();
            bFalse.ShouldBeFalse();

            CmdLineParsers.ParseOverride("1", out Override ovOn).ShouldBeTrue();
            ovOn.ShouldBe(Override.On);
            CmdLineParsers.ParseOverride("0", out Override ovOff).ShouldBeTrue();
            ovOff.ShouldBe(Override.Off);
            CmdLineParsers.ParseOverride("2", out _).ShouldBeFalse();

            ColorHintsProxy proxy = new ColorHintsProxy();
            CmdLineParsers.ParseAndAppendKeyValue("color_space=RGB_D65_SRG_Rel_SRG", proxy).ShouldBeTrue();
            CmdLineParsers.ParseAndAppendKeyValue("invalid_no_equals", proxy).ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="TrackingMemoryManager"/> allocation tracking, leak detection, and cap enforcement.
        /// </summary>
        [Test]
        public void TrackingMemoryManagerTracksAndEnforcesCap()
        {
            TrackingMemoryManager tracker = new TrackingMemoryManager(cap: 1024, totalCap: 2048);
            JxlMemoryManager mm           = tracker.Get();

            nint ptr1 = mm.Alloc!(mm.Opaque, 512);
            ptr1.ShouldNotBe(nint.Zero);
            tracker.TotalAllocations.ShouldBe(1UL);
            tracker.MaxBytesInUse.ShouldBe(512UL);

            tracker.Reset().IsOk.ShouldBeFalse();

            nint ptrTooBig = mm.Alloc!(mm.Opaque, 1024);
            ptrTooBig.ShouldBe(nint.Zero);
            tracker.SeenOom.ShouldBeTrue();

            mm.Free!(mm.Opaque, ptr1);
            tracker.Reset().IsOk.ShouldBeTrue();
            tracker.SeenOom.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="FileIo.WriteFile"/> and <see cref="FileIo.ReadFile(string, out byte[])"/>.
        /// </summary>
        [Test]
        public void FileIoReadAndWriteRoundtrip()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"jxlsharp_test_{Guid.NewGuid():N}.bin");

            try
            {
                byte[] payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

                FileIo.WriteFile(tempFile, payload).ShouldBeTrue();
                FileIo.ReadFile(tempFile, out byte[] readBack).ShouldBeTrue();
                readBack.ShouldBe(payload);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        /// <summary>
        /// Verifies <see cref="SpeedStats"/> summary calculation across 1, 2, and 3+ repetitions and <see cref="CodecConfig"/>.
        /// </summary>
        [Test]
        public void SpeedStatsAndCodecConfig()
        {
            SpeedStats stats = new SpeedStats();
            stats.GetSummary(out _).ShouldBeFalse();

            stats.SetImageSize(1000, 1000);
            stats.SetFileSize(50000);

            stats.NotifyElapsed(0.10);
            stats.GetSummary(out SpeedStats.Summary s1).ShouldBeTrue();
            s1.CentralTendency.ShouldBe(0.10, 1e-9);

            stats.NotifyElapsed(0.08);
            stats.GetSummary(out SpeedStats.Summary s2).ShouldBeTrue();
            s2.CentralTendency.ShouldBe(0.08, 1e-9);
            s2.Type.ShouldBe("second: ");

            stats.NotifyElapsed(0.08);
            stats.GetSummary(out SpeedStats.Summary s3).ShouldBeTrue();
            s3.CentralTendency.ShouldBe(0.08, 1e-9);
            s3.Type.ShouldBe("geomean: ");

            stats.Print(4).ShouldBeTrue();

            string cfg = CodecConfig.CodecConfigString(JxlDecoder.Version());
            cfg.ShouldContain(ToolVersion.KJpegxlVersion);
        }
    }

    /// <summary>
    /// End-to-end tests for CLI tools (<see cref="CjxlTool"/>, <see cref="DjxlTool"/>, <see cref="JxlInfoTool"/>, <see cref="JxlTranTool"/>).
    /// </summary>
    [TestFixture]
    public class CliToolsTests
    {
        /// <summary>
        /// Verifies <c>--help</c> and <c>--version</c> flags across all 4 CLI tools.
        /// </summary>
        [Test]
        public void CliToolsHelpAndVersionReturnZero()
        {
            using StringWriter outSw = new StringWriter();
            using StringWriter errSw = new StringWriter();

            CjxlTool.Run(new string[] { "--help" }, outSw, errSw).ShouldBe(0);
            CjxlTool.Run(new string[] { "--version" }, outSw, errSw).ShouldBe(0);

            DjxlTool.Run(new string[] { "--help" }, outSw, errSw).ShouldBe(0);
            DjxlTool.Run(new string[] { "--version" }, outSw, errSw).ShouldBe(0);

            JxlInfoTool.Run(new string[] { "--help" }, outSw, errSw).ShouldBe(0);

            JxlTranTool.Run(new string[] { "--help" }, outSw, errSw).ShouldBe(0);
            JxlTranTool.Run(new string[] { "--version" }, outSw, errSw).ShouldBe(0);
        }

        /// <summary>
        /// Verifies end-to-end <c>cjxl</c> compression, <c>jxlinfo</c> metadata inspection,
        /// <c>jxltran</c> transformation, and <c>djxl</c> decompression on a test image.
        /// </summary>
        [Test]
        public void CjxlJxlInfoJxlTranAndDjxlEndToEnd()
        {
            string inputPpm  = TestUtils.GetTestDataPath("jxl/flower/flower_small.rgb.depth8.ppm");
            string tempDir   = Path.Combine(Path.GetTempPath(), $"jxlsharp_cli_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string jxlPath   = Path.Combine(tempDir, "out.jxl");
                string tranPath  = Path.Combine(tempDir, "tran.jxl");
                string reconPpm  = Path.Combine(tempDir, "recon.ppm");

                using StringWriter outSw = new StringWriter();
                using StringWriter errSw = new StringWriter();

                int cjxlRc = CjxlTool.Run(new string[] { inputPpm, jxlPath, "-d", "0", "-e", "1", "--container=1" }, outSw, errSw);
                cjxlRc.ShouldBe(0, errSw.ToString());
                File.Exists(jxlPath).ShouldBeTrue();

                outSw.GetStringBuilder().Clear();
                int infoRc = JxlInfoTool.Run(new string[] { "-v", jxlPath }, outSw, errSw);
                infoRc.ShouldBe(0, errSw.ToString());
                outSw.ToString().ShouldContain("JPEG XL");

                int tranRc = JxlTranTool.Run(new string[] { "--extract", jxlPath, tranPath }, outSw, errSw);
                tranRc.ShouldBe(0, errSw.ToString());
                File.Exists(tranPath).ShouldBeTrue();

                int djxlRc = DjxlTool.Run(new string[] { tranPath, reconPpm, "--print_read_bytes" }, outSw, errSw);
                djxlRc.ShouldBe(0, errSw.ToString());
                File.Exists(reconPpm).ShouldBeTrue();

                byte[] origBytes  = File.ReadAllBytes(inputPpm);
                byte[] reconBytes = File.ReadAllBytes(reconPpm);
                reconBytes.ShouldBe(origBytes);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
