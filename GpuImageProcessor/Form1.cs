using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using Silk.NET.OpenCL;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace GpuImageProcessor
{
    public partial class Form1 : Form
    {
        // UI Controls
        private PictureBox pictureBox = new PictureBox();
        private Button btnLoadImage = new Button();
        private Button btnGrayscaleCpu = new Button();
        private Button btnGrayscaleGpu = new Button();
        private Label lblCpuTime = new Label();
        private Label lblGpuTime = new Label();
        private Label lblScore = new Label();
        private Label lblRank = new Label();
        private GroupBox gbPerformance = new GroupBox();
        // New control for GPU selection
        private ComboBox gpuComboBox = new ComboBox();
        private Label lblGpuSelector = new Label();


        // Project Logic Variables
        private Bitmap? _currentBitmap;
        private long _cpuTime = -1;
        private long _gpuTime = -1;

        // OpenCL Variables
        private CL? _cl;
        private nint _context;
        private nint _device;
        private nint _queue;
        private bool _gpuIsInitialized = false;

        // A small helper class to hold device info and display it nicely in the ComboBox
        private class GpuCandidate
        {
            public nint DeviceHandle { get; set; }
            public string Name { get; set; } = "";
            public ulong Memory { get; set; }

            // This controls how the object appears in the ComboBox list
            public override string ToString()
            {
                return $"{Name} ({Memory / 1024 / 1024} MB)";
            }
        }

        public Form1()
        {
            InitializeComponentProgrammatically();
            // This method will now ONLY populate the ComboBox
            PopulateGpuSelector();
        }

        #region UI and Initialization Logic

        private void InitializeComponentProgrammatically()
        {
            this.Text = "CPU vs. GPU Image Processor";
            this.Size = new Size(800, 480);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            pictureBox.Location = new Point(12, 12);
            pictureBox.Size = new Size(500, 420);
            pictureBox.BorderStyle = BorderStyle.Fixed3D;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;

            // --- New Controls for GPU Selection ---
            lblGpuSelector.Text = "Select GPU for Processing:";
            lblGpuSelector.Location = new Point(530, 15);
            lblGpuSelector.Size = new Size(240, 15);

            gpuComboBox.Location = new Point(530, 35);
            gpuComboBox.Size = new Size(240, 25);
            gpuComboBox.DropDownStyle = ComboBoxStyle.DropDownList; // Prevents user from typing
            gpuComboBox.SelectedIndexChanged += new EventHandler(gpuComboBox_SelectedIndexChanged);

            // --- Existing Controls, locations adjusted ---
            btnLoadImage.Text = "1. Load Image";
            btnLoadImage.Location = new Point(530, 70);
            btnLoadImage.Size = new Size(240, 40);

            btnGrayscaleCpu.Text = "2. Grayscale on CPU (Optimized)";
            btnGrayscaleCpu.Location = new Point(530, 120);
            btnGrayscaleCpu.Size = new Size(240, 40);

            btnGrayscaleGpu.Text = "3. Grayscale on GPU";
            btnGrayscaleGpu.Location = new Point(530, 170);
            btnGrayscaleGpu.Size = new Size(240, 40);
            btnGrayscaleGpu.Enabled = false; // Disabled until a GPU is chosen

            lblCpuTime.Text = "CPU Time:";
            lblCpuTime.Location = new Point(530, 225);
            lblCpuTime.Size = new Size(240, 20);

            lblGpuTime.Text = "GPU Time:";
            lblGpuTime.Location = new Point(530, 250);
            lblGpuTime.Size = new Size(240, 20);

            gbPerformance.Text = "Performance Score";
            gbPerformance.Location = new Point(530, 285);
            gbPerformance.Size = new Size(240, 120);

            lblScore.Text = "Score: ---";
            lblScore.Location = new Point(15, 30);
            lblScore.Size = new Size(210, 30);
            lblScore.Font = new Font("Segoe UI", 12F, FontStyle.Bold);

            lblRank.Text = "Rank: N/A";
            lblRank.Location = new Point(15, 70);
            lblRank.Size = new Size(210, 25);
            lblRank.Font = new Font("Segoe UI", 10F);
            lblRank.ForeColor = Color.Blue;

            this.Controls.Add(pictureBox);
            this.Controls.Add(lblGpuSelector);
            this.Controls.Add(gpuComboBox);
            this.Controls.Add(btnLoadImage);
            this.Controls.Add(btnGrayscaleCpu);
            this.Controls.Add(btnGrayscaleGpu);
            this.Controls.Add(lblCpuTime);
            this.Controls.Add(lblGpuTime);
            this.Controls.Add(gbPerformance);
            gbPerformance.Controls.Add(lblScore);
            gbPerformance.Controls.Add(lblRank);

            btnLoadImage.Click += new EventHandler(btnLoadImage_Click);
            btnGrayscaleCpu.Click += new EventHandler(btnGrayscaleCpu_Click);
            btnGrayscaleGpu.Click += new EventHandler(btnGrayscaleGpu_Click);
        }

        private void PopulateGpuSelector()
        {
            try
            {
                _cl = CL.GetApi();
            }
            catch (Exception)
            {
                MessageBox.Show("Could not initialize OpenCL. The necessary libraries may be missing.");
                return;
            }

            var gpuCandidates = new List<GpuCandidate>();

            unsafe
            {
                uint numPlatforms;
                _cl.GetPlatformIDs(0, null, &numPlatforms);
                if (numPlatforms == 0) return;

                var platforms = new nint[numPlatforms];
                fixed (nint* platformsPtr = platforms)
                {
                    _cl.GetPlatformIDs(numPlatforms, platformsPtr, null);
                }

                foreach (var p in platforms)
                {
                    uint numDevices;
                    if (_cl.GetDeviceIDs(p, DeviceType.Gpu, 0, null, &numDevices) != 0 || numDevices == 0)
                    {
                        continue;
                    }

                    var devices = new nint[numDevices];
                    fixed (nint* devicesPtr = devices)
                    {
                        _cl.GetDeviceIDs(p, DeviceType.Gpu, numDevices, devicesPtr, null);
                    }

                    foreach (var d in devices)
                    {
                        var deviceNameBuffer = new byte[256];
                        fixed (byte* namePtr = deviceNameBuffer)
                        {
                            _cl.GetDeviceInfo(d, DeviceInfo.Name, (nuint)deviceNameBuffer.Length, namePtr, null);
                        }
                        string deviceName = Encoding.UTF8.GetString(deviceNameBuffer).TrimEnd('\0');

                        ulong memSize;
                        _cl.GetDeviceInfo(d, DeviceInfo.GlobalMemSize, (nuint)sizeof(ulong), &memSize, null);

                        gpuCandidates.Add(new GpuCandidate { DeviceHandle = d, Name = deviceName, Memory = memSize });
                    }
                }
            }

            if (gpuCandidates.Any())
            {
                gpuComboBox.Items.AddRange(gpuCandidates.ToArray());
                gpuComboBox.SelectedIndex = 0; // Select the first one by default
            }
        }

        private void gpuComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // This event fires when the user selects a GPU from the dropdown
            if (gpuComboBox.SelectedItem is GpuCandidate selectedGpu)
            {
                unsafe
                {
                    _device = selectedGpu.DeviceHandle;
                    nint* deviceList = stackalloc nint[1];
                    deviceList[0] = _device;

                    // Clean up old context if it exists
                    if (_context != 0) _cl?.ReleaseContext(_context);
                    if (_queue != 0) _cl?.ReleaseCommandQueue(_queue);

                    _context = _cl.CreateContext(null, 1, deviceList, null!, null, out _);
                    _queue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, out _);
                    _gpuIsInitialized = true;
                    btnGrayscaleGpu.Enabled = true; // Enable the GPU button
                }
            }
        }

        #endregion

        #region Button Click Handlers and Logic

        private void btnLoadImage_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _currentBitmap = new Bitmap(ofd.FileName);
                    pictureBox.Image = _currentBitmap;
                    _cpuTime = -1;
                    _gpuTime = -1;
                    lblCpuTime.Text = "CPU Time:";
                    lblGpuTime.Text = "GPU Time:";
                    lblScore.Text = "Score: ---";
                    lblRank.Text = "Rank: N/A";
                }
            }
        }

        private void btnGrayscaleCpu_Click(object? sender, EventArgs e)
        {
            if (_currentBitmap == null) { MessageBox.Show("Please load an image first."); return; }

            var sw = Stopwatch.StartNew();

            var bmp = new Bitmap(_currentBitmap);
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

            int bytesPerPixel = Image.GetPixelFormatSize(bmp.PixelFormat) / 8;
            int byteCount = bmpData.Stride * bmp.Height;
            byte[] pixels = new byte[byteCount];
            IntPtr ptrFirstPixel = bmpData.Scan0;

            Marshal.Copy(ptrFirstPixel, pixels, 0, byteCount);

            for (int i = 0; i < pixels.Length; i += bytesPerPixel)
            {
                if (bytesPerPixel >= 3)
                {
                    float luminance = 0.2126f * pixels[i + 2] + 0.7152f * pixels[i + 1] + 0.0722f * pixels[i + 0];
                    byte gray = (byte)luminance;
                    pixels[i + 0] = gray;
                    pixels[i + 1] = gray;
                    pixels[i + 2] = gray;
                }
            }

            Marshal.Copy(pixels, 0, ptrFirstPixel, byteCount);
            bmp.UnlockBits(bmpData);

            sw.Stop();
            _cpuTime = sw.ElapsedMilliseconds;
            lblCpuTime.Text = $"CPU Time: {_cpuTime} ms (Optimized)";
            pictureBox.Image = bmp;
            UpdateScore();
        }

        private void btnGrayscaleGpu_Click(object? sender, EventArgs e)
        {
            if (_currentBitmap == null) { MessageBox.Show("Please load an image first."); return; }
            if (!_gpuIsInitialized || _cl is null) { MessageBox.Show("A GPU has not been initialized. Select one from the dropdown."); return; }
            if (!File.Exists("Grayscale.cl")) { MessageBox.Show("Error: Grayscale.cl file not found!"); return; }

            var sw = Stopwatch.StartNew();

            BitmapData bmpData = _currentBitmap.LockBits(new Rectangle(0, 0, _currentBitmap.Width, _currentBitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int byteCount = bmpData.Stride * bmpData.Height;
            byte[] pixelData = new byte[byteCount];
            Marshal.Copy(bmpData.Scan0, pixelData, 0, byteCount);
            _currentBitmap.UnlockBits(bmpData);

            unsafe
            {
                fixed (void* pixelDataPtr = pixelData)
                {
                    var inputBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)byteCount, pixelDataPtr, out _);
                    var outputBuffer = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)byteCount, null, out _);

                    string kernelSource = File.ReadAllText("Grayscale.cl");
                    var program = _cl.CreateProgramWithSource(_context, 1, new[] { kernelSource }, null, out _);

                    nint* devicesToBuild = stackalloc nint[1];
                    devicesToBuild[0] = _device;

                    int buildError = _cl.BuildProgram(program, 1, devicesToBuild, string.Empty, default, default);

                    nuint logSize;
                    _cl.GetProgramBuildInfo(program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                    if (logSize > 1)
                    {
                        var buildLog = new byte[logSize];
                        fixed (byte* logPtr = buildLog)
                        {
                            _cl.GetProgramBuildInfo(program, _device, ProgramBuildInfo.BuildLog, logSize, logPtr, null);
                        }
                        MessageBox.Show($"OpenCL Kernel Build Error (code {buildError}):\n{Encoding.UTF8.GetString(buildLog)}");

                        _cl.ReleaseProgram(program);
                        _cl.ReleaseMemObject(inputBuffer);
                        _cl.ReleaseMemObject(outputBuffer);
                        return;
                    }

                    var kernel = _cl.CreateKernel(program, "ToGrayscale", out _);
                    _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &inputBuffer);
                    _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &outputBuffer);

                    nuint globalSize = (nuint)(_currentBitmap.Width * _currentBitmap.Height);
                    _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &globalSize, null, 0, null, null);

                    var resultData = new byte[byteCount];
                    fixed (void* resultDataPtr = resultData)
                    {
                        _cl.EnqueueReadBuffer(_queue, outputBuffer, true, 0, (nuint)byteCount, resultDataPtr, 0, null, null);
                    }

                    _cl.ReleaseMemObject(inputBuffer);
                    _cl.ReleaseMemObject(outputBuffer);
                    _cl.ReleaseKernel(kernel);
                    _cl.ReleaseProgram(program);

                    sw.Stop();
                    _gpuTime = sw.ElapsedMilliseconds;
                    lblGpuTime.Text = $"GPU Time: {_gpuTime} ms";

                    var resultBmp = new Bitmap(_currentBitmap.Width, _currentBitmap.Height, PixelFormat.Format32bppArgb);
                    BitmapData resultBmpData = resultBmp.LockBits(new Rectangle(0, 0, resultBmp.Width, resultBmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(resultData, 0, resultBmpData.Scan0, byteCount);
                    resultBmp.UnlockBits(resultBmpData);
                    pictureBox.Image = resultBmp;
                }
            }
            UpdateScore();
        }

        private void UpdateScore()
        {
            if (_cpuTime < 0 || _gpuTime < 0) return;
            if (_gpuTime == 0) _gpuTime = 1;

            double speedupFactor = (double)_cpuTime / _gpuTime;
            double score = speedupFactor * 100;
            string rank;

            if (score >= 3000) rank = "GPU Dominance";
            else if (score >= 1000) rank = "Massive Speedup";
            else if (score >= 500) rank = "Significant Parallelism";
            else if (score >= 200) rank = "Noticeable Acceleration";
            else if (score >= 100) rank = "Marginal Gain";
            else rank = "CPU Advantage";

            lblScore.Text = $"Score: {score:F0}";
            lblRank.Text = $"Rank: {rank}";
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_gpuIsInitialized && _cl is not null)
            {
                if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
                if (_context != 0) _cl.ReleaseContext(_context);
            }
            base.OnFormClosing(e);
        }
    }
}