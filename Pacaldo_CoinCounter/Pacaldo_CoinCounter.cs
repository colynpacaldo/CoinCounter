using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pacaldo_CoinCounter
{
    public class Coin
    {
        public int Area;
        public double Diameter, Cx, Cy, HoleRatio;
        public string Name;
        public double Value;
    }

    public static class CoinProcessor
    {
        static readonly string[] Names = { "5 sentimo", "10 sentimo", "25 sentimo", "1 piso", "5 piso", "10 piso" };
        static readonly double[] Values = { 0.05, 0.10, 0.25, 1.00, 5.00, 10.00 };

        public static double[,] ToGray(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var g = new double[h, w];
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] px = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * data.Stride + x * 3;           // B, G, R
                    g[y, x] = 0.299 * px[i + 2] + 0.587 * px[i + 1] + 0.114 * px[i];
                }
            return g;
        }

        public static int Otsu(double[,] gray)
        {
            var hist = new double[256];
            foreach (double v in gray) hist[(int)Math.Min(255, v)]++;
            double total = gray.Length, sumAll = 0;
            for (int t = 0; t < 256; t++) sumAll += t * hist[t];
            double wb = 0, sb = 0, best = -1; int thr = 0;
            for (int t = 0; t < 256; t++)
            {
                wb += hist[t];
                if (wb == 0) continue;
                double wf = total - wb;
                if (wf == 0) break;
                sb += t * hist[t];
                double mb = sb / wb, mf = (sumAll - sb) / wf;
                double var = wb * wf * (mb - mf) * (mb - mf);
                if (var > best) { best = var; thr = t; }
            }
            return thr;
        }

        static bool[,] Morph(bool[,] m, int r, bool dilate)
        {
            int h = m.GetLength(0), w = m.GetLength(1);
            var tmp = new bool[h, w];
            var outp = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool res = !dilate;
                    for (int d = -r; d <= r; d++)
                    {
                        int xx = x + d;
                        bool v = xx >= 0 && xx < w && m[y, xx];
                        if (dilate && v) { res = true; break; }
                        if (!dilate && !v) { res = false; break; }
                    }
                    tmp[y, x] = res;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool res = !dilate;
                    for (int d = -r; d <= r; d++)
                    {
                        int yy = y + d;
                        bool v = yy >= 0 && yy < h && tmp[yy, x];
                        if (dilate && v) { res = true; break; }
                        if (!dilate && !v) { res = false; break; }
                    }
                    outp[y, x] = res;
                }
            return outp;
        }

        static bool[,] Close(bool[,] m, int r) { return Morph(Morph(m, r, true), r, false); }
        static bool[,] Open(bool[,] m, int r) { return Morph(Morph(m, r, false), r, true); }

        static bool[,] FillHoles(bool[,] m)
        {
            int h = m.GetLength(0), w = m.GetLength(1);
            var bg = new bool[h, w];
            var q = new Queue<int>();
            Action<int, int> push = (y, x) =>
            {
                if (y < 0 || y >= h || x < 0 || x >= w || m[y, x] || bg[y, x]) return;
                bg[y, x] = true; q.Enqueue(y * w + x);
            };
            for (int y = 0; y < h; y++) { push(y, 0); push(y, w - 1); }
            for (int x = 0; x < w; x++) { push(0, x); push(h - 1, x); }
            while (q.Count > 0)
            {
                int p = q.Dequeue(); int cy = p / w, cx = p % w;
                push(cy + 1, cx); push(cy - 1, cx); push(cy, cx + 1); push(cy, cx - 1);
            }
            var res = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) res[y, x] = !bg[y, x];
            return res;
        }

        public static List<Coin> Detect(Bitmap bmp)
        {
            double[,] gray = ToGray(bmp);
            int h = gray.GetLength(0), w = gray.GetLength(1);

            int thr = Otsu(gray);
            var mask = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) mask[y, x] = gray[y, x] < thr;

            mask = Close(mask, 1);
            mask = Open(mask, 2);
            bool[,] filled = FillHoles(mask);
            bool[,] closedRaw = Close(mask, 1);

            var visited = new bool[h, w];
            var coins = new List<Coin>();
            var q = new Queue<int>();
            for (int sy = 0; sy < h; sy++)
                for (int sx = 0; sx < w; sx++)
                {
                    if (!filled[sy, sx] || visited[sy, sx]) continue;
                    long sumX = 0, sumY = 0; int area = 0, holes = 0;
                    visited[sy, sx] = true; q.Enqueue(sy * w + sx);
                    while (q.Count > 0)
                    {
                        int p = q.Dequeue(); int cy = p / w, cx = p % w;
                        area++; sumX += cx; sumY += cy;
                        if (!closedRaw[cy, cx]) holes++;
                        int[] ny = { cy + 1, cy - 1, cy, cy };
                        int[] nx = { cx, cx, cx + 1, cx - 1 };
                        for (int k = 0; k < 4; k++)
                            if (ny[k] >= 0 && ny[k] < h && nx[k] >= 0 && nx[k] < w &&
                                filled[ny[k], nx[k]] && !visited[ny[k], nx[k]])
                            {
                                visited[ny[k], nx[k]] = true; q.Enqueue(ny[k] * w + nx[k]);
                            }
                    }
                    if (area < 80) continue;                         // noise
                    coins.Add(new Coin
                    {
                        Area = area,
                        Diameter = 2 * Math.Sqrt(area / Math.PI),
                        Cx = (double)sumX / area,
                        Cy = (double)sumY / area,
                        HoleRatio = (double)holes / area
                    });
                }

            Classify(coins);
            return coins;
        }

        static void Classify(List<Coin> coins)
        {
            if (coins.Count == 0) return;
            var ds = coins.Select(c => c.Diameter).OrderBy(d => d).ToList();
            var cuts = new List<double>();
            for (int i = 0; i < ds.Count - 1; i++)
                if (ds[i + 1] - ds[i] > 1.5) cuts.Add(ds[i]);

            int k = cuts.Count + 1;
            int offset = k < 4 ? Math.Max(0, Names.Length - k) : 0;
            foreach (var c in coins)
            {
                int g = cuts.Count(cut => c.Diameter > cut);         
                int idx = Math.Min(g + offset, Names.Length - 1);
                if (c.HoleRatio > 0.03) idx = 0;                     
                c.Name = Names[idx];
                c.Value = Values[idx];
            }
        }

        public static string Summary(List<Coin> coins)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Coin count ===");
            foreach (string n in Names)
            {
                int cnt = coins.Count(c => c.Name == n);
                if (cnt > 0) sb.AppendLine(n.PadLeft(11) + " : " + cnt);
            }
            sb.AppendLine("Total coins : " + coins.Count);
            sb.AppendLine("TOTAL VALUE : PHP " + coins.Sum(c => c.Value).ToString("N2"));
            return sb.ToString();
        }

        public static Bitmap Annotate(Bitmap src, List<Coin> coins)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            using (var pen = new Pen(Color.Red, 2))
            using (var font = new Font("Segoe UI", Math.Max(8, src.Width / 90f), FontStyle.Bold))
            {
                g.DrawImage(src, 0, 0, src.Width, src.Height);
                foreach (var c in coins)
                {
                    float r = (float)(c.Diameter / 2);
                    g.DrawEllipse(pen, (float)c.Cx - r, (float)c.Cy - r, 2 * r, 2 * r);
                    g.DrawString(c.Name.Split(' ')[0], font, Brushes.Yellow, (float)c.Cx - r / 2, (float)c.Cy - r / 3);
                }
            }
            return bmp;
        }
    }

    public class MainForm : Form
    {
        static readonly Color Navy = Color.FromArgb(24, 38, 66);
        static readonly Color Accent = Color.FromArgb(37, 99, 235);
        static readonly Color Bg = Color.FromArgb(243, 245, 249);
        const string Peso = "\u20B1";

        PictureBox pic;
        ListView list;
        Label lblTotal, lblCount, lblStatus, lblHint;
        Button btnLoad, btnSave;
        CheckBox chkOverlay;
        Bitmap original, annotated;
        string currentFile;

        public MainForm()
        {
            Text = "Philippine Peso Coin Counter";
            Font = new Font("Segoe UI", 10f);
            BackColor = Bg;
            ClientSize = new Size(1150, 760);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) LoadAndProcess(files[0]);
            };

            var imgHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Bg };
            var imgCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
            lblHint = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 13f),
                Text = "Click \"Open image\" or drag & drop a coin scan here"
            };
            imgCard.Controls.Add(pic);
            imgCard.Controls.Add(lblHint);
            imgHost.Controls.Add(imgCard);

            var side = new Panel { Dock = DockStyle.Right, Width = 340, Padding = new Padding(0, 16, 16, 16), BackColor = Bg };
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

            var lblResults = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Text = "  Results",
                Font = new Font("Segoe UI Semibold", 12f),
                ForeColor = Navy,
                TextAlign = ContentAlignment.MiddleLeft
            };

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10.5f)
            };
            list.Columns.Add("Coin", 120);
            list.Columns.Add("Qty", 60, HorizontalAlignment.Right);
            list.Columns.Add("Subtotal", 120, HorizontalAlignment.Right);

            var totalPanel = new Panel { Dock = DockStyle.Bottom, Height = 110, BackColor = Navy };
            lblCount = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                ForeColor = Color.FromArgb(180, 195, 225),
                Text = "0 coins",
                TextAlign = ContentAlignment.BottomCenter
            };
            lblTotal = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 26f),
                Text = Peso + "0.00",
                TextAlign = ContentAlignment.MiddleCenter
            };
            totalPanel.Controls.Add(lblTotal);
            totalPanel.Controls.Add(lblCount);

            card.Controls.Add(list);
            card.Controls.Add(totalPanel);
            card.Controls.Add(lblResults);
            side.Controls.Add(card);

            var status = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(228, 232, 240) };
            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                Text = "  Ready",
                ForeColor = Color.FromArgb(70, 80, 100),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
            status.Controls.Add(lblStatus);

            var bar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.White, Padding = new Padding(16, 9, 16, 9) };
            btnLoad = MakeButton("Open image", Accent, Color.White);
            btnSave = MakeButton("Save result", Color.FromArgb(226, 232, 240), Navy);
            btnSave.Enabled = false;
            chkOverlay = new CheckBox
            {
                Text = "Show detection overlay",
                Checked = true,
                AutoSize = true,
                Enabled = false,
                Location = new Point(300, 16),
                ForeColor = Navy
            };
            btnLoad.Location = new Point(16, 9);
            btnSave.Location = new Point(156, 9);
            btnLoad.Click += (s, e) => OnOpen();
            btnSave.Click += (s, e) => OnSave();
            chkOverlay.CheckedChanged += (s, e) => RefreshPicture();
            bar.Controls.Add(btnLoad);
            bar.Controls.Add(btnSave);
            bar.Controls.Add(chkOverlay);

            var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Navy };
            header.Controls.Add(new Label
            {
                Text = "Philippine Peso Coin Counter",
                ForeColor = Color.White,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 17f),
                Location = new Point(18, 8)
            });
            header.Controls.Add(new Label
            {
                Text = "Digital Image Processing  |  segmentation, labelling & size-based classification",
                ForeColor = Color.FromArgb(180, 195, 225),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(20, 43)
            });

            Controls.Add(imgHost);
            Controls.Add(side);
            Controls.Add(status);
            Controls.Add(bar);
            Controls.Add(header);
        }

        static Button MakeButton(string text, Color back, Color fore)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(130, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 10f)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        void OnOpen()
        {
            using (var ofd = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" })
            {
                if (ofd.ShowDialog() == DialogResult.OK) LoadAndProcess(ofd.FileName);
            }
        }

        void OnSave()
        {
            if (annotated == null) return;
            using (var sfd = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "coin_count_result.png" })
            {
                if (sfd.ShowDialog() == DialogResult.OK) annotated.Save(sfd.FileName, ImageFormat.Png);
            }
        }

        void RefreshPicture()
        {
            if (original == null) return;
            pic.Image = (chkOverlay.Checked && annotated != null) ? annotated : original;
        }

        async void LoadAndProcess(string path)
        {
            Bitmap bmp;
            try { bmp = new Bitmap(path); }
            catch { MessageBox.Show("Could not open that image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            btnLoad.Enabled = btnSave.Enabled = chkOverlay.Enabled = false;
            lblStatus.Text = "  Processing " + System.IO.Path.GetFileName(path) + " ...";
            Cursor = Cursors.WaitCursor;

            var sw = Stopwatch.StartNew();
            List<Coin> coins = null; Bitmap ann = null;
            try
            {
                await Task.Run(() =>
                {
                    coins = CoinProcessor.Detect(bmp);
                    ann = CoinProcessor.Annotate(bmp, coins);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Processing error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnLoad.Enabled = true; Cursor = Cursors.Default; lblStatus.Text = "  Ready";
                return;
            }
            sw.Stop();

            if (original != null) original.Dispose();
            if (annotated != null) annotated.Dispose();
            original = bmp; annotated = ann; currentFile = path;

            lblHint.Visible = false;
            RefreshPicture();
            ShowResults(coins);

            btnLoad.Enabled = btnSave.Enabled = chkOverlay.Enabled = true;
            Cursor = Cursors.Default;
            lblStatus.Text = "  " + System.IO.Path.GetFileName(path) + "   |   " + bmp.Width + " x " + bmp.Height +
                             " px   |   processed in " + sw.ElapsedMilliseconds + " ms";
        }

        void ShowResults(List<Coin> coins)
        {
            list.BeginUpdate();
            list.Items.Clear();
            var groups = coins.GroupBy(c => c.Name).OrderBy(g => g.Min(c => c.Value));
            int row = 0;
            foreach (var g in groups)
            {
                var it = new ListViewItem(g.Key);
                it.SubItems.Add(g.Count().ToString());
                it.SubItems.Add(Peso + g.Sum(c => c.Value).ToString("N2"));
                it.BackColor = (row++ % 2 == 0) ? Color.White : Color.FromArgb(247, 249, 252);
                list.Items.Add(it);
            }
            list.EndUpdate();
            lblCount.Text = coins.Count + (coins.Count == 1 ? " coin" : " coins") + " detected";
            lblTotal.Text = Peso + coins.Sum(c => c.Value).ToString("N2");
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
    }
}