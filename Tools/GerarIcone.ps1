# Gera o ícone AstraProtocolAssistant.ico (tela de CRT âmbar com uma nave) em vários tamanhos.
# Uso, a partir da pasta do projeto:
#   powershell -ExecutionPolicy Bypass -File Tools\GerarIcone.ps1
# Por padrão grava o .ico na raiz do projeto e uma prévia em PNG de 256 px na pasta Tools.
param(
    [string]$Saida = (Join-Path $PSScriptRoot '..\AstraProtocolAssistant.ico'),
    [string]$Previa = (Join-Path $PSScriptRoot 'icone-previa.png')
)

Add-Type -AssemblyName System.Drawing

# Cores padrão do terminal do jogo (manual: Foreground 255,176,0 / Background 5,3,0).
$ambar = [System.Drawing.Color]::FromArgb(255, 255, 176, 0)
$ambarFraco = [System.Drawing.Color]::FromArgb(70, 255, 176, 0)
$fundo = [System.Drawing.Color]::FromArgb(255, 5, 3, 0)

function Desenhar([int]$t) {
    $bmp = New-Object System.Drawing.Bitmap $t, $t, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Tela de CRT: quadrado arredondado escuro com moldura âmbar.
    $m = [Math]::Max(1, [int]($t * 0.04))
    $r = [Math]::Max(2, [int]($t * 0.18))
    $borda = [Math]::Max(1, [int]($t * 0.06))
    $ret = New-Object System.Drawing.RectangleF ($m + $borda / 2), ($m + $borda / 2), ($t - 2 * $m - $borda), ($t - 2 * $m - $borda)
    $caminho = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $caminho.AddArc($ret.X, $ret.Y, $d, $d, 180, 90)
    $caminho.AddArc($ret.Right - $d, $ret.Y, $d, $d, 270, 90)
    $caminho.AddArc($ret.Right - $d, $ret.Bottom - $d, $d, $d, 0, 90)
    $caminho.AddArc($ret.X, $ret.Bottom - $d, $d, $d, 90, 90)
    $caminho.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $fundo), $caminho)

    # Linhas de varredura (scanlines) nos tamanhos maiores.
    if ($t -ge 48) {
        $g.SetClip($caminho)
        $passo = [Math]::Max(3, [int]($t / 32))
        $caneta = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 255, 176, 0)), 1
        for ($y = 0; $y -lt $t; $y += $passo) { $g.DrawLine($caneta, 0, $y, $t, $y) }
        $g.ResetClip()
    }

    $g.DrawPath((New-Object System.Drawing.Pen $ambar, $borda), $caminho)

    # Nave em forma de ponta de seta, apontando para cima.
    $cx = $t / 2.0
    $nave = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $cx, ($t * 0.20)),
        (New-Object System.Drawing.PointF ($t * 0.74), ($t * 0.70)),
        (New-Object System.Drawing.PointF $cx, ($t * 0.58)),
        (New-Object System.Drawing.PointF ($t * 0.26), ($t * 0.70))
    )
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $ambar), $nave)

    # Cursor do prompt sob a nave, nos tamanhos onde fica legível.
    if ($t -ge 32) {
        $g.FillRectangle((New-Object System.Drawing.SolidBrush $ambarFraco), ($t * 0.40), ($t * 0.77), ($t * 0.20), [Math]::Max(1, $t * 0.05))
    }

    $g.Dispose()
    return $bmp
}

$tamanhos = 16, 24, 32, 48, 64, 128, 256
$imagens = foreach ($t in $tamanhos) {
    $bmp = Desenhar $t
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($t -eq 256) { $bmp.Save($Previa, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

# Arquivo .ico com uma entrada PNG por tamanho (suportado a partir do Windows Vista).
$fs = [System.IO.File]::Create($Saida)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$tamanhos.Count)
$offset = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]; $dados = $imagens[$i]
    $lado = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$dados.Length); $w.Write([UInt32]$offset)
    $offset += $dados.Length
}
foreach ($dados in $imagens) { $w.Write($dados) }
$w.Close()
