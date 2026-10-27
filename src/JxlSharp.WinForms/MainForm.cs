// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.
//
// Author: Hefaistos68
// Email: dev@hefaistos68.dev
// Site or repo: https://github.com/Hefaistos68

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using JxlSharp.Base;
using JxlSharp.Extras;
using JxlSharp.Tools;

namespace JxlSharp.WinForms
{
    /// <summary>
    /// Main Windows Forms window for loading, previewing, converting, inspecting, and saving
    /// JPEG XL (<c>.jxl</c>), JPEG (<c>.jpg</c>), and other supported image formats, exposing all
    /// JXL conversion options and CLI tools (<c>cjxl</c>, <c>djxl</c>, <c>jxlinfo</c>, <c>jxltran</c>).
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly JxlImageService _imageService;
        private JxlLoadedImage? _originalImage;
        private JxlLoadedImage? _currentImage;
        private PackedPixelFile? _referencePixelFile;
        private bool _suppressAutoConvert;
        private bool _lastConversionWasJpeg;

        private readonly ToolStripStatusLabel _statusLabel        = new ToolStripStatusLabel("Ready");
        private readonly SplitPreviewPictureBox _previewPictureBox = new SplitPreviewPictureBox();
        private readonly DataGridView _propertiesGrid             = new DataGridView();
        private readonly TextBox _verboseInfoTextBox              = new TextBox();
        private readonly TextBox _consoleLogTextBox               = new TextBox();
        private readonly TabControl _rightTabControl              = new TabControl();
        private TabPage _propertiesTabPage                        = null!;
        private TabPage _encodeTabPage                            = null!;
        private TabPage _decodeTabPage                            = null!;

        // Encode (cjxl) controls
        private readonly CheckBox _chkUseQuality          = new CheckBox();
        private readonly NumericUpDown _numDistance       = new NumericUpDown();
        private readonly NumericUpDown _numQuality        = new NumericUpDown();
        private readonly NumericUpDown _numAlphaDistance  = new NumericUpDown();
        private readonly NumericUpDown _numEffort         = new NumericUpDown();
        private readonly ComboBox _cmbLosslessJpeg        = new ComboBox();
        private readonly ComboBox _cmbModular             = new ComboBox();
        private readonly ComboBox _cmbContainer           = new ComboBox();
        private readonly CheckBox _chkCompressBoxes       = new CheckBox();
        private readonly CheckBox _chkProgressive         = new CheckBox();
        private readonly ComboBox _cmbProgressiveAc       = new ComboBox();
        private readonly ComboBox _cmbQProgressiveAc      = new ComboBox();
        private readonly NumericUpDown _numProgressiveDc  = new NumericUpDown();
        private readonly NumericUpDown _numResponsive     = new NumericUpDown();
        private readonly NumericUpDown _numEpf            = new NumericUpDown();
        private readonly NumericUpDown _numFasterDecoding = new NumericUpDown();
        private readonly NumericUpDown _numResampling     = new NumericUpDown();
        private readonly NumericUpDown _numEcResampling   = new NumericUpDown();
        private readonly NumericUpDown _numBrotliEffort   = new NumericUpDown();
        private readonly ComboBox _cmbGaborish            = new ComboBox();
        private readonly ComboBox _cmbDots                = new ComboBox();
        private readonly ComboBox _cmbPatches             = new ComboBox();
        private readonly ComboBox _cmbKeepInvisible       = new ComboBox();
        private readonly ComboBox _cmbGroupOrder          = new ComboBox();
        private readonly ComboBox _cmbJpegReconCfl        = new ComboBox();
        private readonly NumericUpDown _numCenterX        = new NumericUpDown();
        private readonly NumericUpDown _numCenterY        = new NumericUpDown();
        private readonly NumericUpDown _numPhotonNoise    = new NumericUpDown();
        private readonly NumericUpDown _numIntensity      = new NumericUpDown();
        private readonly NumericUpDown _numOverrideBits   = new NumericUpDown();
        private readonly NumericUpDown _numCodestreamLvl  = new NumericUpDown();
        private readonly NumericUpDown _numUpsamplingMode = new NumericUpDown();
        private readonly NumericUpDown _numPremultiply    = new NumericUpDown();
        private readonly NumericUpDown _numStripAlpha     = new NumericUpDown();
        private readonly NumericUpDown _numBuffering      = new NumericUpDown();
        private readonly NumericUpDown _numCjxlThreads    = new NumericUpDown();
        private readonly NumericUpDown _numCjxlReps       = new NumericUpDown();
        private readonly CheckBox _chkAllowExpert         = new CheckBox();
        private readonly CheckBox _chkAlreadyDownsampled  = new CheckBox();
        private readonly CheckBox _chkStreamingInput      = new CheckBox();
        private readonly CheckBox _chkStreamingOutput     = new CheckBox();
        private readonly CheckBox _chkCjxlQuiet           = new CheckBox();
        private readonly TextBox _txtColorHints           = new TextBox();

        // Decode (djxl) controls
        private readonly CheckBox _chkPixelsToJpeg       = new CheckBox();
        private readonly NumericUpDown _numJpegQuality   = new NumericUpDown();
        private readonly CheckBox _chkUseSjpeg           = new CheckBox();
        private readonly NumericUpDown _numBitsPerSample = new NumericUpDown();
        private readonly ComboBox _cmbColorSpace         = new ComboBox();
        private readonly NumericUpDown _numDisplayNits   = new NumericUpDown();
        private readonly ComboBox _cmbAlphaBlend         = new ComboBox();
        private readonly CheckBox _chkUnpremultiplyAlpha = new CheckBox();
        private readonly NumericUpDown _numDownsampling  = new NumericUpDown();
        private readonly NumericUpDown _numMaxPasses     = new NumericUpDown();
        private readonly CheckBox _chkAllowPartial       = new CheckBox();
        private readonly CheckBox _chkAllowMoreSteps     = new CheckBox();
        private readonly CheckBox _chkRenderSpotcolors   = new CheckBox();
        private readonly CheckBox _chkCoalescing         = new CheckBox();
        private readonly CheckBox _chkOutputFrames       = new CheckBox();
        private readonly CheckBox _chkOutputExtraChans   = new CheckBox();
        private readonly CheckBox _chkUseImageCallback   = new CheckBox();
        private readonly CheckBox _chkPrintReadBytes     = new CheckBox();
        private readonly CheckBox _chkDjxlQuiet          = new CheckBox();
        private readonly NumericUpDown _numDjxlThreads   = new NumericUpDown();
        private readonly NumericUpDown _numDjxlReps      = new NumericUpDown();
        private readonly TextBox _txtPreviewOut          = new TextBox();
        private readonly TextBox _txtIccOut              = new TextBox();
        private readonly TextBox _txtOrigIccOut          = new TextBox();
        private readonly TextBox _txtMetadataOut         = new TextBox();

        // Container (jxltran) & CLI controls
        private readonly CheckBox _chkTranPack      = new CheckBox();
        private readonly CheckBox _chkTranExtract   = new CheckBox();
        private readonly CheckBox _chkTranQuiet     = new CheckBox();
        private readonly TextBox _txtAddBoxSpecs    = new TextBox();
        private readonly TextBox _txtRemoveBoxTypes = new TextBox();
        private readonly ComboBox _cmbCliTool       = new ComboBox();
        private readonly TextBox _txtCliRawArgs     = new TextBox();

        /// <summary>
        /// Initializes a new instance of the <see cref="MainForm"/> class.
        /// </summary>
        public MainForm()
            : this(new JxlImageService())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MainForm"/> class with the specified <see cref="JxlImageService"/>.
        /// </summary>
        /// <param name="imageService">Image loading, conversion, and CLI service.</param>
        public MainForm(JxlImageService imageService)
        {
            ArgumentNullException.ThrowIfNull(imageService);

            _imageService = imageService;
            InitializeFormLayout();
            InitializeDefaultControlValues();
        }

        /// <summary>Gets the originally loaded source image displayed on the left side of the preview slider, if any.</summary>
        public JxlLoadedImage? OriginalImage => _originalImage;

        /// <summary>Gets the currently active (converted) image, if any.</summary>
        public JxlLoadedImage? CurrentImage => _currentImage;

        /// <summary>Gets the most recent <see cref="JxlPropertyReport"/>, if a JXL image is loaded or converted.</summary>
        public JxlPropertyReport? CurrentPropertyReport => _currentImage?.JxlReport ?? _originalImage?.JxlReport;

        /// <summary>Gets the JXL properties <see cref="DataGridView"/> control.</summary>
        public DataGridView PropertiesGrid => _propertiesGrid;

        /// <summary>Gets the verbose <c>jxlinfo</c> output <see cref="TextBox"/> control.</summary>
        public TextBox VerboseInfoTextBox => _verboseInfoTextBox;

        /// <summary>Gets the CLI output and activity log <see cref="TextBox"/> control.</summary>
        public TextBox ConsoleLogTextBox => _consoleLogTextBox;

        /// <summary>Gets the split image preview <see cref="SplitPreviewPictureBox"/> control.</summary>
        public SplitPreviewPictureBox PreviewPictureBox => _previewPictureBox;

        /// <summary>Gets the status bar <see cref="ToolStripStatusLabel"/> control.</summary>
        public ToolStripStatusLabel StatusLabel => _statusLabel;

        /// <summary>Gets the right-hand <see cref="TabControl"/> containing properties, encode, decode, container, and CLI tabs.</summary>
        public TabControl RightTabControl => _rightTabControl;

        /// <summary>Gets the JXL Properties <see cref="TabPage"/>.</summary>
        public TabPage PropertiesTabPage => _propertiesTabPage;

        /// <summary>Gets the Encode (<c>cjxl</c>) <see cref="TabPage"/>.</summary>
        public TabPage EncodeTabPage => _encodeTabPage;

        /// <summary>Gets the Decode (<c>djxl</c>) <see cref="TabPage"/>.</summary>
        public TabPage DecodeTabPage => _decodeTabPage;

        /// <summary>
        /// Loads an image file from disk, displays it in single-image mode on the JXL Properties tab,
        /// and enables the Decode tab only when the loaded file is an encoded JXL image.
        /// </summary>
        /// <param name="filePath">Path to the image file.</param>
        public void LoadFromFilePath(string filePath)
        {
            JxlLoadedImage loaded = _imageService.LoadImageFile(filePath, new DecompressArgs());

            SetOriginalLoadedImage(loaded);
            AppendLog($"Loaded {loaded.DetectedCodec} file: {filePath} ({loaded.RawBytes.Length} bytes, {loaded.PixelFile.XSize}x{loaded.PixelFile.YSize}).");
        }

        /// <summary>
        /// Loads an image from an in-memory byte array, displays it in single-image mode on the JXL Properties tab,
        /// and enables the Decode tab only when the loaded image is an encoded JXL image.
        /// </summary>
        /// <param name="data">Encoded image bytes.</param>
        /// <param name="sourceLabel">Source label or filename hint.</param>
        public void LoadFromBytes(byte[] data, string sourceLabel)
        {
            JxlLoadedImage loaded = _imageService.LoadImageBytes(data, sourceLabel, new DecompressArgs());

            SetOriginalLoadedImage(loaded);
            AppendLog($"Loaded {loaded.DetectedCodec} from memory: {sourceLabel} ({data.Length} bytes).");
        }

        /// <summary>
        /// Converts the original loaded image to JPEG XL using the current UI <see cref="CompressArgs"/>
        /// and updates the split comparison preview and JXL properties display.
        /// </summary>
        public void ConvertCurrentToJxlAndPreview()
        {
            if (_originalImage is null && _currentImage is null)
            {
                AppendLog("No image loaded to convert.");

                return;
            }

            CompressArgs cargs    = ReadCompressArgsFromUi();
            JxlLoadedImage source = _originalImage ?? _currentImage!.Value;
            byte[] jxlBytes       = _imageService.ConvertToJxlBytes(
                source.PixelFile,
                source.OriginalJpegBytes,
                cargs,
                out _,
                out string logMsg);

            DecompressArgs dargs     = ReadDecompressArgsFromUi();
            JxlLoadedImage converted = _imageService.LoadImageBytes(jxlBytes, "(Converted.jxl)", dargs);

            _lastConversionWasJpeg = false;
            SetConvertedLoadedImage(converted);
            AppendLog(logMsg);
        }

        /// <summary>
        /// Decodes the loaded JPEG XL bitstream using the current UI <see cref="DecompressArgs"/>
        /// and updates the right side of the split comparison preview display when the loaded image is a JXL image.
        /// </summary>
        public void DecodeCurrentJxlAndPreview()
        {
            if (_originalImage is null)
            {
                AppendLog("No image loaded to decode.");

                return;
            }

            if (_originalImage.Value.DetectedCodec != Codec.Jxl)
            {
                _decodeTabPage.Enabled = false;
                AppendLog("Loaded image is not an encoded JXL image; decode preview is disabled.");

                return;
            }

            DecompressArgs dargs = ReadDecompressArgsFromUi();

            if (dargs.PixelsToJpeg)
            {
                ConvertCurrentToJpegAndPreview();

                return;
            }

            JxlLoadedImage decoded = _imageService.LoadImageBytes(_originalImage.Value.RawBytes, "(Decoded.jxl)", dargs);

            _lastConversionWasJpeg = false;
            SetConvertedLoadedImage(decoded);
            AppendLog($"Decoded JXL preview ({decoded.PixelFile.XSize}x{decoded.PixelFile.YSize}, downsampling 1:{Math.Max(1u, (uint)dargs.MaxDownsampling)}).");
        }

        /// <summary>
        /// Converts the original loaded image to JPEG using the current UI <see cref="DecompressArgs"/>
        /// and updates the split comparison preview display.
        /// </summary>
        public void ConvertCurrentToJpegAndPreview()
        {
            if (_originalImage is null && _currentImage is null)
            {
                AppendLog("No image loaded to convert.");

                return;
            }

            DecompressArgs dargs  = ReadDecompressArgsFromUi();
            JxlLoadedImage source = _originalImage ?? _currentImage!.Value;
            byte[] jpgBytes;
            string logMsg;

            if (source.DetectedCodec == Codec.Jxl)
            {
                jpgBytes = _imageService.ConvertJxlToJpegBytes(source.RawBytes, dargs, out _, out logMsg);
            }
            else if (_currentImage?.DetectedCodec == Codec.Jxl)
            {
                jpgBytes = _imageService.ConvertJxlToJpegBytes(_currentImage.Value.RawBytes, dargs, out _, out logMsg);
            }
            else
            {
                jpgBytes = _imageService.EncodeBitmapToJpegBytes(source.PreviewBitmap, (long)dargs.JpegQuality);
                logMsg   = $"Encoded preview to JPEG ({jpgBytes.Length} bytes, quality {dargs.JpegQuality}).";
            }

            JxlLoadedImage converted = _imageService.LoadImageBytes(jpgBytes, "(Converted.jpg)", dargs);

            _lastConversionWasJpeg = true;
            SetConvertedLoadedImage(converted);
            AppendLog(logMsg);
        }

        /// <summary>
        /// Saves the currently active image to <paramref name="outputPath"/> using the current UI options.
        /// </summary>
        /// <param name="outputPath">Destination file path.</param>
        /// <returns><c>true</c> if saved successfully; otherwise <c>false</c>.</returns>
        public bool SaveCurrentImageToPath(string outputPath)
        {
            if (_currentImage is null)
            {
                AppendLog("No image loaded to save.");

                return false;
            }

            CompressArgs cargs   = ReadCompressArgsFromUi();
            DecompressArgs dargs = ReadDecompressArgsFromUi();
            bool saved           = _imageService.SaveImageFile(outputPath, _currentImage.Value, cargs, dargs, out string logMsg);

            AppendLog(logMsg);

            return saved;
        }

        /// <summary>
        /// Executes the <c>cjxl</c> CLI tool from <paramref name="inputPath"/> to <paramref name="outputPath"/>
        /// using the current UI <see cref="CompressArgs"/>.
        /// </summary>
        /// <param name="inputPath">Input image path.</param>
        /// <param name="outputPath">Output JXL path.</param>
        /// <returns>CLI tool exit code.</returns>
        public int ExecuteCjxlToPath(string inputPath, string outputPath)
        {
            CompressArgs cargs = ReadCompressArgsFromUi();
            cargs.FileIn       = inputPath;
            cargs.FileOut      = outputPath;

            int exitCode = _imageService.RunCjxl(cargs, out string stdout, out string stderr);

            AppendCliResult("cjxl", exitCode, stdout, stderr);

            if (exitCode == 0 && File.Exists(outputPath))
            {
                ApplyCliOutputFileToPreview(outputPath);
            }

            return exitCode;
        }

        /// <summary>
        /// Executes the <c>djxl</c> CLI tool from <paramref name="inputPath"/> to <paramref name="outputPath"/>
        /// using the current UI <see cref="DecompressArgs"/>.
        /// </summary>
        /// <param name="inputPath">Input JXL path.</param>
        /// <param name="outputPath">Output image path.</param>
        /// <returns>CLI tool exit code.</returns>
        public int ExecuteDjxlToPath(string inputPath, string outputPath)
        {
            DecompressArgs dargs = ReadDecompressArgsFromUi();
            dargs.FileIn         = inputPath;
            dargs.FileOut        = outputPath;

            int exitCode = _imageService.RunDjxl(dargs, out string stdout, out string stderr);

            AppendCliResult("djxl", exitCode, stdout, stderr);

            if (exitCode == 0 && File.Exists(outputPath))
            {
                ApplyCliOutputFileToPreview(outputPath);
            }

            return exitCode;
        }

        /// <summary>
        /// Executes the <c>jxlinfo</c> CLI tool on <paramref name="inputPath"/> and updates the log.
        /// </summary>
        /// <param name="inputPath">Input JXL file path.</param>
        /// <param name="verbose">Whether verbose output is enabled.</param>
        /// <returns>CLI tool exit code.</returns>
        public int ExecuteJxlInfoOnPath(string inputPath, bool verbose)
        {
            int exitCode = _imageService.RunJxlInfo(inputPath, verbose, out string stdout, out string stderr);

            AppendCliResult("jxlinfo", exitCode, stdout, stderr);

            return exitCode;
        }

        /// <summary>
        /// Executes the <c>jxltran</c> CLI tool from <paramref name="inputPath"/> to <paramref name="outputPath"/>
        /// using the current UI <see cref="JxlTranArgs"/>.
        /// </summary>
        /// <param name="inputPath">Input JXL file path.</param>
        /// <param name="outputPath">Output JXL file path.</param>
        /// <returns>CLI tool exit code.</returns>
        public int ExecuteJxlTranOnPath(string inputPath, string outputPath)
        {
            JxlTranArgs targs = ReadJxlTranArgsFromUi();
            targs.FileIn      = inputPath;
            targs.FileOut     = outputPath;

            int exitCode = _imageService.RunJxlTran(targs, out string stdout, out string stderr);

            AppendCliResult("jxltran", exitCode, stdout, stderr);

            if (exitCode == 0 && File.Exists(outputPath))
            {
                ApplyCliOutputFileToPreview(outputPath);
            }

            return exitCode;
        }

        /// <summary>
        /// Applies the current <c>jxltran</c> container options in-memory to the currently loaded JXL image.
        /// </summary>
        /// <returns><c>true</c> if the transformation succeeded; otherwise <c>false</c>.</returns>
        public bool ApplyJxlTranToCurrentImage()
        {
            if (_currentImage is null || _currentImage.Value.DetectedCodec != Codec.Jxl)
            {
                AppendLog("Load or convert to a JXL image before running jxltran.");

                return false;
            }

            string tempIn  = Path.Combine(Path.GetTempPath(), $"jxltran_in_{Guid.NewGuid():N}.jxl");
            string tempOut = Path.Combine(Path.GetTempPath(), $"jxltran_out_{Guid.NewGuid():N}.jxl");

            try
            {
                File.WriteAllBytes(tempIn, _currentImage.Value.RawBytes);
                int code = ExecuteJxlTranOnPath(tempIn, tempOut);

                return code == 0;
            }
            finally
            {
                if (File.Exists(tempIn))
                {
                    File.Delete(tempIn);
                }

                if (File.Exists(tempOut))
                {
                    File.Delete(tempOut);
                }
            }
        }

        /// <summary>
        /// Computes PSNR and SSIMULACRA2 quality metrics between the initial reference image and the current image.
        /// </summary>
        /// <returns><c>true</c> if metrics were computed; otherwise <c>false</c>.</returns>
        public bool ComputeAndLogCurrentQualityMetrics()
        {
            if (_referencePixelFile is null || _currentImage is null)
            {
                AppendLog("Load an image first to compute quality metrics.");

                return false;
            }

            bool ok = _imageService.ComputeImageMetrics(
                _referencePixelFile,
                _currentImage.Value.PixelFile,
                out double psnr,
                out double ssimulacra2);

            if (!ok)
            {
                AppendLog("Cannot compute metrics (dimensions mismatch or empty frames).");

                return false;
            }

            AppendLog(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Quality Metrics vs Reference: PSNR = {0:F2} dB, SSIMULACRA2 = {1:F2}",
                    psnr,
                    ssimulacra2));

            return true;
        }

        /// <summary>
        /// Sets the <see cref="PictureBoxSizeMode"/> of the preview canvas.
        /// </summary>
        /// <param name="sizeMode">Desired <see cref="PictureBoxSizeMode"/>.</param>
        public void SetZoomMode(PictureBoxSizeMode sizeMode)
        {
            _previewPictureBox.SizeMode = sizeMode;
            _previewPictureBox.Dock     = sizeMode == PictureBoxSizeMode.AutoSize ? DockStyle.None : DockStyle.Fill;
        }

        /// <summary>
        /// Reads all <c>cjxl</c> conversion controls from the UI into a <see cref="CompressArgs"/> object.
        /// </summary>
        /// <returns>Populated <see cref="CompressArgs"/>.</returns>
        public CompressArgs ReadCompressArgsFromUi()
        {
            CompressArgs cargs = new CompressArgs
            {
                Distance           = (float)_numDistance.Value,
                GotDistance        = !_chkUseQuality.Checked,
                Quality            = (float)_numQuality.Value,
                GotQuality         = _chkUseQuality.Checked,
                AlphaDistance      = (float)_numAlphaDistance.Value,
                GotAlphaDistance   = _numAlphaDistance.Value >= 0,
                Effort             = (long)_numEffort.Value,
                GotEffort          = true,
                LosslessJpeg       = SelectedOverride(_cmbLosslessJpeg),
                GotLosslessJpeg    = SelectedOverride(_cmbLosslessJpeg) != Override.Default,
                Modular            = SelectedOverride(_cmbModular),
                Container          = SelectedOverride(_cmbContainer),
                CompressBoxes      = _chkCompressBoxes.Checked,
                Progressive        = _chkProgressive.Checked,
                ProgressiveAc      = SelectedOverride(_cmbProgressiveAc),
                QProgressiveAc     = SelectedOverride(_cmbQProgressiveAc),
                ProgressiveDc      = (long)_numProgressiveDc.Value,
                GotProgressiveDc   = _numProgressiveDc.Value >= 0,
                Responsive         = (long)_numResponsive.Value,
                Epf                = (long)_numEpf.Value,
                FasterDecoding     = (long)_numFasterDecoding.Value,
                Resampling         = (long)_numResampling.Value,
                EcResampling       = (long)_numEcResampling.Value,
                BrotliEffort       = (long)_numBrotliEffort.Value,
                Gaborish           = SelectedOverride(_cmbGaborish),
                Dots               = SelectedOverride(_cmbDots),
                Patches            = SelectedOverride(_cmbPatches),
                KeepInvisible      = SelectedOverride(_cmbKeepInvisible),
                GroupOrder         = SelectedOverride(_cmbGroupOrder),
                JpegReconCfl       = SelectedOverride(_cmbJpegReconCfl),
                CenterX            = (long)_numCenterX.Value,
                CenterY            = (long)_numCenterY.Value,
                PhotonNoiseIso     = (float)_numPhotonNoise.Value,
                IntensityTarget    = (float)_numIntensity.Value,
                OverrideBitdepth   = (nuint)_numOverrideBits.Value,
                CodestreamLevel    = (int)_numCodestreamLvl.Value,
                UpsamplingMode     = (long)_numUpsamplingMode.Value,
                Premultiply        = (int)_numPremultiply.Value,
                StripAlpha         = (int)_numStripAlpha.Value,
                Buffering          = (long)_numBuffering.Value,
                NumThreads         = (long)_numCjxlThreads.Value,
                NumReps            = (nuint)_numCjxlReps.Value,
                AllowExpertOptions = _chkAllowExpert.Checked,
                AlreadyDownsampled = _chkAlreadyDownsampled.Checked,
                StreamingInput     = _chkStreamingInput.Checked,
                StreamingOutput    = _chkStreamingOutput.Checked,
                Quiet              = _chkCjxlQuiet.Checked,
            };

            ParseColorHintsText(_txtColorHints.Text, cargs.ColorHints);

            return cargs;
        }

        /// <summary>
        /// Applies the values of a <see cref="CompressArgs"/> object to the UI controls and refreshes the converted preview.
        /// </summary>
        /// <param name="cargs">Compression arguments to apply.</param>
        public void ApplyCompressArgsToUi(CompressArgs cargs)
        {
            ArgumentNullException.ThrowIfNull(cargs);

            _suppressAutoConvert = true;

            try
            {
                _chkUseQuality.Checked         = cargs.GotQuality && !cargs.GotDistance;
                _numDistance.Value             = ClampDecimal((decimal)cargs.Distance, _numDistance);
                _numQuality.Value              = ClampDecimal((decimal)cargs.Quality, _numQuality);
                _numAlphaDistance.Value        = ClampDecimal((decimal)cargs.AlphaDistance, _numAlphaDistance);
                _numEffort.Value               = ClampDecimal(cargs.Effort, _numEffort);
                SetOverrideCombo(_cmbLosslessJpeg, cargs.LosslessJpeg);
                SetOverrideCombo(_cmbModular, cargs.Modular);
                SetOverrideCombo(_cmbContainer, cargs.Container);
                _chkCompressBoxes.Checked      = cargs.CompressBoxes;
                _chkProgressive.Checked        = cargs.Progressive;
                SetOverrideCombo(_cmbProgressiveAc, cargs.ProgressiveAc);
                SetOverrideCombo(_cmbQProgressiveAc, cargs.QProgressiveAc);
                _numProgressiveDc.Value        = ClampDecimal(cargs.ProgressiveDc, _numProgressiveDc);
                _numResponsive.Value           = ClampDecimal(cargs.Responsive, _numResponsive);
                _numEpf.Value                  = ClampDecimal(cargs.Epf, _numEpf);
                _numFasterDecoding.Value       = ClampDecimal(cargs.FasterDecoding, _numFasterDecoding);
                _numResampling.Value           = ClampDecimal(cargs.Resampling, _numResampling);
                _numEcResampling.Value         = ClampDecimal(cargs.EcResampling, _numEcResampling);
                _numBrotliEffort.Value         = ClampDecimal(cargs.BrotliEffort, _numBrotliEffort);
                SetOverrideCombo(_cmbGaborish, cargs.Gaborish);
                SetOverrideCombo(_cmbDots, cargs.Dots);
                SetOverrideCombo(_cmbPatches, cargs.Patches);
                SetOverrideCombo(_cmbKeepInvisible, cargs.KeepInvisible);
                SetOverrideCombo(_cmbGroupOrder, cargs.GroupOrder);
                SetOverrideCombo(_cmbJpegReconCfl, cargs.JpegReconCfl);
                ApplyExtendedCompressArgsToUi(cargs);
            }
            finally
            {
                _suppressAutoConvert = false;
            }

            _lastConversionWasJpeg = false;
            RefreshConvertedPreviewOnSettingsChanged();
        }

        /// <summary>
        /// Reads all <c>djxl</c> decompression controls from the UI into a <see cref="DecompressArgs"/> object.
        /// </summary>
        /// <returns>Populated <see cref="DecompressArgs"/>.</returns>
        public DecompressArgs ReadDecompressArgsFromUi()
        {
            return new DecompressArgs
            {
                PixelsToJpeg              = _chkPixelsToJpeg.Checked,
                JpegQuality               = (nuint)_numJpegQuality.Value,
                UseSjpeg                  = _chkUseSjpeg.Checked,
                BitsPerSample             = (int)_numBitsPerSample.Value,
                ColorSpace                = _cmbColorSpace.Text?.Trim() ?? string.Empty,
                DisplayNits               = (double)_numDisplayNits.Value,
                AlphaBlend                = _cmbAlphaBlend.Text?.Trim() ?? string.Empty,
                UnpremultiplyAlpha        = _chkUnpremultiplyAlpha.Checked,
                MaxDownsampling           = (nuint)_numDownsampling.Value,
                MaxPasses                 = _numMaxPasses.Value == 0 ? uint.MaxValue : (uint)_numMaxPasses.Value,
                AllowPartialFiles         = _chkAllowPartial.Checked,
                AllowMoreProgressiveSteps = _chkAllowMoreSteps.Checked,
                RenderSpotcolors          = _chkRenderSpotcolors.Checked,
                Coalescing                = _chkCoalescing.Checked,
                OutputFrames              = _chkOutputFrames.Checked,
                OutputExtraChannels       = _chkOutputExtraChans.Checked,
                UseImageCallback          = _chkUseImageCallback.Checked,
                PrintReadBytes            = _chkPrintReadBytes.Checked,
                Quiet                     = _chkDjxlQuiet.Checked,
                NumThreads                = (int)_numDjxlThreads.Value,
                NumReps                   = (nuint)_numDjxlReps.Value,
                PreviewOut                = _txtPreviewOut.Text?.Trim() ?? string.Empty,
                IccOut                    = _txtIccOut.Text?.Trim() ?? string.Empty,
                OrigIccOut                = _txtOrigIccOut.Text?.Trim() ?? string.Empty,
                MetadataOut               = _txtMetadataOut.Text?.Trim() ?? string.Empty,
            };
        }

        /// <summary>
        /// Applies the values of a <see cref="DecompressArgs"/> object to the UI controls and refreshes the converted preview.
        /// </summary>
        /// <param name="dargs">Decompression arguments to apply.</param>
        public void ApplyDecompressArgsToUi(DecompressArgs dargs)
        {
            ArgumentNullException.ThrowIfNull(dargs);

            _suppressAutoConvert = true;

            try
            {
                _chkPixelsToJpeg.Checked       = dargs.PixelsToJpeg;
                _numJpegQuality.Value          = ClampDecimal(dargs.JpegQuality, _numJpegQuality);
                _chkUseSjpeg.Checked           = dargs.UseSjpeg;
                _numBitsPerSample.Value        = ClampDecimal(dargs.BitsPerSample, _numBitsPerSample);
                _cmbColorSpace.Text            = dargs.ColorSpace ?? string.Empty;
                _numDisplayNits.Value          = ClampDecimal((decimal)dargs.DisplayNits, _numDisplayNits);
                _cmbAlphaBlend.Text            = dargs.AlphaBlend ?? string.Empty;
                _chkUnpremultiplyAlpha.Checked = dargs.UnpremultiplyAlpha;
                _numDownsampling.Value         = ClampDecimal(dargs.MaxDownsampling, _numDownsampling);
                _numMaxPasses.Value            = dargs.MaxPasses == uint.MaxValue ? 0 : ClampDecimal(dargs.MaxPasses, _numMaxPasses);
                _chkAllowPartial.Checked       = dargs.AllowPartialFiles;
                _chkAllowMoreSteps.Checked     = dargs.AllowMoreProgressiveSteps;
                _chkRenderSpotcolors.Checked   = dargs.RenderSpotcolors;
                _chkCoalescing.Checked         = dargs.Coalescing;
                _chkOutputFrames.Checked       = dargs.OutputFrames;
                _chkOutputExtraChans.Checked   = dargs.OutputExtraChannels;
                _chkUseImageCallback.Checked   = dargs.UseImageCallback;
                _chkPrintReadBytes.Checked     = dargs.PrintReadBytes;
                _chkDjxlQuiet.Checked          = dargs.Quiet;
                _numDjxlThreads.Value          = ClampDecimal(dargs.NumThreads, _numDjxlThreads);
                _numDjxlReps.Value             = ClampDecimal(dargs.NumReps, _numDjxlReps);
                _txtPreviewOut.Text            = dargs.PreviewOut ?? string.Empty;
                _txtIccOut.Text                = dargs.IccOut ?? string.Empty;
                _txtOrigIccOut.Text            = dargs.OrigIccOut ?? string.Empty;
                _txtMetadataOut.Text           = dargs.MetadataOut ?? string.Empty;
            }
            finally
            {
                _suppressAutoConvert = false;
            }

            RefreshDecodedPreviewOnSettingsChanged();
        }

        /// <summary>
        /// Reads all <c>jxltran</c> container controls from the UI into a <see cref="JxlTranArgs"/> object.
        /// </summary>
        /// <returns>Populated <see cref="JxlTranArgs"/>.</returns>
        public JxlTranArgs ReadJxlTranArgsFromUi()
        {
            JxlTranArgs targs = new JxlTranArgs
            {
                Pack    = _chkTranPack.Checked,
                Extract = _chkTranExtract.Checked,
                Quiet   = _chkTranQuiet.Checked,
            };

            PopulateSemicolonList(_txtAddBoxSpecs.Text, targs.AddBoxSpecs);
            PopulateSemicolonList(_txtRemoveBoxTypes.Text, targs.RemoveBoxTypes);

            return targs;
        }

        /// <summary>
        /// Applies the values of a <see cref="JxlTranArgs"/> object to the UI controls.
        /// </summary>
        /// <param name="targs">Container transformation arguments to apply.</param>
        public void ApplyJxlTranArgsToUi(JxlTranArgs targs)
        {
            ArgumentNullException.ThrowIfNull(targs);

            _chkTranPack.Checked    = targs.Pack;
            _chkTranExtract.Checked = targs.Extract;
            _chkTranQuiet.Checked   = targs.Quiet;
            _txtAddBoxSpecs.Text    = string.Join(";", targs.AddBoxSpecs);
            _txtRemoveBoxTypes.Text = string.Join(";", targs.RemoveBoxTypes);
        }

        /// <summary>
        /// Executes a raw command line against the selected CLI tool (<c>cjxl</c>, <c>djxl</c>, <c>jxlinfo</c>, or <c>jxltran</c>).
        /// </summary>
        /// <param name="toolName">Tool name (<c>cjxl</c>, <c>djxl</c>, <c>jxlinfo</c>, <c>jxltran</c>).</param>
        /// <param name="rawArguments">Space-separated command-line arguments.</param>
        /// <returns>CLI tool exit code.</returns>
        public int ExecuteRawCliCommand(string toolName, string rawArguments)
        {
            string[] args            = SplitCommandLine(rawArguments);
            using StringWriter outSw = new StringWriter();
            using StringWriter errSw = new StringWriter();
            string normalized        = (toolName ?? "cjxl").Trim().ToLowerInvariant();

            int exitCode = normalized switch
            {
                "djxl"    => DjxlTool.Run(args, outSw, errSw),
                "jxlinfo" => JxlInfoTool.Run(args, outSw, errSw),
                "jxltran" => JxlTranTool.Run(args, outSw, errSw),
                _         => CjxlTool.Run(args, outSw, errSw),
            };

            AppendCliResult(normalized, exitCode, outSw.ToString(), errSw.ToString());

            return exitCode;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposePreviousLoadedBitmaps(null);
                _originalImage = null;
                _currentImage  = null;
            }

            base.Dispose(disposing);
        }

        private void ApplyExtendedCompressArgsToUi(CompressArgs cargs)
        {
            _numCenterX.Value              = ClampDecimal(cargs.CenterX, _numCenterX);
            _numCenterY.Value              = ClampDecimal(cargs.CenterY, _numCenterY);
            _numPhotonNoise.Value          = ClampDecimal((decimal)cargs.PhotonNoiseIso, _numPhotonNoise);
            _numIntensity.Value            = ClampDecimal((decimal)cargs.IntensityTarget, _numIntensity);
            _numOverrideBits.Value         = ClampDecimal(cargs.OverrideBitdepth, _numOverrideBits);
            _numCodestreamLvl.Value        = ClampDecimal(cargs.CodestreamLevel, _numCodestreamLvl);
            _numUpsamplingMode.Value       = ClampDecimal(cargs.UpsamplingMode, _numUpsamplingMode);
            _numPremultiply.Value          = ClampDecimal(cargs.Premultiply, _numPremultiply);
            _numStripAlpha.Value           = ClampDecimal(cargs.StripAlpha, _numStripAlpha);
            _numBuffering.Value            = ClampDecimal(cargs.Buffering, _numBuffering);
            _numCjxlThreads.Value          = ClampDecimal(cargs.NumThreads, _numCjxlThreads);
            _numCjxlReps.Value             = ClampDecimal(cargs.NumReps, _numCjxlReps);
            _chkAllowExpert.Checked        = cargs.AllowExpertOptions;
            _chkAlreadyDownsampled.Checked = cargs.AlreadyDownsampled;
            _chkStreamingInput.Checked     = cargs.StreamingInput;
            _chkStreamingOutput.Checked    = cargs.StreamingOutput;
            _chkCjxlQuiet.Checked          = cargs.Quiet;
        }

        private void SetOriginalLoadedImage(JxlLoadedImage loaded)
        {
            DisposePreviousLoadedBitmaps(loaded.PreviewBitmap);

            _originalImage         = loaded;
            _currentImage          = loaded;
            _referencePixelFile    = loaded.PixelFile;
            _lastConversionWasJpeg = false;
            _decodeTabPage.Enabled = loaded.DetectedCodec == Codec.Jxl;

            _suppressAutoConvert = true;

            try
            {
                _rightTabControl.SelectedTab = _propertiesTabPage;
            }
            finally
            {
                _suppressAutoConvert = false;
            }

            _previewPictureBox.SetSingleImage(loaded.PreviewBitmap);
            UpdatePropertiesDisplay(loaded.JxlReport);
            UpdateStatusBar(in loaded);
        }

        private void SetConvertedLoadedImage(JxlLoadedImage converted)
        {
            Bitmap? oldConverted = _currentImage?.PreviewBitmap;
            Bitmap? origBitmap   = _originalImage?.PreviewBitmap;

            _currentImage = converted;
            _previewPictureBox.SetComparisonImages(origBitmap ?? converted.PreviewBitmap, converted.PreviewBitmap);

            if (oldConverted is not null &&
                !ReferenceEquals(oldConverted, origBitmap) &&
                !ReferenceEquals(oldConverted, converted.PreviewBitmap))
            {
                oldConverted.Dispose();
            }

            UpdatePropertiesDisplay(converted.JxlReport ?? _originalImage?.JxlReport);
            UpdateStatusBar(in converted);
        }

        private void ApplyCliOutputFileToPreview(string outputPath)
        {
            if (_originalImage is null)
            {
                LoadFromFilePath(outputPath);

                return;
            }

            DecompressArgs dargs     = ReadDecompressArgsFromUi();
            JxlLoadedImage cliOutput = _imageService.LoadImageFile(outputPath, dargs);

            SetConvertedLoadedImage(cliOutput);
        }

        private void DisposePreviousLoadedBitmaps(Bitmap? keepBitmap)
        {
            Bitmap? origBmp = _originalImage?.PreviewBitmap;
            Bitmap? currBmp = _currentImage?.PreviewBitmap;

            if (origBmp is not null && !ReferenceEquals(origBmp, keepBitmap))
            {
                origBmp.Dispose();
            }

            if (currBmp is not null &&
                !ReferenceEquals(currBmp, origBmp) &&
                !ReferenceEquals(currBmp, keepBitmap))
            {
                currBmp.Dispose();
            }
        }

        private void OnRightTabSelecting(object? sender, TabControlCancelEventArgs e)
        {
            if (e.TabPage == _decodeTabPage && !_decodeTabPage.Enabled)
            {
                e.Cancel = true;
            }
        }

        private void OnRightTabSelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_suppressAutoConvert || _originalImage is null)
            {
                return;
            }

            if (_rightTabControl.SelectedTab == _propertiesTabPage)
            {
                _previewPictureBox.SetSingleImage(_originalImage.Value.PreviewBitmap);
            }
            else if (_rightTabControl.SelectedTab == _encodeTabPage)
            {
                ConvertCurrentToJxlAndPreview();
            }
            else if (_rightTabControl.SelectedTab == _decodeTabPage)
            {
                if (_originalImage.Value.DetectedCodec == Codec.Jxl)
                {
                    DecodeCurrentJxlAndPreview();
                }
                else
                {
                    _decodeTabPage.Enabled = false;
                }
            }
        }

        private void RefreshConvertedPreviewOnSettingsChanged()
        {
            if (_suppressAutoConvert || _originalImage is null)
            {
                return;
            }

            try
            {
                if (_lastConversionWasJpeg)
                {
                    ConvertCurrentToJpegAndPreview();
                }
                else
                {
                    ConvertCurrentToJxlAndPreview();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Auto-conversion error: {ex.Message}");
            }
        }

        private void RefreshDecodedPreviewOnSettingsChanged()
        {
            if (_suppressAutoConvert ||
                _originalImage is null ||
                _originalImage.Value.DetectedCodec != Codec.Jxl)
            {
                return;
            }

            try
            {
                if (_lastConversionWasJpeg || _chkPixelsToJpeg.Checked)
                {
                    ConvertCurrentToJpegAndPreview();
                }
                else
                {
                    DecodeCurrentJxlAndPreview();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Auto-decode error: {ex.Message}");
            }
        }

        private void OnEncodeSettingChanged(object? sender, EventArgs e)
        {
            if (_suppressAutoConvert || _originalImage is null)
            {
                return;
            }

            _lastConversionWasJpeg = false;
            RefreshConvertedPreviewOnSettingsChanged();
        }

        private void OnDecodeSettingChanged(object? sender, EventArgs e)
        {
            RefreshDecodedPreviewOnSettingsChanged();
        }

        private void UpdatePropertiesDisplay(JxlPropertyReport? report)
        {
            _propertiesGrid.Rows.Clear();

            if (report is null)
            {
                _verboseInfoTextBox.Text = "Load or convert to a JPEG XL (.jxl) image to inspect all JXL properties.";

                return;
            }

            foreach (JxlPropertyRow row in report.PropertyRows)
            {
                _propertiesGrid.Rows.Add(row.Category, row.Name, row.Value);
            }

            _verboseInfoTextBox.Text = report.VerboseInfoText.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }

        private void UpdateStatusBar(in JxlLoadedImage loaded)
        {
            string reconInfo = loaded.JxlReport?.HasJpegReconstruction == true ? " [Lossless JPEG jbrd]" : string.Empty;

            _statusLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} | Format: {1}{2} | Dimensions: {3}x{4} | Size: {5:N0} bytes",
                loaded.SourcePath,
                loaded.DetectedCodec,
                reconInfo,
                loaded.PixelFile.XSize,
                loaded.PixelFile.YSize,
                loaded.RawBytes.Length);
        }

        private void AppendCliResult(string toolName, int exitCode, string stdout, string stderr)
        {
            AppendLog($"[{toolName}] Exit Code: {exitCode}");

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                AppendLog(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AppendLog(stderr.TrimEnd());
            }
        }

        private void AppendLog(string message)
        {
            string timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

            _consoleLogTextBox.AppendText(timestamped);
        }

        private void InitializeFormLayout()
        {
            Text          = "JxlSharp Studio — JPEG XL & JPEG Viewer, Converter & CLI Workbench";
            Size          = new Size(1360, 860);
            MinimumSize   = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;

            ToolStrip topStrip        = BuildTopToolStrip();
            StatusStrip statusStrip   = BuildStatusStrip();
            SplitContainer mainSplit  = BuildMainSplitContainer();

            Controls.Add(mainSplit);
            Controls.Add(topStrip);
            Controls.Add(statusStrip);
        }

        private ToolStrip BuildTopToolStrip()
        {
            ToolStrip strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

            strip.Items.Add(new ToolStripButton("Open Image...", null, (_, _) => OnOpenFileClicked()));
            strip.Items.Add(new ToolStripButton("Save As...", null, (_, _) => OnSaveFileClicked()));
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(new ToolStripButton("Convert -> JXL", null, (_, _) => ConvertCurrentToJxlAndPreview()));
            strip.Items.Add(new ToolStripButton("Decode JXL", null, (_, _) => DecodeCurrentJxlAndPreview()));
            strip.Items.Add(new ToolStripButton("Convert -> JPG", null, (_, _) => ConvertCurrentToJpegAndPreview()));
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(new ToolStripButton("Fit Window", null, (_, _) => SetZoomMode(PictureBoxSizeMode.Zoom)));
            strip.Items.Add(new ToolStripButton("Actual Size (1:1)", null, (_, _) => SetZoomMode(PictureBoxSizeMode.AutoSize)));
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(new ToolStripButton("Quality Metrics", null, (_, _) => ComputeAndLogCurrentQualityMetrics()));

            return strip;
        }

        private StatusStrip BuildStatusStrip()
        {
            StatusStrip status = new StatusStrip();

            status.Items.Add(_statusLabel);

            return status;
        }

        private SplitContainer BuildMainSplitContainer()
        {
            SplitContainer mainSplit = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = Orientation.Vertical,
                SplitterDistance = 720,
            };

            SplitContainer leftSplit = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = Orientation.Horizontal,
                SplitterDistance = 560,
            };

            Panel previewPanel = new Panel
            {
                Dock       = DockStyle.Fill,
                AutoScroll = true,
                BackColor  = Color.FromArgb(36, 36, 36),
            };

            _previewPictureBox.Dock     = DockStyle.Fill;
            _previewPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            _previewPictureBox.SetSingleImage(null);
            previewPanel.Controls.Add(_previewPictureBox);

            GroupBox logGroup = new GroupBox
            {
                Text = "CLI Output & Activity Log",
                Dock = DockStyle.Fill,
            };

            _consoleLogTextBox.Dock       = DockStyle.Fill;
            _consoleLogTextBox.Multiline  = true;
            _consoleLogTextBox.ReadOnly   = true;
            _consoleLogTextBox.ScrollBars = ScrollBars.Vertical;
            _consoleLogTextBox.Font       = new Font("Consolas", 9.0f);
            logGroup.Controls.Add(_consoleLogTextBox);

            leftSplit.Panel1.Controls.Add(previewPanel);
            leftSplit.Panel2.Controls.Add(logGroup);

            _propertiesTabPage       = BuildPropertiesTabPage();
            _encodeTabPage           = BuildEncodeOptionsTabPage();
            _decodeTabPage           = BuildDecodeOptionsTabPage();
            _decodeTabPage.Enabled   = false;

            _rightTabControl.Dock = DockStyle.Fill;
            _rightTabControl.TabPages.Add(_propertiesTabPage);
            _rightTabControl.TabPages.Add(_encodeTabPage);
            _rightTabControl.TabPages.Add(_decodeTabPage);
            _rightTabControl.TabPages.Add(BuildJxlTranTabPage());
            _rightTabControl.TabPages.Add(BuildCliConsoleTabPage());
            _rightTabControl.SelectedTab           = _propertiesTabPage;
            _rightTabControl.Selecting            += OnRightTabSelecting;
            _rightTabControl.SelectedIndexChanged += OnRightTabSelectedIndexChanged;

            mainSplit.Panel1.Controls.Add(leftSplit);
            mainSplit.Panel2.Controls.Add(_rightTabControl);

            return mainSplit;
        }

        private TabPage BuildPropertiesTabPage()
        {
            TabPage tab              = new TabPage("JXL Properties");
            SplitContainer propSplit = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = Orientation.Horizontal,
                SplitterDistance = 440,
            };

            _propertiesGrid.Dock                  = DockStyle.Fill;
            _propertiesGrid.ReadOnly              = true;
            _propertiesGrid.AllowUserToAddRows    = false;
            _propertiesGrid.AllowUserToDeleteRows = false;
            _propertiesGrid.RowHeadersVisible     = false;
            _propertiesGrid.AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill;
            _propertiesGrid.Columns.Add("Category", "Category");
            _propertiesGrid.Columns.Add("Property", "Property");
            _propertiesGrid.Columns.Add("Value", "Value");

            GroupBox verboseGroup = new GroupBox
            {
                Text = "jxlinfo -v Output",
                Dock = DockStyle.Fill,
            };

            _verboseInfoTextBox.Dock       = DockStyle.Fill;
            _verboseInfoTextBox.Multiline  = true;
            _verboseInfoTextBox.ReadOnly   = true;
            _verboseInfoTextBox.ScrollBars = ScrollBars.Vertical;
            _verboseInfoTextBox.Font       = new Font("Consolas", 9.0f);
            verboseGroup.Controls.Add(_verboseInfoTextBox);

            propSplit.Panel1.Controls.Add(_propertiesGrid);
            propSplit.Panel2.Controls.Add(verboseGroup);
            tab.Controls.Add(propSplit);

            return tab;
        }

        private TabPage BuildEncodeOptionsTabPage()
        {
            TabPage tab              = new TabPage("Encode (cjxl)");
            TableLayoutPanel layout  = CreateScrollableOptionsTable();

            AddOptionRow(layout, "Use Quality (-q instead of -d):", _chkUseQuality);
            AddOptionRow(layout, "Distance (-d 0..25):", _numDistance);
            AddOptionRow(layout, "Quality (-q 0..100):", _numQuality);
            AddOptionRow(layout, "Alpha Distance (-a -1..25):", _numAlphaDistance);
            AddOptionRow(layout, "Effort (-e 1..11):", _numEffort);
            AddOptionRow(layout, "Lossless JPEG (-j):", _cmbLosslessJpeg);
            AddOptionRow(layout, "Modular Mode (-m):", _cmbModular);
            AddOptionRow(layout, "Use Container (--container):", _cmbContainer);
            AddOptionRow(layout, "Compress Boxes (--compress_boxes):", _chkCompressBoxes);
            AddOptionRow(layout, "Progressive (-p):", _chkProgressive);
            AddOptionRow(layout, "Progressive AC (--progressive_ac):", _cmbProgressiveAc);
            AddOptionRow(layout, "QProgressive AC (--qprogressive_ac):", _cmbQProgressiveAc);
            AddOptionRow(layout, "Progressive DC (--progressive_dc):", _numProgressiveDc);
            AddOptionRow(layout, "Responsive (-R):", _numResponsive);
            AddOptionRow(layout, "EPF (--epf -1..3):", _numEpf);
            AddOptionRow(layout, "Faster Decoding (0..4):", _numFasterDecoding);
            AddOptionRow(layout, "Resampling (-1,1,2,4,8):", _numResampling);
            AddOptionRow(layout, "EC Resampling (-1,1,2,4,8):", _numEcResampling);
            AddOptionRow(layout, "Brotli Effort (-1..11):", _numBrotliEffort);
            AddEncodeAdvancedOptionRows(layout);

            Button btnConvert = new Button { Text = "Convert Current Image to JXL", AutoSize = true };
            btnConvert.Click += (_, _) => ConvertCurrentToJxlAndPreview();
            AddOptionRow(layout, "Action:", btnConvert);

            tab.Controls.Add(layout);

            return tab;
        }

        private void AddEncodeAdvancedOptionRows(TableLayoutPanel layout)
        {
            AddOptionRow(layout, "Gaborish (--gaborish):", _cmbGaborish);
            AddOptionRow(layout, "Dots (--dots):", _cmbDots);
            AddOptionRow(layout, "Patches (--patches):", _cmbPatches);
            AddOptionRow(layout, "Keep Invisible (--keep_invisible):", _cmbKeepInvisible);
            AddOptionRow(layout, "Group Order (--group_order):", _cmbGroupOrder);
            AddOptionRow(layout, "JPEG Recon CfL:", _cmbJpegReconCfl);
            AddOptionRow(layout, "Group Center X (--center_x):", _numCenterX);
            AddOptionRow(layout, "Group Center Y (--center_y):", _numCenterY);
            AddOptionRow(layout, "Photon Noise ISO:", _numPhotonNoise);
            AddOptionRow(layout, "Intensity Target (nits):", _numIntensity);
            AddOptionRow(layout, "Override Bitdepth:", _numOverrideBits);
            AddOptionRow(layout, "Codestream Level (-1,5,10):", _numCodestreamLvl);
            AddOptionRow(layout, "Upsampling Mode (-1..2):", _numUpsamplingMode);
            AddOptionRow(layout, "Premultiply (-1..1):", _numPremultiply);
            AddOptionRow(layout, "Strip Alpha (-1..1):", _numStripAlpha);
            AddOptionRow(layout, "Buffering (-1..3):", _numBuffering);
            AddOptionRow(layout, "Worker Threads (--num_threads):", _numCjxlThreads);
            AddOptionRow(layout, "Benchmark Reps (--num_reps):", _numCjxlReps);
            AddOptionRow(layout, "Allow Expert Options:", _chkAllowExpert);
            AddOptionRow(layout, "Already Downsampled:", _chkAlreadyDownsampled);
            AddOptionRow(layout, "Streaming Input:", _chkStreamingInput);
            AddOptionRow(layout, "Streaming Output:", _chkStreamingOutput);
            AddOptionRow(layout, "Quiet (--quiet):", _chkCjxlQuiet);
            AddOptionRow(layout, "Color Hints (k=v;...):", _txtColorHints);
        }

        private TabPage BuildDecodeOptionsTabPage()
        {
            TabPage tab             = new TabPage("Decode (djxl)");
            TableLayoutPanel layout = CreateScrollableOptionsTable();

            AddOptionRow(layout, "Pixels to JPEG (-j):", _chkPixelsToJpeg);
            AddOptionRow(layout, "JPEG Quality (0..100):", _numJpegQuality);
            AddOptionRow(layout, "Use sjpeg (--use_sjpeg):", _chkUseSjpeg);
            AddOptionRow(layout, "Bits Per Sample (0=orig):", _numBitsPerSample);
            AddOptionRow(layout, "Output Color Space:", _cmbColorSpace);
            AddOptionRow(layout, "Display Nits (--display_nits):", _numDisplayNits);
            AddOptionRow(layout, "Alpha Blend (black/white):", _cmbAlphaBlend);
            AddOptionRow(layout, "Unpremultiply Alpha:", _chkUnpremultiplyAlpha);
            AddOptionRow(layout, "Max Downsampling (1,2,4,8):", _numDownsampling);
            AddOptionRow(layout, "Max Passes (0=all):", _numMaxPasses);
            AddOptionRow(layout, "Allow Partial Files:", _chkAllowPartial);
            AddOptionRow(layout, "Allow More Progressive Steps:", _chkAllowMoreSteps);
            AddOptionRow(layout, "Render Spotcolors:", _chkRenderSpotcolors);
            AddOptionRow(layout, "Coalescing (--coalescing):", _chkCoalescing);
            AddOptionRow(layout, "Output All Animation Frames:", _chkOutputFrames);
            AddOptionRow(layout, "Output Extra Channels:", _chkOutputExtraChans);
            AddOptionRow(layout, "Use Image Callback:", _chkUseImageCallback);
            AddOptionRow(layout, "Print Read Bytes:", _chkPrintReadBytes);
            AddOptionRow(layout, "Quiet (--quiet):", _chkDjxlQuiet);
            AddOptionRow(layout, "Worker Threads (--num_threads):", _numDjxlThreads);
            AddOptionRow(layout, "Benchmark Reps (--num_reps):", _numDjxlReps);
            AddOptionRow(layout, "Preview Out Path:", _txtPreviewOut);
            AddOptionRow(layout, "ICC Out Path:", _txtIccOut);
            AddOptionRow(layout, "Original ICC Out Path:", _txtOrigIccOut);
            AddOptionRow(layout, "Metadata Out Path:", _txtMetadataOut);

            FlowLayoutPanel actionPanel = new FlowLayoutPanel { AutoSize = true };
            Button btnDecodeJxl         = new Button { Text = "Decode Current JXL", AutoSize = true };
            Button btnConvertJpg        = new Button { Text = "Convert Current JXL to JPG", AutoSize = true };

            btnDecodeJxl.Click  += (_, _) => DecodeCurrentJxlAndPreview();
            btnConvertJpg.Click += (_, _) => ConvertCurrentToJpegAndPreview();

            actionPanel.Controls.Add(btnDecodeJxl);
            actionPanel.Controls.Add(btnConvertJpg);
            AddOptionRow(layout, "Action:", actionPanel);

            tab.Controls.Add(layout);

            return tab;
        }

        private TabPage BuildJxlTranTabPage()
        {
            TabPage tab             = new TabPage("Container (jxltran)");
            TableLayoutPanel layout = CreateScrollableOptionsTable();

            AddOptionRow(layout, "Pack Codestream (--pack):", _chkTranPack);
            AddOptionRow(layout, "Extract Codestream (--extract):", _chkTranExtract);
            AddOptionRow(layout, "Add Box Specs (type=file;...):", _txtAddBoxSpecs);
            AddOptionRow(layout, "Remove Box Types (Exif;xml ;...):", _txtRemoveBoxTypes);
            AddOptionRow(layout, "Quiet (--quiet):", _chkTranQuiet);

            Button btnApply = new Button { Text = "Apply jxltran to Loaded JXL", AutoSize = true };
            btnApply.Click += (_, _) => ApplyJxlTranToCurrentImage();
            AddOptionRow(layout, "Action:", btnApply);

            tab.Controls.Add(layout);

            return tab;
        }

        private TabPage BuildCliConsoleTabPage()
        {
            TabPage tab             = new TabPage("CLI Runner");
            TableLayoutPanel layout = CreateScrollableOptionsTable();

            AddOptionRow(layout, "CLI Tool:", _cmbCliTool);
            AddOptionRow(layout, "Arguments:", _txtCliRawArgs);

            FlowLayoutPanel btnPanel = new FlowLayoutPanel { AutoSize = true };
            Button btnRun            = new Button { Text = "Run Tool", AutoSize = true };
            Button btnVer            = new Button { Text = "--version", AutoSize = true };
            Button btnHelp           = new Button { Text = "--help", AutoSize = true };

            btnRun.Click  += (_, _) => ExecuteRawCliCommand(_cmbCliTool.Text, _txtCliRawArgs.Text);
            btnVer.Click  += (_, _) => ExecuteRawCliCommand(_cmbCliTool.Text, "--version");
            btnHelp.Click += (_, _) => ExecuteRawCliCommand(_cmbCliTool.Text, "--help");

            btnPanel.Controls.Add(btnRun);
            btnPanel.Controls.Add(btnVer);
            btnPanel.Controls.Add(btnHelp);
            AddOptionRow(layout, "Execute:", btnPanel);

            tab.Controls.Add(layout);

            return tab;
        }

        private void InitializeDefaultControlValues()
        {
            ConfigureNumeric(_numDistance, 0m, 25m, 1.0m, 2, 0.1m);
            ConfigureNumeric(_numQuality, 0m, 100m, 90.0m, 1, 1.0m);
            ConfigureNumeric(_numAlphaDistance, -1m, 25m, -1.0m, 2, 0.1m);
            ConfigureNumeric(_numEffort, 1m, 11m, 7m, 0, 1m);
            ConfigureNumeric(_numProgressiveDc, -1m, 2m, -1m, 0, 1m);
            ConfigureNumeric(_numResponsive, -1m, 1m, -1m, 0, 1m);
            ConfigureNumeric(_numEpf, -1m, 3m, -1m, 0, 1m);
            ConfigureNumeric(_numFasterDecoding, 0m, 4m, 0m, 0, 1m);
            ConfigureNumeric(_numResampling, -1m, 8m, -1m, 0, 1m);
            ConfigureNumeric(_numEcResampling, -1m, 8m, -1m, 0, 1m);
            ConfigureNumeric(_numBrotliEffort, -1m, 11m, -1m, 0, 1m);
            ConfigureNumeric(_numCenterX, -1m, 65535m, -1m, 0, 1m);
            ConfigureNumeric(_numCenterY, -1m, 65535m, -1m, 0, 1m);
            ConfigureNumeric(_numPhotonNoise, 0m, 64000m, 0m, 1, 100m);
            ConfigureNumeric(_numIntensity, 0m, 10000m, 0m, 1, 100m);
            ConfigureNumeric(_numOverrideBits, 0m, 32m, 0m, 0, 1m);
            ConfigureNumeric(_numCodestreamLvl, -1m, 10m, -1m, 0, 1m);
            ConfigureNumeric(_numUpsamplingMode, -1m, 2m, -1m, 0, 1m);
            ConfigureNumeric(_numPremultiply, -1m, 1m, -1m, 0, 1m);
            ConfigureNumeric(_numStripAlpha, -1m, 1m, -1m, 0, 1m);
            ConfigureNumeric(_numBuffering, -1m, 3m, -1m, 0, 1m);
            ConfigureNumeric(_numCjxlThreads, -1m, 256m, -1m, 0, 1m);
            ConfigureNumeric(_numCjxlReps, 1m, 100m, 1m, 0, 1m);

            InitializeComboAndDecodeDefaults();
        }

        private void InitializeComboAndDecodeDefaults()
        {
            ComboBox[] overrideCombos =
            {
                _cmbLosslessJpeg, _cmbModular, _cmbContainer,
                _cmbProgressiveAc, _cmbQProgressiveAc, _cmbGaborish,
                _cmbDots, _cmbPatches, _cmbKeepInvisible,
                _cmbGroupOrder, _cmbJpegReconCfl,
            };

            foreach (ComboBox cmb in overrideCombos)
            {
                cmb.DropDownStyle = ComboBoxStyle.DropDownList;
                cmb.Items.AddRange(new object[] { "Default (-1)", "On (1)", "Off (0)" });
                cmb.SelectedIndex = 0;
            }

            _cmbLosslessJpeg.SelectedIndex = 1;
            _chkCompressBoxes.Checked      = true;

            ConfigureNumeric(_numJpegQuality, 0m, 100m, 95m, 0, 1m);
            ConfigureNumeric(_numBitsPerSample, 0m, 32m, 0m, 0, 8m);
            ConfigureNumeric(_numDisplayNits, 0m, 10000m, 0m, 1, 100m);
            ConfigureNumeric(_numDownsampling, 1m, 8m, 1m, 0, 1m);
            ConfigureNumeric(_numMaxPasses, 0m, 100m, 0m, 0, 1m);
            ConfigureNumeric(_numDjxlThreads, -1m, 256m, -1m, 0, 1m);
            ConfigureNumeric(_numDjxlReps, 1m, 100m, 1m, 0, 1m);

            _cmbColorSpace.Items.AddRange(new object[] { "", "RGB_D65_SRG_Rel_SRG", "RGB_D65_SRG_Rel_Lin", "RGB_D65_210_Rel_PeQ", "RGB_D65_210_Rel_HLG" });
            _cmbAlphaBlend.Items.AddRange(new object[] { "", "black", "white" });
            _chkRenderSpotcolors.Checked = true;
            _chkCoalescing.Checked       = true;
            _chkUseImageCallback.Checked = true;

            _cmbCliTool.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCliTool.Items.AddRange(new object[] { "cjxl", "djxl", "jxlinfo", "jxltran" });
            _cmbCliTool.SelectedIndex = 0;
            _txtCliRawArgs.Width      = 240;
            _txtColorHints.Width      = 200;
            _txtAddBoxSpecs.Width     = 200;
            _txtRemoveBoxTypes.Width  = 200;

            RegisterAutoConversionHandlers();
        }

        private void RegisterAutoConversionHandlers()
        {
            NumericUpDown[] encodeNumerics =
            {
                _numDistance, _numQuality, _numAlphaDistance, _numEffort,
                _numProgressiveDc, _numResponsive, _numEpf, _numFasterDecoding,
                _numResampling, _numEcResampling, _numBrotliEffort, _numCenterX,
                _numCenterY, _numPhotonNoise, _numIntensity, _numOverrideBits,
                _numCodestreamLvl, _numUpsamplingMode, _numPremultiply, _numStripAlpha,
                _numBuffering, _numCjxlThreads, _numCjxlReps,
            };

            foreach (NumericUpDown num in encodeNumerics)
            {
                num.ValueChanged += OnEncodeSettingChanged;
            }

            ComboBox[] encodeCombos =
            {
                _cmbLosslessJpeg, _cmbModular, _cmbContainer,
                _cmbProgressiveAc, _cmbQProgressiveAc, _cmbGaborish,
                _cmbDots, _cmbPatches, _cmbKeepInvisible,
                _cmbGroupOrder, _cmbJpegReconCfl,
            };

            foreach (ComboBox cmb in encodeCombos)
            {
                cmb.SelectedIndexChanged += OnEncodeSettingChanged;
            }

            RegisterEncodeCheckBoxAndDecodeHandlers();
        }

        private void RegisterEncodeCheckBoxAndDecodeHandlers()
        {
            CheckBox[] encodeChecks =
            {
                _chkUseQuality, _chkCompressBoxes, _chkProgressive, _chkAllowExpert,
                _chkAlreadyDownsampled, _chkStreamingInput, _chkStreamingOutput, _chkCjxlQuiet,
            };

            foreach (CheckBox chk in encodeChecks)
            {
                chk.CheckedChanged += OnEncodeSettingChanged;
            }

            _txtColorHints.TextChanged += OnEncodeSettingChanged;

            NumericUpDown[] decodeNumerics =
            {
                _numJpegQuality, _numBitsPerSample, _numDisplayNits,
                _numDownsampling, _numMaxPasses, _numDjxlThreads, _numDjxlReps,
            };

            foreach (NumericUpDown num in decodeNumerics)
            {
                num.ValueChanged += OnDecodeSettingChanged;
            }

            RegisterDecodeCheckBoxAndTextHandlers();
        }

        private void RegisterDecodeCheckBoxAndTextHandlers()
        {
            CheckBox[] decodeChecks =
            {
                _chkPixelsToJpeg, _chkUseSjpeg, _chkUnpremultiplyAlpha, _chkAllowPartial,
                _chkAllowMoreSteps, _chkRenderSpotcolors, _chkCoalescing, _chkOutputFrames,
                _chkOutputExtraChans, _chkUseImageCallback, _chkPrintReadBytes, _chkDjxlQuiet,
            };

            foreach (CheckBox chk in decodeChecks)
            {
                chk.CheckedChanged += OnDecodeSettingChanged;
            }

            _cmbColorSpace.SelectedIndexChanged += OnDecodeSettingChanged;
            _cmbColorSpace.TextChanged          += OnDecodeSettingChanged;
            _cmbAlphaBlend.SelectedIndexChanged += OnDecodeSettingChanged;
            _cmbAlphaBlend.TextChanged          += OnDecodeSettingChanged;
            _txtPreviewOut.TextChanged          += OnDecodeSettingChanged;
            _txtIccOut.TextChanged              += OnDecodeSettingChanged;
            _txtOrigIccOut.TextChanged          += OnDecodeSettingChanged;
            _txtMetadataOut.TextChanged         += OnDecodeSettingChanged;
        }

        private void OnOpenFileClicked()
        {
            using OpenFileDialog dlg = new OpenFileDialog
            {
                Title  = "Open JPEG XL, JPEG, or Image File",
                Filter = "All Supported Images|*.jxl;*.jpg;*.jpeg;*.png;*.apng;*.ppm;*.pgm;*.pam;*.pnm;*.pfm;*.pgx|JPEG XL (*.jxl)|*.jxl|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG (*.png)|*.png|All Files (*.*)|*.*",
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                LoadFromFilePath(dlg.FileName);
            }
        }

        private void OnSaveFileClicked()
        {
            using SaveFileDialog dlg = new SaveFileDialog
            {
                Title  = "Save Image As",
                Filter = "JPEG XL (*.jxl)|*.jxl|JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|PPM (*.ppm)|*.ppm|PFM (*.pfm)|*.pfm|PGX (*.pgx)|*.pgx|NumPy (*.npy)|*.npy",
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                SaveCurrentImageToPath(dlg.FileName);
            }
        }

        private static TableLayoutPanel CreateScrollableOptionsTable()
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                AutoScroll  = true,
                ColumnCount = 2,
                Padding     = new Padding(8),
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));

            return panel;
        }

        private static void AddOptionRow(TableLayoutPanel panel, string labelText, Control editor)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label lbl = new Label
            {
                Text      = labelText,
                AutoSize  = true,
                Anchor    = AnchorStyles.Left,
                Padding   = new Padding(0, 4, 4, 4),
            };

            editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(lbl, 0, row);
            panel.Controls.Add(editor, 1, row);
        }

        private static void ConfigureNumeric(
            NumericUpDown ctrl,
            decimal min,
            decimal max,
            decimal val,
            int decimals,
            decimal increment)
        {
            ctrl.Minimum       = min;
            ctrl.Maximum       = max;
            ctrl.DecimalPlaces = decimals;
            ctrl.Increment     = increment;
            ctrl.Value         = val;
        }

        private static Override SelectedOverride(ComboBox cmb)
        {
            return cmb.SelectedIndex switch
            {
                1 => Override.On,
                2 => Override.Off,
                _ => Override.Default,
            };
        }

        private static void SetOverrideCombo(ComboBox cmb, Override value)
        {
            cmb.SelectedIndex = value switch
            {
                Override.On  => 1,
                Override.Off => 2,
                _            => 0,
            };
        }

        private static decimal ClampDecimal(decimal value, NumericUpDown ctrl)
        {
            return Math.Clamp(value, ctrl.Minimum, ctrl.Maximum);
        }

        private static void ParseColorHintsText(string? text, ColorHints hints)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string[] parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string part in parts)
            {
                int eq = part.IndexOf('=');

                if (eq > 0)
                {
                    hints.Add(part.Substring(0, eq), part.Substring(eq + 1));
                }
            }
        }

        private static void PopulateSemicolonList(string? text, List<string> target)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string[] entries = text.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in entries)
            {
                target.Add(entry);
            }
        }

        private static string[] SplitCommandLine(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<string>();
            }

            return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
