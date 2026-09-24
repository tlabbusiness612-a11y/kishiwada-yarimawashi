param([int]$Port = 8765, [string]$Root = (Split-Path $PSScriptRoot -Parent))
$types = @{ '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'; '.css' = 'text/css'; '.png' = 'image/png'; '.json' = 'application/json' }
$l = New-Object System.Net.HttpListener
$l.Prefixes.Add("http://localhost:$Port/")
$l.Start()
Write-Host "Serving $Root on http://localhost:$Port/"
while ($l.IsListening) {
  $ctx = $l.GetContext()
  $path = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
  if ($path -eq '') { $path = 'index.html' }
  $file = Join-Path $Root $path
  if (Test-Path $file -PathType Leaf) {
    $bytes = [IO.File]::ReadAllBytes($file)
    $ext = [IO.Path]::GetExtension($file)
    if ($ext -eq '.html' -and -not ([Text.Encoding]::UTF8.GetString($bytes, 0, [Math]::Min(20, $bytes.Length)) -match '(?i)<!doctype')) {
      $body = [Text.Encoding]::UTF8.GetString($bytes)
      $body = "<!doctype html><html lang=`"ja`"><head><meta charset=`"utf-8`"><meta name=`"viewport`" content=`"width=device-width,initial-scale=1,viewport-fit=cover`"></head><body>" + $body + "</body></html>"
      $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    }
    $ctx.Response.ContentType = $types[$ext]
    $ctx.Response.Headers.Add('Cache-Control', 'no-store')
    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
  } else { $ctx.Response.StatusCode = 404 }
  $ctx.Response.Close()
}

