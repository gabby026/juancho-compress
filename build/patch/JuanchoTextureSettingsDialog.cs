using AssetStudio;
using ATTextureFormat = AssetsTools.NET.Texture.TextureFormat;
using StbImageSharp;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AssetStudioGUI
{
    internal sealed class JuanchoTextureSettings
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Format { get; set; }
        public int FilterMode { get; set; }
        public int AnisoLevel { get; set; }
        public float MipMapBias { get; set; }
        public int WrapMode { get; set; }
        public bool GenerateMipMaps { get; set; }
        public int MipCount { get; set; }
        public bool UseBgra { get; set; }
    }

    internal sealed class JuanchoTextureFormatItem
    {
        public ATTextureFormat Format { get; }
        public JuanchoTextureFormatItem(ATTextureFormat format) => Format = format;

        public override string ToString()
        {
            return Format switch
            {
                ATTextureFormat.DXT1 or ATTextureFormat.DXT3 or ATTextureFormat.DXT5 => $"{Format} (DXT)",
                ATTextureFormat.ETC_RGB4 or ATTextureFormat.ETC2_RGB4 or ATTextureFormat.ETC2_RGBA1 or ATTextureFormat.ETC2_RGBA8 => $"{Format} (ETC)",
                ATTextureFormat.ASTC_RGB_4x4 or ATTextureFormat.ASTC_RGB_5x5 or ATTextureFormat.ASTC_RGB_6x6 or
                ATTextureFormat.ASTC_RGB_8x8 or ATTextureFormat.ASTC_RGB_10x10 or ATTextureFormat.ASTC_RGB_12x12 or
                ATTextureFormat.ASTC_RGBA_4x4 or ATTextureFormat.ASTC_RGBA_5x5 or ATTextureFormat.ASTC_RGBA_6x6 or
                ATTextureFormat.ASTC_RGBA_8x8 or ATTextureFormat.ASTC_RGBA_10x10 or ATTextureFormat.ASTC_RGBA_12x12 => $"{Format} (ASTC)",
                ATTextureFormat.PVRTC_RGB2 or ATTextureFormat.PVRTC_RGBA2 or ATTextureFormat.PVRTC_RGB4 or ATTextureFormat.PVRTC_RGBA4 => $"{Format} (PVRTC)",
                ATTextureFormat.BC4 or ATTextureFormat.BC5 or ATTextureFormat.BC6H or ATTextureFormat.BC7 => $"{Format} (BC)",
                _ => Format.ToString()
            };
        }
    }

    internal sealed class JuanchoTextureSettingsDialog : Form
    {
        private readonly NumericUpDown widthBox;
        private readonly NumericUpDown heightBox;
        private readonly ComboBox formatBox;
        private readonly ComboBox filterBox;
        private readonly NumericUpDown anisoBox;
        private readonly NumericUpDown mipBiasBox;
        private readonly ComboBox wrapBox;
        private readonly CheckBox mipMapsBox;
        private readonly NumericUpDown mipCountBox;
        private readonly ComboBox channelBox;
        private readonly CheckBox keepAspectBox;
        private readonly PictureBox previewBox;
        private readonly Label sourceInfoLabel;
        private readonly Label formatNoteLabel;
        private readonly ToolTip toolTip;
        private readonly double sourceAspect;
        private bool changingDimensions;

        public JuanchoTextureSettings Settings { get; private set; }

        public JuanchoTextureSettingsDialog(string textureName, string imagePath, Texture2D sourceTexture)
        {
            Text = "Juancho - Texture2D Replacement";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 760;
            Height = 650;
            Padding = new Padding(10);

            int imageWidth;
            int imageHeight;
            Bitmap preview = null;
            try
            {
                using var stream = File.OpenRead(imagePath);
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                imageWidth = image.Width;
                imageHeight = image.Height;
                preview = CreatePreviewBitmap(image.Data, image.Width, image.Height);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not read the selected image.\n\n{ex.Message}", ex);
            }

            if (imageWidth < 1 || imageHeight < 1)
                throw new InvalidOperationException("The selected image has invalid dimensions.");

            int originalWidth = sourceTexture?.m_Width > 0 ? sourceTexture.m_Width : imageWidth;
            int originalHeight = sourceTexture?.m_Height > 0 ? sourceTexture.m_Height : imageHeight;
            int originalFormat = sourceTexture != null ? (int)sourceTexture.m_TextureFormat : (int)ATTextureFormat.RGBA32;
            int originalFilter = sourceTexture?.m_TextureSettings.m_FilterMode ?? 1;
            int originalAniso = sourceTexture?.m_TextureSettings.m_Aniso ?? 1;
            float originalMipBias = sourceTexture?.m_TextureSettings.m_MipBias ?? 0f;
            int originalWrap = sourceTexture?.m_TextureSettings.m_WrapMode ?? 0;
            bool originalMipMaps = sourceTexture?.m_MipMap ?? false;
            int originalMipCount = sourceTexture?.m_MipCount > 0 ? sourceTexture.m_MipCount : 1;

            originalAniso = Math.Max(1, originalAniso);
            originalMipCount = Math.Max(1, originalMipCount);
            sourceAspect = imageWidth / (double)imageHeight;

            toolTip = new ToolTip();

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0),
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285f));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

            var previewPanel = new GroupBox
            {
                Text = "Replacement Preview",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = SystemColors.ControlDark,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = preview,
            };
            previewPanel.Controls.Add(previewBox);
            main.Controls.Add(previewPanel, 0, 0);

            var settingsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoScroll = true,
                Padding = new Padding(8, 0, 0, 0)
            };
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155f));
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = $"Texture2D: {textureName}",
                Font = new System.Drawing.Font(Font, FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 4)
            };
            AddFullRow(settingsPanel, title, 52);

            sourceInfoLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = $"Original: {originalWidth} × {originalHeight}\nImage: {imageWidth} × {imageHeight}",
                Padding = new Padding(0, 0, 0, 6)
            };
            AddFullRow(settingsPanel, sourceInfoLabel, 48);

            widthBox = CreateNumber(1, 65536, imageWidth);
            AddRow(settingsPanel, "Width", widthBox);
            heightBox = CreateNumber(1, 65536, imageHeight);
            AddRow(settingsPanel, "Height", heightBox);

            var sizeButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false
            };
            var useImageButton = new Button { Text = "Use image size", AutoSize = true, Height = 28 };
            var useOriginalButton = new Button { Text = "Use original size", AutoSize = true, Height = 28 };
            keepAspectBox = new CheckBox { Text = "Keep aspect ratio", AutoSize = true, Checked = true, Padding = new Padding(8, 5, 0, 0) };
            sizeButtons.Controls.Add(useImageButton);
            sizeButtons.Controls.Add(useOriginalButton);
            sizeButtons.Controls.Add(keepAspectBox);
            AddFullRow(settingsPanel, sizeButtons, 42);

            formatBox = CreateCombo();
            foreach (ATTextureFormat format in Enum.GetValues<ATTextureFormat>().Distinct())
                formatBox.Items.Add(new JuanchoTextureFormatItem(format));

            var selectedItem = formatBox.Items.Cast<JuanchoTextureFormatItem>()
                .FirstOrDefault(x => (int)x.Format == originalFormat);
            formatBox.SelectedItem = selectedItem ?? formatBox.Items.Cast<JuanchoTextureFormatItem>()
                .First(x => x.Format == ATTextureFormat.RGBA32);
            AddRow(settingsPanel, "Format", formatBox);

            formatNoteLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = "Compressed formats use the bundled native texture encoder. Channel selection is most relevant to RGBA/BGRA formats.",
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, 2, 0, 4)
            };
            AddFullRow(settingsPanel, formatNoteLabel, 34);

            filterBox = CreateCombo();
            filterBox.Items.Add("Point");
            filterBox.Items.Add("Bilinear");
            filterBox.Items.Add("Trilinear");
            filterBox.Items.Add("Anisotropic");
            int filterIndex = originalAniso > 1
                ? 3
                : Math.Max(0, Math.Min(2, originalFilter));
            filterBox.SelectedIndex = filterIndex;
            AddRow(settingsPanel, "Filter Mode", filterBox);

            anisoBox = CreateNumber(1, 16, originalAniso);
            AddRow(settingsPanel, "Anisotropic Level", anisoBox);

            mipBiasBox = new NumericUpDown
            {
                Width = 220,
                Dock = DockStyle.Left,
                Minimum = -16,
                Maximum = 16,
                DecimalPlaces = 3,
                Increment = 0.05m,
                Value = (decimal)Math.Max(-16, Math.Min(16, originalMipBias))
            };
            AddRow(settingsPanel, "Mip Map Bias", mipBiasBox);

            wrapBox = CreateCombo();
            wrapBox.Items.Add("Repeat");
            wrapBox.Items.Add("Clamp");
            wrapBox.Items.Add("Mirror");
            wrapBox.Items.Add("Mirror Once");
            wrapBox.SelectedIndex = Math.Max(0, Math.Min(3, originalWrap));
            AddRow(settingsPanel, "Wrap Mode", wrapBox);

            mipMapsBox = new CheckBox
            {
                Text = "Generate Mip Maps",
                AutoSize = true,
                Checked = originalMipMaps
            };
            AddRow(settingsPanel, "Mip Maps", mipMapsBox);

            mipCountBox = CreateNumber(1, 32, originalMipCount);
            AddRow(settingsPanel, "Mip Count", mipCountBox);

            channelBox = CreateCombo();
            channelBox.Items.Add("RGBA");
            channelBox.Items.Add("BGRA");
            channelBox.SelectedIndex = UsesBgraByDefault(originalFormat) ? 1 : 0;
            AddRow(settingsPanel, "Channel", channelBox);

            var hint = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = "Safe default: the existing texture settings are preserved. Change only what you need.",
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, 4, 0, 4)
            };
            AddFullRow(settingsPanel, hint, 42);

            main.Controls.Add(settingsPanel, 1, 0);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };

            var saveButton = new Button { Text = "Save", Width = 110, Height = 30 };
            var cancelButton = new Button { Text = "Cancel", Width = 110, Height = 30 };
            var resetButton = new Button { Text = "Reset to Original", Width = 125, Height = 30 };

            bottom.Controls.Add(saveButton);
            bottom.Controls.Add(cancelButton);
            bottom.Controls.Add(resetButton);
            main.Controls.Add(bottom, 0, 1);
            main.SetColumnSpan(bottom, 2);

            Controls.Add(main);

            useImageButton.Click += (_, _) => SetDimensions(imageWidth, imageHeight);
            useOriginalButton.Click += (_, _) => SetDimensions(originalWidth, originalHeight);
            widthBox.ValueChanged += (_, _) => UpdateLinkedHeight();
            heightBox.ValueChanged += (_, _) => UpdateLinkedWidth();
            keepAspectBox.CheckedChanged += (_, _) => UpdateMipMaximum();

            filterBox.SelectedIndexChanged += (_, _) =>
                anisoBox.Enabled = filterBox.SelectedIndex == 3 || anisoBox.Value > 1;

            mipMapsBox.CheckedChanged += (_, _) =>
            {
                mipCountBox.Enabled = mipMapsBox.Checked;
                UpdateMipMaximum();
            };

            resetButton.Click += (_, _) =>
            {
                SetDimensions(imageWidth, imageHeight);
                SelectFormat(originalFormat);
                filterBox.SelectedIndex = originalAniso > 1 ? 3 : Math.Max(0, Math.Min(2, originalFilter));
                anisoBox.Value = Math.Max(1, Math.Min(16, originalAniso));
                mipBiasBox.Value = (decimal)Math.Max(-16, Math.Min(16, originalMipBias));
                wrapBox.SelectedIndex = Math.Max(0, Math.Min(3, originalWrap));
                mipMapsBox.Checked = originalMipMaps;
                mipCountBox.Value = Math.Max(1, Math.Min(32, originalMipCount));
                channelBox.SelectedIndex = UsesBgraByDefault(originalFormat) ? 1 : 0;
            };

            saveButton.Click += (_, _) =>
            {
                if (!(formatBox.SelectedItem is JuanchoTextureFormatItem selectedFormat))
                {
                    MessageBox.Show(this, "Choose a Texture2D format first.", "Juancho", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int width = (int)widthBox.Value;
                int height = (int)heightBox.Value;
                int maxMips = GetMaxMipCount(width, height);
                int mipCount = mipMapsBox.Checked ? Math.Min((int)mipCountBox.Value, maxMips) : 1;

                if (width < 1 || height < 1)
                {
                    MessageBox.Show(this, "Width and height must be at least 1.", "Juancho", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (selectedFormat.Format == ATTextureFormat.PVRTC_RGB2 ||
                    selectedFormat.Format == ATTextureFormat.PVRTC_RGBA2 ||
                    selectedFormat.Format == ATTextureFormat.PVRTC_RGB4 ||
                    selectedFormat.Format == ATTextureFormat.PVRTC_RGBA4)
                {
                    if ((width & 3) != 0 || (height & 3) != 0)
                    {
                        MessageBox.Show(this, "PVRTC textures normally require dimensions compatible with the selected PVRTC format. Resize the image to a multiple of 4 first.", "Juancho", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                Settings = new JuanchoTextureSettings
                {
                    Width = width,
                    Height = height,
                    Format = (int)selectedFormat.Format,
                    FilterMode = filterBox.SelectedIndex == 3 ? 1 : filterBox.SelectedIndex,
                    AnisoLevel = Math.Max(1, (int)anisoBox.Value),
                    MipMapBias = (float)mipBiasBox.Value,
                    WrapMode = wrapBox.SelectedIndex,
                    GenerateMipMaps = mipMapsBox.Checked,
                    MipCount = Math.Max(1, mipCount),
                    UseBgra = channelBox.SelectedIndex == 1
                };

                DialogResult = DialogResult.OK;
                Close();
            };

            cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            toolTip.SetToolTip(filterBox, "Anisotropic is a shortcut: Unity stores it through the filter mode plus anisotropic level.");
            toolTip.SetToolTip(channelBox, "For compressed formats, the encoder determines the final channel representation.");
            toolTip.SetToolTip(mipCountBox, "Higher mip counts require the native encoder included with this build.");
            UpdateMipMaximum();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (previewBox.Image != null)
            {
                previewBox.Image.Dispose();
                previewBox.Image = null;
            }
            toolTip?.Dispose();
            base.OnFormClosed(e);
        }

        private void SetDimensions(int width, int height)
        {
            changingDimensions = true;
            try
            {
                widthBox.Value = Math.Max(widthBox.Minimum, Math.Min(widthBox.Maximum, width));
                heightBox.Value = Math.Max(heightBox.Minimum, Math.Min(heightBox.Maximum, height));
            }
            finally
            {
                changingDimensions = false;
                UpdateMipMaximum();
            }
        }

        private void UpdateLinkedHeight()
        {
            if (changingDimensions || !keepAspectBox.Checked || sourceAspect <= 0) return;
            changingDimensions = true;
            try
            {
                int h = Math.Max(1, (int)Math.Round((double)widthBox.Value / sourceAspect));
                heightBox.Value = Math.Max(heightBox.Minimum, Math.Min(heightBox.Maximum, h));
            }
            finally
            {
                changingDimensions = false;
                UpdateMipMaximum();
            }
        }

        private void UpdateLinkedWidth()
        {
            if (changingDimensions || !keepAspectBox.Checked || sourceAspect <= 0) return;
            changingDimensions = true;
            try
            {
                int w = Math.Max(1, (int)Math.Round((double)heightBox.Value * sourceAspect));
                widthBox.Value = Math.Max(widthBox.Minimum, Math.Min(widthBox.Maximum, w));
            }
            finally
            {
                changingDimensions = false;
                UpdateMipMaximum();
            }
        }

        private void UpdateMipMaximum()
        {
            if (mipCountBox == null || widthBox == null || heightBox == null) return;
            int maxMipCount = GetMaxMipCount((int)widthBox.Value, (int)heightBox.Value);
            mipCountBox.Maximum = Math.Max(1, Math.Min(32, maxMipCount));
            if (mipCountBox.Value > mipCountBox.Maximum)
                mipCountBox.Value = mipCountBox.Maximum;
        }

        private static int GetMaxMipCount(int width, int height)
        {
            int maxDimension = Math.Max(1, Math.Max(width, height));
            int count = 1;
            while (maxDimension > 1)
            {
                maxDimension >>= 1;
                count++;
            }
            return count;
        }

        private void SelectFormat(int numericFormat)
        {
            for (int i = 0; i < formatBox.Items.Count; i++)
            {
                if (formatBox.Items[i] is JuanchoTextureFormatItem item && (int)item.Format == numericFormat)
                {
                    formatBox.SelectedIndex = i;
                    return;
                }
            }

            for (int i = 0; i < formatBox.Items.Count; i++)
            {
                if (formatBox.Items[i] is JuanchoTextureFormatItem item && item.Format == ATTextureFormat.RGBA32)
                {
                    formatBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private static bool UsesBgraByDefault(int format)
        {
            return format == (int)ATTextureFormat.BGRA32 || format == (int)ATTextureFormat.BGRA32Old;
        }

        private static void AddRow(TableLayoutPanel panel, string label, Control control)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            panel.Controls.Add(LabelFor(label), 0, row);
            panel.Controls.Add(control, 1, row);
        }

        private static void AddFullRow(TableLayoutPanel panel, Control control, int height)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            panel.Controls.Add(control, 0, row);
            panel.SetColumnSpan(control, 2);
        }

        private static Label LabelFor(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 8, 0, 0)
        };

        private static NumericUpDown CreateNumber(int min, int max, int value) => new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Max(min, Math.Min(max, value)),
            Width = 220,
            Dock = DockStyle.Left,
            ThousandsSeparator = true
        };

        private static ComboBox CreateCombo() => new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Dock = DockStyle.Left
        };

        private static Bitmap CreatePreviewBitmap(byte[] rgba, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] bgra = new byte[stride * height];
                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * width * 4;
                    int dstRow = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int src = srcRow + x * 4;
                        int dst = dstRow + x * 4;
                        bgra[dst] = rgba[src + 2];
                        bgra[dst + 1] = rgba[src + 1];
                        bgra[dst + 2] = rgba[src];
                        bgra[dst + 3] = rgba[src + 3];
                    }
                }
                Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }
    }
}
