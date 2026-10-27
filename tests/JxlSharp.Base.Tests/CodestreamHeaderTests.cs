// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Base.Tests
{
    /// <summary>
    /// Unit tests for codestream header types such as <see cref="JxlBasicInfo"/>.
    /// </summary>
    [TestFixture]
    public class CodestreamHeaderTests
    {
        /// <summary>
        /// Verifies that <see cref="JxlPreviewHeader"/> equality and properties work as expected.
        /// </summary>
        [Test]
        public void PreviewHeaderEqualityWorks()
        {
            JxlPreviewHeader a = new JxlPreviewHeader { XSize = 128, YSize = 64 };
            JxlPreviewHeader b = new JxlPreviewHeader { XSize = 128, YSize = 64 };
            JxlPreviewHeader c = new JxlPreviewHeader { XSize = 64, YSize = 64 };

            (a == b).ShouldBeTrue();
            (a != c).ShouldBeTrue();
            a.Equals((object)b).ShouldBeTrue();
            a.Equals((object?)null).ShouldBeFalse();
            a.GetHashCode().ShouldBe(b.GetHashCode());
        }

        /// <summary>
        /// Verifies that <see cref="JxlAnimationHeader"/> equality and properties work as expected.
        /// </summary>
        [Test]
        public void AnimationHeaderEqualityWorks()
        {
            JxlAnimationHeader a = new JxlAnimationHeader
            {
                TpsNumerator   = 100,
                TpsDenominator = 1,
                NumLoops       = 0,
                HaveTimecodes  = true,
            };
            JxlAnimationHeader b = new JxlAnimationHeader
            {
                TpsNumerator   = 100,
                TpsDenominator = 1,
                NumLoops       = 0,
                HaveTimecodes  = true,
            };
            JxlAnimationHeader c = new JxlAnimationHeader
            {
                TpsNumerator   = 30,
                TpsDenominator = 1,
                NumLoops       = 2,
                HaveTimecodes  = false,
            };

            (a == b).ShouldBeTrue();
            (a != c).ShouldBeTrue();
            a.Equals((object)b).ShouldBeTrue();
            a.Equals((object?)null).ShouldBeFalse();
            a.GetHashCode().ShouldBe(b.GetHashCode());
        }

        /// <summary>
        /// Verifies <see cref="JxlBasicInfo"/>, <see cref="JxlExtraChannelInfo"/>, <see cref="JxlHeaderExtensions"/>,
        /// <see cref="JxlBlendInfo"/>, and <see cref="JxlFrameHeader"/> properties.
        /// </summary>
        [Test]
        public void BasicInfoAndFrameHeaderPropertiesRoundtrip()
        {
            JxlBasicInfo info = new JxlBasicInfo
            {
                HaveContainer          = true,
                XSize                  = 1920,
                YSize                  = 1080,
                BitsPerSample          = 8,
                ExponentBitsPerSample  = 0,
                IntensityTarget        = 255.0f,
                MinNits                = 0.0f,
                RelativeToMaxDisplay   = false,
                LinearBelow            = 0.0f,
                UsesOriginalProfile    = false,
                HavePreview            = true,
                HaveAnimation          = false,
                Orientation            = JxlOrientation.Rotate90Cw,
                NumColorChannels       = 3,
                NumExtraChannels       = 1,
                AlphaBits              = 8,
                AlphaExponentBits      = 0,
                AlphaPremultiplied     = true,
                Preview                = new JxlPreviewHeader { XSize = 192, YSize = 108 },
                IntrinsicXSize         = 1920,
                IntrinsicYSize         = 1080,
            };

            info.XSize.ShouldBe(1920u);
            info.YSize.ShouldBe(1080u);
            info.Orientation.ShouldBe(JxlOrientation.Rotate90Cw);
            info.Preview.XSize.ShouldBe(192u);

            JxlExtraChannelInfo extra = new JxlExtraChannelInfo
            {
                Type                  = JxlExtraChannelType.SpotColor,
                BitsPerSample         = 16,
                ExponentBitsPerSample = 5,
                DimShift              = 1,
                NameLength            = 4,
                AlphaPremultiplied    = false,
                SpotColorR            = 0.1f,
                SpotColorG            = 0.2f,
                SpotColorB            = 0.3f,
                SpotColorA            = 1.0f,
                CfaChannel            = 2,
            };

            extra.Type.ShouldBe(JxlExtraChannelType.SpotColor);
            extra.SpotColorA.ShouldBe(1.0f);

            JxlHeaderExtensions extA = new JxlHeaderExtensions { Extensions = 42 };
            JxlHeaderExtensions extB = new JxlHeaderExtensions { Extensions = 42 };
            JxlHeaderExtensions extC = new JxlHeaderExtensions { Extensions = 99 };

            (extA == extB).ShouldBeTrue();
            (extA != extC).ShouldBeTrue();
            extA.Equals((object)extB).ShouldBeTrue();
            extA.GetHashCode().ShouldBe(extB.GetHashCode());

            JxlBlendInfo blendA = new JxlBlendInfo
            {
                BlendMode = JxlBlendMode.Blend,
                Source    = 1,
                Alpha     = 0,
                Clamp     = true,
            };
            JxlBlendInfo blendB = new JxlBlendInfo
            {
                BlendMode = JxlBlendMode.Blend,
                Source    = 1,
                Alpha     = 0,
                Clamp     = true,
            };

            (blendA == blendB).ShouldBeTrue();
            blendA.Equals((object)blendB).ShouldBeTrue();

            JxlFrameHeader frame = new JxlFrameHeader
            {
                Duration   = 10,
                Timecode   = 0x01020304u,
                NameLength = 5,
                IsLast     = true,
                LayerInfo  = new JxlLayerInfo
                {
                    HaveCrop        = true,
                    CropX0          = -10,
                    CropY0          = 20,
                    XSize           = 100,
                    YSize           = 200,
                    BlendInfo       = blendA,
                    SaveAsReference = 2,
                },
            };

            frame.IsLast.ShouldBeTrue();
            frame.LayerInfo.CropX0.ShouldBe(-10);
            frame.LayerInfo.BlendInfo.ShouldBe(blendA);
        }
    }
}
