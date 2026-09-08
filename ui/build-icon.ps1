param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$reader=[Xml.XmlReader]::Create((Join-Path $PSScriptRoot 'Icon.xaml'))
try {$drawing=[Windows.Markup.XamlReader]::Load($reader)} finally {$reader.Dispose()}
$sizes=@(16,24,32,48,64,128,256)
$frames=@(foreach($size in $sizes){
  $visual=New-Object Windows.Media.DrawingVisual
  $context=$visual.RenderOpen()
  $context.DrawImage($drawing,(New-Object Windows.Rect 0,0,$size,$size))
  $context.Close()
  $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([Windows.Media.PixelFormats]::Pbgra32)
  $bitmap.Render($visual)
  $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
  $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $memory=New-Object IO.MemoryStream
  $encoder.Save($memory)
  ,$memory.ToArray()
  $memory.Dispose()
})
$file=[IO.File]::Create([IO.Path]::GetFullPath($Output))
$writer=New-Object IO.BinaryWriter $file
try {
  $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
  $offset=6+16*$sizes.Count
  for($i=0;$i -lt $sizes.Count;$i++){
    $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
    $offset+=$frames[$i].Length
  }
  foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally {$writer.Dispose();$file.Dispose()}
