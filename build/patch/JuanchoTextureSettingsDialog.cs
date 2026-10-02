using AssetStudio;
using AssetsTools.NET.Texture;
using StbImageSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
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

        public JuanchoTextureSettings Settings { get; }

        public JuanchoTextureSettingsDialog(string textureName, string imagePath, Texture2D sourceTexture)
        {
            Text = "Juancho - Texture2D Replacement";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 560;
            Height = 560;
            Padding = new Padding(12);

            int imageWidth;
            int imageHeight;
            using (var stream = File.OpenRead(imagePath))
            {
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                imageWidth = image.Width;
                imageHeight = image.Height;
            }

            int originalWidth = sourceTexture?.m_Width ?? imageWidth;
            int originalHeight = sourceTexture?.m_Height ?? imageHeight;
            int originalFormat = sourceTexture == null ? (int)TextureFormat.RGBA32 : (int)sourceTexture.m_TextureFormat;
            int originalFilter = sourceTexture?.m_TextureSettings?.m_FilterMode ?? 1;
            int originalAniso = sourceTexture?.m_TextureSettings?.m_Aniso ?? 1;
            float originalMipBias = sourceTexture?.m_TextureSettings?.m_MipBias ?? 0f;
            int originalWrap = sourceTexture?.m_TextureSettings?.m_WrapMode ?? 0;
            bool originalMipMaps = sourceTexture?.m_MipMap ?? false;
            int originalMipCount = sourceTexture?.m_MipCount > 0 ? sourceTexture.m_MipCount : 1;

            if (originalWidth <= 0) originalWidth = imageWidth;
            if (originalHeight <= 0) originalHeight = imageHeight;
            if (originalAniso < 1) originalAniso = 1;
            if (originalMipCount < 1) originalMipCount = 1;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 12,
                AutoSize = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 10; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = "Texture2D: " + textureName + Environment.NewLine +
                       $"Original: {originalWidth} × {originalHeight}    Replacement: {imageWidth} × {imageHeight}",
                Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 6)
            };
            root.Controls.Add(title, 0, 0);
            root.SetColumnSpan(title, 2);

            widthBox = CreateNumber(1, 32768, imageWidth);
            heightBox = CreateNumber(1, 32768, imageHeight);
            root.Controls.Add(LabelFor("Width"), 0, 1);
            root.Controls.Add(widthBox, 1, 1);
            root.Controls.Add(LabelFor("Height"), 0, 2);
            root.Controls.Add(heightBox, 1, 2);

            formatBox = CreateCombo();
            foreach (var format in Enum.GetValues<TextureFormat>().Distinct())
                formatBox.Items.Add(format);
            var selectedFormat = Enum.GetValues<TextureFormat>().FirstOrDefault(f => (int)f == originalFormat);
            formatBox.SelectedItem = selectedFormat == default && originalFormat != 0
                ? TextureFormat.RGBA32
                : selectedFormat;
            root.Controls.Add(LabelFor("Format"), 0, 3);
            root.Controls.Add(formatBox, 1, 3);

            filterBox = CreateCombo();
            filterBox.Items.Add("Point");
            filterBox.Items.Add("Bilinear");
            filterBox.Items.Add("Trilinear");
            filterBox.Items.Add("Anisotropic");
            int filterIndex = originalFilter switch
            {
                0 => 0,
                2 => 2,
                _ => originalAniso > 1 ? 3 : 1
            };
            filterBox.SelectedIndex = filterIndex;
            root.Controls.Add(LabelFor("Filter Mode"), 0, 4);
            root.Controls.Add(filterBox, 1, 4);

            anisoBox = CreateNumber(1, 16, originalAniso);
            root.Controls.Add(LabelFor("Anisotropic Level"), 0, 5);
            root.Controls.Add(anisoBox, 1, 5);
            filterBox.SelectedIndexChanged += (_, _) => anisoBox.Enabled = filterBox.SelectedIndex == 3 || anisoBox.Enabled;

            mipBiasBox = new NumericUpDown
            {
                Dock = DockStyle.Left,
                Width = 220,
                Minimum = -16,
                Maximum = 16,
                DecimalPlaces = 3,
                Increment = 0.05m,
                Value = (decimal)Math.Max(-16, Math.Min(16, originalMipBias))
            };
            root.Controls.Add(LabelFor("Mip Map Bias"), 0, 6);
            root.Controls.Add(mipBiasBox, 1, 6);

            wrapBox = CreateCombo();
            wrapBox.Items.Add("Repeat");
            wrapBox.Items.Add("Clamp");
            wrapBox.Items.Add("Mirror");
            wrapBox.Items.Add("Mirror Once");
            wrapBox.SelectedIndex = Math.Max(0, Math.Min(3, originalWrap));
            root.Controls.Add(LabelFor("Wrap Mode"), 0, 7);
            root.Controls.Add(wrapBox, 1, 7);

            mipMapsBox = new CheckBox
            {
                Text = "Generate Mip Maps",
                AutoSize = true,
                Checked = originalMipMaps
            };
            root.Controls.Add(LabelFor("Mip Maps"), 0, 8);
            root.Controls.Add(mipMapsBox, 1, 8);

            mipCountBox = CreateNumber(1, 16, Math.Max(1, Math.Min(16, originalMipCount)));
            mipCountBox.Enabled = originalMipMaps;
            mipMapsBox.CheckedChanged += (_, _) => mipCountBox.Enabled = mipMapsBox.Checked;
            root.Controls.Add(LabelFor("Mip Count"), 0, 9);
            root.Controls.Add(mipCountBox, 1, 9);

            channelBox = CreateCombo();
            channelBox.Items.Add("RGBA");
            channelBox.Items.Add("BGRA");
            channelBox.SelectedIndex = 1;
            root.Controls.Add(LabelFor("Channel"), 0, 10);
            root.Controls.Add(channelBox, 1, 10);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            var saveButton = new Button { Text = "Save", Width = 110, Height = 30 };
            var cancelButton = new Button { Text = "Cancel", Width = 110, Height = 30 };
            buttons.Controls.Add(saveButton);
            buttons.Controls.Add(cancelButton);
            root.Controls.Add(buttons, 0, 11);
            root.SetColumnSpan(buttons, 2);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

            saveButton.Click += (_, _) =>
            {
                var selected = (TextureFormat)formatBox.SelectedItem;
                int width = (int)widthBox.Value;
                int height = (int)heightBox.Value;
                int mipCount = mipMapsBox.Checked ? Math.Max(1, (int)mipCountBox.Value) : 1;

                Settings = new JuanchoTextureSettings
                {
                    Width = width,
                    Height = height,
                    Format = (int)selected,
                    FilterMode = filterBox.SelectedIndex == 3 ? 1 : filterBox.SelectedIndex,
                    AnisoLevel = Math.Max(1, (int)anisoBox.Value),
                    MipMapBias = (float)mipBiasBox.Value,
                    WrapMode = wrapBox.SelectedIndex,
                    GenerateMipMaps = mipMapsBox.Checked,
                    MipCount = mipCount,
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
            Controls.Add(root);
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
            Dock = DockStyle.Left
        };

        private static ComboBox CreateCombo() => new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Dock = DockStyle.Left
        };
    }
}
