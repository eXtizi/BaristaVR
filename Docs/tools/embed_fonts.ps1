# Re-saves the deck through PowerPoint with TrueType fonts embedded (Google Sans must be installed).
param([string]$deck = (Join-Path (Split-Path $PSScriptRoot) "XR-Continuum-Viva-Presentation.pptx"))
$deck = (Resolve-Path $deck).Path
$tmp = [IO.Path]::ChangeExtension($deck, ".embedding.pptx")
$pp = New-Object -ComObject PowerPoint.Application
$pres = $pp.Presentations.Open($deck, $false, $false, $false)
$pres.SaveAs($tmp, 24, -1)   # ppSaveAsOpenXMLPresentation, EmbedTrueTypeFonts = msoTrue
$pres.Close()
$pp.Quit()
Move-Item $tmp $deck -Force
Write-Output "embedded fonts into $deck"
