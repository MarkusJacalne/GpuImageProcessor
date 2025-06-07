using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using Silk.NET.OpenCL;
using System.Runtime.InteropServices;
using System.Text;

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

        // Logic
        private Bitmap? _currentBitmap;
        private long _cpuTime = -1;
        private long _gpuTime = -1;

        // OpenCL
        private CL? _cl;
        private nint _context;
        private nint _device;
        private nint _queue;
        private bool _openClInitialized = false;

        public Form1()
        {
            InitializeComponentProgrammatically();
            InitializeOpenCL();
        }

        #region UI Initialization

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

            btnLoadImage.Text = "1. Load Image";
            btnLoadImage.Location = new Point(530, 25);
            btnLoadImage.Size = new Size(240, 40);

            btnGrayscaleCpu.Text = "2. Grayscale on CPU";
            btnGrayscaleCpu.Location = new Point(530, 80);
            btnGrayscaleCpu.Size = new Size(240, 40);

            btnGrayscaleGpu.Text = "3. Grayscale on GPU";
            btnGrayscaleGpu.Location = new Point(530, 135);
            btnGrayscaleGpu.Size = new Size(240, 40);

            lblCpuTime.Text = "CPU Time:";
            lblCpuTime.Location = new Point(530, 190);
            lblCpuTime.Size = new Size(240, 20);

            lblGpuTime.Text = "GPU Time:";
            lblGpuTime.Location = new Point(530, 215);
            lblGpuTime.Size = new Size(240, 20);

            gbPerformance.Text = "Performance Score";
            gbPerformance.Location = new Point(530, 260);
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
            this.Controls.Add(btnLoadImage);
            this.Controls.Add(btnGrayscaleCpu);
            this.Controls.Add(btnGrayscaleGpu);
            this.Controls.Add(lblCpuTime);
            this.Controls.Add(lblGpuTime);
            this.Controls.Add(gbPerformance);
            gbPerformance.Controls.Add(lblScore);
            gbPerformance.Controls.Add(lblRank);

            btnLoadImage.Click += btnLoadImage_Click;
            btnGrayscaleCpu.Click += btnGrayscaleCpu_Click;
            btnGrayscaleGpu.Click += btnGrayscaleGpu_Click;
        }

        #endregion

        #region OpenCL Initialization

        private void InitializeOpenCL()
        {
            try
            {
                _cl = CL.GetApi();
            }
            catch
            {
                MessageBox.Show("Failed to load OpenCL. Ensure Silk.NET.OpenCL is installed and your drivers are up to date.");
                btnGrayscaleGpu.Enabled = false;
                return;
            }

            unsafe
            {
                uint numPlatforms;
                _cl.GetPlatformIDs(0, null, &numPlatforms);
                if (numPlatforms == 0)
                {
                    MessageBox.Show("No OpenCL platforms found.");
                    btnGrayscaleGpu.Enabled = false;
                    return;
                }

                var platforms = new nint[numPlatforms];
                fixed (nint* ptr = platforms)
                    _cl.GetPlatformIDs(numPlatforms, ptr, null);

                nint amdPlatform = 0;
                // 1) Find AMD platform by matching Vendor string
                foreach (var p in platforms)
                {
                    // Read vendor
                    var vendBuf = new byte[256];
                    fixed (byte* vendPtr = vendBuf)
                        _cl.GetPlatformInfo(p, PlatformInfo.Vendor, (nuint)vendBuf.Length, vendPtr, null);
                    string vendName = Encoding.UTF8.GetString(vendBuf).TrimEnd('\0');

                    if (vendName.Contains("Advanced Micro Devices"))
                    {
                        amdPlatform = p;
                        break;
                    }
                }

                if (amdPlatform == 0)
                {
                    MessageBox.Show("AMD OpenCL platform not found.");
                    btnGrayscaleGpu.Enabled = false;
                    return;
                }

                // 2) Get the first GPU device on that platform
                uint numDevices;
                _cl.GetDeviceIDs(amdPlatform, DeviceType.Gpu, 0, null, &numDevices);
                if (numDevices == 0)
                {
                    MessageBox.Show("No GPU devices found on AMD platform.");
                    btnGrayscaleGpu.Enabled = false;
                    return;
                }

                var devices = new nint[numDevices];
                fixed (nint* devPtr = devices)
                    _cl.GetDeviceIDs(amdPlatform, DeviceType.Gpu, numDevices, devPtr, null);

                _device = devices[0];

                // 3) Create context + queue
                nint* deviceList = stackalloc nint[1];
                deviceList[0] = _device;

                _context = _cl.CreateContext(null, 1, deviceList, null!, null, out _);
                _queue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, out _);
                _openClInitialized = true;
            }
        }

        #endregion

        #region Button Handlers

        private void btnLoadImage_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp"
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            _currentBitmap = new Bitmap(ofd.FileName);
            pictureBox.Image = _currentBitmap;
            lblCpuTime.Text = "CPU Time:";
            lblGpuTime.Text = "GPU Time:";
            lblScore.Text = "Score: ---";
            lblRank.Text = "Rank: N/A";
            _cpuTime = _gpuTime = -1;
        }

        private void btnGrayscaleCpu_Click(object? sender, EventArgs e)
        {
            if (_currentBitmap == null)
            {
                MessageBox.Show("Please load an image first.");
                return;
            }

            var sw = Stopwatch.StartNew();
            var bmp = new Bitmap(_currentBitmap);
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    int g = (int)(c.R * 0.2126 + c.G * 0.7152 + c.B * 0.0722);
                    bmp.SetPixel(x, y, Color.FromArgb(c.A, g, g, g));
                }
            sw.Stop();

            _cpuTime = sw.ElapsedMilliseconds;
            lblCpuTime.Text = $"CPU Time: {_cpuTime} ms";
            pictureBox.Image = bmp;
            UpdateScore();
        }

        private void btnGrayscaleGpu_Click(object? sender, EventArgs e)
        {
            if (_currentBitmap == null)
            {
                MessageBox.Show("Load an image first.");
                return;
            }
            if (!_openClInitialized || _cl is null)
            {
                MessageBox.Show("OpenCL not initialized.");
                return;
            }
            if (!File.Exists("Grayscale.cl"))
            {
                MessageBox.Show("Grayscale.cl not found.");
                return;
            }

            var sw = Stopwatch.StartNew();

            // copy pixels into byte[]
            var bmpData = _currentBitmap.LockBits(
                new Rectangle(0, 0, _currentBitmap.Width, _currentBitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int byteCount = bmpData.Stride * bmpData.Height;
            var pixels = new byte[byteCount];
            Marshal.Copy(bmpData.Scan0, pixels, 0, byteCount);
            _currentBitmap.UnlockBits(bmpData);

            unsafe
            {
                fixed (void* p = pixels)
                {
                    var inBuf = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)byteCount, p, out _);
                    var outBuf = _cl.CreateBuffer(_context, MemFlags.WriteOnly, (nuint)byteCount, null, out _);

                    string src = File.ReadAllText("Grayscale.cl");
                    var prog = _cl.CreateProgramWithSource(_context, 1, new[] { src }, null, out _);

                    // build with empty options
                    nint* devList = stackalloc nint[1];
                    devList[0] = _device;
                    int buildErr = _cl.BuildProgram(prog, 1, devList, string.Empty, default, default);

                    // check build log
                    nuint logSize;
                    _cl.GetProgramBuildInfo(prog, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                    if (logSize > 1)
                    {
                        var log = new byte[logSize];
                        fixed (byte* lp = log)
                            _cl.GetProgramBuildInfo(prog, _device, ProgramBuildInfo.BuildLog, logSize, lp, null);
                        MessageBox.Show($"Kernel build error:\n{Encoding.UTF8.GetString(log)}");
                        _cl.ReleaseProgram(prog);
                        _cl.ReleaseMemObject(inBuf);
                        _cl.ReleaseMemObject(outBuf);
                        return;
                    }

                    var kernel = _cl.CreateKernel(prog, "ToGrayscale", out _);
                    _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &inBuf);
                    _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &outBuf);

                    nuint global = (nuint)(_currentBitmap.Width * _currentBitmap.Height);
                    _cl.EnqueueNdrangeKernel(_queue, kernel, 1, null, &global, null, 0, null, null);

                    var result = new byte[byteCount];
                    fixed (void* rp = result)
                    {
                        _cl.EnqueueReadBuffer(_queue, outBuf, true, 0, (nuint)byteCount, rp, 0, null, null);
                    }

                    _cl.ReleaseKernel(kernel);
                    _cl.ReleaseProgram(prog);
                    _cl.ReleaseMemObject(inBuf);
                    _cl.ReleaseMemObject(outBuf);

                    sw.Stop();
                    _gpuTime = sw.ElapsedMilliseconds;
                    lblGpuTime.Text = $"GPU Time: {_gpuTime} ms";

                    // create result bitmap
                    var resBmp = new Bitmap(_currentBitmap.Width, _currentBitmap.Height, PixelFormat.Format32bppArgb);
                    var resData = resBmp.LockBits(new Rectangle(0, 0, resBmp.Width, resBmp.Height),
                                                  ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(result, 0, resData.Scan0, byteCount);
                    resBmp.UnlockBits(resData);

                    pictureBox.Image = resBmp;
                }
            }

            UpdateScore();
        }

        #endregion

        private void UpdateScore()
        {
            if (_cpuTime < 0 || _gpuTime < 0) return;
            if (_gpuTime == 0) _gpuTime = 1;
            double factor = (double)_cpuTime / _gpuTime;
            double score = factor * 100;
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_openClInitialized && _cl is not null)
            {
                if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
                if (_context != 0) _cl.ReleaseContext(_context);
            }
            base.OnFormClosing(e);
        }
    }
}