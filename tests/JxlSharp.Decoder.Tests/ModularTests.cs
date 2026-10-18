// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using JxlSharp.Base;
using NUnit.Framework;
using Shouldly;

namespace JxlSharp.Decoder
{
    /// <summary>
    /// Unit tests for <see cref="ModularOptions"/>.
    /// </summary>
    [TestFixture]
    public sealed class ModularOptionsTests
    {
        /// <summary>
        /// Verifies default values of <see cref="ModularOptions"/>.
        /// </summary>
        [Test]
        public void DefaultConstructor_InitializesExpectedDefaults()
        {
            ModularOptions sut = new ModularOptions();

            sut.MaxChanSize.ShouldBe((nuint)0xFFFFFF);
            sut.GroupDim.ShouldBe((nuint)0x1FFFFFFF);
            sut.NbRepeats.ShouldBe(0.5f);
            sut.Predictor.ShouldBe(Predictor.Undefined);
            sut.WpTreeMode.ShouldBe(ModularOptions.TreeMode.Default);
            sut.Kind.ShouldBe(ModularOptions.TreeKind.Learn);
            sut.SplittingHeuristicsProperties.Count.ShouldBe(8);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Channel"/>.
    /// </summary>
    [TestFixture]
    public sealed class ChannelTests
    {
        /// <summary>
        /// Verifies that <see cref="Channel.Create"/> allocates a channel with expected dimensions and shifts.
        /// </summary>
        [Test]
        public void Create_AllocatesPlaneAndReadsWritesRows()
        {
            StatusOr<Channel> chOr = Channel.Create(null, 8, 4, 1, 2);

            chOr.IsOk.ShouldBeTrue();
            using Channel sut = chOr.Value;

            sut.W.ShouldBe((nuint)8);
            sut.H.ShouldBe((nuint)4);
            sut.HShift.ShouldBe(1);
            sut.VShift.ShouldBe(2);
            sut.Component.ShouldBe(-1);

            Span<int> row0 = sut.Row(0);

            for (int x = 0; x < 8; x++)
            {
                row0[x] = (x + 1) * 10;
            }

            ReadOnlySpan<int> constRow0 = sut.ConstRow(0);
            constRow0[3].ShouldBe(40);
        }

        /// <summary>
        /// Verifies that <see cref="Channel.Shrink(int, int)"/> resizes the underlying plane.
        /// </summary>
        [Test]
        public void Shrink_UpdatesDimensionsAndPlane()
        {
            StatusOr<Channel> chOr = Channel.Create(null, 16, 16);
            chOr.IsOk.ShouldBeTrue();
            using Channel sut = chOr.Value;

            Status shrinkStatus = sut.Shrink(6, 5);

            shrinkStatus.IsOk.ShouldBeTrue();
            sut.W.ShouldBe((nuint)6);
            sut.H.ShouldBe((nuint)5);
            sut.Plane.XSize.ShouldBe((nuint)6);
            sut.Plane.YSize.ShouldBe((nuint)5);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ModularImage"/>.
    /// </summary>
    [TestFixture]
    public sealed class ModularImageTests
    {
        /// <summary>
        /// Verifies that <see cref="ModularImage.Create"/> and <see cref="ModularImage.Clone"/> copy channels and metadata.
        /// </summary>
        [Test]
        public void CreateAndClone_CopiesChannelsAndProperties()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 4, 3, 8, 3);

            imgOr.IsOk.ShouldBeTrue();
            using ModularImage sut = imgOr.Value;

            sut.Empty.ShouldBeFalse();
            sut.Channels.Count.ShouldBe(3);
            sut.Channels[0].Row(1)[2] = 42;
            sut.DebugString().ShouldContain("4x3, depth: 8");

            StatusOr<ModularImage> cloneOr = ModularImage.Clone(sut);

            cloneOr.IsOk.ShouldBeTrue();
            using ModularImage clone = cloneOr.Value;

            clone.W.ShouldBe((nuint)4);
            clone.H.ShouldBe((nuint)3);
            clone.Channels.Count.ShouldBe(3);
            clone.Channels[0].ConstRow(1)[2].ShouldBe(42);
        }

        /// <summary>
        /// Verifies that <see cref="ModularImage.UndoTransforms"/> inverts transforms on the image.
        /// </summary>
        [Test]
        public void UndoTransforms_AppliesInverseRct()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 2, 1, 8, 3);
            imgOr.IsOk.ShouldBeTrue();
            using ModularImage sut = imgOr.Value;

            sut.Channels[0].Row(0)[0] = 100;
            sut.Channels[1].Row(0)[0] = 20;
            sut.Channels[2].Row(0)[0] = -10;

            Transform rct = new Transform(TransformId.RCT)
            {
                BeginC  = 0,
                RctType = 0,
            };
            sut.Transforms.Add(rct);

            sut.UndoTransforms(new WeightedHeader());

            sut.Error.ShouldBeFalse();
            sut.Transforms.Count.ShouldBe(0);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="PropertyDecisionNode"/>.
    /// </summary>
    [TestFixture]
    public sealed class PropertyDecisionNodeTests
    {
        /// <summary>
        /// Verifies leaf and split factory methods on <see cref="PropertyDecisionNode"/>.
        /// </summary>
        [Test]
        public void LeafAndSplit_CreateExpectedNodes()
        {
            PropertyDecisionNode defNode   = PropertyDecisionNode.Default();
            PropertyDecisionNode leafNode  = PropertyDecisionNode.Leaf(Predictor.Gradient, 5, 2);
            PropertyDecisionNode splitNode = PropertyDecisionNode.Split(3, 10, 1);

            defNode.Property.ShouldBe((short)-1);
            defNode.Predictor.ShouldBe(Predictor.Zero);
            defNode.Multiplier.ShouldBe(1u);

            leafNode.Property.ShouldBe((short)-1);
            leafNode.Predictor.ShouldBe(Predictor.Gradient);
            leafNode.PredictorOffset.ShouldBe(5L);
            leafNode.Multiplier.ShouldBe(2u);

            splitNode.Property.ShouldBe((short)3);
            splitNode.SplitVal.ShouldBe(10);
            splitNode.LChild.ShouldBe(1u);
            splitNode.RChild.ShouldBe(2u);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ModularMa"/>.
    /// </summary>
    [TestFixture]
    public sealed class ModularMaTests
    {
        /// <summary>
        /// Verifies that <see cref="ModularMa.ValidateTree"/> accepts valid trees and rejects contradictory split ranges.
        /// </summary>
        [Test]
        public void ValidateTree_ValidAndInvalidSplitIntervals_ReturnsExpectedStatus()
        {
            List<PropertyDecisionNode> validTree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(0, 10, 1, 2),
                PropertyDecisionNode.Leaf(Predictor.Gradient),
                PropertyDecisionNode.Leaf(Predictor.Weighted),
            };

            ModularMa.ValidateTree(validTree).IsOk.ShouldBeTrue();

            List<PropertyDecisionNode> invalidTree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(0, 10, 1, 2),
                PropertyDecisionNode.Split(0, 5, 3, 4),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
                PropertyDecisionNode.Leaf(Predictor.Zero),
            };

            ModularMa.ValidateTree(invalidTree).IsOk.ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="WeightedHeader"/>.
    /// </summary>
    [TestFixture]
    public sealed class WeightedHeaderTests
    {
        /// <summary>
        /// Verifies <see cref="WeightedHeader.SetPredictorMode"/> and <see cref="WeightedHeader.Clone"/>.
        /// </summary>
        [Test]
        public void SetPredictorModeAndClone_PreservesParameters()
        {
            WeightedHeader sut = new WeightedHeader();

            for (int mode = 0; mode <= 4; mode++)
            {
                sut.SetPredictorMode(mode);
                WeightedHeader clone = sut.Clone();

                clone.P1C.ShouldBe(sut.P1C);
                clone.P2C.ShouldBe(sut.P2C);
                clone.P3Ca.ShouldBe(sut.P3Ca);
                clone.W[0].ShouldBe(sut.W[0]);
            }
        }
    }

    /// <summary>
    /// Unit tests for <see cref="NeighborPixels"/>.
    /// </summary>
    [TestFixture]
    public sealed class NeighborPixelsTests
    {
        /// <summary>
        /// Verifies field assignment on <see cref="NeighborPixels"/>.
        /// </summary>
        [Test]
        public void NeighborPixels_StoresAllSevenSpatialNeighbors()
        {
            NeighborPixels sut = new NeighborPixels
            {
                Left          = 1,
                Top           = 2,
                TopTop        = 3,
                TopLeft       = 4,
                TopRight      = 5,
                LeftLeft      = 6,
                TopRightRight = 7,
            };

            sut.Left.ShouldBe(1L);
            sut.TopRightRight.ShouldBe(7L);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="WeightedState"/>.
    /// </summary>
    [TestFixture]
    public sealed class WeightedStateTests
    {
        /// <summary>
        /// Verifies that <see cref="WeightedState"/> predicts and updates error buffers across pixels.
        /// </summary>
        [Test]
        public void PredictAndUpdateErrors_ComputesPredictionAndProperty()
        {
            WeightedHeader header = new WeightedHeader();
            WeightedState sut     = new WeightedState(header, 8, 4);

            NeighborPixels neighbors = new NeighborPixels
            {
                Left          = 50,
                Top           = 60,
                TopLeft       = 55,
                TopRight      = 62,
                LeftLeft      = 48,
                TopTop        = 58,
                TopRightRight = 64,
            };

            Span<int> props = stackalloc int[16];
            long wpPred     = sut.Predict(2, 1, 8, in neighbors, props, 15, computeProperties: true);

            wpPred.ShouldBeInRange(40L, 70L);
            sut.UpdateErrors(56, 2, 1, 8);
            WeightedState.AddBits(3).ShouldBe(24L);
            WeightedState.ErrorWeight(0, 12).ShouldBeGreaterThan(0u);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="FlatDecisionNode"/>.
    /// </summary>
    [TestFixture]
    public sealed class FlatDecisionNodeTests
    {
        /// <summary>
        /// Verifies leaf representation in <see cref="FlatDecisionNode"/>.
        /// </summary>
        [Test]
        public void FlatDecisionNode_StoresFields()
        {
            FlatDecisionNode sut = new FlatDecisionNode
            {
                Property0       = -1,
                Predictor       = Predictor.Left,
                PredictorOffset = 3,
                Multiplier      = 2,
                ChildID         = 7,
            };

            sut.Property0.ShouldBe(-1);
            sut.Predictor.ShouldBe(Predictor.Left);
            sut.ChildID.ShouldBe(7u);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="MATreeLookup"/>.
    /// </summary>
    [TestFixture]
    public sealed class MATreeLookupTests
    {
        /// <summary>
        /// Verifies that <see cref="MATreeLookup.Lookup"/> traverses a flattened decision tree to the matching leaf.
        /// </summary>
        [Test]
        public void Lookup_ReturnsExpectedLeafResult()
        {
            List<PropertyDecisionNode> tree = new List<PropertyDecisionNode>
            {
                PropertyDecisionNode.Split(2, 5, 1, 2),
                PropertyDecisionNode.Leaf(Predictor.Top, 1, 1),
                PropertyDecisionNode.Leaf(Predictor.Left, -1, 2),
            };
            tree[1] = new PropertyDecisionNode(-1, 0, 10, 0, Predictor.Top, 1, 1);
            tree[2] = new PropertyDecisionNode(-1, 0, 20, 0, Predictor.Left, -1, 2);

            ReadOnlySpan<int> staticProps = stackalloc int[] { 0, 0 };
            FlatDecisionNode[] flat       = ModularEncoding.FilterTree(
                tree,
                staticProps,
                out _,
                out _,
                out _,
                out _);

            MATreeLookup sut = new MATreeLookup(flat);
            Span<int> props  = stackalloc int[16];

            props[2] = 10;
            MATreeLookup.LookupResult resHigh = sut.Lookup(props);
            resHigh.Context.ShouldBe(10u);
            resHigh.Predictor.ShouldBe(Predictor.Top);

            props[2] = 2;
            MATreeLookup.LookupResult resLow = sut.Lookup(props);
            resLow.Context.ShouldBe(20u);
            resLow.Predictor.ShouldBe(Predictor.Left);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="PredictionResult"/>.
    /// </summary>
    [TestFixture]
    public sealed class PredictionResultTests
    {
        /// <summary>
        /// Verifies fields of <see cref="PredictionResult"/>.
        /// </summary>
        [Test]
        public void PredictionResult_StoresValues()
        {
            PredictionResult sut = new PredictionResult
            {
                Context    = 4,
                Guess      = 123,
                Predictor  = Predictor.Gradient,
                Multiplier = 1,
            };

            sut.Context.ShouldBe(4);
            sut.Guess.ShouldBe(123L);
            sut.Predictor.ShouldBe(Predictor.Gradient);
            sut.Multiplier.ShouldBe(1);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ModularPredictors"/>.
    /// </summary>
    [TestFixture]
    public sealed class ModularPredictorsTests
    {
        /// <summary>
        /// Verifies all 14 standard predictors in <see cref="ModularPredictors.PredictOne"/>.
        /// </summary>
        [Test]
        public void PredictOne_EvaluatesAllStandardPredictors()
        {
            NeighborPixels n = new NeighborPixels
            {
                Left          = 10,
                Top           = 20,
                TopLeft       = 12,
                TopRight      = 24,
                LeftLeft      = 8,
                TopTop        = 18,
                TopRightRight = 26,
            };

            ModularPredictors.PredictOne(Predictor.Zero, in n, 99).ShouldBe(0L);
            ModularPredictors.PredictOne(Predictor.Left, in n, 99).ShouldBe(10L);
            ModularPredictors.PredictOne(Predictor.Top, in n, 99).ShouldBe(20L);
            ModularPredictors.PredictOne(Predictor.Average0, in n, 99).ShouldBe(15L);
            ModularPredictors.PredictOne(Predictor.Select, in n, 99).ShouldBe(20L);
            ModularPredictors.PredictOne(Predictor.Gradient, in n, 99).ShouldBe(18L);
            ModularPredictors.PredictOne(Predictor.Weighted, in n, 99).ShouldBe(99L);
            ModularPredictors.PredictOne(Predictor.TopRight, in n, 99).ShouldBe(24L);
            ModularPredictors.PredictOne(Predictor.TopLeft, in n, 99).ShouldBe(12L);
            ModularPredictors.PredictOne(Predictor.LeftLeft, in n, 99).ShouldBe(8L);
            ModularPredictors.PredictOne(Predictor.Average1, in n, 99).ShouldBe(11L);
            ModularPredictors.PredictOne(Predictor.Average2, in n, 99).ShouldBe(16L);
            ModularPredictors.PredictOne(Predictor.Average3, in n, 99).ShouldBe(22L);
            ModularPredictors.PredictOne(Predictor.Average4, in n, 99).ShouldBe(16L);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="SqueezeParams"/>.
    /// </summary>
    [TestFixture]
    public sealed class SqueezeParamsTests
    {
        /// <summary>
        /// Verifies default values and cloning of <see cref="SqueezeParams"/>.
        /// </summary>
        [Test]
        public void DefaultAndClone_PreservesFields()
        {
            SqueezeParams sut = new SqueezeParams
            {
                Horizontal = true,
                InPlace    = true,
                BeginC     = 1,
                NumC       = 3,
            };

            SqueezeParams clone = sut.Clone();

            clone.Horizontal.ShouldBeTrue();
            clone.InPlace.ShouldBeTrue();
            clone.BeginC.ShouldBe(1u);
            clone.NumC.ShouldBe(3u);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="Transform"/> and <see cref="ModularTransformOps"/>.
    /// </summary>
    [TestFixture]
    public sealed class TransformTests
    {
        /// <summary>
        /// Verifies that all 42 RCT permutations and color space transforms invert cleanly via <see cref="Transform.Inverse"/>.
        /// </summary>
        [Test]
        public void InverseRct_All42Permutations_Succeed()
        {
            WeightedHeader wpHeader = new WeightedHeader();

            for (uint rctType = 0; rctType < 42; rctType++)
            {
                StatusOr<ModularImage> imgOr = ModularImage.Create(null, 4, 2, 8, 3);
                imgOr.IsOk.ShouldBeTrue();
                using ModularImage img = imgOr.Value;

                for (int c = 0; c < 3; c++)
                {
                    for (nuint y = 0; y < 2; y++)
                    {
                        Span<int> row = img.Channels[c].Row(y);

                        for (int x = 0; x < 4; x++)
                        {
                            row[x] = (c * 10) + (int)y + x;
                        }
                    }
                }

                Transform sut = new Transform(TransformId.RCT)
                {
                    BeginC  = 0,
                    RctType = rctType,
                };

                sut.MetaApply(img).IsOk.ShouldBeTrue();
                sut.Inverse(img, wpHeader).IsOk.ShouldBeTrue();
            }
        }

        /// <summary>
        /// Verifies that <see cref="Transform"/> with <see cref="TransformId.Squeeze"/> applies meta-squeeze and inverse squeeze.
        /// </summary>
        [Test]
        public void SqueezeTransform_MetaApplyAndInverse_RestoresOriginalDimensions()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 8, 8, 8, 3);
            imgOr.IsOk.ShouldBeTrue();
            using ModularImage img = imgOr.Value;

            Transform sut = new Transform(TransformId.Squeeze);
            sut.MetaApply(img).IsOk.ShouldBeTrue();
            img.Channels.Count.ShouldBeGreaterThan(3);

            sut.Inverse(img, new WeightedHeader()).IsOk.ShouldBeTrue();
            img.Channels.Count.ShouldBe(3);
            img.Channels[0].W.ShouldBe((nuint)8);
            img.Channels[0].H.ShouldBe((nuint)8);
        }

        /// <summary>
        /// Verifies that <see cref="Transform"/> with <see cref="TransformId.Palette"/> applies meta-palette and inverse palette.
        /// </summary>
        [Test]
        public void PaletteTransform_MetaApplyAndInverse_ReconstructsChannels()
        {
            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 4, 2, 8, 3);
            imgOr.IsOk.ShouldBeTrue();
            using ModularImage img = imgOr.Value;

            Transform sut = new Transform(TransformId.Palette)
            {
                BeginC    = 0,
                NumC      = 3,
                NbColors  = 4,
                NbDeltas  = 0,
                Predictor = Predictor.Zero,
            };

            sut.MetaApply(img).IsOk.ShouldBeTrue();
            img.NbMetaChannels.ShouldBe((nuint)1);
            img.Channels.Count.ShouldBe(2);

            // Set palette color 1 to (10, 20, 30) and index channel pixel (0,0) to 1.
            img.Channels[0].Row(0)[1] = 10;
            img.Channels[0].Row(1)[1] = 20;
            img.Channels[0].Row(2)[1] = 30;
            img.Channels[1].Row(0)[0] = 1;

            sut.Inverse(img, new WeightedHeader()).IsOk.ShouldBeTrue();
            img.Channels.Count.ShouldBe(3);
            img.Channels[0].ConstRow(0)[0].ShouldBe(10);
            img.Channels[1].ConstRow(0)[0].ShouldBe(20);
            img.Channels[2].ConstRow(0)[0].ShouldBe(30);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ModularTransformOps"/>.
    /// </summary>
    [TestFixture]
    public sealed class ModularTransformOpsTests
    {
        /// <summary>
        /// Verifies <see cref="ModularTransformOps.PixelAdd"/> and <see cref="ModularTransformOps.CheckEqualChannels"/>.
        /// </summary>
        [Test]
        public void PixelAddAndCheckEqualChannels_BehaveAsExpected()
        {
            ModularTransformOps.PixelAdd(int.MaxValue, 1).ShouldBe(int.MinValue);

            StatusOr<ModularImage> imgOr = ModularImage.Create(null, 4, 4, 8, 3);
            imgOr.IsOk.ShouldBeTrue();
            using ModularImage img = imgOr.Value;

            ModularTransformOps.CheckEqualChannels(img, 0, 2).IsOk.ShouldBeTrue();
            ModularTransformOps.CheckEqualChannels(img, 0, 5).IsOk.ShouldBeFalse();
        }
    }

    /// <summary>
    /// Unit tests for <see cref="GroupHeader"/>, <see cref="TreeLut"/>, <see cref="ModularStreamId"/>,
    /// <see cref="GlobalModularContext"/>, and <see cref="ModularEncoding"/>.
    /// </summary>
    [TestFixture]
    public sealed class ModularEncodingTests
    {
        /// <summary>
        /// Verifies <see cref="GroupHeader"/> default initialization and bundle reading from zero bits.
        /// </summary>
        [Test]
        public void GroupHeader_ReadsDefaultHeaderFromZeroBits()
        {
            GroupHeader sut = new GroupHeader();
            byte[] data     = new byte[16];
            using BitReader br = new BitReader(data);

            Status status = Bundle.Read(br, sut);

            status.IsOk.ShouldBeTrue();
            sut.UseGlobalTree.ShouldBeFalse();
            sut.Transforms.Count.ShouldBe(0);
            br.Close().IsOk.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies <see cref="TreeLut"/> and <see cref="ModularEncoding.TreeToLookupTable"/>.
        /// </summary>
        [Test]
        public void TreeToLookupTable_SingleLeaf_PopulatesContextLookup()
        {
            TreeLut lut = new TreeLut(hasOffsets: true, hasMultipliers: true);
            FlatDecisionNode[] flat =
            {
                new FlatDecisionNode
                {
                    Property0       = -1,
                    ChildID         = 3,
                    Predictor       = Predictor.Gradient,
                    PredictorOffset = 0,
                    Multiplier      = 1,
                },
            };

            bool ok = ModularEncoding.TreeToLookupTable(flat, lut);

            ok.ShouldBeTrue();
            lut.ContextLookup[TreeLut.KPropRangeFast].ShouldBe((byte)3);
        }

        /// <summary>
        /// Verifies <see cref="ModularStreamId"/> stream numbering and debug strings.
        /// </summary>
        [Test]
        public void ModularStreamId_ComputesDistinctStreamIds()
        {
            FrameDimensions dim = new FrameDimensions();
            dim.Set(256, 256, 1, 0, 0, false, 1);

            ModularStreamId.Global().ID(dim).ShouldBe((nuint)0);
            ModularStreamId.VarDCTDC(0).ID(dim).ShouldBe((nuint)1);
            ModularStreamId.ModularDC(0).ID(dim).ShouldBe((nuint)2);
            ModularStreamId.ACMetadata(0).ID(dim).ShouldBe((nuint)3);

            StatusOr<ModularStreamId> qtOr = ModularStreamId.QuantTable(0);
            qtOr.IsOk.ShouldBeTrue();
            qtOr.Value.ID(dim).ShouldBe((nuint)4);
            qtOr.Value.DebugString().ShouldContain("QuantTable");

            ModularStreamId.QuantTable(17).IsOk.ShouldBeFalse();
        }

        /// <summary>
        /// Verifies <see cref="GlobalModularContext"/> default properties.
        /// </summary>
        [Test]
        public void GlobalModularContext_DefaultUndoTransformsIsTrue()
        {
            GlobalModularContext sut = new GlobalModularContext();

            sut.UndoTransforms.ShouldBeTrue();
            sut.AllowTruncatedGroup.ShouldBeFalse();
        }
    }
}
