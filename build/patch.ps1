$ErrorActionPreference = 'Stop'

$repo = Join-Path $env:GITHUB_WORKSPACE 'AssetStudio'
$gui = Join-Path $repo 'AssetStudioGUI'
$csproj = Join-Path $gui 'AssetStudioGUI.csproj'
$form = Join-Path $gui 'AssetStudioGUIForm.cs'
$designer = Join-Path $gui 'AssetStudioGUIForm.Designer.cs'
$helperSource = Join-Path $env:GITHUB_WORKSPACE 'build\patch\JuanchoTextureReplacer.cs'
$helperDest = Join-Path $gui 'JuanchoTextureReplacer.cs'

Copy-Item $helperSource $helperDest -Force

$projText = Get-Content $csproj -Raw
$projText = $projText.Replace('<TargetFrameworks>net472;net5.0-windows;net6.0-windows</TargetFrameworks>', '<TargetFrameworks>net8.0-windows</TargetFrameworks>')
if ($projText -notmatch 'AssetsTools\.NET"') {
    $tab = [char]9
    $marker = $tab + $tab + '<PackageReference Include="Newtonsoft.Json" Version="13.0.1" />'
    if (-not $projText.Contains($marker)) { throw "Could not find Newtonsoft.Json PackageReference marker." }
    $projText = $projText.Replace(
        $marker,
        $marker + [Environment]::NewLine + $tab + $tab + '<PackageReference Include="AssetsTools.NET" Version="3.0.5" />' + [Environment]::NewLine + $tab + $tab + '<PackageReference Include="AssetsTools.NET.Texture" Version="3.0.2" />'
    )
    Set-Content $csproj $projText -Encoding UTF8
}

$designerText = Get-Content $designer -Raw
if ($designerText -notmatch 'juanchoToolStripMenuItem') {
    $designerText = $designerText.Replace(
        "            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();",
        "            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();" + [Environment]::NewLine + "            this.juanchoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();" + [Environment]::NewLine + "            this.replaceSelectedTextureToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();"
    )
    $designerText = $designerText.Replace(
        "            this.fileToolStripMenuItem," + [Environment]::NewLine + "            this.optionsToolStripMenuItem,",
        "            this.fileToolStripMenuItem," + [Environment]::NewLine + "            this.juanchoToolStripMenuItem," + [Environment]::NewLine + "            this.optionsToolStripMenuItem,"
    )

    $oldOptions = "            // " + [Environment]::NewLine + "            // optionsToolStripMenuItem"
    $juanchoBlock = "            // " + [Environment]::NewLine +
"            // juanchoToolStripMenuItem" + [Environment]::NewLine +
"            // " + [Environment]::NewLine +
"            this.juanchoToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {" + [Environment]::NewLine +
"            this.replaceSelectedTextureToolStripMenuItem});" + [Environment]::NewLine +
"            this.juanchoToolStripMenuItem.Name = ""juanchoToolStripMenuItem"";" + [Environment]::NewLine +
"            this.juanchoToolStripMenuItem.Size = new System.Drawing.Size(65, 21);" + [Environment]::NewLine +
"            this.juanchoToolStripMenuItem.Text = ""Juancho"";" + [Environment]::NewLine +
"            // " + [Environment]::NewLine +
"            // replaceSelectedTextureToolStripMenuItem" + [Environment]::NewLine +
"            // " + [Environment]::NewLine +
"            this.replaceSelectedTextureToolStripMenuItem.Name = ""replaceSelectedTextureToolStripMenuItem"";" + [Environment]::NewLine +
"            this.replaceSelectedTextureToolStripMenuItem.Size = new System.Drawing.Size(210, 22);" + [Environment]::NewLine +
"            this.replaceSelectedTextureToolStripMenuItem.Text = ""Replace selected texture"";" + [Environment]::NewLine +
"            this.replaceSelectedTextureToolStripMenuItem.Click += new System.EventHandler(this.replaceSelectedTextureToolStripMenuItem_Click);" + [Environment]::NewLine +
"            // " + [Environment]::NewLine +
"            // optionsToolStripMenuItem"
    $designerText = $designerText.Replace($oldOptions, $juanchoBlock)

    $fieldOld = "        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;" + [Environment]::NewLine + "        private System.Windows.Forms.SplitContainer splitContainer1;"
    $fieldNew = "        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;" + [Environment]::NewLine + "        private System.Windows.Forms.ToolStripMenuItem juanchoToolStripMenuItem;" + [Environment]::NewLine + "        private System.Windows.Forms.ToolStripMenuItem replaceSelectedTextureToolStripMenuItem;" + [Environment]::NewLine + "        private System.Windows.Forms.SplitContainer splitContainer1;"
    $designerText = $designerText.Replace($fieldOld, $fieldNew)

    if ($designerText -notmatch [regex]::Escape('juanchoToolStripMenuItem')) { throw "Failed to patch Designer.cs" }
    Set-Content $designer $designerText -Encoding UTF8
}

$formText = Get-Content $form -Raw
if ($formText -notmatch 'replaceSelectedTextureToolStripMenuItem_Click') {
    $nl = [Environment]::NewLine
    $handler =
"        private async void replaceSelectedTextureToolStripMenuItem_Click(object sender, EventArgs e)" + $nl +
"        {" + $nl +
"            var selectedAssets = GetSelectedAssets();" + $nl +
"            if (selectedAssets.Count != 1 || selectedAssets[0].Type != ClassIDType.Texture2D)" + $nl +
"            {" + $nl +
"                MessageBox.Show(this, ""Select exactly one Texture2D in the asset list first."", ""Juancho"", MessageBoxButtons.OK, MessageBoxIcon.Information);" + $nl +
"                return;" + $nl +
"            }" + $nl + $nl +
"            AssetItem selectedAsset = selectedAssets[0];" + $nl + $nl +
"            using (var imageDialog = new OpenFileDialog())" + $nl +
"            {" + $nl +
"                imageDialog.Title = ""Choose replacement texture"";" + $nl +
"                imageDialog.Filter = ""Image files|*.png;*.jpg;*.jpeg;*.bmp;*.tga|PNG|*.png|JPEG|*.jpg;*.jpeg|Bitmap|*.bmp|TGA|*.tga|All files|*.*"";" + $nl +
"                imageDialog.RestoreDirectory = true;" + $nl +
"                if (imageDialog.ShowDialog(this) != DialogResult.OK) return;" + $nl + $nl +
"                string sourcePath = string.IsNullOrWhiteSpace(selectedAsset.SourceFile.originalPath) ? selectedAsset.SourceFile.fullName : selectedAsset.SourceFile.originalPath;" + $nl +
"                string extension = Path.GetExtension(sourcePath);" + $nl +
"                if (string.IsNullOrEmpty(extension)) extension = "".unity3d"";" + $nl + $nl +
"                using (var saveDialog = new SaveFileDialog())" + $nl +
"                {" + $nl +
"                    saveDialog.Title = ""Save modified Unity file"";" + $nl +
"                    saveDialog.Filter = extension.TrimStart('.').ToUpperInvariant() + "" file|*"" + extension + ""|All files|*.*"";" + $nl +
"                    saveDialog.FileName = Path.GetFileNameWithoutExtension(sourcePath) + ""_juancho"" + extension;" + $nl +
"                    saveDialog.InitialDirectory = Path.GetDirectoryName(sourcePath);" + $nl +
"                    saveDialog.OverwritePrompt = true;" + $nl +
"                    if (saveDialog.ShowDialog(this) != DialogResult.OK) return;" + $nl + $nl +
"                    replaceSelectedTextureToolStripMenuItem.Enabled = false;" + $nl +
"                    StatusStripUpdate(""Juancho: replacing selected Texture2D..."");" + $nl +
"                    try" + $nl +
"                    {" + $nl +
"                        await Task.Run(() => JuanchoTextureReplacer.ReplaceTexture(selectedAsset, imageDialog.FileName, saveDialog.FileName));" + $nl +
"                        StatusStripUpdate(""Juancho: Texture2D replacement finished."");" + $nl +
"                        MessageBox.Show(this, ""Texture2D replaced successfully."" + Environment.NewLine + Environment.NewLine + ""Saved file:"" + Environment.NewLine + saveDialog.FileName, ""Juancho"", MessageBoxButtons.OK, MessageBoxIcon.Information);" + $nl +
"                    }" + $nl +
"                    catch (Exception ex)" + $nl +
"                    {" + $nl +
"                        StatusStripUpdate(""Juancho: Texture2D replacement failed."");" + $nl +
"                        MessageBox.Show(this, ""Texture2D replacement failed."" + Environment.NewLine + Environment.NewLine + ex.Message, ""Juancho"", MessageBoxButtons.OK, MessageBoxIcon.Error);" + $nl +
"                    }" + $nl +
"                    finally" + $nl +
"                    {" + $nl +
"                        replaceSelectedTextureToolStripMenuItem.Enabled = true;" + $nl +
"                    }" + $nl +
"                }" + $nl +
"            }" + $nl +
"        }" + $nl + $nl

    $marker = "        private void showExpOpt_Click(object sender, EventArgs e)"
    if (-not $formText.Contains($marker)) { throw "Could not find form insertion marker." }
    $formText = $formText.Replace($marker, $handler + $marker)
    Set-Content $form $formText -Encoding UTF8
}
